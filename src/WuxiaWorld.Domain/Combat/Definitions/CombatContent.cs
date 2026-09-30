using WuxiaWorld.Domain.Characters;

namespace WuxiaWorld.Domain.Combat.Definitions;

/// <summary>
/// 战斗内核依赖的内容目录（已校验的只读定义）。字典只按 ID 查找，规则代码不枚举字典，
/// 需要次序时一律按稳定 ID 排序（架构文档 7.5）。
/// </summary>
public sealed class CombatContent
{
    public CombatContent(
        IEnumerable<SkillDefinition> skills,
        IEnumerable<StatusDefinition> statuses,
        IEnumerable<ArtDefinition> arts,
        IEnumerable<BattleItemDefinition> items,
        IEnumerable<CombatantTemplate> combatants,
        IEnumerable<EncounterDefinition> encounters,
        IEnumerable<CounterRule> counters)
    {
        Skills = ToMap(skills, s => s.Id, "招式");
        Statuses = ToMap(statuses, s => s.Id, "状态");
        Arts = ToMap(arts, a => a.Id, "心法 / 轻功 / 天赋");
        Items = ToMap(items, i => i.Id, "物品");
        Combatants = ToMap(combatants, c => c.Id, "战斗单位");
        Encounters = ToMap(encounters, e => e.Id, "遭遇");
        Counters = [.. counters];
    }

    public IReadOnlyDictionary<string, SkillDefinition> Skills { get; }
    public IReadOnlyDictionary<string, StatusDefinition> Statuses { get; }
    public IReadOnlyDictionary<string, ArtDefinition> Arts { get; }
    public IReadOnlyDictionary<string, BattleItemDefinition> Items { get; }
    public IReadOnlyDictionary<string, CombatantTemplate> Combatants { get; }
    public IReadOnlyDictionary<string, EncounterDefinition> Encounters { get; }
    public IReadOnlyList<CounterRule> Counters { get; }

    public SkillDefinition Skill(string id) => Get(Skills, id, "招式");

    public StatusDefinition Status(string id) => Get(Statuses, id, "状态");

    public ArtDefinition Art(string id) => Get(Arts, id, "心法 / 轻功 / 天赋");

    public BattleItemDefinition Item(string id) => Get(Items, id, "物品");

    public CombatantTemplate Combatant(string id) => Get(Combatants, id, "战斗单位");

    public EncounterDefinition Encounter(string id) => Get(Encounters, id, "遭遇");

    private static T Get<T>(IReadOnlyDictionary<string, T> map, string id, string kind) =>
        map.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException($"未知{kind} ID：{id}");

    private static Dictionary<string, T> ToMap<T>(IEnumerable<T> items, Func<T, string> id, string kind)
    {
        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!map.TryAdd(id(item), item))
            {
                throw new ArgumentException($"重复的{kind} ID：{id(item)}");
            }
        }

        return map;
    }
}

/// <summary>战斗内核直接识别的状态与招式 ID；内容包必须提供这些定义（内容编译器检查）。</summary>
public static class CoreIds
{
    public const string BasicAttack = "skill.basic.strike";

    public const string Defend = "status.defend";
    public const string Broken = "status.broken";
    public const string Meditating = "status.meditating";
    public const string Charging = "status.charging";
    public const string Guarded = "status.guarded";
    public const string ControlGuard = "status.control_guard";
    public const string Stunned = "status.stunned";

    public const string ControlTag = "control";

    public static readonly IReadOnlyList<string> RequiredStatuses =
        [Defend, Broken, Meditating, Charging, Guarded, ControlGuard, Stunned];
}
