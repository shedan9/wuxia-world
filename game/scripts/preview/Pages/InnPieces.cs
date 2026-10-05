using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;
using WuxiaWorld.Game.Presentation.Art;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>室内布景的用色：木作偏暖、方砖偏青灰，剖切面用深赭，灯光为暖黄、门口天光为暖白。</summary>
internal static class InnTone
{
    public static readonly Color Brick = Color.FromHtml("#949D95");
    public static readonly Color BrickSeam = Color.FromHtml("#626C66");

    /// <summary>AI 方砖纹理（平均色约 #6F7680）调到 <see cref="Brick"/> 的青灰。</summary>
    public static readonly Color BrickTint = new(1.34f, 1.33f, 1.18f);
    public static readonly Color Section = Color.FromHtml("#3B3129");
    public static readonly Color Plaster = Color.FromHtml("#ECE6D6");
    public static readonly Color Lacquer = Color.FromHtml("#6B3A2A");
    public static readonly Color LacquerLight = Color.FromHtml("#8C5238");
    public static readonly Color Silk = Color.FromHtml("#E8DFC6");
    public static readonly Color Azurite = Color.FromHtml("#3E7F9A");
    public static readonly Color AzuriteLight = Color.FromHtml("#7FB3C4");
    public static readonly Color Malachite = Color.FromHtml("#4E9A6E");
    public static readonly Color Porcelain = Color.FromHtml("#DCE6E8");
    public static readonly Color Daylight = Color.FromHtml("#FFF3D6");
    public static readonly Color Background = Color.FromHtml("#161D22");
}

/// <summary>面的替换工具：在 <see cref="Solid"/> 搭出的面里，把朝某方向的一面换成带贴花或换色的一面。</summary>
internal static class FaceKit
{
    public static void Decorate(List<Face> faces, Vector3 normal, Transform2D local, Action<CanvasItem> decal)
    {
        var i = faces.FindIndex(f => f.Normal.IsEqualApprox(normal));
        var old = faces[i];
        faces[i] = new Face { Points = old.Points, Normal = old.Normal, Color = old.Color, Outline = old.Outline, Local = local, Decal = decal };
    }

    public static void Recolor(List<Face> faces, Vector3 normal, Color color)
    {
        var i = faces.FindIndex(f => f.Normal.IsEqualApprox(normal));
        var old = faces[i];
        faces[i] = new Face { Points = old.Points, Normal = old.Normal, Color = color, Outline = old.Outline, Local = old.Local, Decal = old.Decal };
    }

    /// <summary>贴在水平面上的一片（桌上的算盘、账本、信），局部 x 向东、y 向南。</summary>
    public static Face Flat(Rect2 r, float z, Color color, Action<CanvasItem>? decal = null, float outline = 1.4f) => new()
    {
        Points = [new(r.Position.X, r.Position.Y, z), new(r.End.X, r.Position.Y, z), new(r.End.X, r.End.Y, z), new(r.Position.X, r.End.Y, z)],
        Normal = TownView.Above, Color = color, Outline = outline,
        Local = TownView.Local(new Vector3(r.Position.X, r.Position.Y, z), Vector3.Right, new Vector3(0, 1, 0)),
        Decal = decal,
    };
}

/// <summary>
/// 大堂的壳：暗场、门外一段街面、方砖地、北墙与东墙内墙面（木构架、粉壁、裙板、货架、水牌、后厨门帘、槅窗、字轴）
/// 与墙顶剖切面。全部贴地或靠后，画在排序层之下；南墙、西墙剖切段是排序件（<see cref="InnCutWall"/>）。
/// </summary>
public partial class InnShell : Node2D
{
    private readonly List<Face> _faces = [];

    public InnShell()
    {
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        var room = InnSamples.Room;
        var (w, d, h, t) = (room.Size.X, room.Size.Y, InnSamples.WallHeight, InnSamples.WallThickness);

        _faces.Add(FaceKit.Flat(room, 0, InnTone.Brick, ci => Floor(ci, w, d), 0));
        _faces.Add(new Face
        {
            Points = [new(0, 0, 0), new(w, 0, 0), new(w, 0, h), new(0, 0, h)], Normal = new Vector3(0, 1, 0), Color = InnTone.Plaster,
            Local = TownView.Local(Vector3.Zero, Vector3.Right, TownView.Below),
            Decal = ci => WallArt(ci, "inn.wall.north", c => NorthWall(c, w, h)), Lettering = NorthLettering,
        });
        _faces.Add(new Face
        {
            Points = [new(w, 0, 0), new(w, d, 0), new(w, d, h), new(w, 0, h)], Normal = new Vector3(-1, 0, 0), Color = InnTone.Plaster,
            Local = TownView.Local(new Vector3(w, 0, 0), new Vector3(0, 1, 0), TownView.Below),
            Decal = ci => WallArt(ci, "inn.wall.east", c => EastWall(c, d, h)), Lettering = ci => EastLettering(ci, h),
        });

        // 墙顶剖切面与北墙西端、东墙南端的断面。
        _faces.Add(new Face { Points = [new(-t, -t, h), new(w + t, -t, h), new(w, 0, h), new(-t, 0, h)], Normal = TownView.Above, Color = InnTone.Section, Outline = 1.6f });
        _faces.Add(new Face { Points = [new(w, 0, h), new(w + t, -t, h), new(w + t, d + t, h), new(w, d + t, h)], Normal = TownView.Above, Color = InnTone.Section, Outline = 1.6f });
        _faces.Add(new Face { Points = [new(-t, -t, 0), new(-t, 0, 0), new(-t, 0, h), new(-t, -t, h)], Normal = new Vector3(-1, 0, 0), Color = InnTone.Section, Outline = 1.6f });
        _faces.Add(new Face { Points = [new(w, d + t, 0), new(w + t, d + t, 0), new(w + t, d + t, h), new(w, d + t, h)], Normal = new Vector3(0, 1, 0), Color = InnTone.Section, Outline = 1.6f });
    }

    public override void _Draw()
    {
        foreach (var face in _faces)
        {
            face.Draw(this);
        }
    }

    /// <summary>
    /// 方砖地：60 见方、横平竖直铺，砖色略有深浅，自门口到柜台一带踩得发亮。AI 方砖纹理（inn.ground.brick，格线与 60 见方对齐）
    /// 入库时平铺作底，逐砖深浅改为半透明叠加；缝线仍由引擎勾深。
    /// </summary>
    private static void Floor(CanvasItem ci, float w, float d)
    {
        const float size = 60;
        var tex = PieceArt.FindTexture("inn.ground.brick");
        if (tex is { } t)
        {
            var px = t.Texture.GetSize() / t.WorldSize;
            for (var y = 0f; y < d; y += t.WorldSize)
            {
                for (var x = 0f; x < w; x += t.WorldSize)
                {
                    var cell = new Vector2(Mathf.Min(t.WorldSize, w - x), Mathf.Min(t.WorldSize, d - y));
                    ci.DrawTextureRectRegion(t.Texture, new Rect2(new Vector2(x, y), cell), new Rect2(Vector2.Zero, cell * px), InnTone.BrickTint);
                }
            }
        }

        for (var y = 0f; y < d; y += size)
        {
            for (var x = 0f; x < w; x += size)
            {
                var i = (int)(x / size) * 31 + (int)(y / size);
                var k = Cel.Rand(7, i);
                var tone = k > 0.8f ? InnTone.Brick.Darkened(0.07f) : k < 0.2f ? InnTone.Brick.Lightened(0.06f) : InnTone.Brick;
                if (tex is not null)
                {
                    tone = k > 0.8f ? Cel.Ink with { A = 0.08f } : k < 0.2f ? Colors.White with { A = 0.06f } : Colors.Transparent;
                }

                ci.DrawRect(new Rect2(x, y, Mathf.Min(size, w - x), Mathf.Min(size, d - y)), tone);
                if (Cel.Rand(9, i) > 0.93f)
                {
                    ci.DrawPolyline([new(x + 10, y + 14), new(x + 26, y + 30), new(x + 24, y + 46)], InnTone.BrickSeam with { A = 0.6f }, 1.4f, true);
                }
            }
        }

        ci.DrawPolygon([new(InnSamples.DoorWest - 20, d), new(InnSamples.DoorEast + 20, d), new(560, 240), new(220, 240)],
            [Colors.White with { A = 0.1f }, Colors.White with { A = 0.1f }, Colors.White with { A = 0.02f }, Colors.White with { A = 0.02f }]);
        for (var x = size; x < w; x += size) ci.DrawLine(new Vector2(x, 0), new Vector2(x, d), InnTone.BrickSeam, 2f);
        for (var y = size; y < d; y += size) ci.DrawLine(new Vector2(0, y), new Vector2(w, y), InnTone.BrickSeam, 2f);

        // 墙根一圈阴影。
        ci.DrawPolygon([new(0, 0), new(w, 0), new(w, 36), new(0, 36)],
            [Cel.Ink with { A = 0.3f }, Cel.Ink with { A = 0.3f }, Cel.Ink with { A = 0 }, Cel.Ink with { A = 0 }]);
        ci.DrawPolygon([new(w, 0), new(w, d), new(w - 36, d), new(w - 36, 0)],
            [Cel.Ink with { A = 0.25f }, Cel.Ink with { A = 0.25f }, Cel.Ink with { A = 0 }, Cel.Ink with { A = 0 }]);
    }

    /// <summary>
    /// 内墙面：AI 按正立面引导图（<see cref="InnWallElevation"/>）出的整面墙已入库时贴图，否则画程序化占位。
    /// 立面图的 origin / px 以墙面局部坐标计（x 沿墙、y 自墙顶 -高 到地面 0），与面的 Local 变换直接相乘即到屏幕。
    /// </summary>
    private static void WallArt(CanvasItem ci, string id, Action<CanvasItem> fallback)
    {
        if (PieceArt.Find(id) is { } art)
        {
            ci.DrawTextureRect(art.Texture, art.Frame, false);
            return;
        }

        fallback(ci);
    }

    /// <summary>内墙通用：檐下暗带、木裙板、立柱与顶梁。posts 为立柱的局部 x。</summary>
    private static void Frame(CanvasItem ci, float w, float h, float[] posts)
    {
        ci.DrawPolygon([new(0, -h), new(w, -h), new(w, -h + 70), new(0, -h + 70)],
            [Cel.Ink with { A = 0.22f }, Cel.Ink with { A = 0.22f }, Cel.Ink with { A = 0 }, Cel.Ink with { A = 0 }]);
        ci.DrawRect(new Rect2(0, -95, w, 95), Cel.Wood);
        for (var x = 20f; x < w - 60; x += 110)
        {
            ci.DrawRect(new Rect2(x, -80, 90, 64), Cel.WoodLight, false, 2.5f);
        }

        ci.DrawRect(new Rect2(0, -102, w, 12), Cel.WoodDark);
        foreach (var x in posts)
        {
            Cel.Box(ci, new Rect2(x - 10, -h, 20, h), InnTone.Lacquer, 1.6f);
            ci.DrawLine(new Vector2(x - 5, -h + 30), new Vector2(x - 5, -6), InnTone.LacquerLight, 3f);
        }

        Cel.Box(ci, new Rect2(0, -h, w, 26), InnTone.Lacquer, 1.6f);
    }

    internal static void NorthWall(CanvasItem ci, float w, float h)
    {
        Frame(ci, w, h, [10, 120, 620, 692, 848, 1130, w - 10]);
        Shelves(ci, new Rect2(132, -300, 478, 300));
        MenuBoard(ci, Menu);
        KitchenDoor(ci, new Rect2(704, -228, 136, 228));
        foreach (var x in new[] { 890f, 1010f })
        {
            Cel.LatticeWindow(ci, new Rect2(x, -262, 104, 120));
            ci.DrawRect(new Rect2(x + 7, -255, 90, 106), InnTone.Daylight with { A = 0.45f });
        }
    }

    /// <summary>字轴的位置（东墙局部坐标）。</summary>
    private static readonly Rect2 Scroll = new(790, -284, 86, 196);

    /// <summary>水牌的位置（北墙局部坐标）。</summary>
    private static readonly Rect2 Menu = new(626, -278, 58, 168);

    internal static void EastWall(CanvasItem ci, float d, float h)
    {
        Frame(ci, d, h, [10, 240, 480, 730, d - 10]);

        // 字轴：绢心留白（字由字层补写），上下木轴。
        var scroll = Scroll;
        Cel.Box(ci, scroll, InnTone.Silk, 1.6f);
        ci.DrawRect(new Rect2(scroll.Position.X + 8, scroll.Position.Y + 18, scroll.Size.X - 16, scroll.Size.Y - 36), InnTone.Silk.Lightened(0.3f));
        Cel.Box(ci, new Rect2(scroll.Position.X - 8, scroll.Position.Y - 8, scroll.Size.X + 16, 10), Cel.WoodDark, 1.4f);
        Cel.Box(ci, new Rect2(scroll.Position.X - 8, scroll.End.Y - 2, scroll.Size.X + 16, 10), Cel.WoodDark, 1.4f);
        ci.DrawLine(new Vector2(scroll.GetCenter().X, -h + 26), scroll.Position + new Vector2(scroll.Size.X / 2, -8), Cel.Ink, 1.6f);
    }

    /// <summary>字层：字轴竖写“宾至如归”与落款小印；引导图不画，AI 只画空白绢心。</summary>
    private static void EastLettering(CanvasItem ci, float h)
    {
        var y = Scroll.Position.Y + 54;
        foreach (var ch in "宾至如归")
        {
            ci.DrawString(UiFonts.Title, new Vector2(Scroll.GetCenter().X - 17, y), ch.ToString(), HorizontalAlignment.Left, -1, 34, Cel.Ink);
            y += 38;
        }

        ci.DrawRect(new Rect2(Scroll.GetCenter().X - 7, Scroll.End.Y - 28, 14, 14), UiPalette.Cinnabar);
    }

    /// <summary>字层：水牌朱砂题头“今日”，下面白字竖写三样菜名（与交互提示的样例一致）。</summary>
    private static void NorthLettering(CanvasItem ci)
    {
        var r = Menu;
        ci.DrawString(UiFonts.Title, new Vector2(r.Position.X + 9, r.Position.Y + 24), "今日", HorizontalAlignment.Left, -1, 19, InnTone.Silk);
        string[] dishes = ["菱角", "黄酒", "阳春面"];
        for (var i = 0; i < dishes.Length; i++)
        {
            // 竖写自右向左。
            var x = r.End.X - 19 - i * 16;
            var y = r.Position.Y + 50;
            foreach (var ch in dishes[i])
            {
                ci.DrawString(UiFonts.Title, new Vector2(x, y), ch.ToString(), HorizontalAlignment.Left, -1, 14, Colors.White with { A = 0.9f });
                y += 16;
            }
        }
    }

    /// <summary>柜台后的博古货架：四层，酒坛、叠碗、瓷瓶与纸包。</summary>
    private static void Shelves(CanvasItem ci, Rect2 r)
    {
        Cel.Box(ci, r, Cel.WoodDark, 2f);
        ci.DrawRect(r.Grow(-8), Color.FromHtml("#3A2A20"));
        float[] boards = [r.Position.Y + 78, r.Position.Y + 150, r.Position.Y + 222];
        foreach (var by in boards)
        {
            Cel.Box(ci, new Rect2(r.Position.X + 4, by, r.Size.X - 8, 10), Cel.WoodLight, 1.4f);
        }

        // 第一层：瓷瓶与纸包。
        for (var i = 0; i < 9; i++)
        {
            var x = r.Position.X + 30 + i * 50;
            var baseY = boards[0];
            if (i % 3 == 1)
            {
                Cel.Box(ci, new Rect2(x - 18, baseY - 30, 36, 30), Cel.Paper, 1.4f);
                ci.DrawLine(new Vector2(x, baseY - 30), new Vector2(x, baseY), UiPalette.Cinnabar, 2f);
            }
            else
            {
                Cel.Shape(ci, [new(x - 8, baseY - 50), new(x + 8, baseY - 50), new(x + 16, baseY - 22), new(x + 12, baseY), new(x - 12, baseY), new(x - 16, baseY - 22)],
                    i % 2 == 0 ? InnTone.Porcelain : InnTone.Azurite, 1.4f);
                ci.DrawLine(new Vector2(x - 14, baseY - 26), new Vector2(x + 14, baseY - 26), InnTone.Azurite, 2f);
            }
        }

        // 第二层：叠碗。
        for (var i = 0; i < 6; i++)
        {
            var x = r.Position.X + 44 + i * 76;
            for (var k = 0; k < 3; k++)
            {
                Cel.Shape(ci, [new(x - 24, boards[1] - 14 - k * 12), new(x + 24, boards[1] - 14 - k * 12), new(x + 14, boards[1] - 2 - k * 12), new(x - 14, boards[1] - 2 - k * 12)],
                    InnTone.Porcelain, 1.2f);
            }
        }

        // 第三层：小酒坛，红纸封口。
        for (var i = 0; i < 7; i++)
        {
            var c = new Vector2(r.Position.X + 40 + i * 66, boards[2] - 26);
            Cel.Shape(ci, Cel.Ellipse(c, 24, 26, 18), Color.FromHtml("#6A4A32"), 1.6f);
            ci.DrawColoredPolygon(Cel.Ellipse(c + new Vector2(-8, -6), 7, 12, 10), Color.FromHtml("#8A6848"));
            Cel.Box(ci, new Rect2(c.X - 12, c.Y - 30, 24, 10), UiPalette.Cinnabar, 1.2f);
        }
    }

    /// <summary>水牌：黑漆牌，朱砂题头；菜名在字层（<see cref="NorthLettering"/>）。</summary>
    private static void MenuBoard(CanvasItem ci, Rect2 r)
    {
        Cel.Box(ci, r, Cel.WoodDark, 2f);
        ci.DrawRect(new Rect2(r.Position.X + 6, r.Position.Y + 6, r.Size.X - 12, 22), UiPalette.Cinnabar);
    }

    /// <summary>后厨门：门洞透灶火暖光，石青门帘垂到齐胸，中缝分开。</summary>
    private static void KitchenDoor(CanvasItem ci, Rect2 r)
    {
        Cel.Box(ci, r.Grow(8), InnTone.Lacquer, 1.8f);
        ci.DrawRect(r, Cel.Interior);
        ci.DrawPolygon([r.Position, new(r.End.X, r.Position.Y), r.End, new(r.Position.X, r.End.Y)],
            [Cel.Glow with { A = 0.05f }, Cel.Glow with { A = 0.05f }, Cel.Glow with { A = 0.5f }, Cel.Glow with { A = 0.5f }]);
        var half = r.Size.X / 2;
        foreach (var (x0, sway) in new[] { (r.Position.X, -6f), (r.Position.X + half, 6f) })
        {
            Vector2[] cloth = [new(x0, r.Position.Y), new(x0 + half, r.Position.Y), new(x0 + half + sway, r.Position.Y + 120), new(x0 + sway, r.Position.Y + 118)];
            Cel.Shape(ci, cloth, Cel.Cloth, 1.8f);
            ci.DrawLine(new Vector2(x0 + 6, r.Position.Y + 20), new Vector2(x0 + half - 6, r.Position.Y + 20), InnTone.Silk with { A = 0.7f }, 2.5f);
        }

        ci.DrawCircle(new Vector2(r.GetCenter().X, r.Position.Y + 62), 16, InnTone.Silk with { A = 0.85f });
        ci.DrawArc(new Vector2(r.GetCenter().X, r.Position.Y + 62), 10, 0, Mathf.Tau, 16, Cel.Cloth, 3f, true);
    }
}

/// <summary>
/// 内墙正立面（只用于 <see cref="PieceGuideExport"/> 导出引导图）：墙面局部坐标 x 沿墙 0–宽、y 自墙顶 -高 到地面 0，不经投影。
/// AI 按它出整面墙（正面平视、无透视），游戏里经墙面的 Local 仿射变换贴回，斜视下与布局一致；字轴与水牌的字不画，由字层补写。
/// </summary>
public partial class InnWallElevation : Node2D
{
    private readonly bool _north;

    public InnWallElevation(bool north)
    {
        _north = north;
        var length = north ? InnSamples.Room.Size.X : InnSamples.Room.Size.Y;
        Box = new Rect2(0, -InnSamples.WallHeight, length, InnSamples.WallHeight);
    }

    public Rect2 Box { get; }

    public override void _Draw()
    {
        DrawRect(Box, InnTone.Plaster);
        if (_north)
        {
            InnShell.NorthWall(this, Box.Size.X, Box.Size.Y);
        }
        else
        {
            InnShell.EastWall(this, Box.Size.X, Box.Size.Y);
        }
    }
}

/// <summary>吊灯引导图（<see cref="PieceGuideExport"/> 用）：屏幕坐标，原点为灯笼顶，按游戏里的大小画一盏占位灯笼。</summary>
public partial class InnLanternGuide : Node2D
{
    public const float Size = 0.95f;

    /// <summary>灯身宽约 ±16、自顶到流苏末端约 78（均乘 Size），外留描边余量，再按竖直投影比例缩放。</summary>
    public static Rect2 Box => new(new Vector2(-19, -2) * TownView.Upright, new Vector2(38, 80) * TownView.Upright);

    public override void _Draw()
    {
        DrawSetTransform(Vector2.Zero, 0, Vector2.One * TownView.Upright);
        Cel.HangingLantern(this, Vector2.Zero, Size);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}

/// <summary>
/// 地上的光：吊灯下的暖黄光斑与门口斜进来的光，叠加混合，画在壳之上、排序件之下。随光色时段（M3-01 光影）：
/// 白天门口是雨后天光；日落时门口一道拉长的暖光；夜里吊灯光斑更大更亮，门口只剩一缕淡淡的月色。
/// 所在的贴地层被整体调色压暗，本层用 <see cref="SceneTimes.Unlit"/> 抵消，灯光不跟着变暗。
/// </summary>
public partial class InnFloorLight : Node2D
{
    private readonly SceneTime _light;

    public InnFloorLight(SceneTime light = SceneTime.Day, Color? tint = null)
    {
        _light = light;
        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        Modulate = SceneTimes.Unlit(tint ?? Colors.White);
    }

    public override void _Draw()
    {
        var (radius, alpha) = _light switch
        {
            SceneTime.Night => (250f, 0.3f),
            SceneTime.Dusk => (215f, 0.21f),
            _ => (200f, 0.17f),
        };
        foreach (var lamp in InnSamples.Lanterns)
        {
            Pool(this, new Vector2(lamp.X, lamp.Y), radius, Cel.Glow with { A = alpha });
            if (_light == SceneTime.Night)
            {
                // 灯下一小圈更亮的光心。
                Pool(this, new Vector2(lamp.X, lamp.Y), 90, Cel.Glow with { A = 0.14f });
            }
        }

        // 门口的光：日在西南，光自门洞斜向东北落在地上（日落时更低更长、偏橙；夜里是一缕偏蓝的月色）。
        var (color, length) = _light switch
        {
            SceneTime.Night => (new Color(0.55f, 0.68f, 0.95f, 0.1f), 200f),
            SceneTime.Dusk => (new Color(1f, 0.72f, 0.42f, 0.32f), 420f),
            _ => (InnTone.Daylight with { A = 0.3f }, 250f),
        };
        var d = InnSamples.Room.End.Y;
        var reach = new Vector2(0.49f, -0.87f) * length;
        Vector2[] spill = [new(InnSamples.DoorWest, d), new(InnSamples.DoorEast, d), new Vector2(InnSamples.DoorEast, d) + reach, new Vector2(InnSamples.DoorWest, d) + reach];
        DrawPolygon(spill.Select(p => TownView.P(p)).ToArray(), [color, color, color with { A = 0 }, color with { A = 0 }]);
    }

    /// <summary>地面上的圆形光斑：中心亮、边缘淡出，经投影压扁。</summary>
    public static void Pool(CanvasItem ci, Vector2 center, float radius, Color color, float z = 0)
    {
        using var batch = new PolyBatch(ci);
        Pool(batch, center, radius, color, z);
    }

    /// <summary>同上，攒进合批（28 个扇面合成一次绘制调用）。</summary>
    internal static void Pool(PolyBatch batch, Vector2 center, float radius, Color color, float z = 0)
    {
        const int n = 28;
        var c = TownView.P(center, z);
        var edge = color with { A = 0 };
        for (var i = 0; i < n; i++)
        {
            var a0 = Mathf.Tau * i / n;
            var a1 = Mathf.Tau * (i + 1) / n;
            var p0 = TownView.P(center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius, z);
            var p1 = TownView.P(center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius, z);
            batch.Add([c, p0, p1], [color, edge, edge]);
        }
    }
}

/// <summary>南墙、西墙剖切到齐腰的一段：外侧粉墙与条石墙脚，顶上是深赭剖切面；门洞两侧立门柱残段。</summary>
public partial class InnCutWall : TownPiece
{
    public InnCutWall(Vector3 min, Vector3 max, bool post = false)
    {
        Occluder = false;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        var faces = Solid.Box(min, max, post ? InnTone.Lacquer : InnTone.Plaster, 1.8f);
        FaceKit.Recolor(faces, TownView.Above, InnTone.Section);
        if (!post)
        {
            var size = max - min;
            if (size.X > size.Y)
            {
                FaceKit.Decorate(faces, new Vector3(0, 1, 0), TownView.Local(new Vector3(min.X, max.Y, 0), Vector3.Right, TownView.Below),
                    ci => Base(ci, size.X, size.Z));
            }
            else
            {
                FaceKit.Decorate(faces, new Vector3(-1, 0, 0), TownView.Local(new Vector3(min.X, max.Y, 0), new Vector3(0, -1, 0), TownView.Below),
                    ci => Base(ci, size.Y, size.Z));
            }
        }

        Faces.AddRange(faces);
        Seal(new Rect2(min.X, min.Y, max.X - min.X, max.Y - min.Y));
    }

    /// <summary>粉壁纹理（inn.wall.plaster）偏灰，乘上略大于 1 的系数提回粉壁的暖白。</summary>
    private static readonly Color PlasterLift = new(1.14f, 1.12f, 1.07f);

    /// <summary>外侧立面：粉壁 + 30 高条石墙脚。有 AI 纹理时粉壁贴 inn.wall.plaster、墙脚取驳岸条石纹理（town.embankment）最上一行。</summary>
    internal static void Base(CanvasItem ci, float w, float h)
    {
        ci.DrawRect(new Rect2(0, -h, w, h), InnTone.Plaster);
        var plaster = PieceArt.FindTexture("inn.wall.plaster");
        var stone = PieceArt.FindTexture("town.embankment");
        if (plaster is { } p && stone is { } s)
        {
            var ppx = p.Texture.GetSize().X / p.WorldSize;
            for (var x = 0f; x < w; x += p.WorldSize)
            {
                var cell = new Vector2(Mathf.Min(p.WorldSize, w - x), h - 30);
                ci.DrawTextureRectRegion(p.Texture, new Rect2(x, -h, cell.X, cell.Y), new Rect2(0, 0, cell.X * ppx, cell.Y * ppx), PlasterLift);
            }

            // 驳岸纹理三行条石，取最上一行（约三分之一高）压成 30 高的墙脚。
            var row = s.Texture.GetSize().Y / 3;
            var spx = row / 30;
            var span = s.Texture.GetSize().X / spx;
            for (var x = 0f; x < w; x += span)
            {
                var len = Mathf.Min(span, w - x);
                ci.DrawTextureRectRegion(s.Texture, new Rect2(x, -30, len, 30), new Rect2(0, 0, len * spx, row), new Color(1.05f, 1.05f, 1.02f));
            }

            ci.DrawLine(new Vector2(0, -30), new Vector2(w, -30), Cel.Ink with { A = 0.35f }, 1.6f);
            return;
        }

        ci.DrawRect(new Rect2(0, -30, w, 30), Cel.Stone);
        ci.DrawLine(new Vector2(0, -29), new Vector2(w, -29), Cel.StoneLight, 3);
        for (var x = 55f; x < w; x += 60)
        {
            ci.DrawLine(new Vector2(x, -28), new Vector2(x, 0), Cel.Ink with { A = 0.5f }, 1.8f);
        }
    }

    /// <summary>剖切墙全套：西墙、门洞两侧的南墙、门柱残段与门槛。</summary>
    public static IEnumerable<TownPiece> Build()
    {
        var room = InnSamples.Room;
        var (w, d, t, h) = (room.Size.X, room.Size.Y, InnSamples.WallThickness, InnSamples.CutHeight);
        yield return new InnCutWall(new Vector3(-t, -t, 0), new Vector3(0, d, h));
        yield return new InnCutWall(new Vector3(-t, d, 0), new Vector3(InnSamples.DoorWest, d + t, h));
        yield return new InnCutWall(new Vector3(InnSamples.DoorEast, d, 0), new Vector3(w + t, d + t, h));
        foreach (var x in new[] { InnSamples.DoorWest, InnSamples.DoorEast })
        {
            yield return new InnCutWall(new Vector3(x - 15, d - 6, 0), new Vector3(x + 15, d + t + 6, h + 10), post: true);
        }
    }
}

/// <summary>门槛：一道低木槛，可跨过（不挡路）。</summary>
public partial class InnSill : TownPiece
{
    public InnSill()
    {
        Occluder = false;
        var d = InnSamples.Room.End.Y;
        Faces.AddRange(Solid.Box(new Vector3(InnSamples.DoorWest + 15, d + 4, 0), new Vector3(InnSamples.DoorEast - 15, d + 18, 12), Cel.WoodDark, 1.4f));
        Seal(new Rect2(InnSamples.DoorWest + 15, d + 4, InnSamples.DoorEast - InnSamples.DoorWest - 30, 14));
    }
}

/// <summary>柜台：漆木台身、深色台面，台上算盘、账本、茶壶。</summary>
public partial class InnCounter : TownPiece
{
    private readonly Vector3 _teapot;

    public InnCounter()
    {
        var r = InnSamples.Counter;
        const float top = 95;
        var body = Solid.Box(new Vector3(r.Position.X, r.Position.Y, 0), new Vector3(r.End.X, r.End.Y, top), InnTone.Lacquer, 2f);
        FaceKit.Decorate(body, new Vector3(0, 1, 0), TownView.Local(new Vector3(r.Position.X, r.End.Y, top), Vector3.Right, TownView.Below),
            ci => Front(ci, r.Size.X, top));
        Faces.AddRange(body);
        Faces.AddRange(Solid.Box(new Vector3(r.Position.X - 8, r.Position.Y - 8, top), new Vector3(r.End.X + 8, r.End.Y + 8, top + 12), Cel.WoodDark, 2f));

        const float z = top + 12;
        Faces.Add(FaceKit.Flat(new Rect2(r.Position.X + 60, r.Position.Y + 22, 90, 40), z, Cel.WoodDark, ci => Abacus(ci, 90, 40)));
        Faces.Add(FaceKit.Flat(new Rect2(r.Position.X + 180, r.Position.Y + 24, 52, 38), z, Cel.Paper, ci =>
        {
            ci.DrawLine(new Vector2(26, 0), new Vector2(26, 38), Cel.PaperShade, 2f);
            for (var y = 8f; y < 34; y += 7) ci.DrawLine(new Vector2(30, y), new Vector2(48, y), Cel.Ink with { A = 0.5f }, 1.2f);
        }));
        _teapot = new Vector3(r.Position.X + 360, r.Position.Y + 40, z);
        Seal(r);
        UseArt("inn.counter");
    }

    private static void Front(CanvasItem ci, float w, float h)
    {
        ci.DrawRect(new Rect2(0, h - 16, w, 16), Cel.WoodDark);
        for (var x = 16f; x < w - 60; x += 110)
        {
            var panel = new Rect2(x, 16, 94, h - 42);
            ci.DrawRect(panel, InnTone.LacquerLight);
            ci.DrawRect(panel, Cel.Ink with { A = 0.6f }, false, 1.8f);
            ci.DrawRect(panel.Grow(-10), InnTone.Lacquer, false, 2f);
        }

        var c = new Vector2(w / 2, h / 2 - 4);
        ci.DrawColoredPolygon([c + new Vector2(0, -16), c + new Vector2(16, 0), c + new Vector2(0, 16), c + new Vector2(-16, 0)], UiPalette.Gilt.Darkened(0.15f));
    }

    private static void Abacus(CanvasItem ci, float w, float d)
    {
        ci.DrawRect(new Rect2(4, 4, w - 8, d - 8), Color.FromHtml("#8A6240"));
        ci.DrawLine(new Vector2(4, d * 0.35f), new Vector2(w - 4, d * 0.35f), Cel.WoodDark, 2.5f);
        for (var x = 12f; x < w - 6; x += 10)
        {
            ci.DrawLine(new Vector2(x, 4), new Vector2(x, d - 4), Cel.Ink with { A = 0.6f }, 1f);
            ci.DrawCircle(new Vector2(x, d * 0.2f), 3.2f, Cel.Ink);
            ci.DrawCircle(new Vector2(x, d * 0.6f + Cel.Rand(3, (int)x) * 8), 3.2f, Cel.Ink);
        }
    }

    protected override void DrawExtras() => InnTableNode.Teapot(this, TownView.P(_teapot), Cel.Leaf.Darkened(0.2f));
}

/// <summary>八仙桌与长凳：一张桌连同四面（雅座为南北两面）长凳作为一件，按远近先画北、东凳，再画桌，最后西、南凳。</summary>
public partial class InnTableNode : TownPiece
{
    private const float Half = 46;
    private const float TopZ = 80;
    private readonly InnTable _table;
    private readonly List<(Vector3 At, int Kind)> _items = [];

    public InnTableNode(InnTable table)
    {
        _table = table;
        var c = table.Center;
        var booth = table.Kind == InnTableKind.Booth;
        const float gap = 76;

        Bench(c + new Vector2(0, -gap), true);
        if (!booth) Bench(c + new Vector2(gap, 0), false);

        foreach (var (dx, dy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            var leg = c + new Vector2(dx * (Half - 8), dy * (Half - 8));
            Faces.AddRange(Solid.Box(new Vector3(leg.X - 5, leg.Y - 5, 0), new Vector3(leg.X + 5, leg.Y + 5, TopZ - 14), Cel.WoodDark, 1.2f));
        }

        Faces.AddRange(Solid.Box(new Vector3(c.X - Half + 4, c.Y - Half + 4, TopZ - 16), new Vector3(c.X + Half - 4, c.Y + Half - 4, TopZ - 4), Cel.Wood, 1.4f));
        var top = Solid.Box(new Vector3(c.X - Half, c.Y - Half, TopZ - 4), new Vector3(c.X + Half, c.Y + Half, TopZ + 4), Cel.WoodLight, 1.8f);
        FaceKit.Decorate(top, TownView.Above, TownView.Local(new Vector3(c.X - Half, c.Y - Half, TopZ + 4), Vector3.Right, new Vector3(0, 1, 0)), ci =>
        {
            for (var y = 18f; y < Half * 2; y += 18) ci.DrawLine(new Vector2(4, y), new Vector2(Half * 2 - 4, y), Cel.Wood with { A = 0.55f }, 1.4f);
        });
        Faces.AddRange(top);

        if (!booth) Bench(c + new Vector2(-gap, 0), false);
        Bench(c + new Vector2(0, gap), true);

        var z = TopZ + 4;
        switch (table.Kind)
        {
            case InnTableKind.Tea:
                _items.Add((new Vector3(c.X + 10, c.Y - 10, z), 0));
                _items.Add((new Vector3(c.X - 24, c.Y + 18, z), 1));
                _items.Add((new Vector3(c.X + 26, c.Y + 22, z), 1));
                break;
            case InnTableKind.Meal:
                _items.Add((new Vector3(c.X - 20, c.Y - 12, z), 2));
                _items.Add((new Vector3(c.X + 22, c.Y + 16, z), 2));
                _items.Add((new Vector3(c.X - 6, c.Y + 26, z), 1));
                break;
            case InnTableKind.Booth:
                // 雅座桌上三封信并排、各压一方朱印（会面场景的道具占位）。
                for (var i = 0; i < 3; i++)
                {
                    var letter = new Rect2(c.X - 38 + i * 26, c.Y + 4 + (i == 1 ? -6 : 0), 20, 32);
                    Faces.Add(FaceKit.Flat(letter, z + 0.5f, Cel.Paper, ci => ci.DrawRect(new Rect2(6, 22, 8, 7), UiPalette.Cinnabar)));
                }

                _items.Add((new Vector3(c.X + 22, c.Y - 22, z), 0));
                break;
        }

        var reach = booth ? new Vector2(Half + 10, gap + 16) : new Vector2(gap + 16, gap + 16);
        Seal(new Rect2(c - reach, reach * 2));
        UseArt($"inn.{table.Id}");
        return;

        void Bench(Vector2 at, bool alongX)
        {
            var half = alongX ? new Vector2(52, 12) : new Vector2(12, 52);
            foreach (var s in new[] { -1, 1 })
            {
                var leg = at + (alongX ? new Vector2(s * 40, 0) : new Vector2(0, s * 40));
                Faces.AddRange(Solid.Box(new Vector3(leg.X - 4, leg.Y - 8, 0), new Vector3(leg.X + 4, leg.Y + 8, 40), Cel.WoodDark, 1.1f));
            }

            Faces.AddRange(Solid.Box(new Vector3(at.X - half.X, at.Y - half.Y, 40), new Vector3(at.X + half.X, at.Y + half.Y, 47), Cel.WoodLight, 1.4f));
        }
    }

    protected override void DrawExtras()
    {
        foreach (var (at, kind) in _items)
        {
            var p = TownView.P(at);
            switch (kind)
            {
                case 0:
                    Teapot(this, p, InnTone.Porcelain);
                    break;
                case 1:
                    Cel.Shape(this, [p + new Vector2(-8, -12), p + new Vector2(8, -12), p + new Vector2(6, 0), p + new Vector2(-6, 0)], InnTone.Porcelain, 1.2f);
                    DrawLine(p + new Vector2(-7, -8), p + new Vector2(7, -8), InnTone.Azurite, 1.6f);
                    break;
                default:
                    Cel.Shape(this, [p + new Vector2(-14, -14), p + new Vector2(14, -14), p + new Vector2(8, 0), p + new Vector2(-8, 0)], InnTone.Porcelain, 1.3f);
                    DrawColoredPolygon(Cel.Ellipse(p + new Vector2(0, -14), 13, 4, 12), Cel.Glow.Darkened(0.1f));
                    DrawLine(p + new Vector2(4, -26), p + new Vector2(18, -6), Cel.WoodDark, 2f, true);
                    DrawLine(p + new Vector2(8, -27), p + new Vector2(21, -8), Cel.WoodDark, 2f, true);
                    break;
            }
        }
    }

    /// <summary>茶壶精灵（屏幕空间，底部中心在 p）。</summary>
    public static void Teapot(CanvasItem ci, Vector2 p, Color tone)
    {
        var u = TownView.Upright;
        var body = Cel.Ellipse(p + new Vector2(0, -14 * u), 16 * u, 14 * u, 20);
        ci.DrawPolyline([p + new Vector2(14 * u, -16 * u), p + new Vector2(28 * u, -26 * u), p + new Vector2(32 * u, -30 * u)], Cel.Ink, 4 * u, true);
        ci.DrawArc(p + new Vector2(-16 * u, -15 * u), 8 * u, Mathf.Pi * 0.5f, Mathf.Pi * 1.5f, 10, Cel.Ink, 3 * u, true);
        Cel.Shape(ci, body, tone, 1.6f);
        ci.DrawColoredPolygon(Cel.Ellipse(p + new Vector2(-5 * u, -18 * u), 5 * u, 6 * u, 10), Colors.White with { A = 0.5f });
        ci.DrawCircle(p + new Vector2(0, -29 * u), 4 * u, Cel.Ink);
    }
}

/// <summary>楼梯：沿东墙自南向北十三级升到楼板高度，北端接楼上平台；西侧扶手与望柱，平台沿口一道栏杆。</summary>
public partial class InnStairs : TownPiece
{
    public InnStairs()
    {
        var r = InnSamples.Stairs;
        var (x0, x1) = (r.Position.X, r.End.X);
        var foot = r.End.Y;
        var landing = InnSamples.StairsLanding;
        var h = InnSamples.WallHeight;
        const int steps = 13;
        var run = (foot - landing) / steps;
        var rise = h / steps;

        // 平台（楼上楼板的一角）：西面木板壁。
        var block = Solid.Box(new Vector3(x0, r.Position.Y, 0), new Vector3(x1, landing, h), Cel.Wood, 2f);
        FaceKit.Decorate(block, new Vector3(-1, 0, 0), TownView.Local(new Vector3(x0, landing, h), new Vector3(0, -1, 0), TownView.Below), ci =>
        {
            for (var x = 30f; x < landing - r.Position.Y; x += 30) ci.DrawLine(new Vector2(x, 0), new Vector2(x, h), Cel.WoodDark with { A = 0.7f }, 1.6f);
            ci.DrawRect(new Rect2(0, 0, landing - r.Position.Y, 18), Cel.WoodDark);
        });
        Faces.AddRange(block);

        // 自上而下（远到近）逐级搭。
        for (var i = steps - 1; i >= 0; i--)
        {
            var y1 = foot - i * run;
            var y0 = y1 - run;
            Faces.AddRange(Solid.Box(new Vector3(x0, y0, 0), new Vector3(x1, y1, (i + 1) * rise), Cel.WoodLight, 1.6f));
        }

        // 平台栏杆与梯边扶手。
        const float rail = 86;
        for (var y = r.Position.Y + 10; y <= landing; y += 60)
        {
            Faces.AddRange(Solid.Box(new Vector3(x0 + 2, y - 4, h), new Vector3(x0 + 10, y + 4, h + rail), Cel.WoodDark, 1.2f));
        }

        Faces.AddRange(Solid.Box(new Vector3(x0, r.Position.Y, h + rail - 8), new Vector3(x0 + 12, landing, h + rail), InnTone.Lacquer, 1.4f));
        for (var i = 0; i < steps; i += 2)
        {
            var y = foot - (i + 0.5f) * run;
            Faces.AddRange(Solid.Box(new Vector3(x0 + 2, y - 4, (i + 1) * rise), new Vector3(x0 + 10, y + 4, (i + 1) * rise + rail), Cel.WoodDark, 1.2f));
        }

        Faces.Add(new Face
        {
            Points = [new(x0, foot - run * 0.5f, rise + rail), new(x0, landing, h + rail), new(x0, landing, h + rail - 12), new(x0, foot - run * 0.5f, rise + rail - 12)],
            Normal = new Vector3(-1, 0, 0), Color = InnTone.Lacquer, Outline = 1.4f,
        });
        Seal(r);
        UseArt("inn.stairs");
    }
}

/// <summary>屏风：四扇绢面，画一幅连贯的青绿山水，朝西（朝大堂）一面可见。</summary>
public partial class InnScreenNode : TownPiece
{
    private const float Top = 200;
    private const float Panel = 80;

    public InnScreenNode()
    {
        var r = InnSamples.Screen;
        var count = (int)(r.Size.Y / Panel);
        for (var i = count - 1; i >= 0; i--)
        {
            var y0 = r.Position.Y + i * Panel;
            var offset = i * Panel;
            var leaf = Solid.Box(new Vector3(r.Position.X, y0 + 1, 16), new Vector3(r.End.X, y0 + Panel - 1, Top), Cel.WoodDark, 1.8f);
            FaceKit.Decorate(leaf, new Vector3(-1, 0, 0), TownView.Local(new Vector3(r.Position.X, y0 + 1, Top), new Vector3(0, 1, 0), TownView.Below),
                ci => Painting(ci, Panel - 2, Top - 16, offset));
            Faces.AddRange(Solid.Box(new Vector3(r.Position.X - 10, y0 + 12, 0), new Vector3(r.End.X + 10, y0 + 22, 16), Cel.WoodDark, 1.2f));
            Faces.AddRange(Solid.Box(new Vector3(r.Position.X - 10, y0 + Panel - 22, 0), new Vector3(r.End.X + 10, y0 + Panel - 12, 16), Cel.WoodDark, 1.2f));
            Faces.AddRange(leaf);
        }

        Seal(r);
        UseArt("inn.screen");
    }

    /// <summary>一扇的画心：各扇按 offset 接续同一条山脊，远山石青、近坡石绿，第二扇上一轮朱日。</summary>
    private static void Painting(CanvasItem ci, float w, float h, float offset)
    {
        var inner = new Rect2(6, 6, w - 12, h - 12);
        ci.DrawRect(inner, InnTone.Silk);
        ci.DrawPolygon([inner.Position, new(inner.End.X, inner.Position.Y), new(inner.End.X, inner.Position.Y + 70), new(inner.Position.X, inner.Position.Y + 70)],
            [InnTone.AzuriteLight with { A = 0.35f }, InnTone.AzuriteLight with { A = 0.35f }, InnTone.Silk with { A = 0 }, InnTone.Silk with { A = 0 }]);
        if (offset is >= 80 and < 160)
        {
            ci.DrawCircle(new Vector2(w * 0.55f, 34), 9, UiPalette.Cinnabar.Lightened(0.1f));
        }

        Ridge(ci, inner, offset, 0.55f, 40, 0.021f, InnTone.AzuriteLight, InnTone.Azurite);
        Ridge(ci, inner, offset, 0.78f, 26, 0.034f, InnTone.Malachite, InnTone.Malachite.Darkened(0.25f));
        ci.DrawRect(new Rect2(0, 0, w, h), Cel.WoodDark, false, 6);
    }

    private static void Ridge(CanvasItem ci, Rect2 inner, float offset, float level, float amplitude, float freq, Color fill, Color line)
    {
        var pts = new List<Vector2>();
        for (var x = inner.Position.X; x <= inner.End.X + 0.1f; x += 4)
        {
            var gx = x + offset;
            var y = inner.Position.Y + inner.Size.Y * level - amplitude * (0.6f * Mathf.Abs(Mathf.Sin(gx * freq)) + 0.4f * Mathf.Sin(gx * freq * 2.7f + 1));
            pts.Add(new Vector2(x, y));
        }

        ci.DrawColoredPolygon([.. pts, inner.End, new(inner.Position.X, inner.End.Y)], fill);
        ci.DrawPolyline(pts.ToArray(), line, 2f, true);
    }
}

/// <summary>大堂里的立柱：石鼓柱础、八角漆柱，到楼板高度为止。</summary>
public partial class InnPillarNode : TownPiece
{
    public InnPillarNode(Vector2 p, int index)
    {
        Occluder = false;
        Faces.AddRange(Solid.ExtrudeZ(Octagon(p, 24), 0, 16, Cel.Stone, Cel.StoneLight, 1.6f));
        Faces.AddRange(Solid.ExtrudeZ(Octagon(p, 15), 16, InnSamples.WallHeight, InnTone.Lacquer, InnTone.Section, 1.6f));
        Seal(new Rect2(p - new Vector2(24, 24), new Vector2(48, 48)));
        UseArt($"inn.pillar.{index + 1}");
    }

    private static Vector2[] Octagon(Vector2 c, float r) =>
        Enumerable.Range(0, 8).Select(i => c + new Vector2(Mathf.Cos(Mathf.Tau * (i + 0.5f) / 8), Mathf.Sin(Mathf.Tau * (i + 0.5f) / 8)) * r).ToArray();
}

/// <summary>青花盆栽：立着的精灵，一丛兰叶。</summary>
public partial class InnPlantNode : TownPiece
{
    private readonly Vector2 _at;

    public InnPlantNode(Vector2 at, int index)
    {
        Occluder = false;
        _at = at;
        Position = TownView.P(at);
        Foot = new Rect2(at - new Vector2(28, 28), new Vector2(56, 56));
        ScreenBox = new Rect2(Position + new Vector2(-70, -150) * TownView.Upright, new Vector2(140, 160) * TownView.Upright);
        UseArt($"inn.plant.{index + 1}");
    }

    public override void _Draw()
    {
        DrawSetTransform(-Position, 0, Vector2.One);
        Cel.GroundShadow(this, _at + new Vector2(8, -8), 36, 30, 0.25f);
        if (Art is not null)
        {
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            DrawArt();
            return;
        }

        DrawSetTransform(Vector2.Zero, 0, Vector2.One * TownView.Upright);
        Color[] tones = [Cel.LeafDark, Cel.Leaf, Cel.LeafLight];
        for (var i = 0; i < 11; i++)
        {
            var a = -Mathf.Pi / 2 + (i - 5) * 0.2f;
            var len = 70 + Cel.Rand(11, i) * 50;
            var tip = new Vector2(Mathf.Cos(a) * len, -50 + Mathf.Sin(a) * len);
            var bend = new Vector2(tip.X * 0.5f + (i - 5) * 6, -50 + tip.Y * 0.35f - 20);
            DrawPolyline([new Vector2(0, -50), bend, tip + new Vector2((i - 5) * 8, 10)], tones[i % 3], 5, true);
        }

        Vector2[] pot = [new(-30, -54), new(30, -54), new(24, -6), new(18, 0), new(-18, 0), new(-24, -6)];
        Cel.Shape(this, pot, InnTone.Porcelain, 2f);
        DrawLine(new Vector2(-28, -40), new Vector2(28, -40), InnTone.Azurite, 5);
        DrawArc(new Vector2(0, -24), 9, 0, Mathf.Tau, 14, InnTone.Azurite, 2.5f, true);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}

/// <summary>吊灯：悬空层，画在所有排序件之上；灯绳自画面上方垂下，灯周一圈叠加暖光。</summary>
public partial class InnLanterns : Node2D
{
    private readonly Node2D _halo = new() { Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };
    private readonly float _glow;
    private readonly float _haloRadius;

    /// <summary>灯笼本身是光源，不随整体调色变暗（抵消所在悬空层的调色）；夜里光晕更大更亮。</summary>
    public InnLanterns(SceneTime light = SceneTime.Day, Color? tint = null)
    {
        Modulate = SceneTimes.Unlit(tint ?? Colors.White);
        (_glow, _haloRadius) = light == SceneTime.Night ? (0.5f, 96f) : (0.35f, 70f);
    }

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        _halo.Draw += () =>
        {
            foreach (var l in InnSamples.Lanterns)
            {
                var c = TownView.P(l) + new Vector2(0, 38 * TownView.Upright);
                const int n = 24;
                var inner = Cel.Glow with { A = _glow };
                var edge = Cel.Glow with { A = 0 };
                for (var i = 0; i < n; i++)
                {
                    var a0 = Mathf.Tau * i / n;
                    var a1 = Mathf.Tau * (i + 1) / n;
                    _halo.DrawPrimitive([c, c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * _haloRadius, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * _haloRadius], [inner, edge, edge], []);
                }
            }
        };
        AddChild(_halo);
    }

    public override void _Draw()
    {
        // AI 灯笼件（inn.lantern）的 origin 以灯笼顶为原点、屏幕坐标计，六盏共用一件。
        var art = PieceArt.Find("inn.lantern");
        foreach (var l in InnSamples.Lanterns)
        {
            DrawLine(TownView.P(l.X, l.Y, InnSamples.WallHeight), TownView.P(l), Cel.Ink, 2f);
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
