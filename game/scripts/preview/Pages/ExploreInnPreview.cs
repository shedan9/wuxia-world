using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 客栈 / 室内探索展示页“江南客栈·大堂”（M0-03 第二类布景：人定布局）。
/// 与城镇同一个2:1 等距正交投影：镜头一侧的南墙、西墙剖切到齐腰，北墙与东墙是完整内墙面；
/// 方砖地、木构架粉壁、柜台货架、八仙桌长凳、屏风雅座、楼梯、吊灯光斑与门口天光都是程序化赛璐璐占位。
/// 行走、排序、遮挡淡出（屏风后的行人）与镜头由 <see cref="ExploreStage"/> 共用；
/// 门口按 E 回到芦湾河街客栈门前。掌柜乔红绡、店小二、茶客为静态剪影，不写台词。
/// 截图参数 <c>--tab</c>：0 进门、1 屏风后雅座（屏风淡出）、2 柜台前（交互提示）、3 楼梯口、4 缩到 0.85 看全堂。
/// </summary>
public partial class ExploreInnPreview : ExploreStage
{
    protected override Rect2 Bounds => InnSamples.Bounds;

    protected override IReadOnlyList<TownInteraction> Interactions => InnSamples.Interactions;

    protected override (string Region, string Name, string Time) PlaceInfo => ("芦湾", "江南客栈　大堂", "申时　·　雨后初晴");

    protected override (Vector2 Ground, float Height, string Label)? Goal => (InnSamples.Goal, 200, "雅座");

    protected override string Caption => "室内布局样板：镜头一侧的墙剖切到齐腰；柜台、楼梯、屏风、柱、桌、盆栽与方砖地为 AI 出件（方案 C），墙面、灯光与人物仍为程序化占位（M0-03）";

    protected override (Vector2 Hero, Vector2 Lu, float Zoom) Start(string? arrival)
    {
        var door = InnSamples.FrontDoor;
        if (arrival is not null)
        {
            return (door, door + new Vector2(-40, 110), 1f);
        }

        return DevCapture.Tab switch
        {
            1 => (new Vector2(940, 560), new Vector2(1010, 650), 1f),
            2 => (new Vector2(390, 280), new Vector2(520, 320), 1f),
            3 => (new Vector2(1225, 800), new Vector2(1110, 830), 1f),
            4 => (new Vector2(640, 600), new Vector2(560, 660), MinZoom),
            _ => (door, door + new Vector2(-40, 110), 1f),
        };
    }

    protected override void BuildScene()
    {
        GroundLayer.AddChild(new InnBackdrop());
        GroundLayer.AddChild(new InnShell());
        GroundLayer.AddChild(new InnFloorLight());

        foreach (var piece in InnCutWall.Build()) Add(piece);
        Add(new InnSill(), blocks: false);
        Add(new InnCounter());
        Add(new InnStairs());
        Add(new InnScreenNode());
        foreach (var (p, i) in InnSamples.Pillars.Select((p, i) => (p, i))) Add(new InnPillarNode(p, i));
        foreach (var table in InnSamples.Tables) Add(new InnTableNode(table));
        foreach (var (p, i) in InnSamples.Plants.Select((p, i) => (p, i))) Add(new InnPlantNode(p, i));
        Add(new TownPropNode(new TownProp("prop.inn.jars", PropKind.Jars, InnSamples.Jars, 6)));
        foreach (var person in InnSamples.People)
        {
            var (look, tone) = person.Figure switch
            {
                InnFigure.Keeper => (FigureLook.Keeper, Color.FromHtml("#B8544A")),
                InnFigure.Waiter => (FigureLook.Waiter, Color.FromHtml("#5E7C8A")),
                _ => (FigureLook.Seated, Color.FromHtml("#8A6A48")),
            };
            var figure = new WalkerFigure { Look = look, Tone = tone, Occluder = false, Facing = person.Facing };
            figure.Place(person.Position);
            Add(figure);
        }

        OverheadLayer.AddChild(new InnLanterns());
    }

    protected override bool InWalkArea(Vector2 q)
    {
        if (InnSamples.BehindCounter.HasPoint(q))
        {
            return false;
        }

        var inside = InnSamples.Room.Grow(-24);
        if (inside.HasPoint(q))
        {
            return true;
        }

        // 门洞：可走到门槛上。
        return q.X > InnSamples.DoorWest + 18 && q.X < InnSamples.DoorEast - 18 && q.Y >= inside.End.Y - 1 && q.Y < InnSamples.Room.End.Y + 14;
    }

    protected override Control CreateMiniMap(Func<(Vector2 Position, Vector2 Heading)> hero) => new InnMiniMap { Hero = hero };
}

/// <summary>墙外：四周暗场，门前一段雨后石板街向外渐隐。</summary>
public partial class InnBackdrop : Node2D
{
    private static readonly Rect2 Street = new(
        InnSamples.Room.Position.X - 400, InnSamples.Room.End.Y + InnSamples.WallThickness, InnSamples.Room.Size.X + 800, 360);

    public override void _Ready()
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/town_ground.gdshader") };
        material.SetShaderParameter("kind", 1);
        material.SetShaderParameter("yaw", Mathf.DegToRad(TownView.Yaw));
        material.SetShaderParameter("pitch", Mathf.DegToRad(TownView.Pitch));
        material.SetShaderParameter("scale", TownView.Scale);
        material.SetShaderParameter("plane_z", 0f);
        AddChild(new Polygon2D { Polygon = Corners(Street).Select(p => TownView.P(p)).ToArray(), Material = material });

        // 街面自门口向外渐隐进暗场：远离墙的一侧与东西两头都压暗。
        var fade = new Node2D();
        fade.Draw += () =>
        {
            var dark = InnTone.Background;
            var clear = dark with { A = 0 };
            var (x0, x1, y0, y1) = (Street.Position.X, Street.End.X, Street.Position.Y, Street.End.Y);
            fade.DrawPolygon([TownView.P(x0, y0), TownView.P(x1, y0), TownView.P(x1, y1), TownView.P(x0, y1)], [clear, clear, dark, dark]);
            fade.DrawPolygon([TownView.P(x0, y0), TownView.P(x0 + 450, y0), TownView.P(x0 + 450, y1), TownView.P(x0, y1)], [dark, clear, clear, dark]);
            fade.DrawPolygon([TownView.P(x1 - 450, y0), TownView.P(x1, y0), TownView.P(x1, y1), TownView.P(x1 - 450, y1)], [clear, dark, dark, clear]);
        };
        AddChild(fade);
    }

    public override void _Draw() => DrawColoredPolygon(Corners(InnSamples.Bounds.Grow(1600)).Select(p => TownView.P(p)).ToArray(), InnTone.Background);

    private static Vector2[] Corners(Rect2 r) => [r.Position, new(r.End.X, r.Position.Y), r.End, new(r.Position.X, r.End.Y)];
}

/// <summary>小地图：大堂平面（上北），整间房一屏；家具为暗块、门洞为缺口，石青箭头为主角、泥金菱形为雅座。</summary>
public partial class InnMiniMap : Control
{
    private const float Window = 1560;

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
        var origin = InnSamples.Room.GetCenter() - new Vector2(Window, Window) / 2;
        DrawRect(new Rect2(Vector2.Zero, Size), UiPalette.Abyss);
        DrawSetTransform(-origin * k, 0, new Vector2(k, k));

        var room = InnSamples.Room;
        DrawRect(room.Grow(InnSamples.WallThickness), UiPalette.TextMuted with { A = 0.8f });
        DrawRect(room, UiPalette.SurfaceShade);
        DrawRect(new Rect2(InnSamples.DoorWest, room.End.Y - 2, InnSamples.DoorEast - InnSamples.DoorWest, InnSamples.WallThickness + 4), UiPalette.SurfaceShade);
        var furniture = UiPalette.TextMuted with { A = 0.7f };
        DrawRect(InnSamples.Counter, furniture);
        DrawRect(InnSamples.Stairs, furniture);
        DrawRect(InnSamples.Screen.Grow(6), UiPalette.Trim);
        foreach (var t in InnSamples.Tables) DrawRect(new Rect2(t.Center - new Vector2(46, 46), new Vector2(92, 92)), furniture);
        foreach (var p in InnSamples.Pillars) DrawCircle(p, 22, furniture);

        var g = InnSamples.Goal;
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
