using Godot;

namespace WuxiaWorld.Game.Preview.Samples;

public enum WildTreeKind
{
    /// <summary>山松：斜干，层层平展的针叶团。</summary>
    Pine,

    /// <summary>竹丛：数竿细竹，上半截叶簇。</summary>
    Bamboo,

    /// <summary>槭树：小乔木，暖色树冠（画面中唯一的暖色植被点缀）。</summary>
    Maple,
}

/// <summary>野外的树：画面坐标 (A, D)，所在台地由布局推出。</summary>
public sealed record WildTree(float A, float D, WildTreeKind Kind, float Size, int Seed);

/// <summary>山石：W 为宽（世界单位），H 为高；Moss 为顶面长苔。</summary>
public sealed record WildRock(float A, float D, float W, float H, int Seed, bool Moss = true);

public enum ShrubKind
{
    Bush,
    Fern,

    /// <summary>草药：矮丛上开小白花，可采（交互点）。</summary>
    Herb,
}

public sealed record WildShrub(float A, float D, ShrubKind Kind, float Size, int Seed);

/// <summary>崖壁上开凿的石阶：A0–A1 为阶宽，自 D1（崖脚，低台）向 D0（崖顶，高台）逐级升高。</summary>
public sealed record WildSteps(float A0, float A1, float D0, float D1, float Low, float High, int Count);

/// <summary>
/// 山路 / 野外探索样板“山门路·半山”的布局（M0-03 第三类布景：人定布局）。
/// STORY.md 第三章 `quest.main.03.mountain_pass` 的地点；这里只做“溪涧—半山茶亭—山门”一段山道，不含药庐与抄写房。
/// 与城镇、客栈同一个2:1 等距正交投影（镜头在西南）。为便于描山势，布局用“画面坐标”写：
/// A 为画面水平方向（世界东南向，屏幕向右），D 为离镜头的远近（世界西南向，屏幕向下），换算见 <see cref="W"/>；
/// z 向上，单位约为厘米。山势是三层台地：溪涧所在的谷底 L0、茶亭所在的半山 L1、山门所在的上台 L2，
/// 台地之间为崖壁，崖边在画面上大致水平、向远处层层升高，只有凿进崖里的石阶可以上下。样例数据，不是正式地图。
/// </summary>
public static class WildSamples
{
    private const float R = 0.70710678f;

    /// <summary>画面坐标 → 世界平面坐标（x 东、y 南）。</summary>
    public static Vector2 W(float a, float d) => new((a - d) * R, (a + d) * R);

    public static Vector2 W(Vector2 ad) => W(ad.X, ad.Y);

    /// <summary>世界平面坐标 → 画面坐标 (A, D)。</summary>
    public static Vector2 Frame(Vector2 w) => new((w.X + w.Y) * R, (w.Y - w.X) * R);

    /// <summary>三层台地的高度。</summary>
    public const float Z0 = 0;

    public const float Z1 = 150;

    public const float Z2 = 320;

    /// <summary>溪面比谷底低。</summary>
    public const float WaterZ = -40;

    /// <summary>可走的水平范围与谷底前沿、上台后沿（再往后没入山雾）。</summary>
    public const float WalkA0 = 120;

    public const float WalkA1 = 3480;

    public const float Front = 950;

    public const float Back = -1240;

    /// <summary>上台地面在此没入山雾，再往后是远山背景。</summary>
    public const float FogEnd = -1600;

    public static readonly WildSteps Steps1 = new(1320, 1460, 250 - 234, 250, Z0, Z1, 9);

    public static readonly WildSteps Steps2 = new(2760, 2900, -480 - 260, -480, Z1, Z2, 10);

    /// <summary>石阶一带崖边拉直，远处起伏。</summary>
    private static float Flat(float a, WildSteps s) => Mathf.Clamp((Mathf.Abs(a - (s.A0 + s.A1) / 2) - 90) / 220, 0, 1);

    /// <summary>谷底与半山之间的崖边（D 值）。</summary>
    public static float Edge1(float a) => Steps1.D1 + (Mathf.Sin(a / 260 + 0.7f) * 45 + Mathf.Sin(a / 97) * 18) * Flat(a, Steps1);

    /// <summary>半山与上台之间的崖边。</summary>
    public static float Edge2(float a) => Steps2.D1 + (Mathf.Sin(a / 300 + 2) * 55 + Mathf.Sin(a / 111 + 1) * 20) * Flat(a, Steps2);

    /// <summary>溪涧北岸、南岸（D 值），溪水沿 A 方向自左向右流。</summary>
    public static float StreamNorth(float a) => 585 + Mathf.Sin(a / 380 + 1) * 35;

    public static float StreamSouth(float a) => StreamNorth(a) + 120 + Mathf.Sin(a / 210) * 18;

    /// <summary>木桥：跨溪，A 方向宽 100，桥面高出谷底 8。</summary>
    public const float BridgeA0 = 840;

    public const float BridgeA1 = 940;

    public const float BridgeZ = 8;

    public static float BridgeD0 => StreamNorth((BridgeA0 + BridgeA1) / 2) - 45;

    public static float BridgeD1 => StreamSouth((BridgeA0 + BridgeA1) / 2) + 45;

    /// <summary>
    /// 山道中线（画面坐标），分段在同一台地上：谷底南段 → 木桥 → 谷底北段 → 石阶 → 半山 → 石阶 → 上台北去山门、东岔药庐。
    /// </summary>
    public static readonly Vector2[] PathSouth = [new(60, 1000), new(330, 890), new(640, 800), new(890, 770), new(890, 740)];

    public static readonly Vector2[] PathNorth = [new(890, 540), new(900, 470), new(1120, 390), new(1330, 300), new(1390, 262)];

    public static readonly Vector2[] PathMid =
        [new(1390, 14), new(1450, -10), new(1720, 30), new(2050, 20), new(2400, -40), new(2650, -170), new(2800, -300), new(2830, -430), new(2830, -500)];

    public static readonly Vector2[] PathTop =
        [new(2830, -742), new(2815, -810), new(2700, -900), new(2560, -1010), new(2420, -1150), new(2240, -1330), new(2060, -1520)];

    public static readonly Vector2[] PathFork = [new(2825, -805), new(3000, -880), new(3200, -1040), new(3400, -1260), new(3560, -1460)];

    public const float PathWidth = 130;

    /// <summary>茶亭（世界平面坐标的中心，边长 260，四角立柱）。</summary>
    public static readonly Vector2 Pavilion = W(2080, -250);

    public const float PavilionHalf = 130;

    /// <summary>山门牌坊：跨在北去的山道上，两柱东西相距 360，面朝南。</summary>
    public static readonly Vector2 Gate = W(2560, -1010);

    public const float GateSpan = 360;

    /// <summary>路碑、岔路木牌与脚印（画面坐标）。</summary>
    public static readonly Vector2 Stele = new(1560, -40);

    public static readonly Vector2 Signpost = new(2950, -770);

    public static readonly Vector2[] Footprints = [new(2440, -60), new(2490, -85), new(2540, -112), new(2590, -140), new(2635, -172), new(2680, -205)];

    public static readonly WildTree[] Trees =
    [
        // 谷底：前景松竹压画框、溪边杂树。
        new(40, 920, WildTreeKind.Pine, 1.25f, 1),
        new(260, 1010, WildTreeKind.Bamboo, 1.1f, 2),
        new(1180, 960, WildTreeKind.Bamboo, 1.0f, 3),
        new(1700, 1000, WildTreeKind.Pine, 1.15f, 4),
        new(2500, 980, WildTreeKind.Bamboo, 1.15f, 5),
        new(3380, 940, WildTreeKind.Pine, 1.2f, 6),
        new(560, 480, WildTreeKind.Pine, 0.95f, 7),
        new(1650, 430, WildTreeKind.Maple, 0.9f, 8),
        new(2750, 470, WildTreeKind.Pine, 1.0f, 9),
        new(3250, 520, WildTreeKind.Bamboo, 1.0f, 10),
        new(130, 470, WildTreeKind.Bamboo, 1.0f, 11),
        // 半山：茶亭旁的槭树与松、崖边竹。
        new(1840, -330, WildTreeKind.Pine, 1.1f, 12),
        new(2330, -300, WildTreeKind.Maple, 0.85f, 13),
        new(1150, -200, WildTreeKind.Bamboo, 1.05f, 14),
        new(900, -120, WildTreeKind.Pine, 1.0f, 15),
        new(3200, -260, WildTreeKind.Bamboo, 1.0f, 16),
        new(3400, -80, WildTreeKind.Pine, 0.9f, 17),
        new(400, -250, WildTreeKind.Pine, 1.05f, 18),
        // 上台：山门旁的古松与竹。
        new(2180, -1120, WildTreeKind.Pine, 1.35f, 19),
        new(2960, -1190, WildTreeKind.Pine, 1.05f, 20),
        new(1900, -800, WildTreeKind.Bamboo, 1.1f, 21),
        new(3300, -820, WildTreeKind.Pine, 1.0f, 22),
        new(1500, -900, WildTreeKind.Pine, 1.1f, 23),
        new(3500, -1100, WildTreeKind.Bamboo, 0.95f, 24),
    ];

    public static readonly WildRock[] Rocks =
    [
        new(700, 900, 150, 90, 1),
        new(1430, 820, 110, 70, 2, false),
        new(2050, 830, 190, 120, 3),
        new(3000, 880, 140, 80, 4),
        new(1050, 560, 90, 40, 5, false),
        new(1900, 560, 120, 50, 6, false),
        new(2600, 640, 100, 44, 7, false),
        new(350, 330, 130, 80, 8),
        new(1200, 330, 90, 60, 9),
        new(2950, 340, 150, 90, 10),
        new(1600, -250, 120, 80, 11),
        new(2600, -350, 140, 70, 12),
        new(3100, -120, 110, 90, 13),
        new(2130, -760, 170, 120, 14),
        new(3050, -1000, 140, 100, 15),
        new(2640, -780, 90, 50, 16, false),
    ];

    public static readonly WildShrub[] Shrubs =
    [
        new(2150, 440, ShrubKind.Herb, 1.0f, 1),
        new(2230, 410, ShrubKind.Herb, 0.8f, 2),
        new(520, 860, ShrubKind.Fern, 1.0f, 3),
        new(980, 880, ShrubKind.Bush, 1.0f, 4),
        new(1500, 420, ShrubKind.Fern, 0.9f, 5),
        new(2400, 360, ShrubKind.Bush, 1.1f, 6),
        new(3100, 460, ShrubKind.Fern, 1.0f, 7),
        new(760, 380, ShrubKind.Bush, 0.9f, 8),
        new(1260, -40, ShrubKind.Fern, 1.0f, 9),
        new(1900, -120, ShrubKind.Bush, 0.9f, 10),
        new(2700, -60, ShrubKind.Fern, 1.0f, 11),
        new(3000, -300, ShrubKind.Bush, 1.0f, 12),
        new(2200, -1000, ShrubKind.Fern, 1.0f, 13),
        new(2950, -940, ShrubKind.Bush, 0.9f, 14),
        new(2450, -760, ShrubKind.Fern, 0.9f, 15),
    ];

    /// <summary>
    /// 可交互点（世界平面坐标）。标记高度自所在台地地面算起。沈槐、药庐与抄写房的事件属第二阶段，不在此页写台词。
    /// </summary>
    public static readonly TownInteraction[] Interactions =
    [
        new("interact.wild.descend", W(170, 900), 150, "下山", "返回江湖大地图", "提示", "回到大地图", "",
            "res://scenes/preview/WorldMap.tscn"),
        new("interact.wild.herbs", W(2190, 425), 120, "采集", "路边草药", "物品", "采得止血草 ×2（样例，不入行囊）", "行囊 → 药材"),
        new("interact.wild.stele", W(Stele), 220, "查看", "山门路碑", "见闻", "已记录：路碑刻着“北上山门三里，东岔药庐”（样例）", "札记 → 见闻"),
        new("interact.wild.pavilion", Pavilion, 400, "歇脚", "半山茶亭", "提示", "同行者闲谈与休整在此触发（第二阶段）", "第三章 山门借道"),
        new("interact.wild.footprints", W(2560, -120), 90, "查看", "新鲜脚印", "线索", "已记录：一串脚印匆匆往山门去，鞋底沾着河泥（样例）", "札记 → 线索"),
        new("interact.wild.signpost", W(Signpost), 240, "查看", "岔路木牌", "提示", "东岔通药庐：伤者救援属第三章地区事件（M0 未制作）", "第三章 山门借道"),
        new("interact.wild.gate", Gate, 420, "前往", "山门", "提示", "M0 只做山道一段，山门后的抄写房未制作", "第三章 山门借道"),
    ];

    /// <summary>主线目标：山门（小地图菱形、画面边缘的方向箭头）。</summary>
    public static readonly Vector2 Goal = Gate;
}
