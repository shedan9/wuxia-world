namespace WuxiaWorld.Domain.Characters;

public enum ArtKind
{
    /// <summary>心法：可装为主修或辅修。</summary>
    Inner,

    /// <summary>轻功。</summary>
    Qinggong,

    /// <summary>被动天赋。</summary>
    Talent,
}

public enum Polarity
{
    Neutral,
    Yin,
    Yang,
}

/// <summary>
/// 心法、轻功与天赋定义。效果只由有限的原语组成：固定属性加成、调息加成、每次行动回内与特性标签；
/// 新特性需要写代码，常规新心法只需数据（架构文档 8.2）。
/// </summary>
public sealed record ArtDefinition
{
    public required string Id { get; init; }
    public ArtKind Kind { get; init; }
    public Polarity Polarity { get; init; }
    public StatBonus Bonus { get; init; } = StatBonus.None;

    /// <summary>调息时额外恢复的内力（万分比，按最大内力计）。</summary>
    public int MeditateBonusBp { get; init; }

    /// <summary>每次自身行动开始时恢复的内力。</summary>
    public int InnerPerAction { get; init; }

    /// <summary>
    /// 规则特性标签，由战斗内核识别：<c>parry</c>（防御中受击时招架：回架势、积势，占用本轮反应）、
    /// <c>stance_breaker</c>（造成的架势伤害 +20%）。
    /// </summary>
    public IReadOnlyList<string> Traits { get; init; } = [];
}
