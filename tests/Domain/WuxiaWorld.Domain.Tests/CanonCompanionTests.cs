using WuxiaWorld.Application.Combat;
using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.Characters;
using WuxiaWorld.Domain.Combat;
using WuxiaWorld.Domain.Combat.Ai;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Infrastructure.Content;

namespace WuxiaWorld.Domain.Tests;

/// <summary>
/// 第一章三侠的个人战斗模板与剧情先手的遭遇变体（M3 前置，架构文档 7.6、9.4.5）：
/// 三条同行者路线都用真实内核把两场剧情战打完，核对上场模板、生效的变体与战果。
/// </summary>
public class CanonCompanionTests
{
    private static readonly string[] Canon = ["char.linghu_chong", "char.huang_rong", "char.xiao_feng"];

    private static GameSession NewGame(WorldRules rules) => GameSession.NewGame(rules, new GrowthRules(rules, TestContent.Real));

    [Fact]
    public void Classic_companions_use_their_own_templates_with_canon_skills_and_do_not_level_with_the_hero()
    {
        var rules = ChapterOneWalkthroughTests.Rules();
        var g = NewGame(rules);
        var growth = g.Growth!;
        var s = g.World.Clone();
        rules.Apply(s, [new WorldEffect { Type = WorldEffectType.GrantExperience, Amount = rules.ExperienceFor(8) }], new EffectResult(), "test");
        foreach (var who in Canon)
        {
            var t = growth.Template(s, who);
            Assert.NotEqual(GrowthRules.PlaceholderCompanion, t.Id);
            Assert.Equal(7, t.Level);
            // 个人招式：别的战斗模板都不用的招式至少两门（主角讨教学的是原创通用武学，不是三侠的独门功夫）。
            var others = TestContent.Real.Combatants.Values.Where(c => c.Id != t.Id).SelectMany(c => c.Loadout.Skills).ToHashSet(StringComparer.Ordinal);
            var personal = t.Loadout.Skills.Where(id => !others.Contains(id)).ToList();
            Assert.True(personal.Count >= 2, $"{who} 的个人招式不足两门");
            Assert.DoesNotContain(personal, id => rules.Content.Progression.Styles.Any(st => st.Skills.Contains(id, StringComparer.Ordinal)));
            Assert.All(t.Loadout.Skills, id => Assert.True(TestContent.Real.Skills.ContainsKey(id), id));
            Assert.Equal(t.Attributes.Total, 25 + StatFormula.PotentialAt(7));
        }
    }

    public static TheoryData<string, bool, string[], string[]> Routes => new()
    {
        { "linghu", false, ["variant.ch01.disguise_seen"], [] },
        { "huang", false, [], ["variant.ch01.sluice_jammed"] },
        { "xiao", false, [], ["variant.ch01.ferryman_freed"] },
        { "linghu", true, ["variant.ch01.disguise_seen"], ["variant.ch01.ferryman_freed"] },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public void Each_companion_route_wins_both_story_battles_with_its_head_start(string companion, bool side, string[] escortVariants, string[] sluiceVariants)
    {
        var rules = ChapterOneWalkthroughTests.Rules();
        var engine = new BattleEngine(TestContent.Real);
        var fought = new List<(BattleSetup Setup, BattleState End)>();
        var w = new ChapterOneWalkthroughTests.Walker(rules, NewGame(rules))
        {
            Fighter = game =>
            {
                var pending = game.World.Battle!;
                var setup = game.StoryBattleSetup(seed: 7);
                var session = new BattleSession(engine, setup);
                for (var step = 0; step < 800 && !session.Ended; step++)
                {
                    _ = session.AwaitingPlayer is { } unit
                        ? session.Submit(BattleAi.BestAttack(engine, session.State, unit, skirmish: false))
                        : session.StepAi();
                }

                fought.Add((setup, session.State));
                Assert.Equal(BattleOutcome.Victory, session.State.Outcome);
                var encounter = TestContent.Real.Encounter(pending.Encounter);
                Assert.True(game.SettleBattle(pending.InstanceId, BattleEnd.Victory, encounter.Experience, cultivation: encounter.Cultivation).Ok);
            },
        };

        w.PlayChapter(companion, "public", side);
        Assert.Equal(QuestStatus.Completed, w.Game.World.QuestStatusOf("quest.main.01.jiangnan_guest"));
        Assert.Equal(2, fought.Count);

        var (escort, escortEnd) = fought[0];
        var (sluice, sluiceEnd) = fought[1];
        Assert.Equal("battle.01.escort_skirmish", escort.EncounterId);
        Assert.Equal(escortVariants, escort.Variants);
        Assert.Equal(sluiceVariants, sluice.Variants);

        // 两部以上作品的经典人物同场：旧渡战主要同行者与援手都用各自的个人模板上场，并真的出过个人招式。
        var canonAtSluice = sluice.Allies.Where(a => Canon.Contains(a.UnitId)).ToList();
        Assert.Equal(2, canonAtSluice.Count);
        Assert.All(canonAtSluice, a => Assert.Equal("combatant." + a.UnitId["char.".Length..], a.Template.Id));
        Assert.Single(escort.Allies, a => Canon.Contains(a.UnitId));
        Assert.True(escortEnd.Round <= 8 && sluiceEnd.Round <= 14, $"轮数异常：{escortEnd.Round} / {sluiceEnd.Round}");
    }

    [Fact]
    public void Variants_change_the_opening_state_and_unknown_ones_are_rejected()
    {
        var engine = new BattleEngine(TestContent.Real);
        AllyEntry[] allies = [new(TestContent.Real.Combatant("combatant.hero.sword"), "char.hero", new Position(0, 1))];
        BattleState Open(params string[] variants) =>
            engine.Start(new BattleSetup { EncounterId = "battle.01.old_ferry_sluice", Seed = 3, Allies = allies, Variants = variants }).State;

        var plain = Open();
        var jammed = Open("variant.ch01.sluice_jammed");
        var gate = plain.Unit("enemy.sluice_gate");
        Assert.Equal(gate.Stats.MaxHp, gate.Hp);
        Assert.Equal(gate.Stats.MaxHp * 5500 / 10_000, jammed.Unit("enemy.sluice_gate").Hp);

        var freed = Open("variant.ch01.ferryman_freed");
        Assert.True(freed.Unit("enemy.sluice_gate").HasStatus("status.sluice_slack"));

        // 杜三篙已获救：水门第一次行动被拖住，水位比平常晚一层。
        int WaterAfterRounds(BattleState start, int rounds)
        {
            var s = start;
            for (var i = 0; i < 400 && s.Round <= rounds && s.Outcome == BattleOutcome.Ongoing; i++)
            {
                var actor = s.Unit(s.Pending!);
                BattleCommand cmd = actor.Side == Side.Ally ? new Defend(actor.Id) : BattleAi.Decide(engine, s);
                Assert.True(engine.Submit(s, cmd).Accepted);
            }

            return s.Unit("enemy.sluice_gate").StacksOf("status.water_level");
        }

        Assert.Equal(WaterAfterRounds(plain.Clone(), 2) - 1, WaterAfterRounds(freed.Clone(), 2));

        var escort = engine.Start(new BattleSetup
        {
            EncounterId = "battle.01.escort_skirmish", Seed = 3, Allies = allies, Variants = ["variant.ch01.disguise_seen"],
        }).State;
        Assert.All(escort.Units.Where(u => u.Side == Side.Enemy), u => Assert.True(u.HasStatus("status.insight"), u.Id));

        Assert.Throws<ArgumentException>(() => Open("variant.ch01.disguise_seen"));
    }

    [Fact]
    public void Validator_reports_broken_variants()
    {
        var b = TestContent.Bundle;
        var sluice = b.Encounters.First(e => e.Id == "battle.01.old_ferry_sluice");
        var broken = b with
        {
            Encounters =
            [
                .. b.Encounters.Where(e => e != sluice),
                sluice with
                {
                    Variants =
                    [
                        .. sluice.Variants,
                        new EncounterVariant { Id = "variant.ch01.sluice_jammed", WhenFact = "fact.x", Hp = [new VariantHp("enemy.nobody", 0)] },
                        new EncounterVariant { Id = "variant.test.empty", WhenFact = "fact.x" },
                        new EncounterVariant
                        {
                            Id = "variant.test.spawned", WhenFact = "fact.x",
                            Statuses = [new PhaseStatus("enemy.yard_guard", "status.missing")],
                        },
                    ],
                },
            ],
        };
        var errors = CombatContentValidator.Validate(broken);
        Assert.Contains(errors, e => e.Contains("变体 ID 重复", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("气血目标不在开局阵容里 enemy.nobody", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("气血比例须在", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("variant.test.empty：变体没有任何改动", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("状态目标不在开局阵容里 enemy.yard_guard", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("状态不存在或写了 remove status.missing", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("缺少文本 variant.test.empty.name", StringComparison.Ordinal));

        // 世界侧：没有内容会写出的事实值，变体永远不会生效。
        var worldErrors = WorldContentValidator.Validate(ChapterOneWalkthroughTests.Bundle.Value, broken);
        Assert.Contains(worldErrors, e => e.Contains("没有内容会把 fact.x 设为 true", StringComparison.Ordinal));
        Assert.DoesNotContain(worldErrors, e => e.Contains("fact.ch01.sluice_jammed", StringComparison.Ordinal));
    }
}
