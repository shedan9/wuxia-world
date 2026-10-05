using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.Art;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 河岸布局的派生几何：某处高度、能否站人、芦苇丛的分布。地面、碰撞、排序与小地图共用一份。
/// 坐标为画面坐标 (A, D)，经 <see cref="RiverSamples.W"/> 换成世界平面坐标；见 <see cref="RiverSamples"/>。
/// 碰撞全部在这里按画面坐标判定（芦苇、桥墩、渠岸），不用物件占地：沿画面轴摆的大件换成世界坐标的外接矩形会大出一圈。
/// </summary>
public sealed class RiverGeo(RiverSamples s)
{
    /// <summary>离水线、堤边、渠岸留出的余量（脚的半径以外）。</summary>
    public const float Margin = 22;

    private const float Curb = 18;

    public RiverSamples Site { get; } = s;

    public static Vector2 S(float a, float d, float z) => TownView.P(RiverSamples.W(a, d), z);

    public static Vector2 S(Vector2 ad, float z) => S(ad.X, ad.Y, z);

    /// <summary>一片芦苇里各丛的位置、高度与精灵（按种子固定），布景与碰撞共用。</summary>
    public static IEnumerable<(Vector2 Ad, float Height, int Art, bool Flip)> Clumps(ReedBed bed)
    {
        for (var i = 0; i < bed.Count; i++)
        {
            var t = bed.Count == 1 ? 0.5f : (float)i / (bed.Count - 1);
            var a = bed.A - bed.Width / 2 + bed.Width * t + (Cel.Rand(bed.Seed, i) - 0.5f) * bed.Width / bed.Count * 0.8f;
            var d = bed.D + (Cel.Rand(bed.Seed, i + 50) - 0.5f) * bed.Depth;
            var h = bed.Height * (0.78f + 0.4f * Cel.Rand(bed.Seed, i + 100));
            yield return (new Vector2(a, d), h, bed.Arts[i % bed.Arts.Length], Cel.Rand(bed.Seed, i + 150) > 0.5f);
        }
    }

    public bool OnJetty(Vector2 world) => Site.Jetty is { } j && j.Deck.HasPoint(world);

    public bool OnJetty(float a, float d) => OnJetty(RiverSamples.W(a, d));

    private bool OnSteps(float a, float d) =>
        Site.Levee is { Steps: var st } && a >= st.A0 && a <= st.A1 && d >= st.D0 && d <= st.D1;

    public bool OnLevee(float a, float d) => Site.Levee is { } l && a >= l.A0 && d >= l.Back && d <= l.Front;

    public bool InChannel(float a, float d) => Site.Levee is { } l && a > l.GateA0 && a < l.GateA1 && d > l.Front;

    /// <summary>某处地面的高度：石阶上按远近连续升高，栈桥面、堤顶、渠面与江面各有高度。</summary>
    public float GroundZ(Vector2 world)
    {
        var f = WildSamples.Frame(world);
        var (a, d) = (f.X, f.Y);
        if (Site.Levee is { } l)
        {
            var st = l.Steps;
            if (OnSteps(a, d))
            {
                return st.Low + (st.High - st.Low) * (st.D1 - d) / (st.D1 - st.D0);
            }

            if (OnLevee(a, d)) return l.Z;
            if (InChannel(a, d)) return l.ChannelZ;
        }

        if (Site.Jetty is { } j && OnJetty(world)) return j.Z;
        return d < Site.Shore.At(a) ? RiverSamples.WaterZ : 0;
    }

    public bool InWalkArea(Vector2 world)
    {
        var f = WildSamples.Frame(world);
        var (a, d) = (f.X, f.Y);
        if (a < Site.WalkA0 || a > Site.WalkA1 || d > Site.Front)
        {
            return false;
        }

        if (InReeds(a, d))
        {
            return false;
        }

        if (Site.Jetty is { } j && j.Deck.Grow(-14).HasPoint(world) && world.X < j.Deck.End.X - 30)
        {
            return true;
        }

        if (Site.Levee is { } l)
        {
            var st = l.Steps;
            // 阶面（两侧石栏不能走），阶顶多留一截接上堤顶。
            if (a > st.A0 + Curb && a < st.A1 - Curb && d >= st.D0 - Margin - 6 && d < st.D1 + Margin)
            {
                return true;
            }

            if (a > st.A0 - Margin && a < st.A1 + Margin && d >= st.D0 && d < st.D1 + Margin)
            {
                return false;
            }

            if (a >= l.A0 - Margin)
            {
                if (d < l.Back + 30) return false;
                if (d <= l.Front + Margin)
                {
                    // 堤顶：西端、前沿留边；两座闸墩不能走（门洞上方是石板闸桥，可走）。
                    if (a < l.A0 + Margin || d > l.Front - Margin) return false;
                    if (a > l.GateA0 - 44 && a < l.GateA0 + 6 || a > l.GateA1 - 6 && a < l.GateA1 + 44) return false;
                    // 门洞上只有闸桥（堤前一侧），桥后是石栏与闸板。
                    return a <= l.GateA0 || a >= l.GateA1 || d > l.Front - RiverGatePart.DeckDepth + 8;
                }

                // 堤前低地：渠两岸留边；水门以东是苇荡。
                if (a > l.GateA0 - Margin - 10 && a < l.GateA1 + Margin + 10) return false;
                return a < l.MarshA;
            }
        }

        return d >= Site.Shore.At(a) + Margin;
    }

    /// <summary>挡路的芦苇丛：每丛脚下一个扁椭圆。</summary>
    private bool InReeds(float a, float d)
    {
        foreach (var bed in Site.Reeds)
        {
            if (!bed.Blocks || Mathf.Abs(a - bed.A) > bed.Width / 2 + 120 || Mathf.Abs(d - bed.D) > bed.Depth / 2 + 80)
            {
                continue;
            }

            foreach (var (ad, h, _, _) in Clumps(bed))
            {
                var rx = 34 + h * 0.18f;
                var ry = rx * 0.6f;
                var (dx, dy) = ((a - ad.X) / rx, (d - ad.Y) / ry);
                if (dx * dx + dy * dy < 1)
                {
                    return true;
                }
            }
        }

        return false;
    }
}

/// <summary>一丛芦苇（AI 精灵 river.reed.N，脚底对齐地面）：浅水里的脚下一圈水影与碎沫，岸上压一小片地影；高的挡住身后行人时淡出。</summary>
public partial class RiverReedNode : TownPiece
{
    private static readonly Dictionary<int, (Texture2D Texture, Vector2 Foot)?> Sprites = [];

    private readonly (Texture2D Texture, Vector2 Foot)? _sprite;
    private readonly Vector2 _ground;
    private readonly float _z;
    private readonly float _height;
    private readonly bool _flip;
    private readonly bool _wet;

    public RiverReedNode(Vector2 ad, float z, float height, int art, bool flip, bool wet)
    {
        _ground = RiverSamples.W(ad.X, ad.Y);
        _z = z;
        _height = height;
        _flip = flip;
        _wet = wet;
        _sprite = Load(art);
        Position = TownView.P(_ground, z);
        Foot = new Rect2(_ground - new Vector2(16, 16), new Vector2(32, 32));
        var h = height * TownView.Upright;
        var w = _sprite is { } s ? h * s.Texture.GetWidth() / s.Texture.GetHeight() : h * 0.8f;
        ScreenBox = new Rect2(Position - new Vector2(w / 2, h), new Vector2(w, h + 6));
        Occluder = height > 170;
    }

    private static (Texture2D, Vector2)? Load(int art)
    {
        if (Sprites.TryGetValue(art, out var cached))
        {
            return cached;
        }

        var path = $"res://assets/art/river/river.reed.{art}";
        (Texture2D, Vector2)? sprite = null;
        if (PieceArt.Enabled && ResourceLoader.Exists(path + ".png") && Godot.FileAccess.FileExists(path + ".json"))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(Godot.FileAccess.GetFileAsString(path + ".json"));
            var foot = doc.RootElement.GetProperty("foot");
            sprite = (GD.Load<Texture2D>(path + ".png"), new Vector2(foot[0].GetSingle(), foot[1].GetSingle()));
        }

        Sprites[art] = sprite;
        return sprite;
    }

    public override void _Ready() => TextureFilter = TextureFilterEnum.LinearWithMipmaps;

    public override void _Draw()
    {
        DrawSetTransform(-Position, 0, Vector2.One);
        if (_wet)
        {
            DrawColoredPolygon(Ring(_height * 0.22f, _height * 0.14f), Cel.Ink with { A = 0.14f });
        }
        else
        {
            Cel.GroundShadow(this, _ground + new Vector2(18, -18), _height * 0.3f, _height * 0.2f, 0.14f, _z);
        }

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        if (_sprite is not { } s)
        {
            // 未入库时画几笔竖叶占位。
            for (var i = 0; i < 9; i++)
            {
                var x = (i - 4) * 7f;
                DrawLine(new Vector2(x, 0), new Vector2(x * 1.6f, -_height * TownView.Upright * (0.6f + 0.4f * Cel.Rand(i, 3))), Cel.Leaf, 3, true);
            }

            return;
        }

        var h = _height * TownView.Upright;
        var k = h / s.Foot.Y;
        var size = s.Texture.GetSize() * k;
        var rect = new Rect2(-s.Foot.X * k, -s.Foot.Y * k, size.X, size.Y);
        // 翻转用变换（以脚底为轴）：负宽度矩形不是原地镜像，画出的位置会偏离排序外框。
        DrawSetTransform(Vector2.Zero, 0, new Vector2(_flip ? -1 : 1, 1));
        // 芦苇调到画面的青绿基调：AI 精灵略偏灰绿，压一点暖度。
        DrawTextureRect(s.Texture, rect, false, _wet ? new Color(0.9f, 0.97f, 0.97f) : new Color(0.97f, 1f, 0.97f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        if (_wet)
        {
            // 水没过苇根：根部压一道水色。
            DrawSetTransform(-Position, 0, Vector2.One);
            var front = Ring(_height * 0.2f, _height * 0.11f);
            DrawPolyline(front.Skip(3).Take(7).ToArray(), Color.FromHtml("#EAF6F2") with { A = 0.55f }, 1.8f, true);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }

    /// <summary>苇根一圈（世界平面椭圆，投到水面）。</summary>
    private Vector2[] Ring(float rx, float ry)
    {
        var pts = new Vector2[20];
        for (var i = 0; i < pts.Length; i++)
        {
            var t = Mathf.Tau * i / pts.Length;
            pts[i] = TownView.P(_ground + new Vector2(Mathf.Cos(t) * rx, Mathf.Sin(t) * ry), _z);
        }

        return pts;
    }
}

/// <summary>
/// 借来的 AI 出件（城镇柳树、山石、船、刻痕石；客栈后院另借井台、水缸、樟树与大堂盆栽）：出件按原布景的投影位置对齐，
/// 这里挪到新位置，可缩放、左右翻转。原点在脚底投影；船随水轻晃，水里的件脚下压一圈暗影与碎沫。
/// </summary>
public partial class RiverArtNode : TownPiece
{
    private readonly Vector2 _authored;
    private readonly float _scale;
    private readonly bool _flip;
    private readonly Vector2 _ground;
    private readonly float _z;
    private readonly Vector2 _base;
    private readonly Vector2 _shadow;
    private readonly bool _inWater;

    public RiverArtNode(string artId, Vector2 authored, Vector2 ground, float z, float scale, bool flip, Vector2 foot, Vector2 shadow, bool inWater = false)
    {
        _authored = authored;
        _scale = scale;
        _flip = flip;
        _ground = ground;
        _z = z;
        _shadow = shadow;
        _inWater = inWater;
        Position = TownView.P(ground, z);
        _base = Position;
        Foot = new Rect2(ground - foot / 2, foot);
        UseArt(artId);
        if (Art is { } art)
        {
            var rel = (art.Origin - authored) * scale;
            var size = art.Frame.Size * scale;
            ScreenBox = new Rect2(Position + new Vector2(flip ? -rel.X - size.X : rel.X, rel.Y), size);
        }
        else
        {
            ScreenBox = new Rect2(Position - new Vector2(60, 120), new Vector2(120, 120));
        }
    }

    /// <summary>某件出件在原布景里的脚底投影（贴回时以此为锚）。</summary>
    public static Vector2 Anchor(string artId)
    {
        var parts = artId.Split('.');
        if (artId.StartsWith("town.tree.", StringComparison.Ordinal) && int.TryParse(parts[^1], out var tree))
        {
            return TownView.P(TownSamples.Trees.First(t => t.Seed == tree).Position);
        }

        if (artId.StartsWith("wild.rock.", StringComparison.Ordinal) && int.TryParse(parts[^1], out var seed))
        {
            var rock = WildSamples.Rocks.First(r => r.Seed == seed);
            var ground = WildSamples.W(rock.A, rock.D);
            return TownView.P(ground, WildLayout.GroundZ(ground));
        }

        if (artId.StartsWith("inn.plant.", StringComparison.Ordinal) && int.TryParse(parts[^1], out var plant))
        {
            return TownView.P(InnSamples.Plants[plant - 1]);
        }

        return artId switch
        {
            "town.prop.well" => TownView.P(new Vector2(2040, 380)),
            "town.prop.jars" => TownView.P(new Vector2(1700, 1430)),
            "town.prop.boat.ferry" => TownView.P(new Vector3(1640, 2080, -80)),
            "town.prop.boat.2" => TownView.P(new Vector3(3900, 2450, -80)),
            "town.prop.stone_mark" => TownView.P(new Vector3(1180, 1790, 0)),
            _ => Vector2.Zero,
        };
    }

    /// <summary>船随水轻晃：屏幕纵向偏移。</summary>
    public void Bob(float dy) => Position = _base + new Vector2(0, dy);

    public override void _Draw()
    {
        DrawSetTransform(-Position, 0, Vector2.One);
        if (_inWater)
        {
            Cel.GroundShadow(this, _ground, _shadow.X, _shadow.Y, 0.2f, RiverSamples.WaterZ);
        }
        else if (_shadow != Vector2.Zero)
        {
            Cel.GroundShadow(this, _ground + new Vector2(50, -50) * _scale, _shadow.X, _shadow.Y, 0.16f, _z);
        }

        if (Art is { } art)
        {
            DrawSetTransform(Vector2.Zero, 0, new Vector2(_flip ? -_scale : _scale, _scale));
            DrawTextureRect(art.Texture, new Rect2(art.Origin - _authored, art.Frame.Size), false);
        }

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}

/// <summary>
/// 旧渡栈桥：顺世界东向伸进江里（与城镇的桥、船同向，画面上斜向右上），两排桩柱自水底立到桥面下，桥面贴 AI 木板纹理，
/// 两侧边梁、桥头断了半块板（废渡口）。画在贴地层（行人走在桥面上），水面上压一道桥影。
/// </summary>
public partial class RiverJettyNode : TownPiece
{
    private readonly Jetty _j;

    public RiverJettyNode(Jetty j)
    {
        _j = j;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        TextureRepeat = TextureRepeatEnum.Enabled;
        var timber = PieceArt.FindTexture("town.wood.weathered");
        List<Face> Wood(List<Face> faces, Color tone) => timber is { } t ? FaceTexture.Apply(faces, t.Texture, t.WorldSize, tone, 1.4f, 0.12f) : faces;
        var deck = j.Deck;
        var (x0, x1, y0, y1) = (deck.Position.X, deck.End.X, deck.Position.Y, deck.End.Y);

        // 桩柱：两排，每 110 一对，桥头两根歪斜。
        var posts = new List<Face>();
        for (var x = x0 + 70; x < x1 - 10; x += 110)
        {
            foreach (var y in new[] { y0 + 6, y1 - 6 })
            {
                var lean = x > x1 - 120 ? (Cel.Rand((int)x, (int)y) - 0.5f) * 14 : 0;
                posts.AddRange(Solid.Prism(new Vector3(x + lean, y, RiverSamples.WaterZ - 40), new Vector3(x, y, j.Z - 6), 7, 6, Cel.WoodDark, 1.2f));
            }
        }

        Solid.Sort(posts);
        Faces.AddRange(Wood(posts, new Color(0.86f, 0.82f, 0.76f)));
        // 边梁：桥面两侧一根方木。
        foreach (var y in new[] { y0, y1 - 10 })
        {
            Faces.AddRange(Wood(Solid.Box(new Vector3(x0, y, j.Z - 16), new Vector3(x1 - 30, y + 10, j.Z - 4), Cel.WoodDark, 1.2f), new Color(0.8f, 0.76f, 0.7f)));
        }

        // 桥面：断头处少半幅。
        var top = Solid.ExtrudeZ([new(x0, y0), new(x1 - 60, y0), new(x1 - 60, y0 + j.Width * 0.45f), new(x1, y0 + j.Width * 0.45f), new(x1, y1), new(x0, y1)],
            j.Z - 6, j.Z, Cel.Wood, Cel.WoodLight, 1.4f);
        Faces.AddRange(top);
        if (PieceArt.FindTexture("wild.ground.planks") is { } planks)
        {
            _planks = planks;
        }

        Seal(deck);
    }

    private readonly (Texture2D Texture, float WorldSize)? _planks;

    public override void _Draw()
    {
        var j = _j;
        var deck = j.Deck;
        // 水面上的桥影：朝镜头一侧偏一点。
        var z = RiverSamples.WaterZ;
        DrawColoredPolygon([TownView.P(deck.Position + new Vector2(-20, 30), z), TownView.P(new Vector2(deck.End.X - 20, deck.Position.Y + 30), z),
            TownView.P(deck.End + new Vector2(-20, 30), z), TownView.P(new Vector2(deck.Position.X - 20, deck.End.Y + 30), z)], Cel.Ink with { A = 0.22f });
        base._Draw();
        if (_planks is { } p)
        {
            // 木板：板缝沿桥宽（世界 y），每块宽 22，按世界 x 平铺纹理；断头处半幅。
            var tone = new Color(1.04f, 0.98f, 0.9f);
            for (var x = deck.Position.X; x < deck.End.X - 0.5f; x += 22)
            {
                var x1 = Mathf.Min(x + 22, deck.End.X);
                var y0 = x1 > deck.End.X - 60 ? deck.Position.Y + j.Width * 0.45f : deck.Position.Y;
                Vector2[] quad = [TownView.P(new Vector2(x, y0), j.Z), TownView.P(new Vector2(x1, y0), j.Z), TownView.P(new Vector2(x1, deck.End.Y), j.Z), TownView.P(new Vector2(x, deck.End.Y), j.Z)];
                var (u0, u1) = (x / p.WorldSize, x1 / p.WorldSize);
                var (v0, v1) = (y0 / p.WorldSize, deck.End.Y / p.WorldSize);
                var shade = Cel.Rand((int)x, 3) > 0.8f ? tone.Darkened(0.1f) : tone;
                DrawPolygon(quad, new[] { shade, shade, shade, shade }, new Vector2[] { new(u0, v0), new(u1, v0), new(u1, v1), new(u0, v1) }, p.Texture);
                DrawLine(quad[0], quad[3], Cel.WoodDark with { A = 0.45f }, 1.2f, true);
            }

            DrawPolyline([TownView.P(deck.Position, j.Z), TownView.P(new Vector2(deck.End.X - 60, deck.Position.Y), j.Z)], Cel.Ink, 1.6f, true);
            DrawPolyline([TownView.P(new Vector2(deck.Position.X, deck.End.Y), j.Z), TownView.P(deck.End, j.Z)], Cel.Ink, 1.6f, true);
        }
    }
}

/// <summary>
/// 石砌河堤（贴地层）：朝镜头的立面贴 AI 条石纹理（town.embankment），堤脚压接触阴影与湿痕，堤顶边勾线、苔边与草丛；
/// 水门门洞里是暗的过水孔（闸墩、闸桥、闸板与绞架是排序件，见 <see cref="RiverGatePart"/>）。堤顶地面另由页面按石板街铺。
/// </summary>
public partial class RiverLeveeNode : Node2D
{
    private const float Seg = 20;
    private readonly Levee _l;
    private readonly float _a1;

    public RiverLeveeNode(Levee l, float viewA1)
    {
        _l = l;
        _a1 = viewA1 + 600;
        TextureRepeat = TextureRepeatEnum.Enabled;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    private static readonly (Texture2D Texture, float WorldSize)? Stone = PieceArt.FindTexture("town.embankment");

    public override void _Draw()
    {
        var l = _l;
        using (var batch = new PolyBatch(this))
        {
            // 堤脚接触阴影。
            var shade = Cel.Ink with { A = 0.26f };
            foreach (var (a0, a1) in Spans())
            {
                batch.Add([RiverGeo.S(a0, l.Front, 0), RiverGeo.S(a1, l.Front, 0), RiverGeo.S(a1, l.Front + 80, 0), RiverGeo.S(a0, l.Front + 80, 0)],
                    [shade, shade, shade with { A = 0 }, shade with { A = 0 }]);
            }

            // 门洞：过水孔里暗，透出一点江水的青色。
            var dark = Color.FromHtml("#1C2C2E");
            var deepWater = Color.FromHtml("#2D5153");
            batch.Add([RiverGeo.S(l.GateA0, l.Front - 2, 118), RiverGeo.S(l.GateA1, l.Front - 2, 118), RiverGeo.S(l.GateA1, l.Front - 2, l.ChannelZ), RiverGeo.S(l.GateA0, l.Front - 2, l.ChannelZ)],
                [dark, dark, deepWater, deepWater]);
            // 立面。
            foreach (var (a0, a1) in Spans())
            {
                for (var a = a0; a < a1 - 0.5f; a += Seg)
                {
                    var b = Mathf.Min(a + Seg, a1);
                    Vector2[] quad = [RiverGeo.S(a, l.Front, l.Z), RiverGeo.S(b, l.Front, l.Z), RiverGeo.S(b, l.Front, 0), RiverGeo.S(a, l.Front, 0)];
                    var white = Colors.White;
                    var foot = new Color(0.72f, 0.78f, 0.76f);
                    if (Stone is { } t)
                    {
                        var vh = t.WorldSize * t.Texture.GetHeight() / t.Texture.GetWidth();
                        batch.Add(quad, [white, white, foot, foot], [new(a / t.WorldSize, 0), new(b / t.WorldSize, 0), new(b / t.WorldSize, l.Z / vh), new(a / t.WorldSize, l.Z / vh)], t.Texture);
                    }
                    else
                    {
                        batch.Add(quad, [Cel.Stone, Cel.Stone, Cel.Stone.Darkened(0.2f), Cel.Stone.Darkened(0.2f)]);
                    }
                }
            }

            batch.Flush();
            // 堤脚湿痕与苔：一道暗绿，上沿随位置起伏。
            foreach (var (a0, a1) in Spans())
            {
                for (var a = a0; a < a1; a += Seg)
                {
                    var b = Mathf.Min(a + Seg, a1);
                    var ha = 18 + 14 * Mathf.Sin(a / 90) + 8 * Cel.Rand(7, (int)a);
                    var hb = 18 + 14 * Mathf.Sin(b / 90) + 8 * Cel.Rand(7, (int)b);
                    var moss = Cel.LeafDark with { A = 0.5f };
                    batch.Add([RiverGeo.S(a, l.Front, ha), RiverGeo.S(b, l.Front, hb), RiverGeo.S(b, l.Front, 0), RiverGeo.S(a, l.Front, 0)],
                        [moss with { A = 0 }, moss with { A = 0 }, moss, moss]);
                }
            }
        }

        foreach (var (a0, a1) in Spans())
        {
            DrawLine(RiverGeo.S(a0, l.Front, l.Z), RiverGeo.S(a1, l.Front, l.Z), Cel.Ink, 2.4f, true);
            DrawLine(RiverGeo.S(a0, l.Front, 0), RiverGeo.S(a1, l.Front, 0), Cel.Ink with { A = 0.45f }, 1.6f, true);
            if (a0 > l.A0 - 1)
            {
                DrawLine(RiverGeo.S(a0, l.Front, 0), RiverGeo.S(a0, l.Front, l.Z), Cel.Ink, 2.2f, true);
            }
        }

        // 堤西端：截面朝左看不见，只勾一道堤顶边线到临水的后沿。
        DrawLine(RiverGeo.S(l.A0, l.Back, l.Z), RiverGeo.S(l.A0, l.Front, l.Z), Cel.Ink, 2.4f, true);
        DrawLine(RiverGeo.S(l.A0, l.Back, l.Z), RiverGeo.S(_a1, l.Back, l.Z), Cel.Ink with { A = 0.6f }, 2f, true);

        // 堤顶前沿一溜草丛。
        if (Tufts.Ready)
        {
            for (var a = l.A0 + 20; a < _a1; a += 26 + 40 * Cel.Rand(31, (int)a))
            {
                if (a > l.GateA0 - 60 && a < l.GateA1 + 60 || a > l.Steps.A0 - 20 && a < l.Steps.A1 + 20) continue;
                var r = Cel.Rand(31, (int)a + 7);
                if (r < 0.3f) continue;
                Tufts.Draw(this, RiverGeo.S(a, l.Front - 6, l.Z) + new Vector2(0, 3), 22 + 16 * r, (int)(r * 4), Cel.Rand(31, (int)a + 9) > 0.5f);
            }
        }
    }

    /// <summary>立面分段（门洞处断开）。</summary>
    private IEnumerable<(float A0, float A1)> Spans()
    {
        yield return (_l.A0, _l.GateA0);
        yield return (_l.GateA1, _a1);
    }
}

/// <summary>
/// 水门的排序件：两座高出堤顶的条石闸墩、门洞上的石板闸桥（临江一侧石栏，黄蓉坐在上面）、半开的木闸板与闸墩顶上的绞架（转轮、绳）。
/// 闸墩与闸板朝镜头的面贴 AI 条石与风化木纹理。
/// </summary>
public partial class RiverGatePart : TownPiece
{
    private readonly Levee _l;
    private readonly string _kind;

    private RiverGatePart(Levee l, string kind)
    {
        _l = l;
        _kind = kind;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        TextureRepeat = TextureRepeatEnum.Enabled;
    }

    private static Vector3 P3(float a, float d, float z)
    {
        var w = RiverSamples.W(a, d);
        return new Vector3(w.X, w.Y, z);
    }

    private static Vector2[] Quad(float a0, float a1, float d0, float d1) => WildStepsNode.Quad(a0, a1, d0, d1);

    /// <summary>闸桥自堤前沿往江一侧的进深；桥后依次是石栏、闸板与绞架。</summary>
    public const float DeckDepth = 90;

    private const float Pier = 44;
    private const float Top = 270;

    /// <summary>闸板与绞架所在（离堤前沿的进深）。</summary>
    private static float BoardD(Levee l) => l.Front - DeckDepth - 50;

    public static IEnumerable<RiverGatePart> Build(Levee l)
    {
        var stone = PieceArt.FindTexture("town.embankment");
        var timber = PieceArt.FindTexture("town.wood.weathered");
        List<Face> Stone(List<Face> f) => stone is { } t ? FaceTexture.Apply(f, t.Texture, t.WorldSize, new Color(0.98f, 1f, 0.98f), 1.8f, 0.18f) : f;
        List<Face> Wood(List<Face> f, Color tone) => timber is { } t ? FaceTexture.Apply(f, t.Texture, t.WorldSize, tone, 1.4f, 0.12f) : f;
        var deckBack = l.Front - DeckDepth;

        // 闸桥：门洞上方堤前一侧的石板，桥后一道矮石栏（黄蓉坐在上面）。
        var deck = new RiverGatePart(l, "deck");
        deck.Faces.AddRange(Stone(Solid.ExtrudeZ(Quad(l.GateA0, l.GateA1, deckBack, l.Front - 2), 118, l.Z, Cel.Stone, Cel.StoneLight, 1.8f)));
        deck.Faces.AddRange(Stone(Solid.ExtrudeZ(Quad(l.GateA0, l.GateA1, deckBack - 16, deckBack), 118, l.Z + 36, Cel.Stone, Cel.StoneLight, 1.6f)));
        deck.Seal(WildStepsNode.Bounds(Quad(l.GateA0, l.GateA1, deckBack - 16, deckBack)));
        yield return deck;

        // 闸板：嵌在两墩的槽里，提起一半，板下翻水；板顶露出石栏之上。
        var bd = BoardD(l);
        var boards = new RiverGatePart(l, "boards");
        boards.Faces.AddRange(Wood(Solid.ExtrudeZ(Quad(l.GateA0 - 6, l.GateA1 + 6, bd - 8, bd + 8), 60, 236, Cel.Wood, Cel.WoodLight, 1.6f), new Color(0.95f, 0.9f, 0.82f)));
        boards.Seal(WildStepsNode.Bounds(Quad(l.GateA0, l.GateA1, bd - 8, bd + 8)));
        yield return boards;

        foreach (var (a0, a1, name) in new[] { (l.GateA0 - Pier, l.GateA0, "pier_w"), (l.GateA1, l.GateA1 + Pier, "pier_e") })
        {
            var part = new RiverGatePart(l, name);
            part.Faces.AddRange(Stone(Solid.ExtrudeZ(Quad(a0, a1, l.Back + 10, l.Front + 24), 0, Top, Cel.Stone, Cel.StoneLight, 2f)));
            // 墩顶压一块出檐的盖石。
            part.Faces.AddRange(Stone(Solid.ExtrudeZ(Quad(a0 - 6, a1 + 6, l.Back + 4, l.Front + 30), Top, Top + 14, Cel.Stone, Cel.StoneLight, 1.8f)));
            part.Seal(WildStepsNode.Bounds(Quad(a0, a1, l.Back + 10, l.Front + 24)));
            yield return part;
        }

        // 绞架：两墩顶上各立一根木柱，横一根圆木轴，轴中一只转轮；绳垂到闸板。
        var winch = new RiverGatePart(l, "winch");
        foreach (var a in new[] { l.GateA0 - Pier / 2, l.GateA1 + Pier / 2 })
        {
            winch.Faces.AddRange(Wood(Solid.Prism(P3(a, bd, Top + 14), P3(a, bd, Top + 110), 8, 6, Cel.Wood, 1.4f), new Color(0.92f, 0.86f, 0.78f)));
        }

        winch.Faces.AddRange(Wood(Solid.Prism(P3(l.GateA0 - Pier / 2, bd, Top + 96), P3(l.GateA1 + Pier / 2, bd, Top + 96), 7, 8, Cel.Wood, 1.4f, alongU: true), new Color(0.92f, 0.86f, 0.78f)));
        winch.Seal(WildStepsNode.Bounds(Quad(l.GateA0 - Pier, l.GateA1 + Pier, bd - 10, bd + 10)));
        yield return winch;
    }

    protected override void DrawExtras()
    {
        if (_kind != "winch") return;
        var l = _l;
        const float top = Top;
        var mid = BoardD(l);
        var c = RiverGeo.S((l.GateA0 + l.GateA1) / 2, mid, top + 96);
        // 转轮：轮辋、辐条与把手。
        DrawArc(c, 34, 0, Mathf.Tau, 28, Cel.Ink, 6, true);
        DrawArc(c, 34, 0, Mathf.Tau, 28, Cel.WoodLight, 3.4f, true);
        for (var i = 0; i < 6; i++)
        {
            var dir = Vector2.FromAngle(i * Mathf.Tau / 6 + 0.3f);
            DrawLine(c, c + dir * 33, Cel.Wood, 3, true);
            DrawLine(c + dir * 33, c + dir * 44, Cel.WoodDark, 3, true);
        }

        DrawCircle(c, 6, Cel.WoodDark);
        // 吊绳：轴上两处垂到闸板顶。
        foreach (var a in new[] { l.GateA0 + 40, l.GateA1 - 40 })
        {
            DrawLine(RiverGeo.S(a, mid, top + 90), RiverGeo.S(a, mid, 236), Color.FromHtml("#6B5A44"), 2.4f, true);
        }
    }
}

/// <summary>木桩与铁链（锁船的桩、渡船的缆桩）：桩是排序件，链 / 缆自桩头垂成弧线拴到船头。</summary>
public partial class RiverStakeNode : TownPiece
{
    private readonly Vector2 _stake;
    private readonly Vector2 _bow;
    private readonly bool _chain;

    public RiverStakeNode(Vector2 stakeAd, Vector2 bowAd, bool chain)
    {
        _stake = stakeAd;
        _bow = bowAd;
        _chain = chain;
        var w = RiverSamples.W(stakeAd.X, stakeAd.Y);
        var timber = PieceArt.FindTexture("town.wood.weathered");
        var post = Solid.Prism(new Vector3(w.X, w.Y, -6), new Vector3(w.X + 3, w.Y - 3, chain ? 92 : 70), chain ? 10 : 8, 7, Cel.WoodDark, 1.6f);
        Faces.AddRange(timber is { } t ? FaceTexture.Apply(post, t.Texture, t.WorldSize, new Color(0.85f, 0.8f, 0.74f), 1.6f, 0.12f) : post);
        Seal(new Rect2(w - new Vector2(14, 14), new Vector2(28, 28)));
        var bow = RiverGeo.S(bowAd, 40);
        ScreenBox = ScreenBox.Expand(bow).Grow(10);
    }

    public override void _Draw()
    {
        Cel.GroundShadow(this, RiverSamples.W(_stake.X, _stake.Y) + new Vector2(12, -12), 26, 16, 0.2f);
        base._Draw();
        var from = RiverGeo.S(_stake, _chain ? 80 : 58);
        var to = RiverGeo.S(_bow, 38);
        var sag = _chain ? 46f : 30f;
        var pts = new Vector2[17];
        for (var i = 0; i < pts.Length; i++)
        {
            var t = (float)i / (pts.Length - 1);
            pts[i] = from.Lerp(to, t) + new Vector2(0, Mathf.Sin(t * Mathf.Pi) * sag);
        }

        if (_chain)
        {
            // 铁链：深色粗线上一节一节的环。
            DrawPolyline(pts, Cel.Ink, 5, true);
            DrawPolyline(pts, Color.FromHtml("#5E6466"), 3, true);
            for (var i = 1; i < pts.Length - 1; i++)
            {
                var dir = (pts[i + 1] - pts[i - 1]).Normalized();
                var c = pts[i];
                DrawLine(c - dir * 4, c + dir * 4, Color.FromHtml("#9AA2A2"), 2, true);
            }

            // 绕桩两圈。
            DrawArc(from + new Vector2(0, 4), 12, 0.2f, Mathf.Pi - 0.2f, 10, Color.FromHtml("#5E6466"), 3.2f, true);
            DrawArc(from + new Vector2(0, 12), 12, 0.2f, Mathf.Pi - 0.2f, 10, Color.FromHtml("#5E6466"), 3.2f, true);
        }
        else
        {
            DrawPolyline(pts, Color.FromHtml("#4E4334"), 3, true);
            DrawPolyline(pts, Color.FromHtml("#9C8A68"), 1.6f, true);
        }
    }
}

/// <summary>泥滩上的拖船潮痕：一道被船底刮平的浅槽，两侧挤起的泥埂，槽里积着薄水；顺着水线往下游、钻进苇根。画在泥滩之上。</summary>
public partial class RiverTideMark : Node2D
{
    private readonly Vector2[] _line;

    public RiverTideMark(Vector2[] line) => _line = line;

    public override void _Draw()
    {
        if (_line.Length < 2) return;
        // 加密折线，按长度取宽：船头一端窄、中段宽、钻进苇根处收尖，边缘随位置微微起伏。
        var dense = new List<Vector2>();
        for (var i = 0; i < _line.Length - 1; i++)
        {
            var n = Mathf.Max(1, Mathf.CeilToInt(_line[i].DistanceTo(_line[i + 1]) / 24));
            for (var k = 0; k < n; k++) dense.Add(_line[i].Lerp(_line[i + 1], (float)k / n));
        }

        dense.Add(_line[^1]);
        var left = new List<Vector2>();
        var right = new List<Vector2>();
        for (var i = 0; i < dense.Count; i++)
        {
            var t = (float)i / (dense.Count - 1);
            var dir = (dense[Mathf.Min(i + 1, dense.Count - 1)] - dense[Mathf.Max(i - 1, 0)]).Normalized();
            var normal = new Vector2(-dir.Y, dir.X);
            var half = 26 * Mathf.Sin(Mathf.Pi * Mathf.Clamp(t * 1.15f, 0, 1)) * (0.85f + 0.3f * Cel.Rand(17, i));
            left.Add(dense[i] + normal * half);
            right.Add(dense[i] - normal * half);
        }

        Vector2[] Proj(IEnumerable<Vector2> pts) => pts.Select(p => RiverGeo.S(p, 0)).ToArray();
        // 槽：湿泥色，两端淡；槽底一道船底刮出的深痕，积着一线薄水。
        var mud = Color.FromHtml("#6A5C46");
        var colors = new List<Color>();
        for (var i = 0; i < dense.Count; i++) colors.Add(mud with { A = 0.55f * Mathf.Sin(Mathf.Pi * i / (dense.Count - 1)) + 0.06f });
        // 逐段画四边形：两端宽度收到 0，左右边重合，整条外轮廓当一个多边形三角化会失败（报错且整段不画）。
        var (pl, pr) = (Proj(left), Proj(right));
        for (var i = 0; i < dense.Count - 1; i++)
        {
            DrawPrimitive([pl[i], pl[i + 1], pr[i + 1], pr[i]], [colors[i], colors[i + 1], colors[i + 1], colors[i]], []);
        }
        var keel = Proj(dense.Skip(2).Take(dense.Count - 5));
        DrawPolyline(keel, Color.FromHtml("#43392D") with { A = 0.7f }, 4f, true);
        DrawPolyline(keel.Select(p => p + new Vector2(0, -1.5f)).ToArray(), Color.FromHtml("#B9CFCB") with { A = 0.45f }, 1.4f, true);
        // 两侧挤起的泥埂：受光侧一道浅沙色，背光侧一道暗。
        DrawPolyline(Proj(left.Skip(1).Take(left.Count - 3)), Color.FromHtml("#DCCFAE") with { A = 0.8f }, 3f, true);
        DrawPolyline(Proj(right.Skip(1).Take(right.Count - 3)), Color.FromHtml("#4E4536") with { A = 0.35f }, 2f, true);
    }
}

/// <summary>草坡与泥滩交界、苇丛之间零星的草丛（AI 草丛精灵），打断地面的平铺。</summary>
public partial class RiverTufts : Node2D
{
    private readonly RiverSamples _s;

    public RiverTufts(RiverSamples s) => _s = s;

    public override void _Draw()
    {
        if (!Tufts.Ready) return;
        var s = _s;
        var geo = new RiverGeo(s);
        for (var a = s.ViewA0 - 200; a < s.ViewA1 + 300; a += 24 + 46 * Cel.Rand(51, (int)a))
        {
            var r = Cel.Rand(51, (int)a + 3);
            if (r < 0.25f) continue;
            var d = s.GrassEdge(a) + (Cel.Rand(51, (int)a + 5) - 0.3f) * 90;
            if (s.Levee is { } l && a > l.A0 - 40 && d < l.Front + 60) continue;
            if (s.Levee is { } l2 && a > l2.GateA0 - 30 && a < l2.GateA1 + 30) continue;
            if (geo.OnJetty(a, d)) continue;
            Tufts.Draw(this, RiverGeo.S(a, d, 0) + new Vector2(0, 3), 20 + 18 * r, (int)(Cel.Rand(51, (int)a + 9) * 4), Cel.Rand(51, (int)a + 11) > 0.5f);
        }

        // 草坡上稀疏的草丛。
        for (var i = 0; i < 36; i++)
        {
            var a = Mathf.Lerp(s.ViewA0, s.ViewA1, Cel.Rand(52, i));
            var d = Mathf.Lerp(s.GrassEdge(a) + 60, s.Front + 120, Cel.Rand(52, i + 100));
            if (s.Levee is { } l && a > l.GateA0 - 40 && a < l.GateA1 + 40) continue;
            var r = Cel.Rand(52, i + 200);
            Tufts.Draw(this, RiverGeo.S(a, d, 0) + new Vector2(0, 3), 18 + 16 * r, (int)(r * 4), r > 0.5f);
        }
    }
}

/// <summary>
/// 河岸远景：天色、远岸的低丘与树影、江面尽头的一层水汽。雨后（河滩）天色青灰、远丘淡；黄昏（旧渡）天边暖、远丘偏紫灰。
/// 各层按镜头移动的比例反向平移形成视差（同山路远景）。地平线对齐江面没入水汽处。
/// </summary>
public partial class RiverBackdrop : Node2D
{
    private readonly List<(Node2D Layer, float Parallax)> _layers = [];
    private readonly bool _dusk;
    private Vector2 _reference;

    public RiverBackdrop(bool dusk) => _dusk = dusk;

    /// <summary>远岸水平线（投影 y）：江面在此全没入水汽。</summary>
    public static float Horizon => RiverGeo.S(0, RiverSamples.WaterFar, RiverSamples.WaterZ).Y + 30;

    public override void _Ready()
    {
        _reference = new Vector2(1800, -300);
        var horizon = Horizon;
        var (top, low, glow) = _dusk
            ? (Color.FromHtml("#8FAFB9"), Color.FromHtml("#F1D9B8"), Color.FromHtml("#F6C890"))
            : (Color.FromHtml("#A9C5C8"), Color.FromHtml("#E6ECE6"), Color.FromHtml("#F2F2E8"));
        var sky = new Node2D();
        sky.Draw += () =>
        {
            sky.DrawPolygon([new(-4000, horizon - 2600), new(9000, horizon - 2600), new(9000, horizon - 120), new(-4000, horizon - 120)], [top, top, low, low]);
            sky.DrawRect(new Rect2(-4000, horizon - 122, 13000, 3000), low);
            // 天边一团亮（雨后云隙 / 落日）：偏画面左上。
            for (var k = 0; k < 6; k++)
            {
                sky.DrawColoredPolygon(Cel.Ellipse(new Vector2(600, horizon - 200), 1400 - k * 200, 260 - k * 36, 40), glow with { A = 0.12f });
            }

            if (!_dusk)
            {
                // 雨后的层云：几条拉长的淡云带。
                foreach (var (x, y, w) in new[] { (300f, -520f, 1600f), (2200f, -640f, 1900f), (3800f, -480f, 1300f) })
                {
                    sky.DrawColoredPolygon(Cel.Ellipse(new Vector2(x, horizon + y), w / 2, 40, 36), Colors.White with { A = 0.22f });
                    sky.DrawColoredPolygon(Cel.Ellipse(new Vector2(x + 120, horizon + y + 16), w / 2.6f, 26, 36), Color.FromHtml("#9DB4B6") with { A = 0.18f });
                }
            }
        };
        AddLayer(sky, 0.95f);
        var hills = _dusk ? (Color.FromHtml("#A7A9B6"), Color.FromHtml("#9196A5")) : (Color.FromHtml("#AFC4C2"), Color.FromHtml("#9DB4B2"));
        AddLayer(Hills(horizon - 20, 150, 260, hills.Item1, hills.Item2, 0.18f, 61), 0.85f);
        var near = _dusk ? (Color.FromHtml("#7E8E8C"), Color.FromHtml("#6E7C7C")) : (Color.FromHtml("#8DAAA0"), Color.FromHtml("#7A988E"));
        AddLayer(Hills(horizon - 4, 70, 200, near.Item1, near.Item2, 0.3f, 73, trees: true), 0.7f);
        var mist = _dusk ? Color.FromHtml("#E9D9C4") : Color.FromHtml("#DCE6E2");
        var band = new Node2D();
        band.Draw += () =>
        {
            band.DrawPolygon([new(-4000, horizon - 70), new(9000, horizon - 70), new(9000, horizon + 4), new(-4000, horizon + 4)], [mist with { A = 0 }, mist with { A = 0 }, mist, mist]);
            band.DrawRect(new Rect2(-4000, horizon + 3, 13000, 3000), mist);
        };
        AddLayer(band, 0.7f);
    }

    private void AddLayer(Node2D layer, float parallax)
    {
        AddChild(layer);
        _layers.Add((layer, parallax));
    }

    public void Scroll(Vector2 camera)
    {
        foreach (var (layer, k) in _layers)
        {
            layer.Position = ((camera - _reference) * new Vector2(k, k * 0.5f)).Round();
        }
    }

    /// <summary>一层平缓的江南低丘（比山路的远山矮、圆），trees 时丘顶与丘脚点一排树影。</summary>
    private static Node2D Hills(float baseY, float height, float spacing, Color tone, Color shade, float ink, int seed, bool trees = false)
    {
        const float x0 = -4000;
        const float x1 = 9000;
        var peaks = new List<(float X, float H, float W)>();
        for (var x = x0; x < x1; x += spacing * (2f + 3f * Cel.Rand(seed, (int)x)))
        {
            peaks.Add((x, height * (0.35f + 0.65f * Cel.Rand(seed, (int)x + 3)), height * (3.2f + 2.4f * Cel.Rand(seed, (int)x + 5))));
        }

        float Ridge(float x)
        {
            var best = 0f;
            foreach (var (px, h, w) in peaks)
            {
                var t = Mathf.Abs(x - px) / w;
                if (t < 1) best = Mathf.Max(best, h * (0.5f + 0.5f * Mathf.Cos(t * Mathf.Pi)));
            }

            return best + Brushwork.Noise(x * 0.03f + seed) * 4;
        }

        var ridge = new List<Vector2>();
        for (var x = x0; x <= x1; x += 24) ridge.Add(new Vector2(x, baseY - Ridge(x)));
        var node = new Node2D();
        node.Draw += () =>
        {
            var batch = new PolyBatch(node);
            batch.Add([.. ridge, new(x1, baseY + 2000), new(x0, baseY + 2000)], tone);
            foreach (var (px, h, w) in peaks)
            {
                var poly = new List<Vector2> { new(px, baseY - Ridge(px)) };
                for (var x = px + 24; x < px + w * 0.9f; x += 24) poly.Add(new Vector2(x, baseY - Ridge(x)));
                poly.Add(new Vector2(px + w * 0.9f, baseY + 10));
                poly.Add(new Vector2(px + w * 0.15f, baseY + 10));
                if (poly.Count > 3) batch.Add(poly.ToArray(), shade);
            }

            if (trees)
            {
                for (var x = x0; x < x1; x += 16)
                {
                    if (Cel.Rand(seed, (int)x + 90) < 0.45f) continue;
                    var y = baseY - Ridge(x) * Cel.Rand(seed, (int)x + 92);
                    var s = 7 + 9 * Cel.Rand(seed, (int)x + 91);
                    batch.Add(Cel.Ellipse(new Vector2(x, y - s * 0.8f), s * 0.8f, s, 10), shade.Darkened(0.12f));
                }
            }

            batch.Flush();
            node.DrawPolyline(ridge.ToArray(), Cel.Ink with { A = ink }, 1.6f, true);
        };
        return node;
    }
}

/// <summary>
/// 画面光色（M3-01 光影）：雨后（河滩）整体偏青、略带水汽；黄昏（旧渡）整体压暖、自画面左上斜照一层暖光，右下渐暗。
/// 盖在布景之上、交互菱形之下；只做大面积的色调，不画投影（投影仍由各件自带）。
/// </summary>
public partial class RiverLight : Node2D
{
    private readonly bool _dusk;

    public RiverLight(bool dusk) => _dusk = dusk;

    public Rect2 Area { get; set; }

    public override void _Draw()
    {
        var r = Area.Grow(400);
        var (tl, tr, br, bl) = (r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y));
        if (_dusk)
        {
            var warm = Color.FromHtml("#FFB66B");
            var dim = Color.FromHtml("#2A3550");
            DrawPolygon([tl, tr, br, bl], [warm with { A = 0.22f }, warm with { A = 0.1f }, dim with { A = 0.18f }, warm with { A = 0.06f }]);
        }
        else
        {
            var haze = Color.FromHtml("#E8F0EE");
            var cool = Color.FromHtml("#4F7480");
            DrawPolygon([tl, tr, br, bl], [haze with { A = 0.08f }, haze with { A = 0.1f }, cool with { A = 0.06f }, cool with { A = 0.05f }]);
        }
    }
}

/// <summary>贴着江面飘的几缕水汽（雨后浓、黄昏淡），缓慢横移。</summary>
public partial class RiverMist : Node2D
{
    private readonly (float A, float D, float Length, float Speed)[] _wisps;
    private readonly float _alpha;

    public RiverMist(bool dusk)
    {
        _alpha = dusk ? 0.05f : 0.09f;
        _wisps = [(500, -500, 1300, 0.09f), (2100, -650, 1600, 0.07f), (3400, -420, 1100, 0.11f), (1300, -300, 900, 0.12f)];
    }

    public float Seconds { get; set; }

    public override void _Draw()
    {
        foreach (var (a, d, len, speed) in _wisps)
        {
            var c = RiverGeo.S(a, d, 30) + new Vector2(Mathf.Sin(Seconds * speed + a) * 80, 0);
            for (var k = 0; k < 3; k++)
            {
                var s = 1 - k * 0.25f;
                DrawColoredPolygon(Cel.Ellipse(c + new Vector2(k * 50, -k * 5), len * 0.5f * s, 30 * s, 32), WildFog.Mist with { A = _alpha });
            }
        }
    }
}

/// <summary>小地图：江面、泥滩与草坡、河堤与水门、栈桥、小路；石青箭头为主角、泥金菱形为目标（上北）。</summary>
public partial class RiverMiniMap : Control
{
    private const float Window = 2600;

    public required RiverSamples Site { get; init; }

    public Func<(Vector2 Position, Vector2 Heading)> Hero { get; init; } = () => (Vector2.Zero, Vector2.Right);

    public Func<Vector2?> Goal { get; init; } = () => null;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
    }

    public override void _Draw()
    {
        var s = Site;
        var (hero, heading) = Hero();
        var k = Size.X / Window;
        var origin = hero - new Vector2(Window, Window) / 2;
        DrawRect(new Rect2(Vector2.Zero, Size), Color.FromHtml("#3F7F8A"));
        DrawSetTransform(-origin * k, 0, new Vector2(k, k));
        Vector2[] W(IEnumerable<Vector2> ad) => ad.Select(p => RiverSamples.W(p.X, p.Y)).ToArray();
        IEnumerable<Vector2> Line(Func<float, float> d, float from, float to)
        {
            for (var a = from; a <= to; a += 60) yield return new Vector2(a, d(a));
        }

        var (a0, a1) = (s.ViewA0 - 2000, s.ViewA1 + 2000);
        DrawColoredPolygon(W(Line(s.Shore.At, a0, a1).Concat(Line(_ => 3000, a0, a1).Reverse())), Color.FromHtml("#B9AE8E"));
        DrawColoredPolygon(W(Line(s.GrassEdge, a0, a1).Concat(Line(_ => 3000, a0, a1).Reverse())), Color.FromHtml("#4E7267"));
        if (s.Levee is { } l)
        {
            DrawColoredPolygon(W([new(l.A0, l.Back), new(a1, l.Back), new(a1, l.Front), new(l.A0, l.Front)]), UiPalette.TextMuted);
            DrawColoredPolygon(W([new(l.GateA0, l.Front), new(l.GateA1, l.Front), new(l.GateA1, 3000), new(l.GateA0, 3000)]), Color.FromHtml("#3F7F8A"));
            DrawColoredPolygon(W([new(l.GateA0 - 44, l.Back), new(l.GateA1 + 44, l.Back), new(l.GateA1 + 44, l.Front), new(l.GateA0 - 44, l.Front)]), UiPalette.Text with { A = 0.8f });
            var st = l.Steps;
            DrawColoredPolygon(W([new(st.A0, st.D0), new(st.A1, st.D0), new(st.A1, st.D1), new(st.A0, st.D1)]), UiPalette.TextMuted.Lightened(0.2f));
        }

        if (s.Jetty is { } j)
        {
            var dk = j.Deck;
            DrawColoredPolygon([dk.Position, new(dk.End.X, dk.Position.Y), dk.End, new(dk.Position.X, dk.End.Y)], Cel.WoodLight);
        }

        if (s.Path.Length > 1)
        {
            DrawPolyline(W(s.Path), UiPalette.Text with { A = 0.55f }, 2.5f / k, true);
        }

        foreach (var bed in s.Reeds.Where(b => b.Blocks))
        {
            DrawColoredPolygon(Cel.Ellipse(RiverSamples.W(bed.A, bed.D), bed.Width * 0.4f, bed.Width * 0.4f, 12), Color.FromHtml("#5E8A62") with { A = 0.8f });
        }

        var r = 9 / k;
        if (Goal() is { } g)
        {
            DrawColoredPolygon([g + new Vector2(0, -r), g + new Vector2(r, 0), g + new Vector2(0, r), g + new Vector2(-r, 0)], UiPalette.Gilt.Darkened(0.1f));
        }

        var arrow = 11 / k;
        var f = heading.Normalized();
        var side = new Vector2(-f.Y, f.X);
        DrawColoredPolygon([hero + f * arrow * 1.2f, hero - f * arrow * 0.8f + side * arrow * 0.8f, hero - f * arrow * 0.3f, hero - f * arrow * 0.8f - side * arrow * 0.8f], UiPalette.Accent);
        DrawArc(hero, 18 / k, 0, Mathf.Tau, 32, UiPalette.Accent with { A = 0.5f }, 1.5f / k, true);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        DrawString(UiFonts.Title, new Vector2(Size.X - 30, 28), "北", HorizontalAlignment.Left, -1, 20, UiPalette.Text);
    }
}
