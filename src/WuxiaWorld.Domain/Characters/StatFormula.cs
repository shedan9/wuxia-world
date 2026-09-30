namespace WuxiaWorld.Domain.Characters;

/// <summary>
/// v0 派生公式（架构文档 7.4）。气血、内力、外功、内功、速度、命中率沿用文档原式；
/// 外防、内防、命中、闪避、暴击、架势、控制抗性为 M1 补充的 v0 取值，均属配置，随模拟调整。
/// </summary>
public static class StatFormula
{
    /// <summary>规则版本：公式或常数一改即递增，并写入战斗记录与存档（架构文档 12.3）。2：同速先比身法；新增“穿透一列”选目标规则。</summary>
    public const int RulesetVersion = 2;

    public const int MaxLevel = 8;

    /// <summary>每升一级获得的潜能点。</summary>
    public const int PotentialPerLevel = 3;

    public static StatBlock Derive(int level, Attributes a, StatBonus bonus)
    {
        return new StatBlock
        {
            MaxHp = 180 + 22 * a.Physique + 10 * level + bonus.MaxHp,
            MaxInner = 80 + 18 * a.Root + 6 * level + bonus.MaxInner,
            ExternalAttack = 20 + 3 * a.Strength + 2 * level + bonus.ExternalAttack,
            InternalAttack = 20 + 3 * a.Root + 2 * level + bonus.InternalAttack,
            ExternalDefense = 10 + 2 * a.Physique + level + bonus.ExternalDefense,
            InternalDefense = 10 + 2 * a.Root + level + bonus.InternalDefense,
            Speed = 30 + 2 * a.Agility + bonus.Speed,
            Accuracy = 100 + 2 * a.Insight + 2 * level + bonus.Accuracy,
            Evasion = 50 + 2 * a.Agility + level + bonus.Evasion,
            CritBp = 500 + 30 * a.Insight + bonus.CritBp,
            MaxStance = 40 + 2 * a.Physique + bonus.MaxStance,
            ControlResistBp = 40 * a.Root + bonus.ControlResistBp,
        };
    }

    public static StatBlock Apply(StatBlock s, StatOverride? o)
    {
        if (o is null)
        {
            return s;
        }

        return s with
        {
            MaxHp = o.MaxHp ?? s.MaxHp,
            MaxInner = o.MaxInner ?? s.MaxInner,
            ExternalDefense = o.ExternalDefense ?? s.ExternalDefense,
            InternalDefense = o.InternalDefense ?? s.InternalDefense,
            Speed = o.Speed ?? s.Speed,
            Evasion = o.Evasion ?? s.Evasion,
            MaxStance = o.MaxStance ?? s.MaxStance,
        };
    }

    /// <summary>某等级累计可分配的潜能点（1 级为 0）。成长预算相同的比较以此为准（开发计划 6.2）。</summary>
    public static int PotentialAt(int level) => Math.Max(0, level - 1) * PotentialPerLevel;
}
