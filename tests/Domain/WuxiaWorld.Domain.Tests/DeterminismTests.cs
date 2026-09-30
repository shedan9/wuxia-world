using WuxiaWorld.Application.Combat;
using WuxiaWorld.Domain.Combat;
using WuxiaWorld.Domain.Combat.Definitions;
using static WuxiaWorld.Domain.Tests.TestContent;

namespace WuxiaWorld.Domain.Tests;

/// <summary>确定性与重放（开发计划 M1 验收：同一状态与命令序列重复 100 次哈希一致；4 对 6 能正常结束）。</summary>
public class DeterminismTests
{
    private static BattleSetup FerrySetup(ulong seed) => new()
    {
        EncounterId = "battle.01.old_ferry_sluice",
        Seed = seed,
        Allies =
        [
            Ally(Real.Combatant("combatant.hero.sword"), "char.hero", 0, 1),
            Ally(Real.Combatant("combatant.lu_qinghe"), "char.lu_qinghe", 1, 1),
        ],
        Items = new Dictionary<string, int> { ["item.medicine.golden_sore"] = 2, ["item.medicine.qi_pill"] = 1 },
    };

    [Fact]
    public void Same_setup_and_policy_give_the_same_hash_100_times()
    {
        var engine = new BattleEngine(Real);
        var first = Autoplay(engine, FerrySetup(20260930));
        Assert.True(first.Ended);
        for (var i = 0; i < 99; i++)
        {
            var again = Autoplay(engine, FerrySetup(20260930));
            Assert.Equal(first.Record.FinalHash, again.Record.FinalHash);
            Assert.Equal(first.Record.Entries.Count, again.Record.Entries.Count);
        }
    }

    [Fact]
    public void Recorded_battle_replays_with_matching_hashes()
    {
        var engine = new BattleEngine(Real);
        var session = Autoplay(engine, FerrySetup(7));
        Assert.Null(BattleSession.Replay(new BattleEngine(Real), session.Record));
    }

    [Fact]
    public void Tampered_record_is_detected_at_the_first_divergent_command()
    {
        var engine = new BattleEngine(Real);
        var session = Autoplay(engine, FerrySetup(7));
        var other = Autoplay(engine, FerrySetup(8));
        Assert.NotEqual(session.Record.FinalHash, other.Record.FinalHash);

        // 同样的命令换一个种子重放，必然在某条命令处对不上。
        var forged = new BattleRecord(FerrySetup(8), "", session.Record.RulesetVersion, session.Record.InitialHash);
        Assert.Equal(-1, BattleSession.Replay(engine, forged));
    }

    [Fact]
    public void Different_seeds_diverge()
    {
        var engine = new BattleEngine(Real);
        var hashes = Enumerable.Range(1, 8).Select(s => Autoplay(engine, FerrySetup((ulong)s)).Record.FinalHash).ToHashSet();
        Assert.True(hashes.Count > 1);
    }

    [Fact]
    public void Four_versus_six_battle_ends()
    {
        var enemies = new EncounterDefinition
        {
            Id = "battle.test.four_six",
            Enemies =
            [
                .. Enumerable.Range(0, 6).Select(i => new EncounterSlot
                {
                    Template = i % 3 == 2 ? "combatant.enemy.hookman" : "combatant.enemy.escort", UnitId = $"enemy.e{i}",
                    Row = i / 3, Slot = i % 3,
                }),
            ],
        };
        var engine = new BattleEngine(With(encounters: [enemies]));
        var setup = new BattleSetup
        {
            EncounterId = enemies.Id, Seed = 99,
            Allies =
            [
                Ally(Real.Combatant("combatant.hero.fist"), "char.hero", 0, 1),
                Ally(Real.Combatant("combatant.lu_qinghe"), "char.lu_qinghe", 0, 2),
                Ally(Real.Combatant("combatant.hero.sword") with { Id = "t.sword" }, "char.guest_a", 1, 0),
                Ally(Real.Combatant("combatant.hero.inner") with { Id = "t.inner" }, "char.guest_b", 1, 2),
            ],
        };
        var session = Autoplay(engine, setup, maxCommands: 2000);
        Assert.True(session.Ended, "4 对 6 在 2000 条命令内未结束");
        Assert.Null(BattleSession.Replay(engine, session.Record));
    }

    [Fact]
    public void Ferry_boss_telegraphs_torrent_one_round_ahead()
    {
        var engine = new BattleEngine(Real);
        var session = new BattleSession(engine, FerrySetup(3));
        var log = new List<BattleEvent>(session.StartEvents);
        for (var i = 0; i < 200 && !session.Ended; i++)
        {
            var result = session.AwaitingPlayer is { } unit
                ? session.Submit(new Defend(unit.Id))
                : session.StepAi()!;
            log.AddRange(result.Events);
        }

        var charge = log.OfType<ChargeStarted>().First(c => c.SkillId == "skill.boss.sluice_torrent");
        var chargeAt = log.IndexOf(charge);
        var roundAtCharge = log.Take(chargeAt).OfType<RoundStarted>().Last().Round;
        var release = log.Skip(chargeAt).OfType<SkillUsed>().First(u => u.SkillId == "skill.boss.sluice_torrent");
        var roundAtRelease = log.Take(log.IndexOf(release)).OfType<RoundStarted>().Last().Round;
        Assert.Equal(roundAtCharge + 1, roundAtRelease);
    }

    [Fact]
    public void Sluice_gate_floods_at_three_levels_and_its_fall_exposes_the_boss()
    {
        var engine = new BattleEngine(Real);
        var session = new BattleSession(engine, FerrySetup(5));
        var log = new List<BattleEvent>(session.StartEvents);
        for (var i = 0; i < 60 && !session.Ended; i++)
        {
            var result = session.AwaitingPlayer is { } unit ? session.Submit(new Defend(unit.Id)) : session.StepAi()!;
            log.AddRange(result.Events);
        }

        Assert.Contains(log, e => e is LevelRaised { Level: 3, Released: true });
        Assert.True(session.State.Unit("enemy.tang_shouting").HasStatus("status.sluice_cover")
            || session.State.Outcome != BattleOutcome.Ongoing);

        // 毁掉水门：唐守亭失去掩护。
        var s2 = new BattleSession(engine, FerrySetup(5));
        s2.State.Unit("enemy.sluice_gate").Hp = 1;
        for (var i = 0; i < 40 && !s2.Ended && !s2.State.FiredPhases.Contains("phase.sluice_broken"); i++)
        {
            if (s2.AwaitingPlayer is { } unit)
            {
                var skill = unit.Skills.Contains("skill.sword.pierce") && engine.Validate(s2.State, new UseSkill(unit.Id, "skill.sword.pierce", "enemy.sluice_gate")) is null
                    ? new UseSkill(unit.Id, "skill.sword.pierce", "enemy.sluice_gate")
                    : (BattleCommand)new Defend(unit.Id);
                s2.Submit(skill);
            }
            else
            {
                s2.StepAi();
            }
        }

        var tang = s2.State.Unit("enemy.tang_shouting");
        Assert.Contains("phase.sluice_broken", s2.State.FiredPhases);
        Assert.False(tang.HasStatus("status.sluice_cover"));
        Assert.True(tang.HasStatus("status.exposed"));
    }
}
