using WuxiaWorld.Domain.Characters;
using WuxiaWorld.Domain.Combat;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Domain.World;

namespace WuxiaWorld.Application.World;

/// <summary>一次已提交事务的结果：给界面的提示、需要开打的战斗与剧情换图请求。</summary>
public sealed record CommitResult(bool Ok, string? Error, IReadOnlyList<WorldNotice> Notices, PendingBattle? Battle, TravelRequest? Travel)
{
    public static CommitResult Reject(string error) => new(false, error, [], null, null);

    internal static CommitResult From(EffectResult r) => new(r.Ok, r.Error, r.Notices, r.Battle, r.Travel);
}

/// <summary>进行中的对话：在世界状态副本上运行，结束后整体提交；中途取消则什么都不改变。</summary>
public sealed class DialogueSession
{
    internal DialogueSession(DialogueRunner runner, long originRevision, string? eventId)
    {
        Runner = runner;
        OriginRevision = originRevision;
        EventId = eventId;
    }

    public DialogueRunner Runner { get; }
    public string? EventId { get; }
    internal long OriginRevision { get; }
}

/// <summary>
/// 大地图上一处地标的状态。<see cref="Route"/> 为从所在之处去那里、条件已满足的路线；
/// 不能前往时 <see cref="LockedHint"/> 为写明开放条件的文本键（可能为 null，表示无路可通且未写条件）。
/// </summary>
public sealed record WorldNodeStatus(WorldNodeDefinition Node, bool Here, bool Visited, RouteDefinition? Route, string? LockedHint)
{
    public bool Open => Here || Route is not null;
}

public enum TransitionKind
{
    /// <summary>大地图路线旅行（扣费、推进时辰、抽途中事件）。</summary>
    Route,

    /// <summary>小地图出口。</summary>
    Exit,

    /// <summary>剧情效果请求的换图。</summary>
    Story,
}

/// <summary>
/// 场景切换事务（架构文档 6.3）：在候选状态里扣费、推进时间、确定途中事件；
/// 表现层加载目的地成功后提交，失败则中止——不扣费、不推进时间、可再次出发。
/// </summary>
public sealed class Transition
{
    internal Transition(TransitionKind kind, WorldState candidate, long originRevision, string? routeId, TravelMode? mode)
    {
        Kind = kind;
        Candidate = candidate;
        OriginRevision = originRevision;
        RouteId = routeId;
        Mode = mode;
    }

    public TransitionKind Kind { get; }
    public string? RouteId { get; }
    public TravelMode? Mode { get; }
    public string MapId => Candidate.MapId;
    public string SpawnId => Candidate.SpawnId;

    /// <summary>途中抽中的事件（到达后自动开始）。</summary>
    public string? RolledEvent => Candidate.QueuedEvent;

    internal WorldState Candidate { get; }
    internal long OriginRevision { get; }
}

public enum BattleEnd
{
    Victory,
    Defeat,
    Retreated,
}

/// <summary>
/// 一局游戏的应用层会话（架构文档 5.2 Session）：持有已提交的世界状态，
/// 把对话、交互、旅行与战斗结算作为事务执行。界面只读 <see cref="World"/>，所有改动经由这里。
/// </summary>
public sealed class GameSession
{
    private DialogueSession? _dialogue;
    private Transition? _transition;

    /// <param name="growth">成长与装配规则（需要战斗内容）；为 null 时养成类操作被拒绝。</param>
    public GameSession(WorldRules rules, WorldState world, GrowthRules? growth = null)
    {
        Rules = rules;
        World = world;
        Growth = growth;
    }

    public static GameSession NewGame(WorldRules rules, GrowthRules? growth = null) => new(rules, rules.NewGame(), growth);

    public WorldRules Rules { get; }

    public GrowthRules? Growth { get; }

    /// <summary>最近一次提交的世界状态。不要直接修改。</summary>
    public WorldState World { get; private set; }

    public DialogueSession? ActiveDialogue => _dialogue;
    public Transition? ActiveTransition => _transition;

    /// <summary>对话、切换或待开战斗进行中时不开放手动保存（架构文档 3）。</summary>
    public bool CanSave => _dialogue is null && _transition is null && World.Battle is null;

    public IReadOnlyList<StoryEventDefinition> Events => Rules.EventsAt(World);

    public IReadOnlyList<MapInteractable> Interactables => Rules.InteractablesAt(World);

    /// <summary>进图后应自动开始的事件：先途中事件，再本图的过场事件。</summary>
    public StoryEventDefinition? AutoEvent
    {
        get
        {
            if (World.QueuedEvent is { } queued && Rules.Content.Events.TryGetValue(queued, out var q) && Rules.IsEventOpen(World, q))
            {
                return q;
            }

            return Events.FirstOrDefault(e => e.Auto);
        }
    }

    // ── 对话与交互 ──────────────────────────────────────────

    public DialogueSession StartEvent(string eventId)
    {
        EnsureExploring();
        if (!Rules.Content.Events.TryGetValue(eventId, out var e))
        {
            throw new InvalidOperationException($"未定义的事件 {eventId}");
        }

        var queued = World.QueuedEvent == eventId;
        if (!Rules.IsEventOpen(World, e) || (!queued && e.Map != World.MapId))
        {
            throw new InvalidOperationException($"事件 {eventId} 当前不可开始");
        }

        var candidate = World.Clone();
        if (WorldRules.Reserve(candidate, e) is { } by)
        {
            throw new InvalidOperationException($"事件 {eventId} 的人物正被 {by} 占用");
        }

        return Begin(candidate, e.Dialogue, e.Id);
    }

    /// <summary>与交互物互动：先结算交互效果，再进入其对话（若有）。没有对话的交互立即提交并返回 null。</summary>
    public DialogueSession? Interact(string interactableId, out CommitResult result)
    {
        EnsureExploring();
        var item = Interactables.FirstOrDefault(i => i.Id == interactableId)
            ?? throw new InvalidOperationException($"交互物 {interactableId} 当前不可用");
        var candidate = World.Clone();
        var r = new EffectResult();
        var delta = WorldRules.DeltaKey(World.MapId, item.Id);
        if (item.Once)
        {
            candidate.MapDeltas.Add(delta);
        }

        if (item.Effects.Count > 0)
        {
            Rules.Apply(candidate, item.Effects, r, item.Once ? "interact:" + delta : null);
        }

        if (!r.Ok)
        {
            result = CommitResult.From(r);
            return null;
        }

        if (item.Dialogue is { } dialogue)
        {
            result = CommitResult.From(r);
            return Begin(candidate, dialogue, null, r);
        }

        result = Commit(candidate, r);
        return null;
    }

    public DialogueSession StartDialogue(string dialogueId)
    {
        EnsureExploring();
        return Begin(World.Clone(), dialogueId, null);
    }

    private DialogueSession Begin(WorldState candidate, string dialogueId, string? eventId, EffectResult? carried = null)
    {
        var def = Rules.Content.Dialogues.TryGetValue(dialogueId, out var d) ? d : throw new InvalidOperationException($"未定义的对白 {dialogueId}");
        _dialogue = new DialogueSession(new DialogueRunner(Rules, def, candidate, carried ?? new EffectResult()), World.Revision, eventId);
        return _dialogue;
    }

    /// <summary>对话走到结束后提交：释放事件人物、记为已结算，并返回战斗或换图请求。</summary>
    public CommitResult FinishDialogue(DialogueSession session)
    {
        if (session != _dialogue)
        {
            return CommitResult.Reject("对话已提交或已取消");
        }

        var runner = session.Runner;
        if (!runner.Ended)
        {
            return CommitResult.Reject("对话尚未结束");
        }

        _dialogue = null;
        if (!runner.Result.Ok)
        {
            return CommitResult.From(runner.Result);
        }

        if (session.OriginRevision != World.Revision)
        {
            return CommitResult.Reject("世界状态已变化，对话结果作废");
        }

        var candidate = runner.State;
        if (session.EventId is { } eventId)
        {
            WorldRules.Release(candidate, eventId);
            candidate.Settled.Add(WorldRules.EventKey(eventId));
            if (candidate.QueuedEvent == eventId)
            {
                candidate.QueuedEvent = null;
            }
        }

        return Commit(candidate, runner.Result);
    }

    /// <summary>放弃对话（退回主菜单等）：丢弃副本，世界不变。</summary>
    public void CancelDialogue(DialogueSession session)
    {
        if (session == _dialogue)
        {
            _dialogue = null;
        }
    }

    // ── 场景切换 ──────────────────────────────────────────

    /// <summary>可用的路线（从当前所在之处出发且条件满足）：起点与当前地图同属一处大地图地标即可，不必站在码头。</summary>
    public IReadOnlyList<RouteDefinition> Routes =>
        [.. Rules.Content.Routes.Values.Where(r => Rules.Content.SamePlace(r.From, World.MapId) && Rules.Check(r.When, World))];

    /// <summary>
    /// 大地图上此刻可见的地标及其状态（架构文档 6.5）：所在之处、是否到过、可走的路线；不能前往时给出开放条件文本键——
    /// 有路线但条件未满足用路线的提示，没有路线用地标自己的提示。所在之处总是可见。
    /// </summary>
    public IReadOnlyList<WorldNodeStatus> WorldMapNodes
    {
        get
        {
            var content = Rules.Content;
            var here = content.NodeOf(World.MapId);
            var list = new List<WorldNodeStatus>();
            foreach (var node in content.WorldMap.Nodes)
            {
                var isHere = node.Id == here?.Id;
                if (!isHere && !Rules.Check(node.When, World))
                {
                    continue;
                }

                var visited = isHere || node.Maps.Any(World.Visited.Contains);
                List<RouteDefinition> routes = isHere ? [] : content.Routes.Values
                    .Where(r => content.SamePlace(r.From, World.MapId) && node.Maps.Contains(r.To, StringComparer.Ordinal)).ToList();
                var open = routes.FirstOrDefault(r => Rules.Check(r.When, World));
                var hint = open is not null || isHere ? null : routes.Select(r => r.LockedHint).FirstOrDefault(h => h is not null) ?? node.LockedHint;
                list.Add(new WorldNodeStatus(node, isHere, visited, open, hint));
            }

            return list;
        }
    }

    /// <summary>一处地标里此刻可开始的地区事件（各小地图合在一起，主线在前）。</summary>
    public IReadOnlyList<StoryEventDefinition> EventsIn(WorldNodeDefinition node) =>
        [.. node.Maps.SelectMany(m => Rules.EventsAt(World, m)).OrderBy(e => e.Priority).ThenBy(e => e.Id, StringComparer.Ordinal)];

    public Transition BeginRoute(string routeId, TravelMode mode)
    {
        EnsureExploring();
        var route = Rules.Content.Routes.TryGetValue(routeId, out var rd) ? rd : throw new InvalidOperationException($"未定义的路线 {routeId}");
        if (!Rules.Content.SamePlace(route.From, World.MapId) || !Rules.Check(route.When, World))
        {
            throw new InvalidOperationException($"路线 {routeId} 当前不可走");
        }

        var m = route.Modes.FirstOrDefault(x => x.Mode == mode && Rules.Check(x.When, World))
            ?? throw new InvalidOperationException($"路线 {routeId} 不能用 {mode}");
        if (World.Silver < m.Silver)
        {
            throw new InvalidOperationException($"银两不足：需要 {m.Silver}，现有 {World.Silver}");
        }

        var c = World.Clone();
        c.Silver -= m.Silver;
        c.Clock += m.Ticks;
        c.MapId = route.To;
        c.SpawnId = route.Spawn;
        var rng = c.GetRng();
        foreach (var enc in route.Encounters)
        {
            if (Rules.Content.Events.TryGetValue(enc.Event, out var ev) && Rules.IsEventOpen(c, ev) && rng.RollBp(enc.ChanceBp))
            {
                c.QueuedEvent = enc.Event;
                break;
            }
        }

        c.SetRng(rng);
        _transition = new Transition(TransitionKind.Route, c, World.Revision, routeId, mode);
        return _transition;
    }

    public Transition BeginExit(string exitId)
    {
        EnsureExploring();
        var map = Rules.Content.Maps[World.MapId];
        var exit = map.Exits.FirstOrDefault(x => x.Id == exitId && Rules.Check(x.When, World))
            ?? throw new InvalidOperationException($"出口 {exitId} 当前不可用");
        var c = World.Clone();
        c.MapId = exit.To;
        c.SpawnId = exit.Spawn;
        _transition = new Transition(TransitionKind.Exit, c, World.Revision, null, null);
        return _transition;
    }

    public Transition BeginStoryTravel(TravelRequest request)
    {
        EnsureIdle();
        var c = World.Clone();
        c.MapId = request.MapId;
        c.SpawnId = request.SpawnId ?? Rules.Content.Maps[request.MapId].SafeSpawn;
        _transition = new Transition(TransitionKind.Story, c, World.Revision, null, null);
        return _transition;
    }

    /// <summary>目的地加载完成后提交。重复提交或过期票据被拒绝，不会重复扣费。</summary>
    public CommitResult CommitTransition(Transition t)
    {
        if (t != _transition)
        {
            return CommitResult.Reject("切换已提交或已中止");
        }

        _transition = null;
        if (t.OriginRevision != World.Revision)
        {
            return CommitResult.Reject("世界状态已变化，切换作废");
        }

        return Commit(t.Candidate, new EffectResult());
    }

    /// <summary>加载失败或玩家取消：丢弃候选状态，回到出发前。</summary>
    public void AbortTransition(Transition t)
    {
        if (t == _transition)
        {
            _transition = null;
        }
    }

    /// <summary>
    /// 校正出生点：地图缺失回到新游戏起点，落点缺失回到该图安全入口（架构文档 6.4）。
    /// 返回需要记录的错误说明；无误时为 null。不改已提交状态以外的东西。
    /// </summary>
    public string? RepairSpawn()
    {
        if (!Rules.Content.Maps.TryGetValue(World.MapId, out var map))
        {
            var ng = Rules.Content.NewGame;
            var error = $"地图 {World.MapId} 不存在，回到 {ng.Map}";
            var c = World.Clone();
            c.MapId = ng.Map;
            c.SpawnId = ng.Spawn;
            Commit(c, new EffectResult());
            return error;
        }

        if (!map.Spawns.Contains(World.SpawnId, StringComparer.Ordinal))
        {
            var error = $"落点 {World.SpawnId} 不在 {map.Id}，回到安全入口 {map.SafeSpawn}";
            var c = World.Clone();
            c.SpawnId = map.SafeSpawn;
            Commit(c, new EffectResult());
            return error;
        }

        return null;
    }

    /// <summary>旧档补齐成长数据（见 <see cref="WorldRules.UpgradeLegacy"/>）；无需补齐时返回 null。读档后调用一次。</summary>
    public string? UpgradeLegacy()
    {
        var c = World.Clone();
        if (Rules.UpgradeLegacy(c) is not { } note)
        {
            return null;
        }

        Commit(c, new EffectResult());
        return note;
    }

    // ── 养成、行囊与店铺 ──────────────────────────────────

    /// <summary>
    /// 分配潜能、换装备、改装配、修炼与买卖只在稳定点进行：对话、换图或待开战斗期间不可（同存档的条件），
    /// 免得战斗中改了装配却不影响已开打的这一场、让玩家误会。
    /// </summary>
    public bool CanManage => _dialogue is null && _transition is null && World.Battle is null;

    public CommitResult Allocate(string who, Attributes add) => Manage(g => g.Allocate(Candidate, who, add));

    public CommitResult Equip(string who, string itemId) => Manage(g => g.Equip(Candidate, who, itemId));

    public CommitResult Unequip(string who, EquipSlot slot) => Manage(g => g.Unequip(Candidate, who, slot));

    public CommitResult SetSkills(string who, IReadOnlyList<string> skills) => Manage(g => g.SetSkills(Candidate, who, skills));

    public CommitResult SetArt(string who, ArtSlot slot, string? artId) => Manage(g => g.SetArt(Candidate, who, slot, artId));

    public CommitResult ToggleTalent(string who, string talentId) => Manage(g => g.ToggleTalent(Candidate, who, talentId));

    public CommitResult Cultivate(string who, string skillId) =>
        Manage(g => g.Cultivate(Candidate, who, skillId), new WorldNotice("mastery", skillId));

    /// <summary>
    /// 在城镇洗点：潜能，或招式熟练度（给了招式只退这一门），花银两；通知带收回的点数或返还的修为，
    /// ID 为人物（潜能、全部熟练度）或招式。
    /// </summary>
    public CommitResult Respec(string who, RespecKind kind, string? skillId = null)
    {
        var refund = Growth?.RespecRefund(World, who, kind, skillId) ?? 0;
        var notice = kind == RespecKind.Potential
            ? new WorldNotice("respec.potential", who, refund)
            : new WorldNotice("respec.mastery", skillId ?? who, refund);
        return Manage(g => g.Respec(Candidate, who, kind, skillId), notice);
    }

    /// <summary>调换阵位：把在队人物移到某格（0–2 前排、3–5 后排），该格有人则互换（见 <see cref="PartyRules"/>）。</summary>
    public CommitResult SetFormation(string who, int cell) => Manage(_ => PartyRules.SetCell(Candidate, who, cell));

    public CommitResult Buy(string shopId, string itemId, int count = 1) => Trade(r => Rules.Buy(Candidate, shopId, itemId, count, r));

    public CommitResult Sell(string shopId, string itemId, int count = 1) => Trade(r => Rules.Sell(Candidate, shopId, itemId, count, r));

    // 养成操作在这份副本上执行；成功才提交。
    private WorldState Candidate { get; set; } = null!;

    private CommitResult Manage(Func<GrowthRules, string?> change, WorldNotice? notice = null)
    {
        if (Growth is null)
        {
            return CommitResult.Reject("未载入战斗内容，不能调整人物");
        }

        if (!CanManage)
        {
            return CommitResult.Reject("对话、换图或战斗进行中，告一段落后再调整");
        }

        Candidate = World.Clone();
        if (change(Growth) is { } error)
        {
            return CommitResult.Reject(error);
        }

        var r = new EffectResult();
        if (notice is not null)
        {
            r.Notices.Add(notice);
        }

        return Commit(Candidate, r);
    }

    private CommitResult Trade(Action<EffectResult> trade)
    {
        if (!CanManage)
        {
            return CommitResult.Reject("对话、换图或战斗进行中，告一段落后再买卖");
        }

        Candidate = World.Clone();
        var r = new EffectResult();
        trade(r);
        return r.Ok ? Commit(Candidate, r) : CommitResult.From(r);
    }

    // ── 战斗结算 ──────────────────────────────────────────

    /// <summary>
    /// 待开剧情战的开战输入（界面与测试共用）：主角与可招募伙伴由世界状态现推模板，暂时同行的经典人物用各自角色模板；
    /// 站位取队伍阵位；带入行囊里的战斗用药；同行者先手与支线结果按世界事实套用遭遇变体。需要成长规则（含战斗内容）。
    /// </summary>
    public BattleSetup StoryBattleSetup(ulong seed)
    {
        var growth = Growth ?? throw new InvalidOperationException("没有成长规则，不能开剧情战");
        var w = World;
        var pending = w.Battle ?? throw new InvalidOperationException("没有待开战斗");
        var content = growth.Combat;
        var encounter = content.Encounter(pending.Encounter);
        var hero = Rules.Content.Progression.Hero;
        // 单人遭遇（一对一切磋）：只有主角上场，站前排正中。
        var allies = encounter.Solo
            ? [new AllyEntry(growth.Template(w, hero), hero, new Position(0, 1))]
            : PartyRules.Cells(w).Take(BattleSetup.MaxAllies)
                .Select(x => new AllyEntry(growth.Template(w, x.Id), x.Id, new Position(PartyRules.RowOf(x.Cell), PartyRules.SlotOf(x.Cell))))
                .ToList();
        // 切磋点到为止，不动用行囊里的药。
        var items = encounter.Victory == VictoryRule.Spar
            ? new Dictionary<string, int>()
            : w.Items.Where(i => content.Items.ContainsKey(i.Key) && i.Value > 0).ToDictionary(i => i.Key, i => i.Value);
        return new BattleSetup
        {
            EncounterId = pending.Encounter, Seed = seed, Allies = allies, Items = items,
            Variants = EncounterVariants.Active(encounter, w),
        };
    }

    /// <summary>
    /// 以 <c>battle_instance_id</c> 一次性结算（架构文档 11）：胜利效果按请求只发一次，
    /// 同一实例重复结算被忽略。可重试的战败保留请求并递增次数；不可重试的执行战败效果。
    /// <paramref name="consumed"/> 是战斗中用掉的行囊物品（无论胜负都扣除，随同一事务提交，重复结算不重复扣）；
    /// 数量超过行囊现有的按现有扣完，不让事务失败。
    /// </summary>
    public CommitResult SettleBattle(string instanceId, BattleEnd end, int experience = 0, IReadOnlyDictionary<string, int>? consumed = null,
        int cultivation = 0)
    {
        EnsureIdle();
        if (World.Battle is not { } battle || battle.InstanceId != instanceId)
        {
            return CommitResult.Reject($"战斗 {instanceId} 不在待结算状态（可能已结算）");
        }

        var c = World.Clone();
        var r = new EffectResult();
        c.Settled.Add("battle:" + instanceId);
        foreach (var (item, used) in consumed ?? new Dictionary<string, int>())
        {
            var have = c.Items.GetValueOrDefault(item);
            var take = Math.Min(have, used);
            if (take > 0)
            {
                Rules.Apply(c, [new WorldEffect { Type = WorldEffectType.RemoveItem, Id = item, Amount = take }], r, $"battle:{instanceId}:item:{item}");
            }
        }

        if (end == BattleEnd.Victory)
        {
            c.Battle = null;
            IEnumerable<WorldEffect> effects = battle.OnVictory;
            if (experience > 0)
            {
                effects = effects.Append(new WorldEffect { Type = WorldEffectType.GrantExperience, Amount = experience });
            }

            if (cultivation > 0)
            {
                effects = effects.Append(new WorldEffect { Type = WorldEffectType.GrantCultivation, Amount = cultivation });
            }

            Rules.Apply(c, effects, r, $"battle:{battle.RequestId}:victory");
        }
        else if (battle.Retry)
        {
            c.Battle!.Attempt++;
        }
        else
        {
            c.Battle = null;
            Rules.Apply(c, battle.OnDefeat, r, $"battle:{battle.RequestId}:defeat");
        }

        return r.Ok ? Commit(c, r) : CommitResult.From(r);
    }

    /// <summary>战败后不再重试：执行战败收束效果并回到当前地图安全入口。</summary>
    public CommitResult GiveUpBattle()
    {
        EnsureIdle();
        if (World.Battle is not { } battle)
        {
            return CommitResult.Reject("没有待开的战斗");
        }

        var c = World.Clone();
        c.Battle = null;
        if (Rules.Content.Maps.TryGetValue(c.MapId, out var map))
        {
            c.SpawnId = map.SafeSpawn;
        }

        var r = new EffectResult();
        Rules.Apply(c, battle.OnDefeat, r, $"battle:{battle.RequestId}:defeat");
        return r.Ok ? Commit(c, r) : CommitResult.From(r);
    }

    // ── 内部 ──────────────────────────────────────────

    private CommitResult Commit(WorldState candidate, EffectResult r)
    {
        // 到达地图、时辰推进等也会满足任务条件（如“抵达客栈”），提交前统一推进一次。
        Rules.Refresh(candidate, r);
        if (!r.Ok)
        {
            return CommitResult.From(r);
        }

        candidate.Visited.Add(candidate.MapId);
        candidate.Revision = World.Revision + 1;
        World = candidate;
        return CommitResult.From(r);
    }

    private void EnsureIdle()
    {
        if (_dialogue is not null || _transition is not null)
        {
            throw new InvalidOperationException("已有进行中的对话或场景切换");
        }
    }

    /// <summary>有待开战斗时先打完、重试或放弃，才能继续探索。</summary>
    private void EnsureExploring()
    {
        EnsureIdle();
        if (World.Battle is not null)
        {
            throw new InvalidOperationException($"待开战斗 {World.Battle.RequestId} 尚未结算");
        }
    }
}
