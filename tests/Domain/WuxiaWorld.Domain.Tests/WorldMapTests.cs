using System.Security.Cryptography;
using System.Text;
using WuxiaWorld.Application.Persistence;
using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Infrastructure.Content;
using WuxiaWorld.Infrastructure.Saves;

namespace WuxiaWorld.Domain.Tests;

/// <summary>
/// M2-02 江湖大地图（架构文档 6.5）：地标状态（所在、到访、可前往、开放条件）、从同一处地标的任意小地图启程、
/// 预告地标随线索出现、v2 存档迁移补到访记录，以及大地图内容校验。读仓库里的正式内容。
/// </summary>
public sealed class WorldMapTests : IDisposable
{
    private const string Luwan = "node.jiangnan.luwan";
    private const string OldFerry = "node.jiangnan.old_ferry";
    private const string Granary = "node.jiangnan.granary";
    private const string Pass = "node.jiangnan.mountain_pass";
    private const string ToFerry = "route.jiangnan.luwan_to_old_ferry";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wuxia-worldmap-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static WorldNodeStatus Node(GameSession g, string id) => g.WorldMapNodes.Single(n => n.Node.Id == id);

    /// <summary>走到选好同行者、还在客栈里（尚未出门去码头）。</summary>
    private static ChapterOneWalkthroughTests.Walker ReadyToTravel()
    {
        var w = new ChapterOneWalkthroughTests.Walker(ChapterOneWalkthroughTests.Rules());
        w.PlayAuto();
        w.Interact("stone_marks");
        w.Exit("to_street");
        w.Exit("to_inn");
        w.PlayAuto();
        w.Exit("to_yard");
        w.Pick("choice.linghu");
        w.PlayEvent("event.ch01.mentor_choice");
        w.Exit("to_hall");
        w.Pick("choice.linghu");
        w.PlayEvent("event.ch01.companion_choice");
        Assert.Equal("map.jiangnan.inn", w.Game.World.MapId);
        return w;
    }

    [Fact]
    public void New_game_shows_luwan_here_and_the_old_ferry_locked_with_the_route_hint()
    {
        var g = GameSession.NewGame(ChapterOneWalkthroughTests.Rules());
        Assert.Equal(["map.jiangnan.luwan_shore"], g.World.Visited);
        Assert.Equal([Luwan, OldFerry], g.WorldMapNodes.Select(n => n.Node.Id));

        var here = Node(g, Luwan);
        Assert.True(here.Here);
        Assert.True(here.Visited);
        Assert.True(here.Open);

        var ferry = Node(g, OldFerry);
        Assert.False(ferry.Open);
        Assert.False(ferry.Visited);
        Assert.Null(ferry.Route);
        Assert.Equal("route.jiangnan.luwan_to_old_ferry.locked", ferry.LockedHint);
        Assert.Empty(g.Routes);
    }

    [Fact]
    public void Travel_can_start_from_any_map_of_the_same_place_and_marks_the_destination_visited()
    {
        var w = ReadyToTravel();
        var g = w.Game;
        var ferry = Node(g, OldFerry);
        Assert.True(ferry.Open);
        Assert.Equal(ToFerry, ferry.Route?.Id);
        Assert.Contains(g.Routes, r => r.Id == ToFerry);

        // 人在客栈里，也能从大地图乘船（不必先走到街南码头）；只扣一次船钱。
        var silver = g.World.Silver;
        var t = g.BeginRoute(ToFerry, TravelMode.Ferry);
        Assert.True(g.CommitTransition(t).Ok);
        Assert.False(g.CommitTransition(t).Ok);
        Assert.Equal(silver - 5, g.World.Silver);
        Assert.Equal("map.jiangnan.old_ferry", g.World.MapId);
        Assert.Contains("map.jiangnan.old_ferry", g.World.Visited);

        var luwan = Node(g, Luwan);
        Assert.False(luwan.Here);
        Assert.True(luwan.Visited);
        Assert.True(Node(g, OldFerry).Here);
        Assert.Contains(g.EventsIn(Node(g, OldFerry).Node), e => e.Id == "event.ch01.old_ferry_arrival");
    }

    [Fact]
    public void Route_from_another_place_is_still_refused()
    {
        var w = ReadyToTravel();
        Assert.True(w.Game.CommitTransition(w.Game.BeginRoute(ToFerry, TravelMode.Ferry)).Ok);
        // 已在旧渡：去旧渡的路线起点不在此处。
        Assert.Throws<InvalidOperationException>(() => w.Game.BeginRoute(ToFerry, TravelMode.Ferry));
    }

    [Fact]
    public void Next_chapter_places_appear_locked_once_the_two_leads_are_known()
    {
        var w = new ChapterOneWalkthroughTests.Walker(ChapterOneWalkthroughTests.Rules());
        w.PlayChapter("xiao", "sealed", side: false);
        var g = w.Game;
        Assert.Contains("clue.ch01.next_leads", g.World.Clues);
        Assert.Equal([Luwan, OldFerry, Granary, Pass], g.WorldMapNodes.Select(n => n.Node.Id));
        Assert.True(Node(g, OldFerry).Visited);
        foreach (var id in new[] { Granary, Pass })
        {
            var n = Node(g, id);
            Assert.False(n.Open);
            Assert.False(n.Visited);
            Assert.Equal(id + ".locked", n.LockedHint);
        }

        // 章末回到芦湾后，回旧渡的路仍开着。
        Assert.True(Node(g, OldFerry).Open);
    }

    [Fact]
    public void Visited_maps_survive_save_and_load()
    {
        var w = ReadyToTravel();
        var store = new FileSaveStore(_dir);
        store.Write(SaveSlot.Manual(2), new SaveGame { Header = new SaveHeader { Sequence = 1 }, World = w.Game.World });
        var read = store.Read(SaveSlot.Manual(2));
        Assert.True(read.Ok, read.Error);
        Assert.Equal(w.Game.World.Visited, read.Game!.World.Visited);
        Assert.Equal(w.Game.World.Hash(), read.Game.World.Hash());
    }

    /// <summary>迁移样本：v2 存档（世界里没有 visited）读入后只补当前所在的地图，原文件另存为 .v2.bak。</summary>
    [Fact]
    public void Version_two_save_gets_the_current_map_as_visited()
    {
        var w = ReadyToTravel();
        var header = new SaveHeader { SaveSchemaVersion = 2, Sequence = 3, MapId = w.Game.World.MapId };
        var payload = System.Text.Json.JsonSerializer.SerializeToNode(new SaveGame { Header = header, World = w.Game.World }, FileSaveStore.Json)!.AsObject();
        Assert.True(payload["world"]!.AsObject().Remove("visited"));
        var text = payload.ToJsonString();
        var sum = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "manual_04.json");
        File.WriteAllText(path, $"{{\"checksum\":\"{sum}\",\"payload\":{text}}}");

        var read = new FileSaveStore(_dir).Read(SaveSlot.Manual(4));
        Assert.True(read.Ok, read.Error);
        Assert.True(read.Migrated);
        Assert.True(File.Exists(path + ".v2.bak"));
        Assert.Equal(["map.jiangnan.inn"], read.Game!.World.Visited);
    }

    [Fact]
    public void Validator_reports_bad_world_map_data()
    {
        var b = ChapterOneWalkthroughTests.Bundle.Value;
        var wm = b.WorldMap!;
        var broken = b with
        {
            WorldMap = wm with
            {
                Nodes =
                [
                    .. wm.Nodes.Select(n => n.Id switch
                    {
                        // 河滩不再属于芦湾；旧渡多挂一张客栈（已属芦湾）与不存在的图，坐标出界。
                        Luwan => n with { Maps = [.. n.Maps.Where(m => m != "map.jiangnan.luwan_shore")] },
                        OldFerry => n with { Maps = [.. n.Maps, "map.jiangnan.inn", "map.none"], Pos = [3000, 10] },
                        Granary => n with { LockedHint = null },
                        _ => n,
                    }),
                    new WorldNodeDefinition { Id = "place.bad", Region = "region.jiangnan", Pos = [1, 1], Maps = ["map.jiangnan.luwan_shore"] },
                ],
                // 去掉芦湾—旧渡的水路；加一条连到未定义地标的路。
                Roads = [.. wm.Roads.Where(r => r.Kind != RoadKind.Water), new WorldRoadDefinition { From = Luwan, To = "node.none", Via = [[1, 2, 3]] }],
            },
        };

        var errors = WorldContentValidator.Validate(broken);
        Assert.Contains(errors, e => e.Contains("map.none", StringComparison.Ordinal) && e.Contains("不存在", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("map.jiangnan.inn", StringComparison.Ordinal) && e.Contains("只能属于一处", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains(OldFerry, StringComparison.Ordinal) && e.Contains("坐标", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains(Granary, StringComparison.Ordinal) && e.Contains("locked_hint", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("place.bad", StringComparison.Ordinal) && e.Contains("node.", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("place.bad", StringComparison.Ordinal) && e.Contains("缺少文本", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("node.none", StringComparison.Ordinal) && e.Contains("起讫", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("node.none", StringComparison.Ordinal) && e.Contains("途经点", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains(ToFerry, StringComparison.Ordinal) && e.Contains("缺少水路", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, e => e.Contains(ToFerry, StringComparison.Ordinal) && e.Contains("缺少陆路", StringComparison.Ordinal));
        Assert.DoesNotContain(WorldContentValidator.Validate(b), e => e.Contains("大地图", StringComparison.Ordinal) || e.Contains("node.", StringComparison.Ordinal));
    }
}
