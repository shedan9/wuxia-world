using System.Text;
using System.Text.Json;
using WuxiaWorld.Domain.Characters;
using WuxiaWorld.Domain.Combat.Definitions;

namespace WuxiaWorld.Infrastructure.Content;

/// <summary>
/// 从内容源目录读取战斗相关定义（目录约定见 content/README.md）。文件按相对路径的序数次序读取，
/// 内容版本见 <see cref="ContentFiles.Version"/>（战斗与世界内容包共用同一个版本）。
/// </summary>
public static class CombatContentLoader
{
    public static CombatBundle LoadDirectory(string contentRoot)
    {
        var root = Path.GetFullPath(contentRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"找不到内容目录 {root}");
        }

        var skills = new List<SkillDefinition>();
        var statuses = new List<StatusDefinition>();
        var arts = new List<ArtDefinition>();
        var items = new List<BattleItemDefinition>();
        var combatants = new List<CombatantTemplate>();
        var encounters = new List<EncounterDefinition>();
        var counters = new List<CounterRule>();
        var text = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in ContentFiles.Enumerate(root))
        {
            var json = File.ReadAllText(Path.Combine(root, file), Encoding.UTF8);
            var parts = file.Split('/');
            try
            {
                switch (parts)
                {
                    case ["shared", "skills", ..]:
                        skills.AddRange(Read<List<SkillDefinition>>(json));
                        break;
                    case ["shared", "statuses", ..]:
                        statuses.AddRange(Read<List<StatusDefinition>>(json));
                        break;
                    case ["shared", "arts", ..]:
                        arts.AddRange(Read<List<ArtDefinition>>(json));
                        break;
                    case ["shared", "items", "battle.json"]:
                        items.AddRange(Read<List<BattleItemDefinition>>(json));
                        break;
                    case ["shared", "combat", "counters.json"]:
                        counters.AddRange(Read<List<CounterRule>>(json));
                        break;
                    case ["shared", "text", "zh-Hans.json"]:
                    case ["regions", _, "text", "zh-Hans.json"]:
                        ContentFiles.MergeText(text, json);
                        break;
                    case ["characters", "combat_presets.json"]:
                    case ["regions", _, "combatants", ..]:
                        combatants.AddRange(Read<List<CombatantTemplate>>(json));
                        break;
                    case ["regions", _, "encounters", ..]:
                        encounters.AddRange(Read<List<EncounterDefinition>>(json));
                        break;
                    default:
                        // 其他内容（地图、任务、对白等）由 WorldContentLoader 读取。
                        continue;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                throw new InvalidDataException($"{file}: {ex.Message}", ex);
            }
        }

        return new CombatBundle
        {
            ContentVersion = ContentFiles.Version(root),
            Skills = skills,
            Statuses = statuses,
            Arts = arts,
            Items = items,
            Combatants = combatants,
            Encounters = encounters,
            Counters = counters,
            Text = text,
        };
    }

    private static T Read<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, ContentJson.Options) ?? throw new InvalidDataException("文件内容为空。");
}
