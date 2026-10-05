using WuxiaWorld.Application.Combat;
using WuxiaWorld.Application.Dev;
using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.Combat;
using WuxiaWorld.Domain.Combat.Ai;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Domain.Common;
using WuxiaWorld.Domain.World;

namespace WuxiaWorld.Domain.Tests;

/// <summary>
/// 第一章讨教后的后院切磋（M3 第十五批，架构文档 7.7）：点到为止的胜负规则、单人上场不带药、输赢都能往下走，
/// 以及按讨教所学的那一手（破招、点穴、防住蓄力掌）应对时能赢。
/// </summary>
public class SparTests
{
    public static TheoryData<string> Mentors => ["linghu", "huang", "xiao"];

    private static readonly Dictionary<string, string> Builds = new() { ["linghu"] = "sword", ["huang"] = "inner", ["xiao"] = "fist" };

    private static GameSession NewGame()
    {
        var rules = ChapterOneWalkthroughTests.Rules();
        return GameSession.NewGame(rules, new GrowthRules(rules, TestContent.Real));
    }

    /// <summary>走到讨教之后、切磋开打之前（主角潜能按推荐分配，像真实玩家那样）。</summary>
    private static ChapterOneRoute ToSpar(string mentor, string result = "won")
    {
        var route = new ChapterOneRoute(NewGame()) { Companion = mentor, Spar = result, AllocatePotential = true, Combat = TestContent.Real };
        route.RunTo(ChapterOnePoint.SparBattle);
        return route;
    }

    /// <summary>
    /// 照着讨教教的做：破招的那一手（破势剑 / 点穴手）平时留着，对手蓄力时能打断就打断，打不断就防御；其余按贪心出招。
    /// </summary>
    private static BattleCommand Aware(BattleEngine engine, BattleState state, BattleUnit unit)
    {
        var charging = state.Living(Side.Enemy).FirstOrDefault(t => t.HasStatus(CoreIds.Charging));
        if (charging is null)
        {
            var reserve = unit.Template.Loadout.Skills.Contains("skill.sword.break_guard") ? "skill.sword.break_guard" : "skill.inner.pressure_point";
            return BattleAi.BestAttack(engine, state, unit, skirmish: false, exclude: reserve);
        }

        if (engine.Estimate(state, unit.Id, "skill.sword.break_guard", charging.Id) is { Breaks: true }
            && engine.Validate(state, new UseSkill(unit.Id, "skill.sword.break_guard", charging.Id)) is null)
        {
            return new UseSkill(unit.Id, "skill.sword.break_guard", charging.Id);
        }

        return engine.Validate(state, new UseSkill(unit.Id, "skill.inner.pressure_point", charging.Id)) is null
            ? new UseSkill(unit.Id, "skill.inner.pressure_point", charging.Id)
            : new Defend(unit.Id);
    }

    private static BattleCommand BasicOnly(BattleEngine engine, BattleState state, BattleUnit unit) =>
        state.Living(Side.Enemy).FirstOrDefault() is { } foe ? new UseSkill(unit.Id, CoreIds.BasicAttack, foe.Id) : new Defend(unit.Id);

    private static (BattleState State, List<BattleEvent> Events) Fight(BattleEngine engine, BattleSetup setup,
        Func<BattleEngine, BattleState, BattleUnit, BattleCommand> policy)
    {
        var session = new BattleSession(engine, setup);
        var events = new List<BattleEvent>(session.StartEvents);
        for (var step = 0; step < 400 && !session.Ended; step++)
        {
            var result = session.AwaitingPlayer is { } unit ? session.Submit(policy(engine, session.State, unit)) : session.StepAi()!;
            if (!result.Accepted)
            {
                result = session.Submit(new Defend(session.AwaitingPlayer!.Id));
            }

            events.AddRange(result.Events);
        }

        Assert.True(session.Ended, "切磋 400 步内没打完");
        return (session.State, events);
    }

    [Theory]
    [MemberData(nameof(Mentors))]
    public void A_spar_is_the_hero_alone_without_medicine_against_the_mentor(string mentor)
    {
        var route = ToSpar(mentor);
        var game = route.Game;
        Assert.Contains("char.lu_qinghe", game.World.Party);
        Assert.True(game.World.Items.Values.Sum() > 0, "行囊里应有开局的药，才能核对切磋不带药");

        var setup = game.StoryBattleSetup(seed: 3);
        Assert.Equal($"battle.01.spar_{mentor}", setup.EncounterId);
        var hero = Assert.Single(setup.Allies);
        Assert.Equal("char.hero", hero.UnitId);
        Assert.Equal(new Position(0, 1), hero.Position);
        Assert.Empty(setup.Items);
        Assert.Equal(0, game.Growth!.Unspent(game.World, "char.hero"));
    }

    [Theory]
    [MemberData(nameof(Mentors))]
    public void Nobody_goes_down_and_the_spar_ends_when_one_side_reaches_the_yield_line(string mentor)
    {
        var engine = new BattleEngine(TestContent.Real);
        var setup = ToSpar(mentor).Game.StoryBattleSetup(seed: 1);
        var encounter = TestContent.Real.Encounter(setup.EncounterId);
        Assert.Equal(VictoryRule.Spar, encounter.Victory);
        foreach (var seed in Enumerable.Range(1, 12))
        {
            foreach (var policy in new Func<BattleEngine, BattleState, BattleUnit, BattleCommand>[] { Aware, BasicOnly })
            {
                var (state, events) = Fight(engine, setup with { Seed = (ulong)seed }, policy);
                Assert.DoesNotContain(events, e => e is UnitDowned);
                Assert.All(state.Units, u => Assert.True(u.Hp >= 1, $"{u.Id} 气血 {u.Hp}"));
                Assert.Contains(state.Outcome, new[] { BattleOutcome.Victory, BattleOutcome.Defeat });

                var loser = state.Outcome == BattleOutcome.Victory ? state.Unit(encounter.VictoryUnit!) : state.Unit("char.hero");
                Assert.True((long)loser.Hp * Bp.One <= (long)loser.Stats.MaxHp * encounter.YieldBp);
                var yielded = events.OfType<UnitYielded>().Select(y => y.Unit).ToList();
                Assert.Contains(loser.Id, yielded);

                // 认输紧跟在结束之前。
                var end = events.FindIndex(e => e is BattleEnded);
                Assert.IsType<UnitYielded>(events[end - 1]);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Mentors))]
    public void Applying_the_lesson_wins_most_spars_and_beats_plain_attacks(string mentor)
    {
        var engine = new BattleEngine(TestContent.Real);
        var setup = ToSpar(mentor).Game.StoryBattleSetup(seed: 1);
        Assert.Contains(setup.Allies[0].Template.Loadout.Skills, s => s.StartsWith($"skill.{Builds[mentor]}.", StringComparison.Ordinal));

        int Wins(Func<BattleEngine, BattleState, BattleUnit, BattleCommand> policy) =>
            Enumerable.Range(1, 30).Count(seed => Fight(engine, setup with { Seed = (ulong)seed }, policy).State.Outcome == BattleOutcome.Victory);

        var aware = Wins(Aware);
        var plain = Wins(BasicOnly);
        Assert.True(aware >= 24, $"{mentor}：照着教的打只赢 {aware}/30");
        Assert.True(aware > plain, $"{mentor}：照着教的打 {aware}/30 不比只用普通攻击 {plain}/30 强");
    }

    [Fact]
    public void The_mentor_telegraphs_from_round_two_and_breaking_the_charge_interrupts_it()
    {
        var engine = new BattleEngine(TestContent.Real);
        var setup = ToSpar("linghu").Game.StoryBattleSetup(seed: 5);
        var (_, events) = Fight(engine, setup, Aware);
        var charge = events.OfType<ChargeStarted>().First();
        Assert.Equal("spar.linghu_chong", charge.Actor);
        Assert.Equal("skill.spar.sword_lunge", charge.SkillId);
        Assert.Equal("char.hero", charge.Target);
        var round = events.Take(events.IndexOf(charge)).OfType<RoundStarted>().Last().Round;
        Assert.True(round >= 2, $"第 {round} 轮就蓄力了");
        Assert.Contains(events, e => e is ChargeInterrupted { Actor: "spar.linghu_chong" });
    }

    [Fact]
    public void Conceding_a_spar_always_works()
    {
        var engine = new BattleEngine(TestContent.Real);
        var setup = ToSpar("xiao").Game.StoryBattleSetup(seed: 2);
        var session = new BattleSession(engine, setup);
        while (session.AwaitingPlayer is null && !session.Ended)
        {
            session.StepAi();
        }

        var r = session.Submit(new Retreat(session.AwaitingPlayer!.Id));
        Assert.True(r.Accepted);
        Assert.Contains(r.Events, e => e is RetreatAttempted { Success: true, ChanceBp: Bp.One });
        Assert.Equal(BattleOutcome.Retreated, session.State.Outcome);
    }

    [Theory]
    [InlineData("won", 30, 20)]
    [InlineData("lost", 15, 10)]
    public void Either_result_moves_on_with_the_mentor_review_and_closes_the_offer(string result, int experience, int cultivation)
    {
        var route = ToSpar("huang", result);
        var game = route.Game;
        var exp = game.World.Experience;
        var cult = game.World.Cultivation;
        var trust = game.World.Relationship("char.huang_rong").Trust;
        var pending = game.World.Battle!;
        Assert.False(pending.Retry);

        var won = result == "won";
        var encounter = TestContent.Real.Encounter(pending.Encounter);
        Assert.True(game.SettleBattle(pending.InstanceId, won ? BattleEnd.Victory : BattleEnd.Defeat,
            won ? encounter.Experience : 0, cultivation: won ? encounter.Cultivation : 0).Ok);

        Assert.Null(game.World.Battle);
        Assert.Equal(result, game.World.Facts["fact.ch01.spar"]);
        Assert.Equal(exp + experience, game.World.Experience);
        Assert.Equal(cult + cultivation, game.World.Cultivation);
        Assert.Equal(trust + 1, game.World.Relationship("char.huang_rong").Trust);
        Assert.Equal("event.ch01.mentor_spar_after", game.AutoEvent?.Id);
        Assert.DoesNotContain(game.Events, e => e.Id.StartsWith("event.ch01.mentor_spar_", StringComparison.Ordinal) && !e.Auto);

        route.PlayAuto();
        Assert.Null(game.AutoEvent);
        Assert.Equal("map.jiangnan.inn_yard", game.World.MapId);
        route.Exit("to_hall");
        Assert.Contains(game.Events, e => e.Id == "event.ch01.companion_choice");
    }

    [Fact]
    public void Declining_keeps_the_offer_until_a_companion_is_chosen_and_only_the_mentor_offers()
    {
        var route = new ChapterOneRoute(NewGame()) { Companion = "xiao", Mentor = "linghu" };
        route.RunTo(ChapterOnePoint.SparBattle);
        var game = route.Game;
        Assert.Equal(["event.ch01.mentor_spar_linghu"], game.Events.Where(e => e.Id.StartsWith("event.ch01.mentor_spar", StringComparison.Ordinal)).Select(e => e.Id));

        route.Pick("choice.later");
        route.PlayEvent("event.ch01.mentor_spar_linghu");
        Assert.Null(game.World.Battle);
        Assert.Contains(game.Events, e => e.Id == "event.ch01.mentor_spar_linghu");

        route.Exit("to_hall");
        route.Pick("choice.xiao");
        route.PlayEvent("event.ch01.companion_choice");
        route.Exit("to_yard");
        Assert.DoesNotContain(game.Events, e => e.Id.StartsWith("event.ch01.mentor_spar", StringComparison.Ordinal));
    }

    [Fact]
    public void Spar_barks_praise_the_interrupt_and_the_hero_says_thanks_instead_of_the_rescue_line()
    {
        var engine = new BattleEngine(TestContent.Real);
        var setup = ToSpar("linghu").Game.StoryBattleSetup(seed: 5);
        var session = new BattleSession(engine, setup);
        var director = new BattleBarkDirector(ChapterOneWalkthroughTests.Bundle.Value.Barks, session.State);
        var spoken = new List<string>();
        Assert.Equal("ch01.battle.spar.linghu.start", director.Start(BarkMode.Normal)?.LineId);

        void Feed(IEnumerable<BattleEvent> events)
        {
            foreach (var e in events)
            {
                if (director.Observe(e, BarkMode.Normal) is { } bark)
                {
                    spoken.Add(bark.LineId);
                }
            }
        }

        Feed(session.StartEvents);
        while (!session.Ended)
        {
            Feed((session.AwaitingPlayer is { } unit ? session.Submit(Aware(engine, session.State, unit)) : session.StepAi()!).Events);
        }

        Assert.Equal(BattleOutcome.Victory, session.State.Outcome);
        Assert.Contains("ch01.battle.spar.linghu.charge", spoken);
        Assert.Contains("ch01.battle.spar.linghu.interrupted", spoken);
        Assert.Equal("ch01.battle.spar.linghu.hero.victory", spoken[^1]);
        Assert.DoesNotContain("ch01.battle.hero.low_hp", spoken);
        Assert.DoesNotContain("ch01.battle.hero.victory", spoken);
    }
}
