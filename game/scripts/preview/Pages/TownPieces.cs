using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 城镇布景的一件物。立体件（房屋、廊棚、桥栏、摊位……）由世界坐标的面搭成，Node2D 原点不动、直接画投影后的面；
/// 立着的精灵（人物、树、石、坛）原点放在脚底投影处、高度按 <see cref="TownView.Upright"/> 缩放。
/// 前后次序由 <see cref="DepthSort"/> 按占地写入 ZIndex；行人被本件挡住时整件淡出（遮挡规则见架构文档 10.3）。
/// 全部是程序化赛璐璐占位，正式件按架构文档 10.3 由 AI 逐件生成后替换，布局不变。
/// </summary>
public partial class TownPiece : Node2D, ISortable
{
    protected List<Face> Faces { get; } = [];

    public Rect2 Foot { get; protected set; }

    public Rect2 ScreenBox { get; protected set; }

    public bool Overhead { get; init; }

    public bool Walker { get; protected init; }

    public CanvasItem Item => this;

    public bool Occluder { get; init; } = true;

    /// <summary>多部件件（茶亭、山门）里本部件的名字：引导图按部件出遮罩，AI 件按 &lt;件 id&gt;.&lt;部件&gt; 贴回。</summary>
    public string? Part { get; init; }

    /// <summary>AI 出件贴图（有则代替程序化占位画出，面仍用于遮挡判定）。</summary>
    protected PieceArt? Art { get; private set; }

    private Vector2 _artAnchor;

    /// <summary>按件 id 查找 AI 出件；在构造末尾（Position 与屏幕外框已定）调用。</summary>
    protected void UseArt(string id)
    {
        Art = PieceArt.Find(id);
        if (Art is null)
        {
            return;
        }

        _artAnchor = Position;
        ScreenBox = ScreenBox.Merge(Art.Frame);
    }

    protected void DrawArt() => Art!.Draw(this, _artAnchor);

    /// <summary>由面算屏幕外框；子类在搭好面之后调用。</summary>
    protected void Seal(Rect2 foot)
    {
        Foot = foot;
        var first = true;
        var box = new Rect2();
        foreach (var p in Faces.SelectMany(f => f.Points))
        {
            var s = TownView.P(p);
            box = first ? new Rect2(s, Vector2.Zero) : box.Expand(s);
            first = false;
        }

        ScreenBox = box.Grow(6);
    }

    private List<Vector2[]>? _screenFaces;

    /// <summary>行人的头与胸是否落在本件朝向镜头的面内（精灵件用屏幕外框近似）。</summary>
    public bool Covers(WalkerFigure w)
    {
        Vector2[] probes = [w.Position + new Vector2(0, -w.Height * 0.85f), w.Position + new Vector2(0, -w.Height * 0.5f)];
        if (Faces.Count == 0)
        {
            var box = ScreenBox.Grow(-ScreenBox.Size.X * 0.15f);
            return probes.Any(box.HasPoint);
        }

        _screenFaces ??= Faces.Where(f => f.Normal == Vector3.Zero || TownView.Facing(f.Normal))
            .Select(f => f.Points.Select(TownView.P).ToArray()).ToList();
        return probes.Any(p => _screenFaces.Any(poly => Geometry2D.IsPointInPolygon(p - Position, poly)));
    }

    public override void _Draw()
    {
        if (Art is not null)
        {
            DrawArt();
            foreach (var face in Faces) face.DrawLettering(this);
            return;
        }

        foreach (var face in Faces)
        {
            face.Draw(this);
        }

        DrawExtras();
    }

    /// <summary>面以外的屏幕空间细节（屋脊起翘、灯笼、摊上货物）。</summary>
    protected virtual void DrawExtras()
    {
    }
}

/// <summary>布景共用色与画法：勾线、两到三阶硬边明暗、青绿清新基调，暖色只做点缀。</summary>
internal static class Cel
{
    public static readonly Color Ink = Color.FromHtml("#22303A");
    public static readonly Color Plaster = Color.FromHtml("#EEEEE6");
    public static readonly Color PlasterShade = Color.FromHtml("#C9D0CB");
    public static readonly Color Damp = Color.FromHtml("#A9B8B1");
    public static readonly Color Stone = Color.FromHtml("#A3ADA9");
    public static readonly Color StoneLight = Color.FromHtml("#BEC7C2");
    public static readonly Color Tile = Color.FromHtml("#56626E");
    public static readonly Color TileRow = Color.FromHtml("#72818D");
    public static readonly Color TileDark = Color.FromHtml("#2B343D");
    public static readonly Color Wood = Color.FromHtml("#6A4630");
    public static readonly Color WoodLight = Color.FromHtml("#8A6240");
    public static readonly Color WoodDark = Color.FromHtml("#43291B");
    public static readonly Color Paper = Color.FromHtml("#E9DDBF");
    public static readonly Color PaperShade = Color.FromHtml("#CDBE9C");
    public static readonly Color Lantern = Color.FromHtml("#C8432F");
    public static readonly Color Interior = Color.FromHtml("#2C2824");
    public static readonly Color Glow = Color.FromHtml("#F2C77A");
    public static readonly Color LeafDark = Color.FromHtml("#3F7A55");
    public static readonly Color Leaf = Color.FromHtml("#6FA66A");
    public static readonly Color LeafLight = Color.FromHtml("#9CCD84");
    public static readonly Color LeafGlint = Color.FromHtml("#C4E4A2");
    public static readonly Color Bark = Color.FromHtml("#5B4A3C");
    public static readonly Color BarkLight = Color.FromHtml("#7E6B57");
    public static readonly Color Cloth = Color.FromHtml("#3F7E9A");

    /// <summary>是否画贴地投影阴影；导出 AI 出件的引导图时关闭，阴影留给引擎画。</summary>
    public static bool Shadows { get; set; } = true;

    public static float Rand(int seed, int i) => Brushwork.Hash(seed * 12.9898f + i * 78.233f);

    public static void Shape(CanvasItem ci, Vector2[] points, Color fill, float outline = 2.2f)
    {
        ci.DrawColoredPolygon(points, fill);
        if (outline > 0)
        {
            Outline(ci, points, outline);
        }
    }

    public static void Outline(CanvasItem ci, Vector2[] points, float width = 2.2f, Color? color = null)
    {
        var closed = new Vector2[points.Length + 1];
        points.CopyTo(closed, 0);
        closed[^1] = points[0];
        ci.DrawPolyline(closed, color ?? Ink, width, true);
    }

    public static void Box(CanvasItem ci, Rect2 r, Color fill, float outline = 2f)
    {
        ci.DrawRect(r, fill);
        if (outline > 0)
        {
            ci.DrawRect(r, Ink, false, outline);
        }
    }

    public static Vector2[] Ellipse(Vector2 c, float rx, float ry, int n = 28, float from = 0, float to = Mathf.Tau)
    {
        var pts = new Vector2[n];
        for (var i = 0; i < n; i++)
        {
            var a = from + (to - from) * i / (to >= Mathf.Tau ? n : n - 1);
            pts[i] = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }

        return pts;
    }

    /// <summary>地面上的投影阴影：世界坐标椭圆，经投影画出（随视角压扁）。</summary>
    public static void GroundShadow(CanvasItem ci, Vector2 center, float rx, float ry, float alpha = 0.22f, float z = 0)
    {
        if (!Shadows)
        {
            return;
        }

        var pts = new Vector2[24];
        for (var i = 0; i < pts.Length; i++)
        {
            var a = Mathf.Tau * i / pts.Length;
            pts[i] = TownView.P(center + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry), z);
        }

        ci.DrawColoredPolygon(pts, Ink with { A = alpha });
    }

    /// <summary>瓦面贴花（坡面局部坐标：x 沿屋脊，y 自屋脊向檐口，单位为世界单位）：瓦垄竖条、檐口暗边与瓦当。</summary>
    public static void TileDecal(CanvasItem ci, float width, float length, int seed)
    {
        if (Face.StructureOnly)
        {
            return;
        }

        for (var x = 10f; x < width - 6; x += 22)
        {
            var shade = Rand(seed, (int)x) > 0.85f ? TileDark with { A = 0.35f } : TileRow with { A = 0.6f };
            ci.DrawRect(new Rect2(x, 0, 9, length - 8), shade);
        }

        ci.DrawRect(new Rect2(0, length - 12, width, 12), TileDark);
        for (var x = 16f; x < width - 6; x += 22)
        {
            ci.DrawCircle(new Vector2(x, length - 4), 6.5f, TileDark);
            ci.DrawCircle(new Vector2(x - 2, length - 6), 2.2f, TileRow);
        }
    }

    /// <summary>木槅窗（墙面局部坐标，y 向下）：木框、糊纸、方格棂，下沿窗台石。</summary>
    public static void LatticeWindow(CanvasItem ci, Rect2 r)
    {
        Box(ci, r, Wood);
        var paper = r.Grow(-7);
        ci.DrawRect(paper, Paper);
        ci.DrawRect(new Rect2(paper.Position, new Vector2(paper.Size.X, paper.Size.Y * 0.28f)), PaperShade);
        for (var x = paper.Position.X + 14; x < paper.End.X - 2; x += 14)
        {
            ci.DrawLine(new Vector2(x, paper.Position.Y), new Vector2(x, paper.End.Y), WoodLight, 2f);
        }

        for (var y = paper.Position.Y + 14; y < paper.End.Y - 2; y += 14)
        {
            ci.DrawLine(new Vector2(paper.Position.X, y), new Vector2(paper.End.X, y), WoodLight, 2f);
        }

        ci.DrawRect(new Rect2(r.Position.X - 8, r.End.Y, r.Size.X + 16, 8), Stone);
    }

    public static void HangingLantern(CanvasItem ci, Vector2 top, float size = 1)
    {
        ci.DrawLine(top, top + new Vector2(0, 16 * size), Ink, 2f);
        var c = top + new Vector2(0, 38 * size);
        Shape(ci, Ellipse(c, 16 * size, 22 * size, 20), Lantern, 2f);
        ci.DrawColoredPolygon(Ellipse(c + new Vector2(-5 * size, -5 * size), 6 * size, 12 * size, 14), Lantern.Lightened(0.35f));
        ci.DrawRect(new Rect2(c.X - 10 * size, c.Y - 24 * size, 20 * size, 5 * size), TileDark);
        ci.DrawRect(new Rect2(c.X - 10 * size, c.Y + 19 * size, 20 * size, 5 * size), TileDark);
        ci.DrawLine(c + new Vector2(0, 24 * size), c + new Vector2(0, 40 * size), UiPalette.Gilt, 3f);
    }

    /// <summary>粉墙贴花：檐下阴影、墙脚湿痕与雨渍、条石墙脚（墙面局部坐标，y 自墙顶 -h 到地面 0）。</summary>
    public static void PlasterDecal(CanvasItem ci, float w, float h, int seed)
    {
        if (Face.StructureOnly)
        {
            return;
        }

        ci.DrawRect(new Rect2(0, -h, w, 40), PlasterShade);
        ci.DrawPolygon([new(0, -34), new(w, -34), new(w, -120), new(0, -120)],
            [Damp, Damp, Damp with { A = 0 }, Damp with { A = 0 }]);
        for (var i = 0; i < 4; i++)
        {
            var x = Rand(seed, i) * (w - 50) + 25;
            var len = 60 + Rand(seed, i + 9) * 90;
            ci.DrawPolygon([new(x, -h + 40), new(x + 12, -h + 40), new(x + 8, -h + 40 + len), new(x + 4, -h + 40 + len)],
                [Damp with { A = 0.45f }, Damp with { A = 0.45f }, Damp with { A = 0 }, Damp with { A = 0 }]);
        }

        ci.DrawRect(new Rect2(0, -34, w, 34), Stone);
        ci.DrawLine(new Vector2(0, -33), new Vector2(w, -33), StoneLight, 4);
        for (var x = 60f - Rand(seed, 3) * 40; x < w; x += 60)
        {
            ci.DrawLine(new Vector2(x, -32), new Vector2(x, 0), Ink with { A = 0.5f }, 2f);
        }
    }
}

/// <summary>粉墙黛瓦房屋：民居、马头墙民居、铺面、两层客栈。屋脊东西向，南墙临街。</summary>
public partial class TownHouseNode : TownPiece
{
    private const float Pitch = 0.55f;
    private readonly TownHouse _h;
    private readonly List<(Vector3 Tip, int Dir)> _horns = [];
    private Transform2D _frontLocal;
    private Face? _flag;

    public TownHouseNode(TownHouse house)
    {
        _h = house;
        Build();
        Seal(new Rect2(house.X0, house.Y0, house.X1 - house.X0, house.Y1 - house.Y0));
        UseArt($"town.{house.Id}");
    }

    private void Build()
    {
        var (x0, y0, x1, y1, hgt) = (_h.X0, _h.Y0, _h.X1, _h.Y1, _h.WallHeight);
        var w = x1 - x0;
        var depth = y1 - y0;
        var mid = (y0 + y1) / 2;
        const float over = 50;
        var gable = _h.Style == HouseStyle.Gable;
        var ox = gable ? 0 : over;
        var eaveZ = hgt - over * Pitch;
        var ridgeZ = eaveZ + (depth / 2 + over) * Pitch;
        var inn = _h.Style == HouseStyle.Inn;

        // 墙：北、西两面在默认视角下背对镜头（剔除），仍搭出以便换视角。
        var north = new Face { Points = [new(x1, y0, 0), new(x0, y0, 0), new(x0, y0, hgt), new(x1, y0, hgt)], Normal = new Vector3(0, -1, 0), Color = Cel.Plaster };
        var west = new Face
        {
            Points = [new(x0, y0, 0), new(x0, y1, 0), new(x0, y1, hgt), new(x0, mid, ridgeZ), new(x0, y0, hgt)], Normal = new Vector3(-1, 0, 0),
            Color = inn ? Cel.Wood : Cel.Plaster,
            Local = TownView.Local(new Vector3(x0, y0, 0), new Vector3(0, 1, 0), TownView.Below),
            Decal = ci => Side(ci, depth, hgt),
        };
        var southWall = new Face
        {
            Points = [new(x0, y1, 0), new(x1, y1, 0), new(x1, y1, hgt), new(x0, y1, hgt)], Normal = new Vector3(0, 1, 0),
            Color = inn ? Cel.Wood : Cel.Plaster,
            Local = TownView.Local(new Vector3(x0, y1, 0), Vector3.Right, TownView.Below),
            Decal = ci => Front(ci, w, hgt),
        };
        _frontLocal = southWall.Local;
        var eastWall = new Face
        {
            Points = [new(x1, y1, 0), new(x1, y0, 0), new(x1, y0, hgt), new(x1, mid, ridgeZ), new(x1, y1, hgt)], Normal = new Vector3(1, 0, 0),
            Color = inn ? Cel.Wood : Cel.Plaster,
            Local = TownView.Local(new Vector3(x1, y1, 0), new Vector3(0, -1, 0), TownView.Below),
            Decal = ci => Side(ci, depth, hgt),
        };
        // 远侧的马头墙先画：它朝镜头的内侧面在墙身以下会被南墙挡住，只露出高出屋面的部分。
        List<Face> westGable = [];
        List<Face> eastGable = [];
        if (gable)
        {
            westGable = GableWall(x0, y0, y1, eaveZ, ridgeZ, over);
            eastGable = GableWall(x1, y0, y1, eaveZ, ridgeZ, over);
        }

        var (backGable, frontGable) = TownView.ToCamera.X < 0 ? (eastGable, westGable) : (westGable, eastGable);
        Faces.AddRange(backGable);
        Faces.AddRange([north, west, southWall, eastWall]);

        if (inn)
        {
            // 腰檐：楼上楼下之间挑出一圈单坡瓦檐（南、东、西三面，背对镜头的一面被剔除）。
            const float waist = 330;
            const float reach = 70;
            Faces.Add(Slope(new Vector3(x1 + reach, y1 + reach, waist - 20), new Vector3(x1 + reach, y0, waist - 20), new Vector3(x1, y0, waist + 30), new Vector3(x1, y1, waist + 30), _h.Seed + 2));
            Faces.Add(Slope(new Vector3(x0 - reach, y0, waist - 20), new Vector3(x0 - reach, y1 + reach, waist - 20), new Vector3(x0, y1, waist + 30), new Vector3(x0, y0, waist + 30), _h.Seed + 4));
            Faces.Add(Slope(new Vector3(x0 - reach, y1 + reach, waist - 20), new Vector3(x1 + reach, y1 + reach, waist - 20), new Vector3(x1, y1, waist + 30), new Vector3(x0, y1, waist + 30), _h.Seed + 3));
        }

        // 靠镜头一侧的马头墙压在屋面之上。
        // 屋面：北坡先画（在后），南坡压在上面。
        Faces.Add(Slope(new Vector3(x1 + ox, y0 - over, eaveZ), new Vector3(x0 - ox, y0 - over, eaveZ), new Vector3(x0 - ox, mid, ridgeZ), new Vector3(x1 + ox, mid, ridgeZ), _h.Seed + 1));
        Faces.Add(Slope(new Vector3(x0 - ox, y1 + over, eaveZ), new Vector3(x1 + ox, y1 + over, eaveZ), new Vector3(x1 + ox, mid, ridgeZ), new Vector3(x0 - ox, mid, ridgeZ), _h.Seed));
        Faces.AddRange(Solid.Box(new Vector3(x0 - ox + 8, mid - 16, ridgeZ - 6), new Vector3(x1 + ox - 8, mid + 16, ridgeZ + 26), Cel.TileDark, 1.8f));
        if (!gable)
        {
            _horns.Add((new Vector3(x0 - ox + 8, mid, ridgeZ + 26), -1));
            _horns.Add((new Vector3(x1 + ox - 8, mid, ridgeZ + 26), 1));
        }

        Faces.AddRange(frontGable);

        if (inn)
        {
            // 幌子：竹竿立在客栈西侧巷口，旗面朝街。
            var pole = new Vector2(x0 - 40, y1 + 50);
            var flagLocal = TownView.Local(new Vector3(pole.X - 110, pole.Y, hgt + 70), Vector3.Right, TownView.Below);
            Faces.AddRange(Solid.Box(new Vector3(pole.X - 7, pole.Y - 7, 0), new Vector3(pole.X + 7, pole.Y + 7, hgt + 90), Cel.WoodDark, 1.4f));
            _flag = new Face
            {
                Points = [new(pole.X - 110, pole.Y, hgt + 70), new(pole.X - 8, pole.Y, hgt + 70), new(pole.X - 8, pole.Y, hgt - 150), new(pole.X - 110, pole.Y, hgt - 150)],
                Normal = new Vector3(0, 1, 0), Color = Cel.Paper,
                Local = flagLocal,
                Decal = ci =>
                {
                    ci.DrawRect(new Rect2(6, 6, 90, 208), UiPalette.Cinnabar, false, 6);
                    FlagText(ci);
                },
            };
            Faces.Add(_flag);
        }
    }

    /// <summary>一片瓦坡：a–b 为檐口，c–d 为屋脊（或上沿）；贴花局部 x 沿檐口、y 自上沿向檐口。</summary>
    private static Face Slope(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int seed)
    {
        var along = (b - a).Normalized();
        var downSlope = a - d;
        var length = downSlope.Length();
        var normal = along.Cross(d - a).Normalized();
        if (normal.Z < 0) normal = -normal;
        var width = (b - a).Length();
        return new Face
        {
            Points = [a, b, c, d], Normal = normal, Color = Cel.Tile,
            Local = TownView.Local(d, along, downSlope / length),
            Decal = ci => Cel.TileDecal(ci, width, length, seed),
        };
    }

    /// <summary>马头墙：墙端粉墙高出屋面，前后各两级跌落，每级压一道黛瓦墙帽。截面（y, z）沿 x 挤出。</summary>
    private static List<Face> GableWall(float x, float y0, float y1, float eaveZ, float ridgeZ, float over)
    {
        var step = (y1 - y0) / 6;
        float Roof(float y) => eaveZ + Mathf.Min(y1 + over - y, y - (y0 - over)) * Pitch;
        var zA = Roof(y1 - step) + 34;
        var zB = Roof(y1 - 2 * step) + 34;
        var zC = ridgeZ + 50;
        Vector2[] profile =
        [
            new(y1 + 24, 0), new(y1 + 24, zA), new(y1 - step, zA), new(y1 - step, zB), new(y1 - 2 * step, zB), new(y1 - 2 * step, zC),
            new(y0 + 2 * step, zC), new(y0 + 2 * step, zB), new(y0 + step, zB), new(y0 + step, zA), new(y0 - 24, zA), new(y0 - 24, 0),
        ];
        var faces = Solid.ExtrudeX(profile, x - 18, x + 18, Cel.Plaster, 1.8f);
        foreach (var (ya, yb, z) in new[] { (y1 - step, y1 + 24, zA), (y1 - 2 * step, y1 - step, zB), (y0 + 2 * step, y1 - 2 * step, zC), (y0 + step, y0 + 2 * step, zB), (y0 - 24, y0 + step, zA) })
        {
            faces.AddRange(Solid.Box(new Vector3(x - 30, ya - 8, z), new Vector3(x + 30, yb + 8, z + 18), Cel.TileDark, 1.6f));
        }

        Solid.Sort(faces);
        return faces;
    }

    private void Front(CanvasItem ci, float w, float h)
    {
        switch (_h.Style)
        {
            case HouseStyle.Inn:
                InnFront(ci, w, h);
                break;
            case HouseStyle.Shop:
                Cel.PlasterDecal(ci, w, h, _h.Seed);
                ShopFront(ci, w, h);
                break;
            default:
                Cel.PlasterDecal(ci, w, h, _h.Seed);
                HouseFront(ci, w, h);
                break;
        }
    }

    private void Side(CanvasItem ci, float depth, float h)
    {
        if (_h.Style == HouseStyle.Inn)
        {
            ci.DrawRect(new Rect2(0, -330, depth, 34), Cel.WoodDark);
            for (var x = 90f; x < depth - 150; x += 230)
            {
                Cel.LatticeWindow(ci, new Rect2(x, -h + 90, 130, 110));
                Cel.LatticeWindow(ci, new Rect2(x, -250, 130, 110));
            }

            return;
        }

        Cel.PlasterDecal(ci, depth, h, _h.Seed + 5);
        for (var x = 140f; x < depth - 200; x += 300)
        {
            Cel.LatticeWindow(ci, new Rect2(x, -h * 0.72f, 100, 84));
        }
    }

    private void HouseFront(CanvasItem ci, float w, float h)
    {
        var doorX = w * (0.4f + Cel.Rand(_h.Seed, 20) * 0.2f);
        Cel.Box(ci, new Rect2(doorX - 66, -210, 132, 210), Cel.Stone);
        Cel.Box(ci, new Rect2(doorX - 52, -194, 104, 194), Cel.WoodDark);
        ci.DrawLine(new Vector2(doorX, -194), new Vector2(doorX, 0), Cel.Ink, 2.5f);
        for (var x = doorX - 40; x < doorX + 46; x += 13)
        {
            ci.DrawLine(new Vector2(x, -188), new Vector2(x, -6), Cel.Wood, 2f);
        }

        ci.DrawCircle(new Vector2(doorX - 12, -100), 5, UiPalette.Gilt.Darkened(0.2f));
        ci.DrawCircle(new Vector2(doorX + 12, -100), 5, UiPalette.Gilt.Darkened(0.2f));
        ci.DrawRect(new Rect2(doorX - 74, -224, 148, 16), Cel.TileDark);
        for (var x = 60f; x < w - 150; x += 210)
        {
            if (Mathf.Abs(x + 55 - doorX) < 160)
            {
                continue;
            }

            Cel.LatticeWindow(ci, new Rect2(x, -h * 0.74f, 110, 88));
        }

        if (Cel.Rand(_h.Seed, 30) > 0.35f)
        {
            Cel.HangingLantern(ci, new Vector2(doorX + 100, -h + 44));
        }
    }

    private void ShopFront(CanvasItem ci, float w, float h)
    {
        // 排门板：部分卸下露出店内柜台与暖光。
        var top = -h + 60;
        var panels = (int)((w - 60) / 46);
        for (var i = 0; i < panels; i++)
        {
            var r = new Rect2(30 + i * 46, top, 46, -top - 34);
            var open = i > 1 && i < panels - 2 && Cel.Rand(_h.Seed, i) > 0.25f;
            if (open)
            {
                ci.DrawRect(r, Cel.Interior);
                continue;
            }

            ci.DrawRect(r, i % 2 == 0 ? Cel.Wood : Cel.WoodLight);
            ci.DrawLine(r.Position, r.Position + new Vector2(0, r.Size.Y), Cel.Ink, 2f);
        }

        var inside = new Rect2(30 + 2 * 46, top, (panels - 4) * 46, -top - 34);
        ci.DrawPolygon([inside.Position, inside.Position + new Vector2(inside.Size.X, 0), inside.End, inside.Position + new Vector2(0, inside.Size.Y)],
            [Cel.Glow with { A = 0.05f }, Cel.Glow with { A = 0.05f }, Cel.Glow with { A = 0.4f }, Cel.Glow with { A = 0.4f }]);
        Cel.Box(ci, new Rect2(inside.Position.X + 30, -110, inside.Size.X - 60, 76), Cel.WoodLight);
        ci.DrawRect(new Rect2(30, top, w - 60, -top - 34), Cel.Ink, false, 2.5f);
        ci.DrawRect(new Rect2(20, top - 14, w - 40, 16), Cel.WoodDark);
        if (_h.Sign is not null)
        {
            var board = ShopBoard(h);
            board.Position += new Vector2(w - 110, 0);
            Cel.Box(ci, board, Cel.Paper, 2.5f);
            SignText(ci, w, h);
        }

        Cel.HangingLantern(ci, new Vector2(90, top - 8));
    }

    private void InnFront(CanvasItem ci, float w, float h)
    {
        const float lower = 330;
        // 楼下：木柱开间、槅扇门，中间大门敞开透暖光。
        ci.DrawRect(new Rect2(0, -lower, w, lower - 34), Cel.WoodLight);
        const int bays = 5;
        var bay = w / bays;
        for (var i = 0; i < bays; i++)
        {
            var r = new Rect2(i * bay + 16, -lower + 60, bay - 32, lower - 94);
            if (i == bays / 2)
            {
                ci.DrawRect(r, Cel.Interior);
                ci.DrawPolygon([r.Position, r.Position + new Vector2(r.Size.X, 0), r.End, r.Position + new Vector2(0, r.Size.Y)],
                    [Cel.Glow with { A = 0.15f }, Cel.Glow with { A = 0.15f }, Cel.Glow with { A = 0.6f }, Cel.Glow with { A = 0.6f }]);
                Cel.Box(ci, new Rect2(r.Position.X + 30, r.End.Y - 70, r.Size.X - 60, 70), Cel.Wood);
                continue;
            }

            Cel.LatticeWindow(ci, new Rect2(r.Position.X + 6, r.Position.Y + 8, r.Size.X - 12, 100));
            Cel.Box(ci, new Rect2(r.Position.X + 6, r.Position.Y + 120, r.Size.X - 12, r.Size.Y - 120), Cel.Wood);
        }

        for (var i = 0; i <= bays; i++)
        {
            Cel.Box(ci, new Rect2(i * bay - 9, -lower, 18, lower - 34), Cel.WoodDark, 2f);
        }

        ci.DrawRect(new Rect2(0, -34, w, 34), Cel.Stone);
        Cel.HangingLantern(ci, new Vector2(w / 2 - bay * 0.6f, -lower + 50));
        Cel.HangingLantern(ci, new Vector2(w / 2 + bay * 0.6f, -lower + 50));

        // 楼上：木板壁、一排槅窗、正中匾额。
        ci.DrawRect(new Rect2(0, -h, w, 40), Cel.WoodDark);
        for (var i = 0; i < bays; i++)
        {
            if (i == bays / 2) continue;
            Cel.LatticeWindow(ci, new Rect2(i * bay + 26, -h + 90, bay - 52, 120));
        }

        if (_h.Sign is not null)
        {
            var plaque = InnPlaque(w, h);
            Cel.Box(ci, plaque, UiPalette.PanelDark, 3f);
            ci.DrawRect(plaque.Grow(-7), UiPalette.Gilt, false, 2f);
            SignText(ci, w, h);
        }
    }

    private static Rect2 ShopBoard(float h) => new(0, -h + 80, 64, 160);

    private static Rect2 InnPlaque(float w, float h) => new(w / 2 - 190, -h + 100, 380, 84);

    /// <summary>
    /// 招牌字（正面局部坐标）。引导图（导出时 PieceArt 关闭）不画字，AI 只画空白招牌；贴上 AI 件后由 <see cref="_Draw"/> 在同一位置补写，
    /// 字形与字体台账一致，不依赖模型写汉字。
    /// </summary>
    private void SignText(CanvasItem ci, float w, float h)
    {
        if (_h.Sign is not { } sign || Face.StructureOnly || !PieceArt.Enabled)
        {
            return;
        }

        if (_h.Style == HouseStyle.Inn)
        {
            var plaque = InnPlaque(w, h);
            var size = UiFonts.Title.GetStringSize(sign, HorizontalAlignment.Left, -1, 52);
            ci.DrawString(UiFonts.Title, new Vector2(plaque.GetCenter().X - size.X / 2, plaque.GetCenter().Y + 18), sign,
                HorizontalAlignment.Left, -1, 52, UiPalette.Gilt);
            return;
        }

        var board = ShopBoard(h);
        board.Position += new Vector2(w - 110, 0);
        ci.DrawString(UiFonts.Title, new Vector2(board.Position.X + 8, board.Position.Y + 98), sign, HorizontalAlignment.Left, -1, 48, Cel.Ink);
    }

    public override void _Draw()
    {
        base._Draw();
        if (Art is null || _h.Sign is null)
        {
            return;
        }

        DrawSetTransformMatrix(_frontLocal);
        SignText(this, _h.X1 - _h.X0, _h.WallHeight);
        DrawSetTransformMatrix(Transform2D.Identity);

        // 幌子整面补画：AI 件的旗面颜色不定，墨字会看不清，按占位画法（纸色旗面、朱边、墨字）压在贴图上。
        _flag?.Draw(this);
    }

    private static void FlagText(CanvasItem ci)
    {
        if (!Face.StructureOnly && PieceArt.Enabled) ci.DrawString(UiFonts.Title, new Vector2(18, 130), "客", HorizontalAlignment.Left, -1, 64, Cel.Ink);
    }

    protected override void DrawExtras()
    {
        // 屋脊两端起翘（屏幕空间笔画，随投影落点）。
        foreach (var (tip, dir) in _horns)
        {
            var p = TownView.P(tip);
            var sx = TownView.ScreenX(new Vector2(dir, 0)) >= 0 ? 1 : -1;
            Vector2[] horn = [p, p + new Vector2(sx * 10, -10), p + new Vector2(sx * 14, -26), p + new Vector2(sx * 8, -32)];
            DrawPolyline(horn, Cel.TileDark, 7, true);
            DrawPolyline(horn, Cel.Ink, 1.6f, true);
        }
    }
}

/// <summary>廊棚的一件：屋面（Overhead，压在廊下行人之上）、柱或坐栏。</summary>
public partial class TownCorridorPart : TownPiece
{
    private readonly List<Vector3> _lanternTops = [];

    public static IEnumerable<TownCorridorPart> Build(Rect2 area)
    {
        const float eaveZ = 250;
        const float ridgeZ = 330;
        const float over = 40;
        const float bay = 140;
        var (x0, y0, x1, y1) = (area.Position.X, area.Position.Y, area.End.X, area.End.Y);
        var mid = (y0 + y1) / 2;

        var roof = new TownCorridorPart { Overhead = true };
        roof.Faces.Add(new Face
        {
            Points = [new(x1 + over, y0 - over, eaveZ), new(x0 - over, y0 - over, eaveZ), new(x0 - over, mid, ridgeZ), new(x1 + over, mid, ridgeZ)],
            Normal = new Vector3(0, -(ridgeZ - eaveZ), mid - (y0 - over)).Normalized(), Color = Cel.Tile,
        });
        var slope = new Vector3(0, y1 + over - mid, eaveZ - ridgeZ);
        var len = slope.Length();
        roof.Faces.Add(new Face
        {
            Points = [new(x0 - over, y1 + over, eaveZ), new(x1 + over, y1 + over, eaveZ), new(x1 + over, mid, ridgeZ), new(x0 - over, mid, ridgeZ)],
            Normal = new Vector3(0, ridgeZ - eaveZ, y1 + over - mid).Normalized(), Color = Cel.Tile,
            Local = TownView.Local(new Vector3(x0 - over, mid, ridgeZ), Vector3.Right, slope / len),
            Decal = ci => Cel.TileDecal(ci, x1 - x0 + 2 * over, len, 41),
        });
        roof.Faces.AddRange(Solid.Box(new Vector3(x0 - over + 6, mid - 12, ridgeZ - 4), new Vector3(x1 + over - 6, mid + 12, ridgeZ + 18), Cel.TileDark, 1.6f));
        // 檐下横梁（河沿一侧）。
        roof.Faces.AddRange(Solid.Box(new Vector3(x0 - 10, y1 - 10, eaveZ - 34), new Vector3(x1 + 10, y1 + 10, eaveZ - 8), Cel.WoodDark, 1.4f));
        for (var x = x0 + bay * 1.5f; x < x1; x += bay * 2)
        {
            roof._lanternTops.Add(new Vector3(x, y1, eaveZ - 34));
        }

        roof.Seal(area);
        roof.UseArt("town.corridor.roof");
        yield return roof;

        for (var x = x0; x <= x1 + 1; x += bay)
        {
            foreach (var y in new[] { y0, y1 })
            {
                var pillar = new TownCorridorPart { Occluder = false };
                pillar.Faces.AddRange(Solid.Box(new Vector3(x - 14, y - 14, 0), new Vector3(x + 14, y + 14, 12), Cel.Stone, 1.4f));
                pillar.Faces.AddRange(Solid.Box(new Vector3(x - 9, y - 9, 12), new Vector3(x + 9, y + 9, eaveZ - 8), Cel.Wood, 1.6f));
                pillar.Seal(new Rect2(x - 14, y - 14, 28, 28));
                yield return pillar;
            }

            // 河沿一侧的坐栏（美人靠），每三间留一个口下到河边。
            if (x + bay <= x1 + 1 && Mathf.PosMod(Mathf.RoundToInt((x - x0) / bay), 3) != 1)
            {
                var bench = new TownCorridorPart { Occluder = false };
                bench.Faces.AddRange(Solid.Box(new Vector3(x + 10, y1 - 30, 40), new Vector3(x + bay - 10, y1 - 4, 50), Cel.WoodLight, 1.4f));
                bench.Faces.AddRange(Solid.Box(new Vector3(x + 10, y1 - 8, 50), new Vector3(x + bay - 10, y1 + 4, 88), Cel.Wood, 1.4f));
                bench.Seal(new Rect2(x + 10, y1 - 30, bay - 20, 34));
                yield return bench;
            }
        }
    }

    /// <summary>贴上 AI 屋面后灯笼仍由引擎挂在檐下（引导图不含灯笼，导出时屋面件无贴图照常画出）。</summary>
    public override void _Draw()
    {
        base._Draw();
        if (Art is not null) DrawExtras();
    }

    protected override void DrawExtras()
    {
        if (!PieceArt.Enabled) return;
        foreach (var top in _lanternTops)
        {
            DrawSetTransform(TownView.P(top), 0, Vector2.One * TownView.Upright);
            Cel.HangingLantern(this, Vector2.Zero, 0.9f);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }
}

/// <summary>树：立着的精灵，垂柳（丝绦下垂）与樟树（团块树冠），三阶绿色，光自左上。</summary>
public partial class TownTreeNode : TownPiece
{
    private const float DrawScale = 1.8f;
    private readonly TownTree _tree;

    public TownTreeNode(TownTree tree)
    {
        _tree = tree;
        Position = TownView.P(tree.Position);
        Foot = new Rect2(tree.Position - new Vector2(20, 20), new Vector2(40, 40));
        var s = tree.Size * DrawScale * TownView.Upright;
        var local = new Rect2(-160 * s, -360 * s, 320 * s, 380 * s);
        ScreenBox = new Rect2(Position + local.Position, local.Size);
        UseArt($"town.tree.{tree.Seed}");
    }

    public override void _Draw()
    {
        var s = _tree.Size;
        DrawSetTransform(-Position, 0, Vector2.One);
        Cel.GroundShadow(this, _tree.Position + new Vector2(60, -60) * s, 230 * s, 170 * s, 0.16f);
        if (Art is not null)
        {
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            DrawArt();
            return;
        }

        DrawSetTransform(Vector2.Zero, 0, Vector2.One * (DrawScale * TownView.Upright));
        if (_tree.Kind == TreeKind.Willow)
        {
            DrawWillow(s, _tree.Seed);
        }
        else
        {
            DrawCamphor(s, _tree.Seed);
        }

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    private void DrawTrunk(float s, Vector2 top, float baseWidth)
    {
        Vector2[] trunk =
        [
            new(-baseWidth * s, 0), new(baseWidth * s, 0),
            new(top.X + baseWidth * 0.45f * s, top.Y), new(top.X - baseWidth * 0.45f * s, top.Y),
        ];
        Cel.Shape(this, trunk, Cel.Bark, 1.4f);
        DrawLine(new Vector2(-baseWidth * 0.55f * s, -4), top + new Vector2(-baseWidth * 0.2f * s, 0), Cel.BarkLight, 4 * s);
    }

    private void DrawWillow(float s, int seed)
    {
        var crown = new Vector2(-10 * s, -250 * s);
        DrawTrunk(s, crown + new Vector2(0, 30 * s), 13);
        DrawLine(crown + new Vector2(0, 40 * s), crown + new Vector2(-70 * s, -30 * s), Cel.Bark, 6 * s);
        DrawLine(crown + new Vector2(0, 40 * s), crown + new Vector2(60 * s, -40 * s), Cel.Bark, 6 * s);
        foreach (var (dx, dy, r) in new[] { (-60f, -40f, 60f), (10f, -60f, 70f), (70f, -30f, 55f) })
        {
            DrawColoredPolygon(Cel.Ellipse(crown + new Vector2(dx, dy) * s, r * s, r * 0.6f * s, 20), Cel.LeafDark);
        }

        Color[] tones = [Cel.LeafDark, Cel.Leaf, Cel.LeafLight];
        for (var layer = 0; layer < 3; layer++)
        {
            for (var i = 0; i < 30; i++)
            {
                var k = layer * 100 + i;
                var a = Mathf.Pi * (1.05f + 0.9f * Cel.Rand(seed, k));
                var start = crown + new Vector2(Mathf.Cos(a) * 125 * s, Mathf.Sin(a) * 70 * s + 10 * s);
                if (layer == 2 && start.X > crown.X + 40 * s)
                {
                    continue;
                }

                var len = (110 + 120 * Cel.Rand(seed, k + 50)) * s;
                var bend = (start.X - crown.X) * 0.18f;
                Vector2[] strand = [start, start + new Vector2(bend * 0.5f, len * 0.4f), start + new Vector2(bend * 0.8f, len * 0.75f), start + new Vector2(bend, len)];
                DrawPolyline(strand, tones[layer], (layer == 0 ? 8 : 5.5f) * s, true);
            }
        }

        for (var i = 0; i < 14; i++)
        {
            var p = crown + new Vector2((Cel.Rand(seed, 400 + i) - 0.7f) * 180 * s, (Cel.Rand(seed, 420 + i) - 0.3f) * 150 * s);
            DrawLine(p, p + new Vector2(0, 26 * s), Cel.LeafGlint, 3 * s, true);
        }
    }

    private void DrawCamphor(float s, int seed)
    {
        var crown = new Vector2(0, -230 * s);
        DrawTrunk(s, crown + new Vector2(0, 60 * s), 18);
        var blobs = new List<(Vector2 C, float R)>();
        for (var i = 0; i < 9; i++)
        {
            var a = Mathf.Tau * i / 9 + Cel.Rand(seed, i);
            var d = i == 0 ? 0 : 70 + 30 * Cel.Rand(seed, i + 20);
            blobs.Add((crown + new Vector2(Mathf.Cos(a) * d * 1.3f, Mathf.Sin(a) * d * 0.7f) * s, (55 + 25 * Cel.Rand(seed, i + 40)) * s));
        }

        foreach (var (c, r) in blobs)
        {
            DrawColoredPolygon(Cel.Ellipse(c + new Vector2(6, 10) * s, r, r * 0.85f, 22), Cel.LeafDark);
        }

        foreach (var (c, r) in blobs)
        {
            DrawColoredPolygon(Cel.Ellipse(c, r * 0.92f, r * 0.78f, 22), Cel.Leaf);
            DrawColoredPolygon(Cel.Ellipse(c + new Vector2(-r * 0.25f, -r * 0.25f), r * 0.55f, r * 0.42f, 18), Cel.LeafLight);
            DrawPolyline(Cel.Ellipse(c + new Vector2(6, 10) * s, r, r * 0.85f, 16, 0.15f, Mathf.Pi - 0.15f), Cel.Ink with { A = 0.55f }, 2, true);
        }
    }
}

/// <summary>街面杂件：告示牌、摊位、井台、乌篷船为立体件；旧渡石痕与酒坛为立着的精灵。</summary>
public partial class TownPropNode : TownPiece
{
    private readonly TownProp _prop;
    private readonly List<(Vector3 At, Color Tone)> _goods = [];
    private readonly Vector2 _sprite;

    public TownPropNode(TownProp prop)
    {
        _prop = prop;
        Occluder = prop.Kind is PropKind.Stall or PropKind.NoticeBoard or PropKind.Well;
        var p = prop.Position;
        switch (prop.Kind)
        {
            case PropKind.NoticeBoard:
                BuildNotice(p);
                Seal(new Rect2(p - new Vector2(100, 34), new Vector2(200, 68)));
                break;
            case PropKind.Stall:
                BuildStall(p);
                Seal(new Rect2(p - new Vector2(125, 70), new Vector2(250, 140)));
                break;
            case PropKind.Well:
                BuildWell(p);
                Seal(new Rect2(p - new Vector2(66, 64), new Vector2(132, 128)));
                break;
            case PropKind.Boat:
                BuildBoat(p);
                Seal(new Rect2(p - new Vector2(170, 50), new Vector2(340, 100)));
                break;
            default:
                _sprite = TownView.P(p);
                Foot = new Rect2(p - new Vector2(60, 30), new Vector2(120, 60));
                ScreenBox = new Rect2(_sprite + new Vector2(-80, -120) * TownView.Upright, new Vector2(160, 130) * TownView.Upright);
                break;
        }

        UseArt($"town.{prop.Id}");
    }

    public bool IsBoat => _prop.Kind == PropKind.Boat;

    /// <summary>船随水轻晃：页面逐帧写入屏幕纵向偏移。</summary>
    public void Bob(float offset) => Position = new Vector2(0, offset);

    private void BuildNotice(Vector2 p)
    {
        foreach (var dx in new[] { -60f, 60f })
        {
            Faces.AddRange(Solid.Box(new Vector3(p.X + dx - 7, p.Y - 7, 0), new Vector3(p.X + dx + 7, p.Y + 7, 190), Cel.WoodDark, 1.4f));
        }

        Faces.AddRange(Solid.Box(new Vector3(p.X - 80, p.Y - 5, 90), new Vector3(p.X + 80, p.Y + 5, 196), Cel.WoodLight, 1.8f));
        Faces.Add(new Face
        {
            Points = [new(p.X - 80, p.Y + 5.5f, 90), new(p.X + 80, p.Y + 5.5f, 90), new(p.X + 80, p.Y + 5.5f, 196), new(p.X - 80, p.Y + 5.5f, 196)],
            Normal = new Vector3(0, 1, 0), Color = Cel.WoodLight,
            Local = TownView.Local(new Vector3(p.X - 80, p.Y + 5.5f, 196), Vector3.Right, TownView.Below),
            Decal = ci =>
            {
                foreach (var (x, y, w, h) in new[] { (10f, 10f, 50f, 70f), (70f, 14f, 42f, 60f), (120f, 8f, 32f, 80f) })
                {
                    Cel.Box(ci, new Rect2(x, y, w, h), Cel.Paper, 1.4f);
                    for (var ly = y + 12; ly < y + h - 6; ly += 11)
                    {
                        ci.DrawLine(new Vector2(x + 7, ly), new Vector2(x + w - 7, ly), Cel.Ink with { A = 0.55f }, 1.8f);
                    }
                }

                ci.DrawRect(new Rect2(120, 8, 32, 10), UiPalette.Cinnabar);
            },
        });
        Faces.AddRange(Solid.ExtrudeX([new(p.Y - 34, 196), new(p.Y + 34, 196), new(p.Y, 226)], p.X - 100, p.X + 100, Cel.Tile, 1.6f));
    }

    private void BuildStall(Vector2 p)
    {
        foreach (var dx in new[] { -110f, 110f })
        {
            Faces.AddRange(Solid.Box(new Vector3(p.X + dx - 5, p.Y - 55, 0), new Vector3(p.X + dx + 5, p.Y - 45, 230), Cel.WoodDark, 1.2f));
        }

        Faces.AddRange(Solid.Box(new Vector3(p.X - 100, p.Y - 40, 0), new Vector3(p.X + 100, p.Y + 40, 80), Cel.WoodLight, 1.8f));
        foreach (var (dx, tone) in new[] { (-60f, UiPalette.Ochre), (-5f, Cel.Leaf), (50f, UiPalette.Warm) })
        {
            _goods.Add((new Vector3(p.X + dx, p.Y, 80), tone));
        }

        // 布篷：后高前低，蓝白条。
        var a = new Vector3(p.X - 125, p.Y + 70, 190);
        var b = new Vector3(p.X + 125, p.Y + 70, 190);
        var c = new Vector3(p.X + 125, p.Y - 60, 232);
        var d = new Vector3(p.X - 125, p.Y - 60, 232);
        var slope = a - d;
        var len = slope.Length();
        var normal = (b - a).Cross(d - a).Normalized();
        if (normal.Z < 0) normal = -normal;
        Faces.Add(new Face
        {
            Points = [a, b, c, d], Normal = normal, Color = Cel.Cloth,
            Local = TownView.Local(d, Vector3.Right, slope / len),
            Decal = ci =>
            {
                for (var x = 0f; x < 250; x += 62.5f)
                {
                    ci.DrawRect(new Rect2(x, 0, 31, len), Cel.Plaster with { A = 0.85f });
                }
            },
        });
    }

    private void BuildWell(Vector2 p)
    {
        var ring = new Vector2[12];
        for (var i = 0; i < ring.Length; i++)
        {
            var a = Mathf.Tau * i / ring.Length;
            ring[i] = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 60;
        }

        Faces.AddRange(Solid.ExtrudeZ(ring, 0, 64, Cel.Stone, Cel.StoneLight, 1.6f));
        var water = ring.Select(v => new Vector3(p.X + (v.X - p.X) * 0.7f, p.Y + (v.Y - p.Y) * 0.7f, 64.5f)).ToArray();
        Faces.Add(new Face { Points = water, Normal = Vector3.Zero, Color = Color.FromHtml("#1E3A40"), Outline = 1.2f, Lit = false });
        foreach (var dx in new[] { -56f, 56f })
        {
            Faces.AddRange(Solid.Box(new Vector3(p.X + dx - 6, p.Y - 6, 64), new Vector3(p.X + dx + 6, p.Y + 6, 190), Cel.WoodDark, 1.2f));
        }

        Faces.AddRange(Solid.Box(new Vector3(p.X - 66, p.Y - 7, 180), new Vector3(p.X + 66, p.Y + 7, 196), Cel.Wood, 1.2f));
        Faces.AddRange(Solid.Box(new Vector3(p.X - 16, p.Y - 16, 150), new Vector3(p.X + 16, p.Y + 16, 180), Cel.WoodLight, 1.2f));
    }

    private void BuildBoat(Vector2 p)
    {
        var len = _prop.Seed == 1 ? 1f : 0.85f;
        var z = TownSamples.WaterLevel;
        // 船身：侧面轮廓（x, z）沿 y 挤出，两头翘起。
        Vector2[] hull =
        [
            new(p.X - 120 * len, z - 18), new(p.X + 120 * len, z - 18), new(p.X + 170 * len, z + 30),
            new(p.X + 120 * len, z + 22), new(p.X - 120 * len, z + 22), new(p.X - 165 * len, z + 28),
        ];
        Faces.AddRange(Solid.ExtrudeY(hull, p.Y - 46, p.Y + 46, Color.FromHtml("#5B4232"), 1.8f));
        // 乌篷：拱形截面（y, z）沿 x 挤出，两段。
        foreach (var (a, b, h) in new[] { (-80f, 30f, 70f), (36f, 90f, 54f) })
        {
            var arch = new Vector2[11];
            for (var i = 0; i <= 10; i++)
            {
                var t = Mathf.Pi * i / 10;
                arch[i] = new Vector2(p.Y - Mathf.Cos(t) * 44, z + 22 + Mathf.Sin(t) * h);
            }

            Faces.AddRange(Solid.ExtrudeX(arch, p.X + a * len, p.X + b * len, Color.FromHtml("#2E3A46"), 1.4f));
        }
    }

    public override void _Draw()
    {
        if (_prop.Kind is PropKind.StoneMark or PropKind.Jars)
        {
            Cel.GroundShadow(this, _prop.Position + new Vector2(10, -10), _prop.Kind == PropKind.Jars ? 90 : 45, 34, 0.2f);
            if (Art is not null)
            {
                DrawArt();
                return;
            }

            DrawSetTransform(_sprite, 0, Vector2.One * TownView.Upright);
            if (_prop.Kind == PropKind.StoneMark)
            {
                DrawStone();
            }
            else
            {
                DrawJars();
            }

            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            return;
        }

        if (_prop.Kind != PropKind.Boat)
        {
            Cel.GroundShadow(this, _prop.Position + new Vector2(30, -30), 130, 70, 0.18f);
        }

        base._Draw();
    }

    protected override void DrawExtras()
    {
        foreach (var (at, tone) in _goods)
        {
            var c = TownView.P(at);
            var u = TownView.Upright;
            Cel.Shape(this, Cel.Ellipse(c, 26 * u, 12 * u, 16), Color.FromHtml("#B99462"), 1.6f);
            DrawColoredPolygon(Cel.Ellipse(c - new Vector2(0, 5 * u), 19 * u, 8 * u, 14), tone);
        }
    }

    private void DrawStone()
    {
        Vector2[] stone = [new(-30, 0), new(-36, -40), new(-26, -86), new(-4, -98), new(20, -90), new(32, -52), new(30, 0)];
        Cel.Shape(this, stone, Color.FromHtml("#8D9A94"));
        DrawColoredPolygon([new(-30, 0), new(-36, -40), new(-26, -86), new(-4, -98), new(-8, -40), new(-12, 0)], Color.FromHtml("#A9B5AE"));
        foreach (var y in new[] { -30f, -46f, -62f })
        {
            DrawLine(new Vector2(-22, y), new Vector2(24, y + 3), Cel.Ink with { A = 0.7f }, 2, true);
        }

        DrawArc(new Vector2(4, -76), 8, 0.3f, Mathf.Tau - 0.3f, 14, Cel.Ink with { A = 0.7f }, 2, true);
        DrawColoredPolygon([new(-30, 0), new(30, 0), new(28, -12), new(-32, -10)], Cel.Leaf with { A = 0.7f });
        Cel.Outline(this, stone, 2.2f);
    }

    private void DrawJars()
    {
        for (var i = 0; i < 3; i++)
        {
            var c = new Vector2(i * 34 - 34, -4 - (i == 1 ? 8 : 0));
            Cel.Shape(this, Cel.Ellipse(c + new Vector2(0, -24), 20, 26), Color.FromHtml("#6A4A32"), 1.8f);
            DrawColoredPolygon(Cel.Ellipse(c + new Vector2(-6, -30), 7, 14, 12), Color.FromHtml("#8A6848"));
            Cel.Box(this, new Rect2(c.X - 10, c.Y - 56, 20, 10), Color.FromHtml("#3A2A20"), 1.4f);
            Cel.Box(this, new Rect2(c.X - 8, c.Y - 30, 16, 14), UiPalette.Cinnabar, 1.2f);
        }
    }
}

/// <summary>平桥的栏杆：一侧一条，参与前后排序（行人走在两栏之间，东栏压在人前）。</summary>
public partial class TownRailNode : TownPiece
{
    public TownRailNode(float x, Rect2 bridge, string? artId = null)
    {
        Occluder = false;
        var (y0, y1) = (bridge.Position.Y, bridge.End.Y);
        Faces.AddRange(Solid.Box(new Vector3(x - 8, y0, 30), new Vector3(x + 8, y1, 44), Cel.StoneLight, 1.4f));
        for (var y = y0; y <= y1 + 1; y += 100)
        {
            Faces.AddRange(Solid.Box(new Vector3(x - 12, y - 12, 0), new Vector3(x + 12, y + 12, 62), Cel.Stone, 1.4f));
        }

        Solid.Sort(Faces);
        Seal(new Rect2(x - 12, y0 - 12, 24, y1 - y0 + 24));
        if (artId is not null) UseArt(artId);
    }
}

/// <summary>占位形象的装束：只为核对比例与辨认人物，正式形象为 4 向 / 8 向精灵（M1 起）。</summary>
public enum FigureLook
{
    /// <summary>主角：束发。</summary>
    Hero,

    /// <summary>陆青禾：斗笠、肩扛长篙。</summary>
    Boatwoman,

    /// <summary>客栈掌柜乔红绡：高髻插簪、窄袖长裙。</summary>
    Keeper,

    /// <summary>店小二：小帽、肩搭白巾。</summary>
    Waiter,

    /// <summary>茶客：坐在长凳上，束发。</summary>
    Seated,
}

/// <summary>
/// 探索形象占位：人高 175 世界单位，按投影竖直比例缩放（俯 30° 时约 152 逻辑像素，架构文档 10.2 的 120–180），
/// 左右朝向，走动时身体起伏、两腿交替。陆青禾戴斗笠、肩扛长篙以便核对比例；室内 NPC 为站立或坐姿的静态剪影。
/// </summary>
public partial class WalkerFigure : TownPiece
{
    public Color Tone { get; init; } = UiPalette.Accent;
    public FigureLook Look { get; init; }
    public int Facing { get; set; } = 1;
    public float Phase { get; set; }
    public bool Moving { get; set; }

    /// <summary>坐姿时凳面高度（世界单位）。</summary>
    private const float SeatZ = 45;

    /// <summary>世界坐标（地面）与高度（石阶上为负）。</summary>
    public Vector2 Ground { get; private set; }

    public float Z { get; private set; }

    public float Height => Look switch
    {
        FigureLook.Boatwoman or FigureLook.Keeper => 165,
        FigureLook.Seated => 130,
        _ => 175,
    } * TownView.Upright;

    public WalkerFigure() => Walker = true;

    public void Place(Vector2 ground, float z = 0)
    {
        Ground = ground;
        Z = z;
        Foot = new Rect2(ground - new Vector2(20, 20), new Vector2(40, 40));
        Position = TownView.P(ground, z);
        ScreenBox = new Rect2(Position + new Vector2(-40, -Height - 20), new Vector2(80, Height + 30));
    }

    public override void _Draw()
    {
        DrawSetTransform(-Position, 0, Vector2.One);
        Cel.GroundShadow(this, Ground, 34, 26, 0.3f, Z);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        if (Look == FigureLook.Seated)
        {
            DrawSeated();
            return;
        }

        var h = Height;
        var dir = Facing;
        var bob = Moving ? -Mathf.Abs(Mathf.Sin(Phase)) * 5 : 0;
        var swing = Moving ? Mathf.Sin(Phase) * 12 : 0;
        var skirt = Look == FigureLook.Keeper;

        var dark = Tone.Darkened(0.5f);
        foreach (var side in new[] { -1, 1 })
        {
            var foot = new Vector2(side * 9 + side * swing * 0.8f, 0);
            var hip = new Vector2(side * 8, -h * 0.34f + bob);
            DrawLine(hip, foot + new Vector2(0, -6), dark, 11, true);
            DrawLine(foot + new Vector2(-6, -3), foot + new Vector2(dir * 10, -3), Cel.Ink, 7, true);
        }

        var shoulderY = -h * 0.76f + bob;
        var hemY = skirt ? -8 : -h * (Look == FigureLook.Boatwoman ? 0.3f : 0.2f) + bob;
        var hemX = skirt ? 34 : 30;
        Vector2[] robe =
        [
            new(-22, shoulderY), new(22, shoulderY), new(18, -h * 0.5f + bob), new(hemX, hemY), new(-hemX, hemY), new(-18, -h * 0.5f + bob),
        ];
        Cel.Shape(this, robe, Tone);
        DrawColoredPolygon([new(-dir * 2, shoulderY), new(-dir * 22, shoulderY), new(-dir * 18, -h * 0.5f + bob), new(-dir * hemX, hemY), new(-dir * 2, hemY)],
            Tone.Darkened(0.22f));
        if (skirt)
        {
            // 长裙外罩一截短衫，腰间系带。
            Cel.Shape(this, [new(-22, shoulderY), new(22, shoulderY), new(24, -h * 0.46f + bob), new(-24, -h * 0.46f + bob)], Cel.Paper, 1.8f);
            DrawRect(new Rect2(-20, -h * 0.5f + bob, 40, 6), UiPalette.Cinnabar);
        }
        else
        {
            DrawRect(new Rect2(-21, -h * 0.52f + bob, 42, 7), dark);
        }

        DrawLine(new Vector2(dir * 18, shoulderY + 8), new Vector2(dir * 22 - swing * 0.6f, -h * 0.42f + bob), Tone.Darkened(0.1f), 10, true);
        DrawHead(new Vector2(dir * 2, -h * 0.86f + bob), h, dir, bob);
        if (Look == FigureLook.Waiter)
        {
            // 肩上白巾。
            Cel.Shape(this, [new(-dir * 20, shoulderY - 2), new(-dir * 8, shoulderY - 2), new(-dir * 10, shoulderY + 36), new(-dir * 22, shoulderY + 30)], Cel.Plaster, 1.6f);
        }
    }

    private void DrawHead(Vector2 head, float h, int dir, float bob)
    {
        DrawCircle(head, h * 0.075f, Color.FromHtml("#E8C9A6"));
        DrawColoredPolygon(Cel.Ellipse(head + new Vector2(-dir * 3, -4), h * 0.078f, h * 0.06f, 16, Mathf.Pi, Mathf.Tau), Cel.Ink);
        switch (Look)
        {
            case FigureLook.Boatwoman:
                Cel.Shape(this, [head + new Vector2(-34, 2), head + new Vector2(34, 2), head + new Vector2(0, -24)], Color.FromHtml("#C9A96E"), 1.8f);
                DrawLine(new Vector2(-dir * 40, -h * 1.1f + bob), new Vector2(dir * 44, 0), Cel.WoodLight, 4, true);
                break;
            case FigureLook.Keeper:
                // 高髻与金簪。
                DrawColoredPolygon(Cel.Ellipse(head + new Vector2(-dir * 2, -h * 0.1f), 10, 12, 16), Cel.Ink);
                DrawLine(head + new Vector2(-dir * 14, -h * 0.1f), head + new Vector2(dir * 12, -h * 0.13f), UiPalette.Gilt, 3, true);
                DrawCircle(head + new Vector2(dir * 12, -h * 0.13f), 3.5f, UiPalette.Cinnabar);
                break;
            case FigureLook.Waiter:
                DrawColoredPolygon(Cel.Ellipse(head + new Vector2(0, -h * 0.045f), h * 0.08f, h * 0.045f, 16, Mathf.Pi, Mathf.Tau), Cel.TileDark);
                break;
            default:
                DrawCircle(head + new Vector2(-dir * 4, -h * 0.07f), 7, Cel.Ink);
                break;
        }

        DrawArc(head, h * 0.075f, 0, Mathf.Tau, 20, Cel.Ink, 1.8f, true);
    }

    /// <summary>坐姿：臀在凳面高度，大腿朝前平伸，小腿垂到地面。</summary>
    private void DrawSeated()
    {
        var u = TownView.Upright;
        var dir = Facing;
        var seat = -SeatZ * u;
        var knee = new Vector2(dir * 40, seat - 4);
        var dark = Tone.Darkened(0.5f);
        DrawLine(new Vector2(0, seat), knee, dark, 13, true);
        DrawLine(knee, new Vector2(dir * 42, -4), dark, 11, true);
        DrawLine(new Vector2(dir * 36, -3), new Vector2(dir * 54, -3), Cel.Ink, 7, true);

        var shoulderY = seat - 80 * u;
        Vector2[] robe = [new(-dir * 20, shoulderY), new(dir * 20, shoulderY), new(dir * 26, seat + 4), new(-dir * 24, seat + 4)];
        Cel.Shape(this, robe, Tone);
        DrawColoredPolygon([new(-dir * 20, shoulderY), new(-dir * 2, shoulderY), new(-dir * 2, seat + 4), new(-dir * 24, seat + 4)], Tone.Darkened(0.22f));
        DrawLine(new Vector2(dir * 14, shoulderY + 10), new Vector2(dir * 38, seat - 18), Tone.Darkened(0.1f), 10, true);
        DrawHead(new Vector2(dir * 2, shoulderY - 20 * u), 175 * u, dir, 0);
    }
}

/// <summary>可交互物头顶的泥金菱形，上下浮动；靠近时变亮。</summary>
public partial class InteractMarker : Node2D
{
    public bool Near { get; set; }

    public override void _Draw()
    {
        var bob = Motion.Enabled ? Mathf.Sin(Time.GetTicksMsec() / 260f) * 6 : 0;
        var c = new Vector2(0, bob);
        var r = Near ? 13f : 10f;
        Vector2[] diamond = [c + new Vector2(0, -r * 1.3f), c + new Vector2(r, 0), c + new Vector2(0, r * 1.3f), c + new Vector2(-r, 0)];
        DrawColoredPolygon(diamond, Near ? UiPalette.Gilt.Lightened(0.2f) : UiPalette.Gilt with { A = 0.85f });
        Cel.Outline(this, diamond, 2.4f, UiPalette.Abyss);
    }

    public override void _Process(double delta)
    {
        if (Motion.Enabled)
        {
            QueueRedraw();
        }
    }
}
