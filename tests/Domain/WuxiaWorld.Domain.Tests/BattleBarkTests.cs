using WuxiaWorld.Application.Combat;
using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.Combat;
using WuxiaWorld.Domain.Combat.Ai;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Infrastructure.Content;

namespace WuxiaWorld.Domain.Tests;

/// <summary>
/// 第一章关键战斗语音（开发计划 M3-06）：喊声数据的校验，<see cref="BattleBarkDirector"/> 的挑选规则，
/// 以及三条同行者路线用真实内核打两场剧情战时实际说出的喊声。
/// </summary>
public class BattleBarkTests
{
    private static IEnumerable<BattleBarkDefinition> Barks => ChapterOneWalkthroughTests.Bundle.Value.Barks;

    private static GameSession NewGame(WorldRules rules) => GameSession.NewGame(rules, new GrowthRules(rules, TestContent.Real));

    public static TheoryData<string, string, string> Routes => new()
    {
        { "linghu", "char.linghu_chong", "ch01.battle.escort.linghu.start" },
        { "huang", "char.huang_rong", "ch01.battle.escort.huang.start" },
        { "xiao", "char.xiao_feng", "ch01.battle.escort.xiao.start" },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public void Story_battles_open_with_the_companion_and_the_boss_and_end_with_the_finisher(string companion, string companionId, string escortStart)
    {
        var rules = ChapterOneWalkthroughTests.Rules();
        var engine = new BattleEngine(TestContent.Real);
        var spoken = new List<(string Encounter, BattleBarkDefinition Bark, int Round, string? Turn)>();
        var w = new ChapterOneWalkthroughTests.Walker(rules, NewGame(rules))
        {
            Fighter = game =>
            {
                var pending = game.World.Battle!;
                var session = new BattleSession(engine, game.StoryBattleSetup(seed: 7));
                var director = new BattleBarkDirector(Barks, session.State);
                var round = 0;
                string? turn = null;
                string? lastAlly = null;

                void Note(BattleBarkDefinition? bark)
                {
                    if (bark is not null)
                    {
                        Assert.False(session.State.TryUnit(bark.UnitId) is null, $"{bark.LineId} 的单位不在场");
                        spoken.Add((pending.Encounter, bark, round, turn));
                    }
                }

                void Feed(IEnumerable<BattleEvent> events)
                {
                    foreach (var e in events)
                    {
                        (round, turn) = e switch
                        {
                            RoundStarted r => (r.Round, null),
                            TurnStarted t => (round, t.Actor),
                            _ => (round, turn),
                        };
                        if (e is SkillUsed { IsReaction: false } u && session.State.Unit(u.Actor).Side == Side.Ally)
                        {
                            lastAlly = u.Actor;
                        }

                        if (e is BattleEnded)
                        {
                            // 胜利句由最后出手的我方人物说（他仍站着时）。
                            var bark = director.Observe(e, BarkMode.Normal);
                            Assert.NotNull(bark);
                            Assert.Equal(BarkTrigger.Victory, bark.Trigger);
                            if (!session.State.Unit(lastAlly!).IsDown)
                            {
                                Assert.Equal(lastAlly, bark.UnitId);
                            }

                            Note(bark);
                            continue;
                        }

                        Note(director.Observe(e, BarkMode.Normal));
                    }
                }

                Note(director.Start(BarkMode.Normal));
                Feed(session.StartEvents);
                for (var step = 0; step < 800 && !session.Ended; step++)
                {
                    var result = session.AwaitingPlayer is { } unit
                        ? session.Submit(BattleAi.BestAttack(engine, session.State, unit, skirmish: false))
                        : session.StepAi()!;
                    Feed(result.Events);
                }

                Assert.Equal(BattleOutcome.Victory, session.State.Outcome);
                var encounter = TestContent.Real.Encounter(pending.Encounter);
                Assert.True(game.SettleBattle(pending.InstanceId, BattleEnd.Victory, encounter.Experience, cultivation: encounter.Cultivation).Ok);
            },
        };

        w.PlayChapter(companion, "public", side: false);

        var escort = spoken.Where(s => s.Encounter == "battle.01.escort_skirmish").ToList();
        var sluice = spoken.Where(s => s.Encounter == "battle.01.old_ferry_sluice").ToList();
        Assert.Equal(escortStart, escort[0].Bark.LineId);
        Assert.Equal("ch01.battle.sluice.tang.start", sluice[0].Bark.LineId);

        Assert.All(new[] { escort, sluice }, fight => Assert.Equal(BarkTrigger.Victory, fight[^1].Bark.Trigger));

        // 经典人物的招式句真的说出过，且都属于本人。
        Assert.Contains(spoken, s => s.Bark.Trigger == BarkTrigger.Skill && s.Bark.Speaker == companionId);

        foreach (var fight in new[] { escort, sluice })
        {
            // 一场一次的句子不重复；招式句隔够冷却轮数。
            foreach (var g in fight.GroupBy(s => s.Bark.LineId))
            {
                var rounds = g.Select(s => s.Round).ToList();
                var cooldown = g.First().Bark.CooldownRounds;
                Assert.True(cooldown > 0 || rounds.Count == 1, $"{g.Key} 说了 {rounds.Count} 次");
                Assert.All(rounds.Zip(rounds.Skip(1)), p => Assert.True(p.Second - p.First >= cooldown, $"{g.Key} 冷却不足"));
            }

            // 普通句同一人至少隔两轮。
            foreach (var g in fight.Where(s => s.Bark.Priority == 1).GroupBy(s => s.Bark.UnitId))
            {
                var rounds = g.Select(s => s.Round).ToList();
                Assert.All(rounds.Zip(rounds.Skip(1)), p => Assert.True(p.Second - p.First >= BattleBarkDirector.OrdinaryGapRounds, $"{g.Key} 普通句太密"));
            }

            // 同一人的回合里，后一句一定比前一句更要紧。
            foreach (var g in fight.Where(s => s.Turn is not null && s.Bark.Trigger != BarkTrigger.Victory).GroupBy(s => (s.Round, s.Turn)))
            {
                var priorities = g.Select(s => s.Bark.Priority).ToList();
                Assert.True(priorities.Zip(priorities.Skip(1)).All(p => p.Second > p.First), $"第 {g.Key.Round} 轮 {g.Key.Turn} 的回合连喊：{string.Join("、", g.Select(s => s.Bark.LineId))}");
            }
        }
    }

    [Fact]
    public void Director_respects_priority_cooldown_speed_and_the_fallen()
    {
        var engine = new BattleEngine(TestContent.Real);
        var setup = new BattleSetup
        {
            EncounterId = "battle.01.old_ferry_sluice", Seed = 1,
            Allies =
            [
                new AllyEntry(TestContent.Real.Combatant("combatant.linghu_chong"), "char.linghu_chong", new Position(0, 1)),
                new AllyEntry(TestContent.Real.Combatant("combatant.lu_qinghe"), "char.lu_qinghe", new Position(1, 1)),
            ],
        };
        var state = new BattleSession(engine, setup).State;
        var linghu = state.Unit("char.linghu_chong");
        BattleBarkDefinition Bark(string id, BarkTrigger trigger, int priority, string speaker = "char.linghu_chong", string? skill = null, int cooldown = 0) =>
            new() { LineId = id, Speaker = speaker, Trigger = trigger, Priority = priority, Skill = skill, CooldownRounds = cooldown, Text = "x" };
        var lines = new[]
        {
            Bark("t.start.hero", BarkTrigger.BattleStart, 2, speaker: "char.hero"), // 主角不在场
            Bark("t.start.lu", BarkTrigger.BattleStart, 2, speaker: "char.lu_qinghe"),
            Bark("t.start.linghu", BarkTrigger.BattleStart, 3),
            Bark("t.skill", BarkTrigger.Skill, 1, skill: "skill.sword.dugu_blade", cooldown: 2),
            Bark("t.low", BarkTrigger.LowHp, 2),
            Bark("t.down", BarkTrigger.Downed, 2),
            Bark("t.lu.victory", BarkTrigger.Victory, 3, speaker: "char.lu_qinghe"),
            Bark("t.linghu.victory", BarkTrigger.Victory, 3),
        };

        var d = new BattleBarkDirector(lines, state);
        Assert.Equal("t.start.linghu", d.Start(BarkMode.Normal)?.LineId);
        Assert.Null(d.Start(BarkMode.Normal)); // 开战只一句

        SkillUsed Skill() => new("char.linghu_chong", "skill.sword.dugu_blade", ["enemy.escort_a"], 10, 0, false);
        Assert.Null(d.Observe(new RoundStarted(1, []), BarkMode.Normal));
        d.Observe(new TurnStarted("char.linghu_chong"), BarkMode.Normal);
        Assert.Equal("t.skill", d.Observe(Skill(), BarkMode.Normal)?.LineId);
        Assert.Null(d.Observe(new SkillUsed("char.linghu_chong", "skill.sword.dugu_blade", ["x"], 0, 0, IsReaction: true), BarkMode.Normal));

        // 第 2 轮还在冷却；第 3 轮可以再说，但倍速下只记账不说（之后第 4 轮仍在冷却）。
        d.Observe(new RoundStarted(2, []), BarkMode.Normal);
        Assert.Null(d.Observe(Skill(), BarkMode.Normal));
        d.Observe(new RoundStarted(3, []), BarkMode.Normal);
        Assert.Null(d.Observe(Skill(), BarkMode.Fast));
        d.Observe(new RoundStarted(4, []), BarkMode.Normal);
        Assert.Null(d.Observe(Skill(), BarkMode.Normal));
        d.Observe(new RoundStarted(5, []), BarkMode.Normal);
        d.Observe(new TurnStarted("enemy.tang_shouting"), BarkMode.Normal);

        // 同一回合里：重伤（2）之后倒下（2）不再喊；重伤线只认第一次跌破。
        var max = linghu.Stats.MaxHp;
        Assert.Null(d.Observe(new Damaged("enemy.tang_shouting", "char.linghu_chong", 1, DamageKind.External, false, max / 2), BarkMode.Normal));
        Assert.Equal("t.low", d.Observe(new Damaged("enemy.tang_shouting", "char.linghu_chong", 1, DamageKind.External, false, max / 5), BarkMode.Normal)?.LineId);
        Assert.Null(d.Observe(new UnitDowned("char.linghu_chong"), BarkMode.Normal));

        // 倒下的人不再开口，胜利句由仍站着的陆青禾说；静默模式不说。
        d.Observe(new TurnStarted("char.lu_qinghe"), BarkMode.Normal);
        Assert.Null(d.Observe(new BattleEnded(BattleOutcome.Victory), BarkMode.Silent));
        var fresh = new BattleBarkDirector(lines, state);
        fresh.Observe(new UnitDowned("char.linghu_chong"), BarkMode.Silent);
        Assert.Equal("t.lu.victory", fresh.Observe(new BattleEnded(BattleOutcome.Victory), BarkMode.Normal)?.LineId);

        // 都站着时，胜利句给最后出手的我方人物（文件里陆青禾在前）。
        var finisher = new BattleBarkDirector(lines, state);
        finisher.Observe(new SkillUsed("char.lu_qinghe", "skill.staff.pole_jab", ["enemy.escort_a"], 0, 0, false), BarkMode.Silent);
        finisher.Observe(new SkillUsed("char.linghu_chong", "skill.sword.probe", ["enemy.escort_a"], 0, 0, false), BarkMode.Silent);
        finisher.Observe(new SkillUsed("enemy.tang_shouting", "skill.boss.harbor_blade", ["char.lu_qinghe"], 0, 0, false), BarkMode.Silent);
        Assert.Equal("t.linghu.victory", finisher.Observe(new BattleEnded(BattleOutcome.Victory), BarkMode.Normal)?.LineId);
        Assert.Null(new BattleBarkDirector(lines, state).Observe(new BattleEnded(BattleOutcome.Defeat), BarkMode.Normal));
    }

    [Fact]
    public void Repository_barks_cover_each_companion_and_the_boss()
    {
        var barks = Barks.ToList();
        foreach (var who in new[] { "char.hero", "char.lu_qinghe", "char.linghu_chong", "char.huang_rong", "char.xiao_feng" })
        {
            Assert.Contains(barks, b => b.Speaker == who && b.Trigger == BarkTrigger.LowHp);
            Assert.Contains(barks, b => b.Speaker == who && b.Trigger == BarkTrigger.Downed);
        }

        // 首领的两种蓄力都有预兆喊声，倍速下也说（优先级 3）。
        foreach (var skill in new[] { "skill.boss.sluice_torrent", "skill.boss.undertow_hook" })
        {
            Assert.Contains(barks, b => b is { Trigger: BarkTrigger.Charge, Priority: BattleBarkDefinition.MaxPriority } && b.Skill == skill);
        }

        Assert.All(barks, b => Assert.StartsWith("ch01.battle.", b.LineId, StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_reports_broken_barks()
    {
        var b = ChapterOneWalkthroughTests.Bundle.Value;
        var existing = b.Barks.First();
        BattleBarkDefinition Bark(string id, BarkTrigger trigger, string text = "好", string speaker = "char.linghu_chong") =>
            new() { LineId = id, Speaker = speaker, Trigger = trigger, Text = text };
        var broken = b with
        {
            Chapters =
            [
                .. b.Chapters,
                new DialogueChapter
                {
                    Arc = "arc.01", Chapter = "chapter.01",
                    Barks =
                    [
                        existing,
                        Bark("x.long", BarkTrigger.LowHp, new string('长', BattleBarkDefinition.MaxChars + 1)),
                        Bark("x.nobody", BarkTrigger.Downed, speaker: "char.nobody"),
                        Bark("x.no_skill", BarkTrigger.Skill),
                        Bark("x.foreign_skill", BarkTrigger.Skill) with { Skill = "skill.fist.xianglong_kanglong" },
                        Bark("x.phase", BarkTrigger.Phase) with { Phase = "phase.none", Encounter = "battle.01.old_ferry_sluice" },
                        Bark("x.enemy", BarkTrigger.Downed, speaker: "char.tang_shouting") with { Unit = "enemy.ghost", Encounter = "battle.01.old_ferry_sluice" },
                        Bark("x.no_template", BarkTrigger.Downed, speaker: "char.du_sangao"),
                        Bark("x.priority", BarkTrigger.Victory) with { Priority = 4 },
                        Bark("x.cooldown", BarkTrigger.Downed) with { CooldownRounds = 2 },
                    ],
                },
            ],
        };
        var errors = WorldContentValidator.Validate(broken, TestContent.Bundle);
        Assert.Contains(errors, e => e.Contains($"喊声 {existing.LineId}：line_id 重复", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("x.long", StringComparison.Ordinal) && e.Contains("字上限", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("x.nobody", StringComparison.Ordinal) && e.Contains("不是已登记人物", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("x.no_skill", StringComparison.Ordinal) && e.Contains("skill 时机必填", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("x.foreign_skill", StringComparison.Ordinal) && e.Contains("永远说不出来", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("x.phase", StringComparison.Ordinal) && e.Contains("没有阶段 phase.none", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("x.enemy", StringComparison.Ordinal) && e.Contains("不在遭遇", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("x.no_template", StringComparison.Ordinal) && e.Contains("没有战斗模板", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("x.priority", StringComparison.Ordinal) && e.Contains("priority", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("x.cooldown", StringComparison.Ordinal) && e.Contains("cooldown_rounds", StringComparison.Ordinal));
    }
}
