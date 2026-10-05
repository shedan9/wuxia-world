using WuxiaWorld.Application.Dev;
using WuxiaWorld.Application.Persistence;
using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Infrastructure.Saves;

namespace WuxiaWorld.Domain.Tests;

/// <summary>
/// 旧档迁移回归（M3 验收“存档恢复和旧档迁移回归通过”）：<c>tests/Fixtures/saves/</c> 里是用历史版本的真实代码沿第一章写下的存档
/// （文件名 <c>v{结构版本}.{位置}.json</c>；v1 取自 <c>1523447^</c>、v2 取自 <c>3e7f6ff^</c>、v4 取自后院改版前的 <c>7e8b810</c>，
/// 生成方法见架构文档 11.1）。每份都按游戏读档的同一段代码（<see cref="SaveResume"/>）读入、迁移、补齐，再用当前内容一路走到章末；
/// 走完后用当前版本另存、重读不再迁移且世界不变。样本一经入库不再改写——它们代表玩家手里已有的存档。
/// </summary>
public class OldSaveRegressionTests
{
    private static readonly string FixtureDir = Path.Combine(TestContent.RepoRoot(), "tests", "Fixtures", "saves");

    /// <summary>样本写下时的选择：v1 / v2 讨教萧峰、同行黄蓉、密封副页；v4 讨教令狐冲、同行黄蓉、公开副页；都接了支线。</summary>
    private static (string Mentor, string Custody) PicksOf(int version) => version < 4 ? ("xiao", "sealed") : ("linghu", "public");

    public static TheoryData<string> Fixtures()
    {
        var data = new TheoryData<string>();
        foreach (var file in FixtureFiles())
        {
            data.Add(file);
        }

        return data;
    }

    private static IEnumerable<string> FixtureFiles() =>
        Directory.GetFiles(FixtureDir, "v*.json").Select(Path.GetFileName).Order(StringComparer.Ordinal)!;

    [Fact]
    public void Fixtures_cover_every_older_schema_that_shipped_and_the_pre_yard_content()
    {
        var versions = FixtureFiles().Select(VersionOf).ToHashSet();

        // v3 只在同一次提交内存在过（3e7f6ff 直接从 v2 升到 v4），玩家手里不会有。
        Assert.Equal([1, 2, 4], versions.Order());
        Assert.Contains(FileSaveStore.CurrentSchemaVersion, versions);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Old_save_loads_migrates_and_plays_on_to_the_chapter_end(string file)
    {
        var version = VersionOf(file);
        var point = file.Split('.')[1];
        var dir = Path.Combine(Path.GetTempPath(), "wuxia-oldsave-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSaveStore(dir);
            Directory.CreateDirectory(dir);
            var slotPath = store.PathOf(SaveSlot.Quick);
            File.Copy(Path.Combine(FixtureDir, file), slotPath);

            var read = store.Read(SaveSlot.Quick);
            Assert.True(read.Ok, read.Error);
            Assert.False(read.FromBackup);
            var migrated = version < FileSaveStore.CurrentSchemaVersion;
            Assert.Equal(migrated, read.Migrated);
            Assert.Equal(migrated, File.Exists($"{slotPath}.v{version}.bak"));

            var rules = ChapterOneWalkthroughTests.Rules();
            var resumed = SaveResume.Resume(read, rules, new GrowthRules(rules, TestContent.Real), ChapterOneWalkthroughTests.Bundle.Value.ContentVersion);
            var game = resumed.Game ?? throw new InvalidOperationException(resumed.Error);
            Assert.Contains(resumed.Notes, n => n.Contains("另一内容版本", StringComparison.Ordinal));
            Assert.Equal(version == 1, resumed.Notes.Any(n => n.Contains("人物成长之前", StringComparison.Ordinal)));

            // 迁移补出的字段：主角成长构成（v1 由读档补齐）、到过的地图（v2 → v3）、阵位与同行记录（v3 → v4）。
            var w = game.World;
            Assert.True(w.Builds.ContainsKey(rules.Content.Progression.Hero));
            Assert.Contains(w.MapId, w.Visited);
            Assert.All(w.Party, id => Assert.True(w.Formation.ContainsKey(id), $"{id} 没有阵位"));
            Assert.All(w.Party.Skip(1), id => Assert.True(w.Companions.ContainsKey(id), $"{id} 没有同行记录"));
            if (point != "inn_training")
            {
                Assert.NotEmpty(w.Skills);
            }

            PlayToEnd(game, version, point);
            Assert.Equal(QuestStatus.Completed, game.World.QuestStatusOf(ChapterOneRoute.MainQuest));
            Assert.Equal(QuestStatus.Completed, game.World.QuestStatusOf(ChapterOneRoute.SideQuest));
            Assert.True(game.CanSave);

            // 当前版本另存后重读：不再迁移，世界不变。
            var header = new SaveHeader { ContentVersion = ChapterOneWalkthroughTests.Bundle.Value.ContentVersion, Sequence = store.NextSequence(), MapId = game.World.MapId };
            Assert.True(store.Write(SaveSlot.Quick, new SaveGame { Header = header, World = game.World }).Ok);
            var again = store.Read(SaveSlot.Quick);
            Assert.False(again.Migrated);
            Assert.Equal(game.World.Hash(), again.Game!.World.Hash());
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
    public void Pre_yard_save_after_the_lesson_still_finds_the_spar_in_the_backyard()
    {
        // 后院改版前，讨教之后人在大堂、切磋入口也在大堂；读入后切磋入口改在后院，从大堂后门进去就能找到。
        var dir = Path.Combine(Path.GetTempPath(), "wuxia-oldsave-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSaveStore(dir);
            Directory.CreateDirectory(dir);
            File.Copy(Path.Combine(FixtureDir, "v4.inn_after_mentor.json"), store.PathOf(SaveSlot.Quick));
            var rules = ChapterOneWalkthroughTests.Rules();
            var game = SaveResume.Resume(store.Read(SaveSlot.Quick), rules, new GrowthRules(rules, TestContent.Real), "").Game!;
            Assert.Equal("map.jiangnan.inn", game.World.MapId);
            Assert.DoesNotContain(game.Events, e => e.Id.StartsWith("event.ch01.mentor_spar_", StringComparison.Ordinal));

            Assert.True(game.CommitTransition(game.BeginExit("to_yard")).Ok);
            Assert.Contains(game.Events, e => e.Id == "event.ch01.mentor_spar_linghu");
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    /// <summary>从样本所在位置接着走到章末（与样本写下时同一套选择；位置名见文件名）。</summary>
    private static void PlayToEnd(GameSession game, int version, string point)
    {
        var (mentor, custody) = PicksOf(version);
        var from = point switch
        {
            "inn_training" => ChapterOnePoint.Mentor,
            "inn_after_mentor" or "shore_side_steps" => ChapterOnePoint.SparBattle,
            "street_departure" => ChapterOnePoint.Departure,
            "ferry_after_escort" => ChapterOnePoint.Sluice,
            "ferry_custody" => ChapterOnePoint.Custody,
            "chapter_end" => ChapterOnePoint.End,
            _ => throw new InvalidOperationException($"未知样本位置 {point}"),
        };

        if (point == "shore_side_steps")
        {
            // 支线查证已在河滩做完，回客栈大堂接着选同行者。
            Assert.True(game.CommitTransition(game.BeginExit("to_street")).Ok);
            Assert.True(game.CommitTransition(game.BeginExit("to_inn")).Ok);
        }

        var route = new ChapterOneRoute(game)
        {
            From = from,
            Companion = "huang",
            Mentor = mentor,
            Custody = custody,
            Side = true,
            FinishSideSteps = point is not ("shore_side_steps" or "street_departure" or "ferry_after_escort" or "ferry_custody"),
        };
        route.RunTo(ChapterOnePoint.End);
    }

    private static int VersionOf(string file) => int.Parse(file[1..file.IndexOf('.', StringComparison.Ordinal)], System.Globalization.CultureInfo.InvariantCulture);
}
