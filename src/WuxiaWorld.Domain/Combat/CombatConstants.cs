namespace WuxiaWorld.Domain.Combat;

/// <summary>
/// v0 战斗常数（架构文档 7.2–7.4）。属于配置而非承诺；修改任何一项须递增
/// <see cref="Characters.StatFormula.RulesetVersion"/>，旧战斗记录据此拒绝重放。
/// </summary>
public static class CombatConstants
{
    public const int MaxMomentum = 100;

    // 命中率 = clamp(0.90 + (命中 − 闪避) / 1000, 0.65, 0.98)
    public const int HitBaseBp = 9000;
    public const int HitPerPointBp = 10;
    public const int HitMinBp = 6500;
    public const int HitMaxBp = 9800;

    public const int CritCapBp = 3500;
    public const int CritMultiplierBp = 15_000;
    public const int VarianceMinBp = 9500;
    public const int VarianceMaxBp = 10_500;
    public const int CounterMinBp = 8000;
    public const int CounterMaxBp = 12_500;

    /// <summary>攻方增减伤（加法合计）截断范围。</summary>
    public const int DealtMinBp = -5000;
    public const int DealtMaxBp = 10_000;

    /// <summary>受方增减伤（加法合计）截断范围。</summary>
    public const int TakenMinBp = -8000;
    public const int TakenMaxBp = 10_000;

    public const int MomentumOnHit = 5;
    public const int MomentumOnBreak = 10;
    public const int MomentumWhenHurt = 3;

    /// <summary>调息恢复最大内力的万分比；另回架势。</summary>
    public const int MeditateInnerBp = 2000;
    public const int MeditateStance = 10;
    public const int DefendStance = 5;

    /// <summary>反击按普通攻击的万分比倍率出手。</summary>
    public const int CounterAttackBp = 7000;

    public const int ParryStance = 10;
    public const int ParryMomentum = 10;

    /// <summary>天赋 stance_breaker 的架势伤害加成。</summary>
    public const int StanceBreakerBp = 2000;

    public const int RetreatBaseBp = 5000;
    public const int RetreatPerSpeedBp = 100;
    public const int RetreatMinBp = 2000;
    public const int RetreatMaxBp = 9500;

    /// <summary>单条命令产生的事件上限：超限说明反应链失控，报错并保存诊断快照（架构文档 7.5）。</summary>
    public const int EventCapPerCommand = 512;

    /// <summary>战斗随机流的 PCG 流选择量；旅行等其他用途另取（架构文档 7.5）。</summary>
    public const ulong BattleStream = 0xB477_1E00UL;
}
