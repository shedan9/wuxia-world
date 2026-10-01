namespace WuxiaWorld.Domain.World;

/// <summary>条件原语（架构文档 9.1），支持受限的 all / any / not 组合；不提供任意脚本。</summary>
public enum ConditionType
{
    All,
    Any,
    Not,

    /// <summary>持有 <c>id</c> 物品至少 <c>amount</c> 个（缺省 1）。</summary>
    HasItem,

    /// <summary>任务 <c>id</c> 处于 <c>status</c>；给出 <c>stage</c> 时还须处于该阶段。</summary>
    QuestStateIs,

    /// <summary>对人物 <c>id</c> 的 <c>axis</c>（好感 / 信任）不少于 <c>amount</c>。</summary>
    RelationshipAtLeast,

    /// <summary>事实 <c>id</c> 等于 <c>value</c>。</summary>
    FactEquals,

    /// <summary>人物 <c>id</c> 未被其他事件占用。</summary>
    CharacterAvailable,

    PartyContains,
    SkillLearned,
    AtRegion,
    AtMap,
    WorldGateReached,

    /// <summary>见闻札记里已有线索 <c>id</c>。</summary>
    ClueKnown,

    /// <summary>已当面见过人物 <c>id</c>。</summary>
    CharacterMet,

    /// <summary>银两不少于 <c>amount</c>。</summary>
    SilverAtLeast,
}

public enum RelationshipAxis
{
    Affection,
    Trust,
}

/// <summary>条件节点。未用到的字段保持默认值，未知字段在内容加载时报错。</summary>
public sealed record Condition
{
    public ConditionType Type { get; init; }

    /// <summary>all / any 的子条件；not 只取第一个。</summary>
    public IReadOnlyList<Condition> Of { get; init; } = [];

    public string? Id { get; init; }
    public string? Value { get; init; }
    public int Amount { get; init; } = 1;
    public QuestStatus Status { get; init; }
    public string? Stage { get; init; }
    public RelationshipAxis Axis { get; init; }

    public static Condition Fact(string id, string value) => new() { Type = ConditionType.FactEquals, Id = id, Value = value };

    public static Condition Quest(string id, QuestStatus status, string? stage = null) =>
        new() { Type = ConditionType.QuestStateIs, Id = id, Status = status, Stage = stage };

    public static Condition AllOf(params Condition[] of) => new() { Type = ConditionType.All, Of = of };

    public static Condition AnyOf(params Condition[] of) => new() { Type = ConditionType.Any, Of = of };

    public static Condition NotOf(Condition c) => new() { Type = ConditionType.Not, Of = [c] };
}

/// <summary>条件求值：只读世界状态，不改状态、不消耗随机数。</summary>
public static class Conditions
{
    /// <summary>空条件视为满足。</summary>
    public static bool Check(Condition? c, WorldState s, WorldContent content) => c is null || Eval(c, s, content);

    private static bool Eval(Condition c, WorldState s, WorldContent content) => c.Type switch
    {
        ConditionType.All => c.Of.All(x => Eval(x, s, content)),
        ConditionType.Any => c.Of.Any(x => Eval(x, s, content)),
        ConditionType.Not => c.Of.Count > 0 && !Eval(c.Of[0], s, content),
        ConditionType.HasItem => s.CountOf(c.Id!) >= Math.Max(1, c.Amount),
        ConditionType.QuestStateIs => s.QuestStatusOf(c.Id!) == c.Status
            && (c.Stage is null || (s.Quests.TryGetValue(c.Id!, out var q) && q.Stage == c.Stage)),
        ConditionType.RelationshipAtLeast => s.Relationships.TryGetValue(c.Id!, out var r)
            ? (c.Axis == RelationshipAxis.Trust ? r.Trust : r.Affection) >= c.Amount
            : c.Amount <= 0,
        ConditionType.FactEquals => s.Facts.TryGetValue(c.Id!, out var v) && v == c.Value,
        ConditionType.CharacterAvailable => !s.Reservations.ContainsKey(c.Id!),
        ConditionType.PartyContains => s.Party.Contains(c.Id!, StringComparer.Ordinal),
        ConditionType.SkillLearned => s.Skills.Contains(c.Id!),
        ConditionType.AtRegion => content.RegionOf(s.MapId) == c.Id,
        ConditionType.AtMap => s.MapId == c.Id,
        ConditionType.WorldGateReached => s.Gates.Contains(c.Id!),
        ConditionType.ClueKnown => s.Clues.Contains(c.Id!),
        ConditionType.CharacterMet => s.Met.Contains(c.Id!),
        ConditionType.SilverAtLeast => s.Silver >= c.Amount,
        _ => false,
    };

    /// <summary>条件里引用到的全部 ID 与类型，供内容校验检查引用。</summary>
    public static IEnumerable<Condition> Flatten(Condition? c)
    {
        if (c is null)
        {
            yield break;
        }

        yield return c;
        foreach (var child in c.Of.SelectMany(Flatten))
        {
            yield return child;
        }
    }
}
