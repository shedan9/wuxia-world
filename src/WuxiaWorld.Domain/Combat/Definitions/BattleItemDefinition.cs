namespace WuxiaWorld.Domain.Combat.Definitions;

/// <summary>
/// 战斗可用消耗品。物品实例、背包与装备在 M2 的物品模块建立，这里只描述战斗中使用时的目标与效果。
/// </summary>
public sealed record BattleItemDefinition
{
    public required string Id { get; init; }
    public TargetRule TargetRule { get; init; } = TargetRule.SingleAlly;
    public IReadOnlyList<EffectDefinition> Effects { get; init; } = [];
}

/// <summary>克制倍率：招式带 <see cref="SkillTag"/>、目标带 <see cref="TargetTag"/> 时乘以 <see cref="Bp"/>，限定 0.8–1.25（架构文档 7.4）。</summary>
public sealed record CounterRule(string SkillTag, string TargetTag, int Bp);
