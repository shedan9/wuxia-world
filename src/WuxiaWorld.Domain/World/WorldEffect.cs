namespace WuxiaWorld.Domain.World;

/// <summary>
/// 效果原语（架构文档 9.1）：授予物品、设置事实、修改关系、推进任务、请求战斗、请求旅行等。
/// 请求类效果不在领域层执行，只交给应用层（开战、换图）；其余在事务副本上立即生效。
/// </summary>
public enum WorldEffectType
{
    /// <summary>事实 <c>id</c> = <c>value</c>。</summary>
    SetFact,

    ClearFact,

    /// <summary>获得 <c>amount</c> 个物品 <c>id</c>。</summary>
    GrantItem,

    /// <summary>交出 / 消耗物品；数量不足则整个事务失败。</summary>
    RemoveItem,

    /// <summary>银两增减；不足则事务失败。</summary>
    ChangeSilver,

    /// <summary>对人物 <c>id</c> 的 <c>axis</c> 加 <c>amount</c>（可为负）。</summary>
    ChangeRelationship,

    /// <summary>记下承诺 / 立场事实 <c>value</c>。</summary>
    AddCommitment,

    AddClue,
    MeetCharacter,

    /// <summary>接取任务 <c>id</c>；锁定、互斥组已占用或已结束时失败。</summary>
    StartQuest,

    /// <summary>完成任务 <c>id</c> 当前阶段的目标 <c>value</c>；不在该阶段时忽略（可重复触发的调查不报错）。</summary>
    CompleteObjective,

    FailQuest,
    AbandonQuest,

    JoinParty,
    LeaveParty,

    /// <summary>推进 <c>amount</c> 个时辰。</summary>
    AdvanceClock,

    LearnSkill,
    GrantExperience,

    /// <summary>写入世界关口 <c>id</c>。</summary>
    ReachGate,

    /// <summary>切换篇章：<c>id</c> 为篇，<c>value</c> 为章。</summary>
    SetChapter,

    /// <summary>请求战斗：<c>id</c> 为遭遇，<c>value</c> 为请求 ID（缺省由事务生成），胜负效果另给。</summary>
    RequestBattle,

    /// <summary>请求剧情换图：<c>id</c> 为地图，<c>value</c> 为落点。</summary>
    RequestTravel,
}

public sealed record WorldEffect
{
    public WorldEffectType Type { get; init; }
    public string? Id { get; init; }
    public string? Value { get; init; }
    public int Amount { get; init; } = 1;
    public RelationshipAxis Axis { get; init; }

    /// <summary>仅 <see cref="WorldEffectType.RequestBattle"/>：胜利、战败后的效果与能否重试。</summary>
    public IReadOnlyList<WorldEffect> OnVictory { get; init; } = [];

    public IReadOnlyList<WorldEffect> OnDefeat { get; init; } = [];
    public bool Retry { get; init; } = true;

    public static WorldEffect Fact(string id, string value) => new() { Type = WorldEffectType.SetFact, Id = id, Value = value };

    public static WorldEffect Item(string id, int amount = 1) => new() { Type = WorldEffectType.GrantItem, Id = id, Amount = amount };

    public static WorldEffect Objective(string quest, string objective) =>
        new() { Type = WorldEffectType.CompleteObjective, Id = quest, Value = objective };
}

/// <summary>给界面的提示（获得物品、任务更新等），只用于显示。</summary>
public sealed record WorldNotice(string Kind, string Id, int Amount = 0);

/// <summary>剧情换图请求。</summary>
public sealed record TravelRequest(string MapId, string? SpawnId);

/// <summary>一次事务的结果：失败原因、提示与待应用层处理的请求。</summary>
public sealed class EffectResult
{
    public string? Error { get; private set; }
    public bool Ok => Error is null;
    public List<WorldNotice> Notices { get; } = [];
    public TravelRequest? Travel { get; set; }

    /// <summary>本事务里新提交的战斗请求（同时写进 <see cref="WorldState.Battle"/>）。</summary>
    public PendingBattle? Battle { get; set; }

    public void Fail(string reason) => Error ??= reason;
}
