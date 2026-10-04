using Godot;

namespace WuxiaWorld.Game.Preview.Samples;

/// <summary>河岸布景的两处地点：芦湾河滩（开场醒来处）与芦湾旧渡（下游废渡口与水门）。</summary>
public enum RiverSite
{
    Shore,
    OldFerry,
}

/// <summary>沿画面水平方向起伏的一条线：D = Base + Amp1·sin(A / Period1 + Phase1) + Amp2·sin(A / Period2 + Phase2)。着色器按同一组参数算离岸远近。</summary>
public readonly record struct Wave(float Base, float Amp1, float Period1, float Phase1, float Amp2 = 0, float Period2 = 1, float Phase2 = 0)
{
    public float At(float a) => Base + Amp1 * Mathf.Sin(a / Period1 + Phase1) + Amp2 * Mathf.Sin(a / Period2 + Phase2);

    public Vector4 First => new(Base, Amp1, Period1, Phase1);

    public Vector3 Second => new(Amp2, Period2, Phase2);
}

/// <summary>
/// 一片芦苇：中心 (A, D)、横向宽与纵深，内含 Count 丛（AI 精灵 river.reed.N，Arts 里轮换），高 Height 上下浮动。
/// Wet 为长在浅水里（脚下压水影与涟漪）；Blocks 为整片挡路。
/// </summary>
public sealed record ReedBed(float A, float D, float Width, float Depth, float Height, int[] Arts, int Count, int Seed, bool Wet = false, bool Blocks = true);

/// <summary>借用的 AI 出件（城镇柳树 town.tree.N、山石 wild.rock.N）：画面坐标、缩放与左右翻转。</summary>
public sealed record RiverProp(string Art, float A, float D, float Size, bool Flip = false);

/// <summary>
/// 旧渡栈桥：桥根在岸上 (A, D)，顺世界东向伸进江里 Length、宽 Width、桥面高 Z（与城镇的桥、船同向，画面上斜向右上，看得出立体）。
/// </summary>
public sealed record Jetty(float A, float D, float Length, float Width, float Z)
{
    public Vector2 Root => RiverSamples.W(A, D);

    /// <summary>桥面的世界坐标矩形（桥根往岸上多伸 40）。</summary>
    public Rect2 Deck => new(Root + new Vector2(-40, -Width / 2), new Vector2(Length + 40, Width));
}

/// <summary>
/// 石砌河堤与水门：堤沿 A 自 A0 延伸到画面外，临水的后沿 Back、朝镜头的前沿 Front、堤顶高 Z；
/// 水门开在 GateA0–GateA1，堤顶在门洞上方是石板闸桥；门内引水渠自堤脚朝镜头流去（渠面低 ChannelZ）；
/// Steps 为贴在堤前的石阶，ExitA 以东的堤前低地是苇荡，不能走。
/// </summary>
public sealed record Levee(float A0, float A1, float Back, float Front, float Z, float GateA0, float GateA1, float ChannelZ, WildSteps Steps, float MarshA);

/// <summary>泊船（城镇乌篷船 / 渡船出件挪到河里）：画面坐标、缩放、翻转。</summary>
public sealed record Moored(string Art, float A, float D, float Scale, bool Flip = false);

/// <summary>
/// 河岸布景“芦湾河滩 / 芦湾旧渡”的布局（M3-01 专属布景，取代 M2 借用的山路溪涧）。
/// 与山路同一套画面坐标：A 为画面水平（向右），D 为离镜头远近（向下），经 <see cref="WildSamples.W(float, float)"/> 换成世界平面坐标，
/// 投影后屏幕坐标为 (A, D / 2 − 0.87 z)。远处是宽阔的江面，没入水汽，远岸只在远景里隐约可见；近处是雨后的泥沙滩，
/// 滩后接草坡，岸线以 <see cref="Wave"/> 描出。地面、碰撞、高度、排序与小地图共用这一份。
/// </summary>
public sealed class RiverSamples
{
    public const float WaterZ = -8;

    /// <summary>江面自岸线往远处铺到这里，再往后交给远景。</summary>
    public const float WaterFar = -850;

    public required RiverSite Site { get; init; }

    /// <summary>水线：比它小（更远）是水。</summary>
    public required Wave Shore { get; init; }

    /// <summary>草坡起点离水线的距离（泥沙滩宽度）。</summary>
    public required Wave Beach { get; init; }

    public required float WalkA0 { get; init; }

    public required float WalkA1 { get; init; }

    /// <summary>可走区的前沿（离镜头最近），再往前只有压画框的前景芦苇。</summary>
    public required float Front { get; init; }

    /// <summary>镜头水平范围。</summary>
    public required float ViewA0 { get; init; }

    public required float ViewA1 { get; init; }

    public Vector2[] Path { get; init; } = [];

    public ReedBed[] Reeds { get; init; } = [];

    public RiverProp[] Props { get; init; } = [];

    public Moored[] Boats { get; init; } = [];

    public Jetty? Jetty { get; init; }

    public Levee? Levee { get; init; }

    /// <summary>刻痕青石（半没在水里，河滩调查点）。</summary>
    public Vector2? StoneMark { get; init; }

    /// <summary>拖船潮痕：泥滩上两道并行的新鲜拖痕（画面坐标折线）。</summary>
    public Vector2[] TideMark { get; init; } = [];

    /// <summary>锁船的木桩与铁链拴在船头的位置（画面坐标）；Bow 的 z 为船舷高。</summary>
    public (Vector2 Stake, Vector2 Bow)? Chain { get; init; }

    /// <summary>渡船缆桩（河滩上陆青禾的渡船）：桩与船头。</summary>
    public (Vector2 Stake, Vector2 Bow)? Mooring { get; init; }

    public static Vector2 W(float a, float d) => WildSamples.W(a, d);

    public float GrassEdge(float a) => Shore.At(a) + Beach.At(a);

    public static readonly RiverSamples ShoreSite = new()
    {
        Site = RiverSite.Shore,
        Shore = new(40, 70, 520, 0.8f, 24, 170, 0.3f),
        Beach = new(290, 80, 430, 2f),
        WalkA0 = 110,
        WalkA1 = 3480,
        Front = 930,
        ViewA0 = 0,
        ViewA1 = 3600,
        Path = [new(-80, 800), new(180, 730), new(460, 650), new(760, 560), new(980, 470)],
        StoneMark = new(2050, -18),
        TideMark = [new(2470, 18), new(2620, 40), new(2800, 62), new(2990, 70), new(3180, 66), new(3380, 52)],
        Mooring = (new(1215, 118), new(1290, -32)),
        Boats = [new("town.prop.boat.ferry", 1330, -90, 1f)],
        Reeds =
        [
            // 醒来处上游：雨后倒伏、半泡在水里的芦苇（开场镜头“芦苇伏在水里”）。
            new(760, -40, 420, 90, 150, [6, 7, 8], 7, 1, Wet: true),
            new(380, 20, 300, 80, 210, [3, 4, 6], 5, 2),
            // 渡船下游、刻痕石两侧：沿水线的高芦苇。
            new(1720, 20, 360, 90, 230, [1, 3, 5, 9], 6, 3),
            new(2330, -40, 260, 80, 200, [4, 6, 10], 5, 4, Wet: true),
            // 潮痕钻进的芦苇根（“芦苇根下一道新鲜泥痕”）：拖痕自滩上没入这片苇丛。
            new(3150, 0, 520, 110, 240, [1, 2, 3, 7], 8, 5),
            new(3480, 150, 240, 120, 230, [2, 5, 9], 5, 6),
            // 草坡边零星几丛。
            new(1450, 470, 160, 50, 120, [5, 8], 3, 7, Blocks: false),
            new(2800, 520, 200, 60, 130, [4, 10], 3, 8, Blocks: false),
            // 压画框的前景芦苇（在可走区之外）。
            new(150, 1010, 360, 60, 280, [1, 3, 9], 5, 9, Blocks: false),
            new(1380, 1040, 300, 50, 260, [2, 4], 4, 10, Blocks: false),
            new(2650, 1020, 340, 60, 290, [1, 5, 10], 5, 11, Blocks: false),
            new(3450, 1010, 280, 60, 260, [3, 6], 4, 12, Blocks: false),
        ],
        Props =
        [
            new("town.tree.1", 430, 700, 0.95f),
            new("town.tree.3", 2330, 720, 1f, Flip: true),
            new("town.tree.6", 3260, 600, 0.9f),
            new("town.tree.2", 1760, 900, 1.05f, Flip: true),
            new("wild.rock.2", 1880, 230, 0.8f),
            new("wild.rock.5", 620, 380, 0.9f, Flip: true),
            new("wild.rock.7", 3120, 360, 0.8f),
            new("wild.rock.6", 1010, 760, 0.7f),
        ],
    };

    public static readonly RiverSamples OldFerrySite = new()
    {
        Site = RiverSite.OldFerry,
        Shore = new(90, 50, 600, 2.2f, 20, 150, 1f),
        Beach = new(280, 70, 380, 0.5f),
        WalkA0 = 110,
        WalkA1 = 3500,
        Front = 930,
        ViewA0 = 0,
        ViewA1 = 3700,
        Path = [new(480, 300), new(700, 420), new(1000, 520), new(1500, 560), new(2000, 520), new(2330, 470)],
        Jetty = new(560, 230, 620, 120, 18),
        Boats =
        [
            new("town.prop.boat.ferry", 1034, -32, 1f),
            new("town.prop.boat.2", 1520, -100, 1.25f, Flip: true),
        ],
        Chain = (new(1400, 100), new(1470, -40)),
        Levee = new(2150, 4600, -90, 130, 150, 2660, 2860, -30, new WildSteps(2260, 2400, 130, 430, 0, 150, 10), 2900),
        Reeds =
        [
            new(200, 60, 240, 90, 210, [3, 4, 6], 4, 21),
            new(1330, 10, 260, 80, 220, [1, 5, 9], 5, 22, Wet: true),
            // 河堤西端与浅滩相接处：一大片苇丛（令狐冲从这里跃出）。
            new(1990, 80, 260, 150, 250, [1, 2, 3, 7], 7, 23),
            // 水门以东的苇荡。
            new(3150, 330, 420, 200, 260, [1, 3, 5, 9], 8, 24),
            new(3500, 620, 400, 220, 250, [2, 4, 10], 7, 25),
            new(3050, 760, 300, 120, 230, [3, 6], 5, 26),
            new(1250, 560, 160, 50, 120, [5, 8], 3, 27, Blocks: false),
            new(160, 1010, 340, 60, 280, [1, 3, 9], 5, 28, Blocks: false),
            new(1500, 1030, 300, 50, 260, [2, 4], 4, 29, Blocks: false),
            new(2500, 1040, 280, 50, 270, [1, 5], 4, 30, Blocks: false),
        ],
        Props =
        [
            new("town.tree.5", 200, 700, 1f),
            new("town.tree.7", 1950, 880, 1f, Flip: true),
            new("town.tree.1", 3300, 520, 0.95f),
            new("wild.rock.5", 1900, 330, 0.85f),
            new("wild.rock.2", 820, 300, 0.75f, Flip: true),
            new("wild.rock.7", 2450, 800, 0.8f),
        ],
    };

    public static RiverSamples For(RiverSite site) => site == RiverSite.Shore ? ShoreSite : OldFerrySite;
}
