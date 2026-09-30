namespace WuxiaWorld.Domain.Combat.Definitions;

/// <summary>持续时间的消耗时点：持有者自身行动开始或结束（架构文档 7.3）。</summary>
public enum StatusTick
{
    OwnerActionStart,
    OwnerActionEnd,

    /// <summary>施加者下次行动开始（护援：持续到护卫者再次行动）。</summary>
    SourceActionStart,
}

public enum StackRule
{
    /// <summary>再次施加只刷新持续次数。</summary>
    Refresh,

    /// <summary>叠一层（不超过上限）并刷新持续次数。</summary>
    AddStack,

    /// <summary>已有时忽略。</summary>
    Ignore,
}

/// <summary>
/// 状态定义：触发时点、叠层上限、刷新规则、驱散标签（架构文档 7.3），效果取有限的修正字段。
/// 伤害与架势修正为加法，与其他同类状态相加后再截断，避免同类倍率连乘。
/// </summary>
public sealed record StatusDefinition
{
    public required string Id { get; init; }
    public bool Harmful { get; init; }

    /// <summary>驱散与免疫标签，如 <c>control</c>、<c>bleed</c>、<c>dispellable</c>。</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    public StatusTick Tick { get; init; } = StatusTick.OwnerActionEnd;

    /// <summary>默认持续次数；0 表示不随时点消耗（如水位，由效果移除）。</summary>
    public int Duration { get; init; } = 1;

    public int MaxStacks { get; init; } = 1;
    public StackRule Stacking { get; init; } = StackRule.Refresh;

    /// <summary>行动开始时每层按最大气血的万分比掉血（流血、中毒）。</summary>
    public int TickDamageBp { get; init; }

    public int DamageTakenBp { get; init; }
    public int DamageDealtBp { get; init; }
    public int StanceTakenBp { get; init; }

    /// <summary>速度修正，只在下一轮排序时生效（架构文档 7.1）。</summary>
    public int SpeedBp { get; init; }

    /// <summary>内力恢复修正（内伤减半等）。</summary>
    public int InnerRecoveryBp { get; init; }

    public int HitBp { get; init; }

    /// <summary>硬控制：跳过本次主行动。</summary>
    public bool SkipAction { get; init; }

    /// <summary>受到一次有效伤害后消失（防御、破绽）。</summary>
    public bool ConsumeOnHit { get; init; }

    /// <summary>持有期间不能作出反应（破绽）。</summary>
    public bool CancelsReactions { get; init; }

    /// <summary>持有期间受近身攻击后反击（占用本轮反应）。</summary>
    public bool CounterReady { get; init; }

    /// <summary>结束或被消耗时把架势回满（破绽）。</summary>
    public bool RestoreStanceOnEnd { get; init; }

    /// <summary>结束时授予的状态（点穴结束后的免控）。</summary>
    public string? GrantOnEnd { get; init; }

    /// <summary>持有期间免疫带此标签的状态。</summary>
    public string? ImmuneTag { get; init; }

    public bool HasTag(string tag) => Tags.Contains(tag, StringComparer.Ordinal);
}
