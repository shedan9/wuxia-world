using WuxiaWorld.Application.Persistence;
using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Infrastructure.Content;
using WuxiaWorld.Infrastructure.Saves;

namespace WuxiaWorld.Domain.Tests;

/// <summary>
/// 第一章固定走查（架构文档 9.3“复杂分支可达性用固定测试存档与条件图遍历验证”）：
/// 读仓库里的正式内容，按同行者、副页处置、是否做支线的组合从开场走到章末；战斗以胜负结算代替实际对战。
/// </summary>
public class ChapterOneWalkthroughTests
{
    private const string Main = "quest.main.01.jiangnan_guest";
    private const string Side = "quest.side.01.missing_ferryman";

    private static readonly Lazy<WorldBundle> Bundle = new(() => WorldContentLoader.LoadDirectory(Path.Combine(TestContent.RepoRoot(), "content")));

    private static WorldRules Rules() => new(Bundle.Value.ToContent());

    [Fact]
    public void Repository_world_content_validates_without_errors()
    {
        var errors = WorldContentValidator.Validate(Bundle.Value, TestContent.Bundle);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public void Validator_reports_broken_references_dead_ends_and_orphans()
    {
        var b = Bundle.Value;
        var d = b.Chapters[0].Dialogues[0];
        var broken = b with
        {
            Chapters =
            [
                b.Chapters[0] with
                {
                    Dialogues =
                    [
                        d with
                        {
                            Nodes =
                            [
                                .. d.Nodes,
                                new DialogueNode { Id = "orphan", Type = DialogueNodeType.Line, LineId = d.Nodes.First(n => n.Type == DialogueNodeType.Line).LineId, Speaker = "char.nobody", Text = "x" },
                                new DialogueNode { Id = "dead", Type = DialogueNodeType.Choice, Options =
                                    [new DialogueOption { LineId = "x.1", Text = "x", Next = "missing", When = Condition.Fact("fact.x", "1") }] },
                                new DialogueNode { Id = "narr", Type = DialogueNodeType.Line, LineId = "x.narr", Speaker = WorldContentValidator.Narrator, Text = "x" },
                                new DialogueNode { Id = "cue", Type = DialogueNodeType.Stage },
                                new DialogueNode { Id = "card", Type = DialogueNodeType.Stage, Kind = StageKind.Title, Direction = "x" },
                                new DialogueNode { Id = "npc_inner", Type = DialogueNodeType.Line, LineId = "x.npc_inner", Speaker = "char.lu_qinghe", Inner = true, Text = "她想", Next = "hero_inner" },
                                new DialogueNode { Id = "hero_inner", Type = DialogueNodeType.Line, LineId = "x.hero_inner", Speaker = "char.hero", Inner = true,
                                    Text = new string('想', DialogueNode.InnerMaxChars + 1), Next = "hero_inner2" },
                                new DialogueNode { Id = "hero_inner2", Type = DialogueNodeType.Line, LineId = "x.hero_inner2", Speaker = "char.hero", Inner = true, Text = "（又想）" },
                                new DialogueNode { Id = "bare", Type = DialogueNodeType.Line, LineId = "x.bare", Speaker = "char.xiao_feng", Text = "（暗想）" },
                            ],
                        },
                        .. b.Chapters[0].Dialogues.Skip(1),
                    ],
                },
            ],
            Quests = [.. b.Quests.Select(q => q.Kind == QuestKind.Main ? q with { GuaranteedClue = "clue.nowhere" } : q)],
        };
        var errors = WorldContentValidator.Validate(broken, TestContent.Bundle);
        Assert.Contains(errors, e => e.Contains("orphan", StringComparison.Ordinal) && e.Contains("孤立", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("line_id 重复", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("char.nobody", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("无选项死路", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("missing", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("clue.nowhere", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("不再使用旁白", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("缺少 kind", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("缺少 direction", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("标题卡缺少 caption", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("npc_inner", StringComparison.Ordinal) && e.Contains("只有主角", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("hero_inner", StringComparison.Ordinal) && e.Contains("字上限", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("hero_inner：", StringComparison.Ordinal) && e.Contains("紧接着又是心里话", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("hero_inner2", StringComparison.Ordinal) && e.Contains("不带括号", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("bare", StringComparison.Ordinal) && e.Contains("须标 inner", StringComparison.Ordinal));
    }

    public static TheoryData<string, string, bool> Paths()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var companion in new[] { "linghu", "huang", "xiao" })
        {
            foreach (var custody in new[] { "public", "sealed" })
            {
                foreach (var side in new[] { true, false })
                {
                    data.Add(companion, custody, side);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void Every_companion_custody_and_side_quest_path_reaches_the_chapter_end(string companion, string custody, bool side)
    {
        var w = new Walker(Rules());
        w.PlayChapter(companion, custody, side);
        var s = w.Game.World;

        Assert.Equal(QuestStatus.Completed, s.QuestStatusOf(Main));
        Assert.Contains("gate.part1.ch01_done", s.Gates);
        Assert.Equal("chapter.02", s.ChapterId);
        Assert.Equal(custody, s.Facts["fact.ch01.copy_custody"]);
        Assert.Equal(1, s.CountOf("item.quest.relay_copy"));
        Assert.Contains("clue.ch01.water_tag_mismatch", s.Clues);
        Assert.Contains("clue.ch01.next_leads", s.Clues);
        Assert.Equal(["char.hero", "char.lu_qinghe"], s.Party);
        Assert.Empty(s.Reservations);
        Assert.Null(s.Battle);
        Assert.Equal("map.jiangnan.inn", s.MapId);
        Assert.Equal(["char.du_sangao", "char.hero", "char.huang_rong", "char.linghu_chong", "char.lu_qinghe", "char.qiao_hongxiao",
            "char.tang_shouting", "char.xiao_feng"], s.Met);

        // 三条同行路径都有另一部作品的人物到场参战。
        var helper = new Dictionary<string, string> { ["linghu"] = "xiao", ["huang"] = "linghu", ["xiao"] = "huang" }[companion];
        Assert.Equal(helper, s.Facts["fact.ch01.helper"]);

        // 支线：做了且抢先救人得全额；萧峰同行会先救人；没接时支线随主线救人收回，不留悬空入口。
        if (side)
        {
            Assert.Equal(QuestStatus.Completed, s.QuestStatusOf(Side));
            Assert.Equal("full", s.Facts["fact.ch01.side01_result"]);
        }
        else
        {
            Assert.Equal(QuestStatus.Locked, s.QuestStatusOf(Side));
        }

        Assert.Equal(30 + 30 + (side ? 30 : 0) - 5, s.Silver);
    }

    [Fact]
    public void Side_quest_taken_but_unfinished_settles_as_late_rescue_after_the_boss()
    {
        var w = new Walker(Rules());
        w.PlayChapter("linghu", "sealed", side: true, finishSideSteps: false);
        Assert.Equal(QuestStatus.Active, w.Game.World.QuestStatusOf(Side));
        Assert.Equal("tags", w.Game.World.Quests[Side].Stage);

        // 章末后补做：船牌与潮痕查完，救人目标已由主线完成，按“迟到”收束。
        w.Exit("out");
        w.Interact("ferry_tags");
        w.Exit("to_shore");
        w.Interact("tide_line");
        Assert.Equal(QuestStatus.Completed, w.Game.World.QuestStatusOf(Side));
        Assert.Equal("late", w.Game.World.Facts["fact.ch01.side01_result"]);
    }

    [Fact]
    public void Giving_up_a_lost_battle_leaves_a_way_back_into_the_fight()
    {
        var w = new Walker(Rules());
        w.PlayUntilFerry("huang", side: false);
        w.PlayAuto(); // 登岸，押运队冲突
        var battle = w.Game.World.Battle!;
        Assert.Equal("battle.01.escort_skirmish", battle.Encounter);
        Assert.True(w.Game.SettleBattle(battle.InstanceId, BattleEnd.Defeat).Ok);
        Assert.True(w.Game.GiveUpBattle().Ok);
        Assert.Null(w.Game.World.Battle);

        Assert.Contains(w.Game.Events, e => e.Id == "event.ch01.escort_regroup");
        w.PlayEvent("event.ch01.escort_regroup");
        Assert.Equal("req.ch01.escort_skirmish#2", w.Game.World.Battle!.InstanceId); // #1 已结算为战败，#2 未开打即放弃
        w.WinBattle();
        Assert.DoesNotContain(w.Game.Events, e => e.Id == "event.ch01.escort_regroup");

        // 首领战同样可放弃后从对峙事件重开。
        w.PlayEvent("event.ch01.sluice_confrontation");
        var boss = w.Game.World.Battle!;
        w.Game.SettleBattle(boss.InstanceId, BattleEnd.Defeat);
        w.Game.GiveUpBattle();
        w.PlayEvent("event.ch01.sluice_confrontation");
        Assert.Equal("battle.01.old_ferry_sluice", w.Game.World.Battle!.Encounter);
        Assert.Equal(["char.hero", "char.lu_qinghe", "char.huang_rong", "char.linghu_chong"], w.Game.World.Party);
    }

    [Fact]
    public void Saving_and_loading_mid_chapter_keeps_quests_items_and_used_interactables()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wuxia-walk-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var rules = Rules();
            var w = new Walker(rules);
            w.PlayUntilFerry("xiao", side: true);
            w.Exit("to_inn");
            w.Interact("rations_basket");
            Assert.True(w.Game.CanSave);
            var store = new FileSaveStore(dir);
            var header = new SaveHeader { ContentVersion = Bundle.Value.ContentVersion, Sequence = store.NextSequence(), MapId = w.Game.World.MapId };
            Assert.True(store.Write(SaveSlot.Quick, new SaveGame { Header = header, World = w.Game.World }).Ok);

            var loaded = store.Read(SaveSlot.Quick).Game!;
            Assert.Empty(SaveCompatibility.Check(loaded.World, rules.Content));
            var resumed = new Walker(rules, new GameSession(rules, loaded.World));
            Assert.Equal(w.Game.World.Hash(), resumed.Game.World.Hash());
            Assert.DoesNotContain(resumed.Game.Interactables, i => i.Id == "rations_basket");
            Assert.Equal(2, resumed.Game.World.CountOf("item.misc.dry_rations"));

            resumed.Exit("out");
            resumed.FinishFromFerry("sealed");
            Assert.Equal(QuestStatus.Completed, resumed.Game.World.QuestStatusOf(Main));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Every_dialogue_line_is_reached_by_some_walkthrough()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Paths())
        {
            var w = new Walker(Rules());
            w.PlayChapter((string)row[0], (string)row[1], (bool)row[2]);
            seen.UnionWith(w.Lines);
        }

        // 开场三个选项、会面三种表态、拒绝支线、告示两种结果、无同伴的石痕、迟到救人分别补走。
        foreach (var picks in new[] { "choice.b", "choice.c" })
        {
            var w = new Walker(Rules()) { OpeningPick = picks };
            w.PlayAuto();
            seen.UnionWith(w.Lines);
        }

        foreach (var stance in new[] { "choice.rescue", "choice.courier" })
        {
            var w = new Walker(Rules()) { CouncilPick = stance };
            w.PlayChapter("xiao", "public", side: false, declineSideFirst: true, readNoticeEarly: true);
            seen.UnionWith(w.Lines);
        }

        var late = new Walker(Rules());
        late.PlayChapter("linghu", "sealed", side: true, finishSideSteps: false);
        late.Exit("out");
        late.Interact("ferry_tags");
        late.Exit("to_shore");
        late.Interact("tide_line");
        late.Interact("stone_marks");
        late.Exit("to_street");
        late.Exit("to_inn");
        seen.UnionWith(late.Lines);

        // 没看过石痕就读告示。
        var lonely = new Walker(Rules());
        lonely.Exit("to_street");
        lonely.Interact("ferry_notice");
        seen.UnionWith(lonely.Lines);

        // 主线救人在前、支线步骤已做完：收束时按“迟到”。
        var slow = new Walker(Rules());
        slow.PlayChapter("huang", "public", side: true, freeEarly: false);
        Assert.Equal("late", slow.Game.World.Facts["fact.ch01.side01_result"]);
        seen.UnionWith(slow.Lines);

        // 押运队战败放弃后的再战入口。
        var regroup = new Walker(Rules());
        regroup.PlayUntilFerry("linghu", side: false);
        regroup.PlayAuto();
        regroup.Game.SettleBattle(regroup.Game.World.Battle!.InstanceId, BattleEnd.Defeat);
        regroup.Game.GiveUpBattle();
        regroup.PlayEvent("event.ch01.escort_regroup");
        seen.UnionWith(regroup.Lines);

        var all = Bundle.Value.Dialogues.SelectMany(d => d.Nodes.Where(n => n.Type == DialogueNodeType.Line).Select(n => n.LineId!));
        Assert.Empty(all.Where(id => !seen.Contains(id)));

        // 演出提示同样每个都要能走到（台词的 line_id 与演出提示键分开记）。
        var stages = Bundle.Value.Dialogues.SelectMany(d => d.Nodes.Where(n => n.Type == DialogueNodeType.Stage).Select(n => StageKey(d.Id, n.Id)));
        Assert.Empty(stages.Where(id => !seen.Contains(id)));
    }

    [Fact]
    public void Chapter_one_has_no_narrator_lines()
    {
        Assert.DoesNotContain(Bundle.Value.Dialogues.SelectMany(d => d.Nodes), n => n.Speaker == WorldContentValidator.Narrator);
    }

    [Fact]
    public void Chapter_one_inner_lines_belong_to_the_hero_only()
    {
        var inner = Bundle.Value.Dialogues.SelectMany(d => d.Nodes).Where(n => n.Inner).ToList();
        Assert.NotEmpty(inner);
        Assert.All(inner, n => Assert.Equal("char.hero", n.Speaker));
        Assert.All(inner, n => Assert.InRange(n.Text!.Length, 1, DialogueNode.InnerMaxChars));
    }

    private static string StageKey(string dialogue, string node) => $"stage:{dialogue}/{node}";

    /// <summary>按固定选择驱动 <see cref="GameSession"/> 的走查器。</summary>
    private sealed class Walker
    {
        public Walker(WorldRules rules, GameSession? game = null) => Game = game ?? GameSession.NewGame(rules);

        public GameSession Game { get; }
        public HashSet<string> Lines { get; } = new(StringComparer.Ordinal);
        public string OpeningPick { get; init; } = "choice.a";
        public string CouncilPick { get; init; } = "choice.insight";

        private readonly Queue<string> _picks = new();

        public void PlayChapter(string companion, string custody, bool side, bool finishSideSteps = true,
            bool declineSideFirst = false, bool readNoticeEarly = false, bool freeEarly = true)
        {
            PlayAuto(); // 开场
            Interact("stone_marks");
            Assert.Equal("to_inn", Game.World.Quests[Main].Stage);
            Exit("to_street");
            if (readNoticeEarly)
            {
                Interact("ferry_notice");
            }

            Exit("to_inn");
            Pick(CouncilPick);
            PlayAuto(); // 会面
            Assert.Equal("training", Game.World.Quests[Main].Stage);
            Assert.Equal(QuestStatus.Available, Game.World.QuestStatusOf(Side));

            if (declineSideFirst)
            {
                Pick("choice.later");
                PlayEvent("event.ch01.side_offer");
                Assert.Equal(QuestStatus.Available, Game.World.QuestStatusOf(Side));
            }

            if (side)
            {
                Pick("choice.accept");
                PlayEvent("event.ch01.side_offer");
                Assert.DoesNotContain(Game.Events, e => e.Id == "event.ch01.side_offer");
            }

            Pick("choice." + companion);
            PlayEvent("event.ch01.mentor_choice");
            Assert.Contains(Game.World.Skills, s => s.StartsWith("skill.", StringComparison.Ordinal));

            if (side && finishSideSteps)
            {
                Exit("out");
                Interact("ferry_tags");
                Exit("to_shore");
                Interact("tide_line");
                Exit("to_street");
                Exit("to_inn");
                Assert.Equal("free", Game.World.Quests[Side].Stage);
            }

            Pick("choice." + companion);
            PlayEvent("event.ch01.companion_choice");
            Assert.Equal(3, Game.World.Party.Count);
            Exit("out");
            FinishFromFerry(custody, freeEarly: side && finishSideSteps && freeEarly);
        }

        public void PlayUntilFerry(string companion, bool side)
        {
            PlayAuto();
            Interact("stone_marks");
            Exit("to_street");
            Exit("to_inn");
            PlayAuto();
            if (side)
            {
                Pick("choice.accept");
                PlayEvent("event.ch01.side_offer");
            }

            Pick("choice." + companion);
            PlayEvent("event.ch01.mentor_choice");
            Pick("choice." + companion);
            PlayEvent("event.ch01.companion_choice");
            Exit("out");
            var t = Game.BeginRoute("route.jiangnan.luwan_to_old_ferry", TravelMode.Ferry);
            if (side)
            {
                // 存读档用例：先不出发，回到街上。
                Game.AbortTransition(t);
                return;
            }

            Assert.True(Game.CommitTransition(t).Ok);
        }

        public void FinishFromFerry(string custody, bool freeEarly = false)
        {
            Assert.True(Game.CommitTransition(Game.BeginRoute("route.jiangnan.luwan_to_old_ferry", TravelMode.Ferry)).Ok);
            Assert.Equal("rescue", Game.World.Quests[Main].Stage);
            PlayAuto(); // 登岸
            WinBattle();
            if (freeEarly && Game.Interactables.Any(i => i.Id == "locked_boat"))
            {
                Interact("locked_boat");
            }

            PlayEvent("event.ch01.sluice_confrontation");
            Assert.Equal(4, Game.World.Party.Count);
            WinBattle();
            Assert.Equal("custody", Game.World.Quests[Main].Stage);
            Pick("choice." + custody);
            var result = PlayAuto();
            Assert.NotNull(result.Travel);
            Assert.True(Game.CommitTransition(Game.BeginStoryTravel(result.Travel!)).Ok);
            PlayAuto(); // 收束
        }

        public void WinBattle()
        {
            var b = Game.World.Battle ?? throw new InvalidOperationException("没有待开战斗");
            var r = Game.SettleBattle(b.InstanceId, BattleEnd.Victory, experience: 20);
            Assert.True(r.Ok, r.Error);
        }

        public void Pick(string lineSuffix) => _picks.Enqueue(lineSuffix);

        public CommitResult PlayAuto()
        {
            var e = Game.AutoEvent ?? throw new InvalidOperationException($"{Game.World.MapId} 没有自动事件");
            return PlayEvent(e.Id);
        }

        public CommitResult PlayEvent(string id) => Play(Game.StartEvent(id));

        public void Interact(string id)
        {
            var d = Game.Interact(id, out var r);
            Assert.True(r.Ok, r.Error);
            if (d is not null)
            {
                Play(d);
            }
        }

        public void Exit(string id) => Assert.True(Game.CommitTransition(Game.BeginExit(id)).Ok);

        private CommitResult Play(DialogueSession d)
        {
            var opening = d.Runner.Definition.Id == "dlg.ch01.opening_luwan";
            while (!d.Runner.Ended)
            {
                if (!d.Runner.AwaitingChoice)
                {
                    d.Runner.Continue();
                    continue;
                }

                var want = opening ? OpeningPick : _picks.Count > 0 ? _picks.Dequeue() : null;
                var choice = d.Runner.Choices.FirstOrDefault(c => c.Enabled && (want is null || c.Option.LineId.EndsWith("." + want, StringComparison.Ordinal)))
                    ?? throw new InvalidOperationException($"{d.Runner.Definition.Id}：找不到选项 {want}");
                d.Runner.Choose(choice.Index);
            }

            Lines.UnionWith(d.Runner.Transcript);
            Lines.UnionWith(d.Runner.StagesPlayed.Select(n => StageKey(d.Runner.Definition.Id, n)));
            var r = Game.FinishDialogue(d);
            Assert.True(r.Ok, r.Error);
            return r;
        }
    }
}
