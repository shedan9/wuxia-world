using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.Art;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 后院的壳：北面客房楼正立面与东院墙（完整立面，贴 AI 整面墙，未入库时画程序化占位）与东墙头的瓦顶。
/// 立面伸出院外：楼比院子宽，东端露在院墙之上、西端露在矮墙之外。全部靠后，画在排序层之下。
/// </summary>
public partial class YardShell : Node2D
{
    private readonly List<Face> _faces = [];

    public YardShell()
    {
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        var yard = YardSamples.Yard;
        var (w, d) = (yard.Size.X, yard.Size.Y);
        var (o, h, e, t) = (YardSamples.WingOverhang, YardSamples.WingHeight, YardSamples.EastWallHeight, YardSamples.WallThickness);

        _faces.Add(new Face
        {
            Points = [new(-o, 0, 0), new(w + o, 0, 0), new(w + o, 0, h), new(-o, 0, h)], Normal = new Vector3(0, 1, 0), Color = Cel.Plaster,
            Local = TownView.Local(Vector3.Zero, Vector3.Right, TownView.Below),
            Decal = ci => WallArt(ci, "yard.wall.north", c => YardElevation.North(c)),
        });
        _faces.Add(new Face
        {
            Points = [new(w, 0, 0), new(w, d, 0), new(w, d, e), new(w, 0, e)], Normal = new Vector3(-1, 0, 0), Color = Cel.Plaster,
            Local = TownView.Local(new Vector3(w, 0, 0), new Vector3(0, 1, 0), TownView.Below),
            Decal = ci => WallArt(ci, "yard.wall.east", c => YardElevation.East(c)),
        });

        // 东院墙的墙头：瓦顶朝院内的一坡与南端断面。
        _faces.Add(new Face
        {
            Points = [new(w, 0, e), new(w + t, 0, e + 14), new(w + t, d + t, e + 14), new(w, d + t, e)], Normal = TownView.Above, Color = Cel.Tile, Outline = 1.6f,
        });
        _faces.Add(new Face
        {
            Points = [new(w, d + t, 0), new(w + t, d + t, 0), new(w + t, d + t, e + 14), new(w, d + t, e)], Normal = new Vector3(0, 1, 0), Color = Cel.PlasterShade, Outline = 1.6f,
        });
    }

    public override void _Draw()
    {
        foreach (var face in _faces)
        {
            face.Draw(this);
        }
    }

    /// <summary>立面：AI 整面墙已入库时贴图（origin 以墙面局部坐标计），否则画程序化占位。</summary>
    private static void WallArt(CanvasItem ci, string id, Action<CanvasItem> fallback)
    {
        if (PieceArt.Find(id) is { } art)
        {
            ci.DrawTextureRect(art.Texture, art.Frame, false);
            return;
        }

        fallback(ci);
    }
}

/// <summary>后院两面立面的程序化画法（墙面局部坐标，y 自顶 −高 到地面 0）：游戏里的占位，也是 AI 出件的引导图。</summary>
internal static class YardElevation
{
    public static Rect2 NorthBox => new(-YardSamples.WingOverhang, -YardSamples.WingHeight, YardSamples.Yard.Size.X + 2 * YardSamples.WingOverhang, YardSamples.WingHeight);

    public static Rect2 EastBox => new(0, -YardSamples.EastWallHeight, YardSamples.Yard.Size.Y, YardSamples.EastWallHeight);

    /// <summary>客房楼：一层粉壁、木门两扇与槅窗，檐枋上是二层木栏廊与槅窗，顶上一道瓦檐。</summary>
    public static void North(CanvasItem ci)
    {
        var box = NorthBox;
        var (x0, x1) = (box.Position.X, box.End.X);
        ci.DrawRect(box, Cel.Plaster);

        // 二层：木板墙。
        ci.DrawRect(new Rect2(x0, -490, x1 - x0, 190), Cel.WoodLight.Darkened(0.1f));
        for (var x = x0 + 30; x < x1; x += 46)
        {
            ci.DrawLine(new Vector2(x, -488), new Vector2(x, -302), Cel.Wood, 2f);
        }

        // 一层：粉壁、条石墙脚。
        Cel.PlasterDecal(ci, x1 - x0, 280, 3);
        ci.DrawRect(new Rect2(x0, -30, x1 - x0, 30), Cel.Stone);
        ci.DrawLine(new Vector2(x0, -30), new Vector2(x1, -30), Cel.Ink with { A = 0.5f }, 2f);

        foreach (var (x, _) in YardSamples.GroundWindows)
        {
            Cel.LatticeWindow(ci, YardSamples.GroundWindow(x));
        }

        foreach (var x in YardSamples.DoorCenters)
        {
            var r = YardSamples.Door(x);
            Cel.Box(ci, r.Grow(8), InnTone.Lacquer, 1.8f);
            Cel.Box(ci, new Rect2(r.Position, new Vector2(r.Size.X / 2, r.Size.Y)), Cel.Wood, 1.6f);
            Cel.Box(ci, new Rect2(r.Position + new Vector2(r.Size.X / 2, 0), new Vector2(r.Size.X / 2, r.Size.Y)), Cel.Wood, 1.6f);
            for (var y = r.Position.Y + 20; y < r.End.Y - 60; y += 22)
            {
                ci.DrawLine(new Vector2(r.Position.X + 10, y), new Vector2(r.End.X - 10, y), Cel.WoodDark, 1.6f);
            }

            ci.DrawRect(new Rect2(r.Position.X - 12, -12, r.Size.X + 24, 12), Cel.StoneLight);
        }

        // 立柱、一层檐枋。
        foreach (var x in YardSamples.WingPosts)
        {
            Cel.Box(ci, new Rect2(x - 11, -300, 22, 300), InnTone.Lacquer, 1.6f);
        }

        Cel.Box(ci, new Rect2(x0, -306, x1 - x0, 26), InnTone.Lacquer, 1.6f);

        // 二层槅窗与廊下木栏。
        foreach (var (x, _) in YardSamples.UpperWindows)
        {
            Cel.LatticeWindow(ci, YardSamples.UpperWindow(x));
        }

        foreach (var x in YardSamples.WingPosts)
        {
            Cel.Box(ci, new Rect2(x - 9, -490, 18, 184), Cel.WoodDark, 1.4f);
        }

        Cel.Box(ci, new Rect2(x0, -392, x1 - x0, 12), Cel.WoodDark, 1.4f);
        for (var x = x0 + 14; x < x1; x += 28)
        {
            ci.DrawRect(new Rect2(x, -380, 6, 72), Cel.Wood);
        }

        Cel.Box(ci, new Rect2(x0, -312, x1 - x0, 10), Cel.WoodDark, 1.4f);

        // 瓦檐：一道黛瓦，檐口暗边与瓦当。
        ci.DrawRect(new Rect2(x0, -540, x1 - x0, 50), Cel.Tile);
        for (var x = x0 + 8; x < x1; x += 22)
        {
            ci.DrawRect(new Rect2(x, -540, 9, 40), Cel.TileRow with { A = 0.6f });
            ci.DrawCircle(new Vector2(x + 4, -496), 6.5f, Cel.TileDark);
        }

        ci.DrawRect(new Rect2(x0, -492, x1 - x0, 6), Cel.TileDark);
    }

    /// <summary>东院墙：粉壁、条石墙脚、墙头盖瓦，南段开一扇带小瓦檐的木后门。</summary>
    public static void East(CanvasItem ci)
    {
        var box = EastBox;
        var (w, h) = (box.Size.X, box.Size.Y);
        ci.DrawRect(box, Cel.Plaster);
        Cel.PlasterDecal(ci, w, h - 40, 7);

        ci.DrawRect(new Rect2(0, -h, w, 40), Cel.Tile);
        for (var x = 6f; x < w; x += 22)
        {
            ci.DrawRect(new Rect2(x, -h, 9, 32), Cel.TileRow with { A = 0.6f });
            ci.DrawCircle(new Vector2(x + 4, -h + 36), 6, Cel.TileDark);
        }

        ci.DrawRect(new Rect2(0, -30, w, 30), Cel.Stone);
        ci.DrawLine(new Vector2(0, -30), new Vector2(w, -30), Cel.Ink with { A = 0.5f }, 2f);

        var gate = new Rect2(YardSamples.GateNorth, -215, YardSamples.GateSouth - YardSamples.GateNorth, 215);
        Cel.Box(ci, gate.Grow(10), Cel.WoodDark, 1.8f);
        Cel.Box(ci, new Rect2(gate.Position, new Vector2(gate.Size.X / 2, gate.Size.Y)), Cel.Wood, 1.6f);
        Cel.Box(ci, new Rect2(gate.Position + new Vector2(gate.Size.X / 2, 0), new Vector2(gate.Size.X / 2, gate.Size.Y)), Cel.Wood, 1.6f);
        foreach (var y in new[] { -170f, -60f })
        {
            ci.DrawLine(new Vector2(gate.Position.X + 6, y), new Vector2(gate.End.X - 6, y), Cel.WoodDark, 4f);
        }

        ci.DrawRect(new Rect2(gate.Position.X - 30, -250, gate.Size.X + 60, 26), Cel.Tile);
        ci.DrawRect(new Rect2(gate.Position.X - 30, -228, gate.Size.X + 60, 5), Cel.TileDark);
    }
}

/// <summary>立面引导图（<see cref="PieceGuideExport"/> 用）：墙面局部坐标的正立面。</summary>
public partial class YardWallElevation : Node2D
{
    private readonly bool _north;

    public YardWallElevation(bool north)
    {
        _north = north;
        Box = north ? YardElevation.NorthBox : YardElevation.EastBox;
    }

    public Rect2 Box { get; }

    public override void _Draw()
    {
        if (_north)
        {
            YardElevation.North(this);
        }
        else
        {
            YardElevation.East(this);
        }
    }
}

/// <summary>
/// 镜头一侧的矮院墙（西）：粉壁、条石墙脚，墙头盖一道黛瓦。南面大堂后墙沿用大堂的剖切墙（<see cref="InnCutWall"/>）。
/// </summary>
public partial class YardLowWall : TownPiece
{
    public YardLowWall(Vector3 min, Vector3 max)
    {
        Occluder = false;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        var cap = max.Z - 16;
        var faces = Solid.Box(min, new Vector3(max.X, max.Y, cap), Cel.Plaster, 1.8f);
        var size = max - min;
        FaceKit.Decorate(faces, new Vector3(-1, 0, 0), TownView.Local(new Vector3(min.X, max.Y, 0), new Vector3(0, -1, 0), TownView.Below),
            ci => InnCutWall.Base(ci, size.Y, cap));
        Faces.AddRange(faces);
        var mid = (min.X + max.X) / 2;
        Faces.AddRange(Solid.ExtrudeY([new(min.X - 10, cap), new(max.X + 10, cap), new(mid, max.Z)], min.Y - 10, max.Y, Cel.Tile, 1.6f));
        Seal(new Rect2(min.X, min.Y, size.X, size.Y));
    }
}

/// <summary>门槛：后厨门洞里一道低木槛，可跨过（不挡路）。</summary>
public partial class YardSill : TownPiece
{
    public YardSill()
    {
        Occluder = false;
        var d = YardSamples.Yard.End.Y;
        Faces.AddRange(Solid.Box(new Vector3(YardSamples.DoorWest + 15, d + 4, 0), new Vector3(YardSamples.DoorEast - 15, d + 18, 12), Cel.WoodDark, 1.4f));
        Seal(new Rect2(YardSamples.DoorWest + 15, d + 4, YardSamples.DoorEast - YardSamples.DoorWest - 30, 14));
    }
}

/// <summary>柴堆：贴北墙码放的劈柴，断面朝院里；顶上搭一块斜木板挡雨。</summary>
public partial class YardWoodpile : TownPiece
{
    public YardWoodpile()
    {
        var r = YardSamples.Woodpile;
        var h = YardSamples.WoodpileHeight;
        var faces = Solid.Box(new Vector3(r.Position.X, r.Position.Y, 0), new Vector3(r.End.X, r.End.Y, h), Cel.WoodLight, 1.6f);
        FaceKit.Decorate(faces, new Vector3(0, 1, 0), TownView.Local(new Vector3(r.Position.X, r.End.Y, h), Vector3.Right, TownView.Below),
            ci => LogEnds(ci, r.Size.X, h, 1));
        FaceKit.Decorate(faces, new Vector3(-1, 0, 0), TownView.Local(new Vector3(r.Position.X, r.End.Y, h), new Vector3(0, -1, 0), TownView.Below),
            ci => LogSides(ci, r.Size.Y, h));
        Faces.AddRange(faces);
        Faces.AddRange(Solid.ExtrudeX([new(r.Position.Y - 6, h + 40), new(r.End.Y + 16, h + 6), new(r.End.Y + 16, h + 14), new(r.Position.Y - 6, h + 48)],
            r.Position.X - 14, r.End.X + 14, Cel.WoodDark, 1.6f));
        Seal(r);
        UseArt("yard.woodpile");
    }

    /// <summary>断面：一排排圆木头，年轮浅色、树皮深色。</summary>
    private static void LogEnds(CanvasItem ci, float w, float h, int seed)
    {
        if (Face.StructureOnly)
        {
            return;
        }

        ci.DrawRect(new Rect2(0, 0, w, h), Cel.WoodDark);
        var row = 0;
        for (var y = 14f; y < h; y += 24, row++)
        {
            for (var x = 14f + (row % 2) * 12; x < w - 8; x += 25)
            {
                var r = 10 + Cel.Rand(seed, row * 31 + (int)x) * 3;
                ci.DrawCircle(new Vector2(x, y), r + 2, Cel.Bark);
                ci.DrawCircle(new Vector2(x, y), r, Cel.Paper.Darkened(0.12f));
                ci.DrawArc(new Vector2(x, y), r * 0.5f, 0, Mathf.Tau, 10, Cel.WoodLight, 1.2f, true);
            }
        }
    }

    /// <summary>西端侧面：顺着木纹的劈柴侧面。</summary>
    private static void LogSides(CanvasItem ci, float w, float h)
    {
        if (Face.StructureOnly)
        {
            return;
        }

        for (var y = 0f; y < h; y += 24)
        {
            ci.DrawRect(new Rect2(0, y, w, 20), Cel.Bark);
            ci.DrawLine(new Vector2(0, y + 21), new Vector2(w, y + 21), Cel.WoodDark, 3f);
        }
    }
}

/// <summary>劈柴墩：一截粗木墩，上面斜插一把斧头，脚边散几块柴。</summary>
public partial class YardStump : TownPiece
{
    public YardStump()
    {
        var c = YardSamples.Stump;
        Faces.AddRange(Solid.Prism(new Vector3(c.X, c.Y, 0), new Vector3(c.X, c.Y, 48), 30, 10, Cel.Bark, 1.6f));
        foreach (var (dx, dy, len) in new[] { (-48f, 30f, 60f), (38f, 40f, 54f) })
        {
            Faces.AddRange(Solid.Prism(new Vector3(c.X + dx, c.Y + dy, 7), new Vector3(c.X + dx + len, c.Y + dy - 8, 7), 7, 6, Cel.WoodLight, 1.2f, alongU: true));
        }

        // 斧柄斜插在墩面上，斧刃朝东。
        Faces.AddRange(Solid.Prism(new Vector3(c.X - 4, c.Y, 48), new Vector3(c.X - 44, c.Y + 6, 108), 3.5f, 6, Cel.WoodLight, 1.2f, alongU: true));
        Faces.AddRange(Solid.Box(new Vector3(c.X - 8, c.Y - 3, 44), new Vector3(c.X + 22, c.Y + 3, 62), Cel.StoneLight, 1.2f));
        Seal(new Rect2(c - new Vector2(40, 34), new Vector2(80, 68)));
        UseArt("yard.stump");
    }
}

/// <summary>石桌与两只鼓凳：圆石桌面立在石墩上。</summary>
public partial class YardStoneTable : TownPiece
{
    public YardStoneTable()
    {
        var c = YardSamples.StoneTable;
        Faces.AddRange(Solid.Prism(new Vector3(c.X, c.Y, 0), new Vector3(c.X, c.Y, 62), 22, 12, Cel.Stone, 1.6f));
        Faces.AddRange(Solid.Prism(new Vector3(c.X, c.Y, 62), new Vector3(c.X, c.Y, 74), 60, 20, Cel.StoneLight, 1.8f));
        foreach (var s in YardSamples.Stools)
        {
            Faces.AddRange(Solid.Prism(new Vector3(s.X, s.Y, 0), new Vector3(s.X, s.Y, 42), 20, 12, Cel.Stone, 1.6f));
        }

        Solid.Sort(Faces);
        var points = YardSamples.Stools.Append(c).ToArray();
        var box = new Rect2(c, Vector2.Zero);
        foreach (var p in points) box = box.Expand(p);
        Seal(box.Grow(34));
        UseArt("yard.stone_table");
    }
}

/// <summary>客房楼檐下的灯笼：叠加暖光晕，不随夜色变暗。</summary>
public partial class YardLanterns : Node2D
{
    private readonly Node2D _halo = new() { Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };

    public YardLanterns(Color tint) => Modulate = SceneTimes.Unlit(tint);

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        _halo.Draw += () =>
        {
            foreach (var l in YardSamples.Lanterns)
            {
                var c = TownView.P(l) + new Vector2(0, 38 * TownView.Upright);
                const int n = 24;
                var inner = Cel.Glow with { A = 0.5f };
                var edge = Cel.Glow with { A = 0 };
                for (var i = 0; i < n; i++)
                {
                    var a0 = Mathf.Tau * i / n;
                    var a1 = Mathf.Tau * (i + 1) / n;
                    _halo.DrawPrimitive([c, c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 100, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 100], [inner, edge, edge], []);
                }
            }
        };
        AddChild(_halo);
    }

    public override void _Draw()
    {
        var art = PieceArt.Find("inn.lantern");
        foreach (var l in YardSamples.Lanterns)
        {
            DrawLine(TownView.P(l.X, l.Y - 30, 300), TownView.P(l), Cel.Ink, 2f);
            if (art is not null)
            {
                DrawTextureRect(art.Texture, new Rect2(art.Frame.Position + TownView.P(l), art.Frame.Size), false);
                continue;
            }

            DrawSetTransform(TownView.P(l), 0, Vector2.One * TownView.Upright);
            Cel.HangingLantern(this, Vector2.Zero, InnLanternGuide.Size);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }
}

/// <summary>
/// 夜里的光：客房楼亮窗透出的暖光（贴在立面上）与窗前、灯笼下、后厨门洞里铺到地上的暖光，叠加混合，
/// 画在壳之上、排序件之下；用 <see cref="SceneTimes.Unlit"/> 抵消夜色调色。
/// </summary>
public partial class YardLight : Node2D
{
    public YardLight(Color tint)
    {
        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        Modulate = SceneTimes.Unlit(tint);
    }

    public override void _Draw()
    {
        // 窗纸透光：立面局部坐标经北墙面的变换落到屏幕。
        DrawSetTransformMatrix(TownView.Local(Vector3.Zero, Vector3.Right, TownView.Below));
        var warm = Cel.Glow;
        foreach (var r in YardSamples.LitWindows)
        {
            var paper = r.Grow(-8);
            DrawRect(paper, warm with { A = 0.42f });
            DrawRect(paper.Grow(10), warm with { A = 0.08f });
        }

        foreach (var x in YardSamples.DoorCenters)
        {
            var door = YardSamples.Door(x);
            DrawRect(new Rect2(door.GetCenter().X - 3, door.Position.Y + 6, 6, door.Size.Y - 12), warm with { A = 0.35f });
        }

        DrawSetTransform(Vector2.Zero);
        using var batch = new PolyBatch(this);

        // 亮窗下、门缝前的地面暖光。
        foreach (var r in YardSamples.LitWindows.Where(r => r.End.Y > -300))
        {
            InnFloorLight.Pool(batch, new Vector2(r.GetCenter().X, 60), 150, warm with { A = 0.13f });
        }

        foreach (var l in YardSamples.Lanterns)
        {
            InnFloorLight.Pool(batch, new Vector2(l.X, l.Y + 60), 230, warm with { A = 0.2f });
            InnFloorLight.Pool(batch, new Vector2(l.X, l.Y + 50), 90, warm with { A = 0.12f });
        }

        // 后厨门洞：大堂的灯光从门里斜铺进院子。
        var d = YardSamples.Yard.End.Y;
        var reach = new Vector2(-0.2f, -1) * 260;
        var color = warm with { A = 0.26f };
        Vector2[] spill = [new(YardSamples.DoorWest, d), new(YardSamples.DoorEast, d), new Vector2(YardSamples.DoorEast + 40, d) + reach, new Vector2(YardSamples.DoorWest - 40, d) + reach];
        DrawPolygon(spill.Select(p => TownView.P(p)).ToArray(), [color, color, color with { A = 0 }, color with { A = 0 }]);
    }
}

/// <summary>
/// 墙外与地面：四周暗场；南墙外露一段大堂方砖地（门洞后透着灯光），西墙外一条暗巷；院里铺石板（城镇石板街的 AI 纹理），
/// 井台四周一圈湿痕，墙根一圈阴影。
/// </summary>
public partial class YardGround : Node2D
{
    public override void _Ready()
    {
        var shader = GD.Load<Shader>("res://assets/shaders/town_ground.gdshader");
        var yard = YardSamples.Yard;
        var t = YardSamples.WallThickness;
        var hall = new Rect2(yard.Position.X - 200, yard.End.Y + t, yard.Size.X + 400, 320);
        var alley = new Rect2(yard.Position.X - 300, yard.Position.Y - 60, 300 - t, yard.Size.Y + 60 + t);

        ShaderMaterial Material(bool texture)
        {
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("kind", 1);
            material.SetShaderParameter("yaw", Mathf.DegToRad(TownView.Yaw));
            material.SetShaderParameter("pitch", Mathf.DegToRad(TownView.Pitch));
            material.SetShaderParameter("scale", TownView.Scale);
            material.SetShaderParameter("plane_z", 0f);
            if (texture)
            {
                PieceArt.ApplyGround(material, "town.ground.flagstone", new Vector3(1.08f, 1.14f, 1.12f), macro: 0.08f);
                material.SetShaderParameter("puddles", 0f);
            }

            return material;
        }

        AddChild(new Polygon2D { Polygon = Corners(alley).Select(p => TownView.P(p)).ToArray(), Material = Material(false), Modulate = new Color(0.7f, 0.7f, 0.72f) });
        AddChild(new Polygon2D { Polygon = Corners(yard).Select(p => TownView.P(p)).ToArray(), Material = Material(true) });

        var detail = new Node2D();
        detail.Draw += () =>
        {
            var dark = InnTone.Background;
            var clear = dark with { A = 0 };

            // 南墙外的大堂地面：方砖色，自墙往外没入暗处。
            Vector2[] floor = Corners(hall).Select(p => TownView.P(p)).ToArray();
            detail.DrawColoredPolygon(floor, InnTone.Brick.Darkened(0.35f));
            detail.DrawPolygon(floor, [clear, clear, dark, dark]);

            // 西墙外暗巷：远离墙一侧压暗。
            var (ax0, ax1, ay0, ay1) = (alley.Position.X, alley.End.X, alley.Position.Y, alley.End.Y);
            detail.DrawPolygon([TownView.P(ax0, ay0), TownView.P(ax1, ay0), TownView.P(ax1, ay1), TownView.P(ax0, ay1)], [dark, clear, clear, dark]);

            // 井台四周的湿痕与墙根阴影。
            Cel.GroundShadow(detail, YardSamples.Well, 120, 105, 0.12f);
            var (w, d) = (yard.Size.X, yard.Size.Y);
            detail.DrawPolygon([TownView.P(0, 0), TownView.P(w, 0), TownView.P(w, 40), TownView.P(0, 40)],
                [Cel.Ink with { A = 0.32f }, Cel.Ink with { A = 0.32f }, Cel.Ink with { A = 0 }, Cel.Ink with { A = 0 }]);
            detail.DrawPolygon([TownView.P(w, 0), TownView.P(w, d), TownView.P(w - 40, d), TownView.P(w - 40, 0)],
                [Cel.Ink with { A = 0.28f }, Cel.Ink with { A = 0.28f }, Cel.Ink with { A = 0 }, Cel.Ink with { A = 0 }]);
        };
        AddChild(detail);
    }

    public override void _Draw() => DrawColoredPolygon(Corners(YardSamples.Bounds.Grow(1600)).Select(p => TownView.P(p)).ToArray(), InnTone.Background);

    private static Vector2[] Corners(Rect2 r) => [r.Position, new(r.End.X, r.Position.Y), r.End, new(r.Position.X, r.End.Y)];
}

/// <summary>小地图：后院平面（上北），一屏全院；北面客房楼、东墙后门、南面大堂门洞，物件为暗块，石青箭头为主角。</summary>
public partial class YardMiniMap : Control
{
    private const float Window = 1640;

    public Func<(Vector2 Position, Vector2 Heading)> Hero { get; init; } = () => (Vector2.Zero, Vector2.Right);

    public Func<Vector2?> Goal { get; init; } = () => null;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
    }

    public override void _Draw()
    {
        var (hero, heading) = Hero();
        var k = Size.X / Window;
        var yard = YardSamples.Yard;
        var origin = yard.GetCenter() - new Vector2(Window, Window) / 2;
        DrawRect(new Rect2(Vector2.Zero, Size), UiPalette.Abyss);
        DrawSetTransform(-origin * k, 0, new Vector2(k, k));

        var t = YardSamples.WallThickness;
        DrawRect(new Rect2(-YardSamples.WingOverhang, -180, yard.Size.X + 2 * YardSamples.WingOverhang, 180), UiPalette.TextMuted with { A = 0.55f });
        DrawRect(new Rect2(-200, yard.End.Y + t, yard.Size.X + 400, 260), UiPalette.TextMuted with { A = 0.35f });
        DrawRect(yard.Grow(t), UiPalette.TextMuted with { A = 0.8f });
        DrawRect(yard, UiPalette.SurfaceShade);
        DrawRect(new Rect2(YardSamples.DoorWest, yard.End.Y - 2, YardSamples.DoorEast - YardSamples.DoorWest, t + 4), UiPalette.SurfaceShade);
        DrawRect(new Rect2(yard.End.X - 2, YardSamples.GateNorth, t + 4, YardSamples.GateSouth - YardSamples.GateNorth), UiPalette.Trim);
        var furniture = UiPalette.TextMuted with { A = 0.7f };
        DrawRect(YardSamples.Woodpile, furniture);
        DrawCircle(YardSamples.Stump, 30, furniture);
        DrawCircle(YardSamples.Well, 64, furniture);
        DrawCircle(YardSamples.StoneTable, 60, furniture);
        foreach (var s in YardSamples.Stools) DrawCircle(s, 20, furniture);
        foreach (var j in YardSamples.Jars) DrawCircle(j, 46, furniture);

        var r = 9 / k;
        if (Goal() is { } g)
        {
            DrawColoredPolygon([g + new Vector2(0, -r), g + new Vector2(r, 0), g + new Vector2(0, r), g + new Vector2(-r, 0)], UiPalette.Gilt.Darkened(0.1f));
        }

        var a = 11 / k;
        var f = heading.Normalized();
        var side = new Vector2(-f.Y, f.X);
        DrawColoredPolygon([hero + f * a * 1.2f, hero - f * a * 0.8f + side * a * 0.8f, hero - f * a * 0.3f, hero - f * a * 0.8f - side * a * 0.8f], UiPalette.Accent);
        DrawArc(hero, 18 / k, 0, Mathf.Tau, 32, UiPalette.Accent with { A = 0.5f }, 1.5f / k, true);

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        DrawString(UiFonts.Title, new Vector2(Size.X - 30, 28), "北", HorizontalAlignment.Left, -1, 20, UiPalette.Text);
    }
}
