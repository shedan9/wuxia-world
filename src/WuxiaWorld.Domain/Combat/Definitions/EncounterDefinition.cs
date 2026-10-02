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

public enum VictoryRule
{
    /// <summary>击倒所有计入胜利的敌人。</summary>
    DefeatAll,

    /// <summary>击倒指定单位即胜（首领）。</summary>
    DefeatUnit,
}

public sealed record EncounterDefinition
{
    public required string Id { get; init; }

    /// <summary>剧情锁定战：不能撤退，界面说明原因（架构文档 7.1）。</summary>
    public bool Locked { get; init; }

    public IReadOnlyList<EncounterSlot> Enemies { get; init; } = [];
    public VictoryRule Victory { get; init; } = VictoryRule.DefeatAll;
    public string? VictoryUnit { get; init; }
    public IReadOnlyList<EncounterPhase> Phases { get; init; } = [];

    /// <summary>胜利结算后的经验与修为（战后成长在 M2 的应用事务里一次性提交）。</summary>
    public int Experience { get; init; }

    public int Cultivation { get; init; }
}
