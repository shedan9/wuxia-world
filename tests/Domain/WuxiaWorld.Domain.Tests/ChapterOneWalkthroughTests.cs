using WuxiaWorld.Application.Dev;
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

    internal static readonly Lazy<WorldBundle> Bundle = new(() => WorldContentLoader.LoadDirectory(Path.Combine(TestContent.RepoRoot(), "content")));

    internal static WorldRules Rules() => new(Bundle.Value.ToContent());

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

    [Fact]
    public void Validator_requires_prompt_text_for_interactables_and_player_started_events()
    {
        var b = Bundle.Value;
        var text = b.Text.Where(t => t.Key is not ("map.jiangnan.inn.rations_basket.name" or "event.ch01.side_offer.verb"))
            .ToDictionary(t => t.Key, t => t.Value);
        var errors = WorldContentValidator.Validate(b with { Text = text }, TestContent.Bundle);
        Assert.Contains(errors, e => e.Contains("map.jiangnan.inn.rations_basket.name", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("event.ch01.side_offer.verb", StringComparison.Ordinal));

        // 进图自动开始的过场事件不需要交互提示。
        Assert.DoesNotContain(errors, e => e.Contains("event.ch01.opening_luwan.verb", StringComparison.Ordinal));
    }

    [Fact]
    public void Chapter_one_canon_characters_have_checked_story_anchors()
    {
        var b = Bundle.Value;
        var anchors = b.Anchors.ToDictionary(a => a.Id);
        foreach (var c in b.Characters.Where(c => c.Origin == CharacterOrigin.Canon))
        {
            Assert.NotEqual(WorldContentValidator.PendingAnchor, c.StoryAnchor);
            var a = anchors[c.StoryAnchor!];
            Assert.Equal(c.Id, a.Character);
            Assert.Equal(AnchorStatus.TextChecked, a.Status);
            Assert.True(a.Adult, $"{a.Id} 本作年龄未成年");
            Assert.NotEmpty(a.NotYet);
        }

        // 黄蓉为用户决定的改编年龄：原文十五岁，本作 18 岁，两者分开记录。
        var huang = anchors["anchor.huang_rong.shediao_40"];
        Assert.True(huang.AgeAdapted);
        Assert.Contains("十五", huang.CanonAge, StringComparison.Ordinal);
        Assert.Contains("18 岁", huang.Adaptation, StringComparison.Ordinal);
        Assert.All(anchors.Values.Where(a => a.Id != huang.Id), a => Assert.False(a.AgeAdapted));
    }

    [Fact]
    public void Validator_checks_story_anchor_references_ages_and_adaptation_notes()
    {
        var b = Bundle.Value;
        var huang = b.Anchors.Single(a => a.Character == "char.huang_rong");
        var broken = b with
        {
            Anchors =
            [
                .. b.Anchors.Where(a => a != huang),
                huang with { Adaptation = null, AgeMin = 20, AgeMax = 18 },
                new StoryAnchorDefinition { Id = "anchor.test.unused", Character = "char.lu_qinghe", Work = "work.x" },
            ],
            Characters =
            [
                .. b.Characters.Select(c => c.Id switch
                {
                    "char.xiao_feng" => c with { StoryAnchor = "anchor.test.missing" },
                    "char.linghu_chong" => c with { StoryAnchor = "anchor.huang_rong.shediao_40" },
                    _ => c,
                }),
            ],
        };
        var errors = WorldContentValidator.Validate(broken, TestContent.Bundle);
        Assert.Contains(errors, e => e.Contains("char.xiao_feng", StringComparison.Ordinal) && e.Contains("未定义", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("char.linghu_chong", StringComparison.Ordinal) && e.Contains("不符", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("anchor.huang_rong.shediao_40", StringComparison.Ordinal) && e.Contains("年龄范围", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("anchor.huang_rong.shediao_40", StringComparison.Ordinal) && e.Contains("改编说明", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("anchor.test.unused", StringComparison.Ordinal) && e.Contains("缺少", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("anchor.test.unused", StringComparison.Ordinal) && e.Contains("没有人物引用", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("anchor.xiao_feng.tianlong_21_23", StringComparison.Ordinal) && e.Contains("没有人物引用", StringComparison.Ordinal));
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

        // 成长：讨教学到整套流派武学并装上；整章走完到第二阶段 Demo 的等级上限。
        var style = new Dictionary<string, string> { ["linghu"] = "sword", ["huang"] = "inner", ["xiao"] = "fist" }[companion];
        Assert.Equal(style, s.Facts["fact.hero.style"]);
        var hero = s.Builds["char.hero"];
        Assert.Equal(4, hero.Skills.Count);
        Assert.All(hero.Skills, id => Assert.StartsWith($"skill.{style}.", id, StringComparison.Ordinal));
        Assert.NotNull(hero.MainArt);
        Assert.Equal(8, w.Game.Rules.LevelOf(s.Experience));
        Assert.True(s.Cultivation >= 130, $"修为 {s.Cultivation}");

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
    public void Medicine_used_in_a_story_battle_is_taken_from_the_pack_once()
    {
        var w = new Walker(Rules());
        w.PlayUntilFerry("linghu", side: false);
        w.PlayAuto(); // 登岸，押运队冲突
        var battle = w.Game.World.Battle!;
        var before = w.Game.World.CountOf("item.medicine.golden_sore");
        Assert.True(before >= 1);
        var used = new Dictionary<string, int> { ["item.medicine.golden_sore"] = 1, ["item.medicine.qi_pill"] = 9 };

        // 战败可重试：用掉的药照样扣除；数量超过行囊现有的按现有扣完，不让结算失败。
        Assert.True(w.Game.SettleBattle(battle.InstanceId, BattleEnd.Defeat, consumed: used).Ok);
        Assert.Equal(before - 1, w.Game.World.CountOf("item.medicine.golden_sore"));
        Assert.Equal(0, w.Game.World.CountOf("item.medicine.qi_pill"));

        // 同一实例重复结算被拒，不重复扣。
        Assert.False(w.Game.SettleBattle(battle.InstanceId, BattleEnd.Victory, consumed: used).Ok);
        Assert.Equal(before - 1, w.Game.World.CountOf("item.medicine.golden_sore"));
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
        var unread = all.Where(id => !seen.Contains(id)).ToList();
        Assert.True(unread.Count == 0, "走不到的台词：" + string.Join("、", unread));

        // 演出提示同样每个都要能走到（台词的 line_id 与演出提示键分开记）。
        var stages = Bundle.Value.Dialogues.SelectMany(d => d.Nodes.Where(n => n.Type == DialogueNodeType.Stage).Select(n => StageKey(d.Id, n.Id)));
        var unplayed = stages.Where(id => !seen.Contains(id)).ToList();
        Assert.True(unplayed.Count == 0, "走不到的演出提示：" + string.Join("、", unplayed));
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

    /// <summary>
    /// 按固定选择驱动 <see cref="GameSession"/> 的走查器：路线本身在 <see cref="ChapterOneRoute"/>（游戏的 <c>--jump</c> 与战斗测试台共用），
    /// 这里只加测试要的核对与台词收集。
    /// </summary>
    internal sealed class Walker
    {
        private ChapterOneRoute? _steps;

        public Walker(WorldRules rules, GameSession? game = null) => Game = game ?? GameSession.NewGame(rules);

        public GameSession Game { get; }
        public HashSet<string> Lines { get; } = new(StringComparer.Ordinal);
        public string OpeningPick { get; init; } = "choice.a";
        public string CouncilPick { get; init; } = "choice.insight";

        /// <summary>真打剧情战：给出时由它开战并结算（须打赢），否则按胜利直接结算。</summary>
        public Action<GameSession>? Fighter { get; init; }

        /// <summary>单步操作用的路线（不走整章，只用它的 Pick / Interact / Exit / PlayAuto 等）。</summary>
        private ChapterOneRoute Steps => _steps ??= Route();

        private ChapterOneRoute Route(string companion = "linghu", string custody = "public", bool side = false, bool finishSideSteps = true,
            bool declineSideFirst = false, bool readNoticeEarly = false, bool freeEarly = true, ChapterOnePoint from = ChapterOnePoint.Start) =>
            new(Game)
            {
                Companion = companion, Custody = custody, Side = side, FinishSideSteps = finishSideSteps, DeclineSideFirst = declineSideFirst,
                ReadNoticeEarly = readNoticeEarly, FreeEarly = freeEarly, From = from,
                OpeningPick = OpeningPick, CouncilPick = CouncilPick, Fighter = Fighter, OnDialogue = Collect, Combat = TestContent.Real,
            };

        public void PlayChapter(string companion, string custody, bool side, bool finishSideSteps = true,
            bool declineSideFirst = false, bool readNoticeEarly = false, bool freeEarly = true)
        {
            var route = Route(companion, custody, side, finishSideSteps, declineSideFirst, readNoticeEarly, freeEarly);
            route.RunTo(ChapterOnePoint.Mentor);
            Assert.Equal(QuestStatus.Available, Game.World.QuestStatusOf(Side));

            route.RunTo(ChapterOnePoint.Departure);
            Assert.Contains(Game.World.Skills, s => s.StartsWith("skill.", StringComparison.Ordinal));
            Assert.Equal(3, Game.World.Party.Count);
            if (side)
            {
                Assert.DoesNotContain(Game.Events, e => e.Id == "event.ch01.side_offer");
                Assert.Equal(finishSideSteps ? "free" : "tags", Game.World.Quests[Side].Stage);
            }
            else
            {
                // 没接（含先婉拒一次）的支线仍可接。
                Assert.Equal(QuestStatus.Available, Game.World.QuestStatusOf(Side));
            }

            route.RunTo(ChapterOnePoint.SluiceBattle);
            Assert.Equal(4, Game.World.Party.Count);
            route.RunTo(ChapterOnePoint.End);
        }

        public void PlayUntilFerry(string companion, bool side)
        {
            Route(companion, side: side, finishSideSteps: false).RunTo(ChapterOnePoint.Departure);
            var t = Game.BeginRoute(ChapterOneRoute.FerryRoute, TravelMode.Ferry);
            if (side)
            {
                // 存读档用例：先不出发，回到街上。
                Game.AbortTransition(t);
                return;
            }

            Assert.True(Game.CommitTransition(t).Ok);
            _steps = Route(companion, from: ChapterOnePoint.OldFerry);
        }

        /// <summary>从芦湾街码头（乘船之前）走到章末。</summary>
        public void FinishFromFerry(string custody, bool freeEarly = false) =>
            Route(custody: custody, side: freeEarly, from: ChapterOnePoint.Departure).RunTo(ChapterOnePoint.End);

        public void WinBattle() => Steps.WinBattle();

        public void Pick(string lineSuffix) => Steps.Pick(lineSuffix);

        public CommitResult PlayAuto() => Steps.PlayAuto();

        public CommitResult PlayEvent(string id) => Steps.PlayEvent(id);

        public void Interact(string id) => Steps.Interact(id);

        public void Exit(string id) => Steps.Exit(id);

        private void Collect(DialogueRunner runner)
        {
            Lines.UnionWith(runner.Transcript);
            Lines.UnionWith(runner.StagesPlayed.Select(n => StageKey(runner.Definition.Id, n)));
        }
    }
}
