using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 江南客栈后院（2026-10-05）：第一章讨教与切磋的场地，此前这两段戏借大堂演。布局见 <see cref="YardSamples"/>。
/// 北面客房楼与东院墙是 AI 整面立面（未入库时画程序化占位），柴堆、劈柴墩、石桌为 AI 出件，井台、水缸、樟树与盆栽借城镇和大堂的出件；
/// 南面大堂后墙沿用大堂的剖切墙，西面是一道盖瓦的矮院墙。行走、排序、遮挡淡出与镜头由 <see cref="ExploreStage"/> 共用。
/// 后院的戏都在夜里（讨教“后院，夜”、切磋“后半夜”），规则时辰并不推进（见架构文档 9.4.12），本页固定按月夜布光：
/// 整体压暗偏蓝，客房楼两盏灯笼与两扇亮窗、后厨门洞里大堂的灯光铺到地上，四角压暗。
/// 展示页（不进目录）截图参数 <c>--tab</c>：0 后厨门口、1 井台边、2 石桌旁、3 缩到 0.85 看全院。
/// </summary>
public partial class ExploreYardPreview : ExploreStage
{
    /// <summary>月夜调色：比城镇夜色略亮一些，好看清院里的人。</summary>
    private static readonly Color NightTint = new(0.46f, 0.52f, 0.7f);

    protected override string StepSurface => "stone";

    protected override Rect2 Bounds => YardSamples.Bounds;

    /// <summary>镜头可往上多看一段，露出二层的栏廊与瓦檐。</summary>
    protected override Rect2? CameraArea
    {
        get
        {
            var b = Bounds;
            var box = new Rect2(TownView.P(b.Position), Vector2.Zero);
            foreach (var c in new[] { TownView.P(b.End), TownView.P(b.Position.X, b.End.Y), TownView.P(b.End.X, b.Position.Y) }) box = box.Expand(c);
            return box.GrowSide(Side.Top, 260);
        }
    }

    protected override IReadOnlyList<TownInteraction> Interactions => YardSamples.Interactions;

    protected override (string Region, string Name, string Time) PlaceInfo => ("芦湾", "江南客栈　后院", "子时　·　月夜");

    protected override string Caption => "后院：客房楼与东院墙为 AI 整面立面，柴堆、劈柴墩、石桌为 AI 出件，井台、水缸、樟树与盆栽借城镇和大堂出件；固定月夜布光";

    protected override (Vector2 Hero, Vector2 Lu, float Zoom) Start(string? arrival)
    {
        var door = YardSamples.BackDoor;
        if (arrival is not null)
        {
            return (door, door + new Vector2(-110, 0), 1f);
        }

        return DevCapture.Tab switch
        {
            1 => (new Vector2(560, 560), new Vector2(460, 600), 1f),
            2 => (new Vector2(900, 520), new Vector2(820, 470), 1f),
            3 => (new Vector2(700, 560), new Vector2(600, 600), MinZoom),
            _ => (door, door + new Vector2(-110, 0), 1f),
        };
    }

    protected override void BuildScene()
    {
        var tint = NightTint;
        GroundLayer.AddChild(new YardGround());
        GroundLayer.AddChild(new YardShell());
        var shadows = new SceneShadows(0.2f);
        GroundLayer.AddChild(shadows);
        GroundLayer.AddChild(new YardLight(tint));

        // 灯笼挂在客房楼檐下、紧贴北墙：院里的一切都在它前面，放在贴地层。
        GroundLayer.AddChild(new YardLanterns(tint));

        // 南面大堂后墙：剖切到齐腰，后厨门洞两侧立门柱残段。
        var (w, d, t, h) = (YardSamples.Yard.Size.X, YardSamples.Yard.Size.Y, YardSamples.WallThickness, YardSamples.CutHeight);
        Add(new InnCutWall(new Vector3(-t, d, 0), new Vector3(YardSamples.DoorWest, d + t, h)));
        Add(new InnCutWall(new Vector3(YardSamples.DoorEast, d, 0), new Vector3(w + t, d + t, h)));
        foreach (var x in new[] { YardSamples.DoorWest, YardSamples.DoorEast })
        {
            Add(new InnCutWall(new Vector3(x - 15, d - 6, 0), new Vector3(x + 15, d + t + 6, h + 10), post: true));
        }

        Add(new YardSill(), blocks: false);
        Add(new YardLowWall(new Vector3(-t, -t, 0), new Vector3(0, d, YardSamples.LowWallHeight)));

        Add(new YardWoodpile());
        Add(new YardStump());
        Add(new YardStoneTable());
        Add(Borrowed("town.prop.well", YardSamples.Well, 1f, new Vector2(132, 128), Vector2.Zero));
        foreach (var (j, i) in YardSamples.Jars.Select((j, i) => (j, i)))
        {
            Add(Borrowed("town.prop.jars", j, 0.85f, new Vector2(110, 60), new Vector2(80, 30), flip: i == 1));
        }

        Add(Borrowed("town.tree.9", YardSamples.Tree, 0.72f, new Vector2(40, 40), new Vector2(170, 125)));
        foreach (var (p, i) in YardSamples.Plants.Select((p, i) => (p, i)))
        {
            Add(Borrowed($"inn.plant.{i + 1}", p, 1f, new Vector2(56, 56), new Vector2(36, 30)));
        }

        foreach (var piece in Pieces.Where(p => !p.Walker && p is not YardSill))
        {
            shadows.Cast(piece.ShadowFaces, new Vector2(0.18f, -0.3f));
        }

        TintWorld(tint);
        var wash = Color.FromHtml("#0A0E1A") with { A = 0.2f };
        OverheadLayer.AddChild(new SceneWash(wash with { A = 0.3f }, wash with { A = 0.18f }, wash with { A = 0.32f }, wash with { A = 0.26f }) { Area = ViewArea });
    }

    private static RiverArtNode Borrowed(string art, Vector2 at, float scale, Vector2 foot, Vector2 shadow, bool flip = false) =>
        new(art, RiverArtNode.Anchor(art), at, 0, scale, flip, foot, shadow);

    public override bool InWalkArea(Vector2 q)
    {
        var inside = YardSamples.Yard.Grow(-24);
        if (inside.HasPoint(q))
        {
            return true;
        }

        // 后厨门洞：可走到门槛上。
        return q.X > YardSamples.DoorWest + 18 && q.X < YardSamples.DoorEast - 18 && q.Y >= inside.End.Y - 1 && q.Y < YardSamples.Yard.End.Y + 14;
    }

    protected override Control CreateMiniMap(Func<(Vector2 Position, Vector2 Heading)> hero, Func<Vector2?> goal) => new YardMiniMap { Hero = hero, Goal = goal };
}
