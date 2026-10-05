using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Domain.Common;

namespace WuxiaWorld.Domain.Combat.Ai;

/// <summary>
/// 敌方 AI：按合法目标与效用评分选动作（架构文档 7.1）。只读当前状态，不读取玩家尚未提交的决策；
/// 同分按招式 ID、目标 ID 的序数次序取第一个，保证确定性。
/// </summary>
public static class BattleAi
{
    public static BattleCommand Decide(BattleEngine engine, BattleState state)
    {
        var actorId = state.Pending ?? throw new InvalidOperationException("当前没有待行动单位。");
        var actor = state.Unit(actorId);
        return actor.Template.Ai switch
        {
            AiProfiles.Dummy => new Defend(actorId),
            AiProfiles.Mechanism => Mechanism(engine, state, actor),
            AiProfiles.Guardian => Guardian(engine, state, actor),
            AiProfiles.BossTang => BossTang(engine, state, actor),
            AiProfiles.Sparring => Sparring(engine, state, actor),
            AiProfiles.Skirmisher => BestAttack(engine, state, actor, skirmish: true),
            _ => BestAttack(engine, state, actor, skirmish: false),
        };
    }

    private static BattleCommand Mechanism(BattleEngine engine, BattleState state, BattleUnit actor)
    {
        var usable = engine.UsableSkills(state, actor.Id).FirstOrDefault(u => u.Skill.Id != CoreIds.BasicAttack);
        return usable.Skill is null ? new Defend(actor.Id) : new UseSkill(actor.Id, usable.Skill.Id, null);
    }

    /// <summary>护卫：同伴气血低于六成时护援其中比例最低者（首领优先）；自身无反击架势时摆架势；否则进攻。</summary>
    private static BattleCommand Guardian(BattleEngine engine, BattleState state, BattleUnit actor)
    {
        var usable = engine.UsableSkills(state, actor.Id);
        var guard = usable.FirstOrDefault(u => u.Skill.Effects.Any(e => e.Type == EffectType.Guard));
        if (guard.Skill is not null)
        {
            var ward = guard.Targets
                .Where(t => t.Id != actor.Id && !t.HasStatus(CoreIds.Guarded) && Ratio(t) < 6000)
                .OrderByDescending(t => t.Template.HasTag("boss"))
                .ThenBy(Ratio)
                .ThenBy(t => t.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (ward is not null)
            {
                return new UseSkill(actor.Id, guard.Skill.Id, ward.Id);
            }
        }

        var stanceSkill = usable.FirstOrDefault(u => u.Skill.Effects.Any(e =>
            e.Type == EffectType.ApplyStatus && e.Status is not null && engine.Content.Status(e.Status).CounterReady));
        if (stanceSkill.Skill is not null && !actor.Statuses.Exists(s => engine.Content.Status(s.StatusId).CounterReady))
        {
            return new UseSkill(actor.Id, stanceSkill.Skill.Id, actor.Id);
        }

        return BestAttack(engine, state, actor, skirmish: false);
    }

    /// <summary>
    /// 唐守亭：敌方势够时优先蓄力耗势大招（指向气血比例最低者，意图写明目标）；否则第 2 轮起蓄力群攻；
    /// 两者都不可用时按莽攻评分。蓄力招都提前一轮亮出意图。
    /// </summary>
    private static BattleCommand BossTang(BattleEngine engine, BattleState state, BattleUnit actor)
    {
        var usable = engine.UsableSkills(state, actor.Id);
        var finisher = usable.FirstOrDefault(u => u.Skill.Charged && u.Skill.MomentumCost > 0);
        if (finisher.Skill is not null && finisher.Targets.Count > 0)
        {
            var mark = finisher.Targets.OrderBy(Ratio).ThenBy(t => t.Id, StringComparer.Ordinal).First();
            return new UseSkill(actor.Id, finisher.Skill.Id, mark.Id);
        }

        var charged = usable.FirstOrDefault(u => u.Skill.Charged && u.Skill.MomentumCost == 0);
        if (charged.Skill is not null && state.Round >= 2)
        {
            return new UseSkill(actor.Id, charged.Skill.Id, charged.Targets.Count > 0 ? charged.Targets[0].Id : null);
        }

        return BestAttack(engine, state, actor, skirmish: false);
    }

    /// <summary>教招：第 2 轮起蓄力招可用就亮出起手，对准气血比例最高者（教的是怎么应对，不是专挑软柿子）；否则莽攻。</summary>
    private static BattleCommand Sparring(BattleEngine engine, BattleState state, BattleUnit actor)
    {
        var charged = engine.UsableSkills(state, actor.Id).FirstOrDefault(u => u.Skill.Charged);
        if (charged.Skill is not null && state.Round >= 2)
        {
            var mark = charged.Targets.OrderByDescending(Ratio).ThenBy(t => t.Id, StringComparer.Ordinal).FirstOrDefault();
            return new UseSkill(actor.Id, charged.Skill.Id, mark?.Id);
        }

        return BestAttack(engine, state, actor, skirmish: false);
    }

    /// <summary>
    /// 攻击评分：预计伤害占目标剩余气血的比例、能否击倒、能否打出破绽、附带状态；
    /// 游击型另加后排与已流血目标的分，并偏好远程招。内力成本按点扣分。
    /// </summary>
    public static BattleCommand BestAttack(BattleEngine engine, BattleState state, BattleUnit actor, bool skirmish, string? exclude = null)
    {
        BattleCommand? best = null;
        long bestScore = long.MinValue;
        foreach (var (skill, targets) in engine.UsableSkills(state, actor.Id)
                     .OrderBy(u => u.Skill.Id, StringComparer.Ordinal))
        {
            if (skill.Id == exclude || skill.Charged || !skill.TargetsEnemies)
            {
                continue;
            }

            var group = !Targeting.NeedsChosenTarget(skill.TargetRule);
            long score = 0;
            foreach (var target in targets.OrderBy(t => t.Id, StringComparer.Ordinal))
            {
                var e = engine.Estimate(state, actor.Id, skill.Id, target.Id);
                if (e is null)
                {
                    continue;
                }

                if (skill.TargetRule == TargetRule.ColumnEnemies)
                {
                    // 一列：按所选目标所在列的全部敌人合计评分。
                    long column = -skill.InnerCost * 3;
                    foreach (var other in Targeting.Resolve(state, actor, skill.TargetRule, target.Id) ?? [])
                    {
                        if (engine.Estimate(state, actor.Id, skill.Id, other.Id) is { } oe)
                        {
                            column += UnitScore(engine, skill, oe, state.Unit(oe.TargetId), skirmish);
                        }
                    }

                    if (column > bestScore)
                    {
                        bestScore = column;
                        best = new UseSkill(actor.Id, skill.Id, target.Id);
                    }

                    continue;
                }

                var s = UnitScore(engine, skill, e, state.Unit(e.TargetId), skirmish);
                if (group)
                {
                    score += s;
                    continue;
                }

                s -= skill.InnerCost * 3;
                if (s > bestScore)
                {
                    bestScore = s;
                    best = new UseSkill(actor.Id, skill.Id, target.Id);
                }
            }

            if (group && targets.Count > 0)
            {
                score -= skill.InnerCost * 3;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = new UseSkill(actor.Id, skill.Id, null);
                }
            }
        }

        return best ?? new Defend(actor.Id);
    }

    /// <summary>对一名目标的评分（不含内力成本）。</summary>
    private static long UnitScore(BattleEngine engine, SkillDefinition skill, ActionEstimate e, BattleUnit target, bool skirmish)
    {
        long expected = (long)(e.DamageMin + e.DamageMax) / 2 * e.HitBp / Bp.One;
        var s = expected * 1000 / Math.Max(1, target.Hp);
        if (expected >= target.Hp)
        {
            s += 600;
        }

        if (e.Breaks)
        {
            s += 300;
        }

        s += skill.Effects.Count(x => x.Type == EffectType.ApplyStatus && x.Status is not null && !target.HasStatus(x.Status)) * 80;
        if (skirmish)
        {
            s += target.Position.Row == 1 ? 200 : 0;
            s += target.Statuses.Exists(x => engine.Content.Status(x.StatusId).HasTag("bleed")) ? 120 : 0;
            s += skill.TargetRule == TargetRule.SingleAnyEnemy ? 100 : 0;
        }

        if (target.Template.HasTag("mechanism"))
        {
            s -= 200;
        }

        return s;
    }

    private static int Ratio(BattleUnit u) => (int)((long)u.Hp * Bp.One / Math.Max(1, u.Stats.MaxHp));
}
