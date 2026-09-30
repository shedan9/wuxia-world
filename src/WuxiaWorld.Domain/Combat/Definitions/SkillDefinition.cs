namespace WuxiaWorld.Domain.Combat.Definitions;

/// <summary>选目标规则（架构文档 7.1、9.2 <c>target_rule</c>）。</summary>
public enum TargetRule
{
    /// <summary>近身单体：对方前排还有可战者时只能打前排。</summary>
    SingleReachableEnemy,

    /// <summary>远程 / 穿透单体：可打任意敌人。</summary>
    SingleAnyEnemy,

    /// <summary>可接触的一排（前排有人打前排，否则后排）全体。</summary>
    ReachableRowEnemies,

    /// <summary>穿透一列：任选一名敌人，命中其所在槽位前后两排的全部敌人（前排不挡）。</summary>
    ColumnEnemies,

    AllEnemies,
    Self,

    /// <summary>己方单体，含自己。</summary>
    SingleAlly,

    /// <summary>己方单体，不含自己。</summary>
    OtherAlly,

    AllAllies,
}

public enum DamageKind
{
    /// <summary>外招：外功对外防。</summary>
    External,

    /// <summary>内劲：内功对内防。</summary>
    Internal,
}

public enum EffectType
{
    Damage,
    StanceDamage,
    ApplyStatus,

    /// <summary>驱散带指定标签的状态，按施加先后移除 <c>Count</c> 个。</summary>
    Cleanse,

    Heal,
    RestoreInner,
    RestoreStance,
    GainMomentum,

    /// <summary>施放者对目标施加护援：目标下次受到的单体攻击由施放者代受（占用施放者的反应）。</summary>
    Guard,

    /// <summary>机关特效：给施放者叠一层 <c>Status</c>（如水位），叠满时对敌方可接触一排造成伤害。</summary>
    RaiseLevel,
}

public enum EffectTarget
{
    /// <summary>技能所选目标（群体技能为每个目标）。</summary>
    Target,

    Self,
}

/// <summary>
/// 招式效果原语。未用到的字段保持默认值；新原语需要代码，常规新招式只需数据（架构文档 8.2）。
/// </summary>
public sealed record EffectDefinition
{
    public EffectType Type { get; init; }
    public EffectTarget Applies { get; init; } = EffectTarget.Target;

    // 伤害
    public DamageKind Kind { get; init; }
    public int ScaleBp { get; init; }
    public int Fixed { get; init; }

    /// <summary>目标带此状态时伤害额外提高 <see cref="BonusBp"/>（加法并入攻方增伤层）。</summary>
    public string? BonusVsStatus { get; init; }

    public int BonusBp { get; init; }

    /// <summary>按施放者身上此状态的层数，每层伤害提高 <see cref="PerStackBp"/>（水位加成等）。</summary>
    public string? ScaleByStatus { get; init; }

    public int PerStackBp { get; init; }

    // 架势 / 治疗 / 内力 / 势 / 驱散数量
    public int Amount { get; init; }

    /// <summary>治疗按内功的万分比倍率。</summary>
    public int AmountScaleBp { get; init; }

    // 状态
    public string? Status { get; init; }

    /// <summary>0 表示用状态定义里的默认持续次数。</summary>
    public int Duration { get; init; }

    public int Stacks { get; init; } = 1;

    public int ChanceBp { get; init; } = 10_000;

    // 驱散
    public string? Tag { get; init; }

    public int Count { get; init; } = 1;
}

public sealed record SkillDefinition
{
    public required string Id { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public int InnerCost { get; init; }
    public int MomentumCost { get; init; }
    public TargetRule TargetRule { get; init; }

    /// <summary>冷却 N：施放后接下来 N 次自身行动不可再用（架构文档 7.3）。</summary>
    public int Cooldown { get; init; }

    public IReadOnlyList<EffectDefinition> Effects { get; init; } = [];

    /// <summary>蓄力招：施放时只进入蓄力并亮出意图，下一次自身行动才真正出手（首领预兆，架构文档 7.1）。</summary>
    public bool Charged { get; init; }

    /// <summary>必中：不做命中判定（机关、剧情招式与规则测试用）。</summary>
    public bool SureHit { get; init; }

    public string? AnimationId { get; init; }

    public bool HasTag(string tag) => Tags.Contains(tag, StringComparer.Ordinal);

    public bool TargetsEnemies => TargetRule is TargetRule.SingleReachableEnemy or TargetRule.SingleAnyEnemy
        or TargetRule.ReachableRowEnemies or TargetRule.ColumnEnemies or TargetRule.AllEnemies;

    public bool IsSingleTarget => TargetRule is TargetRule.SingleReachableEnemy or TargetRule.SingleAnyEnemy
        or TargetRule.SingleAlly or TargetRule.OtherAlly;

    /// <summary>近身招：会触发反击。</summary>
    public bool IsMelee => TargetRule is TargetRule.SingleReachableEnemy or TargetRule.ReachableRowEnemies;
}
