using WuxiaWorld.Domain.Characters;

namespace WuxiaWorld.Domain.Combat.Definitions;

/// <summary>
/// 战斗装配（架构文档 8.2）：主动招式至多 6 个、主修心法 1 门、辅修心法至多 1 门、轻功至多 1 门、被动天赋至多 3 个。
/// 普通攻击、防御、调息等基础行动不占招式栏。
/// </summary>
public sealed record Loadout
{
    public const int MaxSkills = 6;
    public const int MaxTalents = 3;

    public IReadOnlyList<string> Skills { get; init; } = [];
    public string? MainArt { get; init; }
    public string? SupportArt { get; init; }
    public string? Qinggong { get; init; }
    public IReadOnlyList<string> Talents { get; init; } = [];
}

/// <summary>AI 行为档案 ID（<see cref="Ai.BattleAi"/> 识别）。</summary>
public static class AiProfiles
{
    /// <summary>由玩家下令。</summary>
    public const string Player = "ai.player";

    /// <summary>莽攻：打能打到的、预计伤害占剩余气血比例最高者，能击倒优先。</summary>
    public const string Brawler = "ai.brawler";

    /// <summary>游击：优先后排与已有流血者，用远程招。</summary>
    public const string Skirmisher = "ai.skirmisher";

    /// <summary>护卫：首领或同伴危急时护援，否则反击架势 / 攻击。</summary>
    public const string Guardian = "ai.guardian";

    /// <summary>首领唐守亭：蓄力群攻有一轮预兆，破招可打断。</summary>
    public const string BossTang = "ai.boss.tang_shouting";

    /// <summary>机关：每次行动抬一层，满层发作。</summary>
    public const string Mechanism = "ai.mechanism";

    /// <summary>木桩：只防御（教学与切磋用）。</summary>
    public const string Dummy = "ai.dummy";
}

public sealed record CombatantTemplate
{
    public required string Id { get; init; }
    public int Level { get; init; } = 1;
    public Attributes Attributes { get; init; } = new(5, 5, 5, 5, 5);
    public Loadout Loadout { get; init; } = new();

    /// <summary>装备固定加成；装备实例与槽位在 M2 的物品模块建立。</summary>
    public StatBonus Equipment { get; init; } = StatBonus.None;

    public StatOverride? Overrides { get; init; }

    /// <summary>克制与机制用标签：<c>armored</c>、<c>mechanism</c>、<c>boss</c> 等。</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    public string Ai { get; init; } = AiProfiles.Player;

    /// <summary>控制积累阈值：大于 0 时硬控制改为积累，满阈值才生效（首领，架构文档 7.3）。</summary>
    public int ControlThreshold { get; init; }

    /// <summary>是否计入“全歼敌方”的胜利条件（机关可不计入）。</summary>
    public bool CountsForVictory { get; init; } = true;

    /// <summary>表现层形象 ID（探索与战斗共用）。</summary>
    public string? ArtId { get; init; }

    public bool HasTag(string tag) => Tags.Contains(tag, StringComparer.Ordinal);
}
