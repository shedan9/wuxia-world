using System.Text.Json;
using Godot;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 江湖大地图的地形数据（<c>res://assets/maps/worldmap.terrain.json</c>，2026-10-25 按中国地理重做）：
/// 文件里写经纬度，按 <c>projection</c> 的分段线性表换算到 frame 像素（故事集中的中东部放宽、西域与漠北压窄）。
/// 陆地、海岛、地貌、湖、江河、运河、山脉、长城、关隘、城池、地点与地域题字都从这里取；
/// 高度图与地貌权重图由 <see cref="WorldTerrainBake"/> 离线烘焙成贴图，画布只读贴图。
/// </summary>
public sealed class WorldTerrain
{
    public const string Path = "res://assets/maps/worldmap.terrain.json";

    private static WorldTerrain? _current;

    /// <summary>地貌种类，顺序即烘焙权重图的通道：biome0 = 前四种 RGBA，biome1 = 中四种，biome2 = 后四种。steppe 为西北荒原（干草地，铺在其余地貌之下）。</summary>
    public static readonly string[] BiomeKinds = ["desert", "gobi", "grass", "plateau", "forest", "paddy", "wheat", "loess", "jungle", "marsh", "snow", "steppe"];

    private readonly (float From, float To)[] _lon;
    private readonly (float From, float To)[] _lat;

    private WorldTerrain(JsonElement root)
    {
        Frame = new Vector2(root.GetProperty("frame")[0].GetSingle(), root.GetProperty("frame")[1].GetSingle());
        var projection = root.GetProperty("projection");
        _lon = Table(projection.GetProperty("lon"));
        _lat = Table(projection.GetProperty("lat"));

        Land = [Poly(root.GetProperty("land")), .. root.GetProperty("islands").EnumerateArray().Select(i => Poly(i.GetProperty("poly")))];
        Biomes = [.. root.GetProperty("biomes").EnumerateArray().Select(b => new Biome(Array.IndexOf(BiomeKinds, b.GetProperty("kind").GetString()), Poly(b.GetProperty("poly"))))];
        Lakes = [.. root.GetProperty("lakes").EnumerateArray().Select(l => new Lake(Point(l.GetProperty("at")), V(l.GetProperty("r"))))];
        Rivers =
        [
            .. root.GetProperty("rivers").EnumerateArray().Select(r => new River(
                r.GetProperty("key").GetString()!, Label(r), Poly(r.GetProperty("path")), r.GetProperty("w")[0].GetSingle(), r.GetProperty("w")[1].GetSingle())),
        ];
        Canal = Poly(root.GetProperty("canal"));
        Ranges =
        [
            .. root.GetProperty("ranges").EnumerateArray().Select(r => new Range(
                r.GetProperty("key").GetString()!, Poly(r.GetProperty("path")), r.GetProperty("h").GetSingle(), r.GetProperty("w").GetSingle(),
                r.TryGetProperty("snow", out var s) && s.GetBoolean(), r.TryGetProperty("arid", out var a) && a.GetBoolean())),
        ];
        GreatWall = Poly(root.GetProperty("great_wall"));
        Passes = [.. root.GetProperty("passes").EnumerateArray().Select(p => new Place(p.GetProperty("key").GetString()!, Label(p), "pass", Point(p.GetProperty("at"))))];
        Cities =
        [
            .. root.GetProperty("cities").EnumerateArray().Select(c => new Place(
                c.GetProperty("key").GetString()!, Label(c), c.TryGetProperty("big", out var b) && b.GetBoolean() ? "big" : "town", Point(c.GetProperty("at")))),
        ];
        Sites =
        [
            .. root.GetProperty("sites").EnumerateArray().Select(s => new Place(
                s.GetProperty("key").GetString()!, Label(s), s.GetProperty("kind").GetString()!, Point(s.GetProperty("at")))),
        ];
        Titles = [.. root.GetProperty("titles").EnumerateArray().Select(t => new Title(t.GetProperty("text").GetString()!, Point(t.GetProperty("at")), t.GetProperty("size").GetInt32()))];
    }

    /// <summary>当前地形（首次取用时读文件）。</summary>
    public static WorldTerrain Current => _current ??= Load();

    /// <summary>设计稿尺寸（地图坐标的范围）。</summary>
    public Vector2 Frame { get; }

    /// <summary>陆地多边形：第一个是大陆（含高丽半岛），其后是海岛；不在任一多边形内的都是海。</summary>
    public IReadOnlyList<Vector2[]> Land { get; }

    public IReadOnlyList<Biome> Biomes { get; }

    public IReadOnlyList<Lake> Lakes { get; }

    public IReadOnlyList<River> Rivers { get; }

    public Vector2[] Canal { get; }

    public IReadOnlyList<Range> Ranges { get; }

    public Vector2[] GreatWall { get; }

    public IReadOnlyList<Place> Passes { get; }

    public IReadOnlyList<Place> Cities { get; }

    /// <summary>STORY 点名的地点（门派、原著势力与剧情地名），画地标小景并标名。</summary>
    public IReadOnlyList<Place> Sites { get; }

    public IReadOnlyList<Title> Titles { get; }

    /// <summary>经纬度换算为地图坐标。</summary>
    public Vector2 Project(float lon, float lat) => new(Piecewise(lon, _lon), Piecewise(lat, _lat));

    private static WorldTerrain Load()
    {
        using var file = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Read);
        using var doc = JsonDocument.Parse(file.GetAsText());
        return new WorldTerrain(doc.RootElement);
    }

    private static (float, float)[] Table(JsonElement e) => [.. e.EnumerateArray().Select(p => (p[0].GetSingle(), p[1].GetSingle()))];

    private static float Piecewise(float v, (float From, float To)[] table)
    {
        var i = 1;
        while (i < table.Length - 1 && (table[1].From > table[0].From ? v > table[i].From : v < table[i].From))
        {
            i++;
        }

        var (a, x0) = table[i - 1];
        var (b, x1) = table[i];
        return x0 + (v - a) / (b - a) * (x1 - x0);
    }

    private Vector2 Point(JsonElement e) => Project(e[0].GetSingle(), e[1].GetSingle());

    private Vector2[] Poly(JsonElement e) => [.. e.EnumerateArray().Select(Point)];

    private static Vector2 V(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle());

    private static string? Label(JsonElement e) => e.TryGetProperty("label", out var l) ? l.GetString() : null;

    // ── 几何 ─────────────────────────────────────────────

    /// <summary>点是否在陆地上（大陆或任一海岛）。</summary>
    public bool OnLand(Vector2 p) => Land.Any(poly => Inside(poly, p));

    /// <summary>点到海岸的有符号距离：陆上为正、海上为负。</summary>
    public float CoastDistance(Vector2 p)
    {
        var d = Land.Min(poly => EdgeDistance(poly, p, closed: true));
        return OnLand(p) ? d : -d;
    }

    /// <summary>点到湖岸的“椭圆距离”：湖心为 0、湖岸为 1。</summary>
    public float LakeFactor(Vector2 p) => Lakes.Count == 0 ? float.MaxValue : Lakes.Min(l => ((p - l.Center) / l.Radius).Length());

    /// <summary>射线法判断点在多边形内。</summary>
    public static bool Inside(Vector2[] poly, Vector2 p)
    {
        var inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            var (a, b) = (poly[i], poly[j]);
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>点到折线（closed 时含首尾相接的边）的最短距离。</summary>
    public static float EdgeDistance(Vector2[] path, Vector2 p, bool closed = false)
    {
        var best = float.MaxValue;
        var n = closed ? path.Length : path.Length - 1;
        for (var i = 0; i < n; i++)
        {
            var c = Geometry2D.GetClosestPointToSegment(p, path[i], path[(i + 1) % path.Length]);
            best = Mathf.Min(best, c.DistanceSquaredTo(p));
        }

        return Mathf.Sqrt(best);
    }

    /// <summary>折线的外框。</summary>
    public static Rect2 Bounds(Vector2[] path) => path.Skip(1).Aggregate(new Rect2(path[0], Vector2.Zero), (r, p) => r.Expand(p));

    /// <summary>一种地貌的一块范围。Kind 为 <see cref="BiomeKinds"/> 的序号。</summary>
    public sealed record Biome(int Kind, Vector2[] Poly);

    /// <summary>湖：椭圆，半径为地图像素。</summary>
    public sealed record Lake(Vector2 Center, Vector2 Radius);

    /// <summary>江河：源头宽 From、入海口宽 To（地图像素）；Label 为图上注记（只有剧情点名的才有）。</summary>
    public sealed record River(string Key, string? Label, Vector2[] Path, float From, float To);

    /// <summary>山脉：走向折线、峰高（0–1）、山体半宽（地图像素）、是否积雪、是否干旱（岩色偏赭灰）。</summary>
    public sealed record Range(string Key, Vector2[] Path, float Peak, float Width, bool Snow, bool Arid);

    /// <summary>城池、关隘与地点：Kind 为样式（城池 big / town；关隘 pass；地点 temple、taoist、sect、camp、relay、fort、tomb、island、port、manor）。</summary>
    public sealed record Place(string Key, string? Label, string Kind, Vector2 At);

    public sealed record Title(string Text, Vector2 At, int Size);
}
