namespace WuxiaWorld.Domain.Combat.Definitions;

/// <summary>阵位：每方 2 排 × 3 槽，Row 0 前排、1 后排（架构文档 7.1）。</summary>
public readonly record struct Position(int Row, int Slot)
{
    public const int Rows = 2;
    public const int Slots = 3;

    public bool IsValid => Row is >= 0 and < Rows && Slot is >= 0 and < Slots;
}

public sealed record EncounterSlot
{
    public required string Template { get; init; }

    /// <summary>场上实例 ID；同一模板多次出场时必须给出，缺省用模板 ID。</summary>
    public string? UnitId { get; init; }

    public int Row { get; init; }
    public int Slot { get; init; }
}

public enum PhaseTriggerKind
{
    /// <summary>指定单位气血低于万分比。</summary>
    UnitHpBelow,

    /// <summary>指定单位倒下。</summary>
    UnitDown,

    /// <summary>到达第 N 轮开始。</summary>
    RoundAtLeast,
}

/// <summary>阶段：条件满足后一次性执行（增援、施加状态、改胜利条件），在轮次开始或行动结束时检查。</summary>
public sealed record EncounterPhase
{
    public required string Id { get; init; }
    public PhaseTriggerKind When { get; init; }
    public string? Unit { get; init; }
    public int Value { get; init; }
    public IReadOnlyList<EncounterSlot> Spawn { get; init; } = [];

    /// <summary>对指定单位施加（或 <see cref="PhaseStatus.Remove"/> 为真时移除）状态。</summary>
    public IReadOnlyList<PhaseStatus> Statuses { get; init; } = [];
}

public sealed record PhaseStatus(string Unit, string Status, bool Remove = false);

/// <summary>开战时单位气血改为上限的万分比（如闸绳已被松动的水门机关）。</summary>
public sealed record VariantHp(string Unit, int Bp);

/// <summary>
/// 遭遇变体：剧情先手（同行者识破伪装、松动闸绳、提前救人等）对同一场遭遇的开局改动。
/// 是否生效由世界事实 <see cref="WhenFact"/> = <see cref="WhenValue"/> 决定，由调用方（世界层）判断后把变体 ID 放进
/// <c>BattleSetup.Variants</c>；内核只按 ID 套用，开战时依次施加状态、调整气血，并发出与阶段相同的提示事件。
/// </summary>
public sealed record EncounterVariant
{
    public required string Id { get; init; }
    public required string WhenFact { get; init; }
    public string WhenValue { get; init; } = "true";
    public IReadOnlyList<PhaseStatus> Statuses { get; init; } = [];
    public IReadOnlyList<VariantHp> Hp { get; init; } = [];
}

public enum VictoryRule
{
    /// <summary>击倒所有计入胜利的敌人。</summary>
    DefeatAll,

    /// <summary>击倒指定单位即胜（首领）。</summary>
    DefeatUnit,

    /// <summary>
    /// 切磋、点到为止：谁也不会倒下（伤害与持续伤害最多压到 1 点气血），气血压到 <see cref="EncounterDefinition.YieldBp"/>
    /// 及以下即算收手认输。我方全部认输为败；<see cref="EncounterDefinition.VictoryUnit"/>（未给时为全部计入胜利的敌人）认输为胜。
    /// </summary>
    Spar,
}

public sealed record EncounterDefinition
{
    public required string Id { get; init; }

    /// <summary>剧情锁定战：不能撤退，界面说明原因（架构文档 7.1）。</summary>
    public bool Locked { get; init; }

    public IReadOnlyList<EncounterSlot> Enemies { get; init; } = [];
    public VictoryRule Victory { get; init; } = VictoryRule.DefeatAll;
    public string? VictoryUnit { get; init; }

    /// <summary>仅 <see cref="VictoryRule.Spar"/>：认输线，气血占上限的万分比（如 3500 为三成五）。</summary>
    public int YieldBp { get; init; }

    /// <summary>只由主角单独上场（一对一切磋等）；队伍里的其他人不入阵。由世界层组队时套用。</summary>
    public bool Solo { get; init; }

    /// <summary>战斗布景的美术 ID（<c>assets/art/battle/&lt;id&gt;</c>）；省略时用默认布景。只是表现，不影响结算。</summary>
    public string? Backdrop { get; init; }

    public IReadOnlyList<EncounterPhase> Phases { get; init; } = [];

    /// <summary>剧情先手造成的开局变体（M3 前置，架构文档 7.6）。</summary>
    public IReadOnlyList<EncounterVariant> Variants { get; init; } = [];

    /// <summary>胜利结算后的经验与修为（战后成长在 M2 的应用事务里一次性提交）。</summary>
    public int Experience { get; init; }

    public int Cultivation { get; init; }
}
