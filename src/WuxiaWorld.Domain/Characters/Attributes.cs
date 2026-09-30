namespace WuxiaWorld.Domain.Characters;

/// <summary>五项基础属性（架构文档 7.4）：体魄、臂力、根骨、身法、悟性。</summary>
public sealed record Attributes(int Physique, int Strength, int Root, int Agility, int Insight)
{
    public int Total => Physique + Strength + Root + Agility + Insight;

    public Attributes Plus(Attributes other) => new(
        Physique + other.Physique,
        Strength + other.Strength,
        Root + other.Root,
        Agility + other.Agility,
        Insight + other.Insight);
}

/// <summary>
/// 战斗派生量。存档不保存派生值，读档后由属性、等级与装配重新计算（架构文档第 11 节）。
/// </summary>
public sealed record StatBlock
{
    public int MaxHp { get; init; }
    public int MaxInner { get; init; }
    public int ExternalAttack { get; init; }
    public int InternalAttack { get; init; }
    public int ExternalDefense { get; init; }
    public int InternalDefense { get; init; }
    public int Speed { get; init; }
    public int Accuracy { get; init; }
    public int Evasion { get; init; }

    /// <summary>暴击率（万分比），判定时再按规则上限截断。</summary>
    public int CritBp { get; init; }

    public int MaxStance { get; init; }

    /// <summary>控制抗性（万分比），从控制类效果的施加几率中扣除。</summary>
    public int ControlResistBp { get; init; }
}

/// <summary>装备、心法、轻功给出的固定加成；全部为加法，不做百分比连乘（架构文档 7.4 末段）。</summary>
public sealed record StatBonus
{
    public static readonly StatBonus None = new();

    public int MaxHp { get; init; }
    public int MaxInner { get; init; }
    public int ExternalAttack { get; init; }
    public int InternalAttack { get; init; }
    public int ExternalDefense { get; init; }
    public int InternalDefense { get; init; }
    public int Speed { get; init; }
    public int Accuracy { get; init; }
    public int Evasion { get; init; }
    public int CritBp { get; init; }
    public int MaxStance { get; init; }
    public int ControlResistBp { get; init; }

    /// <summary>按万分比缩放（阴阳相冲时辅修心法效果减半用），向下取整。</summary>
    public StatBonus Scaled(int bp) => new()
    {
        MaxHp = MaxHp * bp / 10_000,
        MaxInner = MaxInner * bp / 10_000,
        ExternalAttack = ExternalAttack * bp / 10_000,
        InternalAttack = InternalAttack * bp / 10_000,
        ExternalDefense = ExternalDefense * bp / 10_000,
        InternalDefense = InternalDefense * bp / 10_000,
        Speed = Speed * bp / 10_000,
        Accuracy = Accuracy * bp / 10_000,
        Evasion = Evasion * bp / 10_000,
        CritBp = CritBp * bp / 10_000,
        MaxStance = MaxStance * bp / 10_000,
        ControlResistBp = ControlResistBp * bp / 10_000,
    };

    public StatBonus Plus(StatBonus o) => new()
    {
        MaxHp = MaxHp + o.MaxHp,
        MaxInner = MaxInner + o.MaxInner,
        ExternalAttack = ExternalAttack + o.ExternalAttack,
        InternalAttack = InternalAttack + o.InternalAttack,
        ExternalDefense = ExternalDefense + o.ExternalDefense,
        InternalDefense = InternalDefense + o.InternalDefense,
        Speed = Speed + o.Speed,
        Accuracy = Accuracy + o.Accuracy,
        Evasion = Evasion + o.Evasion,
        CritBp = CritBp + o.CritBp,
        MaxStance = MaxStance + o.MaxStance,
        ControlResistBp = ControlResistBp + o.ControlResistBp,
    };
}

/// <summary>机关、木桩等不按属性推导的单位直接给出的数值；非空字段覆盖推导结果。</summary>
public sealed record StatOverride
{
    public int? MaxHp { get; init; }
    public int? MaxInner { get; init; }
    public int? ExternalDefense { get; init; }
    public int? InternalDefense { get; init; }
    public int? Speed { get; init; }
    public int? Evasion { get; init; }
    public int? MaxStance { get; init; }
}
