using Godot;

namespace WuxiaWorld.Game.Preview.Samples;

public enum NodeState
{
    /// <summary>已到访：实色地标。</summary>
    Visited,

    /// <summary>已发现未到访：淡色地标。</summary>
    Known,

    /// <summary>尚未开放：可见但不可启程，写明开放条件。</summary>
    Locked,
}

/// <summary>地标图标种类（M0 为程序化小图，正式版换成对应插画图标）。</summary>
public enum MapIcon
{
    Inn,
    Ferry,
    Granary,
    Wharf,
    Gate,
    Relay,
    Pagoda,
    Hall,
    Peak,
    Beggars,
}

public enum TravelMode
{
    Horse,
    Carriage,
    Ferry,
}

/// <summary>
/// 大地图地标。Pos 为地图坐标（<see cref="WorldMapSamples.Size"/> 画布内），Region 为地域说明，
/// Events 为到达后可见的事件入口（◆ 主线、○ 地区事件）。Icon 为地图上的地标图标；LockReason 仅对未开放地标有效。
/// </summary>
public sealed record MapNode(
    string Id,
    string Name,
    string Region,
    Vector2 Pos,
    MapIcon Icon,
    NodeState State,
    string[] Events,
    string? LockReason = null);

/// <summary>两地标间的一段路。Water 为水路（只走渡船），否则为陆路（骑马、马车）；Via 为途经的弯折点。</summary>
public sealed record MapEdge(string From, string To, bool Water, Vector2[] Via);

/// <summary>
/// 江湖大地图展示页的固定样例：第一篇第一章结束、第二章开始时的局势（STORY 第 1.3–1.4 节与第 2 节）。
/// 门派地标沿用 STORY 第 0.1 节的 <c>map.faction.*</c>；主线地域在 STORY 中尚无稳定节点 ID，
/// 此处用 <c>sample.*</c> 标出，内容配置建立时另行命名。地理为示意，不是正式地图布局。
/// 旅行耗时与铜钱按路程估算，只用于展示面板，不是架构文档 6.2 的正式路线数据。
/// </summary>
public static class WorldMapSamples
{
    /// <summary>
    /// 世界尺度：下方坐标按 2880×1600 的设计稿书写，运行时整体放大这个倍数。山体、河宽与地名章不随之放大，
    /// 因此倍数越大，世界越辽阔、地点间距越远。
    /// </summary>
    public const float Scale = 1.6f;

    /// <summary>地图画布的逻辑尺寸（最小缩放下仍铺满 1920×1080）。</summary>
    public static readonly Vector2 Size = new Vector2(2880, 1600) * Scale;

    public const string Start = "sample.jiangnan_inn";

    /// <summary>当前时辰序号（0 子时 … 8 申时）。</summary>
    public const int StartHour = 8;

    public static readonly string[] Hours = ["子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥"];

    public static readonly string[] Companions = ["主角", "陆青禾", "令狐冲"];

    private static readonly MapNode[] DesignNodes =
    [
        new(Start, "江南客栈", "芦湾　·　第一篇核心地域", new(1818, 1010), MapIcon.Inn, NodeState.Visited,
            ["○ 乔红绡：失踪信使的去向"]),
        new("sample.luwan_ferry", "芦湾旧渡", "芦湾　·　第一篇核心地域", new(1930, 905), MapIcon.Ferry, NodeState.Visited,
            ["✓ 旧渡解围（已结算）", "○ 陆青禾：船牌与水位"]),
        new("sample.granary", "江南粮仓", "江南　·　第二章调查段", new(1640, 1090), MapIcon.Granary, NodeState.Known,
            ["◆ 主线　核对两封真章信", "○ 收粮人口中的换马线索"]),
        new("sample.river_wharf", "河埠", "江南　·　第二章调查段", new(1560, 880), MapIcon.Wharf, NodeState.Known,
            ["◆ 主线　河埠记录中的换马线索"]),
        new("sample.mountain_pass", "山门路", "山门路　·　第三章调查段", new(1330, 690), MapIcon.Gate, NodeState.Known,
            ["◆ 主线　沈槐留下的底账", "○ 地区事件　药庐救援"]),
        new("sample.beiling", "北岭驿", "北岭　·　第四章", new(1360, 300), MapIcon.Relay, NodeState.Locked,
            [], "取得第二、三章两份核心证据后开放"),
        new("sample.guichao", "归潮渡", "江口　·　第五章", new(2080, 800), MapIcon.Ferry, NodeState.Locked,
            [], "北岭驿之后开放"),
        new("map.faction.shaolin", "少林外道", "少林　·　药棚", new(1080, 520), MapIcon.Pagoda, NodeState.Known,
            ["○ 地区事件　被冒用的救济名册"]),
        new("map.faction.wudang", "武当山驿", "武当　·　山下集市", new(900, 830), MapIcon.Hall, NodeState.Known,
            ["○ 地区事件　行程名册与自管信使"]),
        new("map.faction.emei", "峨眉山路", "峨眉　·　歇脚处", new(420, 1010), MapIcon.Peak, NodeState.Known,
            ["○ 地区事件　失窃名册"]),
        new("map.faction.beggars", "丐帮歇脚点", "丐帮　·　沿线歇脚点", new(1500, 560), MapIcon.Beggars, NodeState.Known,
            ["○ 地区事件　真假求救记号"]),
    ];

    private static readonly MapEdge[] DesignEdges =
    [
        new(Start, "sample.luwan_ferry", false, [new(1880, 960)]),
        new(Start, "sample.granary", false, [new(1730, 1070)]),
        new(Start, "sample.river_wharf", false, [new(1700, 960)]),
        new("sample.river_wharf", "sample.mountain_pass", false, [new(1450, 800)]),
        new("sample.mountain_pass", "map.faction.beggars", false, [new(1420, 610)]),
        new("sample.mountain_pass", "map.faction.shaolin", false, [new(1200, 620)]),
        new("sample.mountain_pass", "map.faction.wudang", false, [new(1110, 780)]),
        new("map.faction.beggars", "sample.beiling", false, [new(1470, 420)]),
        new("map.faction.shaolin", "sample.beiling", false, [new(1230, 380)]),
        new("map.faction.wudang", "map.faction.emei", false, [new(700, 900), new(540, 990)]),
        new("sample.granary", "sample.river_wharf", false, [new(1570, 990)]),
        // 大江水路：沿江而行，各段途经点贴着江心。
        new("sample.luwan_ferry", "sample.river_wharf", true, [new(1840, 880), new(1700, 900), new(1620, 870)]),
        new("sample.luwan_ferry", "sample.guichao", true, [new(2010, 850)]),
        new("sample.river_wharf", "map.faction.wudang", true, [new(1400, 900), new(1180, 880), new(1000, 860)]),
    ];

    public static readonly IReadOnlyList<MapNode> Nodes = DesignNodes.Select(n => n with { Pos = n.Pos * Scale }).ToArray();

    public static readonly IReadOnlyList<MapEdge> Edges =
        DesignEdges.Select(e => e with { Via = e.Via.Select(v => v * Scale).ToArray() }).ToArray();

    public static MapNode Node(string id) => Nodes.First(n => n.Id == id);

    public static string ModeName(TravelMode mode) => mode switch
    {
        TravelMode.Horse => "骑马",
        TravelMode.Carriage => "马车",
        _ => "渡船",
    };

    public static string ModeGlyph(TravelMode mode) => mode switch
    {
        TravelMode.Horse => "骑",
        TravelMode.Carriage => "车",
        _ => "船",
    };

    /// <summary>每时辰行进的地图距离与每时辰铜钱（样例估算）。距离随世界尺度放大，时辰数不变。</summary>
    public static (float Pace, int CoinPerHour) Rate(TravelMode mode) => mode switch
    {
        TravelMode.Horse => (260 * Scale, 10),
        TravelMode.Carriage => (190 * Scale, 14),
        _ => (230 * Scale, 6),
    };

    public static string ModeNote(TravelMode mode) => mode switch
    {
        TravelMode.Horse => "最快的陆路；雨天山道减速",
        TravelMode.Carriage => "较慢，途中可歇息，伤员随行不加重伤势",
        _ => "沿大江水路，只到临水的渡口",
    };
}
