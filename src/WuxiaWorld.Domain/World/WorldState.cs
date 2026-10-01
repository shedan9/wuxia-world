using WuxiaWorld.Domain.Common;

namespace WuxiaWorld.Domain.World;

/// <summary>任务状态（架构文档 9.1）：<c>Locked → Available → Active → Completed / Failed / Abandoned</c>。</summary>
public enum QuestStatus
{
    Locked,
    Available,
    Active,
    Completed,
    Failed,
    Abandoned,
}

/// <summary>一条任务的进度。只记录事实，显示文字由内容与文本表给出。</summary>
public sealed class QuestProgress
{
    public QuestStatus Status { get; set; }

    /// <summary>当前阶段；未激活或已结束时为最后所在阶段。</summary>
    public string? Stage { get; set; }

    /// <summary>当前阶段已完成的目标 ID。</summary>
    public SortedSet<string> Objectives { get; init; } = new(StringComparer.Ordinal);

    /// <summary>走过的阶段，按先后次序；用于任务日志回看与汇合检查。</summary>
    public List<string> History { get; init; } = [];

    public QuestProgress Clone() => new()
    {
        Status = Status, Stage = Stage,
        Objectives = new SortedSet<string>(Objectives, StringComparer.Ordinal),
        History = [.. History],
    };
}

/// <summary>主角对某人的关系（架构文档 8.4）：好感、信任数值化；承诺与立场用事件事实记录。</summary>
public sealed class RelationshipState
{
    public int Affection { get; set; }
    public int Trust { get; set; }
    public SortedSet<string> Commitments { get; init; } = new(StringComparer.Ordinal);

    public RelationshipState Clone() => new()
    {
        Affection = Affection, Trust = Trust,
        Commitments = new SortedSet<string>(Commitments, StringComparer.Ordinal),
    };
}

/// <summary>待开打的战斗：由剧情效果请求，提交后进入存档，读档可从战前快照重开（架构文档 11）。</summary>
public sealed class PendingBattle
{
    /// <summary>请求身份：同一请求无论重试几次，胜利效果只结算一次。</summary>
    public required string RequestId { get; init; }

    public required string Encounter { get; init; }

    /// <summary>第几次开打；与请求 ID 组成 <c>battle_instance_id</c>。</summary>
    public int Attempt { get; set; } = 1;

    public string InstanceId => $"{RequestId}#{Attempt}";

    public IReadOnlyList<WorldEffect> OnVictory { get; init; } = [];
    public IReadOnlyList<WorldEffect> OnDefeat { get; init; } = [];

    /// <summary>战败后能否原地重试；否则按 <see cref="OnDefeat"/> 收束（普通战败均可重试或回安全点）。</summary>
    public bool Retry { get; init; } = true;

    public PendingBattle Clone() => new()
    {
        RequestId = RequestId, Encounter = Encounter, Attempt = Attempt, OnVictory = OnVictory, OnDefeat = OnDefeat, Retry = Retry,
    };
}

/// <summary>
/// 统一世界的完整可存档状态（架构文档 9.2 <c>WorldState</c>、11）。领域规则只改这一份数据；
/// 应用层在副本上执行事务，成功后整体替换，失败则丢弃副本。集合一律按序数排序，哈希与存档次序稳定。
/// </summary>
public sealed class WorldState
{
    public string WorldId { get; set; } = "world.main";
    public string ArcId { get; set; } = "";
    public string ChapterId { get; set; } = "";

    /// <summary>已达成的世界关口；并行任务是否完成看任务状态，不由章节数值推断。</summary>
    public SortedSet<string> Gates { get; init; } = new(StringComparer.Ordinal);

    /// <summary>当前所在地图与落点（读档从这里出生）。</summary>
    public string MapId { get; set; } = "";

    public string SpawnId { get; set; } = "";

    /// <summary>逻辑时辰计数：1 = 一个时辰，12 个时辰为一日。只由旅行与关键行为推进。</summary>
    public long Clock { get; set; }

    /// <summary>每次提交事务递增；用于识别过期的旅行票据与诊断。</summary>
    public long Revision { get; set; }

    public SortedDictionary<string, string> Facts { get; init; } = new(StringComparer.Ordinal);

    /// <summary>已结算的一次性键（对白效果、任务奖励、战斗实例、旅行事务、地区事件）；重复处理时据此跳过。</summary>
    public SortedSet<string> Settled { get; init; } = new(StringComparer.Ordinal);

    /// <summary>见闻札记里已经获得的线索。</summary>
    public SortedSet<string> Clues { get; init; } = new(StringComparer.Ordinal);

    /// <summary>确实当面相遇过的人物（<c>met_character</c>）；传闻与遗迹另记线索，不伪造在世实体。</summary>
    public SortedSet<string> Met { get; init; } = new(StringComparer.Ordinal);

    /// <summary>队伍：首位为主角，最多主角 + 3 名伙伴。</summary>
    public List<string> Party { get; init; } = [];

    /// <summary>人物 → 占用它的事件 ID（架构文档 9.1 事件占用）。</summary>
    public SortedDictionary<string, string> Reservations { get; init; } = new(StringComparer.Ordinal);

    public SortedDictionary<string, QuestProgress> Quests { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, RelationshipState> Relationships { get; init; } = new(StringComparer.Ordinal);

    public int Silver { get; set; }

    /// <summary>可堆叠物品数量（定义 ID → 数量）。装备实例在 M2-05 的物品模块补上。</summary>
    public SortedDictionary<string, int> Items { get; init; } = new(StringComparer.Ordinal);

    /// <summary>主角已学武学；成长与装配在 M2-05 接入。</summary>
    public SortedSet<string> Skills { get; init; } = new(StringComparer.Ordinal);

    public int Experience { get; set; }

    /// <summary>地图差异：已消耗的一次性交互物，键为 <c>map_id/interactable_id</c>（架构文档 6.4）。</summary>
    public SortedSet<string> MapDeltas { get; init; } = new(StringComparer.Ordinal);

    public PendingBattle? Battle { get; set; }

    /// <summary>旅行途中抽中的事件：到达后自动开始，结算前读档仍会继续（架构文档 6.3）。</summary>
    public string? QueuedEvent { get; set; }

    /// <summary>世界随机流（途中事件等）；与战斗流分开。</summary>
    public ulong RngState { get; set; }

    public ulong RngIncrement { get; set; }

    public Pcg32 GetRng() => Pcg32.Restore(RngState, RngIncrement);

    public void SetRng(Pcg32 rng)
    {
        RngState = rng.State;
        RngIncrement = rng.Increment;
    }

    public string Hero => Party.Count > 0 ? Party[0] : "";

    public QuestStatus QuestStatusOf(string questId) =>
        Quests.TryGetValue(questId, out var q) ? q.Status : QuestStatus.Locked;

    public int CountOf(string itemId) => Items.TryGetValue(itemId, out var n) ? n : 0;

    public RelationshipState Relationship(string characterId)
    {
        if (!Relationships.TryGetValue(characterId, out var r))
        {
            r = new RelationshipState();
            Relationships[characterId] = r;
        }

        return r;
    }

    public WorldState Clone()
    {
        var c = new WorldState
        {
            WorldId = WorldId, ArcId = ArcId, ChapterId = ChapterId, MapId = MapId, SpawnId = SpawnId, Clock = Clock,
            Revision = Revision, Silver = Silver, Experience = Experience, RngState = RngState, RngIncrement = RngIncrement,
            Battle = Battle?.Clone(), QueuedEvent = QueuedEvent,
            Party = [.. Party],
        };
        c.Gates.UnionWith(Gates);
        c.Settled.UnionWith(Settled);
        c.Clues.UnionWith(Clues);
        c.Met.UnionWith(Met);
        c.Skills.UnionWith(Skills);
        c.MapDeltas.UnionWith(MapDeltas);
        foreach (var (k, v) in Facts)
        {
            c.Facts[k] = v;
        }

        foreach (var (k, v) in Reservations)
        {
            c.Reservations[k] = v;
        }

        foreach (var (k, v) in Items)
        {
            c.Items[k] = v;
        }

        foreach (var (k, v) in Quests)
        {
            c.Quests[k] = v.Clone();
        }

        foreach (var (k, v) in Relationships)
        {
            c.Relationships[k] = v.Clone();
        }

        return c;
    }

    /// <summary>状态哈希：存档往返与事务重放比对用，字段按固定次序写入。</summary>
    public string Hash()
    {
        var h = new StateHasher().Add(WorldId).Add(ArcId).Add(ChapterId).Add(MapId).Add(SpawnId).Add(Clock).Add(Revision)
            .Add(Silver).Add(Experience).Add((long)RngState).Add((long)RngIncrement);
        void Set(string tag, IEnumerable<string> values)
        {
            h.Add(tag);
            foreach (var v in values)
            {
                h.Add(v);
            }
        }

        Set("gates", Gates);
        Set("settled", Settled);
        Set("clues", Clues);
        Set("met", Met);
        Set("skills", Skills);
        Set("deltas", MapDeltas);
        Set("party", Party);
        h.Add("facts");
        foreach (var (k, v) in Facts)
        {
            h.Add(k).Add(v);
        }

        h.Add("reservations");
        foreach (var (k, v) in Reservations)
        {
            h.Add(k).Add(v);
        }

        h.Add("items");
        foreach (var (k, v) in Items)
        {
            h.Add(k).Add(v);
        }

        h.Add("quests");
        foreach (var (k, q) in Quests)
        {
            h.Add(k).Add((int)q.Status).Add(q.Stage);
            Set("objectives", q.Objectives);
            Set("history", q.History);
        }

        h.Add("relationships");
        foreach (var (k, r) in Relationships)
        {
            h.Add(k).Add(r.Affection).Add(r.Trust);
            Set("commitments", r.Commitments);
        }

        h.Add(QueuedEvent).Add("battle");
        if (Battle is { } b)
        {
            h.Add(b.RequestId).Add(b.Encounter).Add(b.Attempt).Add(b.Retry).Add(b.OnVictory.Count).Add(b.OnDefeat.Count);
        }

        return h.Hex;
    }
}
