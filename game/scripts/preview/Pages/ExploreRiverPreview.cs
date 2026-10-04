using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Art;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 河岸布景（M3-01 专属地图，取代 M2 借用的山路溪涧）：芦湾河滩与芦湾旧渡。与城镇、山路同一个 2:1 等距正交投影，
/// 布局沿画面水平展开（同山路的画面坐标）：远处宽阔的江面没入水汽、远岸低丘，近处雨后泥沙滩接草坡，岸边成片芦苇。
/// 河滩有陆青禾的渡船与缆桩、半没在水里的刻痕青石、钻进苇根的拖船潮痕；旧渡有伸进江里的废栈桥、被铁链锁在浅滩的大船、
/// 石砌河堤上的水门（闸墩、闸桥、半开的闸板与绞架，门内引水渠翻白）与贴堤石阶。光色：河滩雨后偏青、旧渡黄昏偏暖。
/// 截图参数 <c>--tab</c>：0 醒来处、1 刻痕石、2 潮痕、3 河滩缩远、4 渡船与缆桩；5 旧渡栈桥、6 锁船、7 堤前石阶、8 闸桥上、9 旧渡缩远。
/// </summary>
public partial class ExploreRiverPreview : ExploreStage
{
    /// <summary>游戏模式下由地图摆放给出；展示页按 tab 选（5 以上为旧渡）。</summary>
    public RiverSite Site { get; init; }

    private RiverSite Current => Driver is null && DevCapture.Tab >= 5 ? RiverSite.OldFerry : Site;

    private RiverSamples S => RiverSamples.For(Current);

    private RiverGeo? _geo;

    private RiverGeo Geo => _geo ??= new RiverGeo(S);

    private bool Dusk => Current == RiverSite.OldFerry;

    private RiverBackdrop _backdrop = null!;
    private RiverMist _mist = null!;
    private readonly List<RiverArtNode> _boats = [];

    protected override string StepSurface => "dirt";

    protected override Rect2 Bounds
    {
        get
        {
            var s = S;
            var box = new Rect2(RiverSamples.W(s.ViewA0, RiverSamples.WaterFar), Vector2.Zero);
            foreach (var p in new[] { RiverSamples.W(s.ViewA1, RiverSamples.WaterFar), RiverSamples.W(s.ViewA0, s.Front + 200), RiverSamples.W(s.ViewA1, s.Front + 200) }) box = box.Expand(p);
            return box;
        }
    }

    /// <summary>画面水平取本图范围；上到远岸低丘、下到前景芦苇。</summary>
    protected override Rect2? CameraArea
    {
        get
        {
            var top = RiverBackdrop.Horizon - 300;
            var bottom = RiverGeo.S(0, S.Front + 160, 0).Y;
            return new Rect2(S.ViewA0, top, S.ViewA1 - S.ViewA0, bottom - top);
        }
    }

    protected override IReadOnlyList<TownInteraction> Interactions => Current == RiverSite.Shore
        ?
        [
            new("interact.river.stone", RiverSamples.W(2050, 40), 130, "查看", "旧渡石痕", "见闻", "已记录：亲见的旧渡石痕（样例）", "札记 → 见闻"),
            new("interact.river.tide", RiverSamples.W(2760, 110), 60, "查看", "拖船潮痕", "线索", "已记录：一道往下游去的拖船泥痕（样例）", "札记 → 线索"),
        ]
        :
        [
            new("interact.river.boat", RiverSamples.W(1460, 110), 180, "查看", "上锁的渡船", "线索", "铁链锁着船头（样例）", "第一章 江南会客"),
            new("interact.river.gate", RiverSamples.W(2760, 70), 220, "查看", "水门", "提示", "闸板半开（样例）", "第一章 江南会客"),
        ];

    protected override (string Region, string Name, string Time) PlaceInfo => Current == RiverSite.Shore
        ? ("芦湾", "芦湾河滩", "申时　·　雨后初晴")
        : ("芦湾", "芦湾旧渡", "酉时　·　日落");

    protected override string Caption => "河岸专属布景（M3-01）：江面、泥滩、河堤水门与栈桥为着色器与几何贴 AI 纹理，芦苇为 AI 精灵，柳树、山石与船借城镇 / 山路出件";

    protected override (Vector2 Ground, float Height, string Label)? Goal => Current == RiverSite.Shore
        ? (RiverSamples.W(2050, 40), 130, "旧渡石痕")
        : (RiverSamples.W(2760, 70), 320, "水门");

    protected override (Vector2 Hero, Vector2 Lu, float Zoom) Start(string? arrival)
    {
        var (hero, lu, zoom) = DevCapture.Tab switch
        {
            1 => (new Vector2(1960, 110), new Vector2(1840, 150), 1f),
            2 => (new Vector2(2700, 190), new Vector2(2580, 220), 1f),
            3 => (new Vector2(1800, 420), new Vector2(1680, 450), MinZoom),
            4 => (new Vector2(1180, 230), new Vector2(1060, 260), 1f),
            5 => (new Vector2(760, 60), new Vector2(660, 124), 1f),
            6 => (new Vector2(1450, 240), new Vector2(1330, 260), 1f),
            7 => (new Vector2(2330, 500), new Vector2(2210, 520), 1f),
            8 => (new Vector2(2760, 80), new Vector2(2580, 60), 1f),
            9 => (new Vector2(1900, 420), new Vector2(1780, 450), MinZoom),
            _ => (new Vector2(1000, 300), new Vector2(880, 330), 1f),
        };
        return (RiverSamples.W(hero.X, hero.Y), RiverSamples.W(lu.X, lu.Y), zoom);
    }

    protected override void BuildScene()
    {
        var s = S;
        _backdrop = new RiverBackdrop(Dusk);
        GroundLayer.AddChild(_backdrop);

        // 江面（含旧渡引水渠）→ 泥滩与草坡 → 小路 → 潮痕 → 草丛 → 栈桥 → 堤顶 → 堤立面 → 石阶。
        var a1 = WildLayout.FarA1;
        Ground(6, WildLayout.Band(_ => RiverSamples.WaterFar, a => s.Shore.At(a) + 60), RiverSamples.WaterZ);
        Ground(7, WildLayout.Band(s.Shore.At, _ => s.Front + 900), 0);
        if (s.Path.Length > 1)
        {
            Ground(4, WildLayout.Ribbon(s.Path, 120, 7), 0);
        }

        if (s.Levee is { } l)
        {
            Ground(6, [new(l.GateA0, l.Front - 2), new(l.GateA1, l.Front - 2), new(l.GateA1, s.Front + 900), new(l.GateA0, s.Front + 900)], l.ChannelZ);
            GroundLayer.AddChild(new RiverChannelBanks(l, s.Front + 900));
        }

        if (s.TideMark.Length > 1)
        {
            GroundLayer.AddChild(new RiverTideMark(s.TideMark));
        }

        GroundLayer.AddChild(new RiverTufts(s));
        if (s.Jetty is { } j)
        {
            GroundLayer.AddChild(new RiverJettyNode(j));
        }

        if (s.Levee is { } levee)
        {
            Ground(1, [new(levee.A0, levee.Back), new(a1, levee.Back), new(a1, levee.Front), new(levee.A0, levee.Front)], levee.Z);
            GroundLayer.AddChild(new RiverLeveeNode(levee, s.ViewA1));
            GroundLayer.AddChild(new WildStepsNode(levee.Steps, "river.steps"));
            foreach (var part in RiverGatePart.Build(levee)) Add(part, blocks: false);
        }

        foreach (var bed in s.Reeds)
        {
            foreach (var (ad, h, art, flip) in RiverGeo.Clumps(bed))
            {
                var z = bed.Wet ? RiverSamples.WaterZ : Geo.GroundZ(RiverSamples.W(ad.X, ad.Y));
                Add(new RiverReedNode(ad, z, h, art, flip, bed.Wet), blocks: false);
            }
        }

        foreach (var prop in s.Props)
        {
            var ground = RiverSamples.W(prop.A, prop.D);
            var tree = prop.Art.StartsWith("town.tree.", StringComparison.Ordinal);
            Add(new RiverArtNode(prop.Art, RiverArtNode.Anchor(prop.Art), ground, 0, prop.Size, prop.Flip,
                tree ? new Vector2(40, 40) : new Vector2(70, 70) * prop.Size, tree ? new Vector2(230, 170) * prop.Size : new Vector2(60, 44) * prop.Size));
        }

        foreach (var boat in s.Boats)
        {
            var node = new RiverArtNode(boat.Art, RiverArtNode.Anchor(boat.Art), RiverSamples.W(boat.A, boat.D), RiverSamples.WaterZ, boat.Scale, boat.Flip,
                new Vector2(260, 100) * boat.Scale, new Vector2(150, 50) * boat.Scale, inWater: true) { Occluder = false };
            Add(node, blocks: false);
            _boats.Add(node);
        }

        if (s.StoneMark is { } mark)
        {
            Add(new RiverArtNode("town.prop.stone_mark", RiverArtNode.Anchor("town.prop.stone_mark"), RiverSamples.W(mark.X, mark.Y), -4, 1f, false,
                new Vector2(100, 60), new Vector2(70, 40), inWater: true), blocks: false);
        }

        if (s.Mooring is { } moor)
        {
            Add(new RiverStakeNode(moor.Stake, moor.Bow, chain: false), blocks: false);
        }

        if (s.Chain is { } chain)
        {
            Add(new RiverStakeNode(chain.Stake, chain.Bow, chain: true), blocks: false);
        }

        _mist = new RiverMist(Dusk);
        OverheadLayer.AddChild(_mist);
        var light = new RiverLight(Dusk) { Area = CameraArea!.Value };
        OverheadLayer.AddChild(light);
        // 整体色调：雨后微冷、黄昏压暖（交互菱形在同一层之上，略受影响）。
        GroundLayer.GetParent<Node2D>().Modulate = Dusk ? new Color(1f, 0.9f, 0.8f) : new Color(0.97f, 1f, 1f);
    }

    /// <summary>一块地面：画面坐标多边形换成世界坐标，在高度 z 的水平面上由 town_ground 着色器铺纹样。</summary>
    private void Ground(int kind, IEnumerable<Vector2> frame, float z)
    {
        var s = S;
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/town_ground.gdshader") };
        material.SetShaderParameter("kind", kind);
        material.SetShaderParameter("yaw", Mathf.DegToRad(TownView.Yaw));
        material.SetShaderParameter("pitch", Mathf.DegToRad(TownView.Pitch));
        material.SetShaderParameter("scale", TownView.Scale);
        material.SetShaderParameter("plane_z", z);
        switch (kind)
        {
            case 1:
                PieceArt.ApplyGround(material, "town.ground.flagstone", new Vector3(1.08f, 1.14f, 1.12f), macro: 0.08f);
                break;
            case 4:
                PieceArt.ApplyGround(material, "wild.ground.dirt", GroundTint.Dirt, 0.2f, 0.1f);
                break;
            case 6 or 7:
                material.SetShaderParameter("shore_a", s.Shore.First);
                material.SetShaderParameter("shore_b", s.Shore.Second);
                material.SetShaderParameter("beach_a", s.Beach.First);
                material.SetShaderParameter("beach_b", s.Beach.Second);
                material.SetShaderParameter("water_far", RiverSamples.WaterFar);
                material.SetShaderParameter("mist_color", Dusk ? new Vector3(0.90f, 0.85f, 0.77f) : new Vector3(0.86f, 0.90f, 0.88f));
                material.SetShaderParameter("sky_tint", Dusk ? new Vector3(0.92f, 0.80f, 0.64f) : new Vector3(0.72f, 0.85f, 0.85f));
                PieceArt.ApplyGround(material, "wild.ground.streambed", kind == 6 ? Vector3.One : new Vector3(1.04f, 1.02f, 0.96f), 0.15f, 0.12f, 0.8f);
                if (kind == 7 && PieceArt.FindTexture("wild.ground.meadow") is { } meadow)
                {
                    material.SetShaderParameter("use_meadow", true);
                    material.SetShaderParameter("meadow_tex", meadow.Texture);
                    material.SetShaderParameter("meadow_world", meadow.WorldSize);
                    material.SetShaderParameter("meadow_tint", GroundTint.Meadow);
                }

                if (s.Levee is { } l)
                {
                    material.SetShaderParameter("channel_a", new Vector2(l.GateA0, l.GateA1));
                    material.SetShaderParameter("channel_d", l.Front - 2);
                }

                var objects = WaterObjects().ToArray();
                material.SetShaderParameter("river_obj", Enumerable.Range(0, 8).Select(i => i < objects.Length ? objects[i].Box : Vector4.Zero).ToArray());
                material.SetShaderParameter("river_obj_shape", Enumerable.Range(0, 8).Select(i => i < objects.Length ? objects[i].Shape : Vector2.Zero).ToArray());
                break;
        }

        GroundLayer.AddChild(new Polygon2D { Polygon = frame.Select(p => TownView.P(RiverSamples.W(p.X, p.Y), z)).ToArray(), Material = material });
    }

    /// <summary>水中物件（船、刻痕石、栈桥）在着色器里的脚影与倒影：世界坐标中心、半宽半长，圆角与露出水面的高度。</summary>
    private IEnumerable<(Vector4 Box, Vector2 Shape)> WaterObjects()
    {
        var s = S;
        foreach (var b in s.Boats)
        {
            var c = RiverSamples.W(b.A, b.D);
            var half = new Vector2(150, 46) * b.Scale;
            if (b.Flip) half = new Vector2(half.Y, half.X);
            yield return (new Vector4(c.X, c.Y, half.X, half.Y), new Vector2(42, 60));
        }

        if (s.StoneMark is { } m)
        {
            var c = RiverSamples.W(m.X, m.Y);
            yield return (new Vector4(c.X, c.Y, 40, 40), new Vector2(30, 40));
        }

        if (s.Jetty is { } j)
        {
            var c = j.Deck.GetCenter();
            yield return (new Vector4(c.X, c.Y, j.Deck.Size.X / 2, j.Deck.Size.Y / 2), new Vector2(10, 26));
        }
    }

    public override bool InWalkArea(Vector2 q) => Geo.InWalkArea(q);

    protected override float StepZ(Vector2 p) => Geo.GroundZ(p);

    protected override Control CreateMiniMap(Func<(Vector2 Position, Vector2 Heading)> hero, Func<Vector2?> goal) =>
        new RiverMiniMap { Site = S, Hero = hero, Goal = goal };

    protected override void Animate(float seconds)
    {
        _backdrop.Scroll(CameraCenter);
        for (var i = 0; i < _boats.Count; i++)
        {
            _boats[i].Bob(Motion.Enabled ? Mathf.Sin(seconds * 1.2f + i * 1.9f) * 2.5f : 0);
        }

        if (Motion.Enabled)
        {
            _mist.Seconds = seconds;
            _mist.QueueRedraw();
        }
    }
}

/// <summary>引水渠两岸：渠面比地面低，两岸的石砌边沿各勾一道条石压边（侧壁正对视线看不见）。</summary>
public partial class RiverChannelBanks : Node2D
{
    private readonly Levee _l;
    private readonly float _d1;

    public RiverChannelBanks(Levee l, float d1)
    {
        _l = l;
        _d1 = d1;
    }

    public override void _Draw()
    {
        var l = _l;
        foreach (var (a, dir) in new[] { (l.GateA0, -1f), (l.GateA1, 1f) })
        {
            // 岸沿条石：一条窄的石面带，外侧勾线；渠面一侧压一道暗。
            Vector2[] kerb = [RiverGeo.S(a, l.Front, 0), RiverGeo.S(a + dir * 26, l.Front, 0), RiverGeo.S(a + dir * 26, _d1, 0), RiverGeo.S(a, _d1, 0)];
            DrawColoredPolygon(kerb, Cel.Stone);
            for (var d = l.Front + 70; d < _d1; d += 70)
            {
                DrawLine(RiverGeo.S(a, d, 0), RiverGeo.S(a + dir * 26, d, 0), Cel.Ink with { A = 0.4f }, 1.4f, true);
            }

            DrawLine(RiverGeo.S(a, l.Front, 0), RiverGeo.S(a, _d1, 0), Cel.Ink, 2.2f, true);
            DrawLine(RiverGeo.S(a + dir * 26, l.Front, 0), RiverGeo.S(a + dir * 26, _d1, 0), Cel.Ink with { A = 0.5f }, 1.6f, true);
            // 渠壁：自岸沿垂到渠面（只看得见一窄条）。
            DrawColoredPolygon([RiverGeo.S(a, l.Front, 0), RiverGeo.S(a, _d1, 0), RiverGeo.S(a, _d1, l.ChannelZ), RiverGeo.S(a, l.Front, l.ChannelZ)], Cel.Stone.Darkened(0.35f));
        }
    }
}
