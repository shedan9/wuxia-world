using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 城镇 / 街道探索展示页“芦湾河街”（M0-03 江南水镇样板的第一步：人定布局）。
/// 整个街区是一套世界坐标（x 东、y 南、z 上），经 <see cref="TownView"/> 的正交斜视投影画出：
/// 地面由 town_ground 着色器按反投影的世界坐标铺石板、河水与草地，房屋、廊棚、桥栏、摊位等由面搭成（按法线剔除与分阶明暗），
/// 人物与树为立着的精灵；行走、排序、遮挡与镜头由 <see cref="ExploreStage"/> 共用。
/// 客栈门前按 E 进入客栈大堂页（<see cref="ExploreInnPreview"/>），从大堂出门回到门前。
/// 房屋、树、杂件、平桥与石板街面已按架构文档 10.3 方案 C 换成 AI 出件（廊柱、坐栏与渡口石阶仍为程序化占位）。
/// 截图参数 <c>--tab</c>：0 旧渡石痕旁（交互提示）、1 南岸街被屋身遮挡、2 客栈门前、3 廊棚下、4 缩到 0.85 看平桥一带、5 西头民居（AI 出件样板）、6 廊棚西头外侧（屋面不淡出，查屋面与廊柱遮挡）。
/// </summary>
public partial class ExploreTownPreview : ExploreStage
{
    protected override Rect2 Bounds => TownSamples.Bounds;

    protected override IReadOnlyList<TownInteraction> Interactions => TownSamples.Interactions;

    protected override (string Region, string Name, string Time) PlaceInfo => ("芦湾", "芦湾河街", "申时　·　雨后初晴");

    protected override (Vector2 Ground, float Height, string Label)? Goal => (TownSamples.Goal, 120, "旧渡石痕");

    protected override string Caption => "2:1 等距布局样板：房屋、树、杂件、井台、平桥、廊棚屋面、街面、草地与驳岸为 AI 出件，石阶、廊柱与坐栏为几何贴 AI 纹理；人物仍为占位（M0-03）";

    protected override (Vector2 Hero, Vector2 Lu, float Zoom) Start(string? arrival)
    {
        if (arrival == "town.inn_door")
        {
            return (TownSamples.InnDoor, TownSamples.InnDoor + new Vector2(-110, 10), 1f);
        }

        return DevCapture.Tab switch
        {
            1 => (new Vector2(1900, 2950), new Vector2(1780, 2930), 1f),
            2 => (new Vector2(3760, 1440), new Vector2(3640, 1470), 1f),
            3 => (new Vector2(4050, 1700), new Vector2(3930, 1690), 1f),
            4 => (new Vector2(2900, 2300), new Vector2(2900, 2180), MinZoom),
            5 => (new Vector2(620, 1560), new Vector2(500, 1590), 1f),
            6 => (new Vector2(3120, 1700), new Vector2(3000, 1690), 1f),
            _ => (TownSamples.Spawn, TownSamples.Spawn + new Vector2(-120, -40), 1f),
        };
    }

    protected override void BuildScene()
    {
        BuildGround(GroundLayer);
        GroundLayer.AddChild(new TownGroundDetail());

        foreach (var house in TownSamples.Houses) Add(new TownHouseNode(house));
        foreach (var part in TownCorridorPart.Build(TownSamples.Corridor)) Add(part);
        foreach (var tree in TownSamples.Trees) Add(new TownTreeNode(tree));
        foreach (var prop in TownSamples.Props) Add(new TownPropNode(prop), prop.Kind != PropKind.Boat);
        var bridge = TownSamples.Bridge;
        Add(new TownRailNode(bridge.Position.X + 12, bridge, "town.bridge.rail_w"));
        Add(new TownRailNode(bridge.End.X - 12, bridge, "town.bridge.rail_e"));
    }

    /// <summary>地面：先画低处的河面，再画两岸（同一平面 z = 0），岸线以外一律延伸成草地。</summary>
    private static void BuildGround(Node2D parent)
    {
        var shader = GD.Load<Shader>("res://assets/shaders/town_ground.gdshader");
        void Ground(int kind, IEnumerable<Vector2> worldPolygon, float z = 0)
        {
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("kind", kind);
            material.SetShaderParameter("yaw", Mathf.DegToRad(TownView.Yaw));
            material.SetShaderParameter("pitch", Mathf.DegToRad(TownView.Pitch));
            material.SetShaderParameter("scale", TownView.Scale);
            material.SetShaderParameter("plane_z", z);
            if (kind == 1)
            {
                PieceArt.ApplyGround(material, "town.ground.flagstone", new Vector3(1.12f, 1.19f, 1.17f), macro: 0.05f);
            }
            else if (kind == 0)
            {
                PieceArt.ApplyGround(material, "town.ground.grass", GroundTint.Grass, 0.35f, 0.08f, 0.5f);
            }
            else if (kind == 2)
            {
                // 河水：按两岸参数算离岸远近，北岸驳岸倒影、天光与浪线全在着色器里画。
                material.SetShaderParameter("canal", true);
                material.SetShaderParameter("canal_n", TownSamples.NorthBankWave[0]);
                material.SetShaderParameter("canal_n2", TownSamples.NorthBankWave[1]);
                material.SetShaderParameter("canal_s", TownSamples.SouthBankWave[0]);
                material.SetShaderParameter("canal_s2", TownSamples.SouthBankWave[1]);
                material.SetShaderParameter("canal_wall", -TownSamples.WaterLevel);
                var obstacles = TownSamples.WaterObstacles;
                material.SetShaderParameter("canal_obj", Enumerable.Range(0, 6).Select(i => i < obstacles.Length ? obstacles[i].Box : Vector4.Zero).ToArray());
                material.SetShaderParameter("canal_obj_shape", Enumerable.Range(0, 6).Select(i => i < obstacles.Length ? obstacles[i].Shape : Vector2.Zero).ToArray());
            }

            parent.AddChild(new Polygon2D { Polygon = worldPolygon.Select(p => TownView.P(p, z)).ToArray(), Material = material });
        }

        const float far = 3000;
        var b = TownSamples.Bounds;
        var (west, east) = (b.Position.X - far, b.End.X + far);
        Ground(2, Rect(new Rect2(west, 1600, east - west, 1400)), TownSamples.WaterLevel);
        Ground(0, [new(west, -far), new(east, -far), .. TownLayout.Edge(TownSamples.NorthBank, east, west)]);
        Ground(0, [.. TownLayout.Edge(TownSamples.SouthBank, west, east), new(east, b.End.Y + far), new(west, b.End.Y + far)]);
        Ground(1, TownLayout.Street);
        Ground(1, Rect(TownSamples.Alley));
        Ground(1, Rect(TownSamples.Plaza));
        Ground(3, TownLayout.SouthBankStrip);
        Ground(1, TownLayout.SouthWalk);
    }

    private static Vector2[] Rect(Rect2 r) => [r.Position, new(r.End.X, r.Position.Y), r.End, new(r.Position.X, r.End.Y)];

    protected override Control CreateMiniMap(Func<(Vector2 Position, Vector2 Heading)> hero) => new TownMiniMap { Hero = hero };

    protected override bool InWalkArea(Vector2 q) => TownLayout.InWalkArea(q);

    /// <summary>渡口石阶上的高度：自岸沿向河里逐级降到接近水面。</summary>
    protected override float StepZ(Vector2 p)
    {
        var steps = TownSamples.FerrySteps;
        if (!steps.HasPoint(p))
        {
            return 0;
        }

        var bank = TownSamples.NorthBank(p.X);
        return -Mathf.Clamp((p.Y - bank) / (steps.End.Y - bank), 0, 1) * (-TownSamples.WaterLevel - 16);
    }

    protected override void Animate(float seconds)
    {
        foreach (var piece in Pieces)
        {
            if (piece is TownPropNode { IsBoat: true } boat)
            {
                boat.Bob(Motion.Enabled ? Mathf.Sin(seconds * 1.3f + boat.Foot.Position.X) * 3 : 0);
            }
        }
    }
}

/// <summary>布局的派生几何（世界平面坐标）：地面多边形与可走区判定，地面、碰撞与小地图共用一份。</summary>
public static class TownLayout
{
    private const float Step = 60;

    public static readonly Vector2[] Street =
        [.. Edge(_ => TownSamples.StreetNorth, 0, TownSamples.Bounds.End.X), .. Edge(TownSamples.NorthBank, TownSamples.Bounds.End.X, 0)];

    public static readonly Vector2[] SouthWalk =
        [.. Edge(x => TownSamples.SouthBank(x) + 20, 0, TownSamples.Bounds.End.X), .. Edge(_ => TownSamples.SouthWalkEnd, TownSamples.Bounds.End.X, 0)];

    public static readonly Vector2[] SouthBankStrip =
        [.. Edge(TownSamples.SouthBank, 0, TownSamples.Bounds.End.X), .. Edge(x => TownSamples.SouthBank(x) + 20, TownSamples.Bounds.End.X, 0)];

    public static readonly Vector2[] Canal =
        [.. Edge(TownSamples.NorthBank, 0, TownSamples.Bounds.End.X), .. Edge(TownSamples.SouthBank, TownSamples.Bounds.End.X, 0)];

    /// <summary>沿一条随 x 变化的边取点，from → to（可逆向）。</summary>
    public static IEnumerable<Vector2> Edge(Func<float, float> y, float from, float to)
    {
        var dir = Mathf.Sign(to - from);
        for (var x = from; dir > 0 ? x < to : x > to; x += dir * Step) yield return new Vector2(x, y(x));
        yield return new Vector2(to, y(to));
    }

    public static bool InWalkArea(Vector2 q)
    {
        var b = TownSamples.Bounds;
        if (q.X < b.Position.X + 20 || q.X > b.End.X - 20)
        {
            return false;
        }

        if (q.Y >= TownSamples.StreetNorth + 20 && q.Y <= TownSamples.NorthBank(q.X) - 24)
        {
            return true;
        }

        if (TownSamples.Alley.Grow(-20).HasPoint(q) || TownSamples.Plaza.Grow(-20).HasPoint(q) || TownSamples.FerrySteps.Grow(-16).HasPoint(q))
        {
            return true;
        }

        var bridge = TownSamples.Bridge;
        if (q.X > bridge.Position.X + 34 && q.X < bridge.End.X - 34 && q.Y > bridge.Position.Y - 60 && q.Y < bridge.End.Y + 60)
        {
            return true;
        }

        return q.Y >= TownSamples.SouthBank(q.X) + 30 && q.Y <= TownSamples.SouthWalkEnd - 20;
    }
}

/// <summary>贴地的立体细节，画在排序层之下：北岸条石驳岸立面与压顶、渡口石阶、平桥桥面与桥墩、系船桩。</summary>
public partial class TownGroundDetail : Node2D
{
    private readonly List<Face> _faces = [];
    private readonly PieceArt? _deckArt;

    public TownGroundDetail()
    {
        TextureRepeat = TextureRepeatEnum.Enabled;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        const float step = 60;
        var water = TownSamples.WaterLevel;
        var b = TownSamples.Bounds;
        var run = 0f;
        for (var x = b.Position.X - 3000; x < b.End.X + 3000; x += step)
        {
            var a = new Vector3(x, TownSamples.NorthBank(x), 0);
            var c = new Vector3(x + step, TownSamples.NorthBank(x + step), 0);
            var along = c - a;
            var offset = run;
            run += along.Length();
            _faces.Add(new Face
            {
                Points = [a, c, c + new Vector3(0, 0, water), a + new Vector3(0, 0, water)],
                Normal = new Vector3(-along.Y, along.X, 0).Normalized(), Color = Cel.Stone, Outline = 0,
                Local = TownView.Local(a, along.Normalized(), TownView.Below),
                Decal = ci => Embankment(ci, along.Length(), -water, offset),
            });
        }

        // 渡口石阶：五级，自岸沿逐级下到水边。
        var steps = TownSamples.FerrySteps;
        var bank = TownSamples.NorthBank(steps.GetCenter().X) - 10;
        const int count = 5;
        var depth = (steps.End.Y - bank) / count;
        // 与山路石阶同法：各面贴石面纹理，踢面压暗一阶（FaceTexture）。
        var slab = PieceArt.FindTexture("wild.ground.slab");
        for (var i = 0; i < count; i++)
        {
            var top = -(i + 1) * (-water - 16) / count;
            var box = Solid.Box(new Vector3(steps.Position.X, bank + i * depth, water), new Vector3(steps.End.X, steps.End.Y, top + 16), Cel.StoneLight, 1.6f);
            var k = Cel.Rand(19, i);
            _faces.AddRange(slab is { } t ? FaceTexture.Apply(box, t.Texture, t.WorldSize, new Color(1.02f + 0.08f * k, 1.06f + 0.08f * k, 1.04f + 0.07f * k), 1.4f) : box);
        }

        // 平桥桥面：有 AI 出件时画贴图（画在驳岸与石阶之后，与占位面同一次序），否则画占位面。
        _deckArt = PieceArt.Find("town.bridge.deck");
        if (_deckArt is null)
        {
            _faces.AddRange(BridgeFaces());
        }

        // 系船桩。
        foreach (var x in new[] { 1580f, 1720f, 3500f, 4100f })
        {
            var y = TownSamples.NorthBank(x) - 18;
            _faces.AddRange(Solid.Box(new Vector3(x - 9, y - 9, 0), new Vector3(x + 9, y + 9, 36), Cel.Wood, 1.4f));
        }
    }

    /// <summary>平桥贴地的部分：桥面条石、两道桥墩（栏杆另为排序件 <see cref="TownRailNode"/>）。</summary>
    public static List<Face> BridgeFaces()
    {
        var water = TownSamples.WaterLevel;
        var bridge = TownSamples.Bridge;
        var deck = new List<Face>();
        deck.AddRange(Solid.Box(new Vector3(bridge.Position.X, bridge.Position.Y + 280, water), new Vector3(bridge.End.X, bridge.Position.Y + 340, -30), Cel.Stone, 1.6f));
        deck.AddRange(Solid.Box(new Vector3(bridge.Position.X, bridge.Position.Y + 640, water), new Vector3(bridge.End.X, bridge.Position.Y + 700, -30), Cel.Stone, 1.6f));
        var slab = Solid.Box(new Vector3(bridge.Position.X, bridge.Position.Y, -30), new Vector3(bridge.End.X, bridge.End.Y, 2), Cel.StoneLight, 2f);
        var topFace = slab[^1];
        slab[^1] = new Face
        {
            Points = topFace.Points, Normal = topFace.Normal, Color = Cel.StoneLight,
            Local = TownView.Local(new Vector3(bridge.Position.X, bridge.Position.Y, 2), Vector3.Right, new Vector3(0, 1, 0)),
            Decal = ci =>
            {
                for (var y = 100f; y < bridge.Size.Y; y += 100)
                {
                    ci.DrawLine(new Vector2(0, y), new Vector2(bridge.Size.X, y), Cel.Ink with { A = 0.45f }, 2f);
                }

                ci.DrawLine(new Vector2(bridge.Size.X / 2, 0), new Vector2(bridge.Size.X / 2, bridge.Size.Y), Cel.Ink with { A = 0.3f }, 2f);
            },
        };
        deck.AddRange(slab);
        return deck;
    }

    private static readonly (Texture2D Texture, float WorldSize)? EmbankmentTexture = PieceArt.FindTexture("town.embankment");

    private static void Embankment(CanvasItem ci, float width, float height, float offset)
    {
        // AI 条石纹理（town.embankment，横向无缝）：按沿岸累计长度取 u，整幅高度对应驳岸高；水线苔带与白沫照旧叠加。
        if (EmbankmentTexture is { } tex)
        {
            var px = tex.Texture.GetSize().X / tex.WorldSize;
            var u0 = Mathf.PosMod(offset, tex.WorldSize) * px;
            ci.DrawTextureRectRegion(tex.Texture, new Rect2(0, 0, width, height), new Rect2(u0, 0, width * px, tex.Texture.GetSize().Y), new Color(1.05f, 1.1f, 1.08f));
            ci.DrawRect(new Rect2(0, height - 16, width, 12), Cel.Leaf.Darkened(0.35f) with { A = 0.55f });
            ci.DrawLine(new Vector2(0, height - 2), new Vector2(width, height - 2), Colors.White with { A = 0.7f }, 3);
            return;
        }

        // 条石：每层高 26，块长 90，逐层错缝；越近水线越暗，水线上一道苔带。
        var rows = (int)Mathf.Ceil(height / 26);
        for (var r = 0; r < rows; r++)
        {
            var y = r * 26f;
            var shift = Cel.Rand(r, 9) * 90;
            var tone = Cel.Stone.Darkened(0.06f * r);
            ci.DrawRect(new Rect2(0, y, width, 26), tone);
            ci.DrawLine(new Vector2(0, y + 1), new Vector2(width, y + 1), Cel.StoneLight with { A = 0.6f }, 2);
            for (var x = -Mathf.PosMod(offset + shift, 90); x < width; x += 90)
            {
                if (x > 0) ci.DrawLine(new Vector2(x, y), new Vector2(x, y + 26), Cel.Ink with { A = 0.5f }, 1.8f);
            }
        }

        ci.DrawRect(new Rect2(0, height - 16, width, 12), Cel.Leaf.Darkened(0.35f) with { A = 0.7f });
        ci.DrawLine(new Vector2(0, height - 2), new Vector2(width, height - 2), Colors.White with { A = 0.7f }, 3);
    }

    public override void _Draw()
    {
        foreach (var face in _faces)
        {
            face.Draw(this);
        }

        _deckArt?.Draw(this, Vector2.Zero);

        // 北岸压顶：街面边沿一道亮石与墨线。
        var coping = TownLayout.Edge(TownSamples.NorthBank, TownSamples.Bounds.Position.X - 3000, TownSamples.Bounds.End.X + 3000)
            .Select(p => TownView.P(p)).ToArray();
        DrawPolyline(coping, Cel.StoneLight, 8, true);
        DrawPolyline(coping, Cel.Ink, 2, true);
    }
}

/// <summary>小地图：平面图（上北），以主角为中心取 2400×2400 的窗口，画石板街、河、桥、屋舍；石青箭头为主角、泥金菱形为目标。</summary>
public partial class TownMiniMap : Control
{
    private const float Window = 2400;

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
        var b = TownSamples.Bounds;
        var origin = new Vector2(Mathf.Clamp(hero.X - Window / 2, b.Position.X, b.End.X - Window), Mathf.Clamp(hero.Y - Window / 2, b.Position.Y, b.End.Y - Window));
        DrawRect(new Rect2(Vector2.Zero, Size), Color.FromHtml("#6E9C74"));
        DrawSetTransform(-origin * k, 0, new Vector2(k, k));

        DrawColoredPolygon(TownLayout.Street, UiPalette.SurfaceShade);
        DrawRect(TownSamples.Alley, UiPalette.SurfaceShade);
        DrawRect(TownSamples.Plaza, UiPalette.SurfaceShade);
        DrawColoredPolygon(TownLayout.SouthWalk, UiPalette.SurfaceShade);
        DrawColoredPolygon(TownLayout.Canal, UiPalette.Trim.Lightened(0.15f));
        DrawRect(TownSamples.Bridge, UiPalette.Surface);
        foreach (var h in TownSamples.Houses)
        {
            DrawRect(new Rect2(h.X0, h.Y0, h.X1 - h.X0, h.Y1 - h.Y0), UiPalette.TextMuted with { A = 0.8f });
        }

        DrawRect(TownSamples.Corridor, UiPalette.TextMuted with { A = 0.45f });

        var g = TownSamples.Goal;
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
