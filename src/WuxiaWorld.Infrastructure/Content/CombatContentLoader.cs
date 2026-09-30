using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WuxiaWorld.Domain.Characters;
using WuxiaWorld.Domain.Combat.Definitions;

namespace WuxiaWorld.Infrastructure.Content;

/// <summary>
/// 从内容源目录读取战斗相关定义（目录约定见 content/README.md）。文件按相对路径的序数次序读取，
/// 内容版本为各文件“路径 + 内容”的 SHA-256 前 16 位十六进制。
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
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                     .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                     .Order(StringComparer.Ordinal))
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
                    case ["shared", "items", ..]:
                        items.AddRange(Read<List<BattleItemDefinition>>(json));
                        break;
                    case ["shared", "combat", "counters.json"]:
                        counters.AddRange(Read<List<CounterRule>>(json));
                        break;
                    case ["shared", "text", "zh-Hans.json"]:
                        foreach (var (k, v) in Read<Dictionary<string, string>>(json))
                        {
                            if (!text.TryAdd(k, v))
                            {
                                throw new InvalidDataException($"文本键重复：{k}");
                            }
                        }

                        break;
                    case ["characters", "combat_presets.json"]:
                    case ["regions", _, "combatants", ..]:
                        combatants.AddRange(Read<List<CombatantTemplate>>(json));
                        break;
                    case ["regions", _, "encounters", ..]:
                        encounters.AddRange(Read<List<EncounterDefinition>>(json));
                        break;
                    default:
                        // 其他内容（地图、任务、对白）由 M2 起的编译步骤处理。
                        continue;
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"{file}: {ex.Message}", ex);
            }

            sha.AppendData(Encoding.UTF8.GetBytes(file + "\n"));
            sha.AppendData(Encoding.UTF8.GetBytes(json.Replace("\r\n", "\n", StringComparison.Ordinal)));
        }

        return new CombatBundle
        {
            ContentVersion = Convert.ToHexStringLower(sha.GetHashAndReset())[..16],
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
