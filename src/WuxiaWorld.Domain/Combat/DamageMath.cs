using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Domain.Common;

namespace WuxiaWorld.Domain.Combat;

/// <summary>
/// 伤害计算，固定计算层顺序（架构文档 7.4）：
/// ① 基础 = (固定值 + 攻击 × 倍率) × 100 / (100 + max(0, 防御))
/// ② × 克制倍率（0.8–1.25）③ × 暴击倍率 ④ × (1 + 攻方增减伤合计) ⑤ × (1 + 受方增减伤合计) ⑥ × 浮动（0.95–1.05）
/// 每层用万分比整数相乘并向下取整；最后 max(1, ·)。护盾吸收层预留在 ⑥ 之后，M1 内容未用到。
/// </summary>
public static class DamageMath
{
    /// <summary>内部精度：基础伤害以 1/10000 为单位保存，避免中间层过早取整。</summary>
    private const long Scale = Bp.One;

    public readonly record struct Inputs(
        int Attack,
        int Defense,
        int Fixed,
        int ScaleBp,
        int CounterBp,
        bool Crit,
        int DealtBp,
        int TakenBp,
        int VarianceBp);

    public static long BaseScaled(int attack, int defense, int fixedValue, int scaleBp)
    {
        var raw = (long)fixedValue * Scale + (long)attack * scaleBp;
        return Bp.FloorDiv(raw * 100, 100 + Math.Max(0, defense));
    }

    public static int Compute(in Inputs i)
    {
        var v = BaseScaled(i.Attack, i.Defense, i.Fixed, i.ScaleBp);
        v = Bp.Apply(v, Bp.Clamp(i.CounterBp, CombatConstants.CounterMinBp, CombatConstants.CounterMaxBp));
        if (i.Crit)
        {
            v = Bp.Apply(v, CombatConstants.CritMultiplierBp);
        }

        v = Bp.Apply(v, Bp.One + Bp.Clamp(i.DealtBp, CombatConstants.DealtMinBp, CombatConstants.DealtMaxBp));
        v = Bp.Apply(v, Bp.One + Bp.Clamp(i.TakenBp, CombatConstants.TakenMinBp, CombatConstants.TakenMaxBp));
        v = Bp.Apply(v, i.VarianceBp);
        return (int)Math.Max(1, v / Scale);
    }

    /// <summary>命中率（万分比）。</summary>
    public static int HitChanceBp(int accuracy, int evasion, int bonusBp) => Bp.Clamp(
        CombatConstants.HitBaseBp + (accuracy - evasion) * CombatConstants.HitPerPointBp + bonusBp,
        CombatConstants.HitMinBp,
        CombatConstants.HitMaxBp);

    public static int CritChanceBp(int critBp) => Bp.Clamp(critBp, 0, CombatConstants.CritCapBp);

    /// <summary>克制倍率：所有匹配规则相乘后截断到 0.8–1.25。</summary>
    public static int CounterBp(SkillDefinition skill, CombatantTemplate target, IReadOnlyList<CounterRule> rules)
    {
        long bp = Bp.One;
        foreach (var r in rules)
        {
            if (skill.HasTag(r.SkillTag) && target.HasTag(r.TargetTag))
            {
                bp = Bp.Apply(bp, r.Bp);
            }
        }

        return (int)Bp.Clamp(bp, CombatConstants.CounterMinBp, CombatConstants.CounterMaxBp);
    }
}
