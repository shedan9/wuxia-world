using WuxiaWorld.Domain.Combat;
using WuxiaWorld.Domain.Combat.Definitions;
using static WuxiaWorld.Domain.Tests.TestContent;

namespace WuxiaWorld.Domain.Tests;

/// <summary>回合次序、冷却、持续时间、控制、反应与校验（开发计划 6.1 T01–T04）。</summary>
public class BattleRulesTests
{
    // 必中测试招式：结果不受命中判定影响，只剩暴击与浮动（不影响“是否倒下”一类断言）。
    private static readonly SkillDefinition Smite = new()
    {
        Id = "skill.test.smite", SureHit = true, TargetRule = TargetRule.SingleAnyEnemy,
        Effects = [new EffectDefinition { Type = EffectType.Damage, Fixed = 5000 }],
    };

    private static readonly SkillDefinition Poke = new()
    {
        Id = "skill.test.poke", SureHit = true, TargetRule = TargetRule.SingleReachableEnemy,
        Effects = [new EffectDefinition { Type = EffectType.Damage, Fixed = 10 }],
    };

    private static readonly SkillDefinition Shoot = new()
    {
        Id = "skill.test.shoot", SureHit = true, TargetRule = TargetRule.SingleAnyEnemy,
        Effects = [new EffectDefinition { Type = EffectType.Damage, Fixed = 10 }],
    };

    private static readonly SkillDefinition Slow = new()
    {
        Id = "skill.test.slow", SureHit = true, TargetRule = TargetRule.SingleAnyEnemy,
        Effects = [new EffectDefinition { Type = EffectType.ApplyStatus, Status = "status.slow" }],
    };

    private static readonly SkillDefinition Stun = new()
    {
        Id = "skill.test.stun", SureHit = true, TargetRule = TargetRule.SingleAnyEnemy,
        Effects = [new EffectDefinition { Type = EffectType.ApplyStatus, Status = CoreIds.Stunned, ChanceBp = 20_000 }],
    };

    private static readonly SkillDefinition Crush = new()
    {
        Id = "skill.test.crush", SureHit = true, TargetRule = TargetRule.SingleAnyEnemy,
        Effects = [new EffectDefinition { Type = EffectType.StanceDamage, Amount = 999 }],
    };

    private static readonly SkillDefinition Kamikaze = new()
    {
        Id = "skill.test.kamikaze", SureHit = true, TargetRule = TargetRule.SingleAnyEnemy,
        Effects =
        [
            new EffectDefinition { Type = EffectType.Damage, Fixed = 5000 },
            new EffectDefinition { Type = EffectType.Damage, Fixed = 5000, Applies = EffectTarget.Self },
        ],
    };

    private static readonly SkillDefinition ChargedShot = new()
    {
        Id = "skill.test.charged_shot", SureHit = true, Charged = true, TargetRule = TargetRule.SingleAnyEnemy,
        Effects = [new EffectDefinition { Type = EffectType.Damage, Fixed = 10 }],
    };

    private static readonly SkillDefinition[] TestSkills = [Smite, Poke, Shoot, Slow, Stun, Crush, Kamikaze, ChargedShot];

    private static (BattleEngine Engine, BattleState State) Start(CombatantTemplate[] allies, CombatantTemplate[] enemies,
        (string Id, int Row, int Slot)[] allySlots, (string Id, int Row, int Slot)[] enemySlots, ulong seed = 1,
        IReadOnlyDictionary<string, int>? items = null, bool locked = false)
    {
        var encounter = new EncounterDefinition
        {
            Id = "battle.test.rules", Locked = locked,
            Enemies = [.. enemies.Select((t, i) => new EncounterSlot { Template = t.Id, UnitId = enemySlots[i].Id, Row = enemySlots[i].Row, Slot = enemySlots[i].Slot })],
        };
        var engine = new BattleEngine(With(allies.Concat(enemies).DistinctBy(t => t.Id), [encounter], TestSkills));
        var setup = new BattleSetup
        {
            EncounterId = encounter.Id, Seed = seed, Items = items ?? new Dictionary<string, int>(),
            Allies = [.. allies.Select((t, i) => Ally(t, allySlots[i].Id, allySlots[i].Row, allySlots[i].Slot))],
        };
        var (state, _) = engine.Start(setup);
        return (engine, state);
    }

    private static IReadOnlyList<BattleEvent> Do(BattleEngine engine, BattleState state, BattleCommand command)
    {
        var result = engine.Submit(state, command);
        Assert.True(result.Accepted, result.Rejection);
        return result.Events;
    }

    [Fact]
    public void T01_equal_speed_breaks_ties_by_stable_id()
    {
        var a = Unit("t.a", speed: 50, skills: ["skill.test.poke"]);
        var e = Unit("t.e", speed: 50);
        var (_, state) = Start([a, a], [e], [("ally.b", 0, 0), ("ally.a", 0, 1)], [("enemy.x", 0, 0)]);
        Assert.Equal(["ally.a", "ally.b", "enemy.x"], state.Order);
        Assert.Equal("ally.a", state.Pending);
    }

    [Fact]
    public void T01_downed_unit_never_acts()
    {
        var a = Unit("t.a", speed: 60, skills: ["skill.test.smite"]);
        var fast = Unit("t.fast", speed: 50, hp: 10);
        var slow = Unit("t.slow", speed: 40, hp: 500);
        var (engine, state) = Start([a], [fast, slow], [("ally.a", 0, 0)], [("enemy.a", 0, 0), ("enemy.b", 0, 1)]);
        var events = Do(engine, state, new UseSkill("ally.a", "skill.test.smite", "enemy.a"));
        Assert.Contains(events, e => e is UnitDowned { Unit: "enemy.a" });
        Assert.Equal("enemy.b", state.Pending);
        Assert.DoesNotContain(events, e => e is TurnStarted { Actor: "enemy.a" });
    }

    [Fact]
    public void Speed_change_applies_from_next_round()
    {
        var a = Unit("t.a", speed: 55, skills: ["skill.test.slow"]);
        var e = Unit("t.e", speed: 60);
        var (engine, state) = Start([a], [e], [("ally.a", 0, 0)], [("enemy.x", 0, 0)]);
        Assert.Equal(["enemy.x", "ally.a"], state.Order);
        Do(engine, state, new Defend("enemy.x"));
        Do(engine, state, new UseSkill("ally.a", "skill.test.slow", "enemy.x"));
        Assert.Equal(2, state.Round);
        Assert.Equal(["ally.a", "enemy.x"], state.Order);
    }

    [Fact]
    public void Cooldown_one_skips_exactly_the_next_own_action()
    {
        var a = Unit("t.a", speed: 60, skills: ["skill.sword.break_guard"]);
        var e = Unit("t.e", speed: 50, hp: 5000);
        var (engine, state) = Start([a], [e], [("ally.a", 0, 0)], [("enemy.x", 0, 0)]);
        Do(engine, state, new UseSkill("ally.a", "skill.sword.break_guard", "enemy.x"));
        Do(engine, state, new Defend("enemy.x"));
        Assert.NotNull(engine.Validate(state, new UseSkill("ally.a", "skill.sword.break_guard", "enemy.x")));
        Do(engine, state, new Defend("ally.a"));
        Do(engine, state, new Defend("enemy.x"));
        Assert.Null(engine.Validate(state, new UseSkill("ally.a", "skill.sword.break_guard", "enemy.x")));
    }

    [Fact]
    public void Defend_is_consumed_by_one_hit_and_otherwise_ends_at_next_own_action()
    {
        var a = Unit("t.a", speed: 60);
        var e = Unit("t.e", speed: 50, skills: ["skill.test.poke"]);
        var (engine, state) = Start([a], [e], [("ally.a", 0, 0)], [("enemy.x", 0, 0)]);
        Do(engine, state, new Defend("ally.a"));
        Assert.True(state.Unit("ally.a").HasStatus(CoreIds.Defend));
        var estimate = engine.Estimate(state, "enemy.x", "skill.test.poke", "ally.a")!;
        var events = Do(engine, state, new UseSkill("enemy.x", "skill.test.poke", "ally.a"));
        var hit = events.OfType<Damaged>().Single();
        Assert.InRange(hit.Amount, estimate.DamageMin, estimate.CritDamageMax);
        Assert.Contains(events, ev => ev is StatusRemoved { StatusId: CoreIds.Defend, Reason: StatusEndReason.Consumed });

        // 不挨打时，防御在自己下次行动开始时结束。
        var (engine2, state2) = Start([a], [e], [("ally.a", 0, 0)], [("enemy.x", 0, 0)]);
        Do(engine2, state2, new Defend("ally.a"));
        var next = Do(engine2, state2, new Defend("enemy.x"));
        Assert.Contains(next, ev => ev is StatusRemoved { Target: "ally.a", StatusId: CoreIds.Defend, Reason: StatusEndReason.Expired });
    }

    [Fact]
    public void Broken_cancels_reactions_lasts_to_target_action_end_and_restores_stance()
    {
        var a = Unit("t.a", speed: 60, skills: ["skill.test.crush", "skill.test.poke"]);
        var e = Unit("t.e", speed: 50, hp: 5000, stance: 40, skills: ["skill.enemy.brace"]);
        var (engine, state) = Start([a], [e], [("ally.a", 0, 0)], [("enemy.x", 0, 0)]);
        var events = Do(engine, state, new UseSkill("ally.a", "skill.test.crush", "enemy.x"));
        Assert.Contains(events, ev => ev is StanceBroken { Target: "enemy.x" });
        var enemy = state.Unit("enemy.x");
        Assert.True(enemy.HasStatus(CoreIds.Broken));
        Assert.False(engine.CanReact(enemy));

        // 破绽持续到目标自己的下次行动结束，结束时架势回满。
        var end = Do(engine, state, new Defend("enemy.x"));
        Assert.Contains(end, ev => ev is StatusRemoved { Target: "enemy.x", StatusId: CoreIds.Broken, Reason: StatusEndReason.Expired });
        Assert.Equal(40, enemy.Stance);
    }

    [Fact]
    public void Broken_is_consumed_by_the_next_damaging_hit()
    {
        var a = Unit("t.a", speed: 60, skills: ["skill.test.crush", "skill.test.poke"]);
        var b = Unit("t.b", speed: 55, skills: ["skill.test.poke"]);
        var e = Unit("t.e", speed: 50, hp: 5000, stance: 40);
        var (engine, state) = Start([a, b], [e], [("ally.a", 0, 0), ("ally.b", 0, 1)], [("enemy.x", 0, 0)]);
        Do(engine, state, new UseSkill("ally.a", "skill.test.crush", "enemy.x"));
        var events = Do(engine, state, new UseSkill("ally.b", "skill.test.poke", "enemy.x"));
        Assert.Contains(events, ev => ev is StatusRemoved { StatusId: CoreIds.Broken, Reason: StatusEndReason.Consumed });
        Assert.Equal(40, state.Unit("enemy.x").Stance);
    }

    [Fact]
    public void T03_stun_skips_one_action_then_grants_one_cycle_of_control_immunity()
    {
        var a = Unit("t.a", speed: 60, skills: ["skill.test.stun"]);
        var e = Unit("t.e", speed: 50, hp: 5000);
        var (engine, state) = Start([a], [e], [("ally.a", 0, 0)], [("enemy.x", 0, 0)]);

        var first = Do(engine, state, new UseSkill("ally.a", "skill.test.stun", "enemy.x"));
        Assert.Contains(first, ev => ev is ActionSkipped { Actor: "enemy.x" });
        Assert.True(state.Unit("enemy.x").HasStatus(CoreIds.ControlGuard));
        Assert.Equal("ally.a", state.Pending);

        // 免控期间再点穴无效，敌人正常行动。
        var second = Do(engine, state, new UseSkill("ally.a", "skill.test.stun", "enemy.x"));
        Assert.Contains(second, ev => ev is StatusResisted { Target: "enemy.x", StatusId: CoreIds.Stunned });
        Assert.Equal("enemy.x", state.Pending);
        Do(engine, state, new Defend("enemy.x"));
        Assert.False(state.Unit("enemy.x").HasStatus(CoreIds.ControlGuard));
    }

    [Fact]
    public void Boss_control_accumulates_to_threshold()
    {
        var a = Unit("t.a", speed: 60, skills: ["skill.test.stun"]);
        var boss = Unit("t.boss", speed: 50, hp: 5000, controlThreshold: 2);
        var (engine, state) = Start([a], [boss], [("ally.a", 0, 0)], [("enemy.boss", 0, 0)]);
        var first = Do(engine, state, new UseSkill("ally.a", "skill.test.stun", "enemy.boss"));
        Assert.Contains(first, ev => ev is ControlAccumulated { Meter: 1, Threshold: 2 });
        Assert.Equal("enemy.boss", state.Pending);
        Do(engine, state, new Defend("enemy.boss"));
        var second = Do(engine, state, new UseSkill("ally.a", "skill.test.stun", "enemy.boss"));
        Assert.Contains(second, ev => ev is ActionSkipped { Actor: "enemy.boss" });
    }

    [Fact]
    public void Counter_triggers_on_melee_only_and_once_per_round()
    {
        var a = Unit("t.a", speed: 60, hp: 5000, skills: ["skill.test.shoot"]);
        var b = Unit("t.b", speed: 55, hp: 5000, skills: ["skill.test.poke"]);
        var c = Unit("t.c", speed: 52, hp: 5000, skills: ["skill.test.poke"]);
        var e = Unit("t.e", speed: 50, hp: 5000, skills: ["skill.fist.counter_stance"]);
        var (engine, state) = Start([a, b, c], [e], [("ally.a", 1, 0), ("ally.b", 0, 1), ("ally.c", 0, 2)], [("enemy.x", 0, 0)]);

        // 第 1 轮：敌方最后行动，摆反击架势，持续到它下次行动开始。
        Do(engine, state, new Defend("ally.a"));
        Do(engine, state, new Defend("ally.b"));
        Do(engine, state, new Defend("ally.c"));
        Do(engine, state, new UseSkill("enemy.x", "skill.fist.counter_stance", "enemy.x"));
        Assert.Equal(2, state.Round);

        var ranged = Do(engine, state, new UseSkill("ally.a", "skill.test.shoot", "enemy.x"));
        Assert.DoesNotContain(ranged.OfType<SkillUsed>(), u => u.IsReaction);
        var melee = Do(engine, state, new UseSkill("ally.b", "skill.test.poke", "enemy.x"));
        var reaction = Assert.Single(melee.OfType<SkillUsed>(), u => u.IsReaction);
        Assert.Equal("enemy.x", reaction.Actor);
        var again = Do(engine, state, new UseSkill("ally.c", "skill.test.poke", "enemy.x"));
        Assert.DoesNotContain(again.OfType<SkillUsed>(), u => u.IsReaction);
    }

    [Fact]
    public void Reaction_does_not_trigger_another_reaction()
    {
        var a = Unit("t.a", speed: 60, hp: 5000, skills: ["skill.test.poke"]);
        var e = Unit("t.e", speed: 50, hp: 5000);
        var (engine, state) = Start([a], [e], [("ally.a", 0, 0)], [("enemy.x", 0, 0)]);
        // 双方同时带反击架势：ally.a 近身攻击 → enemy.x 反击一次；这次反击不再引发 ally.a 的反击。
        state.Unit("ally.a").Statuses.Add(new StatusInstance { StatusId = "status.counter_ready", Remaining = 5 });
        state.Unit("enemy.x").Statuses.Add(new StatusInstance { StatusId = "status.counter_ready", Remaining = 5 });
        var events = Do(engine, state, new UseSkill("ally.a", "skill.test.poke", "enemy.x"));
        var reaction = Assert.Single(events.OfType<SkillUsed>(), u => u.IsReaction);
        Assert.Equal("enemy.x", reaction.Actor);
        Assert.False(state.Unit("ally.a").ReactionUsed);
    }

    [Fact]
    public void Guard_redirects_a_single_target_attack_and_uses_the_guardian_reaction()
    {
        var guard = Unit("t.g", speed: 60, hp: 5000, skills: ["skill.fist.shield_palm"]);
        var ward = Unit("t.w", speed: 55, hp: 5000);
        var e = Unit("t.e", speed: 50, skills: ["skill.test.shoot"]);
        var (engine, state) = Start([guard, ward], [e], [("ally.g", 0, 0), ("ally.w", 1, 0)], [("enemy.x", 0, 0)]);
        Do(engine, state, new UseSkill("ally.g", "skill.fist.shield_palm", "ally.w"));
        Do(engine, state, new Defend("ally.w"));
        var events = Do(engine, state, new UseSkill("enemy.x", "skill.test.shoot", "ally.w"));
        Assert.Contains(events, ev => ev is GuardIntercepted { Guardian: "ally.g", Protected: "ally.w" });
        Assert.Equal("ally.g", events.OfType<Damaged>().Single().Target);
        Assert.False(state.Unit("ally.w").HasStatus(CoreIds.Guarded));
    }

    [Fact]
    public void T02_mutual_knockout_counts_as_defeat()
    {
        var a = Unit("t.a", speed: 60, skills: ["skill.test.kamikaze"]);
        var e = Unit("t.e", speed: 50);
        var (engine, state) = Start([a], [e], [("ally.a", 0, 0)], [("enemy.x", 0, 0)]);
        var events = Do(engine, state, new UseSkill("ally.a", "skill.test.kamikaze", "enemy.x"));
        Assert.Equal(BattleOutcome.Defeat, state.Outcome);
        Assert.Single(events.OfType<BattleEnded>());
    }

    [Fact]
    public void Bleed_ticks_at_owner_action_start_and_can_end_the_battle()
    {
        var a = Unit("t.a", speed: 60, skills: ["skill.test.poke"]);
        var e = Unit("t.e", speed: 50, hp: 5000);
        var (engine, state) = Start([a], [e], [("ally.a", 0, 0)], [("enemy.x", 0, 0)]);
        var enemy = state.Unit("enemy.x");
        Do(engine, state, new Defend("ally.a"));
        enemy.Statuses.Add(new StatusInstance { StatusId = "status.bleed", Stacks = 3, Remaining = 2 });
        enemy.Hp = 1;
        var events = Do(engine, state, new Defend("enemy.x"));
        // enemy.x 的回合已在上一条命令后开始，流血在下一次行动开始时结算。
        Assert.Equal(BattleOutcome.Ongoing, state.Outcome);
        events = Do(engine, state, new Defend("ally.a"));
        Assert.Contains(events, ev => ev is StatusTicked { Unit: "enemy.x", StatusId: "status.bleed" });
        Assert.Equal(BattleOutcome.Victory, state.Outcome);
    }

    [Fact]
    public void T04_rejected_commands_change_nothing()
    {
        var a = Unit("t.a", speed: 60, skills: ["skill.sword.break_guard", "skill.test.poke"]);
        var e1 = Unit("t.front", speed: 50, hp: 500);
        var e2 = Unit("t.back", speed: 40, hp: 500);
        var (engine, state) = Start([a], [e1, e2], [("ally.a", 0, 0)], [("enemy.f", 0, 0), ("enemy.b", 1, 0)], locked: true,
            items: new Dictionary<string, int> { ["item.medicine.golden_sore"] = 1 });
        var hash = state.Hash();

        state.Unit("ally.a").Inner = 0;
        var noInner = engine.Submit(state, new UseSkill("ally.a", "skill.sword.break_guard", "enemy.f"));
        Assert.False(noInner.Accepted);
        Assert.Equal("内力不足。", noInner.Rejection);
        state.Unit("ally.a").Inner = state.Unit("ally.a").Stats.MaxInner;
        Assert.Equal(hash, state.Hash());

        Assert.False(engine.Submit(state, new UseSkill("ally.a", "skill.test.poke", "enemy.b")).Accepted);
        Assert.False(engine.Submit(state, new UseItem("ally.a", "item.medicine.golden_sore", "ally.a")).Accepted);
        Assert.False(engine.Submit(state, new Retreat("ally.a")).Accepted);
        Assert.False(engine.Submit(state, new Defend("enemy.f")).Accepted);
        Assert.False(engine.Submit(state, new UseSkill("ally.a", "skill.fist.heavy_palm", "enemy.f")).Accepted);
        Assert.Equal(hash, state.Hash());
    }

    [Fact]
    public void Items_are_spent_once_and_removed_at_zero()
    {
        var a = Unit("t.a", speed: 60);
        var e = Unit("t.e", speed: 50, skills: ["skill.test.poke"]);
        var (engine, state) = Start([a], [e], [("ally.a", 0, 0)], [("enemy.x", 0, 0)],
            items: new Dictionary<string, int> { ["item.medicine.golden_sore"] = 1 });
        state.Unit("ally.a").Hp -= 100;
        var events = Do(engine, state, new UseItem("ally.a", "item.medicine.golden_sore", "ally.a"));
        Assert.Contains(events, ev => ev is Healed { Amount: 90 });
        Assert.False(state.Items.ContainsKey("item.medicine.golden_sore"));
    }

    [Fact]
    public void Swap_moves_to_empty_slot_or_exchanges_with_ally()
    {
        var a = Unit("t.a", speed: 60);
        var b = Unit("t.b", speed: 55);
        var e = Unit("t.e", speed: 50);
        var (engine, state) = Start([a, b], [e], [("ally.a", 0, 0), ("ally.b", 1, 0)], [("enemy.x", 0, 0)]);
        Do(engine, state, new Swap("ally.a", 1, 0));
        Assert.Equal(new Position(1, 0), state.Unit("ally.a").Position);
        Assert.Equal(new Position(0, 0), state.Unit("ally.b").Position);
        Do(engine, state, new Swap("ally.b", 0, 2));
        Assert.Equal(new Position(0, 2), state.Unit("ally.b").Position);
    }

    [Fact]
    public void Charged_skill_telegraphs_then_releases_and_retargets_if_target_fell()
    {
        var a = Unit("t.a", speed: 60, skills: ["skill.test.smite"]);
        var b = Unit("t.b", speed: 55, hp: 5000);
        var boss = Unit("t.boss", speed: 50, hp: 5000, skills: ["skill.test.charged_shot"]);
        var (engine, state) = Start([a, b], [boss], [("ally.a", 0, 0), ("ally.b", 0, 1)], [("enemy.boss", 0, 0)]);
        Do(engine, state, new Defend("ally.a"));
        Do(engine, state, new Defend("ally.b"));

        // 蓄力目标在出手前倒下（下轮行动开始时流血身亡）：出手时改打第一个合法目标，不需要再下令。
        var doomed = state.Unit("ally.a");
        doomed.Hp = 1;
        doomed.Statuses.Add(new StatusInstance { StatusId = "status.bleed", Remaining = 2 });
        var charge = Do(engine, state, new UseSkill("enemy.boss", "skill.test.charged_shot", "ally.a"));
        Assert.Contains(charge, ev => ev is ChargeStarted { SkillId: "skill.test.charged_shot", Target: "ally.a" });
        Assert.Contains(charge, ev => ev is UnitDowned { Unit: "ally.a" });
        Assert.DoesNotContain(charge, ev => ev is Damaged);

        var round = Do(engine, state, new Defend("ally.b"));
        var release = round.OfType<SkillUsed>().Single(u => u.Actor == "enemy.boss");
        Assert.Equal(["ally.b"], release.Targets);
        Assert.Equal("ally.b", state.Pending);
    }

    [Fact]
    public void Breaking_stance_interrupts_a_charge()
    {
        var a = Unit("t.a", speed: 60, skills: ["skill.test.crush"]);
        var boss = Unit("t.boss", speed: 70, hp: 5000, stance: 40, skills: ["skill.test.charged_shot"]);
        var (engine, state) = Start([a], [boss], [("ally.a", 0, 0)], [("enemy.boss", 0, 0)]);
        Do(engine, state, new UseSkill("enemy.boss", "skill.test.charged_shot", "ally.a"));
        var events = Do(engine, state, new UseSkill("ally.a", "skill.test.crush", "enemy.boss"));
        Assert.Contains(events, ev => ev is ChargeInterrupted { Actor: "enemy.boss" });
        Assert.False(state.Unit("enemy.boss").HasStatus(CoreIds.Charging));
    }
}
