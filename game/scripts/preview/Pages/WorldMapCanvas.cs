using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 大地图占位：程序化生成的写实地形舆图。先按海岸线、山脉走向、江河与湖泊在 CPU 上算出一张低分辨率高度图，
/// 再由 <c>assets/shaders/world_relief.gdshader</c> 叠加岩石与林地细节、做西北光照的山体晕渲、按海拔与坡度着色
/// （平原田块、林地、高地、裸岩、雪线）并画出由浅到深的海水；江河、道路与地域题字以矢量叠在地形之上。
/// 正式大地图按架构文档 10.3 制作后替换此类；地标与路线图层不受影响。全部形状只取决于固定种子。
/// </summary>
public partial class WorldMapCanvas : Control
{
    /// <summary>高度图每格对应的地图逻辑像素。</summary>
    private const int Cell = 6;

    private static readonly Color RiverTone = new(0.46f, 0.75f, 0.8f);
    private static readonly Color RiverBank = new(0.16f, 0.24f, 0.26f, 0.8f);

    /// <summary>海岸线，自北向南；其东为海。</summary>
    private static readonly Vector2[] Coast = K(
    [
        new(2150, 0), new(2120, 170), new(2190, 330), new(2150, 480), new(2210, 630), new(2160, 760),
        new(2150, 840), new(2190, 930), new(2250, 1060), new(2200, 1210), new(2270, 1360), new(2250, 1500), new(2290, 1600),
    ]);

    private static readonly Vector2[] GreatRiver = K(
    [
        new(140, 760), new(380, 820), new(620, 900), new(820, 872), new(1000, 860), new(1180, 880), new(1400, 900),
        new(1620, 870), new(1700, 900), new(1840, 880), new(2010, 850), new(2170, 842),
    ]);

    private static readonly Vector2[] NorthRiver = K(
    [
        new(240, 300), new(500, 360), new(760, 330), new(980, 420), new(1200, 440), new(1450, 470), new(1700, 430),
        new(1950, 480), new(2170, 500),
    ]);

    private static readonly Vector2[][] Tributaries = ((Vector2[][])
    [
        [new(1060, 610), new(1030, 700), new(1012, 790), new(1000, 860)],
        [new(300, 1180), new(430, 1050), new(520, 940), new(620, 900)],
        [new(1560, 1340), new(1640, 1250), new(1700, 1180)],
    ]).Select(K).ToArray();

    private static readonly Vector2 LakeCenter = new Vector2(1740, 1185) * WorldMapSamples.Scale;
    private static readonly Vector2 LakeRadius = new Vector2(80, 44) * WorldMapSamples.Scale;

    /// <summary>山脉：沿折线隆起，(路径, 峰高 0–1, 山体半宽)。</summary>
    private static readonly (Vector2[] Path, float Peak, float Width)[] Ranges = ScaleRanges(
    [
        ([new(240, 130), new(520, 110), new(760, 150)], 0.7f, 120),
        ([new(150, 460), new(290, 640), new(250, 900), new(320, 1180), new(190, 1400)], 1.0f, 210),
        ([new(880, 250), new(1200, 220), new(1560, 250), new(1880, 210)], 0.85f, 150),
        ([new(960, 580), new(1160, 600)], 0.62f, 95),
        ([new(760, 760), new(980, 740)], 0.72f, 100),
        ([new(1240, 640), new(1420, 740)], 0.5f, 90),
        ([new(820, 1260), new(1260, 1340), new(1620, 1380), new(1960, 1320)], 0.48f, 130),
        ([new(120, 1520), new(700, 1480), new(1300, 1540), new(2000, 1510)], 0.55f, 120),
        // 放大后新增的山地：中原西北的丘陵、江南南缘的矮山、巴蜀东侧的山梁。
        ([new(560, 560), new(700, 640)], 0.55f, 80),
        ([new(1880, 1330), new(2080, 1400)], 0.4f, 70),
        ([new(560, 1050), new(640, 1250)], 0.6f, 80),
        // 江南平原上零散的低丘，免得平原一片空。
        ([new(1650, 760), new(1760, 800)], 0.4f, 55),
        ([new(1980, 1000), new(2080, 1080)], 0.38f, 55),
        ([new(1700, 600), new(1850, 640)], 0.45f, 60),
        ([new(1890, 690), new(1980, 730)], 0.35f, 50),
        ([new(1440, 1150), new(1560, 1200)], 0.4f, 55),
        ([new(2000, 1190), new(2110, 1250)], 0.35f, 50),
    ]);

    /// <summary>设计稿坐标（2880×1600）换算为地图坐标。</summary>
    private static Vector2[] K(Vector2[] points) => points.Select(p => p * WorldMapSamples.Scale).ToArray();

    private static (Vector2[] Path, float Peak, float Width)[] ScaleRanges((Vector2[] Path, float Peak, float Width)[] ranges) =>
        ranges.Select(r => (K(r.Path), r.Peak, r.Width * WorldMapSamples.Scale)).ToArray();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = WorldMapSamples.Size;

        var shader = GD.Load<Shader>("res://assets/shaders/world_relief.gdshader");
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("height_map", BuildHeightMap());
        material.SetShaderParameter("map_size", WorldMapSamples.Size);
        var terrain = new ColorRect
        {
            Material = material, Size = WorldMapSamples.Size, MouseFilter = MouseFilterEnum.Ignore, ShowBehindParent = true,
        };
        AddChild(terrain);
    }

    public override void _Draw()
    {
        var item = GetCanvasItem();
        DrawRiver(item, GreatRiver, 4, 15, 11);
        DrawRiver(item, NorthRiver, 3, 11, 13);
        for (var i = 0; i < Tributaries.Length; i++)
        {
            DrawRiver(item, Tributaries[i], 1.5f, 5, 20 + i);
        }

        DrawMountains(item);
        DrawRoads(item);
        DrawTitles();
    }

    // ── 高度图 ───────────────────────────────────────────

    /// <summary>
    /// 高度：海面为 0，海底为负（离岸越远越深）；陆地由起伏的底面、西高东低的地势、沿山脉的脊状隆起组成，
    /// 江河两岸下切成河谷，湖为盆地。
    /// </summary>
    private static ImageTexture BuildHeightMap()
    {
        var w = (int)WorldMapSamples.Size.X / Cell;
        var h = (int)WorldMapSamples.Size.Y / Cell;
        var data = new byte[w * h * 4];
        var rivers = new List<(Vector2[] Path, float Width, float Valley)>
        {
            (GreatRiver, 14, 180), (NorthRiver, 11, 150),
        };
        rivers.AddRange(Tributaries.Select(t => (t, 5f, 90f)));

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var p = new Vector2((x + 0.5f) * Cell, (y + 0.5f) * Cell);
                BitConverter.TryWriteBytes(data.AsSpan((y * w + x) * 4), Height(p, rivers));
            }
        }

        var image = Image.CreateFromData(w, h, false, Image.Format.Rf, data);
        return ImageTexture.CreateFromImage(image);
    }

    private static float Height(Vector2 p, List<(Vector2[] Path, float Width, float Valley)> rivers)
    {
        var coast = CoastX(p.Y) + (Fbm(new Vector2(0, p.Y / 70f), 3) - 0.5f) * 60 - p.X;
        if (coast < 0)
        {
            return -Mathf.Clamp(-coast / 420f, 0.03f, 1f) * 0.6f;
        }

        // 底面：缓丘加西高东低的地势，近海处压低成滨海平原。
        var ground = 0.05f + 0.1f * Fbm(p / 380f, 4) + 0.05f * Fbm(p / 120f + new Vector2(9, 3), 3)
                     + 0.16f * Mathf.SmoothStep(1100 * WorldMapSamples.Scale, 150 * WorldMapSamples.Scale, p.X);
        ground *= Mathf.SmoothStep(0, 160, coast) * 0.7f + 0.3f;

        // 山脉：到走向折线的距离决定山体包络，脊状噪声给出峰与谷。
        var mountain = 0f;
        foreach (var (path, peak, width) in Ranges)
        {
            var d = Distance(path, p);
            if (d >= width * 1.3f)
            {
                continue;
            }

            var envelope = Mathf.SmoothStep(width * 1.3f, 0, d + (Fbm(p / 90f + new Vector2(4, 1), 3) - 0.5f) * width * 0.6f);
            var ridges = Ridge(p / 150f, 4);
            mountain = Mathf.Max(mountain, envelope * peak * (0.45f + 0.55f * ridges));
        }

        // 山脉以平视山峦画在地面之上，地面只保留缓升的山地底色，不再做高耸的晕渲。
        var height = ground + mountain * 0.32f;

        // 河谷：河道本身压到近水面，两岸按谷宽渐升。
        foreach (var (path, width, valley) in rivers)
        {
            var d = Distance(path, p);
            if (d < valley)
            {
                height = Mathf.Lerp(0.012f, height, Mathf.SmoothStep(width * 0.5f, valley, d));
            }
        }

        // 湖：椭圆盆地，湖心略深。
        var lake = ((p - LakeCenter) / LakeRadius).Length() + (Fbm(p / 30f, 2) - 0.5f) * 0.25f;
        if (lake < 2.2f)
        {
            height = lake < 1 ? -0.04f * (1.2f - lake) : Mathf.Lerp(0.01f, height, Mathf.SmoothStep(1, 2.2f, lake));
        }

        return height;
    }

    /// <summary>二维值噪声，取值 0–1。</summary>
    private static float ValueNoise(Vector2 p)
    {
        var i = p.Floor();
        var f = p - i;
        var u = f * f * (Vector2.One * 3 - 2 * f);
        float Corner(float dx, float dy) => Brushwork.Hash((i.X + dx) * 127.1f + (i.Y + dy) * 311.7f);
        return Mathf.Lerp(Mathf.Lerp(Corner(0, 0), Corner(1, 0), u.X), Mathf.Lerp(Corner(0, 1), Corner(1, 1), u.X), u.Y);
    }

    private static float Fbm(Vector2 p, int octaves)
    {
        var sum = 0f;
        var amp = 0.5f;
        var norm = 0f;
        for (var i = 0; i < octaves; i++)
        {
            sum += amp * ValueNoise(p);
            norm += amp;
            p = p * 2.03f + new Vector2(17.1f, 9.2f);
            amp *= 0.5f;
        }

        return sum / norm;
    }

    /// <summary>脊状噪声：山脊处为 1，谷底趋 0。</summary>
    private static float Ridge(Vector2 p, int octaves)
    {
        var sum = 0f;
        var amp = 0.5f;
        var norm = 0f;
        for (var i = 0; i < octaves; i++)
        {
            var n = 1 - Mathf.Abs(ValueNoise(p) * 2 - 1);
            sum += amp * n * n;
            norm += amp;
            p = p * 2.1f + new Vector2(3.3f, 7.7f);
            amp *= 0.5f;
        }

        return sum / norm;
    }

    /// <summary>海岸 x 坐标：按 y 在海岸线折点间插值。</summary>
    private static float CoastX(float y)
    {
        for (var i = 1; i < Coast.Length; i++)
        {
            if (y <= Coast[i].Y)
            {
                var t = Mathf.InverseLerp(Coast[i - 1].Y, Coast[i].Y, y);
                return Mathf.Lerp(Coast[i - 1].X, Coast[i].X, t);
            }
        }

        return Coast[^1].X;
    }

    private static float Distance(Vector2[] path, Vector2 p)
    {
        var best = float.MaxValue;
        for (var i = 1; i < path.Length; i++)
        {
            var closest = Geometry2D.GetClosestPointToSegment(p, path[i - 1], path[i]);
            best = Mathf.Min(best, closest.DistanceSquaredTo(p));
        }

        return Mathf.Sqrt(best);
    }

    // ── 山峦（平视） ─────────────────────────────────────

    private static readonly Color MountainLine = new(0.16f, 0.24f, 0.26f, 0.9f);

    /// <summary>一座山体。Layer 为 0 的主山带山脚阴影与积雪；1、2 为叠在其前方、更矮更绿的山梁。</summary>
    private readonly record struct Massif(Vector2 Base, float Width, float Height, float Seed, int Layer = 0);

    /// <summary>
    /// 沿每条山脉的走向摆放山体：主脊两三排，间距、大小、前后位置都随机错开，偶有一座特别高的主峰；
    /// 山脉前沿再撒一排低矮的丘陵。按底边由远到近绘制，前山压住后山；避开地标所在的空地、江河、湖与海岸。
    /// </summary>
    private static void DrawMountains(Rid item)
    {
        var massifs = new List<Massif>();
        var r = 0;
        foreach (var (path, peak, width) in Ranges)
        {
            r++;
            var length = 0f;
            for (var i = 1; i < path.Length; i++)
            {
                length += path[i - 1].DistanceTo(path[i]);
            }

            var rows = width > 200 ? 3 : 2;
            for (var row = 0; row <= rows; row++)
            {
                var foothills = row == rows;
                var across = foothills ? width * 0.62f : (row - (rows - 1) / 2f) * width * 0.48f;
                var scale = foothills ? 0.42f : 1.12f - 0.2f * row;
                var step = foothills ? 90f : 125f + 30 * row;
                var d = step * (0.2f + 0.6f * Brushwork.Hash(r * 5.1f + row * 2.7f));
                while (d < length)
                {
                    var seed = r * 31.7f + row * 7.3f + d * 0.013f;
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
                    if (!Blocked(c, w, h))
                    {
                        massifs.Add(new Massif(c, w, h, seed));
                    }
                }
            }
        }

        foreach (var m in massifs.OrderBy(m => m.Base.Y))
        {
            DrawMassif(item, m);

            // 叠在主山前的一两道矮山梁，底边略靠前，形成层层叠叠的山势。
            var spurs = m.Height > 70 ? 1 + (int)(Brushwork.Hash(m.Seed + 21) * 2.2f) : 0;
            for (var s = 0; s < spurs; s++)
            {
                var seed = m.Seed + 40 + s * 13.1f;
                var offset = (Brushwork.Hash(seed) - 0.5f) * m.Width * 0.6f;
                var spur = new Massif(m.Base + new Vector2(offset, 4 + s * 6), m.Width * (0.4f + 0.25f * Brushwork.Hash(seed + 1)),
                    m.Height * (0.4f + 0.2f * Brushwork.Hash(seed + 2)), seed, 1 + s);
                DrawMassif(item, spur);
            }

            // 山脚一抹薄雾，把前后两排山隔开。
            Brushwork.Blot(item, m.Base + new Vector2((Brushwork.Hash(m.Seed + 30) - 0.5f) * m.Width * 0.3f, -m.Height * 0.08f),
                m.Width * 0.3f, new Color(0.94f, 0.97f, 0.96f, 0.28f), m.Seed + 31);
        }
    }

    private static bool Blocked(Vector2 c, float w, float h)
    {
        if (c.X + w * 0.5f > CoastX(c.Y) - 30 || c.Y > WorldMapSamples.Size.Y + 20 || c.Y - h < -40)
        {
            return true;
        }

        foreach (var node in WorldMapSamples.Nodes)
        {
            var d = node.Pos - c;
            if (Mathf.Abs(d.X) < w * 0.25f + 16 && d.Y > -h * 0.5f && d.Y < 24)
            {
                return true;
            }
        }

        Vector2[][] rivers = [GreatRiver, NorthRiver, .. Tributaries];
        var side = new Vector2(w * 0.3f, 0);
        return rivers.Any(river => Distance(river, c) < 28 || Distance(river, c + side) < 18 || Distance(river, c - side) < 18) ||
               ((c - LakeCenter) / (LakeRadius + new Vector2(w * 0.5f, 40))).Length() < 1;
    }

    private static void Quad(Rid item, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd) =>
        RenderingServer.CanvasItemAddTriangleArray(item, [0, 1, 2, 0, 2, 3], [a, b, c, d], [ca, cb, cc, cd]);

    /// <summary>
    /// 一座山体（平视）：天际线由三到七座高低不一的峰叠成，每座峰左右坡宽窄不同，峰顶圆肩、山脚内收，
    /// 山脊上有大小两级岩齿。按天际线每段的坡向分面，朝西的上坡受光、朝东的下坡背光（赛璐璐两阶），
    /// 上段石青、下段石绿，山脚淡出到地面；受光面切沟壑暗楔，背光面留亮楔，峰顶补脊线，高峰积雪，
    /// 最后沿天际线勾线。前排山梁（Layer ≥ 1）更绿、更亮、线更细。
    /// </summary>
    private static void DrawMassif(Rid item, Massif m)
    {
        const int n = 56;
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

        var front = m.Layer > 0;
        if (!front)
        {
            Brushwork.Blot(item, c + new Vector2(w * 0.06f, -h * 0.05f), w * 0.34f, new Color(0.1f, 0.2f, 0.2f, 0.15f), m.Seed);
        }

        // 前排山梁偏绿偏亮，远处主山偏石青。
        var green = front ? 0.35f : 0;
        var litTop = new Color(0.55f, 0.74f, 0.8f).Lerp(new Color(0.55f, 0.78f, 0.62f), green);
        var litLow = new Color(0.52f, 0.75f, 0.6f).Lerp(new Color(0.58f, 0.79f, 0.58f), green);
        var shadeTop = new Color(0.33f, 0.5f, 0.62f).Lerp(new Color(0.34f, 0.56f, 0.5f), green);
        var shadeLow = new Color(0.32f, 0.54f, 0.48f).Lerp(new Color(0.36f, 0.58f, 0.46f), green);
        var foot = new Color(0.56f, 0.75f, 0.54f);
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

        // 积雪：高峰顶端一片，下缘参差。
        if (!front && h > 115)
        {
            for (var i = 1; i < n; i++)
            {
                if (profile[i] < 0.88f || profile[i] < profile[i - 1] || profile[i] < profile[i + 1])
                {
                    continue;
                }

                var depth = h * 0.12f;
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

    // ── 江河 ─────────────────────────────────────────────

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
        var pts = Wobble(path, 10, 5, seed);
        const int pieces = 8;
        var per = Mathf.Max(2, pts.Length / pieces);
        foreach (var (tone, extra) in new[] { (RiverBank, 3.2f), (RiverTone, 0f) })
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

    // ── 路 ───────────────────────────────────────────────

    /// <summary>陆路为暗边浅色短划的土路，水路为白色点线。</summary>
    private static void DrawRoads(Rid item)
    {
        var casing = new Color(0.18f, 0.14f, 0.1f, 0.6f);
        var dirt = new Color(0.93f, 0.86f, 0.68f, 0.95f);
        foreach (var edge in WorldMapSamples.Edges)
        {
            var path = RoutePath(edge);
            if (edge.Water)
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

    /// <summary>折线上按总长比例 t（0–1）取点。</summary>
    public static Vector2 AlongPath(Vector2[] path, float t)
    {
        var total = 0f;
        for (var i = 1; i < path.Length; i++)
        {
            total += path[i - 1].DistanceTo(path[i]);
        }

        var goal = Mathf.Clamp(t, 0, 1) * total;
        for (var i = 1; i < path.Length; i++)
        {
            var len = path[i - 1].DistanceTo(path[i]);
            if (goal <= len)
            {
                return path[i - 1].Lerp(path[i], goal / len);
            }

            goal -= len;
        }

        return path[^1];
    }

    // ── 题字 ─────────────────────────────────────────────

    /// <summary>地域题字：竖排大字，浅色字压一层暗影，像写实地图上的地名注记。</summary>
    private void DrawTitles()
    {
        foreach (var (text, pos, size) in new[]
                 {
                     ("中原", new Vector2(760, 470), 50),
                     ("江南", new Vector2(1880, 1180), 54),
                     ("巴蜀", new Vector2(560, 1180), 50),
                     ("北岭", new Vector2(1680, 110), 46),
                     ("大江", new Vector2(1260, 960), 36),
                     ("大河", new Vector2(1790, 300), 34),
                     ("海", new Vector2(2420, 700), 68),
                 })
        {
            var at = pos * WorldMapSamples.Scale;
            var y = at.Y;
            var fontSize = (int)(size * 1.3f);
            foreach (var rune in text.EnumerateRunes())
            {
                var glyph = rune.ToString();
                DrawString(UiFonts.Title, new Vector2(at.X + 2, y + 2), glyph, HorizontalAlignment.Left, -1, fontSize,
                    UiPalette.Abyss with { A = 0.35f });
                DrawString(UiFonts.Title, new Vector2(at.X, y), glyph, HorizontalAlignment.Left, -1, fontSize,
                    UiPalette.Surface with { A = 0.72f });
                y += fontSize * 1.1f;
            }
        }
    }
}
