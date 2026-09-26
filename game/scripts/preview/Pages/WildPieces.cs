using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 山路布局的派生几何：台地高度、可走区、地面多边形与山道带，地面、碰撞、排序与小地图共用一份。
/// 坐标约定见 <see cref="WildSamples"/>：(A, D) 为画面坐标，经 <see cref="WildSamples.W(float, float)"/> 换成世界平面坐标。
/// </summary>
public static class WildLayout
{
    /// <summary>地面多边形向两侧延伸到画面外。</summary>
    public const float FarA0 = -1800;

    public const float FarA1 = 5400;

    /// <summary>崖边两侧不可走的半宽：只能经石阶上下。</summary>
    private const float Wall = 22;

    private const float Step = 40;

    public static Vector2 S(float a, float d, float z) => TownView.P(WildSamples.W(a, d), z);

    public static Vector2 S(Vector2 ad, float z) => S(ad.X, ad.Y, z);

    public static WildSteps[] Stairs { get; } = [WildSamples.Steps1, WildSamples.Steps2];

    public static bool OnBridge(float a, float d) =>
        a >= WildSamples.BridgeA0 && a <= WildSamples.BridgeA1 && d >= WildSamples.BridgeD0 && d <= WildSamples.BridgeD1;

    public static bool InStream(float a, float d) => d > WildSamples.StreamNorth(a) && d < WildSamples.StreamSouth(a);

    /// <summary>某处地面的高度：石阶上按远近连续升高，桥面略高，溪中为水面。</summary>
    public static float GroundZ(Vector2 world)
    {
        var f = WildSamples.Frame(world);
        var (a, d) = (f.X, f.Y);
        foreach (var s in Stairs)
        {
            if (a >= s.A0 && a <= s.A1 && d >= s.D0 && d <= s.D1)
            {
                return s.Low + (s.High - s.Low) * (s.D1 - d) / (s.D1 - s.D0);
            }
        }

        if (OnBridge(a, d)) return WildSamples.BridgeZ;
        if (d > WildSamples.Edge1(a)) return InStream(a, d) ? WildSamples.WaterZ : WildSamples.Z0;
        return d > WildSamples.Edge2(a) ? WildSamples.Z1 : WildSamples.Z2;
    }

    public static bool InWalkArea(Vector2 world)
    {
        var f = WildSamples.Frame(world);
        var (a, d) = (f.X, f.Y);
        if (a < WildSamples.WalkA0 || a > WildSamples.WalkA1 || d > WildSamples.Front || d < WildSamples.Back)
        {
            return false;
        }

        foreach (var s in Stairs)
        {
            // 阶面：两侧留出石栏；阶底多留一截接低台。
            if (a > s.A0 + Wall && a < s.A1 - Wall && d >= s.D0 && d < s.D1 + Wall)
            {
                return true;
            }

            var side = (a > s.A0 - Wall && a < s.A0 + Wall) || (a > s.A1 - Wall && a < s.A1 + Wall);
            if (side && d >= s.D0 && d < s.D1 + Wall)
            {
                return false;
            }
        }

        if (Mathf.Abs(d - WildSamples.Edge1(a)) < Wall || Mathf.Abs(d - WildSamples.Edge2(a)) < Wall)
        {
            return false;
        }

        if (d > WildSamples.StreamNorth(a) - 12 && d < WildSamples.StreamSouth(a) + 12)
        {
            return a > WildSamples.BridgeA0 + 18 && a < WildSamples.BridgeA1 - 18;
        }

        return true;
    }

    /// <summary>沿一条随 A 变化的边取点（画面坐标），from → to（可逆向）。</summary>
    public static IEnumerable<Vector2> Edge(Func<float, float> d, float from, float to)
    {
        var dir = Mathf.Sign(to - from);
        for (var a = from; dir > 0 ? a < to : a > to; a += dir * Step) yield return new Vector2(a, d(a));
        yield return new Vector2(to, d(to));
    }

    /// <summary>
    /// 一层台地的地面（画面坐标）：前沿（崖边或溪岸）自左向右、后沿自右向左；
    /// notch 为凿进本层的石阶，前沿在其宽度内退到阶顶。
    /// </summary>
    public static List<Vector2> Band(Func<float, float> back, Func<float, float> front, WildSteps? notch = null)
    {
        var pts = new List<Vector2>();
        foreach (var p in Edge(front, FarA0, FarA1))
        {
            if (notch is { } s)
            {
                if (p.X > s.A0 && p.X < s.A1) continue;
                if (p.X >= s.A1 && pts.Count > 0 && pts[^1].X < s.A0 + 1)
                {
                    pts.Add(new Vector2(s.A0, front(s.A0)));
                    pts.Add(new Vector2(s.A0, s.D0));
                    pts.Add(new Vector2(s.A1, s.D0));
                    pts.Add(new Vector2(s.A1, front(s.A1)));
                }
            }

            pts.Add(p);
        }

        pts.AddRange(Edge(back, FarA1, FarA0));
        return pts;
    }

    /// <summary>山道带：中线加密后左右各偏半宽，边缘随位置微微起伏（画面坐标）。</summary>
    public static Vector2[] Ribbon(Vector2[] line, float width, int seed)
    {
        var dense = new List<Vector2>();
        for (var i = 0; i < line.Length - 1; i++)
        {
            var n = Mathf.Max(1, Mathf.CeilToInt(line[i].DistanceTo(line[i + 1]) / 30));
            for (var k = 0; k < n; k++) dense.Add(line[i].Lerp(line[i + 1], (float)k / n));
        }

        dense.Add(line[^1]);
        var left = new List<Vector2>();
        var right = new List<Vector2>();
        for (var i = 0; i < dense.Count; i++)
        {
            var t = (dense[Mathf.Min(i + 1, dense.Count - 1)] - dense[Mathf.Max(i - 1, 0)]).Normalized();
            var normal = new Vector2(-t.Y, t.X);
            var half = width / 2 * (0.88f + 0.24f * Cel.Rand(seed, i));
            left.Add(dense[i] + normal * half);
            right.Add(dense[i] - normal * half * (0.9f + 0.2f * Cel.Rand(seed, i + 300)));
        }

        right.Reverse();
        return [.. left, .. right];
    }
}

/// <summary>
/// 崖壁或溪岸的立面：沿一条崖边（画面坐标）自 Z0 立到 Z1，朝向镜头。
/// 岩面由宽窄不一的岩块拼成，每块被一道斜折痕分成受光、背光两面，块间裂缝、横向石台与垂苔打破规整；
/// 崖脚在低台地面上压一道接触阴影。Gaps 为凿石阶处（不画岩面）。
/// 崖顶的勾线、草边与崖沿小丛另由 LipOnly 的一份在高台地面之后画，免得被高台地面盖住。
/// </summary>
public partial class WildWall : Node2D
{
    private static readonly Color RockLight = Color.FromHtml("#B9C4BA");
    private static readonly Color Rock = Color.FromHtml("#96A49B");
    private static readonly Color RockDark = Color.FromHtml("#71817B");
    private static readonly Color RockDeep = Color.FromHtml("#5E6E6F");
    private static readonly Color BankTone = Color.FromHtml("#7A8A7D");

    public required Func<float, float> Edge { get; init; }
    public required float Z0 { get; init; }
    public required float Z1 { get; init; }
    public bool Bank { get; init; }
    public bool LipOnly { get; init; }
    public (float A0, float A1)[] Gaps { get; init; } = [];
    public int Seed { get; init; }

    private const float Seg = 20;

    private IEnumerable<(float A0, float A1)> Spans()
    {
        var a = WildLayout.FarA0;
        foreach (var (g0, g1) in Gaps.OrderBy(g => g.A0))
        {
            yield return (a, g0);
            a = g1;
        }

        yield return (a, WildLayout.FarA1);
    }

    private Vector2 S(float a, float z) => WildLayout.S(a, Edge(a), z);

    /// <summary>沿崖边曲线自 a0 到 a1 取点（高 z）。</summary>
    private List<Vector2> Line(float a0, float a1, float z)
    {
        var pts = new List<Vector2>();
        for (var a = a0; a < a1 - 2; a += Seg) pts.Add(S(a, z));
        pts.Add(S(a1, z));
        return pts;
    }

    /// <summary>岩面上的一条竖带：上沿 top0→top1（高 zTop），下沿 bot1→bot0（高 zBot），上下沿都贴着崖边曲线取点。</summary>
    private Vector2[] Strip(float top0, float top1, float bot1, float bot0, float zTop, float zBot)
    {
        var pts = Line(top0, top1, zTop);
        var bottom = Line(bot0, bot1, zBot);
        bottom.Reverse();
        pts.AddRange(bottom);
        return pts.ToArray();
    }

    public override void _Ready()
    {
        TextureRepeat = TextureRepeatEnum.Enabled;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    public override void _Draw()
    {
        foreach (var (s0, s1) in Spans())
        {
            if (LipOnly)
            {
                DrawLip(s0, s1);
            }
            else if (Bank)
            {
                DrawBank(s0, s1);
            }
            else
            {
                DrawCliff(s0, s1);
            }
        }
    }

    private void DrawCliff(float s0, float s1)
    {
        var h = Z1 - Z0;
        // 崖脚接触阴影：落在低台地面上，向镜头方向渐淡。
        var shade = Cel.Ink with { A = 0.24f };
        for (var a = s0; a < s1; a += 40)
        {
            var b = Mathf.Min(a + 40, s1);
            DrawPolygon([S(a, Z0), S(b, Z0), WildLayout.S(b, Edge(b) + 70, Z0), WildLayout.S(a, Edge(a) + 70, Z0)],
                [shade, shade, shade with { A = 0 }, shade with { A = 0 }]);
        }

        // AI 岩壁纹理（wild.cliff，横向无缝）入库时：沿崖边每段按崖长取 u、按高取 v 贴图，岩块、石台与顶光不再画。
        if (CliffTexture is { } tex)
        {
            DrawFacadeTexture(s0, s1, tex.Texture, tex.WorldSize, new Color(0.78f, 0.82f, 0.8f));
        }
        else
        {
            DrawCliffBlocks(s0, s1, h);
        }

        // 垂苔：自崖顶挂下的不规则绿片，深浅两层（贴了 AI 岩壁纹理时不画：纹理自带苔斑，程序化绿片像三角贴纸）。
        for (var a = s0 + 10; a < s1 - 40 && CliffTexture is null; a += 60)
        {
            if (Cel.Rand(Seed, (int)a + 31) < 0.5f) continue;
            var len = 30 + 50 * Cel.Rand(Seed, (int)a + 33);
            var pts = new List<Vector2> { S(a, Z1) };
            var inner = new List<Vector2> { S(a, Z1) };
            for (var t = 1; t < 6; t++)
            {
                var x = a + len * t / 6;
                var drop = h * (0.12f + 0.25f * Cel.Rand(Seed, (int)x + t)) * Mathf.Sin(Mathf.Pi * t / 6);
                pts.Add(S(x, Z1 - drop));
                inner.Add(S(x, Z1 - drop * 0.55f));
            }

            pts.Add(S(a + len, Z1));
            inner.Add(S(a + len, Z1));
            DrawColoredPolygon(pts.ToArray(), Cel.LeafDark);
            DrawColoredPolygon(inner.ToArray(), Cel.Leaf);
        }

        DrawPolyline(Line(s0, s1, Z0).ToArray(), Cel.Ink with { A = 0.45f }, 1.6f, true);
        if (s0 > WildLayout.FarA0) DrawLine(S(s0, Z0), S(s0, Z1), Cel.Ink, 2.2f, true);
        if (s1 < WildLayout.FarA1) DrawLine(S(s1, Z0), S(s1, Z1), Cel.Ink, 2.2f, true);
    }

    private static readonly (Texture2D Texture, float WorldSize)? CliffTexture = PieceArt.FindTexture("wild.cliff");

    private static readonly (Texture2D Texture, float WorldSize)? BankTexture = PieceArt.FindTexture("wild.bank");

    /// <summary>立面纹理（岩壁、溪岸）：u 随崖长（每 worldSize 循环一次），v 自顶 0 到脚 1；脚下按 foot 压暗（崖脚接地面阴影、岸脚湿石）。</summary>
    private void DrawFacadeTexture(float s0, float s1, Texture2D texture, float worldSize, Color foot, float v1 = 1)
    {
        var white = Colors.White;
        for (var a = s0; a < s1 - 0.5f; a += Seg)
        {
            var b = Mathf.Min(a + Seg, s1);
            DrawPolygon([S(a, Z1), S(b, Z1), S(b, Z0), S(a, Z0)], [white, white, foot, foot],
                [new(a / worldSize, 0), new(b / worldSize, 0), new(b / worldSize, v1), new(a / worldSize, v1)], texture);
        }
    }

    /// <summary>程序化岩块（无 AI 纹理时）：宽 50–150，斜折痕分受光 / 背光两面；色阶按块随机，避免成排同色。</summary>
    private void DrawCliffBlocks(float s0, float s1, float h)
    {
        var i = 0;
        for (var a = s0; a < s1 - 1; i++)
        {
            var b = Mathf.Min(a + 50 + 100 * Cel.Rand(Seed, i), s1);
            if (s1 - b < 30) b = s1;
            var w = b - a;
            var creaseTop = a + w * (0.3f + 0.3f * Cel.Rand(Seed, i + 200));
            var creaseBot = Mathf.Clamp(creaseTop + w * (Cel.Rand(Seed, i + 300) - 0.5f) * 0.6f, a + 4, b - 4);
            var r = Cel.Rand(Seed, i + 400);
            var (lit, dark) = r < 0.35f ? (RockLight, Rock) : r < 0.8f ? (Rock, RockDark) : (RockDark, RockDeep);
            DrawColoredPolygon(Strip(a, creaseTop, creaseBot, a, Z1, Z0), lit);
            DrawColoredPolygon(Strip(creaseTop, b, b, creaseBot, Z1, Z0), dark);
            DrawLine(S(creaseTop, Z1), S(creaseBot, Z0 + h * 0.15f), Cel.Ink with { A = 0.35f }, 1.6f, true);
            if (b < s1)
            {
                var depth = h * (0.55f + 0.45f * Cel.Rand(Seed, i + 500));
                DrawPolyline([S(b, Z1), S(b + 6, Z1 - depth * 0.5f), S(b - 3, Z1 - depth)], Cel.Ink with { A = 0.6f }, 2f, true);
            }

            // 横向石台：受光的上沿 + 暗的下沿。
            if (Cel.Rand(Seed, i + 600) > 0.55f && w > 60)
            {
                var z = Z0 + h * (0.3f + 0.4f * Cel.Rand(Seed, i + 700));
                var x0 = a + w * 0.15f;
                var x1 = Mathf.Min(x0 + 40 + 50 * Cel.Rand(Seed, i + 800), b - 4);
                DrawColoredPolygon(Strip(x0, x1, x1 - 6, x0 + 6, z + 5, z - 3), RockLight);
                DrawLine(S(x0 + 6, z - 3), S(x1 - 6, z - 3), Cel.Ink with { A = 0.55f }, 1.8f, true);
            }

            a = b;
        }

        // 崖顶一道受光。
        DrawColoredPolygon(Strip(s0, s1, s1, s0, Z1, Z1 - h * 0.1f), RockLight with { A = 0.55f });
    }

    private void DrawBank(float s0, float s1)
    {
        // AI 卵石岸纹理（wild.bank，横向无缝）入库时整面贴图，岸脚压暗成湿石；否则画程序化椭圆卵石。
        if (BankTexture is { } tex)
        {
            // 岸高 40 只取纹理上部 v1，卵石按纹理宽高比放大到约二三十厘米。
            var v1 = (Z1 - Z0) * tex.Texture.GetWidth() / tex.Texture.GetHeight() / tex.WorldSize;
            DrawFacadeTexture(s0, s1, tex.Texture, tex.WorldSize, new Color(0.7f, 0.78f, 0.78f), v1);
            DrawPolyline(Line(s0, s1, Z0 + 2).ToArray(), Color.FromHtml("#E4F2EE") with { A = 0.7f }, 2.4f, true);
            return;
        }

        DrawColoredPolygon(Strip(s0, s1, s1, s0, Z1, Z0), BankTone);
        DrawColoredPolygon(Strip(s0, s1, s1, s0, Z1, Z1 - (Z1 - Z0) * 0.35f), BankTone.Lightened(0.15f));
        // 卵石与水线白沫。
        for (var a = s0 + 12; a < s1; a += 26)
        {
            if (Cel.Rand(Seed, (int)a) < 0.35f) continue;
            var c = S(a, Mathf.Lerp(Z0 + 8, Z1 - 8, Cel.Rand(Seed, (int)a + 5)));
            var r = 7 + 6 * Cel.Rand(Seed, (int)a + 9);
            DrawColoredPolygon(Cel.Ellipse(c, r, r * 0.7f, 12), Color.FromHtml("#A9B4AA"));
            DrawArc(c, r, 0.2f, Mathf.Pi - 0.2f, 8, Cel.Ink with { A = 0.45f }, 1.4f, true);
        }

        DrawPolyline(Line(s0, s1, Z0 + 2).ToArray(), Color.FromHtml("#E4F2EE") with { A = 0.85f }, 3f, true);
    }

    /// <summary>崖沿：高台地面上贴边一道苔色、一排挑出的草叶、间或一丛矮灌，最后勾崖顶线。</summary>
    private void DrawLip(float s0, float s1)
    {
        var moss = Cel.LeafDark with { A = 0.55f };
        for (var a = s0; a < s1; a += 40)
        {
            var b = Mathf.Min(a + 40, s1);
            DrawPolygon([S(a, Z1), S(b, Z1), WildLayout.S(b, Edge(b) - 26, Z1), WildLayout.S(a, Edge(a) - 26, Z1)],
                [moss, moss, moss with { A = 0 }, moss with { A = 0 }]);
        }

        DrawPolyline(Line(s0, s1, Z1).ToArray(), Cel.Ink, 2.6f, true);
        if (Tufts.Ready)
        {
            // AI 草丛精灵沿崖沿 / 岸沿错落：间距、大小、左右翻转随机，崖沿间或一丛矮灌。
            for (var a = s0 + 6; a < s1 - 6; a += 20 + 34 * Cel.Rand(Seed, (int)a + 5))
            {
                var r = Cel.Rand(Seed, (int)a + 77);
                if (r < 0.15f) continue;
                var bush = !Bank && Cel.Rand(Seed, (int)a + 91) > 0.86f;
                var height = bush ? 46 + 18 * r : (Bank ? 24 : 30) + 20 * r;
                Tufts.Draw(this, S(a, Z1) + new Vector2(0, 3), height, bush ? -1 : (int)(Cel.Rand(Seed, (int)a + 13) * 4), Cel.Rand(Seed, (int)a + 17) > 0.5f);
            }

            return;
        }

        for (var a = s0 + 4; a < s1; a += 11)
        {
            var r = Cel.Rand(Seed, (int)a + 77);
            if (r < 0.25f) continue;
            var root = S(a, Z1 + 1);
            var tip = S(a + (r - 0.5f) * 24, Z1 + 14 + 22 * r);
            DrawLine(root, tip, r > 0.7f ? Cel.LeafLight : Cel.Leaf, 3f, true);
        }

        if (Bank) return;
        for (var a = s0 + 60; a < s1 - 60; a += 90 + 120 * Cel.Rand(Seed, (int)a + 3))
        {
            if (Cel.Rand(Seed, (int)a + 91) < 0.45f) continue;
            var c = WildLayout.S(a, Edge(a) - 14, Z1);
            var s = 0.7f + 0.5f * Cel.Rand(Seed, (int)a + 92);
            foreach (var (dx, dy, rr, tone) in new[] { (-16f, -10f, 22f, Cel.LeafDark), (14f, -8f, 20f, Cel.LeafDark), (0f, -20f, 20f, Cel.Leaf), (-8f, -26f, 11f, Cel.LeafLight) })
            {
                DrawColoredPolygon(Cel.Ellipse(c + new Vector2(dx, dy) * s, rr * s, rr * 0.75f * s, 14), tone);
            }

            DrawArc(c + new Vector2(0, -12) * s, 30 * s, 0.3f, Mathf.Pi - 0.3f, 10, Cel.Ink with { A = 0.5f }, 1.8f, true);
        }
    }
}

/// <summary>
/// 凿进崖里的石阶：逐级方石，两侧石栏，贴地层中按远近先后画出（行人走在其上）。
/// 各面贴 AI 石面纹理（wild.ground.slab，按世界坐标取样，相邻两级纹理连续），明暗与勾线沿用面的三阶受光；
/// 另加踏面前沿受光倒角、两端苔藓与少量缺角（2026-09-26 用户认为纯色块占位太粗糙）。无纹理时退回纯色面。
/// </summary>
public partial class WildStepsNode : TownPiece
{
    private static readonly Color Tread = Color.FromHtml("#C4CCC4");
    private static readonly Color Riser = Color.FromHtml("#7D8B84");
    private static readonly Color Rock = Color.FromHtml("#7F8D86");
    private const float Curb = 18;

    private readonly WildSteps _s;

    public WildStepsNode(WildSteps s, string artId)
    {
        _s = s;
        var tex = PieceArt.FindTexture("wild.ground.slab");
        if (tex is not null)
        {
            TextureRepeat = TextureRepeatEnum.Enabled;
            TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        }

        var tread = (s.D1 - s.D0) / s.Count;
        var rise = (s.High - s.Low) / s.Count;
        for (var i = s.Count - 1; i >= 0; i--)
        {
            var d0 = s.D1 - (i + 1) * tread;
            var d1 = s.D1 - i * tread;
            var top = s.Low + (i + 1) * rise;
            // 每级石色略有深浅，石栏比踏步暗一阶。
            var k = Cel.Rand(s.Count * 7 + 3, i);
            var step = new Color(1.04f + 0.08f * k, 1.08f + 0.08f * k, 1.04f + 0.07f * k);
            var steps = Solid.ExtrudeZ(Quad(s.A0 + Curb, s.A1 - Curb, d0, d1), s.Low, top, Riser, Tread, 1.4f);
            Faces.AddRange(tex is { } t ? FaceTexture.Apply(steps, t.Texture, t.WorldSize, step, 1.4f) : steps);
        }

        // 两侧石栏：一整条随坡升高的斜顶石栏（镜头只看得到斜顶与朝镜头的端面），不再逐级分块。
        foreach (var (c0, c1) in new[] { (s.A0, s.A0 + Curb), (s.A1 - Curb, s.A1) })
        {
            var (nearTop, farTop) = (s.Low + rise + 14, s.High + 14);
            Vector3 P3(float a, float d, float z) { var w = WildSamples.W(a, d); return new Vector3(w.X, w.Y, z); }
            var slope = (P3(c0, s.D0, farTop) - P3(c0, s.D1, nearTop)).Cross(P3(c1, s.D1, nearTop) - P3(c0, s.D1, nearTop)).Normalized();
            if (slope.Z < 0) slope = -slope;
            List<Face> curb =
            [
                new Face { Points = [P3(c0, s.D1, s.Low), P3(c1, s.D1, s.Low), P3(c1, s.D1, nearTop), P3(c0, s.D1, nearTop)],
                    Normal = new Vector3(-1, 1, 0).Normalized(), Color = Rock, Outline = 1.4f },
                new Face { Points = [P3(c0, s.D1, nearTop), P3(c1, s.D1, nearTop), P3(c1, s.D0, farTop), P3(c0, s.D0, farTop)],
                    Normal = slope, Color = Rock.Lightened(0.18f), Outline = 1.4f },
            ];
            Faces.AddRange(tex is { } u ? FaceTexture.Apply(curb, u.Texture, u.WorldSize, new Color(0.92f, 0.97f, 0.95f), 1.4f) : curb);
        }

        Seal(Bounds(Quad(s.A0, s.A1, s.D0, s.D1)));
        UseArt(artId);
    }

    protected override void DrawExtras()
    {
        if (!PieceArt.Enabled) return;
        var s = _s;
        var tread = (s.D1 - s.D0) / s.Count;
        var rise = (s.High - s.Low) / s.Count;
        var (a0, a1) = (s.A0 + Curb, s.A1 - Curb);
        for (var i = 0; i < s.Count; i++)
        {
            var d1 = s.D1 - i * tread;
            var top = s.Low + (i + 1) * rise;
            // 踏面前沿受光倒角：一道亮线，间或被缺口打断。
            var gapAt = a0 + (a1 - a0) * Cel.Rand(s.Count, i + 40);
            var hasGap = Cel.Rand(s.Count, i + 50) > 0.55f;
            DrawLine(WildLayout.S(a0 + 3, d1 - 2, top), WildLayout.S(hasGap ? gapAt - 8 : a1 - 3, d1 - 2, top), Colors.White with { A = 0.45f }, 2.2f, true);
            if (hasGap)
            {
                DrawLine(WildLayout.S(gapAt + 10, d1 - 2, top), WildLayout.S(a1 - 3, d1 - 2, top), Colors.White with { A = 0.45f }, 2.2f, true);
                // 缺角：前沿一小块暗色三角。
                DrawColoredPolygon([WildLayout.S(gapAt - 8, d1, top), WildLayout.S(gapAt + 10, d1, top), WildLayout.S(gapAt + 2, d1, top - rise * 0.55f)], Cel.Ink with { A = 0.35f });
            }

            // 贴石栏的墙角偶有一小撮苔：三四个大小不一的扁点挤成一团，颜色压暗，不成排。
            foreach (var (edge, dir) in new[] { (a0, 1f), (a1, -1f) })
            {
                var seed = i * 3 + (dir > 0 ? 1 : 2);
                if (Cel.Rand(s.Count, seed) < 0.72f) continue;
                var at = d1 - tread * (0.25f + 0.5f * Cel.Rand(s.Count, seed + 90));
                for (var k = 0; k < 4; k++)
                {
                    var c = WildLayout.S(edge + dir * (3 + 10 * Cel.Rand(s.Count, seed * 7 + k)), at + (Cel.Rand(s.Count, seed * 5 + k) - 0.5f) * 12, top);
                    var r = 2.5f + 4 * Cel.Rand(s.Count, seed * 11 + k);
                    DrawColoredPolygon(Cel.Ellipse(c, r, r * 0.55f, 8), (k == 3 ? Cel.Leaf : Cel.LeafDark) with { A = 0.7f });
                }
            }
        }

        // 石阶脚下两角各一丛草（AI 草丛精灵；未入库时画几笔草叶）。
        foreach (var a in new[] { s.A0 + 4, s.A1 - 4 })
        {
            var root = WildLayout.S(a, s.D1 + 4, s.Low);
            var r = Cel.Rand(s.Count, (int)a);
            if (Tufts.Ready)
            {
                Tufts.Draw(this, root, 24 + 10 * r, (int)(r * 4), a > s.A0 + 10);
            }
            else
            {
                DrawLine(root, root + new Vector2((r - 0.5f) * 14, -12 - 10 * r), r > 0.6f ? Cel.LeafLight : Cel.Leaf, 2.6f, true);
            }
        }
    }

    internal static Vector2[] Quad(float a0, float a1, float d0, float d1) =>
        [WildSamples.W(a0, d0), WildSamples.W(a1, d0), WildSamples.W(a1, d1), WildSamples.W(a0, d1)];

    internal static Rect2 Bounds(Vector2[] quad)
    {
        var r = new Rect2(quad[0], Vector2.Zero);
        foreach (var q in quad) r = r.Expand(q);
        return r;
    }
}

/// <summary>跨溪木桥：桥面木板、两侧圆木栏，水面上压一道桥影。</summary>
public partial class WildBridgeNode : TownPiece
{
    private const float Rail = 12;

    public WildBridgeNode()
    {
        var (a0, a1, d0, d1, z) = (WildSamples.BridgeA0, WildSamples.BridgeA1, WildSamples.BridgeD0, WildSamples.BridgeD1, WildSamples.BridgeZ);
        Faces.AddRange(Solid.ExtrudeZ(Quad(a0 + Rail, a1 - Rail, d0, d1), z - 10, z, Cel.Wood, Cel.WoodLight, 1.6f));
        foreach (var (r0, r1) in new[] { (a0, a0 + Rail), (a1 - Rail, a1) })
        {
            Faces.AddRange(Solid.ExtrudeZ(Quad(r0, r1, d0 - 8, d1 + 8), z - 12, z + 16, Cel.WoodDark, Cel.Wood, 1.4f));
        }

        // 桥面木板：AI 木板纹理（wild.ground.planks，板缝沿 A 向、每 22 一块）在桥面内平铺，栏杆仍为程序化圆木。
        if (PieceArt.FindTexture("wild.ground.planks") is { } planks)
        {
            _planks = true;
            TextureFilter = TextureFilterEnum.LinearWithMipmaps;
            var (u, v) = (WildSamples.W(1, 0) - WildSamples.W(0, 0), WildSamples.W(0, 1) - WildSamples.W(0, 0));
            var o = WildSamples.W(a0 + Rail, d0);
            var size = new Vector2(a1 - a0 - 2 * Rail, d1 - d0);
            FaceKit.Decorate(Faces, TownView.Above,
                TownView.Local(new Vector3(o.X, o.Y, z), new Vector3(u.X, u.Y, 0), new Vector3(v.X, v.Y, 0)),
                ci => Tile(ci, planks.Texture, planks.WorldSize, size));
        }

        Seal(WildStepsNode.Bounds(Quad(a0, a1, d0 - 8, d1 + 8)));
        UseArt("wild.bridge");
    }

    private readonly bool _planks;

    private static void Tile(CanvasItem ci, Texture2D texture, float world, Vector2 size)
    {
        var px = texture.GetSize() / world;
        for (var y = 0f; y < size.Y; y += world)
        {
            for (var x = 0f; x < size.X; x += world)
            {
                var cell = new Vector2(Mathf.Min(world, size.X - x), Mathf.Min(world, size.Y - y));
                ci.DrawTextureRectRegion(texture, new Rect2(new Vector2(x, y), cell), new Rect2(Vector2.Zero, cell * px), new Color(1.1f, 1.05f, 1f));
            }
        }
    }

    private static Vector2[] Quad(float a0, float a1, float d0, float d1) =>
        [WildSamples.W(a0, d0), WildSamples.W(a1, d0), WildSamples.W(a1, d1), WildSamples.W(a0, d1)];

    public override void _Draw()
    {
        var (a0, a1) = (WildSamples.BridgeA0, WildSamples.BridgeA1);
        var mid = (a0 + a1) / 2;
        var (n, s) = (WildSamples.StreamNorth(mid), WildSamples.StreamSouth(mid));
        DrawColoredPolygon([WildLayout.S(a0 + 20, n, WildSamples.WaterZ), WildLayout.S(a1 + 20, n, WildSamples.WaterZ),
            WildLayout.S(a1 + 20, s, WildSamples.WaterZ), WildLayout.S(a0 + 20, s, WildSamples.WaterZ)], Cel.Ink with { A = 0.25f });
        base._Draw();
    }

    protected override void DrawExtras()
    {
        if (_planks) return;
        var (a0, a1, d0, d1, z) = (WildSamples.BridgeA0 + Rail, WildSamples.BridgeA1 - Rail, WildSamples.BridgeD0, WildSamples.BridgeD1, WildSamples.BridgeZ);
        for (var d = d0 + 22; d < d1; d += 22)
        {
            DrawLine(WildLayout.S(a0, d, z + 0.5f), WildLayout.S(a1, d, z + 0.5f), Cel.WoodDark with { A = 0.55f }, 1.6f, true);
        }
    }
}

/// <summary>半山台地上的一串脚印：沿山道往山门去（线索交互点的样子）。</summary>
public partial class WildFootprints : Node2D
{
    public override void _Draw()
    {
        var pts = WildSamples.Footprints;
        for (var i = 0; i < pts.Length; i++)
        {
            var dir = (pts[Mathf.Min(i + 1, pts.Length - 1)] - pts[Mathf.Max(i - 1, 0)]).Normalized();
            var side = new Vector2(-dir.Y, dir.X) * (i % 2 == 0 ? 11 : -11);
            var c = pts[i] + side;
            var ring = new Vector2[12];
            for (var k = 0; k < ring.Length; k++)
            {
                var t = Mathf.Tau * k / ring.Length;
                ring[k] = WildLayout.S(c + dir * Mathf.Cos(t) * 15 + new Vector2(-dir.Y, dir.X) * Mathf.Sin(t) * 7, WildSamples.Z1);
            }

            DrawColoredPolygon(ring, Color.FromHtml("#4A3E30") with { A = 0.55f });
        }
    }
}

/// <summary>上台远处的山雾：地面自近到远渐没入雾色，与远景山脚的雾接上。</summary>
public partial class WildFog : Node2D
{
    public static readonly Color Mist = Color.FromHtml("#E4ECE6");

    public override void _Draw()
    {
        var (a0, a1) = (WildLayout.FarA0, WildLayout.FarA1);
        var clear = Mist with { A = 0 };
        var z = WildSamples.Z2;
        DrawPolygon([WildLayout.S(a0, -1080, z), WildLayout.S(a1, -1080, z), WildLayout.S(a1, WildSamples.FogEnd, z), WildLayout.S(a0, WildSamples.FogEnd, z)],
            [clear, clear, Mist, Mist]);
    }
}

/// <summary>
/// 远景：天色与三层赛璐璐远山（平视起伏，与大地图山峦同一画法），山脚沉在雾里。
/// 各层按镜头移动的比例反向平移，形成视差（远的几乎不动）。
/// </summary>
public partial class WildBackdrop : Node2D
{
    private readonly List<(Node2D Layer, float Parallax)> _layers = [];
    private Vector2 _reference;

    /// <summary>远山山脚所在（投影坐标 y）：与上台地面没入雾中的位置对齐。</summary>
    private static float Base => WildLayout.S(0, WildSamples.FogEnd, WildSamples.Z2).Y - WildLayout.S(0, 0, 0).Y + 30;

    public override void _Ready()
    {
        _reference = new Vector2(2400, -700);
        var sky = new Node2D();
        sky.Draw += () =>
        {
            var top = Color.FromHtml("#B6D3D6");
            var low = Color.FromHtml("#EDF0E4");
            sky.DrawPolygon([new(-4000, -3600), new(8000, -3600), new(8000, Base - 260), new(-4000, Base - 260)], [top, top, low, low]);
            sky.DrawRect(new Rect2(-4000, Base - 262, 12000, 3000), low);
        };
        AddLayer(sky, 0.95f);
        AddLayer(Range(Base - 30, 380, 170, Color.FromHtml("#A9C2C6"), Color.FromHtml("#93AEB5"), 0.25f, 11), 0.8f);
        AddLayer(Range(Base - 10, 260, 120, Color.FromHtml("#86AAA2"), Color.FromHtml("#6E958D"), 0.4f, 23), 0.6f);
        AddLayer(Range(Base + 10, 150, 80, Color.FromHtml("#6D9A84"), Color.FromHtml("#58846F"), 0.55f, 37, trees: true), 0.4f);
    }

    private void AddLayer(Node2D layer, float parallax)
    {
        AddChild(layer);
        _layers.Add((layer, parallax));
    }

    /// <summary>镜头在 camera 时各层的偏移：层随镜头同向移动 parallax 倍，屏幕上看就只走了 (1 − parallax)。</summary>
    public void Scroll(Vector2 camera)
    {
        foreach (var (layer, k) in _layers)
        {
            var offset = (camera - _reference) * new Vector2(k, k * 0.5f);
            layer.Position = offset.Round();
        }
    }

    /// <summary>一层山峦：不对称的峰、背光侧暗一阶、山脊勾线，山脚一段雾。</summary>
    private static Node2D Range(float baseY, float height, float spacing, Color tone, Color shade, float ink, int seed, bool trees = false)
    {
        const float x0 = -4000;
        const float x1 = 8000;
        var peaks = new List<(float X, float H, float Left, float Right)>();
        for (var x = x0; x < x1; x += spacing * (2.4f + 2.2f * Cel.Rand(seed, (int)x)))
        {
            var h = height * (0.45f + 0.55f * Cel.Rand(seed, (int)x + 3));
            peaks.Add((x, h, h * (1.6f + 1.4f * Cel.Rand(seed, (int)x + 5)), h * (1.8f + 1.6f * Cel.Rand(seed, (int)x + 7))));
        }

        float Ridge(float x)
        {
            var best = 0f;
            foreach (var (px, h, l, r) in peaks)
            {
                var t = x < px ? (px - x) / l : (x - px) / r;
                if (t < 1) best = Mathf.Max(best, h * (1 - Mathf.Pow(t, 0.85f)));
            }

            return best + Brushwork.Noise(x * 0.02f + seed) * 10;
        }

        var ridge = new List<Vector2>();
        for (var x = x0; x <= x1; x += 24) ridge.Add(new Vector2(x, baseY - Ridge(x)));

        var node = new Node2D();
        node.Draw += () =>
        {
            node.DrawColoredPolygon([.. ridge, new(x1, baseY + 3000), new(x0, baseY + 3000)], tone);
            // 背光：每座峰自峰顶到右坡山脚的一片。
            foreach (var (px, h, _, r) in peaks)
            {
                var poly = new List<Vector2> { new(px, baseY - Ridge(px)) };
                for (var x = px + 12; x < px + r * 0.9f; x += 24) poly.Add(new Vector2(x, baseY - Ridge(x)));
                poly.Add(new Vector2(px + r * 0.9f, baseY + 20));
                poly.Add(new Vector2(px + r * 0.18f, baseY + 20));
                if (poly.Count > 3) node.DrawColoredPolygon(poly.ToArray(), shade);
            }

            node.DrawPolyline(ridge.ToArray(), Cel.Ink with { A = ink }, 2f, true);
            if (trees)
            {
                for (var x = x0; x < x1; x += 22)
                {
                    if (Cel.Rand(seed, (int)x + 90) < 0.55f) continue;
                    var y = baseY - Ridge(x);
                    var s = 8 + 8 * Cel.Rand(seed, (int)x + 91);
                    node.DrawColoredPolygon([new(x - s * 0.6f, y + 4), new(x, y - s * 1.8f), new(x + s * 0.6f, y + 4)], shade.Darkened(0.12f));
                }
            }

            var mist = WildFog.Mist;
            node.DrawPolygon([new(x0, baseY - 150), new(x1, baseY - 150), new(x1, baseY), new(x0, baseY)],
                [mist with { A = 0 }, mist with { A = 0 }, mist, mist]);
            node.DrawRect(new Rect2(x0, baseY, x1 - x0, 3000), mist);
        };
        return node;
    }
}

/// <summary>飘在画面最上层的几缕薄雾，缓慢横移；只做氛围，透明度低不挡人物。</summary>
public partial class WildMist : Node2D
{
    private static readonly (Vector2 Ad, float Z, float Length, float Speed)[] Wisps =
    [
        (new(700, -520), WildSamples.Z1, 900, 0.11f),
        (new(2500, -600), WildSamples.Z1 + 40, 1100, 0.08f),
        (new(1500, 180), WildSamples.Z0 + 30, 800, 0.13f),
        (new(3100, 150), WildSamples.Z0 + 60, 700, 0.1f),
        (new(1900, -1150), WildSamples.Z2 + 50, 1300, 0.07f),
    ];

    public float Seconds { get; set; }

    public override void _Draw()
    {
        foreach (var (ad, z, len, speed) in Wisps)
        {
            var drift = Mathf.Sin(Seconds * speed + ad.X) * 60;
            var c = WildLayout.S(ad, z) + new Vector2(drift, 0);
            for (var k = 0; k < 3; k++)
            {
                var s = 1 - k * 0.25f;
                DrawColoredPolygon(Cel.Ellipse(c + new Vector2(k * 40, -k * 4), len * 0.5f * s, 34 * s, 32), WildFog.Mist with { A = 0.07f });
            }
        }
    }
}

/// <summary>野外立着的精灵的共同处：原点在脚底投影（含所在台地高度），按投影竖直比例缩放。</summary>
public abstract partial class WildSprite : TownPiece
{
    protected Vector2 Ground { get; }

    protected float Z { get; }

    protected WildSprite(Vector2 ad, Vector2 foot, Rect2 local)
    {
        Ground = WildSamples.W(ad);
        Z = WildLayout.GroundZ(Ground);
        Position = TownView.P(Ground, Z);
        Foot = new Rect2(Ground - foot / 2, foot);
        var u = TownView.Upright;
        ScreenBox = new Rect2(Position + local.Position * u, local.Size * u);
    }

    public override void _Draw()
    {
        DrawSetTransform(-Position, 0, Vector2.One);
        DrawShadow();
        if (Art is not null)
        {
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            DrawArt();
        }
        else
        {
            DrawSetTransform(Vector2.Zero, 0, Vector2.One * TownView.Upright);
            DrawSprite();
        }

        DrawSetTransform(-Position, 0, Vector2.One);
        DrawFront();
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    protected virtual void DrawShadow()
    {
    }

    /// <summary>画在件本身之上、仍用投影坐标（溪中石的前半圈水沫）。</summary>
    protected virtual void DrawFront()
    {
    }

    protected abstract void DrawSprite();
}

/// <summary>野外的树：山松（层层平展的针叶团）、竹丛、槭树（暖色点缀）。</summary>
public partial class WildTreeNode : WildSprite
{
    private static readonly Color PineDark = Color.FromHtml("#2C5A51");
    private static readonly Color Pine = Color.FromHtml("#3F7766");
    private static readonly Color PineLight = Color.FromHtml("#6DA487");
    private static readonly Color PineGlint = Color.FromHtml("#A6D0A2");

    private readonly WildTree _tree;

    public WildTreeNode(WildTree tree)
        : base(new Vector2(tree.A, tree.D), new Vector2(40, 40) * (tree.Kind == WildTreeKind.Bamboo ? 1.6f : 1),
            tree.Kind switch
            {
                WildTreeKind.Pine => new Rect2(-230 * tree.Size, -560 * tree.Size, 460 * tree.Size, 580 * tree.Size),
                WildTreeKind.Bamboo => new Rect2(-150 * tree.Size, -640 * tree.Size, 300 * tree.Size, 660 * tree.Size),
                _ => new Rect2(-170 * tree.Size, -400 * tree.Size, 340 * tree.Size, 420 * tree.Size),
            })
    {
        _tree = tree;
        UseArt($"wild.tree.{tree.Seed}");
    }

    protected override void DrawShadow() =>
        Cel.GroundShadow(this, Ground + new Vector2(50, -50) * _tree.Size, 170 * _tree.Size, 120 * _tree.Size, 0.16f, Z);

    protected override void DrawSprite()
    {
        switch (_tree.Kind)
        {
            case WildTreeKind.Pine:
                DrawPine(_tree.Size, _tree.Seed);
                break;
            case WildTreeKind.Bamboo:
                DrawBamboo(_tree.Size, _tree.Seed);
                break;
            default:
                DrawMaple(_tree.Size, _tree.Seed);
                break;
        }
    }

    private void DrawPine(float s, int seed)
    {
        var lean = (Cel.Rand(seed, 1) - 0.5f) * 60;
        Vector2[] spine = [new(0, 0), new(-14 * s, -140 * s), new((12 + lean * 0.4f) * s, -290 * s), new((lean - 4) * s, -430 * s)];
        var left = new List<Vector2>();
        var right = new List<Vector2>();
        for (var i = 0; i < spine.Length; i++)
        {
            var t = (spine[Mathf.Min(i + 1, spine.Length - 1)] - spine[Mathf.Max(i - 1, 0)]).Normalized();
            var n = new Vector2(-t.Y, t.X);
            var w = Mathf.Lerp(17, 5, (float)i / (spine.Length - 1)) * s;
            left.Add(spine[i] + n * w);
            right.Add(spine[i] - n * w);
        }

        right.Reverse();
        Cel.Shape(this, [.. left, .. right], Cel.Bark, 1.8f);
        DrawPolyline(spine.Take(3).Select(p => p + new Vector2(-5 * s, 0)).ToArray(), Cel.BarkLight, 3.5f * s, true);

        // 枝与针叶团：自下而上左右交替，顶上一团。
        var tiers = new List<(Vector2 At, float Width)>();
        for (var k = 0; k < 4; k++)
        {
            var y = -(190 + k * 70) * s;
            var root = SpineAt(spine, y);
            var side = k % 2 == 0 ? -1 : 1;
            if (Cel.Rand(seed, k + 10) > 0.8f) side = -side;
            var len = (170 - k * 25 + 40 * Cel.Rand(seed, k + 20)) * s;
            var tip = root + new Vector2(side * len, -30 * s);
            DrawLine(root, tip, Cel.Bark, (8 - k) * s, true);
            DrawLine(root + (tip - root) * 0.5f, tip + new Vector2(side * 20 * s, -40 * s), Cel.Bark, 4 * s, true);
            tiers.Add((tip - new Vector2(side * len * 0.15f, 0), len * 0.95f + 70 * s));
        }

        tiers.Add((spine[^1] + new Vector2(0, -20 * s), 150 * s));
        foreach (var (at, width) in tiers)
        {
            DrawPlate(at, width, 44 * s, seed + (int)at.Y);
        }
    }

    private static Vector2 SpineAt(Vector2[] spine, float y)
    {
        for (var i = 0; i < spine.Length - 1; i++)
        {
            if (y <= spine[i].Y && y >= spine[i + 1].Y)
            {
                return spine[i].Lerp(spine[i + 1], (spine[i].Y - y) / (spine[i].Y - spine[i + 1].Y));
            }
        }

        return spine[^1];
    }

    /// <summary>一团平展的针叶：底下一层暗、主体、左上受光，下沿勾线，上沿点几笔针叶。</summary>
    private void DrawPlate(Vector2 c, float width, float height, int seed)
    {
        var blobs = new List<(Vector2 C, float Rx, float Ry)>();
        var n = 4;
        for (var i = 0; i < n; i++)
        {
            var x = (i - (n - 1) / 2f) / n * width * 0.9f;
            var ry = height * (0.6f + 0.4f * Cel.Rand(seed, i));
            blobs.Add((c + new Vector2(x, -ry * 0.3f * Cel.Rand(seed, i + 5)), width / n * 0.85f, ry));
        }

        foreach (var (bc, rx, ry) in blobs) DrawColoredPolygon(Cel.Ellipse(bc + new Vector2(0, height * 0.3f), rx, ry * 0.75f, 20), PineDark);
        foreach (var (bc, rx, ry) in blobs) DrawColoredPolygon(Cel.Ellipse(bc, rx * 0.95f, ry * 0.7f, 20), Pine);
        foreach (var (bc, rx, ry) in blobs.Take(n - 1))
        {
            DrawColoredPolygon(Cel.Ellipse(bc + new Vector2(-rx * 0.2f, -ry * 0.25f), rx * 0.6f, ry * 0.35f, 16), PineLight);
        }

        foreach (var (bc, rx, ry) in blobs)
        {
            DrawPolyline(Cel.Ellipse(bc + new Vector2(0, height * 0.3f), rx, ry * 0.75f, 14, 0.2f, Mathf.Pi - 0.2f), Cel.Ink with { A = 0.6f }, 2, true);
            for (var k = 0; k < 4; k++)
            {
                var p = bc + new Vector2((Cel.Rand(seed, k + 50) - 0.5f) * rx * 1.4f, -ry * 0.55f);
                DrawLine(p, p + new Vector2(-4, -12), PineGlint, 2.2f, true);
            }
        }
    }

    /// <summary>
    /// 竹丛：10–13 竿粗细、高矮不一，外侧的竿向外斜；叶簇集中在上半截，分深、中、浅三层成团。
    /// 2026-09-26 用户认为第一版（6–8 竿细竹、叶稀）简陋，加密后重新导出作 AI 出件引导。
    /// </summary>
    private void DrawBamboo(float s, int seed)
    {
        Color[] leafTones = [Color.FromHtml("#2F6A50"), Color.FromHtml("#4F8C64"), Color.FromHtml("#86BC86")];
        var culms = new List<(Vector2[] Points, float Width)>();
        var count = 10 + (int)(Cel.Rand(seed, 0) * 4);
        for (var i = 0; i < count; i++)
        {
            var x = (Cel.Rand(seed, i + 1) - 0.5f) * 120 * s;
            var h = (400 + 240 * Cel.Rand(seed, i + 2)) * s;
            var bend = (x / (60 * s) * 36 + (Cel.Rand(seed, i + 3) - 0.5f) * 40) * s;
            var width = (5 + 4 * Cel.Rand(seed, i + 5)) * s;
            var pts = new Vector2[10];
            for (var k = 0; k < pts.Length; k++)
            {
                var t = (float)k / (pts.Length - 1);
                pts[k] = new Vector2(x + bend * t * t, -h * t);
            }

            culms.Add((pts, width));
        }

        // 叶簇先画后排，竿在中，前排叶最后。
        var lines = culms.Select(c => c.Points).ToList();
        DrawLeaves(lines, leafTones[0], s, seed, 0);
        foreach (var (pts, width) in culms)
        {
            DrawPolyline(pts, Cel.Ink, width + 2.5f * s, true);
            DrawPolyline(pts, Color.FromHtml("#6E9F72"), width, true);
            DrawPolyline(pts.Select(p => p + new Vector2(-width * 0.25f, 0)).ToArray(), Color.FromHtml("#A8CF9C"), width * 0.3f, true);
            for (var k = 1; k < pts.Length - 1; k++)
            {
                var n = (pts[k + 1] - pts[k - 1]).Normalized();
                var side = new Vector2(-n.Y, n.X) * (width * 0.5f + 1.5f * s);
                DrawLine(pts[k] - side, pts[k] + side, Cel.Ink, 2f, true);
            }
        }

        DrawLeaves(lines, leafTones[1], s, seed, 1);
        DrawLeaves(lines, leafTones[2], s, seed, 2);
    }

    private void DrawLeaves(List<Vector2[]> culms, Color tone, float s, int seed, int layer)
    {
        for (var c = 0; c < culms.Count; c++)
        {
            var pts = culms[c];
            for (var k = 0; k < 24; k++)
            {
                var id = layer * 1000 + c * 40 + k;
                if (layer == 2 && Cel.Rand(seed, id) < 0.5f) continue;
                var t = 0.4f + 0.6f * Cel.Rand(seed, id + 1);
                var i = Mathf.Min((int)(t * (pts.Length - 1)), pts.Length - 2);
                var at = pts[i].Lerp(pts[i + 1], t * (pts.Length - 1) - i);
                var angle = Mathf.Pi * (0.1f + 0.8f * Cel.Rand(seed, id + 2)) + (Cel.Rand(seed, id + 3) > 0.5f ? 0 : Mathf.Pi * 0.9f);
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Abs(Mathf.Sin(angle)) * 0.6f + 0.2f).Normalized();
                var len = (40 + 24 * Cel.Rand(seed, id + 4)) * s;
                var side = new Vector2(-dir.Y, dir.X) * 6.5f * s;
                Vector2[] leaf = [at, at + dir * len * 0.35f + side, at + dir * len, at + dir * len * 0.35f - side];
                DrawColoredPolygon(leaf, tone);
                if (layer == 0) Cel.Outline(this, leaf, 1.2f, Cel.Ink with { A = 0.5f });
            }
        }
    }

    private void DrawMaple(float s, int seed)
    {
        var crown = new Vector2(0, -250 * s);
        Cel.Shape(this, [new(-12 * s, 0), new(12 * s, 0), new(6 * s, -200 * s), new(-5 * s, -200 * s)], Cel.Bark, 1.6f);
        DrawLine(new Vector2(0, -150 * s), new Vector2(-60 * s, -230 * s), Cel.Bark, 5 * s, true);
        DrawLine(new Vector2(2, -170 * s), new Vector2(55 * s, -260 * s), Cel.Bark, 5 * s, true);
        Color[] tones = [Color.FromHtml("#B4583A"), Color.FromHtml("#D77F4A"), Color.FromHtml("#EDB067")];
        var blobs = new List<(Vector2 C, float R)>();
        for (var i = 0; i < 8; i++)
        {
            var a = Mathf.Tau * i / 8 + Cel.Rand(seed, i);
            var d = i == 0 ? 0 : 60 + 30 * Cel.Rand(seed, i + 20);
            blobs.Add((crown + new Vector2(Mathf.Cos(a) * d * 1.3f, Mathf.Sin(a) * d * 0.7f) * s, (48 + 20 * Cel.Rand(seed, i + 40)) * s));
        }

        foreach (var (c, r) in blobs) DrawColoredPolygon(Cel.Ellipse(c + new Vector2(6, 10) * s, r, r * 0.85f, 20), tones[0]);
        foreach (var (c, r) in blobs)
        {
            DrawColoredPolygon(Cel.Ellipse(c, r * 0.92f, r * 0.78f, 20), tones[1]);
            DrawColoredPolygon(Cel.Ellipse(c + new Vector2(-r * 0.25f, -r * 0.25f), r * 0.5f, r * 0.38f, 16), tones[2]);
            DrawPolyline(Cel.Ellipse(c + new Vector2(6, 10) * s, r, r * 0.85f, 14, 0.15f, Mathf.Pi - 0.15f), Cel.Ink with { A = 0.55f }, 2, true);
        }
    }
}

/// <summary>山石：不规则的圆顶轮廓，左上受光面、右下背光面、裂纹，顶上可长苔。</summary>
public partial class WildRockNode : WildSprite
{
    private static readonly Color Light = Color.FromHtml("#B7C1B8");
    private static readonly Color Mid = Color.FromHtml("#8E9C94");
    private static readonly Color Dark = Color.FromHtml("#67766F");

    private readonly WildRock _rock;

    public WildRockNode(WildRock rock)
        : base(new Vector2(rock.A, rock.D), new Vector2(rock.W, rock.W) * 0.55f, new Rect2(-rock.W * 0.6f, -rock.H * 1.1f - 20, rock.W * 1.2f, rock.H * 1.1f + 30))
    {
        _rock = rock;
        Occluder = rock.H >= 90;
        UseArt($"wild.rock.{rock.Seed}");
    }

    private bool InWater => Z < WildSamples.Z0 - 1;

    /// <summary>岸上压地影；溪中石改为石根一圈深色水影和外圈涟漪。</summary>
    protected override void DrawShadow()
    {
        if (!InWater)
        {
            Cel.GroundShadow(this, Ground + new Vector2(20, -20), _rock.W * 0.55f, _rock.W * 0.4f, 0.22f, Z);
            return;
        }

        var (rx, ry) = (_rock.W * 0.46f, _rock.W * 0.34f);
        DrawPolyline(Ring(rx * 1.25f, ry * 1.25f, 0, Mathf.Tau), Color.FromHtml("#E4F2EE") with { A = 0.45f }, 1.6f, true);
        DrawColoredPolygon(Ring(rx, ry, 0, Mathf.Tau), Cel.Ink with { A = 0.18f });
    }

    /// <summary>溪中石：石根前半圈碎白沫压在石上，像水漫过石脚。</summary>
    protected override void DrawFront()
    {
        if (!InWater) return;
        var (rx, ry) = (_rock.W * 0.38f, _rock.W * 0.26f);
        var foam = Color.FromHtml("#EAF6F2");
        // 镜头在西南（世界 x 东、y 南，西南为 3π/4）：朝镜头的前半圈是 π/4 … 5π/4。
        DrawPolyline(Ring(rx, ry, Mathf.Pi * 0.25f, Mathf.Pi * 1.25f), foam with { A = 0.85f }, 3.2f, true);
        DrawPolyline(Ring(rx * 1.12f, ry * 1.12f, Mathf.Pi * 0.5f, Mathf.Pi), foam with { A = 0.55f }, 2f, true);
    }

    /// <summary>石根一圈（世界平面上以石心为中心的椭圆弧，投到水面高度）。</summary>
    private Vector2[] Ring(float rx, float ry, float from, float to)
    {
        var n = Mathf.Max(6, Mathf.CeilToInt((to - from) / Mathf.Tau * 28));
        var pts = new Vector2[n + 1];
        for (var i = 0; i <= n; i++)
        {
            var a = from + (to - from) * i / n;
            pts[i] = TownView.P(Ground + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry), Z);
        }

        return pts;
    }

    protected override void DrawSprite()
    {
        var (w, h, seed) = (_rock.W, _rock.H, _rock.Seed);
        var outline = new List<Vector2> { new(-w * 0.5f, 4) };
        const int n = 9;
        for (var i = 0; i <= n; i++)
        {
            var t = (float)i / n;
            var x = (t - 0.5f) * w * (0.95f + 0.1f * Cel.Rand(seed, i));
            var y = -h * Mathf.Pow(Mathf.Sin(Mathf.Pi * (0.06f + 0.88f * t)), 0.6f) * (0.8f + 0.25f * Cel.Rand(seed, i + 20));
            outline.Add(new Vector2(x, y));
        }

        outline.Add(new Vector2(w * 0.5f, 4));
        var shape = outline.ToArray();
        DrawColoredPolygon(shape, Mid);
        // 受光面：左上到一条斜折线；背光面：右侧下半。
        var crest = shape[n / 2 + 1];
        var lit = shape.Take(n / 2 + 2).Append(crest + new Vector2(-w * 0.08f, h * 0.45f)).Append(new Vector2(-w * 0.3f, 2)).ToArray();
        DrawColoredPolygon(lit, Light);
        var dark = shape.Skip(n / 2 + 3).Append(new Vector2(w * 0.1f, 4)).Append(crest + new Vector2(w * 0.12f, h * 0.55f)).ToArray();
        if (dark.Length >= 3) DrawColoredPolygon(dark, Dark);
        if (_rock.Moss)
        {
            var cap = shape.Skip(2).Take(n - 2).Select(p => p + new Vector2(0, 2)).ToList();
            var under = cap.AsEnumerable().Reverse().Select((p, i) => p + new Vector2(0, h * (0.14f + 0.12f * Cel.Rand(seed, i + 60)))).ToList();
            DrawColoredPolygon([.. cap, .. under], Cel.Leaf);
            DrawColoredPolygon([.. cap.Take(cap.Count / 2 + 1), .. under.Skip(under.Count / 2).Select(p => p + new Vector2(0, -h * 0.05f))], Cel.LeafLight);
        }

        DrawLine(crest + new Vector2(-w * 0.05f, h * 0.2f), crest + new Vector2(w * 0.06f, h * 0.6f), Cel.Ink with { A = 0.6f }, 2, true);
        DrawLine(crest + new Vector2(w * 0.06f, h * 0.6f), crest + new Vector2(w * 0.2f, h * 0.7f), Cel.Ink with { A = 0.6f }, 2, true);
        Cel.Outline(this, shape, 2.2f);
    }
}

/// <summary>矮丛、蕨与草药：不挡路也不遮人，只丰富地面层次。</summary>
public partial class WildShrubNode : WildSprite
{
    private readonly WildShrub _shrub;

    public WildShrubNode(WildShrub shrub)
        : base(new Vector2(shrub.A, shrub.D), new Vector2(40, 40), new Rect2(-90 * shrub.Size, -110 * shrub.Size, 180 * shrub.Size, 120 * shrub.Size))
    {
        _shrub = shrub;
        Occluder = false;
        UseArt($"wild.shrub.{shrub.Seed}");
    }

    protected override void DrawSprite()
    {
        var (s, seed) = (_shrub.Size, _shrub.Seed);
        if (_shrub.Kind == ShrubKind.Fern)
        {
            for (var i = 0; i < 9; i++)
            {
                var a = Mathf.Pi * (1.08f + 0.84f * i / 8);
                var len = (60 + 30 * Cel.Rand(seed, i)) * s;
                var tip = new Vector2(Mathf.Cos(a) * len, Mathf.Sin(a) * len * 0.75f - 10 * s);
                Vector2[] frond = [new(0, 0), tip * 0.5f + new Vector2(0, -18 * s), tip];
                DrawPolyline(frond, i % 2 == 0 ? Cel.LeafDark : Cel.Leaf, 3.5f * s, true);
                for (var k = 1; k < 6; k++)
                {
                    var p = frond[0].Lerp(frond[1], k / 6f).Lerp(frond[1].Lerp(frond[2], k / 6f), k / 6f);
                    DrawLine(p, p + new Vector2(0, 9 * s), Cel.LeafLight, 2.5f * s, true);
                }
            }

            return;
        }

        var tones = _shrub.Kind == ShrubKind.Herb
            ? new[] { Color.FromHtml("#3F7A55"), Color.FromHtml("#5E9A66"), Color.FromHtml("#8DC084") }
            : new[] { Cel.LeafDark, Cel.Leaf, Cel.LeafLight };
        var blobs = new[] { (new Vector2(-30, -26), 38f), (new Vector2(26, -24), 34f), (new Vector2(-2, -46), 36f) };
        foreach (var (c, r) in blobs) DrawColoredPolygon(Cel.Ellipse(c * s + new Vector2(4, 6) * s, r * s, r * 0.8f * s, 18), tones[0]);
        foreach (var (c, r) in blobs)
        {
            DrawColoredPolygon(Cel.Ellipse(c * s, r * 0.9f * s, r * 0.72f * s, 18), tones[1]);
            DrawColoredPolygon(Cel.Ellipse((c + new Vector2(-r * 0.25f, -r * 0.25f)) * s, r * 0.5f * s, r * 0.35f * s, 14), tones[2]);
        }

        DrawPolyline(Cel.Ellipse(new Vector2(0, -22) * s, 66 * s, 30 * s, 16, 0.25f, Mathf.Pi - 0.25f), Cel.Ink with { A = 0.5f }, 2, true);
        if (_shrub.Kind == ShrubKind.Herb)
        {
            for (var i = 0; i < 9; i++)
            {
                var p = new Vector2((Cel.Rand(seed, i) - 0.5f) * 110, -20 - Cel.Rand(seed, i + 9) * 50) * s;
                var petal = i % 3 == 0 ? Color.FromHtml("#D9C8E8") : Color.FromHtml("#F4F2E8");
                for (var k = 0; k < 5; k++)
                {
                    var a = Mathf.Tau * k / 5;
                    DrawCircle(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 4 * s, 3.2f * s, petal);
                }

                DrawCircle(p, 2.2f * s, UiPalette.Gilt);
            }
        }
    }
}

/// <summary>半山茶亭：四柱方亭、攒尖顶、檐下匾额，北、东两面坐凳，当中石桌石凳。拆成屋顶（压在亭下行人之上）与柱、凳、桌各件。</summary>
public partial class WildPavilionPart : TownPiece
{
    private static readonly Color Lacquer = Color.FromHtml("#7C3B2D");

    private readonly List<(Vector3 From, Vector3 To)> _ridges = [];
    private readonly List<(Vector3 Eave, Vector3 Apex)> _rows = [];
    private Vector3 _apex;
    private bool _roof;

    public static IEnumerable<WildPavilionPart> Build()
    {
        var c = WildSamples.Pavilion;
        var h = WildSamples.PavilionHalf;
        var z = WildLayout.GroundZ(c);
        const float eave = 240;
        const float apex = 380;
        const float over = 55;
        var foot = new Rect2(c - new Vector2(h, h), new Vector2(h * 2, h * 2));

        var roof = new WildPavilionPart { Overhead = true, _roof = true, Part = "roof" };
        roof._apex = new Vector3(c.X, c.Y, z + apex);
        Vector3[] corners =
        [
            new(c.X - h - over, c.Y + h + over, z + eave), new(c.X + h + over, c.Y + h + over, z + eave),
            new(c.X + h + over, c.Y - h - over, z + eave), new(c.X - h - over, c.Y - h - over, z + eave),
        ];
        for (var i = 0; i < 4; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % 4];
            var normal = (b - a).Cross(roof._apex - a).Normalized();
            if (normal.Z < 0) normal = -normal;
            roof.Faces.Add(new Face { Points = [a, b, roof._apex], Normal = normal, Color = Cel.Tile, Outline = 2 });
            roof._ridges.Add((a, roof._apex));
            for (var t = 1; t < 8; t++)
            {
                roof._rows.Add((a.Lerp(b, t / 8f), roof._apex));
            }
        }

        Solid.Sort(roof.Faces);
        // 南面檐下横枋与匾额“半山亭”（压在屋面之前画，屋面之下）。
        var beam = Solid.Box(new Vector3(c.X - h, c.Y + h - 20, z + eave - 36), new Vector3(c.X + h, c.Y + h - 8, z + eave - 8), Lacquer, 1.4f);
        roof.Faces.InsertRange(0, beam);
        roof.Faces.Insert(beam.Count, new Face
        {
            Points = [new(c.X - 60, c.Y + h - 7, z + eave - 6), new(c.X + 60, c.Y + h - 7, z + eave - 6),
                new(c.X + 60, c.Y + h - 7, z + eave - 44), new(c.X - 60, c.Y + h - 7, z + eave - 44)],
            Normal = new Vector3(0, 1, 0), Color = Color.FromHtml("#2E3A46"), Outline = 1.6f, Lit = false,
            Local = TownView.Local(new Vector3(c.X - 60, c.Y + h - 7, z + eave - 6), Vector3.Right, TownView.Below),
            Lettering = ci => ci.DrawString(UiFonts.Title, new Vector2(18, 30), "半山亭", HorizontalAlignment.Left, -1, 26, UiPalette.Gilt),
        });
        roof.Seal(foot);
        roof.UseArt($"wild.pavilion.{roof.Part}");
        yield return roof;

        foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            var p = c + new Vector2(sx * (h - 16), sy * (h - 16));
            var pillar = new WildPavilionPart { Occluder = false, Part = $"pillar_{(sy < 0 ? "n" : "s")}{(sx < 0 ? "w" : "e")}" };
            pillar.Faces.AddRange(Solid.Box(new Vector3(p.X - 18, p.Y - 18, z), new Vector3(p.X + 18, p.Y + 18, z + 14), Cel.Stone, 1.4f));
            pillar.Faces.AddRange(Solid.Box(new Vector3(p.X - 11, p.Y - 11, z + 14), new Vector3(p.X + 11, p.Y + 11, z + eave - 8), Lacquer, 1.6f));
            pillar.Seal(new Rect2(p - new Vector2(18, 18), new Vector2(36, 36)));
            pillar.UseArt($"wild.pavilion.{pillar.Part}");
            yield return pillar;
        }

        // 北、东两面坐凳（背镜头的两侧），留出南、西两面进出。
        foreach (var (name, min, max) in new[]
                 {
                     ("bench_n", new Vector2(c.X - h + 30, c.Y - h + 8), new Vector2(c.X + h - 30, c.Y - h + 40)),
                     ("bench_e", new Vector2(c.X + h - 40, c.Y - h + 30), new Vector2(c.X + h - 8, c.Y + h - 30)),
                 })
        {
            var bench = new WildPavilionPart { Occluder = false, Part = name };
            bench.Faces.AddRange(Solid.Box(new Vector3(min.X, min.Y, z), new Vector3(max.X, max.Y, z + 44), Cel.WoodLight, 1.4f));
            bench.Seal(new Rect2(min, max - min));
            bench.UseArt($"wild.pavilion.{bench.Part}");
            yield return bench;
        }

        var table = new WildPavilionPart { Occluder = false, Part = "table" };
        table.Faces.AddRange(Solid.ExtrudeZ(Cel.Ellipse(c, 34, 34, 12), z, z + 72, Cel.Stone, Cel.StoneLight, 1.6f));
        foreach (var off in new[] { new Vector2(-62, 30), new Vector2(40, 58) })
        {
            table.Faces.AddRange(Solid.ExtrudeZ(Cel.Ellipse(c + off, 16, 16, 10), z, z + 40, Cel.Stone, Cel.StoneLight, 1.4f));
        }

        Solid.Sort(table.Faces);
        table.Seal(new Rect2(c - new Vector2(80, 50), new Vector2(140, 130)));
        table.UseArt($"wild.pavilion.{table.Part}");
        yield return table;
    }

    protected override void DrawExtras()
    {
        if (!_roof)
        {
            return;
        }

        foreach (var (eave, apex) in _rows)
        {
            DrawLine(TownView.P(eave), TownView.P(eave.Lerp(apex, 0.88f)), Cel.TileRow with { A = 0.55f }, 2f, true);
        }

        foreach (var (from, to) in _ridges)
        {
            DrawLine(TownView.P(from), TownView.P(to), Cel.TileDark, 5f, true);
            // 翼角起翘。
            var outward = new Vector3(from.X - _apex.X, from.Y - _apex.Y, 0).Normalized();
            DrawLine(TownView.P(from), TownView.P(from + outward * 26 + TownView.Above * 26), Cel.TileDark, 5f, true);
        }

        var top = TownView.P(_apex);
        DrawCircle(top + new Vector2(0, -10), 9, UiPalette.Gilt.Darkened(0.2f));
        DrawCircle(top + new Vector2(0, -24), 6, UiPalette.Gilt.Darkened(0.2f));
        DrawArc(top + new Vector2(0, -10), 9, 0, Mathf.Tau, 16, Cel.Ink, 1.6f, true);
    }
}

/// <summary>山门石牌坊：两柱、抱鼓石、上下额枋、匾额“山门”、瓦顶；拆成两柱（挡路）与额枋瓦顶（压在门下行人之上）。</summary>
public partial class WildGatePart : TownPiece
{
    public static IEnumerable<WildGatePart> Build()
    {
        var g = WildSamples.Gate;
        var half = WildSamples.GateSpan / 2;
        var z = WildLayout.GroundZ(g);
        var stone = Color.FromHtml("#A7B1AA");

        foreach (var sx in new[] { -1, 1 })
        {
            var x = g.X + sx * half;
            var pillar = new WildGatePart { Part = sx < 0 ? "pillar_w" : "pillar_e" };
            pillar.Faces.AddRange(Solid.Box(new Vector3(x - 14, g.Y - 52, z), new Vector3(x + 14, g.Y + 52, z + 70), Cel.Stone, 1.4f));
            pillar.Faces.AddRange(Solid.Box(new Vector3(x - 22, g.Y - 22, z), new Vector3(x + 22, g.Y + 22, z + 330), stone, 1.8f));
            Solid.Sort(pillar.Faces);
            pillar.Seal(new Rect2(x - 22, g.Y - 52, 44, 104));
            pillar.UseArt($"wild.gate.{pillar.Part}");
            yield return pillar;
        }

        var top = new WildGatePart { Overhead = true, Part = "top" };
        top.Faces.AddRange(Solid.Box(new Vector3(g.X - half - 34, g.Y - 14, z + 228), new Vector3(g.X + half + 34, g.Y + 14, z + 256), stone, 1.6f));
        top.Faces.AddRange(Solid.Box(new Vector3(g.X - half - 40, g.Y - 16, z + 302), new Vector3(g.X + half + 40, g.Y + 16, z + 330), stone, 1.6f));
        top.Faces.Add(new Face
        {
            Points = [new(g.X - 80, g.Y + 15, z + 300), new(g.X + 80, g.Y + 15, z + 300), new(g.X + 80, g.Y + 15, z + 258), new(g.X - 80, g.Y + 15, z + 258)],
            Normal = new Vector3(0, 1, 0), Color = Color.FromHtml("#2E3A46"), Outline = 1.8f, Lit = false,
            Local = TownView.Local(new Vector3(g.X - 80, g.Y + 15, z + 300), Vector3.Right, TownView.Below),
            Decal = ci => ci.DrawRect(new Rect2(4, 4, 152, 34), UiPalette.Gilt with { A = 0.5f }, false, 1.5f),
            Lettering = ci => ci.DrawString(UiFonts.Title, new Vector2(42, 32), "山　门", HorizontalAlignment.Left, -1, 28, UiPalette.Gilt),
        });
        top.Faces.AddRange(Solid.ExtrudeX([new(g.Y - 46, z + 330), new(g.Y + 46, z + 330), new(g.Y, z + 368)], g.X - half - 70, g.X + half + 70, Cel.Tile, 1.8f));
        top.Faces.AddRange(Solid.Box(new Vector3(g.X - half - 60, g.Y - 8, z + 362), new Vector3(g.X + half + 60, g.Y + 8, z + 376), Cel.TileDark, 1.4f));
        top.Seal(new Rect2(g.X - half - 70, g.Y - 46, WildSamples.GateSpan + 140, 92));
        top.UseArt($"wild.gate.{top.Part}");
        yield return top;
    }
}

/// <summary>路碑与岔路木牌。</summary>
public partial class WildMarkerNode : TownPiece
{
    public static WildMarkerNode Stele()
    {
        var p = WildSamples.W(WildSamples.Stele);
        var z = WildLayout.GroundZ(p);
        var node = new WildMarkerNode();
        node.Faces.AddRange(Solid.Box(new Vector3(p.X - 50, p.Y - 22, z), new Vector3(p.X + 50, p.Y + 22, z + 22), Cel.Stone, 1.6f));
        node.Faces.AddRange(Solid.Box(new Vector3(p.X - 34, p.Y - 9, z + 22), new Vector3(p.X + 34, p.Y + 9, z + 172), Color.FromHtml("#A5B0A8"), 1.8f));
        node.Faces.Add(new Face
        {
            Points = [new(p.X - 34, p.Y + 9.5f, z + 172), new(p.X + 34, p.Y + 9.5f, z + 172), new(p.X + 34, p.Y + 9.5f, z + 22), new(p.X - 34, p.Y + 9.5f, z + 22)],
            Normal = new Vector3(0, 1, 0), Color = Color.FromHtml("#A5B0A8"), Outline = 1.8f,
            Local = TownView.Local(new Vector3(p.X - 34, p.Y + 9.5f, z + 172), Vector3.Right, TownView.Below),
            Lettering = ci =>
            {
                for (var i = 0; i < 3; i++)
                {
                    ci.DrawString(UiFonts.Title, new Vector2(18, 40 + i * 36), "山门路"[i].ToString(), HorizontalAlignment.Left, -1, 30, Cel.Ink);
                }
            },
        });
        node.Faces.AddRange(Solid.ExtrudeX([new(p.Y - 14, z + 172), new(p.Y + 14, z + 172), new(p.Y, z + 188)], p.X - 40, p.X + 40, Cel.Stone, 1.4f));
        node.Seal(new Rect2(p - new Vector2(50, 22), new Vector2(100, 44)));
        node.UseArt("wild.stele");
        return node;
    }

    public static WildMarkerNode Signpost()
    {
        var p = WildSamples.W(WildSamples.Signpost);
        var z = WildLayout.GroundZ(p);
        var node = new WildMarkerNode { Occluder = false };
        node.Faces.AddRange(Solid.Box(new Vector3(p.X - 7, p.Y - 7, z), new Vector3(p.X + 7, p.Y + 7, z + 220), Cel.WoodDark, 1.4f));
        // 东指药庐（板面朝南），北指山门（板面朝西）。
        node.Faces.AddRange(Solid.ExtrudeZ([new(p.X + 7, p.Y - 5), new(p.X + 110, p.Y - 5), new(p.X + 128, p.Y), new(p.X + 110, p.Y + 5), new(p.X + 7, p.Y + 5)],
            z + 168, z + 200, Cel.WoodLight, Cel.WoodLight, 1.4f));
        node.Faces.Add(Sign(new Vector3(p.X + 12, p.Y + 5.5f, z + 199), Vector3.Right, "药庐"));
        node.Faces.AddRange(Solid.ExtrudeZ([new(p.X - 5, p.Y - 7), new(p.X - 5, p.Y - 110), new(p.X, p.Y - 128), new(p.X + 5, p.Y - 110), new(p.X + 5, p.Y - 7)],
            z + 130, z + 162, Cel.WoodLight, Cel.WoodLight, 1.4f));
        node.Faces.Add(Sign(new Vector3(p.X - 5.5f, p.Y - 108, z + 161), new Vector3(0, 1, 0), "山门"));
        node.Seal(new Rect2(p - new Vector2(10, 130), new Vector2(140, 140)));
        node.UseArt("wild.signpost");
        return node;
    }

    private static Face Sign(Vector3 origin, Vector3 u, string text)
    {
        var normal = u.Cross(TownView.Below);
        if (!TownView.Facing(normal)) normal = -normal;
        return new Face
        {
            Points = [origin, origin + u * 96, origin + u * 96 + TownView.Below * 30, origin + TownView.Below * 30],
            Normal = normal, Color = Cel.WoodLight, Outline = 0, Lit = false,
            Local = TownView.Local(origin, u, TownView.Below),
            Lettering = ci => ci.DrawString(UiFonts.Title, new Vector2(18, 24), text, HorizontalAlignment.Left, -1, 22, Cel.Ink),
        };
    }
}

/// <summary>小地图：山道平面（上北），三层台地由低到高逐层提亮，崖边墨线、溪涧、山道、茶亭与山门；石青箭头为主角、泥金菱形为山门。</summary>
public partial class WildMiniMap : Control
{
    private const float Window = 2600;

    public Func<(Vector2 Position, Vector2 Heading)> Hero { get; init; } = () => (Vector2.Zero, Vector2.Right);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
    }

    public override void _Draw()
    {
        var (hero, heading) = Hero();
        var k = Size.X / Window;
        var origin = hero - new Vector2(Window, Window) / 2;
        DrawRect(new Rect2(Vector2.Zero, Size), UiPalette.Abyss);
        DrawSetTransform(-origin * k, 0, new Vector2(k, k));

        Vector2[] W(IEnumerable<Vector2> ad) => ad.Select(WildSamples.W).ToArray();
        float Far(float _) => -2400;
        float Near(float _) => 2400;
        DrawColoredPolygon(W(WildLayout.Band(WildSamples.Edge1, Near)), Color.FromHtml("#2F4A45"));
        DrawColoredPolygon(W(WildLayout.Band(WildSamples.Edge2, WildSamples.Edge1)), Color.FromHtml("#3D5E56"));
        DrawColoredPolygon(W(WildLayout.Band(Far, WildSamples.Edge2)), Color.FromHtml("#4E7267"));
        DrawColoredPolygon(W(WildLayout.Band(WildSamples.StreamNorth, WildSamples.StreamSouth)), Color.FromHtml("#3F7F8A"));
        foreach (var s in WildLayout.Stairs)
        {
            DrawColoredPolygon(W([new(s.A0, s.D0), new(s.A1, s.D0), new(s.A1, s.D1), new(s.A0, s.D1)]), UiPalette.TextMuted);
        }

        foreach (var edge in new Func<float, float>[] { WildSamples.Edge1, WildSamples.Edge2 })
        {
            DrawPolyline(W(WildLayout.Edge(edge, WildLayout.FarA0, WildLayout.FarA1)), Cel.Ink, 3 / k, true);
        }

        foreach (var path in new[] { WildSamples.PathSouth, WildSamples.PathNorth, WildSamples.PathMid, WildSamples.PathTop, WildSamples.PathFork })
        {
            DrawPolyline(W(path), UiPalette.Text with { A = 0.55f }, 2.5f / k, true);
        }

        DrawPolyline(W([new(WildSamples.BridgeA0 + 50, WildSamples.BridgeD0), new(WildSamples.BridgeA0 + 50, WildSamples.BridgeD1)]), Cel.WoodLight, 4 / k, true);
        var pav = WildSamples.Pavilion;
        var h = WildSamples.PavilionHalf;
        DrawRect(new Rect2(pav - new Vector2(h, h), new Vector2(h * 2, h * 2)), UiPalette.TextMuted with { A = 0.8f });
        DrawLine(WildSamples.Gate - new Vector2(WildSamples.GateSpan / 2, 0), WildSamples.Gate + new Vector2(WildSamples.GateSpan / 2, 0), UiPalette.Text, 4 / k, true);

        var g = WildSamples.Goal;
        var r = 9 / k;
        DrawColoredPolygon([g + new Vector2(0, -r), g + new Vector2(r, 0), g + new Vector2(0, r), g + new Vector2(-r, 0)], UiPalette.Gilt.Darkened(0.1f));
        var a = 11 / k;
        var f = heading.Normalized();
        var side = new Vector2(-f.Y, f.X);
        DrawColoredPolygon([hero + f * a * 1.2f, hero - f * a * 0.8f + side * a * 0.8f, hero - f * a * 0.3f, hero - f * a * 0.8f - side * a * 0.8f], UiPalette.Accent);
        DrawArc(hero, 18 / k, 0, Mathf.Tau, 32, UiPalette.Accent with { A = 0.5f }, 1.5f / k, true);

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        DrawString(UiFonts.Title, new Vector2(Size.X - 30, 28), "北", HorizontalAlignment.Left, -1, 20, UiPalette.Text);
    }
}

/// <summary>
/// AI 草丛精灵（wild.tuft.grass.1–4、wild.tuft.bush.1，2026-09-26 替换崖沿、岸沿与阶脚的直线草叶和椭圆矮灌占位）：
/// 按脚底中心与屏幕高度画出，可左右翻转；variant 为 -1 时画矮灌。
/// </summary>
/// <summary>
/// 溪涧南岸（近镜头一侧）：岸边立面背向镜头看不见，在南岸地面贴边铺一窄条沙滩（溪床纹理 wild.ground.streambed，向草坡渐隐），
/// 水边勾一道淡墨线与白沫，滩外沿错落几丛草。画在南岸地面之后、山道之前。
/// </summary>
public partial class WildShore : Node2D
{
    private const float Width = 34;
    private const float Seg = 20;
    private const int Seed = 41;

    private static readonly (Texture2D Texture, float WorldSize)? SandTexture = PieceArt.FindTexture("wild.ground.streambed");

    private static Vector2 S(float a, float d) => WildLayout.S(a, d, WildSamples.Z0);

    private static float Edge(float a) => WildSamples.StreamSouth(a);

    /// <summary>滩宽随位置起伏，间或收窄到只剩水边一线。</summary>
    private static float Beach(float a) => Width * Mathf.Clamp(0.55f + 0.6f * Mathf.Sin(a / 170 + 2) + 0.25f * Mathf.Sin(a / 57), 0.15f, 1);

    public override void _Ready()
    {
        TextureRepeat = TextureRepeatEnum.Enabled;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    public override void _Draw()
    {
        var (a0, a1) = (WildLayout.FarA0, WildLayout.FarA1);
        if (SandTexture is { } tex)
        {
            var solid = new Color(0.8f, 0.84f, 0.8f);
            for (var a = a0; a < a1; a += Seg)
            {
                var b = a + Seg;
                var (da, db) = (Edge(a), Edge(b));
                var (wa, wb) = (Beach(a), Beach(b));
                // 按画面坐标 (A, D) 平铺沙纹，滩外沿淡出到草坡。
                Vector2 Uv(float x, float d) => new Vector2(x, d) / tex.WorldSize;
                DrawPolygon([S(a, da), S(b, db), S(b, db + wb), S(a, da + wa)],
                    [solid, solid, solid with { A = 0 }, solid with { A = 0 }],
                    [Uv(a, da), Uv(b, db), Uv(b, db + wb), Uv(a, da + wa)], tex.Texture);
            }
        }

        var edge = new List<Vector2>();
        for (var a = a0; a <= a1; a += Seg) edge.Add(S(a, Edge(a)));
        DrawPolyline(edge.ToArray(), Cel.Ink with { A = 0.4f }, 1.8f, true);

        if (!Tufts.Ready) return;
        for (var a = WildSamples.WalkA0 - 400; a < WildSamples.WalkA1 + 400; a += 30 + 50 * Cel.Rand(Seed, (int)a + 5))
        {
            var r = Cel.Rand(Seed, (int)a + 77);
            if (r < 0.35f || a > WildSamples.BridgeA0 - 60 && a < WildSamples.BridgeA1 + 60) continue;
            Tufts.Draw(this, S(a, Edge(a) + Beach(a) * 0.8f + 4), 22 + 16 * r, (int)(Cel.Rand(Seed, (int)a + 13) * 4), Cel.Rand(Seed, (int)a + 17) > 0.5f);
        }
    }
}

internal static class Tufts
{
    private static readonly Texture2D[] Grass = Load("grass", 4);
    private static readonly Texture2D[] Bush = Load("bush", 1);

    public static bool Ready => PieceArt.Enabled && Grass.Length > 0;

    private static Texture2D[] Load(string kind, int count)
    {
        var list = new List<Texture2D>();
        for (var i = 1; i <= count; i++)
        {
            var path = $"res://assets/art/wild/wild.tuft.{kind}.{i}.png";
            if (ResourceLoader.Exists(path)) list.Add(GD.Load<Texture2D>(path));
        }

        return list.ToArray();
    }

    public static void Draw(CanvasItem ci, Vector2 foot, float height, int variant, bool flip)
    {
        var tex = variant < 0 && Bush.Length > 0 ? Bush[0] : Grass[Mathf.PosMod(variant, Grass.Length)];
        var size = tex.GetSize() * (height / tex.GetSize().Y);
        ci.DrawSetTransform(foot, 0, new Vector2(flip ? -1 : 1, 1));
        ci.DrawTextureRect(tex, new Rect2(-size.X / 2, -size.Y, size.X, size.Y), false);
        ci.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
