using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Domain.World;

namespace WuxiaWorld.Application.Dev;

/// <summary>第一章路线上可以停下的位置（开发用的跳关点，均停在该处的对话或战斗开始之前）。</summary>
public enum ChapterOnePoint
{
    /// <summary>新游戏，芦湾河滩，开场对话之前。</summary>
    Start,

    /// <summary>进了江南客栈，会面对话之前。</summary>
    InnCouncil,

    /// <summary>会面之后，客栈里，支线委托与讨教之前。</summary>
    Mentor,

    /// <summary>讨教之后，客栈里：给了 <see cref="ChapterOneRoute.Spar"/> 时后院切磋待开；不切磋时就停在讨教之后、选同行者之前。</summary>
    SparBattle,

    /// <summary>讨教与选同行者之后，芦湾街码头，乘船之前。</summary>
    Departure,

    /// <summary>刚到芦湾旧渡，登岸对话之前。</summary>
    OldFerry,

    /// <summary>登岸对话之后，押运队剧情战待开。</summary>
    EscortBattle,

    /// <summary>押运队战后（支线抢先救人已做），水门对峙之前。</summary>
    Sluice,

    /// <summary>水门对峙之后，旧渡水门首领战待开。</summary>
    SluiceBattle,

    /// <summary>首领战后，副页保管的选择之前。</summary>
    Custody,

    /// <summary>回到客栈，章末收束对话之前。</summary>
    Epilogue,

    /// <summary>整章走完。</summary>
    End,
}

/// <summary>
/// 第一章的固定路线（开发与测试共用，不是游戏规则）：按给定选择驱动 <see cref="GameSession"/> 从开场走到指定位置，
/// 对话在规则层直接走完、战斗默认按胜利结算，不渲染、不行走，整章不到一秒。
/// 规则测试用它走查全部同行者、支线与副页组合；游戏的开发参数 <c>--jump</c> 与战斗测试台用它直接造出某处的世界状态。
/// 每一步都核对任务阶段，内容改动使路线走不通时抛出 <see cref="InvalidOperationException"/> 并写明卡在哪里。
/// </summary>
public sealed class ChapterOneRoute(GameSession game)
{
    public const string MainQuest = "quest.main.01.jiangnan_guest";
    public const string SideQuest = "quest.side.01.missing_ferryman";
    public const string FerryRoute = "route.jiangnan.luwan_to_old_ferry";

    /// <summary>可选的同行者 / 讨教人选（选项 ID 的末段）。</summary>
    public static readonly IReadOnlyList<string> Companions = ["linghu", "huang", "xiao"];

    private readonly Queue<string> _picks = new();

    public GameSession Game { get; } = game;

    /// <summary>同行者：<c>linghu</c> / <c>huang</c> / <c>xiao</c>。</summary>
    public string Companion { get; init; } = "linghu";

    /// <summary>讨教人选（决定所学流派：令狐冲剑术、黄蓉内功、萧峰拳掌）；不给时同 <see cref="Companion"/>。</summary>
    public string? Mentor { get; init; }

    /// <summary>讨教后找那位侠客后院切磋：<c>won</c> / <c>lost</c> 按该结果结算；null 不切磋。</summary>
    public string? Spar { get; init; }

    /// <summary>副页保管：<c>public</c> / <c>sealed</c>。</summary>
    public string Custody { get; init; } = "public";

    /// <summary>接失踪渡工支线。</summary>
    public bool Side { get; init; }

    /// <summary>接了支线后做完查证（船牌、潮痕）；做完且 <see cref="FreeEarly"/> 时在首领战前抢先救人。</summary>
    public bool FinishSideSteps { get; init; } = true;

    public bool FreeEarly { get; init; } = true;

    /// <summary>先婉拒一次支线委托再走主线（测试用）。</summary>
    public bool DeclineSideFirst { get; init; }

    /// <summary>去客栈前先读渡口告示（测试用）。</summary>
    public bool ReadNoticeEarly { get; init; }

    public string OpeningPick { get; init; } = "choice.a";
    public string CouncilPick { get; init; } = "choice.insight";

    /// <summary>
    /// 每场剧情战开打前（登岸对话、水门对峙之前）与停下时，把主角未分配的潜能按流派推荐分完，像真实玩家那样；
    /// 规则测试不开（按未分配计），游戏的跳关与战斗测试台打开。
    /// </summary>
    public bool AllocatePotential { get; init; }

    /// <summary>剧情战怎么打：给出时由它开战并结算（须打赢），否则按胜利直接结算并按遭遇发经验与修为。</summary>
    public Action<GameSession>? Fighter { get; init; }

    /// <summary>默认结算时查遭遇经验与修为的战斗内容；不给时用 <see cref="GameSession.Growth"/> 的。</summary>
    public CombatContent? Combat { get; init; }

    /// <summary>每段对话走完（提交前）回调，测试据此收集走到的台词与演出。</summary>
    public Action<DialogueRunner>? OnDialogue { get; init; }

    /// <summary>已走到的位置。</summary>
    public ChapterOnePoint Reached { get; private set; } = ChapterOnePoint.Start;

    /// <summary>世界状态已在某处（例如读档后）时，从这里接着走，前面的步骤不再执行。</summary>
    public ChapterOnePoint From { init => Reached = value; }

    /// <summary>从当前位置（须是新游戏，或上次停下的位置）接着走到 <paramref name="stop"/>。</summary>
    public void RunTo(ChapterOnePoint stop)
    {
        var mentor = Mentor ?? Companion;
        if (Stop(ChapterOnePoint.Start, stop))
        {
            return;
        }

        if (Reached < ChapterOnePoint.InnCouncil)
        {
            PlayAuto(); // 开场
            Interact("stone_marks");
            Expect("to_inn");
            Exit("to_street");
            if (ReadNoticeEarly)
            {
                Interact("ferry_notice");
            }

            Exit("to_inn");
        }

        if (Stop(ChapterOnePoint.InnCouncil, stop))
        {
            return;
        }

        if (Reached < ChapterOnePoint.Mentor)
        {
            Pick(CouncilPick);
            PlayAuto(); // 会面
            Expect("training");
        }

        if (Stop(ChapterOnePoint.Mentor, stop))
        {
            return;
        }

        if (Reached < ChapterOnePoint.SparBattle)
        {
            if (DeclineSideFirst)
            {
                Pick("choice.later");
                PlayEvent("event.ch01.side_offer");
            }

            if (Side)
            {
                Pick("choice.accept");
                PlayEvent("event.ch01.side_offer");
            }

            Pick("choice." + mentor);
            PlayEvent("event.ch01.mentor_choice");
            if (Spar is not null)
            {
                // 像真实玩家那样先去人物页分了刚升级的潜能，再去找人切磋。
                AutoAllocate();
                Pick("choice.spar");
                PlayEvent("event.ch01.mentor_spar_" + mentor);
            }
        }

        if (Stop(ChapterOnePoint.SparBattle, stop))
        {
            return;
        }

        if (Reached < ChapterOnePoint.Departure)
        {
            if (Spar is not null)
            {
                SettleSpar();
                PlayAuto(); // 切磋点评
            }

            if (Side && FinishSideSteps)
            {
                Exit("out");
                Interact("ferry_tags");
                Exit("to_shore");
                Interact("tide_line");
                Exit("to_street");
                Exit("to_inn");
            }

            Pick("choice." + Companion);
            PlayEvent("event.ch01.companion_choice");
            Exit("out");
        }

        if (Stop(ChapterOnePoint.Departure, stop))
        {
            return;
        }

        if (Reached < ChapterOnePoint.OldFerry)
        {
            Commit(Game.BeginRoute(FerryRoute, TravelMode.Ferry), "乘船去旧渡");
            Expect("rescue");
        }

        if (Stop(ChapterOnePoint.OldFerry, stop))
        {
            return;
        }

        if (Reached < ChapterOnePoint.EscortBattle)
        {
            AutoAllocate();
            PlayAuto(); // 登岸
        }

        if (Stop(ChapterOnePoint.EscortBattle, stop))
        {
            return;
        }

        if (Reached < ChapterOnePoint.Sluice)
        {
            WinBattle();
            if (Side && FinishSideSteps && FreeEarly && Game.Interactables.Any(i => i.Id == "locked_boat"))
            {
                Interact("locked_boat");
            }
        }

        if (Stop(ChapterOnePoint.Sluice, stop))
        {
            return;
        }

        if (Reached < ChapterOnePoint.SluiceBattle)
        {
            AutoAllocate();
            PlayEvent("event.ch01.sluice_confrontation");
        }

        if (Stop(ChapterOnePoint.SluiceBattle, stop))
        {
            return;
        }

        if (Reached < ChapterOnePoint.Custody)
        {
            WinBattle();
            Expect("custody");
        }

        if (Stop(ChapterOnePoint.Custody, stop))
        {
            return;
        }

        if (Reached < ChapterOnePoint.Epilogue)
        {
            Pick("choice." + Custody);
            var result = PlayAuto();
            var travel = result.Travel ?? throw Stuck("副页保管后没有回客栈的剧情换图");
            Commit(Game.BeginStoryTravel(travel), "回客栈");
        }

        if (Stop(ChapterOnePoint.Epilogue, stop))
        {
            return;
        }

        if (Reached < ChapterOnePoint.End)
        {
            PlayAuto(); // 收束
        }

        Reached = ChapterOnePoint.End;
        AutoAllocate();
    }

    /// <summary>记下已走到 <paramref name="point"/>；正好是要停的位置时返回 true（停下前按需分配潜能）。</summary>
    private bool Stop(ChapterOnePoint point, ChapterOnePoint stop)
    {
        if (Reached < point)
        {
            Reached = point;
        }

        if (point == stop)
        {
            AutoAllocate();
            return true;
        }

        return false;
    }

    /// <summary><see cref="AllocatePotential"/> 打开且此刻可以调整养成（没有待开战斗）时分配。</summary>
    private void AutoAllocate()
    {
        if (AllocatePotential && Game.World.Battle is null && Game.Growth is not null)
        {
            AllocateRecommended();
        }
    }

    /// <summary>
    /// 主角未分配的潜能按流派推荐比例分完（真实玩家在剧情战前通常已分配；路线本身不分配，规则测试按未分配计）。
    /// 返回分出去的点数。
    /// </summary>
    public int AllocateRecommended()
    {
        var growth = Game.Growth ?? throw Stuck("没有成长规则，不能分配潜能");
        var unspent = growth.Unspent(Game.World, Hero);
        if (unspent <= 0)
        {
            return 0;
        }

        var r = Game.Allocate(Hero, growth.Recommend(Game.World, Hero));
        return r.Ok ? unspent : throw Stuck($"分配潜能失败：{r.Error}");
    }

    /// <summary>
    /// 开发用：把主角经验直接改到 <paramref name="level"/> 级的门槛（不经事务、不发通知，已学武学与修为不变；伙伴经验不跟着变）。
    /// 只给战斗测试台核对不同等级的难度，不用于正式流程。
    /// </summary>
    public void SetHeroLevel(int level)
    {
        var table = Game.Rules.Content.Progression.Experience;
        level = Math.Clamp(level, 1, table.Count + 1);
        Game.World.Experience = level == 1 ? 0 : table[level - 2];
    }

    private const string Hero = "char.hero";

    // ── 单步操作（测试也直接用） ─────────────────────────

    /// <summary>预先给下一段对话里遇到的选项排队（按选项 <c>line_id</c> 的末段匹配）。</summary>
    public void Pick(string lineSuffix) => _picks.Enqueue(lineSuffix);

    public CommitResult PlayAuto()
    {
        var e = Game.AutoEvent ?? throw Stuck("没有自动事件");
        return PlayEvent(e.Id);
    }

    public CommitResult PlayEvent(string id) => Play(Game.StartEvent(id));

    public void Interact(string id)
    {
        var d = Game.Interact(id, out var r);
        if (!r.Ok)
        {
            throw Stuck($"交互 {id} 失败：{r.Error}");
        }

        if (d is not null)
        {
            Play(d);
        }
    }

    public void Exit(string id) => Commit(Game.BeginExit(id), $"出口 {id}");

    public void WinBattle()
    {
        if (Fighter is not null)
        {
            Fighter(Game);
            return;
        }

        var b = Game.World.Battle ?? throw Stuck("没有待开战斗");
        var encounter = (Combat ?? Game.Growth?.Combat)?.Encounter(b.Encounter);
        var r = Game.SettleBattle(b.InstanceId, BattleEnd.Victory, experience: encounter?.Experience ?? 0, cultivation: encounter?.Cultivation ?? 0);
        if (!r.Ok)
        {
            throw Stuck($"结算 {b.Encounter} 失败：{r.Error}");
        }
    }

    /// <summary>按 <see cref="Spar"/> 结算待开的切磋（不经 <see cref="Fighter"/>：切磋输赢都能往下走）。</summary>
    private void SettleSpar()
    {
        var b = Game.World.Battle ?? throw Stuck("没有待开的切磋");
        var won = Spar == "won";
        var encounter = (Combat ?? Game.Growth?.Combat)?.Encounter(b.Encounter);
        var r = Game.SettleBattle(b.InstanceId, won ? BattleEnd.Victory : BattleEnd.Defeat,
            experience: won ? encounter?.Experience ?? 0 : 0, cultivation: won ? encounter?.Cultivation ?? 0 : 0);
        if (!r.Ok)
        {
            throw Stuck($"结算切磋 {b.Encounter} 失败：{r.Error}");
        }
    }

    private void Commit(Transition t, string what)
    {
        var r = Game.CommitTransition(t);
        if (!r.Ok)
        {
            throw Stuck($"{what}失败：{r.Error}");
        }
    }

    private void Expect(string stage)
    {
        var actual = Game.World.Quests.TryGetValue(MainQuest, out var q) ? q.Stage : null;
        if (actual != stage)
        {
            throw Stuck($"主线应在 {stage} 阶段，实际为 {actual ?? "未接"}");
        }
    }

    private CommitResult Play(DialogueSession d)
    {
        var opening = d.Runner.Definition.Id == "dlg.ch01.opening_luwan";
        while (!d.Runner.Ended)
        {
            if (!d.Runner.AwaitingChoice)
            {
                d.Runner.Continue();
                continue;
            }

            var want = opening ? OpeningPick : _picks.Count > 0 ? _picks.Dequeue() : null;
            var choice = d.Runner.Choices.FirstOrDefault(c => c.Enabled && (want is null || c.Option.LineId.EndsWith("." + want, StringComparison.Ordinal)))
                ?? throw Stuck($"{d.Runner.Definition.Id}：找不到选项 {want}");
            d.Runner.Choose(choice.Index);
        }

        OnDialogue?.Invoke(d.Runner);
        var r = Game.FinishDialogue(d);
        return r.Ok ? r : throw Stuck($"{d.Runner.Definition.Id} 提交失败：{r.Error}");
    }

    private InvalidOperationException Stuck(string what) =>
        new($"第一章路线（{Companion}{(Side ? " + 支线" : "")}{(Spar is null ? "" : " + 切磋")}）走到 {Reached} 之后卡住：{what}（地图 {Game.World.MapId}）");
}
