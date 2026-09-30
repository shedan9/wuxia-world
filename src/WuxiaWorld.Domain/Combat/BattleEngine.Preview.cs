using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Domain.Common;

namespace WuxiaWorld.Domain.Combat;

/// <summary>
/// 招式预估：命中率、伤害区间（浮动上下限，不含 / 含暴击）、架势伤害、是否破绽、是否会被护援代受。
/// 只读当前状态，不消耗也不偷看随机数（架构文档 7.4“技能预览显示概率和伤害区间”）。
/// </summary>
public sealed record ActionEstimate(
    string SkillId,
    string TargetId,
    int HitBp,
    int CritBp,
    int DamageMin,
    int DamageMax,
    int CritDamageMax,
    int Stance,
    bool Breaks,
    string? GuardedBy,
    int Heal)
{
    public bool Hostile => HitBp > 0;
}

public sealed partial class BattleEngine
{
    public ActionEstimate? Estimate(BattleState state, string actorId, string skillId, string targetId)
    {
        var actor = state.Unit(actorId);
        var skill = _content.Skill(skillId);
        var target = state.TryUnit(targetId);
        if (target is null || target.IsDown)
        {
            return null;
        }

        string? guardedBy = null;
        if (target.Side != actor.Side && skill.IsSingleTarget && target.FindStatus(CoreIds.Guarded) is { } g
            && state.TryUnit(g.SourceId) is { } guardian && guardian.Id != target.Id && guardian.Side == target.Side && CanReact(guardian))
        {
            guardedBy = guardian.Id;
            target = guardian;
        }

        var hostile = target.Side != actor.Side;
        int min = 0, max = 0, critMax = 0, stance = 0, heal = 0;
        foreach (var e in skill.Effects)
        {
            var who = e.Applies == EffectTarget.Self ? actor : target;
            switch (e.Type)
            {
                case EffectType.Damage when who.Side != actor.Side:
                    min += DamageMath.Compute(DamageInputs(actor, who, skill, e, Bp.One, false, CombatConstants.VarianceMinBp));
                    max += DamageMath.Compute(DamageInputs(actor, who, skill, e, Bp.One, false, CombatConstants.VarianceMaxBp));
                    critMax += DamageMath.Compute(DamageInputs(actor, who, skill, e, Bp.One, true, CombatConstants.VarianceMaxBp));
                    break;
                case EffectType.StanceDamage when who.Side != actor.Side:
                    stance += StanceDamageOf(actor, who, e.Amount);
                    break;
                case EffectType.Heal:
                    heal += (int)Math.Min(e.Amount + Bp.Apply(actor.Stats.InternalAttack, e.AmountScaleBp), who.Stats.MaxHp - who.Hp);
                    break;
            }
        }

        return new ActionEstimate(
            skill.Id,
            target.Id,
            hostile ? (skill.SureHit ? Bp.One : HitChance(actor, target)) : 0,
            hostile && max > 0 ? DamageMath.CritChanceBp(actor.Stats.CritBp) : 0,
            min,
            max,
            critMax,
            stance,
            hostile && stance > 0 && stance >= target.Stance && target.Stats.MaxStance > 0,
            guardedBy,
            heal);
    }

    /// <summary>当前行动者此刻能用的招式（含普通攻击）及其合法目标；供界面与 AI 使用。</summary>
    public IReadOnlyList<(SkillDefinition Skill, IReadOnlyList<BattleUnit> Targets)> UsableSkills(BattleState state, string actorId)
    {
        var actor = state.Unit(actorId);
        var result = new List<(SkillDefinition, IReadOnlyList<BattleUnit>)>();
        foreach (var id in actor.Skills.Prepend(CoreIds.BasicAttack))
        {
            var skill = _content.Skill(id);
            var candidates = Targeting.Candidates(state, actor, skill.TargetRule);
            if (Validate(state, new UseSkill(actorId, id, candidates.Count > 0 ? candidates[0].Id : null)) is null)
            {
                result.Add((skill, candidates));
            }
        }

        return result;
    }
}
