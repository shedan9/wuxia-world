using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Domain.Common;

namespace WuxiaWorld.Domain.Combat;

public sealed partial class BattleEngine
{
    private void Execute(Ctx ctx, BattleUnit actor, BattleCommand command)
    {
        var s = ctx.State;
        switch (command)
        {
            case UseSkill use:
            {
                var skill = _content.Skill(use.SkillId);
                PayCosts(ctx, actor, skill);
                if (skill.Charged)
                {
                    // 蓄力：本次只亮出意图，下一次自身行动自动出手；冷却在出手时计。
                    var charge = ApplyStatusDirect(ctx, actor, _content.Status(CoreIds.Charging), actor.Id, 0, 1);
                    charge.ChargedSkill = skill.Id;
                    charge.ChargedTarget = use.Target;
                    ctx.Emit(new ChargeStarted(actor.Id, skill.Id, use.Target));
                    return;
                }

                SetCooldown(ctx, actor, skill);
                var targets = Targeting.Resolve(s, actor, skill.TargetRule, use.Target) ?? [];
                ResolveSkill(ctx, actor, skill, targets, isReaction: false, powerBp: actor.Template.PowerOf(skill.Id));
                return;
            }

            case Defend:
                ctx.Emit(new Defended(actor.Id));
                ApplyStatusDirect(ctx, actor, _content.Status(CoreIds.Defend), actor.Id, 0, 1);
                RestoreStance(ctx, actor, CombatConstants.DefendStance);
                return;

            case Meditate:
            {
                ctx.Emit(new Meditated(actor.Id));
                var bp = CombatConstants.MeditateInnerBp + actor.Arts.Sum(a => a.MeditateBonusBp);
                RestoreInner(ctx, actor, (int)Bp.Apply(actor.Stats.MaxInner, bp));
                RestoreStance(ctx, actor, CombatConstants.MeditateStance);
                ApplyStatusDirect(ctx, actor, _content.Status(CoreIds.Meditating), actor.Id, 0, 1);
                return;
            }

            case UseItem use:
            {
                var def = _content.Item(use.ItemId);
                var targets = Targeting.Resolve(s, actor, def.TargetRule, use.Target) ?? [];
                s.Items[use.ItemId]--;
                var left = s.Items[use.ItemId];
                if (left <= 0)
                {
                    s.Items.Remove(use.ItemId);
                }

                ctx.Emit(new ItemUsed(actor.Id, use.ItemId, use.Target, left));
                foreach (var t in targets)
                {
                    foreach (var e in def.Effects)
                    {
                        ApplyEffect(ctx, actor, e.Applies == EffectTarget.Self ? actor : t, null, e, Bp.One);
                    }
                }

                return;
            }

            case Swap swap:
            {
                var to = new Position(swap.Row, swap.Slot);
                var from = actor.Position;
                var other = s.Units.Find(u => u.Side == actor.Side && u.Position == to);
                if (other is not null)
                {
                    other.Position = from;
                }

                actor.Position = to;
                ctx.Emit(new Swapped(actor.Id, from, to, other?.Id));
                return;
            }

            case Retreat:
            {
                var ours = s.Living(actor.Side).Select(EffectiveSpeed).DefaultIfEmpty(0).Average();
                var theirs = s.Living(Targeting.Opposite(actor.Side)).Select(EffectiveSpeed).DefaultIfEmpty(0).Average();
                // 切磋里撤退就是拱手认输，对方不会追，总能成。
                var chance = s.Spar ? Bp.One : Bp.Clamp(
                    CombatConstants.RetreatBaseBp + (int)Math.Floor(ours - theirs) * CombatConstants.RetreatPerSpeedBp,
                    CombatConstants.RetreatMinBp,
                    CombatConstants.RetreatMaxBp);
                var ok = s.Rng.RollBp(chance);
                ctx.Emit(new RetreatAttempted(actor.Id, chance, ok));
                if (ok)
                {
                    End(ctx, BattleOutcome.Retreated);
                }

                return;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
    }

    private static void PayCosts(Ctx ctx, BattleUnit actor, SkillDefinition skill)
    {
        if (skill.InnerCost > 0)
        {
            actor.Inner -= skill.InnerCost;
            ctx.Emit(new InnerChanged(actor.Id, -skill.InnerCost, actor.Inner));
        }

        if (skill.MomentumCost > 0)
        {
            AddMomentum(ctx, actor.Side, -skill.MomentumCost);
        }
    }

    private static void SetCooldown(Ctx ctx, BattleUnit actor, SkillDefinition skill)
    {
        if (skill.Cooldown > 0)
        {
            actor.Cooldowns[skill.Id] = skill.Cooldown;
            ctx.JustCooled.Add(skill.Id);
        }
    }

    private void ReleaseCharge(Ctx ctx, BattleUnit actor, StatusInstance charge)
    {
        var skill = _content.Skill(charge.ChargedSkill!);
        var chosen = charge.ChargedTarget;
        EndStatus(ctx, actor, charge, StatusEndReason.Released);
        SetCooldown(ctx, actor, skill);

        // 目标失效（倒下或退到够不着的位置）时改选第一个合法目标；无目标则落空。
        var targets = Targeting.Resolve(ctx.State, actor, skill.TargetRule, chosen);
        if (targets is null && Targeting.NeedsChosenTarget(skill.TargetRule))
        {
            var fallback = Targeting.Candidates(ctx.State, actor, skill.TargetRule);
            targets = fallback.Count > 0 ? Targeting.Resolve(ctx.State, actor, skill.TargetRule, fallback[0].Id) : [];
        }

        ResolveSkill(ctx, actor, skill, targets ?? [], isReaction: false, powerBp: actor.Template.PowerOf(skill.Id));
    }

    /// <summary>
    /// 结算一次招式。每个敌方目标单独判定命中；对命中目标按定义次序套用效果；然后套用作用于自身的效果；
    /// 最后处理反击（反应不再触发反应，架构文档 7.1）。
    /// </summary>
    private void ResolveSkill(Ctx ctx, BattleUnit actor, SkillDefinition skill, IReadOnlyList<BattleUnit> targets, bool isReaction, int powerBp)
    {
        var s = ctx.State;
        ctx.Emit(new SkillUsed(actor.Id, skill.Id, [.. targets.Select(t => t.Id)], isReaction ? 0 : skill.InnerCost,
            isReaction ? 0 : skill.MomentumCost, isReaction));

        var counters = new List<BattleUnit>();
        foreach (var original in targets)
        {
            if (actor.IsDown || s.Outcome != BattleOutcome.Ongoing)
            {
                break;
            }

            if (original.IsDown)
            {
                continue;
            }

            var target = original;
            var hostile = target.Side != actor.Side;
            if (hostile && skill.IsSingleTarget && !isReaction)
            {
                target = TryGuard(ctx, actor, target);
            }

            if (hostile && !skill.SureHit)
            {
                var hit = HitChance(actor, target);
                if (!s.Rng.RollBp(hit))
                {
                    ctx.Emit(new Missed(actor.Id, target.Id, skill.Id));
                    continue;
                }
            }

            // 防御、破绽等“受一次有效伤害即消失”的状态：以出手前已存在的为准，本招全部段数结算后再移除。
            var consumable = hostile
                ? target.Statuses.Where(st => _content.Status(st.StatusId).ConsumeOnHit).ToList()
                : [];
            var wasDefending = target.HasStatus(CoreIds.Defend);
            var dealt = 0;
            foreach (var e in skill.Effects)
            {
                if (e.Applies != EffectTarget.Target)
                {
                    continue;
                }

                if (target.IsDown)
                {
                    break;
                }

                dealt += ApplyEffect(ctx, actor, target, skill, e, powerBp);
            }

            if (!hostile || dealt <= 0)
            {
                continue;
            }

            foreach (var st in consumable)
            {
                if (target.Statuses.Contains(st))
                {
                    EndStatus(ctx, target, st, StatusEndReason.Consumed);
                }
            }

            if (target.IsDown)
            {
                continue;
            }

            if (wasDefending && target.HasTrait("parry") && CanReact(target))
            {
                target.ReactionUsed = true;
                RestoreStance(ctx, target, CombatConstants.ParryStance);
                AddMomentum(ctx, target.Side, CombatConstants.ParryMomentum);
                ctx.Emit(new Parried(target.Id, target.Stance));
            }
            else if (!isReaction && skill.IsMelee && target.Statuses.Exists(st => _content.Status(st.StatusId).CounterReady))
            {
                counters.Add(target);
            }
        }

        foreach (var e in skill.Effects)
        {
            if (e.Applies == EffectTarget.Self && !actor.IsDown && s.Outcome == BattleOutcome.Ongoing)
            {
                ApplyEffect(ctx, actor, actor, skill, e, powerBp);
            }
        }

        CheckOutcome(ctx);
        foreach (var r in counters)
        {
            if (actor.IsDown || s.Outcome != BattleOutcome.Ongoing)
            {
                break;
            }

            if (!CanReact(r))
            {
                continue;
            }

            r.ReactionUsed = true;
            ResolveSkill(ctx, r, _content.Skill(CoreIds.BasicAttack), [actor], isReaction: true, powerBp: CombatConstants.CounterAttackBp);
        }
    }

    public bool CanReact(BattleUnit u) =>
        !u.IsDown && !u.ReactionUsed
        && !u.Statuses.Exists(st => _content.Status(st.StatusId) is { CancelsReactions: true } or { SkipAction: true });

    /// <summary>护援：被护者身上的护援状态来自一名能作出反应的同伴时，由其代受这一击。</summary>
    private BattleUnit TryGuard(Ctx ctx, BattleUnit attacker, BattleUnit target)
    {
        var guard = target.FindStatus(CoreIds.Guarded);
        if (guard is null || ctx.State.TryUnit(guard.SourceId) is not { } guardian
            || guardian.Id == target.Id || guardian.Side != target.Side || !CanReact(guardian))
        {
            return target;
        }

        guardian.ReactionUsed = true;
        EndStatus(ctx, target, guard, StatusEndReason.Consumed);
        ctx.Emit(new GuardIntercepted(guardian.Id, target.Id, attacker.Id));
        return guardian;
    }

    public int HitChance(BattleUnit actor, BattleUnit target)
    {
        var bonus = actor.Statuses.Sum(st => _content.Status(st.StatusId).HitBp * st.Stacks);
        return DamageMath.HitChanceBp(actor.Stats.Accuracy, target.Stats.Evasion, bonus);
    }
}
