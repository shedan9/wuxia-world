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
}

public sealed record MapDefinition
{
    public required string Id { get; init; }
    public required string Region { get; init; }

    /// <summary>表现层场景 ID（Godot 场景路径由表现层映射）。</summary>
    public required string Scene { get; init; }

    /// <summary>安全入口：缺失落点、战败回退都回到这里。</summary>
    public required string SafeSpawn { get; init; }

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
}

public enum CharacterOrigin
{
    Hero,
    Original,
    Canon,
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
