using WuxiaWorld.Domain.Combat;
using WuxiaWorld.Domain.Combat.Ai;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Domain.Common;

namespace WuxiaWorld.Tools.BattleSimulator;

/// <summary>
/// 模拟用的玩家策略。流派策略是人工制定的基本打法（开发计划 6.2“先由人工制定每种流派的基本策略”），
/// 不是最优解；弱策略的失败不能直接说明流派无用。
/// </summary>
internal interface IPolicy
{
    string Name { get; }

    BattleCommand Decide(BattleEngine engine, BattleState state, BattleUnit unit);
}

internal static class Policies
{
    public const string GoldenSore = "item.medicine.golden_sore";
    public const string QiPill = "item.medicine.qi_pill";

    public static IPolicy ByName(string name) => name switch
    {
        "basic" => new BasicOnly(),
        "greedy" => new Greedy(),
        "sword" => new SwordPolicy(),
        "fist" => new FistPolicy(),
        "inner" => new InnerPolicy(),
        "aware" => new AwarePolicy(),
        _ => throw new ArgumentException($"未知策略 {name}"),
    };

    public static bool Ok(BattleEngine e, BattleState s, BattleCommand c) => e.Validate(s, c) is null;

    public static int Ratio(BattleUnit u) => (int)((long)u.Hp * Bp.One / Math.Max(1, u.Stats.MaxHp));

    /// <summary>通用保命：气血低于三成五吃金创药，内力见底吃回气丹。</summary>
    public static BattleCommand? Survival(BattleEngine e, BattleState s, BattleUnit u, int innerFloor)
    {
        if (Ratio(u) < 3500 && Ok(e, s, new UseItem(u.Id, GoldenSore, u.Id)))
        {
            return new UseItem(u.Id, GoldenSore, u.Id);
        }

        if (u.Inner < innerFloor && Ok(e, s, new UseItem(u.Id, QiPill, u.Id)))
        {
            return new UseItem(u.Id, QiPill, u.Id);
        }

        return null;
    }

    /// <summary>主攻目标：首领优先；其次气血最低者；同分按 ID。</summary>
    public static BattleUnit? Focus(BattleEngine e, BattleState s, BattleUnit u, TargetRule rule)
    {
        return Targeting.Candidates(s, u, rule)
            .Where(t => !t.Template.HasTag("mechanism"))
            .OrderByDescending(t => t.Template.HasTag("boss"))
            .ThenBy(t => t.Hp)
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static BattleUnit? ChargingFoe(BattleState s, BattleUnit u) =>
        s.Living(Targeting.Opposite(u.Side)).FirstOrDefault(t => t.HasStatus(CoreIds.Charging));

    /// <summary>水门机关还护着首领时，先拆机关。</summary>
    public static BattleUnit? CoveringMechanism(BattleState s, BattleUnit u) =>
        s.Living(Targeting.Opposite(u.Side)).Any(t => t.HasStatus("status.sluice_cover"))
            ? s.Living(Targeting.Opposite(u.Side)).FirstOrDefault(t => t.Template.HasTag("mechanism"))
            : null;

    public static BattleCommand Basic(BattleEngine e, BattleState s, BattleUnit u)
    {
        var candidates = Targeting.Candidates(s, u, TargetRule.SingleReachableEnemy);
        var t = Focus(e, s, u, TargetRule.SingleReachableEnemy) ?? (candidates.Count > 0 ? candidates[0] : null);
        return t is not null ? new UseSkill(u.Id, CoreIds.BasicAttack, t.Id) : new Defend(u.Id);
    }

    public static BattleCommand? Try(BattleEngine e, BattleState s, BattleUnit u, string skill, BattleUnit? target)
    {
        if (target is null)
        {
            return null;
        }

        var c = new UseSkill(u.Id, skill, target.Id);
        return Ok(e, s, c) ? c : null;
    }

    public static BattleCommand? TryGroup(BattleEngine e, BattleState s, BattleUnit u, string skill)
    {
        var c = new UseSkill(u.Id, skill, null);
        return Ok(e, s, c) ? c : null;
    }
}

internal sealed class BasicOnly : IPolicy
{
    public string Name => "basic";

    public BattleCommand Decide(BattleEngine engine, BattleState state, BattleUnit unit) => Policies.Basic(engine, state, unit);
}

internal sealed class Greedy : IPolicy
{
    public string Name => "greedy";

    public BattleCommand Decide(BattleEngine engine, BattleState state, BattleUnit unit) =>
        BattleAi.BestAttack(engine, state, unit, skirmish: false);
}

/// <summary>剑术破招：试剑看破 → 破势削架势 → 破绽时连环刺爆发；首领蓄力时优先破招打断；水门掩护时穿林剑拆机关。</summary>
internal sealed class SwordPolicy : IPolicy
{
    public string Name => "sword";

    public BattleCommand Decide(BattleEngine e, BattleState s, BattleUnit u)
    {
        if (Policies.Survival(e, s, u, 12) is { } item)
        {
            return item;
        }

        var charging = Policies.ChargingFoe(s, u);
        if (charging is not null && e.Estimate(s, u.Id, "skill.sword.break_guard", charging.Id) is { Breaks: true }
            && Policies.Try(e, s, u, "skill.sword.break_guard", charging) is { } interrupt)
        {
            return interrupt;
        }

        if (Policies.CoveringMechanism(s, u) is { } gate && Policies.Try(e, s, u, "skill.sword.pierce", gate) is { } pierce)
        {
            return pierce;
        }

        var target = Policies.Focus(e, s, u, TargetRule.SingleReachableEnemy);
        if (target is null)
        {
            return Policies.Basic(e, s, u);
        }

        if (target.HasStatus(CoreIds.Broken) && Policies.Try(e, s, u, "skill.sword.chain_thrust", target) is { } burst)
        {
            return burst;
        }

        // 对方摆了反击架势：不近身，用穿林剑远取。
        if (target.HasStatus("status.counter_ready") && Policies.Try(e, s, u, "skill.sword.pierce", target) is { } safe)
        {
            return safe;
        }

        if (!target.HasStatus("status.insight") && target.Stance > 30 && Policies.Try(e, s, u, "skill.sword.probe", target) is { } probe)
        {
            return probe;
        }

        if (Policies.Try(e, s, u, "skill.sword.break_guard", target) is { } breakGuard)
        {
            return breakGuard;
        }

        if (s.Momentum(u.Side) >= 60 && Policies.Try(e, s, u, "skill.sword.chain_thrust", target) is { } chain)
        {
            return chain;
        }

        if (Policies.Try(e, s, u, "skill.sword.pierce", target) is { } pierceMain)
        {
            return pierceMain;
        }

        if (u.Inner < 12)
        {
            return new Meditate(u.Id);
        }

        return Policies.Basic(e, s, u);
    }
}

/// <summary>拳掌护援：同伴危急时护身掌；首领蓄力时防御（招架积势）；多敌时扫堂；平时沉肘反击与开山掌。</summary>
internal sealed class FistPolicy : IPolicy
{
    public string Name => "fist";

    public BattleCommand Decide(BattleEngine e, BattleState s, BattleUnit u)
    {
        if (Policies.Survival(e, s, u, 8) is { } item)
        {
            return item;
        }

        // 首领蓄力指向某位同伴时，先把他护住。
        var marked = Policies.ChargingFoe(s, u)?.FindStatus(CoreIds.Charging)?.ChargedTarget;
        var ward = marked is not null && marked != u.Id && s.TryUnit(marked) is { IsDown: false } m && !m.HasStatus(CoreIds.Guarded)
            ? m
            : s.Living(u.Side).Where(a => a.Id != u.Id && !a.HasStatus(CoreIds.Guarded) && Policies.Ratio(a) < 6500)
                .OrderBy(Policies.Ratio).FirstOrDefault();
        if (ward is not null && Policies.Try(e, s, u, "skill.fist.shield_palm", ward) is { } guard)
        {
            return guard;
        }

        if (Policies.ChargingFoe(s, u) is not null && u.Position.Row == 0)
        {
            return new Defend(u.Id);
        }

        var front = Targeting.Candidates(s, u, TargetRule.ReachableRowEnemies).Count(t => !t.Template.HasTag("mechanism"));
        if (front >= 2 && Policies.TryGroup(e, s, u, "skill.fist.sweep") is { } sweep)
        {
            return sweep;
        }

        var target = Policies.Focus(e, s, u, TargetRule.SingleReachableEnemy);
        if (target is not null && Policies.Try(e, s, u, "skill.fist.heavy_palm", target) is { } palm)
        {
            return palm;
        }

        if (front >= 1 && !u.HasStatus("status.counter_ready") && Policies.Try(e, s, u, "skill.fist.counter_stance", u) is { } stance)
        {
            return stance;
        }

        if (u.Inner < 12)
        {
            return new Meditate(u.Id);
        }

        return Policies.Basic(e, s, u);
    }
}

/// <summary>内功调息：同伴危急或中了可驱散状态时回气诀；首领蓄力时点穴；势满周天归元；内力不足调息；其余劈空劲。</summary>
internal sealed class InnerPolicy : IPolicy
{
    public string Name => "inner";

    public BattleCommand Decide(BattleEngine e, BattleState s, BattleUnit u)
    {
        if (Policies.Survival(e, s, u, 12) is { } item)
        {
            return item;
        }

        var hurt = s.Living(u.Side).OrderBy(Policies.Ratio).ThenBy(a => a.Id, StringComparer.Ordinal).First();
        if (Policies.Ratio(hurt) < 5500 && Policies.Try(e, s, u, "skill.inner.mend", hurt) is { } mend)
        {
            return mend;
        }

        var afflicted = s.Living(u.Side).FirstOrDefault(a => Policies.Ratio(a) < 8500
            && a.Statuses.Exists(st => e.Content.Status(st.StatusId) is { Harmful: true } d && d.HasTag("dispellable")));
        if (afflicted is not null && Policies.Try(e, s, u, "skill.inner.mend", afflicted) is { } cleanse)
        {
            return cleanse;
        }

        if (Policies.ChargingFoe(s, u) is { } charging && Policies.Try(e, s, u, "skill.inner.pressure_point", charging) is { } point)
        {
            return point;
        }

        if (s.Living(Targeting.Opposite(u.Side)).Count() >= 2 && Policies.TryGroup(e, s, u, "skill.inner.surge") is { } surge)
        {
            return surge;
        }

        if (u.Inner < 16)
        {
            return new Meditate(u.Id);
        }

        var target = Policies.Focus(e, s, u, TargetRule.SingleAnyEnemy);
        return Policies.Try(e, s, u, "skill.inner.qi_bolt", target) ?? Policies.Basic(e, s, u);
    }
}

/// <summary>
/// 看预兆的贪心打法（第一章后院切磋要教的那一手）：破招的那一手（破势剑、点穴手）平时留着，对手蓄力时能打断就打断，
/// 打不断就防御硬接；其余时候按贪心评分出招。
/// </summary>
internal sealed class AwarePolicy : IPolicy
{
    public string Name => "aware";

    public BattleCommand Decide(BattleEngine e, BattleState s, BattleUnit u)
    {
        if (Policies.ChargingFoe(s, u) is { } charging)
        {
            if (e.Estimate(s, u.Id, "skill.sword.break_guard", charging.Id) is { Breaks: true }
                && Policies.Try(e, s, u, "skill.sword.break_guard", charging) is { } interrupt)
            {
                return interrupt;
            }

            if (Policies.Try(e, s, u, "skill.inner.pressure_point", charging) is { } point)
            {
                return point;
            }

            return new Defend(u.Id);
        }

        var reserve = u.Template.Loadout.Skills.Contains("skill.sword.break_guard") ? "skill.sword.break_guard" : "skill.inner.pressure_point";
        return BattleAi.BestAttack(e, s, u, skirmish: false, exclude: reserve);
    }
}
