using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Domain.Common;

namespace WuxiaWorld.Domain.Combat;

public sealed record AllyEntry(CombatantTemplate Template, string UnitId, Position Position);

/// <summary>开战输入：遭遇、己方队伍（主角 + 至多 3 名伙伴）、可用物品与战斗随机种子。</summary>
public sealed record BattleSetup
{
    public const int MaxAllies = 4;
    public const int MaxEnemies = 6;

    public required string EncounterId { get; init; }
    public IReadOnlyList<AllyEntry> Allies { get; init; } = [];
    public IReadOnlyDictionary<string, int> Items { get; init; } = new Dictionary<string, int>();
    public ulong Seed { get; init; }

    /// <summary>生效的遭遇变体 ID（按遭遇定义里的次序套用）；未定义的 ID 开战时报错。</summary>
    public IReadOnlyList<string> Variants { get; init; } = [];
}

public sealed record CommandResult(bool Accepted, string? Rejection, IReadOnlyList<BattleEvent> Events)
{
    public static CommandResult Reject(string reason) => new(false, reason, []);
}

/// <summary>规则内核自检失败（反应链超限、无法推进等）：附诊断快照哈希，调用方应保存战斗记录以便重放（架构文档 7.5）。</summary>
public sealed class BattleInvariantException(string message, string stateHash) : Exception($"{message}（状态哈希 {stateHash}）")
{
    public string StateHash { get; } = stateHash;
}

/// <summary>
/// 确定性战斗内核：<c>BattleState + BattleCommand + 随机状态 → 新状态 + 事件</c>（架构文档 7.5）。
/// 行动流程见架构文档 7.3：行动开始状态 → 检查存活及控制 → 等待命令 → 校验 → 原子扣费 → 效果事件
/// → 有上限的反应 → 倒下与胜负检查 → 行动结束状态 → 冷却与持续时间更新 → 下一角色。
/// 内核不区分玩家与 AI：每名待行动单位都停在 <see cref="BattleState.Pending"/>，由调用方提交命令。
/// </summary>
public sealed partial class BattleEngine(CombatContent content)
{
    private readonly CombatContent _content = content;

    public CombatContent Content => _content;

    public (BattleState State, IReadOnlyList<BattleEvent> Events) Start(BattleSetup setup)
    {
        var encounter = _content.Encounter(setup.EncounterId);
        if (setup.Allies.Count is 0 or > BattleSetup.MaxAllies)
        {
            throw new ArgumentException($"己方须为 1–{BattleSetup.MaxAllies} 人。", nameof(setup));
        }

        if (encounter.Enemies.Count is 0 or > BattleSetup.MaxEnemies)
        {
            throw new ArgumentException($"敌方须为 1–{BattleSetup.MaxEnemies} 人。", nameof(setup));
        }

        var state = new BattleState
        {
            EncounterId = encounter.Id,
            Locked = encounter.Locked,
            Victory = encounter.Victory,
            VictoryUnit = encounter.VictoryUnit,
            Rng = new Pcg32(setup.Seed, CombatConstants.BattleStream),
        };

        foreach (var ally in setup.Allies)
        {
            AddUnit(state, CombatantFactory.Create(ally.Template, ally.UnitId, Side.Ally, ally.Position, _content));
        }

        foreach (var slot in encounter.Enemies)
        {
            var template = _content.Combatant(slot.Template);
            AddUnit(state, CombatantFactory.Create(template, slot.UnitId ?? slot.Template, Side.Enemy, new Position(slot.Row, slot.Slot), _content));
        }

        foreach (var (item, count) in setup.Items.OrderBy(i => i.Key, StringComparer.Ordinal))
        {
            if (count > 0)
            {
                _ = _content.Item(item);
                state.Items[item] = count;
            }
        }

        var ctx = new Ctx(state, _content);
        ApplyVariants(ctx, encounter, setup.Variants);
        StartRound(ctx);
        AdvanceToNextActor(ctx);
        return (state, ctx.Events);
    }

    /// <summary>开局套用遭遇变体：按遭遇定义的次序，先调气血、再施加状态，每个变体发一条阶段提示。</summary>
    private void ApplyVariants(Ctx ctx, EncounterDefinition encounter, IReadOnlyList<string> ids)
    {
        foreach (var id in ids)
        {
            if (!encounter.Variants.Any(v => v.Id == id))
            {
                throw new ArgumentException($"遭遇 {encounter.Id} 没有变体 {id}。");
            }
        }

        foreach (var variant in encounter.Variants.Where(v => ids.Contains(v.Id, StringComparer.Ordinal)))
        {
            ctx.Emit(new PhaseTriggered(variant.Id));
            foreach (var hp in variant.Hp)
            {
                if (ctx.State.TryUnit(hp.Unit) is { } unit)
                {
                    unit.Hp = Math.Max(1, (int)Bp.Apply(unit.Stats.MaxHp, hp.Bp));
                }
            }

            foreach (var ps in variant.Statuses)
            {
                if (ctx.State.TryUnit(ps.Unit) is { } unit)
                {
                    ApplyStatusDirect(ctx, unit, _content.Status(ps.Status), null, 0, 1);
                }
            }
        }
    }

    private static void AddUnit(BattleState state, BattleUnit unit)
    {
        if (!unit.Position.IsValid)
        {
            throw new ArgumentException($"{unit.Id} 的阵位 {unit.Position} 越界。");
        }

        if (state.Units.Exists(u => u.Id == unit.Id))
        {
            throw new ArgumentException($"场上单位 ID 重复：{unit.Id}");
        }

        if (state.Units.Exists(u => u.Side == unit.Side && u.Position == unit.Position))
        {
            throw new ArgumentException($"{unit.Id} 的阵位 {unit.Position} 已被占用。");
        }

        state.Units.Add(unit);
        state.Units.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
    }

    /// <summary>校验命令；合法返回 null，否则返回给玩家看的原因。校验不改状态、不消耗随机数。</summary>
    public string? Validate(BattleState state, BattleCommand command)
    {
        if (state.Outcome != BattleOutcome.Ongoing)
        {
            return "战斗已结束。";
        }

        if (state.Pending != command.Actor)
        {
            return "还没轮到该角色行动。";
        }

        var actor = state.Unit(command.Actor);
        switch (command)
        {
            case UseSkill use:
            {
                if (!_content.Skills.TryGetValue(use.SkillId, out var skill))
                {
                    return "未知招式。";
                }

                if (use.SkillId != CoreIds.BasicAttack && !actor.Skills.Contains(use.SkillId, StringComparer.Ordinal))
                {
                    return "未装配该招式。";
                }

                if (actor.CooldownOf(use.SkillId) > 0)
                {
                    return $"冷却中，还需 {actor.CooldownOf(use.SkillId)} 次行动。";
                }

                if (actor.Inner < skill.InnerCost)
                {
                    return "内力不足。";
                }

                if (state.Momentum(actor.Side) < skill.MomentumCost)
                {
                    return "势不足。";
                }

                var targets = Targeting.Resolve(state, actor, skill.TargetRule, use.Target);
                if (targets is null)
                {
                    return "目标不可选。";
                }

                if (targets.Count == 0 && skill.TargetRule != TargetRule.Self)
                {
                    return "没有可选目标。";
                }

                return null;
            }

            case UseItem item:
            {
                if (!state.Items.TryGetValue(item.ItemId, out var count) || count <= 0)
                {
                    return "没有该物品。";
                }

                var def = _content.Item(item.ItemId);
                var targets = Targeting.Resolve(state, actor, def.TargetRule, item.Target);
                if (targets is null || targets.Count == 0)
                {
                    return "目标不可选。";
                }

                if (!targets.Any(t => def.Effects.Any(e => WouldChange(e, t))))
                {
                    return "用了也没有效果（气血、内力已满或没有可驱散的状态）。";
                }

                return null;
            }

            case Swap swap:
            {
                var to = new Position(swap.Row, swap.Slot);
                if (!to.IsValid)
                {
                    return "阵位越界。";
                }

                return to == actor.Position ? "已在该位置。" : null;
            }

            case Retreat:
                return state.Locked ? "剧情战不能撤退。" : null;

            case Defend or Meditate:
                return null;

            default:
                return "未知命令。";
        }
    }

    /// <summary>物品效果对目标是否会产生变化（满血、满内力、无可驱散状态时不白白用掉物品，开发计划 T04）。</summary>
    private bool WouldChange(EffectDefinition e, BattleUnit t) => e.Type switch
    {
        EffectType.Heal => t.Hp < t.Stats.MaxHp,
        EffectType.RestoreInner => t.Inner < t.Stats.MaxInner,
        EffectType.RestoreStance => t.Stance < t.Stats.MaxStance,
        EffectType.Cleanse => t.Statuses.Exists(st => _content.Status(st.StatusId).HasTag(e.Tag!)),
        _ => true,
    };

    /// <summary>提交当前行动者的命令。非法命令不改状态、不扣费、不消耗随机数。</summary>
    public CommandResult Submit(BattleState state, BattleCommand command)
    {
        var rejection = Validate(state, command);
        if (rejection is not null)
        {
            return CommandResult.Reject(rejection);
        }

        var ctx = new Ctx(state, _content);
        var actor = state.Unit(command.Actor);
        state.Pending = null;
        ctx.Begin(actor, Stage.Action);
        Execute(ctx, actor, command);
        state.CommandCount++;
        FinishTurn(ctx, actor);
        AdvanceToNextActor(ctx);
        return new CommandResult(true, null, ctx.Events);
    }

    // ── 回合流程 ─────────────────────────────────────────

    private void StartRound(Ctx ctx)
    {
        var s = ctx.State;
        s.Round++;
        foreach (var u in s.Units)
        {
            u.ReactionUsed = false;
        }

        CheckPhases(ctx);
        // 速度高者先；同速身法高者先；再同按稳定 ID（架构文档 7.1、7.6）。
        var order = s.Units.Where(u => !u.IsDown)
            .OrderByDescending(u => EffectiveSpeed(u))
            .ThenByDescending(u => u.Template.Attributes.Agility)
            .ThenBy(u => u.Id, StringComparer.Ordinal)
            .Select(u => u.Id)
            .ToList();
        s.Order.Clear();
        s.Order.AddRange(order);
        s.TurnIndex = -1;
        ctx.Emit(new RoundStarted(s.Round, order));
    }

    public int EffectiveSpeed(BattleUnit u)
    {
        var mod = u.Statuses.Sum(st => _content.Status(st.StatusId).SpeedBp * st.Stacks);
        return (int)Math.Max(1, Bp.Apply(u.Stats.Speed, Bp.One + mod));
    }

    private void AdvanceToNextActor(Ctx ctx)
    {
        var s = ctx.State;
        var guard = 0;
        while (s.Outcome == BattleOutcome.Ongoing)
        {
            if (++guard > 10_000)
            {
                throw new BattleInvariantException("战斗无法推进到下一名可行动单位", s.Hash());
            }

            s.TurnIndex++;
            if (s.TurnIndex >= s.Order.Count)
            {
                StartRound(ctx);
                continue;
            }

            var actor = s.TryUnit(s.Order[s.TurnIndex]);
            if (actor is null || actor.IsDown)
            {
                continue;
            }

            BeginTurn(ctx, actor);
            if (s.Outcome != BattleOutcome.Ongoing)
            {
                break;
            }

            if (actor.IsDown)
            {
                continue;
            }

            var control = actor.Statuses.Find(st => _content.Status(st.StatusId).SkipAction);
            if (control is not null)
            {
                ctx.Emit(new ActionSkipped(actor.Id, control.StatusId));
                FinishTurn(ctx, actor);
                continue;
            }

            if (actor.FindStatus(CoreIds.Charging) is { } charge)
            {
                ctx.Begin(actor, Stage.Action);
                ReleaseCharge(ctx, actor, charge);
                FinishTurn(ctx, actor);
                continue;
            }

            s.Pending = actor.Id;
            return;
        }

        s.Pending = null;
    }

    private void BeginTurn(Ctx ctx, BattleUnit actor)
    {
        ctx.Begin(actor, Stage.Start);
        ctx.Emit(new TurnStarted(actor.Id));

        // 以施加者行动为时点的状态（护援）：本单位开始行动时，消耗它施加在各处的这类状态。
        foreach (var other in ctx.State.Units.Where(u => !u.IsDown).ToList())
        {
            foreach (var st in other.Statuses.Where(x => x.SourceId == actor.Id
                         && _content.Status(x.StatusId).Tick == StatusTick.SourceActionStart).ToList())
            {
                ConsumeTick(ctx, other, st);
            }
        }
        foreach (var st in actor.Statuses.ToList())
        {
            var def = _content.Status(st.StatusId);
            if (def.Tick != StatusTick.OwnerActionStart || !actor.Statuses.Contains(st))
            {
                continue;
            }

            if (def.TickDamageBp > 0 && !actor.IsDown)
            {
                var dmg = (int)Math.Max(1, Bp.Apply((long)actor.Stats.MaxHp * st.Stacks, def.TickDamageBp));
                actor.Hp = Math.Max(0, actor.Hp - dmg);
                ctx.Emit(new StatusTicked(actor.Id, st.StatusId, dmg, actor.Hp));
                if (actor.IsDown)
                {
                    Down(ctx, actor);
                    break;
                }
            }

            ConsumeTick(ctx, actor, st);
        }

        if (!actor.IsDown)
        {
            var regen = actor.Arts.Sum(a => a.InnerPerAction);
            if (regen > 0)
            {
                RestoreInner(ctx, actor, regen);
            }
        }

        CheckPhases(ctx);
        CheckOutcome(ctx);
    }

    private void FinishTurn(Ctx ctx, BattleUnit actor)
    {
        if (!actor.IsDown)
        {
            ctx.Begin(actor, Stage.End);
            foreach (var st in actor.Statuses.ToList())
            {
                if (_content.Status(st.StatusId).Tick == StatusTick.OwnerActionEnd && actor.Statuses.Contains(st))
                {
                    ConsumeTick(ctx, actor, st);
                }
            }

            // 冷却 N：施放当次不递减（架构文档 7.3）。
            foreach (var skill in actor.Cooldowns.Keys.ToList())
            {
                if (ctx.JustCooled.Contains(skill))
                {
                    continue;
                }

                if (--actor.Cooldowns[skill] <= 0)
                {
                    actor.Cooldowns.Remove(skill);
                }
            }
        }

        ctx.Begin(null, Stage.None);
        CheckPhases(ctx);
        CheckOutcome(ctx);
    }

    private void ConsumeTick(Ctx ctx, BattleUnit unit, StatusInstance st)
    {
        if (st.SkipNextTick)
        {
            st.SkipNextTick = false;
            return;
        }

        if (st.Remaining > 0 && --st.Remaining == 0)
        {
            EndStatus(ctx, unit, st, StatusEndReason.Expired);
        }
    }

    private static void CheckOutcome(Ctx ctx)
    {
        var s = ctx.State;
        if (s.Outcome != BattleOutcome.Ongoing)
        {
            return;
        }

        BattleOutcome outcome;
        if (!s.Living(Side.Ally).Any())
        {
            // 双方同时失去战斗能力按失败处理（架构文档 7.3）。
            outcome = BattleOutcome.Defeat;
        }
        else if (s.Victory == VictoryRule.DefeatUnit && s.TryUnit(s.VictoryUnit) is { IsDown: true })
        {
            outcome = BattleOutcome.Victory;
        }
        else if (s.Victory == VictoryRule.DefeatAll && !s.Living(Side.Enemy).Any(u => u.Template.CountsForVictory))
        {
            outcome = BattleOutcome.Victory;
        }
        else
        {
            return;
        }

        End(ctx, outcome);
    }

    private static void End(Ctx ctx, BattleOutcome outcome)
    {
        ctx.State.Outcome = outcome;
        ctx.State.Pending = null;
        ctx.Emit(new BattleEnded(outcome));
    }

    private void CheckPhases(Ctx ctx)
    {
        var s = ctx.State;
        if (s.Outcome != BattleOutcome.Ongoing)
        {
            return;
        }

        foreach (var phase in _content.Encounter(s.EncounterId).Phases)
        {
            if (s.FiredPhases.Contains(phase.Id))
            {
                continue;
            }

            var unit = s.TryUnit(phase.Unit);
            var met = phase.When switch
            {
                PhaseTriggerKind.UnitHpBelow => unit is { IsDown: false }
                    && (long)unit.Hp * Bp.One < (long)unit.Stats.MaxHp * phase.Value,
                PhaseTriggerKind.UnitDown => unit is { IsDown: true },
                PhaseTriggerKind.RoundAtLeast => s.Round >= phase.Value,
                _ => false,
            };
            if (!met)
            {
                continue;
            }

            s.FiredPhases.Add(phase.Id);
            ctx.Emit(new PhaseTriggered(phase.Id));
            foreach (var spawn in phase.Spawn)
            {
                Spawn(ctx, spawn);
            }

            foreach (var ps in phase.Statuses)
            {
                if (s.TryUnit(ps.Unit) is not { IsDown: false } target)
                {
                    continue;
                }

                if (!ps.Remove)
                {
                    ApplyStatusDirect(ctx, target, _content.Status(ps.Status), null, 0, 1);
                }
                else if (target.FindStatus(ps.Status) is { } existing)
                {
                    EndStatus(ctx, target, existing, StatusEndReason.Cleansed);
                }
            }
        }
    }

    private void Spawn(Ctx ctx, EncounterSlot slot)
    {
        var s = ctx.State;
        var id = slot.UnitId ?? slot.Template;
        if (s.TryUnit(id) is not null)
        {
            return;
        }

        var wanted = new Position(slot.Row, slot.Slot);
        bool Free(Position p) => !s.Units.Exists(u => u.Side == Side.Enemy && u.Position == p && !u.IsDown);
        var position = Free(wanted)
            ? wanted
            : Enumerable.Range(0, Position.Rows * Position.Slots)
                .Select(i => new Position((slot.Row + i / Position.Slots) % Position.Rows, i % Position.Slots))
                .FirstOrDefault(Free, new Position(-1, -1));
        if (!position.IsValid || s.Living(Side.Enemy).Count() >= BattleSetup.MaxEnemies)
        {
            return;
        }

        // 同位上的倒下者让出位置（移出场地，不再参与排序与目标）。
        s.Units.RemoveAll(u => u.Side == Side.Enemy && u.Position == position && u.IsDown);
        var unit = CombatantFactory.Create(_content.Combatant(slot.Template), id, Side.Enemy, position, _content);
        AddUnit(s, unit);
        ctx.Emit(new UnitSpawned(id, Side.Enemy, position));
    }

    // ── 上下文 ───────────────────────────────────────────

    private enum Stage
    {
        None,
        Start,
        Action,
        End,
    }

    private sealed class Ctx(BattleState state, CombatContent content)
    {
        public BattleState State { get; } = state;
        public CombatContent Content { get; } = content;
        public List<BattleEvent> Events { get; } = [];
        public string? ActorId { get; private set; }
        public Stage Stage { get; private set; }

        /// <summary>本次行动刚设定的冷却，行动结束时不递减。</summary>
        public HashSet<string> JustCooled { get; } = new(StringComparer.Ordinal);

        public void Begin(BattleUnit? actor, Stage stage)
        {
            if (actor?.Id != ActorId)
            {
                JustCooled.Clear();
            }

            ActorId = actor?.Id;
            Stage = stage;
        }

        public void Emit(BattleEvent e)
        {
            Events.Add(e);
            if (Events.Count > CombatConstants.EventCapPerCommand)
            {
                throw new BattleInvariantException($"单条命令产生的事件超过 {CombatConstants.EventCapPerCommand} 条", State.Hash());
            }
        }
    }
}
