using Godot;

namespace WuxiaWorld.Game.Preview.Samples;

public enum InnTableKind
{
    /// <summary>八仙桌，四面长凳，桌上茶壶茶盏。</summary>
    Tea,

    /// <summary>八仙桌，四面长凳，桌上碗筷。</summary>
    Meal,

    /// <summary>屏风后的雅座：南北两条长凳。</summary>
    Booth,
}

public sealed record InnTable(string Id, Vector2 Center, InnTableKind Kind, int Seed);

public enum InnFigure
{
    Keeper,
    Waiter,
    Guest,
}

/// <summary>室内的静态人物：位置（世界平面坐标）、朝向（屏幕左右 -1 / 1）。</summary>
public sealed record InnPerson(string Id, InnFigure Figure, Vector2 Position, int Facing);

/// <summary>
/// 客栈 / 室内探索样板“江南客栈·大堂”的布局（M0-03 第二类布景：人定布局）。
/// 与城镇共用同一个2:1 等距正交投影（镜头在西南）：镜头一侧的南墙与西墙剖切到齐腰以下，露出室内；
/// 北墙与东墙是完整的内墙面。坐标为室内平面坐标：x 向东、y 向南、z 向上，单位约为厘米，墙内净尺寸 13.2 × 9.4 米。
/// 室内比例按大堂构图单独取，城镇页客栈外观的占地待正式布局时对齐。样例数据，不是正式地图；
/// 乔红绡为 STORY.md 第一篇的客栈掌柜，此处只放占位剪影，不写台词。
/// </summary>
public static class InnSamples
{
    /// <summary>墙内净面积。</summary>
    public static readonly Rect2 Room = new(0, 0, 1320, 940);

    /// <summary>页面的世界范围：墙外留出门前一段街面与四周暗场。</summary>
    public static readonly Rect2 Bounds = new(-260, -200, 1840, 1440);

    public const float WallThickness = 24;

    /// <summary>北墙、东墙的内墙高（楼板底）。</summary>
    public const float WallHeight = 330;

    /// <summary>南墙、西墙剖切后留下的高度。</summary>
    public const float CutHeight = 70;

    /// <summary>南墙正中的大门口（东西向范围）。</summary>
    public const float DoorWest = 560;

    public const float DoorEast = 700;

    /// <summary>柜台（占地）；其后是掌柜的位置，不让行人进去。</summary>
    public static readonly Rect2 Counter = new(140, 150, 460, 85);

    public static readonly Rect2 BehindCounter = new(0, 0, 620, 150);

    /// <summary>楼梯：靠东墙自南（y 720，地面）向北升到楼板高度，北端接楼上平台。</summary>
    public static readonly Rect2 Stairs = new(1150, 20, 160, 700);

    public const float StairsLanding = 200;

    /// <summary>屏风：四扇，立在两根柱之间，隔出东侧雅座。</summary>
    public static readonly Rect2 Screen = new(872, 300, 16, 320);

    public static readonly Vector2[] Pillars = [new(880, 280), new(880, 640), new(470, 640)];

    public static readonly InnTable[] Tables =
    [
        new("table.1", new Vector2(290, 470), InnTableKind.Tea, 1),
        new("table.2", new Vector2(290, 770), InnTableKind.Meal, 2),
        new("table.3", new Vector2(720, 470), InnTableKind.Tea, 3),
        new("table.booth", new Vector2(1020, 440), InnTableKind.Booth, 4),
    ];

    public static readonly Vector2 Jars = new(78, 250);

    public static readonly Vector2[] Plants = [new(70, 870), new(790, 880)];

    /// <summary>吊灯（世界坐标，z 为灯笼顶的高度）：柜台两盏、每张桌上一盏。</summary>
    public static readonly Vector3[] Lanterns =
    [
        new(180, 200, 250), new(590, 200, 250), new(290, 470, 240), new(720, 470, 240), new(290, 770, 240), new(1020, 440, 230),
    ];

    public static readonly InnPerson[] People =
    [
        new("npc.qiao_hongxiao", InnFigure.Keeper, new Vector2(380, 92), 1),
        new("npc.waiter", InnFigure.Waiter, new Vector2(960, 110), -1),
        new("npc.tea_guest", InnFigure.Guest, new Vector2(208, 470), 1),
    ];

    /// <summary>从街上进门时站在门内。</summary>
    public static readonly Vector2 FrontDoor = new(630, 740);

    public static readonly TownInteraction[] Interactions =
    [
        new("interact.inn.exit", new Vector2(630, 915), 240, "离开", "江南客栈", "提示", "回到芦湾河街", "",
            "res://scenes/preview/ExploreTown.tscn", "town.inn_door"),
        new("interact.inn.keeper", new Vector2(380, 215), 250, "询问", "掌柜乔红绡", "提示", "住店、打尖与打听消息由对话接入（第二阶段）", "第一章 江南会客"),
        new("interact.inn.menu", new Vector2(650, 190), 280, "查看", "客栈水牌", "物品", "水牌上写着菱角、黄酒、阳春面（样例）", "行囊 → 商店"),
        new("interact.inn.guest", new Vector2(208, 470), 190, "旁听", "邻桌茶客", "见闻", "已记录：茶客说起渡口近来夜里常有船灯（样例）", "札记 → 见闻"),
        new("interact.inn.booth", new Vector2(1020, 440), 200, "查看", "屏风后的雅座", "提示", "三侠会面在此落座（第二阶段玩法 Demo）", "第一章 江南会客"),
        new("interact.inn.stairs", new Vector2(1230, 790), 250, "上楼", "客房", "提示", "M0 只做大堂，楼上客房未制作", "M0-03"),
    ];

    /// <summary>主线目标在小地图上的位置（雅座）。</summary>
    public static readonly Vector2 Goal = new(1020, 440);
}
