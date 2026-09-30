using WuxiaWorld.Application.Combat;
using WuxiaWorld.Domain.Characters;
using WuxiaWorld.Domain.Combat;
using WuxiaWorld.Domain.Combat.Ai;
using WuxiaWorld.Domain.Combat.Definitions;
using static WuxiaWorld.Domain.Tests.TestContent;

namespace WuxiaWorld.Domain.Tests;

/// <summary>规则版本 2：同速比身法、穿透一列、首领耗敌方势的蓄力大招。</summary>
public class RulesetTwoTests
{
    private static (BattleEngine Engine, BattleState State) Start(CombatContent content, string encounter, params AllyEntry[] allies)
    {
        var engine = new BattleEngine(content);
        var (state, _) = engine.Start(new BattleSetup { EncounterId = encounter, Seed = 5, Allies = allies });
        return (engine, state);
    }

    [Fact]
    public void Equal_speed_is_broken_by_agility_before_id()
    {
        // 两人速度都覆盖为 50；z 身法 9、a 身法 5 → z 先动，尽管 ID 靠后。
        var nimble = Unit("t.nimble", speed: 50) with { Attributes = new Attributes(5, 5, 5, 9, 5) };
        var plain = Unit("t.plain", speed: 50);
        var foe = Unit("t.foe", speed: 10);
        var content = With([nimble, plain, foe], [Encounter("battle.test.tie", ("t.foe", "enemy.x", 0, 0))]);
        var (_, state) = Start(content, "battle.test.tie", Ally(plain, "ally.a", 0, 0), Ally(nimble, "ally.z", 0, 1));
        Assert.Equal(["ally.z", "ally.a", "enemy.x"], state.Order);
    }

    [Fact]
    public void Column_skill_hits_front_and_back_of_the_chosen_slot_only()
    {
        var hero = Unit("t.hero", speed: 60, skills: ["skill.sword.pierce"]);
        var foe = Unit("t.foe", speed: 10, hp: 5000);
        var content = With([hero, foe], [Encounter("battle.test.column",
            ("t.foe", "enemy.front_a", 0, 0), ("t.foe", "enemy.front_b", 0, 1), ("t.foe", "enemy.back_b", 1, 1), ("t.foe", "enemy.back_c", 1, 2))]);
        var (engine, state) = Start(content, "battle.test.column", Ally(hero, "ally.hero", 0, 0));

        // 选后排目标也可以（前排不挡），命中同一槽位的前后两人。
        var targets = Targeting.Resolve(state, state.Unit("ally.hero"), TargetRule.ColumnEnemies, "enemy.back_b")!;
        Assert.Equal(["enemy.front_b", "enemy.back_b"], targets.Select(t => t.Id));
        var events = engine.Submit(state, new UseSkill("ally.hero", "skill.sword.pierce", "enemy.back_b")).Events;
        var used = events.OfType<SkillUsed>().Single();
        Assert.Equal(["enemy.front_b", "enemy.back_b"], used.Targets);
        Assert.DoesNotContain(events.OfType<Damaged>(), d => d.Target is "enemy.front_a" or "enemy.back_c");
    }

    [Fact]
    public void Boss_spends_enemy_momentum_on_a_telegraphed_hook_that_a_guard_can_block()
    {
        var engine = new BattleEngine(Real);
        var session = new BattleSession(engine, new BattleSetup
        {
            EncounterId = "battle.01.old_ferry_sluice", Seed = 11,
            Allies =
            [
                Ally(Real.Combatant("combatant.hero.fist"), "char.hero", 0, 1),
                Ally(Real.Combatant("combatant.lu_qinghe"), "char.lu_qinghe", 1, 1),
            ],
        });

        // 推进到唐守亭行动，并让敌方势满 50：他必须改用耗势的回澜钩，指向气血比例最低者。
        for (var i = 0; i < 40 && session.State.Pending != "enemy.tang_shouting"; i++)
        {
            if (session.AwaitingPlayer is { } unit)
            {
                session.Submit(new Defend(unit.Id));
            }
            else
            {
                session.StepAi();
            }
        }

        var state = session.State;
        state.SetMomentum(Side.Enemy, 60);
        state.Unit("char.lu_qinghe").Hp = 100;
        var command = BattleAi.Decide(engine, state);
        Assert.Equal(new UseSkill("enemy.tang_shouting", "skill.boss.undertow_hook", "char.lu_qinghe"), command);
        var charge = session.StepAi()!.Events;
        Assert.Contains(charge, e => e is ChargeStarted { SkillId: "skill.boss.undertow_hook", Target: "char.lu_qinghe" });
        Assert.Equal(10, state.EnemyMomentum);

        // 下轮主角先护住陆青禾：出手时由主角代受。
        IReadOnlyList<BattleEvent> release = [];
        for (var i = 0; i < 20 && release.Count == 0; i++)
        {
            if (session.AwaitingPlayer is { Id: "char.hero" } hero && engine.Validate(state, new UseSkill(hero.Id, "skill.fist.shield_palm", "char.lu_qinghe")) is null)
            {
                session.Submit(new UseSkill(hero.Id, "skill.fist.shield_palm", "char.lu_qinghe"));
            }
            else
            {
                var r = session.AwaitingPlayer is { } u ? session.Submit(new Defend(u.Id)) : session.StepAi()!;
                if (r.Events.OfType<SkillUsed>().Any(s => s.SkillId == "skill.boss.undertow_hook"))
                {
                    release = r.Events;
                }
            }
        }

        Assert.Contains(release, e => e is GuardIntercepted { Guardian: "char.hero", Protected: "char.lu_qinghe" });
    }
}
