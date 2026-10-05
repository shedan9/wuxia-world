using WuxiaWorld.Domain.Combat.Definitions;

namespace WuxiaWorld.Domain.Combat;

/// <summary>
/// 已发生的战斗事实。表现层按次序消费事件播放动作、飘字与日志；动画不反向决定结果（架构文档 7.5）。
/// </summary>
public abstract record BattleEvent;

public sealed record RoundStarted(int Round, IReadOnlyList<string> Order) : BattleEvent;

public sealed record TurnStarted(string Actor) : BattleEvent;

public sealed record StatusTicked(string Unit, string StatusId, int Damage, int HpAfter) : BattleEvent;

public sealed record ActionSkipped(string Actor, string StatusId) : BattleEvent;

public sealed record SkillUsed(string Actor, string SkillId, IReadOnlyList<string> Targets, int InnerCost, int MomentumCost, bool IsReaction)
    : BattleEvent;

/// <summary>蓄力：亮出下一次行动的意图（首领预兆）。</summary>
public sealed record ChargeStarted(string Actor, string SkillId, string? Target) : BattleEvent;

public sealed record ChargeInterrupted(string Actor, string SkillId) : BattleEvent;

public sealed record Missed(string Source, string Target, string SkillId) : BattleEvent;

public sealed record Damaged(string Source, string Target, int Amount, DamageKind Kind, bool Crit, int HpAfter) : BattleEvent;

public sealed record StanceDamaged(string Source, string Target, int Amount, int StanceAfter) : BattleEvent;

public sealed record StanceBroken(string Target, string Source) : BattleEvent;

public sealed record Healed(string Source, string Target, int Amount, int HpAfter) : BattleEvent;

public sealed record InnerChanged(string Unit, int Delta, int InnerAfter) : BattleEvent;

public sealed record StanceRestored(string Unit, int Amount, int StanceAfter) : BattleEvent;

public sealed record StatusApplied(string Target, string StatusId, int Stacks, int Remaining, string? Source) : BattleEvent;

public sealed record StatusResisted(string Target, string StatusId) : BattleEvent;

public enum StatusEndReason
{
    Expired,
    Consumed,
    Cleansed,
    Downed,
    Released,
    Interrupted,
}

public sealed record StatusRemoved(string Target, string StatusId, StatusEndReason Reason) : BattleEvent;

/// <summary>首领的控制积累（未到阈值）。</summary>
public sealed record ControlAccumulated(string Target, string StatusId, int Meter, int Threshold) : BattleEvent;

public sealed record MomentumChanged(Side Side, int Value) : BattleEvent;

/// <summary>护援：<paramref name="Guardian"/> 替 <paramref name="Protected"/> 挡下攻击。</summary>
public sealed record GuardIntercepted(string Guardian, string Protected, string Attacker) : BattleEvent;

public sealed record Parried(string Unit, int StanceAfter) : BattleEvent;

public sealed record Defended(string Actor) : BattleEvent;

public sealed record Meditated(string Actor) : BattleEvent;

public sealed record ItemUsed(string Actor, string ItemId, string? Target, int Remaining) : BattleEvent;

public sealed record Swapped(string Actor, Position From, Position To, string? Other) : BattleEvent;

public sealed record RetreatAttempted(string Actor, int ChanceBp, bool Success) : BattleEvent;

public sealed record UnitDowned(string Unit) : BattleEvent;

/// <summary>切磋中气血压到认输线，收手（随后紧跟 <see cref="BattleEnded"/>）。</summary>
public sealed record UnitYielded(string Unit) : BattleEvent;

public sealed record PhaseTriggered(string PhaseId) : BattleEvent;

public sealed record UnitSpawned(string Unit, Side Side, Position Position) : BattleEvent;

/// <summary>机关抬一层（水位等）；<paramref name="Released"/> 为真表示满层发作并清零。</summary>
public sealed record LevelRaised(string Unit, string StatusId, int Level, int Max, bool Released) : BattleEvent;

public sealed record BattleEnded(BattleOutcome Outcome) : BattleEvent;
