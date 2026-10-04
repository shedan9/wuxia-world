using Godot;
using WuxiaWorld.Game.Preview.Pages;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Presentation.Play;

/// <summary>地图借用的布景：沿用 M0 已验收的三类布局（架构文档 6.1、开发计划 M2-01“扩展已认可的 M0 布景”）。</summary>
public enum StageLayout
{
    Town,
    Inn,
    Wild,

    /// <summary>河岸专属布景（M3-01）：芦湾河滩与芦湾旧渡，见 <see cref="RiverSamples"/>。</summary>
    River,
}

/// <summary>落点：主角站位与同行者排开的方向（世界平面单位向量）。</summary>
public sealed record SpawnPoint(Vector2 At, Vector2 Back);

/// <summary>布景里额外站着的人（押运队等无人物 ID 的群众）。</summary>
public sealed record Extra(FollowerLook Look, Vector2 At, int Facing);

/// <summary>
/// 一张地图在布景里的摆放：落点、交互物、出口、路线登船点与事件锚点的世界平面坐标。
/// 规则数据（有哪些落点、交互物、事件）以内容包为准，这里只给位置；内容里有而这里缺位置的，探索页在地图中央兜底并记日志。
/// 键名：<c>interact:交互物</c>、<c>exit:出口</c>、<c>route:路线</c>、<c>anchor:锚点</c>。
/// </summary>
public sealed record MapStage(StageLayout Layout, string Region, string Caption)
{
    public IReadOnlyDictionary<string, SpawnPoint> Spawns { get; init; } = new Dictionary<string, SpawnPoint>();

    public IReadOnlyDictionary<string, Vector2> Points { get; init; } = new Dictionary<string, Vector2>();

    /// <summary>事件开放时在布景里加站的群众，按事件 ID。</summary>
    public IReadOnlyDictionary<string, Extra[]> Extras { get; init; } = new Dictionary<string, Extra[]>();

    /// <summary>布景本身已画出的人物（客栈掌柜），作为事件参与者时不再另画一个。</summary>
    public IReadOnlySet<string> Residents { get; init; } = new HashSet<string>();

    /// <summary>
    /// 剧情人物在本图的固定站位（人物 ID 或“事件 ID/人物 ID” → 位置、朝向），后者优先；未列出的围着事件锚点站。
    /// 用于避开屏风、柱子等遮挡，或让同一人物在不同事件里站在不同处（唐守亭先在闸桥上，战后被按在堤前泥地里）。
    /// </summary>
    public IReadOnlyDictionary<string, (Vector2 At, int Facing)> Stand { get; init; } = new Dictionary<string, (Vector2, int)>();

    /// <summary>河岸布景的地点（<see cref="StageLayout.River"/>）。</summary>
    public RiverSite River { get; init; }

    /// <summary>本图的配乐（只配主要场景，次要场景为 null、只有环境声）。</summary>
    public string? Music { get; init; }

    /// <summary>本图的环境声层。</summary>
    public string[] Ambience { get; init; } = [];
}

/// <summary>
/// 第一章四张地图的摆放。芦湾街、江南客栈沿用 M0 已验收的城镇、客栈布景；芦湾河滩与芦湾旧渡自 2026-10-04 起用河岸专属布景
/// （M3-01，此前暂借山路溪涧）。坐标沿用 <see cref="TownSamples"/>、<see cref="InnSamples"/> 的世界坐标；
/// 河岸用画面坐标 A / D 经 <see cref="RiverSamples.W(float, float)"/> 换算。
/// </summary>
public static class MapStaging
{
    private static readonly Vector2 North = new(0, -1);
    private static readonly Vector2 South = new(0, 1);
    private static readonly Vector2 West = new(-1, 0);

    /// <summary>画面坐标里朝画面左（A 减小）的世界方向。</summary>
    private static readonly Vector2 FrameLeft = (WildSamples.W(0, 0) - WildSamples.W(1, 0)).Normalized();

    private static Vector2 W(float a, float d) => RiverSamples.W(a, d);

    public static readonly IReadOnlyDictionary<string, MapStage> Maps = new Dictionary<string, MapStage>
    {
        ["map.jiangnan.luwan_shore"] = new(StageLayout.River, "芦湾", "芦湾河滩：河岸专属布景（M3-01）；芦苇为 AI 精灵，柳树、山石与渡船借城镇 / 山路出件")
        {
            River = RiverSite.Shore,
            Ambience = ["amb.river", "amb.reeds"],
            Spawns = new Dictionary<string, SpawnPoint>
            {
                ["wake"] = new(W(1000, 300), FrameLeft),
                ["path_end"] = new(W(300, 700), FrameLeft),
            },
            Points = new Dictionary<string, Vector2>
            {
                ["exit:to_street"] = W(150, 760),
                ["interact:stone_marks"] = W(2050, 40),
                ["interact:tide_line"] = W(2760, 110),
                ["anchor:wake"] = W(1000, 300),
            },
        },
        ["map.jiangnan.luwan_street"] = new(StageLayout.Town, "芦湾", "芦湾街：M0 已验收的河街布景；人物为 AI 全身样稿")
        {
            Music = "bgm.town.luwan",
            Ambience = ["amb.town"],
            Spawns = new Dictionary<string, SpawnPoint>
            {
                ["south_gate"] = new(new Vector2(300, 1600), North),
                ["inn_door"] = new(TownSamples.InnDoor, South),
                ["pier"] = new(new Vector2(1390, 1790), North),
            },
            Points = new Dictionary<string, Vector2>
            {
                ["exit:to_shore"] = new(130, 1600),
                ["exit:to_inn"] = new(3760, 1400),
                ["route:route.jiangnan.luwan_to_old_ferry"] = new(1390, 1905),
                ["interact:ferry_notice"] = new(760, 1600),
                ["interact:ferry_tags"] = new(1000, 1450),
                ["interact:general_store"] = new(2560, 1620), // M0 布景里的杂货摊
            },
        },
        ["map.jiangnan.inn"] = new(StageLayout.Inn, "芦湾", "江南客栈：M0 已验收的大堂布景；令狐冲、黄蓉、萧峰与杜三篙暂为占位剪影（AI 形象待人物锚点核验后制作）")
        {
            Ambience = ["amb.inn"],
            Spawns = new Dictionary<string, SpawnPoint>
            {
                ["door"] = new(InnSamples.FrontDoor, North),
                ["hall"] = new(new Vector2(760, 600), West),
            },
            Points = new Dictionary<string, Vector2>
            {
                ["exit:out"] = new(630, 900),
                ["interact:rations_basket"] = new(390, 280),
                ["anchor:hall"] = new(1010, 560),
                ["anchor:door"] = new(630, 640),
            },
            Residents = new HashSet<string> { "char.qiao_hongxiao" },

            // 三位侠客站在屏风右侧的空地上，不被屏风挡住；令狐冲靠柱、黄蓉居中、萧峰在外侧。
            Stand = new Dictionary<string, (Vector2, int)>
            {
                ["char.linghu_chong"] = (new Vector2(995, 850), -1),
                ["char.huang_rong"] = (new Vector2(920, 672), -1),
                ["char.xiao_feng"] = (new Vector2(1110, 614), -1),
            },
        },
        ["map.jiangnan.old_ferry"] = new(StageLayout.River, "芦湾", "芦湾旧渡：河岸专属布景（M3-01）；栈桥、河堤与水门为几何贴 AI 纹理，大船借城镇乌篷船出件")
        {
            River = RiverSite.OldFerry,
            Ambience = ["amb.river", "amb.reeds"],
            Spawns = new Dictionary<string, SpawnPoint>
            {
                ["landing"] = new(W(666, 124), new Vector2(-1, 0)),
                ["sluice"] = new(W(2330, 540), FrameLeft),
            },
            Points = new Dictionary<string, Vector2>
            {
                ["route:route.jiangnan.old_ferry_to_luwan"] = W(956, -166),
                ["interact:locked_boat"] = W(1460, 110),
                ["anchor:landing"] = W(780, 260),
                ["anchor:sluice"] = W(2480, 480),
            },
            Stand = new Dictionary<string, (Vector2, int)>
            {
                // 唐守亭先站在闸桥上，战后被按在堤前泥地里；获救的杜三篙坐在他西边。
                ["event.ch01.sluice_confrontation/char.tang_shouting"] = (W(2760, 80), -1),
                ["event.ch01.copy_custody/char.tang_shouting"] = (W(2600, 560), -1),
                ["event.ch01.copy_custody/char.du_sangao"] = (W(2420, 620), 1),
            },
            Extras = new Dictionary<string, Extra[]>
            {
                // 押运队守在锁船旁。
                ["event.ch01.escort_regroup"] =
                [
                    new(Looks.Escort, W(1360, 210), 1),
                    new(Looks.Escort, W(1560, 150), -1),
                    new(Looks.Escort, W(1640, 250), -1),
                ],
            },
        },
    };

    /// <summary>任务目标就在本图时，目标指向所指的交互物（按目标 ID）。</summary>
    public static readonly IReadOnlyDictionary<string, string> ObjectiveTargets = new Dictionary<string, string>
    {
        ["inspect_stones"] = "interact:stone_marks",
        ["check_tags"] = "interact:ferry_tags",
        ["find_tide_line"] = "interact:tide_line",
        ["free_ferryman"] = "interact:locked_boat",
    };

    /// <summary>事件参与者围着锚点站的偏移（最多四人）。</summary>
    public static readonly Vector2[] AroundAnchor = [new(-80, -70), new(90, -50), new(110, 70), new(-70, 90)];
}

/// <summary>人物在探索布景里的外观：有 AI 全身样稿的用样稿，其余为占位剪影（按装束区分）。</summary>
public static class Looks
{
    public static readonly FollowerLook Escort = new("figure.escort", FigureLook.Hero, Color.FromHtml("#6A5A48"));

    private static readonly Dictionary<string, FollowerLook> ByCharacter = new()
    {
        ["char.hero"] = new("figure.hero", FigureLook.Hero, UiPalette.Accent),
        ["char.lu_qinghe"] = new("figure.lu_qinghe", FigureLook.Boatwoman, UiPalette.Trim),
        ["char.qiao_hongxiao"] = new("figure.qiao_hongxiao", FigureLook.Keeper, Color.FromHtml("#B8544A")),
        ["char.tang_shouting"] = new("figure.tang_shouting", FigureLook.Hero, Color.FromHtml("#4A5A6A")),
        ["char.du_sangao"] = new("figure.du_sangao", FigureLook.Hero, Color.FromHtml("#7A6A50")),
        ["char.linghu_chong"] = new("figure.linghu_chong", FigureLook.Hero, Color.FromHtml("#4F7A86")),
        ["char.huang_rong"] = new("figure.huang_rong", FigureLook.Keeper, Color.FromHtml("#C9A24A")),
        ["char.xiao_feng"] = new("figure.xiao_feng", FigureLook.Hero, Color.FromHtml("#6B4A3A")),
    };

    public static FollowerLook Of(string characterId) =>
        ByCharacter.TryGetValue(characterId, out var look) ? look : new FollowerLook("figure." + characterId, FigureLook.Hero, UiPalette.TextMuted);
}
