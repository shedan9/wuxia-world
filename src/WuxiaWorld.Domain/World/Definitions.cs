using WuxiaWorld.Domain.Characters;

namespace WuxiaWorld.Domain.World;

// 世界、任务、对白与地图的内容定义（架构文档 6.2、9.2）。字段为蛇形小写 JSON；显示名称与说明取文本表。

public enum QuestKind
{
    Main,
    Side,
    Relationship,
}

/// <summary>目标类型只决定日志图标与追踪提示；完成与否由条件或效果决定。</summary>
public enum ObjectiveKind
{
    Talk,
    Reach,
    Investigate,
    Deliver,
    Defeat,
    Composite,
}

public sealed record QuestObjective
{
    public required string Id { get; init; }
    public ObjectiveKind Kind { get; init; }

    /// <summary>条件一旦满足自动完成；为空则只能由 <c>complete_objective</c> 效果完成。</summary>
    public Condition? When { get; init; }

    /// <summary>可选目标不阻止阶段推进。</summary>
    public bool Optional { get; init; }

    /// <summary>追踪提示所指的地图。</summary>
    public string? Map { get; init; }
}

public sealed record Branch
{
    public required Condition When { get; init; }
    public required string Next { get; init; }
}

public sealed record QuestStage
{
    public required string Id { get; init; }
    public IReadOnlyList<QuestObjective> Objectives { get; init; } = [];

    /// <summary>阶段完成时执行一次。</summary>
    public IReadOnlyList<WorldEffect> OnComplete { get; init; } = [];

    /// <summary>按次序取第一个满足条件的分支；都不满足走 <see cref="Next"/>；<see cref="Next"/> 也为空则任务完成。</summary>
    public IReadOnlyList<Branch> Branches { get; init; } = [];

    public string? Next { get; init; }
}

public sealed record QuestDefinition
{
    public required string Id { get; init; }
    public QuestKind Kind { get; init; }
    public string? Region { get; init; }

    /// <summary>进入这些地图时评估是否可接（架构文档 9.1 地点触发）。</summary>
    public IReadOnlyList<string> TriggerMaps { get; init; } = [];

    public Condition? Prerequisites { get; init; }

    /// <summary>未满足条件时在札记中可见的方向提示（文本键）。</summary>
    public IReadOnlyList<string> Hints { get; init; } = [];

    public int Priority { get; init; }
    public IReadOnlyList<QuestStage> Stages { get; init; } = [];

    /// <summary>主线保底线索：必须有一条不依赖支线的效果授予它，内容校验检查。</summary>
    public string? GuaranteedClue { get; init; }

    public IReadOnlyList<WorldEffect> Rewards { get; init; } = [];

    /// <summary>同组任务互斥：已有一条进行中或已完成时不能再接。</summary>
    public string? ExclusiveGroup { get; init; }

    /// <summary>限时任务接取时给出的期限提示（文本键）；没有期限提示的任务不得失败于时间。</summary>
    public string? ExpiryNotice { get; init; }

    /// <summary>失败或放弃后回收人物与线索的地点。</summary>
    public string? RecoveryMap { get; init; }

    public IReadOnlyList<WorldEffect> OnFail { get; init; } = [];
    public IReadOnlyList<WorldEffect> OnAbandon { get; init; } = [];

    public QuestStage? StageById(string? id) => Stages.FirstOrDefault(s => s.Id == id);
}

public enum DialogueNodeType
{
    /// <summary>一句台词，读完走 <c>next</c>。</summary>
    Line,

    /// <summary>选项；条件不满足的选项隐藏，给了 <c>locked_hint</c> 的显示为不可选。</summary>
    Choice,

    /// <summary>条件分流：取第一个满足条件的分支，否则走 <c>next</c>。</summary>
    Branch,

    /// <summary>执行效果后走 <c>next</c>。</summary>
    Effect,

    Jump,
    End,

    /// <summary>
    /// 演出提示：镜头、人物动作、物件特写或章节标题卡，由画面表现，不朗读、不配音。
    /// 与台词一样停下等表现层播完再继续；不写入对话记录。2026-10-01 起取代旁白。
    /// </summary>
    Stage,
}

/// <summary>演出提示的种类。</summary>
public enum StageKind
{
    /// <summary>场景建立：地点、天色、镜头起落。</summary>
    Scene,

    /// <summary>人物动作与走位。</summary>
    Action,

    /// <summary>物件特写；可带 <c>caption</c> 显示物件上的文字（告示、名单等）。</summary>
    Closeup,

    /// <summary>章节标题卡；<c>caption</c> 必填。</summary>
    Title,
}

public sealed record DialogueOption
{
    /// <summary>选项文字的稳定 ID；选中后若主角开口，配音沿用同一 ID。</summary>
    public required string LineId { get; init; }

    public required string Text { get; init; }
    public Condition? When { get; init; }
    public string? LockedHint { get; init; }
    public IReadOnlyList<WorldEffect> Effects { get; init; } = [];
    public required string Next { get; init; }
}

public sealed record DialogueNode
{
    public required string Id { get; init; }
    public DialogueNodeType Type { get; init; }

    /// <summary>台词稳定 ID（<c>line_id</c>），配音清单与字幕按它对应。</summary>
    public string? LineId { get; init; }

    /// <summary>说话人：人物 ID。不设旁白，画面能交代的交给演出提示。</summary>
    public string? Speaker { get; init; }

    public string? Text { get; init; }

    /// <summary>立绘表情 / 语气标签（表现层与配音参考）。</summary>
    public string? Mood { get; init; }

    /// <summary>
    /// 心里话：只显示字幕、不配音，只允许主角使用，单句不超过 <c>InnerMaxChars</c> 字，不连续出现（2026-10-01 用户要求）。
    /// 文字不带括号，由表现层以心里话样式显示。
    /// </summary>
    public bool Inner { get; init; }

    /// <summary>心里话单句字数上限。</summary>
    public const int InnerMaxChars = 30;

    public string? Next { get; init; }
    public IReadOnlyList<DialogueOption> Options { get; init; } = [];
    public IReadOnlyList<Branch> Branches { get; init; } = [];
    public IReadOnlyList<WorldEffect> Effects { get; init; } = [];

    /// <summary>效果是否每次进入都执行；默认只结算一次（重进对话不重复发奖）。</summary>
    public bool Repeatable { get; init; }

    /// <summary>演出提示的种类（仅 <see cref="DialogueNodeType.Stage"/>）。</summary>
    public StageKind? Kind { get; init; }

    /// <summary>给美术、动画与镜头的制作说明；玩家看不到。</summary>
    public string? Direction { get; init; }

    /// <summary>画面上显示的物件文字或标题（特写、标题卡可用），不配音。</summary>
    public string? Caption { get; init; }

    /// <summary><see cref="Caption"/> 的稳定 ID，与 <c>line_id</c> 共用唯一性检查。</summary>
    public string? CaptionId { get; init; }
}

public sealed record DialogueDefinition
{
    public required string Id { get; init; }

    /// <summary><c>draft</c>（未锁稿）或 <c>locked</c>（锁稿，可配音）。</summary>
    public string Status { get; init; } = "draft";

    public required string Entry { get; init; }
    public IReadOnlyList<DialogueNode> Nodes { get; init; } = [];

    public DialogueNode? Node(string? id) => Nodes.FirstOrDefault(n => n.Id == id);
}

/// <summary>战斗喊声的触发时机（架构文档 10.5“战斗语音”）。</summary>
public enum BarkTrigger
{
    /// <summary>开战：全场只挑一句。</summary>
    BattleStart,

    /// <summary>施展 <c>skill</c>（反击不算）。</summary>
    Skill,

    /// <summary>开始蓄力（首领预兆）；给了 <c>skill</c> 时只认该招。</summary>
    Charge,

    /// <summary>遭遇阶段 <c>phase</c> 发动。</summary>
    Phase,

    /// <summary>本人气血首次跌到三成以下（切磋点到为止，不说）。</summary>
    LowHp,

    /// <summary>本人的蓄力被打断（破招、点穴）。</summary>
    Interrupted,

    /// <summary>本人倒下。</summary>
    Downed,

    /// <summary>我方胜利：从仍站着的人里挑一句。</summary>
    Victory,
}

/// <summary>
/// 一句战斗喊声（“关键战斗语音”，开发计划 M3-06）：与对白一样有稳定 <c>line_id</c>、说话人与字幕，按章保存在对白目录
/// （<c>content/dialogue/arcXX/chapterXX_battle.json</c>）。由 <c>BattleBarkDirector</c> 按战斗事件挑选，只是表现，不改变战果。
/// </summary>
public sealed record BattleBarkDefinition
{
    public required string LineId { get; init; }

    /// <summary>说话人：人物 ID（字幕名与配音按它取）。</summary>
    public required string Speaker { get; init; }

    /// <summary>说话人在战斗里的单位 ID；我方单位 ID 就是人物 ID，可省略。</summary>
    public string? Unit { get; init; }

    public BarkTrigger Trigger { get; init; }

    /// <summary>只在该遭遇里说；省略为任何遭遇。</summary>
    public string? Encounter { get; init; }

    /// <summary><see cref="BarkTrigger.Skill"/> 必填、<see cref="BarkTrigger.Charge"/> 可选的招式 ID。</summary>
    public string? Skill { get; init; }

    /// <summary><see cref="BarkTrigger.Phase"/> 的阶段 ID。</summary>
    public string? Phase { get; init; }

    /// <summary>1 普通（招式）、2 重要（重伤、倒下）、3 关键（开战、首领预兆、阶段、胜利）。倍速时只说 3；正在说的不被同级或更低的打断。</summary>
    public int Priority { get; init; } = 1;

    /// <summary>
    /// 同一句再说要隔的轮数；0 为一场只说一次。只对招式有意义，其余时机本来就一场一次。
    /// </summary>
    public int CooldownRounds { get; init; }

    public required string Text { get; init; }

    /// <summary>喊声字幕的字数上限：短促，一口气说完。</summary>
    public const int MaxChars = 20;

    public const int MinPriority = 1;
    public const int MaxPriority = 3;

    public string UnitId => Unit ?? Speaker;
}

public sealed record MapExit
{
    public required string Id { get; init; }
    public required string To { get; init; }
    public required string Spawn { get; init; }
    public Condition? When { get; init; }
    public string? LockedHint { get; init; }
}

public enum InteractableKind
{
    Inspect,
    Talk,
    Pickup,

    /// <summary>店铺：打开 <see cref="MapInteractable.Shop"/> 的买卖面板，不走对白。</summary>
    Shop,
}

public sealed record MapInteractable
{
    public required string Id { get; init; }
    public InteractableKind Kind { get; init; }
    public Condition? When { get; init; }
    public string? Dialogue { get; init; }
    public IReadOnlyList<WorldEffect> Effects { get; init; } = [];

    /// <summary>一次性交互（宝箱、拾取）：用过记入地图差异，重进地图不刷新。</summary>
    public bool Once { get; init; }

    /// <summary>店铺 ID（仅 <see cref="InteractableKind.Shop"/>）。</summary>
    public string? Shop { get; init; }
}

public sealed record MapDefinition
{
    public required string Id { get; init; }
    public required string Region { get; init; }

    /// <summary>表现层场景 ID（Godot 场景路径由表现层映射）。</summary>
    public required string Scene { get; init; }

    /// <summary>安全入口：缺失落点、战败回退都回到这里。</summary>
    public required string SafeSpawn { get; init; }

    /// <summary>城镇：安全的人居之地，开放洗点等养成设施（架构文档 8.2）。</summary>
    public bool Town { get; init; }

    public IReadOnlyList<string> Spawns { get; init; } = [];
    public IReadOnlyList<MapExit> Exits { get; init; } = [];
    public IReadOnlyList<MapInteractable> Interactables { get; init; } = [];
}

/// <summary>地区事件的优先级：主线紧急 → 已接限时 → 普通地区故事；同级按 ID（架构文档 9.1）。</summary>
public enum EventPriority
{
    MainUrgent,
    Timed,
    Regional,
}

public sealed record StoryEventDefinition
{
    public required string Id { get; init; }
    public required string Map { get; init; }
    public EventPriority Priority { get; init; } = EventPriority.Regional;
    public Condition? When { get; init; }

    /// <summary>参与人物：事件开始时预留，结束时释放；有人被其他事件占用则不展示。</summary>
    public IReadOnlyList<string> Participants { get; init; } = [];

    public required string Dialogue { get; init; }

    /// <summary>入口显示在哪个交互物或落点附近（表现层用）。</summary>
    public string? Anchor { get; init; }

    /// <summary>进入地图即自动开始（过场）；否则显示交互入口。</summary>
    public bool Auto { get; init; }

    public bool Once { get; init; } = true;
}

public enum TravelMode
{
    Walk,
    Horse,
    Carriage,
    Ferry,
    Story,
}

public sealed record RouteMode
{
    public TravelMode Mode { get; init; }
    public int Silver { get; init; }

    /// <summary>消耗的时辰数。</summary>
    public int Ticks { get; init; }

    public Condition? When { get; init; }
}

public sealed record RouteEncounter
{
    /// <summary>途中事件（<see cref="StoryEventDefinition"/>）；须满足其条件才参与抽取。</summary>
    public required string Event { get; init; }

    public int ChanceBp { get; init; }
}

public sealed record RouteDefinition
{
    public required string Id { get; init; }
    public string? Region { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }
    public required string Spawn { get; init; }
    public IReadOnlyList<RouteMode> Modes { get; init; } = [];
    public Condition? When { get; init; }
    public string? LockedHint { get; init; }
    public IReadOnlyList<RouteEncounter> Encounters { get; init; } = [];
}

/// <summary>
/// 江湖大地图（架构文档 6.1、6.5）：地标节点与图上画出的道路。节点把若干小地图归为一处目的地，
/// 旅行仍走 <see cref="RouteDefinition"/>（起讫为小地图）；道路只供表现层描线，不决定能否通行。
/// 坐标为设计稿坐标，范围 <see cref="Frame"/>，表现层按自己的画布尺寸缩放。
/// </summary>
public sealed record WorldMapDefinition
{
    /// <summary>设计稿尺寸 [宽, 高]。</summary>
    public IReadOnlyList<int> Frame { get; init; } = [];

    public IReadOnlyList<WorldNodeDefinition> Nodes { get; init; } = [];
    public IReadOnlyList<WorldRoadDefinition> Roads { get; init; } = [];
}

/// <summary>大地图图标种类（表现层映射为地标小图）。</summary>
public enum WorldNodeIcon
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

/// <summary>
/// 大地图地标。<see cref="Maps"/> 为属于此处的小地图（每张小地图至多属于一处），为空表示尚无可进入的场景、只在图上预告；
/// <see cref="When"/> 为地标在图上出现的条件（缺省一直可见）；<see cref="LockedHint"/> 为不能前往时写明的开放条件（文本键）。
/// </summary>
public sealed record WorldNodeDefinition
{
    public required string Id { get; init; }
    public required string Region { get; init; }
    public WorldNodeIcon Icon { get; init; }

    /// <summary>设计稿坐标 [x, y]。</summary>
    public IReadOnlyList<int> Pos { get; init; } = [];

    public IReadOnlyList<string> Maps { get; init; } = [];
    public Condition? When { get; init; }
    public string? LockedHint { get; init; }
}

public enum RoadKind
{
    Land,
    Water,
}

/// <summary>图上两处地标之间画出的一段路：陆路（步行、骑马、马车）或水路（渡船）；<see cref="Via"/> 为途经的弯折点。</summary>
public sealed record WorldRoadDefinition
{
    public required string From { get; init; }
    public required string To { get; init; }
    public RoadKind Kind { get; init; }
    public IReadOnlyList<IReadOnlyList<int>> Via { get; init; } = [];
}

public enum ItemCategory
{
    Weapon,
    Armor,
    Boots,
    Accessory,
    Charm,
    Medicine,
    Material,
    Quest,
    Misc,
}

public sealed record ItemDefinition
{
    public required string Id { get; init; }
    public ItemCategory Category { get; init; }

    /// <summary>单格堆叠上限；装备为 1。</summary>
    public int Stack { get; init; } = 99;

    public int Price { get; init; }

    /// <summary>主线必要物品：不可丢弃、出售（架构文档 9.1）。</summary>
    public bool Key { get; init; }

    /// <summary>装备的固定加成（架构文档 8.4：每件 1–2 个功能词条，不做随机词条）。</summary>
    public StatBonus Bonus { get; init; } = StatBonus.None;

    /// <summary>装备槽；非装备为 null。</summary>
    public EquipSlot? Slot => Category switch
    {
        ItemCategory.Weapon => EquipSlot.Weapon,
        ItemCategory.Armor => EquipSlot.Armor,
        ItemCategory.Boots => EquipSlot.Boots,
        ItemCategory.Accessory => EquipSlot.Accessory,
        ItemCategory.Charm => EquipSlot.Charm,
        _ => null,
    };
}

/// <summary>装备首发 5 槽（架构文档 8.4）：武器、衣甲、鞋、饰物、护符。</summary>
public enum EquipSlot
{
    Weapon,
    Armor,
    Boots,
    Accessory,
    Charm,
}

public sealed record ShopEntry
{
    public required string Item { get; init; }

    /// <summary>售价；缺省取物品目录价。</summary>
    public int? Price { get; init; }
}

/// <summary>店铺：货单与收购折率。货单不限量（本阶段只有常规药品、干粮与基础装备，关键秘籍不在店里卖）。</summary>
public sealed record ShopDefinition
{
    public required string Id { get; init; }
    public IReadOnlyList<ShopEntry> Stock { get; init; } = [];

    /// <summary>收购价 = 目录价 × 折率（万分比，向下取整）；主线必要物品与无价物品不收。</summary>
    public int BuyBackBp { get; init; } = 5000;
}

/// <summary>主角流派的整套入门武学与推荐潜能分配（讨教三选一；旧档补齐与“推荐分配”按钮用）。</summary>
public sealed record StyleDefinition
{
    /// <summary>与事实 <c>fact.hero.style</c> 的取值一致：<c>sword</c>、<c>fist</c>、<c>inner</c>。</summary>
    public required string Id { get; init; }

    public IReadOnlyList<string> Skills { get; init; } = [];

    /// <summary>心法、轻功与天赋。</summary>
    public IReadOnlyList<string> Arts { get; init; } = [];

    /// <summary>推荐的潜能分配比例（按此权重分配可用潜能）。</summary>
    public Attributes Recommended { get; init; } = new(1, 1, 1, 1, 1);
}

/// <summary>旧档追赶：存档来自加入成长系统之前时，满足条件即把经验与修为补到下限（只在迁移后的首次读取执行）。</summary>
public sealed record CatchUpDefinition
{
    public required Condition When { get; init; }
    public int Experience { get; init; }
    public int Cultivation { get; init; }
}

/// <summary>
/// 成长设置（架构文档 8.1–8.2）：等级经验表、主角基础属性、武学熟练度与流派。
/// 经验累计不清零，等级由累计经验换算；每升一级得 <see cref="StatFormula.PotentialPerLevel"/> 点潜能。
/// </summary>
public sealed record ProgressionDefinition
{
    /// <summary>可由玩家养成的人物（目前只有主角）。</summary>
    public string Hero { get; init; } = "char.hero";

    /// <summary>主角 1 级、未分配潜能时的五项属性。</summary>
    public Attributes BaseAttributes { get; init; } = new(5, 5, 5, 5, 5);

    /// <summary>主角的战斗形象 ID。</summary>
    public string? ArtId { get; init; }

    /// <summary>升到第 2、3……级所需的累计经验；长度 = 等级上限 − 1。</summary>
    public IReadOnlyList<int> Experience { get; init; } = [];

    /// <summary>熟练度从第 n 阶升到 n+1 阶的修为消耗；长度 = 最高阶 − 1。</summary>
    public IReadOnlyList<int> MasteryCosts { get; init; } = [];

    /// <summary>熟练度每高一阶，该招式的效果强度增加的万分比（第 1 阶为基准）。</summary>
    public int MasteryPowerBp { get; init; } = 500;

    /// <summary>在城镇洗点一次的银两 = 等级 × 此值（至少 1 两）；0 表示不开放洗点。</summary>
    public int RespecSilverPerLevel { get; init; }

    public IReadOnlyList<StyleDefinition> Styles { get; init; } = [];

    /// <summary>新游戏时主角身上的装备（同时计入行囊再装上）。</summary>
    public IReadOnlyList<string> StartingEquipment { get; init; } = [];

    public IReadOnlyList<CatchUpDefinition> CatchUp { get; init; } = [];

    public int MaxLevel => Experience.Count + 1;
    public int MaxMastery => MasteryCosts.Count + 1;
}

public enum CharacterOrigin
{
    Hero,
    Original,
    Canon,
}

/// <summary>剧情锚点的核验程度。</summary>
public enum AnchorStatus
{
    /// <summary>已查原著文本（网络文本），出版版本尚未逐字对校。</summary>
    TextChecked,

    /// <summary>已按选定出版版本对校章节与引文。</summary>
    Verified,
}

/// <summary>
/// 经典人物的原著剧情锚点（架构文档 2.1；考据档案见 docs/canon/ANCHORS.md）：所据文本、章节区间、原文年龄依据、
/// 本作采用的年龄范围、身份、锚点前已发生的经历与尚未发生、人物不得预知的原著事件。
/// 本作改编年龄（<see cref="AgeAdapted"/>）须写明改编说明，并与原文年龄分开记录，不得冒称原著设定。
/// </summary>
public sealed record StoryAnchorDefinition
{
    public required string Id { get; init; }
    public required string Character { get; init; }
    public required string Work { get; init; }

    /// <summary>所据文本与版本说明（例：网络文本、出版版本待对校）。</summary>
    public string TextSource { get; init; } = "";

    /// <summary>选定的章节区间。</summary>
    public string Chapters { get; init; } = "";

    /// <summary>原文年龄依据：引文与回目。</summary>
    public string CanonAge { get; init; } = "";

    /// <summary>本作采用的年龄范围（岁）。</summary>
    public int AgeMin { get; init; }
    public int AgeMax { get; init; }

    /// <summary>本作年龄与原文不同（用户决定的改编）。</summary>
    public bool AgeAdapted { get; init; }

    /// <summary>改编说明：原文年龄、本作年龄与决定日期；<see cref="AgeAdapted"/> 时必填。</summary>
    public string? Adaptation { get; init; }

    public string Identity { get; init; } = "";

    /// <summary>锚点前已发生、人物知道的原著经历。</summary>
    public IReadOnlyList<string> Known { get; init; } = [];

    /// <summary>锚点之后才发生、人物不得预知的原著事件。</summary>
    public IReadOnlyList<string> NotYet { get; init; } = [];

    public AnchorStatus Status { get; init; }

    /// <summary>仍待核对或待设计的事项。</summary>
    public IReadOnlyList<string> OpenItems { get; init; } = [];

    /// <summary>本作年龄已成年（可配置恋爱节点的前提之一）。</summary>
    public bool Adult => AgeMin >= 18;
}

/// <summary>人物定义。经典人物须引用原著剧情锚点；锚点未核验时为 <c>pending</c>，不得开放恋爱或改写已知经历。</summary>
public sealed record CharacterDefinition
{
    public required string Id { get; init; }
    public CharacterOrigin Origin { get; init; }
    public string? SourceWork { get; init; }

    /// <summary>剧情锚点 ID；<c>pending</c> 表示考据未完成。原创人物可空。</summary>
    public string? StoryAnchor { get; init; }

    /// <summary>战斗模板（未参战的人物为空）。</summary>
    public string? Combatant { get; init; }

    /// <summary>同行身份（架构文档 8.4）：能入队的人物必填；不入队的人物为 <see cref="PartyRole.None"/>。</summary>
    public PartyRole Party { get; init; }
}

/// <summary>
/// 人物与队伍的关系（架构文档 8.4、9.4.4）。切磋对象与导师属后续篇章，届时再加。
/// </summary>
public enum PartyRole
{
    /// <summary>不入队。</summary>
    None,

    /// <summary>暂时同行：随事件入队与离队，实力随其原著阶段与角色模板，不随主角成长。</summary>
    Temporary,

    /// <summary>可招募伙伴：随主角成长，离队期间有限追赶（见 <see cref="PartyRules"/>）。</summary>
    Recruitable,
}

/// <summary>新游戏的初始世界。</summary>
public sealed record NewGameDefinition
{
    public required string Arc { get; init; }
    public required string Chapter { get; init; }
    public required string Map { get; init; }
    public required string Spawn { get; init; }
    public IReadOnlyList<string> Party { get; init; } = [];
    public int Silver { get; init; }
    public IReadOnlyList<WorldEffect> Effects { get; init; } = [];

    /// <summary>世界随机流的种子；固定值使新游戏可复现，正式版可改为开局时由应用层给出。</summary>
    public ulong Seed { get; init; }
}
