namespace WuxiaWorld.Domain.Combat;

/// <summary>主行动命令（架构文档 7.1）。表现层与 AI 只能提交命令，不能直接改状态。</summary>
public abstract record BattleCommand(string Actor);

/// <summary>施展招式；普通攻击为 <see cref="Definitions.CoreIds.BasicAttack"/>。群体或自身招式的目标可为空。</summary>
public sealed record UseSkill(string Actor, string SkillId, string? Target) : BattleCommand(Actor);

public sealed record Defend(string Actor) : BattleCommand(Actor);

public sealed record Meditate(string Actor) : BattleCommand(Actor);

public sealed record UseItem(string Actor, string ItemId, string? Target) : BattleCommand(Actor);

/// <summary>换位：移到本方空位，或与该位置的同伴互换；消耗主行动。</summary>
public sealed record Swap(string Actor, int Row, int Slot) : BattleCommand(Actor);

public sealed record Retreat(string Actor) : BattleCommand(Actor);
