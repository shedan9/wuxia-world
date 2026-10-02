using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;
using WuxiaWorld.Game.Presentation.Art;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 山路 / 野外探索展示页“山门路·半山”（M0-03 第三类布景：人定布局）。
/// 与城镇、客栈同一个2:1 等距正交投影；山势做成三层台地（溪涧谷底、半山茶亭、上台山门），崖边在画面上层层升高，
/// 只能经凿进崖里的石阶上下，行人的高度随石阶连续变化。前景松竹压画框、中景台地、远景三层远山按镜头做视差，
/// 上台远处没入山雾；山道用土路色带与路碑、木牌标出走向，山门在画面外时屏幕边缘有方向箭头（目标提示预览）。
/// 植被、山石、茶亭、山门、路标、土路、草坡、崖壁、溪岸、溪床与桥面已按架构文档 10.3 方案 C 换成 AI 出件或纹理，石阶与桥栏为几何面贴 AI 纹理，溪水为着色器。
/// 截图参数 <c>--tab</c>：0 山脚入口、1 木桥上、2 茶亭下（屋顶淡出）、3 上台岔路木牌前（交互提示）、4 缩到 0.85 看山门与远山、5 石阶半途。
/// </summary>
public partial class ExploreWildPreview : ExploreStage
{
    /// <summary>
    /// 游戏借景的改装（M2）：<see cref="WildVariant.None"/> 为 M0 山路原样；河滩与旧渡去掉山门、茶亭、路碑与木牌，
    /// 河滩连木桥也去掉（剧情里芦湾从来只有渡船），并加渡船、刻痕石、锁船与水门等道具，限制可走范围。
    /// </summary>
    public WildVariant Variant { get; init; }

    private WildBackdrop _backdrop = null!;
    private WildMist _mist = null!;

    private static readonly SampleQuest MainQuest = new("quest.main.03.mountain_pass", "山门借道", "主线", "山门路",
        "样例：循乔红绡指的山门旧路上山，找沈槐留在抄写房的底账。",
        [
            ("循乔红绡指的山门旧路上山", StageState.Done),
            ("过山门，找到抄写房", StageState.Current),
            ("后续目标在推进后显示", StageState.Hidden),
        ],
        null, null);

    private static readonly SampleQuest SideQuest = new("quest.side.04.herb_debt", "药庐欠账", "支线", "山门路　药庐",
        "样例：东岔药庐缺药。",
        [("到东岔药庐问明缺哪些药", StageState.Current)],
        null, null);

    protected override string StepSurface => "dirt";

    protected override Rect2 Bounds
    {
        get
        {
            var box = new Rect2(WildSamples.W(0, WildSamples.FogEnd), Vector2.Zero);
            foreach (var p in new[] { WildSamples.W(3600, WildSamples.FogEnd), WildSamples.W(0, 1100), WildSamples.W(3600, 1100) }) box = box.Expand(p);
            return box;
        }
    }

    /// <summary>画面水平 0–3600；上到远山山顶、下到谷底前沿的前景树。</summary>
    protected override Rect2? CameraArea => Variant == WildVariant.None ? new(0, -1640, 3600, 2220) : new(0, -700, 3600, 1280);

    protected override IReadOnlyList<TownInteraction> Interactions => WildSamples.Interactions;

    protected override (string Region, string Name, string Time) PlaceInfo => ("山门", "山门路　半山", "辰时　·　山雾初散");

    protected override string Caption => "山路布局样板：树石灌丛、茶亭、山门、碑牌、土路、草坡、崖壁、溪岸、溪床与桥面为 AI 出件，石阶与桥栏为几何贴 AI 纹理；人物为 AI 全身样稿、行走暂以起伏示意（M0-03 / M0-04）";

    protected override (Vector2 Ground, float Height, string Label)? Goal => (WildSamples.Goal, 360, "山门");

    protected override Control Tracker() => ExploreHudKit.Tracker(MainQuest, SideQuest);

    protected override (Vector2 Hero, Vector2 Lu, float Zoom) Start(string? arrival)
    {
        var (hero, lu, zoom) = DevCapture.Tab switch
        {
            1 => (new Vector2(890, 650), new Vector2(890, 790), 1f),
            2 => (new Vector2(2080, -330), new Vector2(1990, -120), 1f),
            3 => (new Vector2(2870, -790), new Vector2(2830, -660), 1f),
            4 => (new Vector2(2640, -900), new Vector2(2740, -820), MinZoom),
            5 => (new Vector2(1390, 120), new Vector2(1390, 250), 1f),
            _ => (new Vector2(420, 860), new Vector2(300, 900), 1f),
        };
        return (WildSamples.W(hero), WildSamples.W(lu), zoom);
    }

    protected override void BuildScene()
    {
        _backdrop = new WildBackdrop();
        GroundLayer.AddChild(_backdrop);

        // 由低到高、由近到远：溪面 → 北岸 → 谷底 → 桥 → 崖 1 → 石阶 1 → 半山 → 崖 2 → 石阶 2 → 上台 → 山雾。
        Ground(2, WildLayout.Band(WildSamples.StreamNorth, WildSamples.StreamSouth), WildSamples.WaterZ, flow: 45);
        GroundLayer.AddChild(new WildWall { Edge = WildSamples.StreamNorth, Z0 = WildSamples.WaterZ, Z1 = WildSamples.Z0, Bank = true, Seed = 3 });
        Ground(5, WildLayout.Band(WildSamples.Edge1, WildSamples.StreamNorth), WildSamples.Z0);
        GroundLayer.AddChild(new WildWall { Edge = WildSamples.StreamNorth, Z0 = WildSamples.WaterZ, Z1 = WildSamples.Z0, Bank = true, LipOnly = true, Seed = 3 });
        Ground(5, WildLayout.Band(WildSamples.StreamSouth, _ => 2400), WildSamples.Z0);
        GroundLayer.AddChild(new WildShore());
        Path(WildSamples.PathSouth, WildSamples.Z0, 1);
        Path(WildSamples.PathNorth, WildSamples.Z0, 2);
        if (Variant != WildVariant.Shore)
        {
            GroundLayer.AddChild(new WildBridgeNode());
        }


        var s1 = WildSamples.Steps1;
        GroundLayer.AddChild(new WildWall { Edge = WildSamples.Edge1, Z0 = WildSamples.Z0, Z1 = WildSamples.Z1, Gaps = [(s1.A0, s1.A1)], Seed = 11 });
        GroundLayer.AddChild(new WildStepsNode(s1, "wild.steps.1"));
        Ground(5, WildLayout.Band(WildSamples.Edge2, WildSamples.Edge1, s1), WildSamples.Z1);
        GroundLayer.AddChild(new WildWall { Edge = WildSamples.Edge1, Z0 = WildSamples.Z0, Z1 = WildSamples.Z1, Gaps = [(s1.A0, s1.A1)], LipOnly = true, Seed = 11 });
        Path(WildSamples.PathMid, WildSamples.Z1, 3);
        GroundLayer.AddChild(new WildFootprints());

        var s2 = WildSamples.Steps2;
        GroundLayer.AddChild(new WildWall { Edge = WildSamples.Edge2, Z0 = WildSamples.Z1, Z1 = WildSamples.Z2, Gaps = [(s2.A0, s2.A1)], Seed = 23 });
        GroundLayer.AddChild(new WildStepsNode(s2, "wild.steps.2"));
        Ground(5, WildLayout.Band(_ => WildSamples.FogEnd, WildSamples.Edge2, s2), WildSamples.Z2);
        GroundLayer.AddChild(new WildWall { Edge = WildSamples.Edge2, Z0 = WildSamples.Z1, Z1 = WildSamples.Z2, Gaps = [(s2.A0, s2.A1)], LipOnly = true, Seed = 23 });
        Path(WildSamples.PathTop, WildSamples.Z2, 4);
        Path(WildSamples.PathFork, WildSamples.Z2, 5, 110);
        GroundLayer.AddChild(new WildFog());

        foreach (var tree in WildSamples.Trees) Add(new WildTreeNode(tree));
        foreach (var rock in WildSamples.Rocks) Add(new WildRockNode(rock));
        foreach (var shrub in WildSamples.Shrubs) Add(new WildShrubNode(shrub), blocks: false);
        if (Variant == WildVariant.None)
        {
            foreach (var part in WildPavilionPart.Build()) Add(part);
            foreach (var part in WildGatePart.Build()) Add(part);
            Add(WildMarkerNode.Stele());
            Add(WildMarkerNode.Signpost());
        }
        else
        {
            AddRiverProps();
        }

        _mist = new WildMist();
        OverheadLayer.AddChild(_mist);
    }

    /// <summary>一块地面：画面坐标多边形换成世界坐标，在高度 z 的水平面上由 town_ground 着色器铺纹样。</summary>
    private void Ground(int kind, IEnumerable<Vector2> frame, float z, float flow = 0)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/town_ground.gdshader") };
        material.SetShaderParameter("kind", kind);
        material.SetShaderParameter("yaw", Mathf.DegToRad(TownView.Yaw));
        material.SetShaderParameter("pitch", Mathf.DegToRad(TownView.Pitch));
        material.SetShaderParameter("scale", TownView.Scale);
        material.SetShaderParameter("plane_z", z);
        material.SetShaderParameter("flow", Mathf.DegToRad(flow));
        if (kind == 4)
        {
            PieceArt.ApplyGround(material, "wild.ground.dirt", GroundTint.Dirt, 0.2f, 0.1f);
        }
        else if (kind == 5)
        {
            PieceArt.ApplyGround(material, "wild.ground.meadow", GroundTint.Meadow, 0.3f, 0.1f, 0.5f);
        }
        else if (kind == 2)
        {
            // 溪水：AI 溪床纹理作水底，着色器按两岸参数算离岸远近（浅水、深水与近岸白沫）。
            PieceArt.ApplyGround(material, "wild.ground.streambed", Vector3.One, 0.15f, 0.12f, 0.8f);
            material.SetShaderParameter("stream_n", WildSamples.StreamN);
            material.SetShaderParameter("stream_s", WildSamples.StreamS);
        }

        GroundLayer.AddChild(new Polygon2D { Polygon = frame.Select(p => TownView.P(WildSamples.W(p), z)).ToArray(), Material = material });
    }

    private void Path(Vector2[] line, float z, int seed, float width = WildSamples.PathWidth) =>
        Ground(4, WildLayout.Ribbon(line, width, seed), z);

    public override bool InWalkArea(Vector2 q)
    {
        if (!WildLayout.InWalkArea(q))
        {
            return false;
        }

        var f = WildSamples.Frame(q);
        return Variant switch
        {
            // 河滩：只在南岸（没有桥，过不了溪）。
            WildVariant.Shore => f.Y > WildSamples.StreamSouth(f.X) + 4,

            // 旧渡：谷底、木桥与半山，不上通往山门的第二道石阶。
            WildVariant.OldFerry => f.Y > WildSamples.Edge2(f.X) + 6,
            _ => true,
        };
    }

    /// <summary>河岸道具：船停在溪里随水轻晃，岸边刻痕石、锁着的大船与水门闸架。</summary>
    private void AddRiverProps()
    {
        var water = WildSamples.WaterZ;
        if (Variant == WildVariant.Shore)
        {
            // 陆青禾的渡船泊在醒来处上游；刻痕青石在调查点旁。
            _boats.Add(AddPiece(new RelocatedPiece("town.prop.boat.ferry", new Vector3(1640, 2080, -80), Lift(WildSamples.W(1240, 610), water), new Vector2(300, 110), occluder: false), blocks: false));
            AddPiece(new RelocatedPiece("town.prop.stone_mark", new Vector3(1180, 1790, 0), Lift(WildSamples.W(2110, 720), 0), new Vector2(110, 60)));
        }
        else
        {
            _boats.Add(AddPiece(new RelocatedPiece("town.prop.boat.ferry", new Vector3(1640, 2080, -80), Lift(WildSamples.W(540, 615), water), new Vector2(300, 110), occluder: false), blocks: false));
            _boats.Add(AddPiece(new RelocatedPiece("town.prop.boat.2", new Vector3(3900, 2450, -80), Lift(WildSamples.W(1760, 620), water), new Vector2(300, 110), occluder: false), blocks: false));
            AddPiece(new TexturePiece("res://assets/art/battle/battle.sluice_gate.png", WildSamples.W(1120, 330), 0, 330, new Vector2(150, 70)));
        }
    }

    private static Vector3 Lift(Vector2 ground, float z) => new(ground.X, ground.Y, z);

    private readonly List<TownPiece> _boats = [];

    private TownPiece AddPiece(TownPiece piece, bool blocks = true)
    {
        Add(piece, blocks);
        return piece;
    }

    protected override float StepZ(Vector2 p) => WildLayout.GroundZ(p);

    protected override Control CreateMiniMap(Func<(Vector2 Position, Vector2 Heading)> hero, Func<Vector2?> goal) => new WildMiniMap { Hero = hero, Goal = goal };

    protected override void Animate(float seconds)
    {
        _backdrop.Scroll(CameraCenter);
        for (var i = 0; i < _boats.Count; i++)
        {
            if (_boats[i] is RelocatedPiece boat)
            {
                boat.Bob(Motion.Enabled ? Mathf.Sin(seconds * 1.3f + i * 1.7f) * 2.5f : 0);
            }
        }

        if (Motion.Enabled)
        {
            _mist.Seconds = seconds;
            _mist.QueueRedraw();
        }
    }
}

/// <summary>山路布景在游戏里的改装方式（见 <see cref="ExploreWildPreview.Variant"/>）。</summary>
public enum WildVariant
{
    None,
    Shore,
    OldFerry,
}

/// <summary>
/// 挪到别处的 AI 出件：出件按原布景的投影坐标对齐，这里整体平移到新位置（含高度差），占地按新位置重算。
/// 用于把城镇的船、刻痕石借到河岸改装里。
/// </summary>
public partial class RelocatedPiece : TownPiece
{
    private readonly Vector2 _offset;

    public RelocatedPiece(string artId, Vector3 authored, Vector3 placed, Vector2 foot, bool occluder = true)
    {
        Occluder = occluder;
        UseArt(artId);
        _offset = TownView.P(placed) - TownView.P(authored);
        Position = _offset;
        Foot = new Rect2(new Vector2(placed.X, placed.Y) - foot / 2, foot);
        ScreenBox = Art is { } art ? new Rect2(art.Frame.Position + _offset, art.Frame.Size) : new Rect2(TownView.P(placed) - new Vector2(60, 120), new Vector2(120, 120));
    }

    /// <summary>船随水轻晃：屏幕纵向偏移。</summary>
    public void Bob(float dy) => Position = _offset + new Vector2(0, dy);
}

/// <summary>立着的一张贴图（脚底中点对齐地面，按世界高度缩放），用于把战斗道具图借到探索里（水门闸架）。</summary>
public partial class TexturePiece : TownPiece
{
    private readonly Texture2D? _texture;
    private readonly Vector2 _size;

    public TexturePiece(string path, Vector2 ground, float z, float height, Vector2 foot)
    {
        _texture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        var h = height * TownView.Upright;
        var aspect = _texture is null ? 0.6f : _texture.GetWidth() / (float)_texture.GetHeight();
        _size = new Vector2(h * aspect, h);
        Position = TownView.P(ground, z);
        Foot = new Rect2(ground - foot / 2, foot);
        ScreenBox = new Rect2(Position - new Vector2(_size.X / 2, _size.Y), _size);
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    public override void _Draw()
    {
        if (_texture is not null)
        {
            DrawTextureRect(_texture, new Rect2(new Vector2(-_size.X / 2, -_size.Y), _size), false);
        }
    }
}
