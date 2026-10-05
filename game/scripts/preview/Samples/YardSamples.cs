using Godot;

namespace WuxiaWorld.Game.Preview.Samples;

/// <summary>
/// 江南客栈后院的布局（2026-10-05，第一章讨教与切磋的场地）。与城镇、大堂同一个2:1 等距正交投影（镜头在西南），
/// 坐标为院内平面坐标：x 向东、y 向南、z 向上，单位约为厘米，院内净尺寸 14 × 10 米。
/// 东西向与大堂对齐：南边是大堂的后墙（大堂北墙），后厨门洞在 x 704–840，与大堂北墙上的后厨门帘同一处；
/// 北边是两层的客房楼正立面，东边是高院墙（墙头盖瓦，开一扇后门），西边是一道齐胸的矮院墙。
/// 南墙与西墙在镜头一侧，按大堂的剖切画法压到齐腰以下。院里有井台、柴堆与劈柴墩、石桌鼓凳、水缸与一棵樟树。
/// </summary>
public static class YardSamples
{
    /// <summary>院内净面积。</summary>
    public static readonly Rect2 Yard = new(0, 0, 1400, 1000);

    /// <summary>页面的世界范围：南墙外留出一段大堂、西墙外一段暗巷。</summary>
    public static readonly Rect2 Bounds = new(-260, -220, 1920, 1420);

    public const float WallThickness = 24;

    /// <summary>北面客房楼正立面的高度（两层到檐口）；立面向两侧伸出院墙外，看得出楼比院子宽。</summary>
    public const float WingHeight = 540;

    public const float WingOverhang = 240;

    /// <summary>东院墙高（含墙头瓦）。</summary>
    public const float EastWallHeight = 270;

    /// <summary>西面矮院墙高（含墙头瓦）。</summary>
    public const float LowWallHeight = 110;

    /// <summary>南面大堂后墙剖切后留下的高度（同大堂的剖切墙）。</summary>
    public const float CutHeight = InnSamples.CutHeight;

    /// <summary>大堂后厨门洞（东西向范围），与大堂北墙上的门帘对齐。</summary>
    public const float DoorWest = 704;

    public const float DoorEast = 840;

    /// <summary>东院墙上的后门（南北向范围，墙面局部 x 即世界 y）。</summary>
    public const float GateNorth = 330;

    public const float GateSouth = 470;

    /// <summary>柴堆：贴北墙西段码放的劈柴（占地）。</summary>
    public static readonly Rect2 Woodpile = new(70, 34, 330, 100);

    public const float WoodpileHeight = 125;

    /// <summary>劈柴墩（中心），墩上斜插一把斧头。</summary>
    public static readonly Vector2 Stump = new(500, 250);

    /// <summary>石桌（中心）与两只鼓凳。</summary>
    public static readonly Vector2 StoneTable = new(1030, 600);

    public static readonly Vector2[] Stools = [new(940, 640), new(1095, 690)];

    /// <summary>借城镇井台的出件（脚底中心）。</summary>
    public static readonly Vector2 Well = new(640, 440);

    /// <summary>借城镇水缸的出件：一组靠东院墙，一组在后厨门东侧。</summary>
    public static readonly Vector2[] Jars = [new(1300, 820), new(960, 930)];

    /// <summary>借城镇樟树：东北角，树冠越过院墙。</summary>
    public static readonly Vector2 Tree = new(1250, 150);

    /// <summary>借大堂盆栽：西墙根两盆。</summary>
    public static readonly Vector2[] Plants = [new(60, 560), new(60, 690)];

    /// <summary>客房楼檐下的两盏灯笼（世界坐标，z 为灯笼顶的高度），挂在一层檐枋下的两根柱前。</summary>
    public static readonly Vector3[] Lanterns = [new(330, 30, 285), new(890, 30, 285)];

    // ── 客房楼正立面（立面局部坐标：x 即世界 x，伸出院外时为负或大于院宽；y 自檐口 −WingHeight 到地面 0）──

    /// <summary>立柱的局部 x。</summary>
    public static readonly float[] WingPosts = [-230, 50, 330, 610, 890, 1170, 1450, 1630];

    /// <summary>一层两扇房门（中心 x）。</summary>
    public static readonly float[] DoorCenters = [470, 1030];

    /// <summary>一层槅窗（中心 x），<c>true</c> 为亮着灯的。</summary>
    public static readonly (float X, bool Lit)[] GroundWindows = [(-90, false), (190, true), (750, true), (1310, false)];

    /// <summary>二层槅窗（中心 x）。</summary>
    public static readonly (float X, bool Lit)[] UpperWindows = [(190, false), (470, true), (1030, true), (1310, false)];

    public static Rect2 Door(float x) => new(x - 62, -250, 124, 250);

    public static Rect2 GroundWindow(float x) => new(x - 75, -240, 150, 120);

    public static Rect2 UpperWindow(float x) => new(x - 100, -480, 200, 110);

    /// <summary>夜里透出暖光的窗。</summary>
    public static IEnumerable<Rect2> LitWindows =>
        GroundWindows.Where(w => w.Lit).Select(w => GroundWindow(w.X)).Concat(UpperWindows.Where(w => w.Lit).Select(w => UpperWindow(w.X)));

    /// <summary>从大堂后厨出来时站在门内。</summary>
    public static readonly Vector2 BackDoor = new(772, 900);

    /// <summary>展示页的交互点（游戏模式由内容包给出）。</summary>
    public static readonly TownInteraction[] Interactions =
    [
        new("interact.yard.exit", new Vector2(772, 985), 220, "返回", "大堂", "提示", "回到江南客栈大堂", "",
            "res://scenes/preview/ExploreInn.tscn", "inn.kitchen"),
    ];
}
