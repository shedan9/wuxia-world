using System.Text.Json;
using WuxiaWorld.Domain.Characters;
using WuxiaWorld.Domain.Combat.Definitions;

namespace WuxiaWorld.Infrastructure.Content;

/// <summary>
/// 战斗内容包：内容编译器把 <c>content/</c> 下分散的源文件校验后合成一个文件，
/// 写到 <c>game/generated/content/combat.json</c>，游戏运行时只读这一份（架构文档 9.3）。
/// </summary>
public sealed record CombatBundle
{
    public const string FileName = "combat.json";

    /// <summary>内容源各文件的哈希摘要，写入战斗记录与存档用于比对（<c>ContentManifest.content_version</c>）。</summary>
    public string ContentVersion { get; init; } = "";

    public int RulesetVersion { get; init; } = StatFormula.RulesetVersion;
    public IReadOnlyList<SkillDefinition> Skills { get; init; } = [];
    public IReadOnlyList<StatusDefinition> Statuses { get; init; } = [];
    public IReadOnlyList<ArtDefinition> Arts { get; init; } = [];
    public IReadOnlyList<BattleItemDefinition> Items { get; init; } = [];
    public IReadOnlyList<CombatantTemplate> Combatants { get; init; } = [];
    public IReadOnlyList<EncounterDefinition> Encounters { get; init; } = [];
    public IReadOnlyList<CounterRule> Counters { get; init; } = [];

    /// <summary>中文文本表（键 → 显示文字）。</summary>
    public IReadOnlyDictionary<string, string> Text { get; init; } = new Dictionary<string, string>();

    public CombatContent ToContent() => new(Skills, Statuses, Arts, Items, Combatants, Encounters, Counters);

    public string Name(string id) => Text.TryGetValue(id + ".name", out var name) ? name : id;

    /// <summary>卡面、图标用的短名（<c>.short</c>，如“绊字诀”）；没有时同 <see cref="Name"/>。</summary>
    public string ShortName(string id) => Text.TryGetValue(id + ".short", out var name) ? name : Name(id);

    /// <summary>招式图标上的单字（<c>.glyph</c>，用于短名首字相同的同门招式，如破刀式“刀”、破索式“索”）；没有时取短名首字。</summary>
    public string Glyph(string id) => Text.TryGetValue(id + ".glyph", out var g) ? g : ShortName(id)[..1];

    public string? Describe(string id) => Text.TryGetValue(id + ".desc", out var desc) ? desc : null;

    public static CombatBundle Parse(string json)
    {
        var bundle = JsonSerializer.Deserialize<CombatBundle>(json, ContentJson.Options)
            ?? throw new InvalidDataException("战斗内容包为空。");
        if (bundle.RulesetVersion != StatFormula.RulesetVersion)
        {
            throw new InvalidDataException(
                $"战斗内容包的规则版本 {bundle.RulesetVersion} 与程序 {StatFormula.RulesetVersion} 不符，请重新运行内容编译器。");
        }

        return bundle;
    }

    public string Serialize() => JsonSerializer.Serialize(this, ContentJson.Options);
}
