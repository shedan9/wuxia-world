using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.World;
using static WuxiaWorld.Domain.Tests.WorldFixture;

namespace WuxiaWorld.Domain.Tests;

public class WorldRulesTests
{
    private static GameSession NewSession() => GameSession.NewGame(Rules());

    /// <summary>把对话走到底：台词一路继续，遇到选项选给定的 line_id（缺省第一个可选项）。</summary>
    private static CommitResult Play(GameSession g, DialogueSession d, params string[] picks)
    {
        var queue = new Queue<string>(picks);
        while (!d.Runner.Ended)
        {
            if (!d.Runner.AwaitingChoice)
            {
                d.Runner.Continue();
            }
            else
            {
                var pick = queue.Count > 0 ? queue.Dequeue() : null;
                var choice = d.Runner.Choices.First(c => c.Enabled && (pick is null || c.Option.LineId == pick));
                d.Runner.Choose(choice.Index);
            }
        }

        return g.FinishDialogue(d);
    }

    [Fact]
    public void New_game_starts_the_main_quest_at_its_first_stage()
    {
        var g = NewSession();
        Assert.Equal(QuestStatus.Active, g.World.QuestStatusOf(Main));
        Assert.Equal("meet", g.World.Quests[Main].Stage);
        Assert.Equal(["char.test.hero"], g.World.Party);
        Assert.Equal(QuestStatus.Available, g.World.QuestStatusOf(SideB));
        Assert.Equal(QuestStatus.Locked, g.World.QuestStatusOf(SideA));
    }

    [Fact]
    public void Conditions_combine_all_any_not()
    {
        var rules = Rules();
        var s = rules.NewGame();
        s.Facts["fact.a"] = "1";
        var a = Condition.Fact("fact.a", "1");
        var b = Condition.Fact("fact.b", "1");
        Assert.True(rules.Check(Condition.AllOf(a, Condition.NotOf(b)), s));
        Assert.False(rules.Check(Condition.AllOf(a, b), s));
        Assert.True(rules.Check(Condition.AnyOf(b, a), s));
        Assert.True(rules.Check(new Condition { Type = ConditionType.AtRegion, Id = "region.test" }, s));
        Assert.True(rules.Check(null, s));
    }

    [Fact]
    public void Replaying_a_finished_dialogue_does_not_grant_rewards_twice()
    {
        var g = NewSession();
        Play(g, g.StartDialogue("dlg.test.meet"));
        Assert.Equal(1, g.World.CountOf("item.test.pill"));
        Assert.Equal(5, g.World.Relationships["char.test.ally"].Trust);
        Assert.Equal(1, g.World.Clock);

        Play(g, g.StartDialogue("dlg.test.meet"));
        Assert.Equal(1, g.World.CountOf("item.test.pill"));
        Assert.Equal(5, g.World.Relationships["char.test.ally"].Trust);
        Assert.Equal(2, g.World.Clock); // 可重复效果节点每次都执行
    }

    [Fact]
    public void Choices_hide_unmet_options_unless_they_carry_a_locked_hint()
    {
        var g = NewSession();
        var d = g.StartDialogue("dlg.test.meet");
        d.Runner.Continue();
        var ids = d.Runner.Choices.Select(c => (c.Option.LineId, c.Enabled)).ToList();
        Assert.Contains(("test.meet.opt.trust", true), ids);
        Assert.Contains(("test.meet.opt.rich", false), ids);
        Assert.DoesNotContain(ids, c => c.LineId == "test.meet.opt.secret");
        Assert.Throws<InvalidOperationException>(() => d.Runner.Choose(1));
        Assert.Equal(["test.meet.001"], d.Runner.Transcript);
    }

    [Fact]
    public void Failed_effect_rejects_the_whole_dialogue_and_leaves_the_world_unchanged()
    {
        var g = NewSession();
        var before = g.World.Hash();
        var result = Play(g, g.StartDialogue("dlg.test.meet"), "test.meet.opt.fail");
        Assert.False(result.Ok);
        Assert.Contains("物品不足", result.Error);
        Assert.Equal(before, g.World.Hash());
    }

    [Fact]
    public void Cancelled_dialogue_changes_nothing()
    {
        var g = NewSession();
        var before = g.World.Hash();
        var d = g.StartDialogue("dlg.test.meet");
        d.Runner.Continue();
        g.CancelDialogue(d);
        Assert.Equal(before, g.World.Hash());
        Assert.False(g.FinishDialogue(d).Ok);
        Assert.True(g.CanSave);
    }

    [Fact]
    public void Quest_stages_advance_on_objectives_branch_on_facts_and_reward_once()
    {
        var g = NewSession();
        Play(g, g.StartDialogue("dlg.test.meet"));
        Assert.Equal("investigate", g.World.Quests[Main].Stage);
        Assert.Equal(QuestStatus.Available, g.World.QuestStatusOf(SideA)); // 见过人物后可接

        // 可选目标未完成不阻止推进；调查交互给出线索后阶段自动结算。
        g.CommitTransition(g.BeginExit("out"));
        g.Interact("stone", out _).Let(d => Play(g, d!));
        Assert.Equal("decide", g.World.Quests[Main].Stage);
        Assert.Equal(30, g.World.Experience);

        var rules = g.Rules;
        var s = g.World.Clone();
        var r = new EffectResult();
        rules.Apply(s, [WorldEffect.Fact("fact.test.choice", "open")], r);
        Assert.True(r.Ok);
        Assert.Equal(QuestStatus.Completed, s.QuestStatusOf(Main));
        Assert.Equal(["meet", "investigate", "decide", "public"], s.Quests[Main].History);
        Assert.Equal("public", s.Facts["fact.test.ending"]);
        Assert.Equal(20 + 50, s.Silver);

        // 已结算的奖励不会因再次推进而重复发放。
        rules.Refresh(s, r);
        rules.Apply(s, [WorldEffect.Fact("fact.test.choice", "sealed")], r);
        Assert.Equal(70, s.Silver);
        Assert.Equal("public", s.Facts["fact.test.ending"]);

        var other = g.World.Clone();
        rules.Apply(other, [WorldEffect.Fact("fact.test.choice", "sealed")], new EffectResult());
        Assert.Equal("sealed", other.Facts["fact.test.ending"]);
        Assert.Equal(["meet", "investigate", "decide", "sealed"], other.Quests[Main].History);
    }

    [Fact]
    public void Main_quest_cannot_be_abandoned_and_side_quests_record_the_outcome()
    {
        var rules = Rules();
        var s = rules.NewGame();
        var r = new EffectResult();
        rules.Apply(s, [new WorldEffect { Type = WorldEffectType.AbandonQuest, Id = Main }], r);
        Assert.False(r.Ok);

        s = rules.NewGame();
        r = new EffectResult();
        rules.Apply(s, [new WorldEffect { Type = WorldEffectType.MeetCharacter, Id = "char.test.ally" },
            new WorldEffect { Type = WorldEffectType.StartQuest, Id = SideA },
            new WorldEffect { Type = WorldEffectType.AbandonQuest, Id = SideA }], r);
        Assert.True(r.Ok, r.Error);
        Assert.Equal(QuestStatus.Abandoned, s.QuestStatusOf(SideA));
        Assert.Equal("abandoned", s.Facts["fact.test.side_a"]);
    }

    [Fact]
    public void Exclusive_quests_cannot_both_be_taken()
    {
        var rules = Rules();
        var s = rules.NewGame();
        var r = new EffectResult();
        rules.Apply(s, [new WorldEffect { Type = WorldEffectType.StartQuest, Id = SideB }], r);
        Assert.True(r.Ok);
        rules.Apply(s, [new WorldEffect { Type = WorldEffectType.MeetCharacter, Id = "char.test.ally" },
            new WorldEffect { Type = WorldEffectType.StartQuest, Id = SideA }], r);
        Assert.False(r.Ok);
        Assert.Contains("互斥", r.Error);
    }

    [Fact]
    public void Events_respect_priority_reservations_and_settle_once()
    {
        var g = NewSession();
        Assert.Equal(["event.test.meet", "event.test.gossip"], g.Events.Select(e => e.Id));

        var d = g.StartEvent("event.test.meet");
        Assert.Equal("event.test.meet", d.Runner.State.Reservations["char.test.ally"]);
        Assert.Empty(g.World.Reservations); // 预留只在事务副本里
        Play(g, d);
        Assert.Empty(g.World.Reservations);
        Assert.Equal(["event.test.gossip"], g.Events.Select(e => e.Id));

        // 人物被另一事件占用时，同一人物的事件不展示。
        var s = g.World.Clone();
        s.Reservations["char.test.ally"] = "event.other";
        Assert.Empty(g.Rules.EventsAt(s));
    }

    [Fact]
    public void Party_is_capped_at_hero_plus_three()
    {
        var rules = Rules();
        var s = rules.NewGame();
        var r = new EffectResult();
        rules.Apply(s, new[] { "char.test.ally", "char.test.c2", "char.test.c3" }.Select(c => new WorldEffect { Type = WorldEffectType.JoinParty, Id = c }), r);
        Assert.True(r.Ok);
        rules.Apply(s, [new WorldEffect { Type = WorldEffectType.JoinParty, Id = "char.test.c4" }], r);
        Assert.False(r.Ok);
        Assert.Equal(4, s.Party.Count);
    }

    [Fact]
    public void Travel_charges_once_and_aborted_or_stale_transitions_change_nothing()
    {
        var g = NewSession();
        g.CommitTransition(g.BeginExit("out"));
        Assert.Equal(("map.test.street", "inn_door"), (g.World.MapId, g.World.SpawnId));

        // 加载失败：丢弃候选状态。
        var before = g.World.Hash();
        var t = g.BeginRoute("route.test.ferry", TravelMode.Ferry);
        Assert.Throws<InvalidOperationException>(() => g.BeginRoute("route.test.ferry", TravelMode.Ferry)); // 连点被锁
        g.AbortTransition(t);
        Assert.Equal(before, g.World.Hash());
        Assert.False(g.CommitTransition(t).Ok);

        // 成功：扣费与时辰只发生一次，重复提交被拒绝。
        t = g.BeginRoute("route.test.ferry", TravelMode.Ferry);
        Assert.True(g.CommitTransition(t).Ok);
        Assert.False(g.CommitTransition(t).Ok);
        Assert.Equal(10, g.World.Silver);
        Assert.Equal(2, g.World.Clock);
        Assert.Equal("map.test.ferry", g.World.MapId);

        // 银两不足不能出发。
        g.CommitTransition(g.BeginExit("back"));
        g.CommitTransition(g.BeginRoute("route.test.ferry", TravelMode.Ferry));
        g.CommitTransition(g.BeginExit("back"));
        Assert.Equal(0, g.World.Silver);
        Assert.Throws<InvalidOperationException>(() => g.BeginRoute("route.test.ferry", TravelMode.Ferry));
        Assert.True(g.CommitTransition(g.BeginRoute("route.test.ferry", TravelMode.Walk)).Ok);
    }

    [Fact]
    public void Route_encounters_are_rolled_from_the_world_stream_and_survive_until_resolved()
    {
        string? Roll()
        {
            var g = NewSession();
            g.CommitTransition(g.BeginExit("out"));
            return g.BeginRoute("route.test.ferry", TravelMode.Walk).RolledEvent;
        }

        Assert.Equal(Roll(), Roll());

        // 找到会抽中途中事件的一次旅行：到达后自动开始，结算后清除且不再出现。
        var g = NewSession();
        g.CommitTransition(g.BeginExit("out"));
        for (var i = 0; i < 20 && g.World.QueuedEvent is null; i++)
        {
            g.CommitTransition(g.BeginRoute("route.test.ferry", TravelMode.Walk));
            if (g.World.QueuedEvent is null)
            {
                g.CommitTransition(g.BeginExit("back"));
            }
        }

        Assert.Equal("event.test.ambush", g.World.QueuedEvent);
        Assert.Equal("event.test.ambush", g.AutoEvent?.Id);
        Play(g, g.StartEvent("event.test.ambush"));
        Assert.Null(g.World.QueuedEvent);
        Assert.Null(g.AutoEvent);
    }

    [Fact]
    public void Battle_rewards_settle_once_per_request_and_defeat_can_retry_or_give_up()
    {
        var rules = Rules();
        var start = rules.NewGame();
        var r = new EffectResult();
        rules.Apply(start, [new WorldEffect
        {
            Type = WorldEffectType.RequestBattle, Id = "battle.test", Value = "req.test",
            OnVictory = [WorldEffect.Item("item.test.letter")], OnDefeat = [WorldEffect.Fact("fact.test.lost", "true")],
        }], r);
        Assert.True(r.Ok);
        var g = new GameSession(rules, start);
        Assert.False(g.CanSave);
        Assert.Equal("req.test#1", g.World.Battle!.InstanceId);

        Assert.True(g.SettleBattle("req.test#1", BattleEnd.Defeat).Ok);
        Assert.Equal(2, g.World.Battle!.Attempt);
        Assert.False(g.SettleBattle("req.test#1", BattleEnd.Victory).Ok); // 旧实例不能再结算

        Assert.True(g.SettleBattle("req.test#2", BattleEnd.Victory, experience: 40).Ok);
        Assert.Null(g.World.Battle);
        Assert.Equal(1, g.World.CountOf("item.test.letter"));
        Assert.Equal(40, g.World.Experience);
        Assert.False(g.SettleBattle("req.test#2", BattleEnd.Victory).Ok);
        Assert.Equal(1, g.World.CountOf("item.test.letter"));

        var lose = new GameSession(rules, start.Clone());
        Assert.True(lose.SettleBattle("req.test#1", BattleEnd.Retreated).Ok);
        Assert.True(lose.GiveUpBattle().Ok);
        Assert.Null(lose.World.Battle);
        Assert.Equal("true", lose.World.Facts["fact.test.lost"]);
        Assert.Equal("door", lose.World.SpawnId);
        Assert.True(lose.CanSave);
    }

    [Fact]
    public void One_time_interactables_stay_used_and_missing_spawns_fall_back_to_the_safe_entry()
    {
        var g = NewSession();
        g.CommitTransition(g.BeginExit("out"));
        Assert.Null(g.Interact("chest", out var result));
        Assert.True(result.Ok);
        Assert.Equal(2, g.World.CountOf("item.test.pill"));
        Assert.DoesNotContain(g.Interactables, i => i.Id == "chest");
        Assert.Throws<InvalidOperationException>(() => g.Interact("chest", out _));

        // 室内外往返后仍不刷新。
        g.CommitTransition(g.BeginExit("to_inn"));
        g.CommitTransition(g.BeginExit("out"));
        Assert.DoesNotContain(g.Interactables, i => i.Id == "chest");

        var broken = g.World.Clone();
        broken.SpawnId = "gone";
        var repaired = new GameSession(g.Rules, broken);
        Assert.NotNull(repaired.RepairSpawn());
        Assert.Equal("gate", repaired.World.SpawnId);
        Assert.Null(repaired.RepairSpawn());
    }

    [Fact]
    public void World_hash_covers_clone_exactly()
    {
        var g = NewSession();
        Play(g, g.StartDialogue("dlg.test.meet"));
        var copy = g.World.Clone();
        Assert.Equal(g.World.Hash(), copy.Hash());
        copy.Relationships["char.test.ally"].Commitments.Add("promise");
        Assert.NotEqual(g.World.Hash(), copy.Hash());
    }
}

internal static class TestExtensions
{
    public static void Let<T>(this T value, Action<T> action) => action(value);
}
