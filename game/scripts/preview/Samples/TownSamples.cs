using Godot;

namespace WuxiaWorld.Game.Preview.Samples;

public enum HouseStyle
{
    /// <summary>粉墙黛瓦单层民居，两端硬山。</summary>
    House,

    /// <summary>两端马头墙的民居。</summary>
    Gable,

    /// <summary>临街铺面：整面木排门、挑出竖招牌。</summary>
    Shop,

    /// <summary>两层客栈：腰檐、楼上木槅窗、匾额与幌子。</summary>
    Inn,
}

/// <summary>
/// 一栋房屋：占地 X0–X1（东西）、Y0–Y1（南北，Y1 为临街正面），WallHeight 为檐口高度，屋脊沿东西向。
/// 世界单位约为厘米，z 向上。
/// </summary>
public sealed record TownHouse(string Id, float X0, float Y0, float X1, float Y1, float WallHeight, HouseStyle Style,
    int Seed, string? Sign = null);

public enum TreeKind
{
    Willow,
    Camphor,
}

public sealed record TownTree(Vector2 Position, TreeKind Kind, float Size, int Seed);

public enum PropKind
{
    NoticeBoard,
    StoneMark,
    Stall,
    Well,
    Boat,
    Jars,
}

public sealed record TownProp(string Id, PropKind Kind, Vector2 Position, int Seed = 0);

/// <summary>
/// 可交互点：靠近时出现“E + 动作 + 对象”，按下推一条通知，不写存档。MarkerHeight 为头顶菱形离地高度（世界单位）。
/// 设了 Scene 的是出入口：按下切到该布景页，Arrival 告诉目标页从哪个门进来。
/// </summary>
public sealed record TownInteraction(string Id, Vector2 Position, float MarkerHeight, string Verb, string Target, string Kind, string Text, string Where,
    string? Scene = null, string? Arrival = null);

/// <summary>
/// 城镇 / 街道探索样板“芦湾河街”的布局（M0-03 第一步：人定布局）。架构文档 10.3 规定布局是事实来源，
/// AI 生成的地面纹理与建筑件只贴合这里的街道、河岸与占地，不反过来改布局。
/// 坐标是真实的平面坐标：x 向东、y 向南、z 向上，单位约为厘米（人高约 175）；
/// 画面由 <c>TownView</c> 统一按2:1 等距正交投影，地面、墙、屋顶、桥与驳岸共用同一投影。样例数据，不是正式地图。
/// </summary>
public static class TownSamples
{
    public static readonly Rect2 Bounds = new(0, 0, 4800, 3600);

    /// <summary>进入页面时主角所在：旧渡石痕旁，一进来就能看到交互提示。</summary>
    public static readonly Vector2 Spawn = new(1130, 1700);

    /// <summary>从客栈出门时站在门前街上。</summary>
    public static readonly Vector2 InnDoor = new(3760, 1470);

    /// <summary>河面比街面低（世界单位），北岸露出条石驳岸。</summary>
    public const float WaterLevel = -80;

    /// <summary>北岸（街侧）河沿：y 随 x 缓慢起伏。</summary>
    public static float NorthBank(float x) => Bank(NorthBankWave, x);

    /// <summary>南岸河沿。</summary>
    public static float SouthBank(float x) => Bank(SouthBankWave, x);

    /// <summary>
    /// 两岸函数的参数：y = 基线 + a1·sin(x / p1 + φ1) + a2·sin(x / p2 + φ2)，
    /// 依次为（基线、a1、p1、φ1）与（a2、p2、φ2、0）；河水着色器按同一组参数算离岸远近。
    /// </summary>
    public static readonly Vector4[] NorthBankWave = [new(1850, 18, 620, 0), new(8, 210, 1.3f, 0)];

    public static readonly Vector4[] SouthBankWave = [new(2760, 16, 700, 1), new(6, 230, 0, 0)];

    /// <summary>
    /// 河中物件在水面上的占地（圆角矩形：中心、半宽半长；圆角半径、露出水面高度），河水着色器据此画接触暗影、倒影与涟漪：
    /// 两条乌篷船（船身两头收圆）、渡口石阶伸入水中的部分、平桥两座桥墩。
    /// </summary>
    public static readonly (Vector4 Box, Vector2 Shape)[] WaterObstacles =
    [
        (new(1640, 2080, 160, 48), new(44, 60)),
        (new(3900, 2450, 138, 48), new(44, 60)),
        (new(1390, 1925, 130, 75), new(4, 70)),
        (new(2900, 2110, 100, 30), new(4, 50)),
        (new(2900, 2470, 100, 30), new(4, 50)),
    ];

    private static float Bank(Vector4[] w, float x) =>
        w[0].X + w[0].Y * Mathf.Sin(x / w[0].Z + w[0].W) + w[1].X * Mathf.Sin(x / w[1].Y + w[1].Z);

    public const float StreetNorth = 1350;
    public const float SouthWalkEnd = 3060;

    /// <summary>平桥：跨河连接两岸，可行走。</summary>
    public static readonly Rect2 Bridge = new(2800, 1800, 200, 1010);

    /// <summary>渡口石阶：自北岸街面下到水边，伸入河中。</summary>
    public static readonly Rect2 FerrySteps = new(1260, 1840, 260, 160);

    /// <summary>北侧小巷与井台小场。</summary>
    public static readonly Rect2 Alley = new(1900, 560, 260, 800);

    public static readonly Rect2 Plaza = new(1450, 120, 1150, 460);

    public static readonly TownHouse[] Houses =
    [
        // 后排（小场两侧）。
        new("house.back.1", 150, 60, 1350, 560, 360, HouseStyle.Gable, 11),
        new("house.back.2", 2700, 40, 3900, 560, 370, HouseStyle.House, 12),

        // 河街北侧临街一排。
        new("house.north.1", 60, 700, 850, 1350, 380, HouseStyle.Gable, 21),
        new("house.north.2", 890, 700, 1860, 1350, 360, HouseStyle.Shop, 22, "米"),
        new("house.north.3", 2200, 700, 3120, 1350, 360, HouseStyle.Shop, 25, "茶"),
        new("house.inn", 3220, 620, 4300, 1350, 660, HouseStyle.Inn, 26, "江南客栈"),
        new("house.north.4", 4380, 700, 4800, 1350, 380, HouseStyle.Gable, 27),

        // 南岸一排：朝河的正面背对镜头，镜头看到山墙与后墙；南岸街在其与河之间，走过时被屋身遮挡。
        new("house.south.1", 150, 3100, 1100, 3600, 360, HouseStyle.Gable, 31),
        new("house.south.2", 1550, 3100, 2350, 3600, 350, HouseStyle.House, 32),
        new("house.south.3", 3350, 3100, 4250, 3600, 370, HouseStyle.Gable, 35),
    ];

    /// <summary>廊棚：沿河一段带顶的廊，柱在河沿与街侧两排，屋面遮住廊下行人。</summary>
    public static readonly Rect2 Corridor = new(3300, 1600, 1400, 210);

    public static readonly TownTree[] Trees =
    [
        new(new Vector2(560, 1790), TreeKind.Willow, 1f, 1),
        new(new Vector2(2050, 1800), TreeKind.Willow, 0.95f, 2),
        new(new Vector2(3150, 1795), TreeKind.Willow, 1.05f, 3),
        new(new Vector2(1320, 2860), TreeKind.Willow, 0.9f, 5),
        new(new Vector2(2650, 2860), TreeKind.Willow, 1f, 6),
        new(new Vector2(4550, 2870), TreeKind.Willow, 0.92f, 7),
        new(new Vector2(2450, 330), TreeKind.Camphor, 1.1f, 8),
        new(new Vector2(4400, 300), TreeKind.Camphor, 1.2f, 9),
        new(new Vector2(1500, 3350), TreeKind.Camphor, 1f, 10),
    ];

    public static readonly TownProp[] Props =
    [
        new("prop.notice", PropKind.NoticeBoard, new Vector2(760, 1560)),
        new("prop.stone_mark", PropKind.StoneMark, new Vector2(1180, 1790)),
        new("prop.stall", PropKind.Stall, new Vector2(2560, 1560), 3),
        new("prop.well", PropKind.Well, new Vector2(2040, 380)),
        new("prop.boat.ferry", PropKind.Boat, new Vector2(1640, 2080), 1),
        new("prop.boat.2", PropKind.Boat, new Vector2(3900, 2450), 2),
        new("prop.jars", PropKind.Jars, new Vector2(1700, 1430), 4),
        new("prop.jars.inn", PropKind.Jars, new Vector2(4340, 1430), 5),
    ];

    public static readonly TownInteraction[] Interactions =
    [
        new("interact.notice", new Vector2(760, 1600), 260, "查看", "渡口告示", "见闻", "已记录：渡口告示上的船牌号", "札记 → 见闻"),
        new("interact.stone_mark", new Vector2(1180, 1800), 170, "查看", "旧渡石痕", "见闻", "已记录：亲见的旧渡石痕", "札记 → 见闻"),
        new("interact.stall", new Vector2(2560, 1620), 300, "查看", "杂货摊", "物品", "摊上有干粮、油纸伞出售", "行囊 → 商店"),
        new("interact.inn_door", new Vector2(3760, 1400), 300, "进入", "江南客栈", "提示", "进入客栈大堂", "",
            "res://scenes/preview/ExploreInn.tscn", "inn.front_door"),
        new("interact.well", new Vector2(2040, 470), 220, "查看", "井台", "见闻", "井栏石上刻着“芦湾”二字", "札记 → 见闻"),
    ];

    /// <summary>主线目标在小地图上的位置（旧渡石痕）。</summary>
    public static readonly Vector2 Goal = new(1180, 1800);
}
