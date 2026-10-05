using WuxiaWorld.Application.Dev;
using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.World;

namespace WuxiaWorld.Domain.Tests;

/// <summary>
/// 开发用第一章路线（<see cref="ChapterOneRoute"/>，游戏的 <c>--jump</c> 与战斗测试台共用）：每个跳关点都停得住、停在预期的地图与状态，
/// 从任一跳关点接着走到章末与一口气走完的世界状态相同；测试台用的等级覆盖与潜能分配可用。
/// </summary>
public class ChapterOneRouteTests
{
    private static GameSession NewGame()
    {
        var rules = ChapterOneWalkthroughTests.Rules();
        return GameSession.NewGame(rules, new GrowthRules(rules, TestContent.Real));
    }

    public static TheoryData<string, bool> Routes => new()
    {
        { "linghu", false },
        { "huang", true },
        { "xiao", false },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public void Every_jump_point_stops_where_expected_and_resuming_matches_a_straight_run(string companion, bool side)
    {
        var straight = new ChapterOneRoute(NewGame()) { Companion = companion, Side = side };
        straight.RunTo(ChapterOnePoint.End);
        Assert.Equal(QuestStatus.Completed, straight.Game.World.QuestStatusOf(ChapterOneRoute.MainQuest));

        var expected = new Dictionary<ChapterOnePoint, (string Map, string? Battle)>
        {
            [ChapterOnePoint.Start] = ("map.jiangnan.luwan_shore", null),
            [ChapterOnePoint.InnCouncil] = ("map.jiangnan.inn", null),
            [ChapterOnePoint.Mentor] = ("map.jiangnan.inn", null),
            [ChapterOnePoint.SparBattle] = ("map.jiangnan.inn_yard", null),
            [ChapterOnePoint.Departure] = ("map.jiangnan.luwan_street", null),
            [ChapterOnePoint.OldFerry] = ("map.jiangnan.old_ferry", null),
            [ChapterOnePoint.EscortBattle] = ("map.jiangnan.old_ferry", "battle.01.escort_skirmish"),
            [ChapterOnePoint.Sluice] = ("map.jiangnan.old_ferry", null),
            [ChapterOnePoint.SluiceBattle] = ("map.jiangnan.old_ferry", "battle.01.old_ferry_sluice"),
            [ChapterOnePoint.Custody] = ("map.jiangnan.old_ferry", null),
            [ChapterOnePoint.Epilogue] = ("map.jiangnan.inn", null),
        };

        foreach (var (point, (map, battle)) in expected)
        {
            var route = new ChapterOneRoute(NewGame()) { Companion = companion, Side = side };
            route.RunTo(point);
            Assert.Equal(point, route.Reached);
            Assert.Equal(map, route.Game.World.MapId);
            Assert.Equal(battle, route.Game.World.Battle?.Encounter);

            route.RunTo(ChapterOnePoint.End);
            Assert.Equal(straight.Game.World.Hash(), route.Game.World.Hash());
        }
    }

    [Fact]
    public void Bench_helpers_set_the_level_and_spend_potential_before_the_battle()
    {
        var route = new ChapterOneRoute(NewGame()) { Companion = "xiao", Mentor = "huang", AllocatePotential = true };
        route.RunTo(ChapterOnePoint.Sluice);
        var growth = route.Game.Growth!;
        Assert.Equal(0, growth.Unspent(route.Game.World, "char.hero"));
        Assert.Equal("huang", route.Game.World.Facts["fact.ch01.mentor"]);

        route.SetHeroLevel(3);
        Assert.Equal(3, growth.Level(route.Game.World));
        route.SetHeroLevel(99);
        Assert.Equal(route.Game.Rules.Content.Progression.MaxLevel, growth.Level(route.Game.World));
        Assert.True(route.AllocateRecommended() > 0);
        Assert.Equal(0, growth.Unspent(route.Game.World, "char.hero"));

        route.RunTo(ChapterOnePoint.SluiceBattle);
        Assert.NotNull(route.Game.World.Battle);
        var setup = route.Game.StoryBattleSetup(seed: 1);
        Assert.Contains(setup.Allies, a => a.UnitId == "char.xiao_feng");
        Assert.Contains("variant.ch01.ferryman_freed", setup.Variants);
    }

    [Fact]
    public void A_broken_route_says_where_it_got_stuck()
    {
        var route = new ChapterOneRoute(NewGame()) { Companion = "nobody" };
        var ex = Assert.Throws<InvalidOperationException>(() => route.RunTo(ChapterOnePoint.Departure));
        Assert.Contains("找不到选项 choice.nobody", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Mentor", ex.Message, StringComparison.Ordinal);
    }
}
