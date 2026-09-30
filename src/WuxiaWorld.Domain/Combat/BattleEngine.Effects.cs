using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Domain.Common;

namespace WuxiaWorld.Domain.Combat;

public sealed partial class BattleEngine
{
    /// <summary>套用一个效果原语，返回造成的气血伤害（其余效果为 0）。<paramref name="skill"/> 为 null 表示物品。</summary>
    private int ApplyEffect(Ctx ctx, BattleUnit actor, BattleUnit target, SkillDefinition? skill, EffectDefinition e, int powerBp)
    {
        var s = ctx.State;
        switch (e.Type)
        {
            case EffectType.Damage:
            {
                var crit = s.Rng.RollBp(DamageMath.CritChanceBp(actor.Stats.CritBp));
                var variance = s.Rng.NextInclusive(CombatConstants.VarianceMinBp, CombatConstants.VarianceMaxBp);
                var amount = DamageMath.Compute(DamageInputs(actor, target, skill, e, powerBp, crit, variance));
                return DealDamage(ctx, actor, target, amount, e.Kind, crit);
            }

            case EffectType.StanceDamage:
                DamageStance(ctx, actor, target, (int)Bp.Apply(e.Amount, powerBp));
                return 0;

            case EffectType.ApplyStatus:
                TryApplyStatus(ctx, actor, target, e.Status!, e.Duration, e.Stacks, e.ChanceBp);
                return 0;

            case EffectType.Cleanse:
            {
                var removed = 0;
                foreach (var st in target.Statuses.ToList())
                {
                    if (removed >= e.Count)
                    {
                        break;
                    }

                    if (_content.Status(st.StatusId).HasTag(e.Tag!))
                    {
                        EndStatus(ctx, target, st, StatusEndReason.Cleansed);
                        removed++;
                    }
                }

                return 0;
            }

            case EffectType.Heal:
            {
                var raw = e.Amount + Bp.Apply(actor.Stats.InternalAttack, e.AmountScaleBp);
                var amount = (int)Math.Min(Bp.Apply(raw, powerBp), target.Stats.MaxHp - target.Hp);
                target.Hp += amount;
                ctx.Emit(new Healed(actor.Id, target.Id, amount, target.Hp));
                return 0;
            }

            case EffectType.RestoreInner:
                RestoreInner(ctx, target, e.Amount);
                return 0;

            case EffectType.RestoreStance:
                RestoreStance(ctx, target, e.Amount);
                return 0;

            case EffectType.GainMomentum:
                AddMomentum(ctx, actor.Side, e.Amount);
                return 0;

            case EffectType.Guard:
                if (target.Id != actor.Id)
                {
                    ApplyStatusDirect(ctx, target, _content.Status(CoreIds.Guarded), actor.Id, e.Duration, 1);
                }

                return 0;

            case EffectType.RaiseLevel:
                RaiseLevel(ctx, actor, e);
                return 0;

            default:
                throw new ArgumentOutOfRangeException(nameof(e));
        }
    }

    /// <summary>伤害计算的输入（预览与实际结算共用，保证预估与结果同源）。</summary>
    public DamageMath.Inputs DamageInputs(BattleUnit actor, BattleUnit target, SkillDefinition? skill, EffectDefinition e, int powerBp, bool crit, int variance)
    {
        var external = e.Kind == DamageKind.External;
        var dealt = actor.Statuses.Sum(st => _content.Status(st.StatusId).DamageDealtBp * st.Stacks);
        if (e.BonusVsStatus is not null && target.HasStatus(e.BonusVsStatus))
        {
            dealt += e.BonusBp;
        }

        if (e.ScaleByStatus is not null)
        {
            dealt += actor.StacksOf(e.ScaleByStatus) * e.PerStackBp;
        }

        return new DamageMath.Inputs(
            Attack: external ? actor.Stats.ExternalAttack : actor.Stats.InternalAttack,
            Defense: external ? target.Stats.ExternalDefense : target.Stats.InternalDefense,
            Fixed: (int)Bp.Apply(e.Fixed, powerBp),
            ScaleBp: (int)Bp.Apply(e.ScaleBp, powerBp),
            CounterBp: skill is null ? Bp.One : DamageMath.CounterBp(skill, target.Template, _content.Counters),
            Crit: crit,
            DealtBp: dealt,
            TakenBp: target.Statuses.Sum(st => _content.Status(st.StatusId).DamageTakenBp * st.Stacks),
            VarianceBp: variance);
    }

    private static int DealDamage(Ctx ctx, BattleUnit source, BattleUnit target, int amount, DamageKind kind, bool crit)
    {
        var lost = Math.Min(amount, target.Hp);
        target.Hp -= lost;
        ctx.Emit(new Damaged(source.Id, target.Id, amount, kind, crit, target.Hp));
        if (source.Side != target.Side)
        {
            AddMomentum(ctx, source.Side, CombatConstants.MomentumOnHit);
            AddMomentum(ctx, target.Side, CombatConstants.MomentumWhenHurt);
        }

        if (target.IsDown)
        {
            Down(ctx, target);
        }

        return Math.Max(1, lost);
    }

    /// <summary>架势伤害（破绽中不再累计）。</summary>
    public int StanceDamageOf(BattleUnit actor, BattleUnit target, int amount)
    {
        if (target.Stats.MaxStance <= 0 || target.HasStatus(CoreIds.Broken) || amount <= 0)
        {
            return 0;
        }

        var mod = target.Statuses.Sum(st => _content.Status(st.StatusId).StanceTakenBp * st.Stacks);
        if (actor.HasTrait("stance_breaker"))
        {
            mod += CombatConstants.StanceBreakerBp;
        }

        return (int)Math.Max(0, Bp.Apply(amount, Bp.One + mod));
    }

    private void DamageStance(Ctx ctx, BattleUnit actor, BattleUnit target, int amount)
    {
        var dmg = StanceDamageOf(actor, target, amount);
        if (dmg <= 0)
        {
            return;
        }

        target.Stance = Math.Max(0, target.Stance - dmg);
        ctx.Emit(new StanceDamaged(actor.Id, target.Id, dmg, target.Stance));
        if (target.Stance == 0)
        {
            // 架势归零 → 破绽：下一次受击加伤、取消本轮剩余反应；蓄力被打断（破招）。
            ApplyStatusDirect(ctx, target, _content.Status(CoreIds.Broken), actor.Id, 0, 1);
            ctx.Emit(new StanceBroken(target.Id, actor.Id));
            AddMomentum(ctx, actor.Side, CombatConstants.MomentumOnBreak);
            InterruptCharge(ctx, target);
        }
    }

    private void TryApplyStatus(Ctx ctx, BattleUnit source, BattleUnit target, string statusId, int duration, int stacks, int chanceBp)
    {
        if (target.IsDown)
        {
            return;
        }

        var def = _content.Status(statusId);
        if (target.Statuses.Exists(st => _content.Status(st.StatusId).ImmuneTag is { } tag && def.HasTag(tag)))
        {
            ctx.Emit(new StatusResisted(target.Id, statusId));
            return;
        }

        var control = def.HasTag(CoreIds.ControlTag);
        var chance = chanceBp - (control && target.Side != source.Side ? target.Stats.ControlResistBp : 0);
        if (!ctx.State.Rng.RollBp(chance))
        {
            ctx.Emit(new StatusResisted(target.Id, statusId));
            return;
        }

        if (control && target.Template.ControlThreshold > 0)
        {
            // 首领：控制积累到阈值才生效，生效后清零（架构文档 7.3）。
            target.ControlMeter++;
            if (target.ControlMeter < target.Template.ControlThreshold)
            {
                ctx.Emit(new ControlAccumulated(target.Id, statusId, target.ControlMeter, target.Template.ControlThreshold));
                return;
            }

            target.ControlMeter = 0;
        }

        ApplyStatusDirect(ctx, target, def, source.Id, duration, stacks);
    }

    private StatusInstance ApplyStatusDirect(Ctx ctx, BattleUnit target, StatusDefinition def, string? source, int duration, int stacks)
    {
        var remaining = duration > 0 ? duration : def.Duration;

        // 施加在当前行动者身上、且会在本次行动结束时消耗的状态，本次结束不计（架构文档 7.3）。
        var skip = target.Id == ctx.ActorId && def.Tick == StatusTick.OwnerActionEnd && ctx.Stage is Stage.Start or Stage.Action;
        var existing = target.FindStatus(def.Id);
        if (existing is not null)
        {
            if (def.Stacking == StackRule.Ignore)
            {
                return existing;
            }

            if (def.Stacking == StackRule.AddStack)
            {
                existing.Stacks = Math.Min(def.MaxStacks, existing.Stacks + stacks);
            }

            existing.Remaining = remaining;
            existing.SourceId = source;
            existing.SkipNextTick = skip;
            ctx.Emit(new StatusApplied(target.Id, def.Id, existing.Stacks, existing.Remaining, source));
            return existing;
        }

        var instance = new StatusInstance
        {
            StatusId = def.Id, Stacks = Math.Min(def.MaxStacks, Math.Max(1, stacks)), Remaining = remaining,
            SourceId = source, SkipNextTick = skip,
        };
        target.Statuses.Add(instance);
        ctx.Emit(new StatusApplied(target.Id, def.Id, instance.Stacks, instance.Remaining, source));
        if (def.SkipAction)
        {
            InterruptCharge(ctx, target);
        }

        return instance;
    }

    private void InterruptCharge(Ctx ctx, BattleUnit unit)
    {
        if (unit.FindStatus(CoreIds.Charging) is { } charge)
        {
            EndStatus(ctx, unit, charge, StatusEndReason.Interrupted);
        }
    }

    private void EndStatus(Ctx ctx, BattleUnit unit, StatusInstance st, StatusEndReason reason)
    {
        if (!unit.Statuses.Remove(st))
        {
            return;
        }

        var def = _content.Status(st.StatusId);
        ctx.Emit(new StatusRemoved(unit.Id, st.StatusId, reason));
        if (st.StatusId == CoreIds.Charging && reason == StatusEndReason.Interrupted && st.ChargedSkill is not null)
        {
            ctx.Emit(new ChargeInterrupted(unit.Id, st.ChargedSkill));
        }

        if (unit.IsDown)
        {
            return;
        }

        if (def.RestoreStanceOnEnd)
        {
            RestoreStance(ctx, unit, unit.Stats.MaxStance);
        }

        if (def.GrantOnEnd is not null)
        {
            ApplyStatusDirect(ctx, unit, _content.Status(def.GrantOnEnd), unit.Id, 0, 1);
        }
    }

    private static void Down(Ctx ctx, BattleUnit unit)
    {
        unit.Hp = 0;
        unit.Statuses.Clear();
        unit.Cooldowns.Clear();
        ctx.Emit(new UnitDowned(unit.Id));
    }

    private void RestoreInner(Ctx ctx, BattleUnit unit, int amount)
    {
        var mod = unit.Statuses.Sum(st => _content.Status(st.StatusId).InnerRecoveryBp * st.Stacks);
        var gain = (int)Math.Min(Math.Max(0, Bp.Apply(amount, Bp.One + mod)), unit.Stats.MaxInner - unit.Inner);
        if (gain <= 0)
        {
            return;
        }

        unit.Inner += gain;
        ctx.Emit(new InnerChanged(unit.Id, gain, unit.Inner));
    }

    private static void RestoreStance(Ctx ctx, BattleUnit unit, int amount)
    {
        if (unit.HasStatus(CoreIds.Broken))
        {
            return;
        }

        var gain = Math.Min(amount, unit.Stats.MaxStance - unit.Stance);
        if (gain <= 0)
        {
            return;
        }

        unit.Stance += gain;
        ctx.Emit(new StanceRestored(unit.Id, gain, unit.Stance));
    }

    private static void AddMomentum(Ctx ctx, Side side, int delta)
    {
        var before = ctx.State.Momentum(side);
        ctx.State.SetMomentum(side, before + delta);
        if (ctx.State.Momentum(side) != before)
        {
            ctx.Emit(new MomentumChanged(side, ctx.State.Momentum(side)));
        }
    }

    /// <summary>机关抬一层；满层时对敌方可接触一排造成固定内劲伤害与架势伤害并清零。</summary>
    private void RaiseLevel(Ctx ctx, BattleUnit actor, EffectDefinition e)
    {
        var def = _content.Status(e.Status!);
        var st = actor.FindStatus(def.Id);
        if (st is null)
        {
            st = ApplyStatusDirect(ctx, actor, def, actor.Id, 0, 1);
        }
        else
        {
            st.Stacks = Math.Min(def.MaxStacks, st.Stacks + 1);
        }

        var released = st.Stacks >= def.MaxStacks;
        ctx.Emit(new LevelRaised(actor.Id, def.Id, st.Stacks, def.MaxStacks, released));
        if (!released)
        {
            return;
        }

        EndStatus(ctx, actor, st, StatusEndReason.Released);
        var foes = Targeting.Candidates(ctx.State, actor, TargetRule.ReachableRowEnemies);
        foreach (var foe in foes)
        {
            var taken = foe.Statuses.Sum(x => _content.Status(x.StatusId).DamageTakenBp * x.Stacks);
            var amount = DamageMath.Compute(new DamageMath.Inputs(0, 0, e.Fixed, 0, Bp.One, false, 0, taken, Bp.One));
            DealDamage(ctx, actor, foe, amount, DamageKind.Internal, false);
            if (!foe.IsDown)
            {
                DamageStance(ctx, actor, foe, e.Amount);
            }
        }
    }
}
