using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.Art;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 江湖大地图（2026-10-25 按中国地理重做，地形数据见 <see cref="WorldTerrain"/>）：
/// 地面由 <c>assets/shaders/world_relief.gdshader</c> 读离线烘焙的高度图与地貌权重图着色（<see cref="WorldTerrainBake"/>）；
/// 其上按层画江河与运河、长城，再把立在地面上的东西——平视山峦、城池、村落、毡帐、门派与剧情地点小景、关隘——
/// 按底边由远到近排序、分成横向条带（每条一个画布项，放大时屏幕外的条带整条剔除），最后是道路与地名。
/// 山峦逐组出图：有 <c>assets/art/worldmap/&lt;id&gt;.png</c> 时贴件，否则程序化画（<see cref="PlanMassifs"/>）。
/// 全部形状只取决于地形数据与固定种子。地标位置（山体避让）与道路由构造参数给出：展示页用样例，游戏内大地图用内容数据。
/// </summary>
public partial class WorldMapCanvas : Control
{
    private readonly IReadOnlyList<Vector2> _landmarks;
    private readonly IReadOnlyList<MapRoad> _roads;

    /// <summary>展示页：样例地标与路网。</summary>
    public WorldMapCanvas()
        : this(WorldMapSamples.Nodes.Select(n => n.Pos).ToArray(), WorldMapSamples.Edges.Select(e => new MapRoad(RoutePath(e), e.Water)).ToArray())
    {
    }

    /// <param name="landmarks">地标位置（地图坐标），山体避开这些空地。</param>
    /// <param name="roads">要画出的道路。</param>
    public WorldMapCanvas(IReadOnlyList<Vector2> landmarks, IReadOnlyList<MapRoad> roads)
    {
        _landmarks = landmarks;
        _roads = roads;
    }

    /// <summary>地图画布的逻辑尺寸（地形数据的 frame）。</summary>
    public static Vector2 MapSize => WorldTerrain.Current.Frame;

    /// <summary>只画地形、江河与地上景物（导出审看图与 AI 引导图用），不画道路与地名。</summary>
    public bool TerrainOnly { get; init; }

    private static WorldTerrain T => WorldTerrain.Current;

    /// <summary>立物条带的高度（地图像素）。</summary>
    private const float BandHeight = 320;

    private static readonly Color RiverTone = new(0.46f, 0.75f, 0.8f);
    private static readonly Color RiverBank = new(0.16f, 0.24f, 0.26f, 0.8f);
    private static readonly Color Ink = new(0.16f, 0.24f, 0.26f, 0.9f);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = MapSize;

        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/world_relief.gdshader") };
        material.SetShaderParameter("height_map", GD.Load<Texture2D>(WorldTerrainBake.HeightPath));
        for (var i = 0; i < 3; i++)
        {
            material.SetShaderParameter($"biome{i}", GD.Load<Texture2D>(WorldTerrainBake.BiomePath(i)));
        }

        material.SetShaderParameter("map_size", MapSize);
        AddChild(new ColorRect { Material = material, Size = MapSize, MouseFilter = MouseFilterEnum.Ignore });

        Layer(DrawGround);
        Layer(DrawWater);
        Layer(DrawWall);
        var props = PlanProps();
        foreach (var band in props.GroupBy(p => Mathf.FloorToInt(p.Base.Y / BandHeight)).OrderBy(g => g.Key))
        {
            var list = band.OrderBy(p => p.Base.Y).ToList();
            Layer(item =>
            {
                foreach (var prop in list)
                {
                    prop.Draw(item);
                }
            });
        }

        if (!TerrainOnly)
        {
            Layer(DrawRoads);
            Layer(DrawLabels);
        }
    }

    private void Layer(Action<Rid> draw)
    {
        var node = new Node2D();
        node.Draw += () => draw(node.GetCanvasItem());
        AddChild(node);
    }

    /// <summary>立在地面上的一件东西：底边位置与画法。</summary>
    private readonly record struct Prop(Vector2 Base, Action<Rid> Draw);

    private List<Prop> PlanProps()
    {
        var props = new List<Prop>();
        var massifs = PlanMassifs();
        foreach (var g in massifs)
        {
            props.Add(new Prop(g.Main.Base, item => DrawMassifWithMist(item, g)));
        }

        foreach (var c in T.Cities)
        {
            props.Add(new Prop(c.At, item => Scaled(item, c.At, c.Kind == "big" ? 1.2f : 1.05f, () => DrawCity(item, c))));
        }

        foreach (var s in T.Sites)
        {
            props.Add(new Prop(s.At, item => Scaled(item, s.At, SiteScale, () => DrawSite(item, s))));
        }

        foreach (var p in T.Passes)
        {
            props.Add(new Prop(p.At + new Vector2(0, 2), item => DrawGate(item, p.At, 1.3f)));
        }

        var blocked = massifs.Select(m => m.Bounds.Grow(6)).ToList();
        foreach (var (at, seed, style) in PlanVillages(blocked, _landmarks))
        {
            props.Add(new Prop(at, item => DrawVillage(item, at, seed, style)));
        }

        foreach (var (at, seed) in PlanYurts(blocked))
        {
            props.Add(new Prop(at, item => DrawYurts(item, at, seed)));
        }

        return props;
    }

    // ── 地面层：绿洲 ─────────────────────────────────────

    /// <summary>大漠与戈壁里的城池脚下铺一片绿洲。</summary>
    private static void DrawGround(Rid item)
    {
        foreach (var c in T.Cities.Where(c => Arid(c.At)))
        {
            Brushwork.Blot(item, c.At + new Vector2(0, -6), c.Kind == "big" ? 80 : 58, new Color(0.5f, 0.72f, 0.46f, 0.85f), c.At.X * 0.01f);
            Brushwork.Blot(item, c.At + new Vector2(10, -2), c.Kind == "big" ? 56 : 40, new Color(0.42f, 0.66f, 0.42f, 0.6f), c.At.Y * 0.01f);
        }
    }

    private static bool InBiome(Vector2 p, params string[] kinds) =>
        T.Biomes.Any(b => kinds.Contains(WorldTerrain.BiomeKinds[b.Kind]) && WorldTerrain.Inside(b.Poly, p));

    private static bool Arid(Vector2 p) => InBiome(p, "desert", "gobi") || p.X < T.Project(96, 40).X && !InBiome(p, "grass", "plateau", "snow");

    // ── 江河与运河 ───────────────────────────────────────

    private static void DrawWater(Rid item)
    {
        for (var i = 0; i < T.Rivers.Count; i++)
        {
            var r = T.Rivers[i];
            DrawRiver(item, r.Path, r.From, r.To, 11 + i * 2);
        }

        // 运河：窄而直的浅色水道，两岸各一道细堤。
        var canal = Wobble(T.Canal, 12, 1.5f, 3);
        Brushwork.Stroke(item, canal, 6.4f, RiverBank, 5, 0, 0, 0);
        Brushwork.Stroke(item, canal, 3.6f, RiverTone.Lightened(0.15f), 6, 0, 0, 0);

        // 帆船：大江、运河、钱塘江上与近海。
        var boats = 0;
        foreach (var key in new[] { "changjiang", "qiantang", "xijiang", "huanghe", "hanshui", "wusong" })
        {
            var river = T.Rivers.FirstOrDefault(r => r.Key == key);
            if (river is null)
            {
                continue;
            }

            var length = PathLength(river.Path);
            for (var d = 140f; d < length; d += 260 + 200 * Brushwork.Hash(d + boats))
            {
                var t = d / length;
                var (p, dir) = AlongPathDir(river.Path, t);
                if (Mathf.Lerp(river.From, river.To, t) > 6)
                {
                    DrawBoat(item, p, dir.X >= 0 ? 1 : -1, 0.7f + 0.3f * t, boats++);
                }
            }
        }

        for (var d = 200f; d < PathLength(T.Canal); d += 300)
        {
            var (p, dir) = AlongPathDir(T.Canal, d / PathLength(T.Canal));
            DrawBoat(item, p, dir.X >= 0 ? 1 : -1, 0.55f, boats++);
        }

        // 近海：沿海岸外 60–240 像素撒船，避开外海。
        for (var k = 0; k < 400; k++)
        {
            var p = new Vector2(Brushwork.Hash(k * 3.7f) * MapSize.X, Brushwork.Hash(k * 5.3f + 1) * MapSize.Y);
            var d = T.CoastDistance(p);
            if (d < -60 && d > -240)
            {
                DrawBoat(item, p, Brushwork.Hash(k + 0.5f) < 0.5f ? 1 : -1, 1.1f, boats++);
            }
        }
    }

    private static Vector2[] Wobble(ReadOnlySpan<Vector2> path, float step, float amp, float seed)
    {
        var result = new List<Vector2>();
        var walked = 0f;
        for (var i = 0; i < path.Length - 1; i++)
        {
            var a = path[i];
            var b = path[i + 1];
            var len = a.DistanceTo(b);
            var normal = (b - a).Normalized().Orthogonal();
            var n = Mathf.Max(1, Mathf.CeilToInt(len / step));
            for (var j = 0; j < n; j++)
            {
                var along = walked + len * j / n;
                var push = (Brushwork.Noise(along / 60f + seed) - 0.5f) * 2 * amp;
                result.Add(a.Lerp(b, (float)j / n) + normal * push);
            }

            walked += len;
        }

        result.Add(path[^1]);
        return result.ToArray();
    }

    /// <summary>河：自源头细到下游宽，先铺一道暗色河岸，再铺水色，不描墨线。</summary>
    private static void DrawRiver(Rid item, Vector2[] path, float from, float to, float seed)
    {
        var pts = Wobble(path, 10, 4 + to * 0.3f, seed);
        var pieces = Mathf.Max(4, pts.Length / 24);
        var per = Mathf.Max(2, pts.Length / pieces);
        foreach (var (tone, extra) in new[] { (RiverBank, 2.6f), (RiverTone, 0f) })
        {
            for (var k = 0; k * per < pts.Length - 1; k++)
            {
                var start = k * per;
                var end = Mathf.Min(pts.Length - 1, start + per + 1);
                var t = (float)(start + end) / 2 / pts.Length;
                Brushwork.Stroke(item, pts.AsSpan(start, end - start + 1), Mathf.Lerp(from, to, t) + extra, tone, seed + k, 0, 0, 0);
            }
        }
    }

    /// <summary>帆船：深色船身加一面米白帆，dir 为朝向（1 向右）。</summary>
    private static void DrawBoat(Rid item, Vector2 p, int dir, float scale, int seed)
    {
        var s = scale * (0.85f + 0.3f * Brushwork.Hash(seed * 1.7f));
        Polygon(item, [p + new Vector2(-9 * s, 0), p + new Vector2(9 * s, 0), p + new Vector2(6 * s, 3 * s), p + new Vector2(-6 * s, 3 * s)], new Color(0.36f, 0.26f, 0.2f));
        var mast = p + new Vector2(-1 * s * dir, 0);
        Polygon(item, [mast, mast + new Vector2(0, -15 * s), mast + new Vector2(8 * s * dir, -2 * s)], new Color(0.95f, 0.92f, 0.82f));
        Line(item, mast, mast + new Vector2(0, -15 * s), Ink with { A = 0.7f }, 1);
    }

    // ── 长城 ─────────────────────────────────────────────

    /// <summary>长城：沿线一道石色墙身、北侧垛口，每隔一段一座烽燧。</summary>
    private static void DrawWall(Rid item)
    {
        var pts = Wobble(T.GreatWall, 8, 6, 41);
        Brushwork.Stroke(item, pts, 6.5f, new Color(0.32f, 0.26f, 0.2f, 0.85f), 41, 0, 0, 0);
        Brushwork.Stroke(item, pts, 3.6f, new Color(0.86f, 0.8f, 0.68f), 42, 0, 0, 0);
        var walked = 0f;
        var next = 60f;
        var tick = 0f;
        for (var i = 1; i < pts.Length; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            var len = a.DistanceTo(b);
            var dir = (b - a) / Mathf.Max(len, 0.01f);
            var north = dir.Orthogonal() * (dir.Orthogonal().Y < 0 ? 1 : -1);
            for (; tick < walked + len; tick += 7)
            {
                var p = a + dir * (tick - walked);
                Line(item, p + north * 1.5f, p + north * 4.5f, new Color(0.32f, 0.26f, 0.2f, 0.8f), 2);
            }

            while (next < walked + len)
            {
                var p = a + dir * (next - walked);
                var tower = new Rect2(p + new Vector2(-4, -11), new Vector2(8, 11));
                Polygon(item, Corners(tower), new Color(0.84f, 0.78f, 0.66f));
                Outline(item, Corners(tower), Ink, 1.2f);
                next += 150;
            }

            walked += len;
        }
    }

    // ── 山峦（平视） ─────────────────────────────────────

    private static readonly Color MountainLine = new(0.16f, 0.24f, 0.26f, 0.9f);

    /// <summary>一座山体。Layer 为 0 的主山带山脚阴影与积雪；1、2 为叠在其前方、更矮更绿的山梁。Snow、Arid 取自所属山脉。</summary>
    internal readonly record struct Massif(Vector2 Base, float Width, float Height, float Seed, int Layer = 0, bool Snow = false, bool Arid = false);

    /// <summary>
    /// 一组山：主山与叠在其前的矮山梁，AI 逐件出图时作为一件（PieceGuideExport --region=worldmap-massifs）。
    /// Id 为 <c>worldmap.massif.&lt;山脉键&gt;.&lt;排&gt;.&lt;序号&gt;</c>，只取决于山脉数据与固定种子，不随地标避让变化；
    /// Bounds 为天际线与底边的外框（地图坐标，含勾线余量）。
    /// </summary>
    public sealed record MassifGroup(string Id, Rect2 Bounds)
    {
        internal Massif Main { get; init; }

        internal Massif[] Spurs { get; init; } = [];
    }

    /// <summary>
    /// 沿每条山脉的走向摆放山体：主脊两三排，间距、大小、前后位置都随机错开，偶有一座特别高的主峰；
    /// 山脉前沿再撒一排低矮的丘陵；高大的主山前叠一两道矮山梁。按底边由远到近排序，前山压住后山；
    /// 避开海、江河、湖、城池、关隘与地点；avoidLandmarks 为真时另避开构造参数给的地标（导出引导图时不避，两套地标各取所需）。
    /// </summary>
    public IReadOnlyList<MassifGroup> PlanMassifs(bool avoidLandmarks = true)
    {
        var groups = new List<MassifGroup>();
        var r = 0;
        foreach (var range in T.Ranges)
        {
            r++;
            var (path, peak, width) = (range.Path, range.Peak, range.Width);
            var length = PathLength(path);
            var rows = width > 200 ? 3 : width > 100 ? 2 : 1;
            for (var row = 0; row <= rows; row++)
            {
                var foothills = row == rows;
                var across = foothills ? width * 0.62f : (row - (rows - 1) / 2f) * width * 0.48f;
                var scale = foothills ? 0.42f : 1.12f - 0.2f * row;
                var step = foothills ? 90f : 125f + 30 * row;
                var d = step * (0.2f + 0.6f * Brushwork.Hash(r * 5.1f + row * 2.7f));
                var k = 0;
                while (d < length)
                {
                    var seed = r * 31.7f + row * 7.3f + d * 0.013f;
                    var id = $"worldmap.massif.{range.Key}.{row}.{k++}";
                    d += step * (0.55f + 0.9f * Brushwork.Hash(seed + 8));
                    if (foothills && Brushwork.Hash(seed + 9) < 0.35f)
                    {
                        continue;
                    }

                    var c = AlongPath(path, Mathf.Min(1, d / length)) + new Vector2(
                        (Brushwork.Hash(seed) - 0.5f) * step * 0.5f,
                        across + (Brushwork.Hash(seed + 1) - 0.5f) * width * 0.4f);
                    // 大小悬殊：多数中等，少数高耸的主峰。
                    var size = Brushwork.Hash(seed + 2);
                    size = 0.35f + 0.65f * size * size + (size > 0.9f ? 0.35f : 0);
                    var h = (50 + 140 * peak * size) * scale;
                    var w = h * (2.3f + 1.6f * Brushwork.Hash(seed + 3));
                    if (!Blocked(c, w, h, avoidLandmarks))
                    {
                        groups.Add(Group(id, new Massif(c, w, h, seed, 0, range.Snow, range.Arid)));
                    }
                }
            }
        }

        return groups.OrderBy(g => g.Main.Base.Y).ToList();
    }

    /// <summary>主山与其前的一两道矮山梁（底边略靠前，形成层层叠叠的山势）。</summary>
    private static MassifGroup Group(string id, Massif m)
    {
        var spurs = m.Height > 70 ? 1 + (int)(Brushwork.Hash(m.Seed + 21) * 2.2f) : 0;
        var list = new Massif[spurs];
        for (var s = 0; s < spurs; s++)
        {
            var seed = m.Seed + 40 + s * 13.1f;
            var offset = (Brushwork.Hash(seed) - 0.5f) * m.Width * 0.6f;
            list[s] = new Massif(m.Base + new Vector2(offset, 4 + s * 6), m.Width * (0.4f + 0.25f * Brushwork.Hash(seed + 1)),
                m.Height * (0.4f + 0.2f * Brushwork.Hash(seed + 2)), seed, 1 + s, m.Snow, m.Arid);
        }

        var bounds = new Rect2(m.Base, Vector2.Zero);
        foreach (var x in list.Prepend(m))
        {
            foreach (var p in Skyline(x).Top)
            {
                bounds = bounds.Expand(p);
            }

            bounds = bounds.Expand(x.Base + new Vector2(0, 1));
        }

        return new MassifGroup(id, bounds.Grow(4)) { Main = m, Spurs = list };
    }

    private static void DrawMassifWithMist(Rid item, MassifGroup g)
    {
        var m = g.Main;
        Brushwork.Blot(item, m.Base + new Vector2(m.Width * 0.06f, -m.Height * 0.05f), m.Width * 0.34f, new Color(0.1f, 0.2f, 0.2f, 0.15f), m.Seed);
        if (PieceArt.Find(g.Id) is { } art)
        {
            RenderingServer.CanvasItemAddTextureRect(item, art.Frame, art.Texture.GetRid(), false);
        }
        else
        {
            DrawMassifGroup(item, g);
        }

        // 山脚一抹薄雾，把前后两排山隔开。
        Brushwork.Blot(item, m.Base + new Vector2((Brushwork.Hash(m.Seed + 30) - 0.5f) * m.Width * 0.3f, -m.Height * 0.08f),
            m.Width * 0.3f, new Color(0.94f, 0.97f, 0.96f, 0.28f), m.Seed + 31);
    }

    /// <summary>程序化画出一组山（不含山脚阴影与薄雾，那两层由引擎画在 AI 件上下）。</summary>
    public static void DrawMassifGroup(Rid item, MassifGroup g)
    {
        DrawMassif(item, g.Main);
        foreach (var spur in g.Spurs)
        {
            DrawMassif(item, spur);
        }
    }

    private bool Blocked(Vector2 c, float w, float h, bool avoidLandmarks)
    {
        var side = new Vector2(w * 0.32f, 0);
        if (!T.OnLand(c) || !T.OnLand(c + side) || !T.OnLand(c - side) || T.CoastDistance(c) < 24 || c.Y - h < -40 || c.Y > MapSize.Y + 10)
        {
            return true;
        }

        bool Covers(Vector2 pos, float margin) =>
            Mathf.Abs(pos.X - c.X) < w * 0.3f + margin && pos.Y - c.Y > -h * 0.55f && pos.Y - c.Y < 28;

        if ((avoidLandmarks ? _landmarks : []).Any(p => Covers(p, 16)) || T.Cities.Any(p => Covers(p.At, p.Kind == "big" ? 46 : 30))
            || T.Sites.Any(p => Covers(p.At, 34)) || T.Passes.Any(p => Covers(p.At, 14)))
        {
            return true;
        }

        foreach (var river in T.Rivers)
        {
            var reach = 22 + river.To;
            if (WorldTerrain.EdgeDistance(river.Path, c) < reach || WorldTerrain.EdgeDistance(river.Path, c + side) < reach * 0.6f
                || WorldTerrain.EdgeDistance(river.Path, c - side) < reach * 0.6f)
            {
                return true;
            }
        }

        return WorldTerrain.EdgeDistance(T.Canal, c) < 20 || T.Lakes.Any(l => ((c - l.Center) / (l.Radius + new Vector2(w * 0.5f, 40))).Length() < 1);
    }

    private static void Quad(Rid item, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd) =>
        RenderingServer.CanvasItemAddTriangleArray(item, [0, 1, 2, 0, 2, 3], [a, b, c, d], [ca, cb, cc, cd]);

    private const int SkylineSteps = 56;

    /// <summary>天际线：三到七座高低不一的峰叠成，每座峰左右坡宽窄不同，峰顶圆肩、山脚内收，山脊上有大小两级岩齿。</summary>
    private static (Vector2[] Top, float[] Profile) Skyline(Massif m)
    {
        const int n = SkylineSteps;
        var (c, w, h) = (m.Base, m.Width, m.Height);

        var peaks = 3 + (int)(Brushwork.Hash(m.Seed + 4) * 5);
        var bumps = new (float At, float Tall, float Left, float Right)[peaks];
        for (var k = 0; k < peaks; k++)
        {
            var at = k == 0 ? 0.25f + 0.5f * Brushwork.Hash(m.Seed + 5) : 0.06f + 0.88f * Brushwork.Hash(m.Seed + k * 3.3f);
            var tall = k == 0 ? 1f : 0.3f + 0.6f * Brushwork.Hash(m.Seed + k * 5.1f);
            var half = k == 0 ? 0.34f + 0.14f * Brushwork.Hash(m.Seed + 6) : 0.1f + 0.2f * Brushwork.Hash(m.Seed + k * 7.7f);
            // 不对称：一侧陡、一侧缓。
            var skew = 0.6f + 0.8f * Brushwork.Hash(m.Seed + k * 9.3f);
            bumps[k] = (at, tall, half * skew, half * (2 - skew));
        }

        var top = new Vector2[n + 1];
        var profile = new float[n + 1];
        for (var i = 0; i <= n; i++)
        {
            var t = (float)i / n;
            var y = 0f;
            foreach (var (at, tall, left, right) in bumps)
            {
                var k = Mathf.Max(0, 1 - Mathf.Abs(t - at) / (t < at ? left : right));
                y = Mathf.Max(y, tall * (0.55f * Mathf.Pow(k, 1.7f) + 0.45f * k * k * (3 - 2 * k)));
            }

            // 两级岩齿：大的是山脊上的起伏，小的是崖石。
            y += (Brushwork.Noise(t * 11 + m.Seed * 2) - 0.5f) * 0.1f * y
                 + (Brushwork.Noise(t * 31 + m.Seed) - 0.5f) * 0.05f * y
                 + (Brushwork.Noise(t * 6 + m.Seed * 3) - 0.5f) * 0.05f;
            y = Mathf.Max(0, y) * Mathf.Min(1, Mathf.Sin(t * Mathf.Pi) * 4);
            profile[i] = y;
            top[i] = new Vector2(c.X + (t - 0.5f) * w, c.Y - y * h);
        }

        return (top, profile);
    }

    /// <summary>
    /// 一座山体（平视）：按天际线每段的坡向分面，朝西的上坡受光、朝东的下坡背光（赛璐璐两阶），
    /// 上段石青、下段石绿（干旱山脉为赭灰岩色），山脚淡出到地面；受光面切沟壑暗楔，背光面留亮楔，峰顶补脊线，
    /// 积雪山脉的高峰戴雪，最后沿天际线勾线。前排山梁（Layer ≥ 1）更绿、更亮、线更细。
    /// </summary>
    private static void DrawMassif(Rid item, Massif m)
    {
        const int n = SkylineSteps;
        var (c, w, h) = (m.Base, m.Width, m.Height);
        var (top, profile) = Skyline(m);
        var front = m.Layer > 0;

        // 前排山梁偏绿偏亮，远处主山偏石青。
        var green = front ? 0.35f : 0;
        var litTop = new Color(0.55f, 0.74f, 0.8f).Lerp(new Color(0.55f, 0.78f, 0.62f), green);
        var litLow = new Color(0.52f, 0.75f, 0.6f).Lerp(new Color(0.58f, 0.79f, 0.58f), green);
        var shadeTop = new Color(0.33f, 0.5f, 0.62f).Lerp(new Color(0.34f, 0.56f, 0.5f), green);
        var shadeLow = new Color(0.32f, 0.54f, 0.48f).Lerp(new Color(0.36f, 0.58f, 0.46f), green);
        var foot = new Color(0.56f, 0.75f, 0.54f);
        if (m.Arid)
        {
            // 干旱山脉：赭灰岩色，阴面偏青灰，山脚融进沙地。
            litTop = new Color(0.78f, 0.72f, 0.62f).Lerp(new Color(0.8f, 0.74f, 0.6f), green);
            litLow = new Color(0.76f, 0.68f, 0.54f);
            shadeTop = new Color(0.5f, 0.52f, 0.56f);
            shadeLow = new Color(0.56f, 0.52f, 0.46f);
            foot = new Color(0.82f, 0.76f, 0.6f);
        }

        var midY = c.Y - h * 0.28f;
        var lit = new bool[n];

        for (var i = 0; i < n; i++)
        {
            var slope = profile[Mathf.Min(n, i + 2)] - profile[Mathf.Max(0, i - 1)];
            lit[i] = slope > 0.003f || (Mathf.Abs(slope) <= 0.003f && profile[i] > 0.85f);
            var (hi, lo) = lit[i] ? (litTop, litLow) : (shadeTop, shadeLow);
            var a = top[i];
            var b = top[i + 1];
            var ma = new Vector2(a.X, Mathf.Max(a.Y, midY));
            var mb = new Vector2(b.X, Mathf.Max(b.Y, midY));
            var ca = lo.Lerp(hi, Mathf.Clamp((c.Y - a.Y) / h, 0, 1) * 0.6f + 0.4f);
            var cb = lo.Lerp(hi, Mathf.Clamp((c.Y - b.Y) / h, 0, 1) * 0.6f + 0.4f);
            var faded = lo.Lerp(foot, 0.75f) with { A = 0.15f };
            Quad(item, a, b, mb, ma, ca, cb, lo, lo);
            Quad(item, ma, mb, new Vector2(b.X, c.Y), new Vector2(a.X, c.Y), lo, lo, faded, faded);
        }

        // 沟壑与脊面：自天际线斜下的暗楔（受光面）与亮楔（背光面），各带一笔皴线。
        var folds = (front ? 2 : 4) + (int)(Brushwork.Hash(m.Seed + 11) * 5);
        for (var f = 0; f < folds; f++)
        {
            var i = 2 + (int)(Brushwork.Hash(m.Seed + f * 4.3f) * (n - 4));
            if (profile[i] < 0.2f)
            {
                continue;
            }

            var from = top[i];
            var drop = (c.Y - from.Y) * (0.35f + 0.45f * Brushwork.Hash(m.Seed + f * 2.9f));
            var lean = w * (0.02f + 0.04f * Brushwork.Hash(m.Seed + f * 6.1f)) * (lit[i] ? 1 : -1);
            var tip = from + new Vector2(lean, drop);
            var side = from + new Vector2(lean * 2.2f + w * 0.012f * Mathf.Sign(lean), drop * 0.35f);
            var tone = lit[i] ? shadeTop.Lerp(shadeLow, 0.4f) with { A = 0.7f } : litTop.Lerp(litLow, 0.5f) with { A = 0.5f };
            RenderingServer.CanvasItemAddTriangleArray(item, [0, 1, 2], [from + new Vector2(0, 2), side, tip], [tone, tone, tone with { A = 0 }]);
            Span<Vector2> crease = [from, from.Lerp(tip, 0.5f) + new Vector2(-lean * 0.2f, 0), tip];
            Brushwork.Stroke(item, crease, 1.2f, MountainLine with { A = 0.32f }, m.Seed + f, 0.3f, 0.05f, 0.7f);
        }

        // 积雪：积雪山脉的高峰顶端一片，下缘参差。
        if (m.Snow && !front && h > 70)
        {
            for (var i = 1; i < n; i++)
            {
                if (profile[i] < 0.88f || profile[i] < profile[i - 1] || profile[i] < profile[i + 1])
                {
                    continue;
                }

                var depth = h * (h > 140 ? 0.16f : 0.12f);
                var l = Mathf.Max(0, i - 3);
                var rt = Mathf.Min(n, i + 3);
                var cap = new List<Vector2>();
                for (var j = l; j <= rt; j++)
                {
                    cap.Add(top[j]);
                }

                cap.Add(top[rt] + new Vector2(0, depth * 0.3f));
                cap.Add(top[i] + new Vector2(w * 0.02f, depth));
                cap.Add(top[i] + new Vector2(0, depth * 0.55f));
                cap.Add(top[i] + new Vector2(-w * 0.018f, depth * 0.8f));
                cap.Add(top[l] + new Vector2(0, depth * 0.3f));
                var fan = new int[(cap.Count - 2) * 3];
                for (var j = 0; j < cap.Count - 2; j++)
                {
                    (fan[j * 3], fan[j * 3 + 1], fan[j * 3 + 2]) = (0, j + 1, j + 2);
                }

                var snow = new Color(0.94f, 0.97f, 0.98f);
                RenderingServer.CanvasItemAddTriangleArray(item, fan, cap.ToArray(), Enumerable.Repeat(snow, cap.Count).ToArray());
            }
        }

        // 主脊线：自各峰顶斜向下一笔，分开亮面与暗面。
        for (var i = 1; i < n; i++)
        {
            if (profile[i] > profile[i - 1] && profile[i] >= profile[i + 1] && profile[i] > 0.3f)
            {
                var from = top[i];
                var to = new Vector2(from.X + w * 0.035f, from.Y + (c.Y - from.Y) * 0.6f);
                Span<Vector2> ridge = [from, from.Lerp(to, 0.5f) + new Vector2(-w * 0.012f, 0), to];
                Brushwork.Stroke(item, ridge, 1.5f, MountainLine with { A = 0.45f }, m.Seed + i, 0.3f, 0.05f, 0.6f);
            }
        }

        Brushwork.Stroke(item, top, front ? 1.7f : 2.1f, MountainLine, m.Seed + 1, 0.25f, 0.1f, 0.1f);
    }

    // ── 村落与毡帐 ───────────────────────────────────────

    /// <summary>村落样式：南方粉墙黛瓦、北方土墙灰瓦。</summary>
    private enum HouseStyle
    {
        South,
        North,
    }

    /// <summary>
    /// 村落：每 92 像素一个候选点（带抖动），按地貌定出现几率——水田、麦田最密，平原次之，黄土、密林、沼泽稀疏，
    /// 草原、大漠、戈壁、高原、雪域没有；避开海、江河、湖、山体、城池、地点与地标。秦岭—淮河以南为南方样式。
    /// </summary>
    private static IEnumerable<(Vector2 At, float Seed, HouseStyle Style)> PlanVillages(List<Rect2> blocked, IReadOnlyList<Vector2> landmarks)
    {
        const float step = 92;
        var southOf = T.Project(110, 33.2f).Y;
        var westOf = T.Project(100, 30).X;
        for (var y = step * 0.5f; y < MapSize.Y; y += step)
        {
            for (var x = step * 0.5f; x < MapSize.X; x += step)
            {
                var seed = x * 0.37f + y * 1.13f;
                var p = new Vector2(x + (Brushwork.Hash(seed) - 0.5f) * step * 0.8f, y + (Brushwork.Hash(seed + 1) - 0.5f) * step * 0.8f);
                var chance = InBiome(p, "paddy", "wheat") ? 0.5f
                    : InBiome(p, "desert", "gobi", "grass", "plateau", "snow") ? 0
                    : InBiome(p, "loess") ? 0.2f
                    : InBiome(p, "jungle", "marsh", "forest") ? 0.07f
                    : p.X < westOf ? 0.02f : 0.24f;
                if (Brushwork.Hash(seed + 2) >= chance || T.CoastDistance(p) < 30 || T.LakeFactor(p) < 1.5f)
                {
                    continue;
                }

                if (blocked.Any(r => r.Grow(10).HasPoint(p)) || T.Rivers.Any(r => WorldTerrain.EdgeDistance(r.Path, p) < 14 + r.To)
                    || WorldTerrain.EdgeDistance(T.Canal, p) < 12 || WorldTerrain.EdgeDistance(T.GreatWall, p) < 16
                    || T.Cities.Any(c => c.At.DistanceTo(p) < 70) || T.Sites.Any(s => s.At.DistanceTo(p) < 60) || landmarks.Any(l => l.DistanceTo(p) < 50))
                {
                    continue;
                }

                yield return (p, seed, p.Y > southOf ? HouseStyle.South : HouseStyle.North);
            }
        }
    }

    /// <summary>一座村落：三到六间小屋错落，脚下一抹暗影。</summary>
    private static void DrawVillage(Rid item, Vector2 at, float seed, HouseStyle style)
    {
        var count = 3 + (int)(Brushwork.Hash(seed + 5) * 4);
        Brushwork.Blot(item, at + new Vector2(0, -2), 22, new Color(0.2f, 0.3f, 0.2f, 0.12f), seed);
        var houses = new List<Vector2>();
        for (var i = 0; i < count; i++)
        {
            var a = Brushwork.Hash(seed + i * 3.1f) * Mathf.Tau;
            var r = 4 + 12 * Brushwork.Hash(seed + i * 4.7f);
            houses.Add(at + new Vector2(Mathf.Cos(a) * r * 1.4f, Mathf.Sin(a) * r * 0.7f));
        }

        foreach (var h in houses.OrderBy(h => h.Y))
        {
            DrawHouse(item, h, 0.8f + 0.4f * Brushwork.Hash(h.X), style);
        }
    }

    /// <summary>一间小屋（平视）：墙身、两坡屋顶与屋脊，细墨线勾边。</summary>
    private static void DrawHouse(Rid item, Vector2 b, float s, HouseStyle style)
    {
        var (wall, roof) = style == HouseStyle.South
            ? (new Color(0.95f, 0.94f, 0.89f), new Color(0.3f, 0.34f, 0.4f))
            : (new Color(0.86f, 0.77f, 0.62f), new Color(0.44f, 0.41f, 0.4f));
        var w = 11 * s;
        var hw = 5.5f * s;
        var body = new[] { b + new Vector2(-w / 2, 0), b + new Vector2(w / 2, 0), b + new Vector2(w / 2, -hw), b + new Vector2(-w / 2, -hw) };
        Polygon(item, body, wall);
        var eave = w * 0.62f;
        var roofPoly = new[]
        {
            b + new Vector2(-eave, -hw + 1), b + new Vector2(eave, -hw + 1), b + new Vector2(w * 0.36f, -hw - 5.5f * s), b + new Vector2(-w * 0.36f, -hw - 5.5f * s),
        };
        Polygon(item, roofPoly, roof);
        Outline(item, body, Ink with { A = 0.55f }, 0.8f);
        Outline(item, roofPoly, Ink with { A = 0.7f }, 0.9f);
    }

    /// <summary>毡帐：草原上三两成群，偶尔一小群牛羊白点。</summary>
    private static IEnumerable<(Vector2 At, float Seed)> PlanYurts(List<Rect2> blocked)
    {
        const float step = 150;
        for (var y = step * 0.5f; y < MapSize.Y; y += step)
        {
            for (var x = step * 0.5f; x < MapSize.X; x += step)
            {
                var seed = x * 0.71f + y * 0.29f + 7;
                var p = new Vector2(x + (Brushwork.Hash(seed) - 0.5f) * step * 0.7f, y + (Brushwork.Hash(seed + 1) - 0.5f) * step * 0.7f);
                if (Brushwork.Hash(seed + 2) < 0.4f && InBiome(p, "grass") && T.OnLand(p) && T.LakeFactor(p) > 1.6f
                    && !blocked.Any(r => r.Grow(10).HasPoint(p)) && T.Rivers.All(r => WorldTerrain.EdgeDistance(r.Path, p) > 16 + r.To))
                {
                    yield return (p, seed);
                }
            }
        }
    }

    private static void DrawYurts(Rid item, Vector2 at, float seed)
    {
        var count = 2 + (int)(Brushwork.Hash(seed + 3) * 3);
        for (var i = 0; i < count; i++)
        {
            var p = at + new Vector2((Brushwork.Hash(seed + i * 2.3f) - 0.5f) * 34, (Brushwork.Hash(seed + i * 5.9f) - 0.5f) * 14);
            var s = 0.8f + 0.4f * Brushwork.Hash(seed + i);
            var dome = new List<Vector2> { p + new Vector2(-7 * s, 0) };
            for (var k = 0; k <= 8; k++)
            {
                var a = Mathf.Pi + Mathf.Pi * k / 8;
                dome.Add(p + new Vector2(Mathf.Cos(a) * 7 * s, -4 * s + Mathf.Sin(a) * 5 * s));
            }

            dome.Add(p + new Vector2(7 * s, 0));
            Brushwork.Blot(item, p + new Vector2(2, -1), 9 * s, new Color(0.2f, 0.3f, 0.2f, 0.14f), seed + i);
            Polygon(item, [.. dome], new Color(0.96f, 0.94f, 0.88f));
            Polygon(item, [p + new Vector2(-2.4f * s, -8.6f * s), p + new Vector2(2.4f * s, -8.6f * s), p + new Vector2(0, -9.6f * s)], new Color(0.5f, 0.32f, 0.22f));
            Polygon(item, [p + new Vector2(-1.6f * s, 0), p + new Vector2(1.6f * s, 0), p + new Vector2(1.6f * s, -3.4f * s), p + new Vector2(-1.6f * s, -3.4f * s)], new Color(0.62f, 0.3f, 0.22f));
            Outline(item, [.. dome], Ink with { A = 0.6f }, 0.9f);
        }

        if (Brushwork.Hash(seed + 11) < 0.6f)
        {
            for (var k = 0; k < 7; k++)
            {
                var p = at + new Vector2(24 + Brushwork.Hash(seed + k * 1.9f) * 26, (Brushwork.Hash(seed + k * 3.3f) - 0.5f) * 16);
                Brushwork.Dot(item, p, 1.6f, new Color(0.97f, 0.96f, 0.92f));
            }
        }
    }

    // ── 城池、关隘与地点 ─────────────────────────────────

    /// <summary>城池（略带俯角的平视）：城内地面与屋顶、后墙一线，正面城墙带垛口与角楼，正中城门楼；大城更宽、屋顶更多。</summary>
    private static void DrawCity(Rid item, WorldTerrain.Place c)
    {
        var big = c.Kind == "big";
        var w = big ? 86f : 52f;
        var depth = big ? 30f : 18f;
        var b = c.At;
        var stone = new Color(0.82f, 0.77f, 0.68f);
        var stoneDark = new Color(0.62f, 0.58f, 0.52f);
        Brushwork.Blot(item, b + new Vector2(4, -depth * 0.4f), w * 0.62f, new Color(0.15f, 0.2f, 0.18f, 0.18f), b.X);

        // 城内：地面与后墙。
        var inner = new[] { b + new Vector2(-w / 2, -6), b + new Vector2(w / 2, -6), b + new Vector2(w / 2 - 6, -depth), b + new Vector2(-w / 2 + 6, -depth) };
        Polygon(item, inner, new Color(0.84f, 0.8f, 0.68f));
        Line(item, inner[3] + new Vector2(0, -3), inner[2] + new Vector2(0, -3), stoneDark, 3);

        // 屋顶：几排错落的黛瓦。
        var rows = big ? 3 : 2;
        for (var r = 0; r < rows; r++)
        {
            var y = -depth + 7 + r * (depth - 12) / Mathf.Max(1, rows - 1) * 0.9f;
            var span = w - 18 - r * 2;
            var n = big ? 5 : 3;
            for (var k = 0; k < n; k++)
            {
                var seed = b.X + r * 7 + k * 3;
                var x = -span / 2 + span * (k + 0.5f) / n + (Brushwork.Hash(seed) - 0.5f) * 4;
                DrawHouse(item, b + new Vector2(x, y + 4), 0.75f + 0.3f * Brushwork.Hash(seed + 1), HouseStyle.South);
            }
        }

        // 正面城墙与垛口。
        var wallTop = -12f;
        var front = new[] { b + new Vector2(-w / 2, 0), b + new Vector2(w / 2, 0), b + new Vector2(w / 2, wallTop), b + new Vector2(-w / 2, wallTop) };
        Polygon(item, front, stone);
        for (var x = -w / 2 + 2; x < w / 2 - 2; x += 5)
        {
            Polygon(item, Corners(new Rect2(b + new Vector2(x, wallTop - 3), new Vector2(3, 3))), stone);
        }

        Outline(item, front, Ink, 1.2f);
        // 角楼与城门楼。
        foreach (var x in new[] { -w / 2, w / 2 })
        {
            var tower = new Rect2(b + new Vector2(x - 5, wallTop - 6), new Vector2(10, 18));
            Polygon(item, Corners(tower), stone);
            Outline(item, Corners(tower), Ink, 1.1f);
        }

        DrawGate(item, b, big ? 1.25f : 1);
    }

    /// <summary>城门楼 / 关楼：城台带门洞，上覆重檐屋顶。</summary>
    private static void DrawGate(Rid item, Vector2 b, float s)
    {
        var stone = new Color(0.84f, 0.79f, 0.7f);
        var platform = new Rect2(b + new Vector2(-11 * s, -15 * s), new Vector2(22 * s, 15 * s));
        Polygon(item, Corners(platform), stone);
        var arch = new List<Vector2> { b + new Vector2(-3.5f * s, 0) };
        for (var k = 0; k <= 6; k++)
        {
            var a = Mathf.Pi + Mathf.Pi * k / 6;
            arch.Add(b + new Vector2(Mathf.Cos(a) * 3.5f * s, -6 * s + Mathf.Sin(a) * 3.5f * s));
        }

        arch.Add(b + new Vector2(3.5f * s, 0));
        Polygon(item, [.. arch], new Color(0.25f, 0.22f, 0.2f));
        Outline(item, Corners(platform), Ink, 1.2f);
        // 门楼：红柱墙身与两重屋檐。
        var hall = new Rect2(b + new Vector2(-8 * s, -21 * s), new Vector2(16 * s, 6 * s));
        Polygon(item, Corners(hall), new Color(0.7f, 0.32f, 0.25f));
        Roof(item, b + new Vector2(0, -21 * s), 22 * s, 5 * s, new Color(0.28f, 0.32f, 0.38f));
        Roof(item, b + new Vector2(0, -26.5f * s), 15 * s, 4.5f * s, new Color(0.28f, 0.32f, 0.38f));
    }

    /// <summary>一层屋檐：底宽 width 的梯形，檐角微翘。</summary>
    private static void Roof(Rid item, Vector2 b, float width, float height, Color tone)
    {
        var poly = new[]
        {
            b + new Vector2(-width / 2 - 2, 1), b + new Vector2(-width / 2, 0), b + new Vector2(width / 2, 0), b + new Vector2(width / 2 + 2, 1),
            b + new Vector2(width * 0.3f, -height), b + new Vector2(-width * 0.3f, -height),
        };
        Polygon(item, poly, tone);
        Outline(item, poly, Ink, 1);
    }

    /// <summary>地点小景：按种类画寺塔、道观、山门、营地、驿站、堡寨、古墓、岛上亭台、船埠或庄院。</summary>
    private static void DrawSite(Rid item, WorldTerrain.Place s)
    {
        var b = s.At;
        var redWall = new Color(0.72f, 0.34f, 0.27f);
        var roof = new Color(0.28f, 0.32f, 0.38f);
        var goldRoof = new Color(0.82f, 0.64f, 0.3f);
        Brushwork.Blot(item, b + new Vector2(3, -6), 30, new Color(0.15f, 0.2f, 0.18f, 0.16f), b.X);
        switch (s.Kind)
        {
            case "temple":
                // 寺：大殿加一座五层塔。
                Hall(item, b + new Vector2(-8, 0), 26, 9, redWall, goldRoof);
                for (var k = 0; k < 5; k++)
                {
                    var w = 11 - k * 1.4f;
                    var y = -k * 7f;
                    var body = new Rect2(b + new Vector2(14 - w * 0.35f, y - 5), new Vector2(w * 0.7f, 5));
                    Polygon(item, Corners(body), new Color(0.9f, 0.86f, 0.76f));
                    Outline(item, Corners(body), Ink with { A = 0.7f }, 0.8f);
                    Roof(item, b + new Vector2(14, y - 5), w + 3, 2.6f, roof);
                }

                Line(item, b + new Vector2(14, -40), b + new Vector2(14, -46), Ink, 1.2f);
                break;
            case "taoist":
                // 道观：台基上一座红墙大殿与前面一座小殿。
                Polygon(item, Corners(new Rect2(b + new Vector2(-20, -4), new Vector2(40, 4))), new Color(0.84f, 0.8f, 0.7f));
                Hall(item, b + new Vector2(0, -4), 30, 10, redWall, roof);
                Hall(item, b + new Vector2(-14, 4), 14, 6, redWall, roof);
                break;
            case "sect":
                // 山门：牌坊加后面一座殿。
                Hall(item, b + new Vector2(6, -6), 24, 9, redWall, roof);
                foreach (var x in new[] { -16f, -4f })
                {
                    Line(item, b + new Vector2(x, 2), b + new Vector2(x, -14), new Color(0.6f, 0.26f, 0.2f), 2.4f);
                }

                Roof(item, b + new Vector2(-10, -14), 20, 4, roof);
                break;
            case "camp":
                // 营地：一圈木栅与几顶帐篷，一面旗。
                for (var k = 0; k < 3; k++)
                {
                    var p = b + new Vector2(-14 + k * 13, -2 - (k % 2) * 4);
                    Polygon(item, [p + new Vector2(-7, 0), p + new Vector2(7, 0), p + new Vector2(0, -10)], new Color(0.9f, 0.86f, 0.74f));
                    Outline(item, [p + new Vector2(-7, 0), p + new Vector2(7, 0), p + new Vector2(0, -10)], Ink with { A = 0.7f }, 0.9f);
                }

                for (var x = -22f; x <= 22; x += 4)
                {
                    Line(item, b + new Vector2(x, 4), b + new Vector2(x, -1), new Color(0.45f, 0.32f, 0.22f), 1.4f);
                }

                Flag(item, b + new Vector2(18, -4));
                break;
            case "relay":
                // 驿站：小院一座、院墙与一根挑着旗的杆。
                DrawHouse(item, b + new Vector2(-6, -2), 1.5f, HouseStyle.North);
                DrawHouse(item, b + new Vector2(8, 2), 1.2f, HouseStyle.North);
                Line(item, b + new Vector2(-20, 5), b + new Vector2(20, 5), new Color(0.6f, 0.52f, 0.42f), 2);
                Flag(item, b + new Vector2(18, 0));
                break;
            case "fort":
                // 堡寨：一圈矮墙、一座望楼。
                var wall = new Rect2(b + new Vector2(-20, -8), new Vector2(40, 8));
                Polygon(item, Corners(wall), new Color(0.7f, 0.64f, 0.56f));
                Outline(item, Corners(wall), Ink, 1.1f);
                DrawGate(item, b + new Vector2(0, -6), 0.9f);
                Flag(item, b + new Vector2(16, -8));
                break;
            case "tomb":
                // 古墓：一座圆丘、石碑与两侧松柏。
                var mound = new List<Vector2>();
                for (var k = 0; k <= 12; k++)
                {
                    var a = Mathf.Pi + Mathf.Pi * k / 12;
                    mound.Add(b + new Vector2(Mathf.Cos(a) * 20, Mathf.Sin(a) * 12));
                }

                Polygon(item, [.. mound], new Color(0.5f, 0.66f, 0.46f));
                Outline(item, [.. mound], Ink with { A = 0.7f }, 1);
                Polygon(item, Corners(new Rect2(b + new Vector2(-3, -8), new Vector2(6, 9))), new Color(0.78f, 0.78f, 0.74f));
                Tree(item, b + new Vector2(-24, 2), 1);
                Tree(item, b + new Vector2(24, 2), 1);
                break;
            case "island":
                // 岛上亭台与几株树。
                Tree(item, b + new Vector2(-12, 2), 1.1f);
                Tree(item, b + new Vector2(13, 3), 0.9f);
                Hall(item, b, 16, 7, redWall, roof);
                break;
            case "port":
                // 船埠：伸进水里的栈桥、一间货栈与泊着的船。
                Line(item, b + new Vector2(0, 0), b + new Vector2(26, 12), new Color(0.5f, 0.38f, 0.26f), 3);
                DrawHouse(item, b + new Vector2(-8, -2), 1.5f, HouseStyle.South);
                DrawBoat(item, b + new Vector2(30, 18), 1, 1.1f, (int)b.X);
                break;
            case "manor":
                // 庄院：水边一组粉墙黛瓦，院前一方池塘。
                Brushwork.Blot(item, b + new Vector2(14, 4), 12, new Color(0.46f, 0.74f, 0.8f, 0.95f), b.Y);
                DrawHouse(item, b + new Vector2(-10, -4), 1.6f, HouseStyle.South);
                DrawHouse(item, b + new Vector2(4, -6), 1.4f, HouseStyle.South);
                DrawHouse(item, b + new Vector2(-2, 2), 1.5f, HouseStyle.South);
                break;
        }
    }

    /// <summary>一座殿：墙身加一层屋顶。</summary>
    private static void Hall(Rid item, Vector2 b, float width, float height, Color wall, Color roof)
    {
        var body = new Rect2(b + new Vector2(-width / 2, -height), new Vector2(width, height));
        Polygon(item, Corners(body), wall);
        Outline(item, Corners(body), Ink with { A = 0.7f }, 0.9f);
        Roof(item, b + new Vector2(0, -height), width + 6, height * 0.6f, roof);
    }

    private static void Flag(Rid item, Vector2 b)
    {
        Line(item, b, b + new Vector2(0, -20), new Color(0.4f, 0.3f, 0.22f), 1.4f);
        Polygon(item, [b + new Vector2(0, -20), b + new Vector2(9, -17), b + new Vector2(0, -13)], new Color(0.78f, 0.24f, 0.18f));
    }

    private static void Tree(Rid item, Vector2 b, float s)
    {
        Line(item, b, b + new Vector2(0, -4 * s), new Color(0.4f, 0.3f, 0.22f), 1.4f);
        Brushwork.Dot(item, b + new Vector2(0, -8 * s), 5.5f * s, new Color(0.36f, 0.56f, 0.4f));
        Brushwork.Dot(item, b + new Vector2(-1.5f * s, -9.5f * s), 3 * s, new Color(0.5f, 0.7f, 0.48f));
    }

    // ── 路 ───────────────────────────────────────────────

    /// <summary>陆路为暗边浅色短划的土路，水路为白色点线。</summary>
    private void DrawRoads(Rid item)
    {
        var casing = new Color(0.18f, 0.14f, 0.1f, 0.6f);
        var dirt = new Color(0.93f, 0.86f, 0.68f, 0.95f);
        foreach (var road in _roads)
        {
            var path = road.Path;
            if (road.Water)
            {
                foreach (var (p, _) in Walk(path, 16))
                {
                    Brushwork.Dot(item, p, 2.4f, Colors.White with { A = 0.85f });
                }

                continue;
            }

            foreach (var (p, dir) in Walk(path, 18))
            {
                Span<Vector2> dash = [p - dir * 6f, p + dir * 6f];
                Brushwork.Stroke(item, dash, 5f, casing, p.X * 0.1f, 0, 0, 0);
            }

            foreach (var (p, dir) in Walk(path, 18))
            {
                Span<Vector2> dash = [p - dir * 5f, p + dir * 5f];
                Brushwork.Stroke(item, dash, 2.6f, dirt, p.X * 0.1f, 0, 0, 0);
            }
        }
    }

    /// <summary>一段路的折线：起点、途经点、终点。</summary>
    public static Vector2[] RoutePath(MapEdge edge) =>
        [WorldMapSamples.Node(edge.From).Pos, .. edge.Via, WorldMapSamples.Node(edge.To).Pos];

    /// <summary>沿折线每隔 step 取一点及其行进方向。</summary>
    public static IEnumerable<(Vector2 Point, Vector2 Dir)> Walk(Vector2[] path, float step)
    {
        var carry = step * 0.5f;
        for (var i = 1; i < path.Length; i++)
        {
            var a = path[i - 1];
            var b = path[i];
            var len = a.DistanceTo(b);
            var dir = (b - a) / Mathf.Max(len, 0.001f);
            var d = carry;
            for (; d < len; d += step)
            {
                yield return (a + dir * d, dir);
            }

            carry = d - len;
        }
    }

    private static float PathLength(Vector2[] path)
    {
        var total = 0f;
        for (var i = 1; i < path.Length; i++)
        {
            total += path[i - 1].DistanceTo(path[i]);
        }

        return total;
    }

    /// <summary>折线上按总长比例 t（0–1）取点。</summary>
    public static Vector2 AlongPath(Vector2[] path, float t) => AlongPathDir(path, t).Point;

    private static (Vector2 Point, Vector2 Dir) AlongPathDir(Vector2[] path, float t)
    {
        var goal = Mathf.Clamp(t, 0, 1) * PathLength(path);
        for (var i = 1; i < path.Length; i++)
        {
            var len = path[i - 1].DistanceTo(path[i]);
            if (goal <= len)
            {
                return (path[i - 1].Lerp(path[i], goal / Mathf.Max(len, 0.001f)), (path[i] - path[i - 1]) / Mathf.Max(len, 0.001f));
            }

            goal -= len;
        }

        return (path[^1], Vector2.Right);
    }

    // ── 地名 ─────────────────────────────────────────────

    /// <summary>
    /// 地名：地域题字竖排大字（浅色字压暗影）；地点名横排、深色描边，写在小景下方；剧情点名的江河与关隘也标名。
    /// 只标 STORY 点名的地方（地形数据里有 label 的），其余城池、山川不标。
    /// </summary>
    private static void DrawLabels(Rid item)
    {
        foreach (var t in T.Titles)
        {
            var size = (int)(t.Size * 1.3f);
            var y = t.At.Y - size * 0.55f * t.Text.Length;
            foreach (var rune in t.Text.EnumerateRunes())
            {
                var glyph = rune.ToString();
                UiFonts.Title.DrawString(item, new Vector2(t.At.X + 3, y + 3), glyph, HorizontalAlignment.Left, -1, size, UiPalette.Abyss with { A = 0.35f });
                UiFonts.Title.DrawString(item, new Vector2(t.At.X, y), glyph, HorizontalAlignment.Left, -1, size, UiPalette.Surface with { A = 0.78f });
                y += size * 1.1f;
            }
        }

        foreach (var place in T.Sites.Concat(T.Passes).Concat(T.Cities).Where(p => p.Label is not null))
        {
            PlaceName(item, place.Label!, place.At + new Vector2(0, place.Kind == "island" ? 34 : 14 * SiteScale), 26);
        }

        foreach (var river in T.Rivers.Where(r => r.Label is not null))
        {
            PlaceName(item, river.Label!, AlongPath(river.Path, 0.45f) + new Vector2(16, 0), 22);
        }
    }

    /// <summary>横排地名：居中写在 at 处，深色描边、浅色字。</summary>
    private static void PlaceName(Rid item, string text, Vector2 at, int size)
    {
        var width = UiFonts.Title.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        var pos = at + new Vector2(-width / 2, size * 0.8f);
        UiFonts.Title.DrawStringOutline(item, pos, text, HorizontalAlignment.Left, -1, size, 6, UiPalette.Abyss with { A = 0.75f });
        UiFonts.Title.DrawString(item, pos, text, HorizontalAlignment.Left, -1, size, UiPalette.Surface);
    }

    // ── 绘制小工具 ───────────────────────────────────────

    /// <summary>地点小景的放大倍数：小景按 1 倍尺寸画，在图上放大到与城池相称。</summary>
    private const float SiteScale = 1.6f;

    /// <summary>以 pivot 为中心放大 s 倍画 draw 里的内容，画完复原变换。</summary>
    private static void Scaled(Rid item, Vector2 pivot, float s, Action draw)
    {
        RenderingServer.CanvasItemAddSetTransform(item, new Transform2D(0, new Vector2(s, s), 0, pivot - pivot * s));
        draw();
        RenderingServer.CanvasItemAddSetTransform(item, Transform2D.Identity);
    }

    private static Vector2[] Corners(Rect2 r) => [r.Position, r.Position + new Vector2(r.Size.X, 0), r.End, r.Position + new Vector2(0, r.Size.Y)];

    private static void Polygon(Rid item, Vector2[] points, Color color) =>
        RenderingServer.CanvasItemAddPolygon(item, points, [color]);

    private static void Outline(Rid item, Vector2[] points, Color color, float width) =>
        RenderingServer.CanvasItemAddPolyline(item, [.. points, points[0]], [color], width, true);

    private static void Line(Rid item, Vector2 a, Vector2 b, Color color, float width) =>
        RenderingServer.CanvasItemAddLine(item, a, b, color, width, true);
}

/// <summary>舆图上画出的一段路：折线（地图坐标）与是否水路（白色点线；否则为土色短划陆路）。</summary>
public sealed record MapRoad(Vector2[] Path, bool Water);
