using System.Text;
using System.Text.Json;
using WuxiaWorld.Domain.World;

namespace WuxiaWorld.Infrastructure.Content;

/// <summary>一个章节对白文件（<c>content/dialogue/arcXX/chapterXX.json</c>）：该章全部对白的唯一可编辑来源。</summary>
public sealed record DialogueChapter
{
    public required string Arc { get; init; }
    public required string Chapter { get; init; }

    /// <summary>整章状态说明，如“第一稿，未锁稿”。</summary>
    public string Status { get; init; } = "";

    public IReadOnlyList<DialogueDefinition> Dialogues { get; init; } = [];
}

/// <summary>
/// 世界内容包：地图、路线、地区事件、任务、对白、物品目录、人物、店铺、成长设置与新游戏设置，
/// 由内容编译器合成为 <c>game/generated/content/world.json</c>（架构文档 9.3）。
/// </summary>
public sealed record WorldBundle
{
    public const string FileName = "world.json";

    public string ContentVersion { get; init; } = "";
    public NewGameDefinition? NewGame { get; init; }
    public IReadOnlyList<MapDefinition> Maps { get; init; } = [];
    public IReadOnlyList<RouteDefinition> Routes { get; init; } = [];
    public IReadOnlyList<StoryEventDefinition> Events { get; init; } = [];
    public IReadOnlyList<QuestDefinition> Quests { get; init; } = [];
    public IReadOnlyList<DialogueChapter> Chapters { get; init; } = [];
    public IReadOnlyList<ItemDefinition> Items { get; init; } = [];
    public IReadOnlyList<CharacterDefinition> Characters { get; init; } = [];

    /// <summary>经典人物的原著剧情锚点（<c>characters/anchors.json</c>），只供内容校验与审阅，运行时规则不读取。</summary>
    public IReadOnlyList<StoryAnchorDefinition> Anchors { get; init; } = [];
    public IReadOnlyList<ShopDefinition> Shops { get; init; } = [];

    /// <summary>成长设置（<c>world/progression.json</c>）。</summary>
    public ProgressionDefinition? Progression { get; init; }

    /// <summary>中文文本表：与战斗包读取同一批文件，世界包单独加载时也能显示名称。</summary>
    public IReadOnlyDictionary<string, string> Text { get; init; } = new Dictionary<string, string>();

    public IEnumerable<DialogueDefinition> Dialogues => Chapters.SelectMany(c => c.Dialogues);

    public WorldContent ToContent() =>
        new(Maps, Routes, Events, Quests, Dialogues, Items, Characters, NewGame ?? throw new InvalidDataException("缺少新游戏设置 world/new_game.json"),
            Progression ?? throw new InvalidDataException("缺少成长设置 world/progression.json"), Shops);

    public string Name(string id) => Text.TryGetValue(id + ".name", out var name) ? name : id;

    public static WorldBundle Parse(string json) =>
        JsonSerializer.Deserialize<WorldBundle>(json, ContentJson.Options) ?? throw new InvalidDataException("世界内容包为空。");

    public string Serialize() => JsonSerializer.Serialize(this, ContentJson.Options);
}

/// <summary>从内容源目录读取世界内容（目录约定见 content/README.md）。</summary>
public static class WorldContentLoader
{
    public static WorldBundle LoadDirectory(string contentRoot)
    {
        var root = Path.GetFullPath(contentRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"找不到内容目录 {root}");
        }

        NewGameDefinition? newGame = null;
        ProgressionDefinition? progression = null;
        var shops = new List<ShopDefinition>();
        var maps = new List<MapDefinition>();
        var routes = new List<RouteDefinition>();
        var events = new List<StoryEventDefinition>();
        var quests = new List<QuestDefinition>();
        var chapters = new List<DialogueChapter>();
        var items = new List<ItemDefinition>();
        var characters = new List<CharacterDefinition>();
        var anchors = new List<StoryAnchorDefinition>();
        var text = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in ContentFiles.Enumerate(root))
        {
            var json = File.ReadAllText(Path.Combine(root, file), Encoding.UTF8);
            try
            {
                switch (file.Split('/'))
                {
                    case ["world", "new_game.json"]:
                        newGame = Read<NewGameDefinition>(json);
                        break;
                    case ["world", "progression.json"]:
                        progression = Read<ProgressionDefinition>(json);
                        break;
                    case ["regions", _, "shops", ..]:
                        shops.AddRange(Read<List<ShopDefinition>>(json));
                        break;
                    case ["characters", "characters.json"]:
                        characters.AddRange(Read<List<CharacterDefinition>>(json));
                        break;
                    case ["characters", "anchors.json"]:
                        anchors.AddRange(Read<List<StoryAnchorDefinition>>(json));
                        break;
                    case ["shared", "items", "catalog.json"]:
                        items.AddRange(Read<List<ItemDefinition>>(json));
                        break;
                    case ["shared", "text", "zh-Hans.json"]:
                    case ["regions", _, "text", "zh-Hans.json"]:
                        ContentFiles.MergeText(text, json);
                        break;
                    case ["regions", _, "maps", ..]:
                        maps.AddRange(Read<List<MapDefinition>>(json));
                        break;
                    case ["regions", _, "routes", ..]:
                        routes.AddRange(Read<List<RouteDefinition>>(json));
                        break;
                    case ["regions", _, "events", ..]:
                        events.AddRange(Read<List<StoryEventDefinition>>(json));
                        break;
                    case ["regions", _, "quests", ..]:
                        quests.AddRange(Read<List<QuestDefinition>>(json));
                        break;
                    case ["dialogue", _, _]:
                        chapters.Add(Read<DialogueChapter>(json));
                        break;
                    default:
                        // 战斗内容由 CombatContentLoader 读取。
                        continue;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                throw new InvalidDataException($"{file}: {ex.Message}", ex);
            }
        }

        return new WorldBundle
        {
            ContentVersion = ContentFiles.Version(root),
            NewGame = newGame, Progression = progression, Shops = shops, Maps = maps, Routes = routes, Events = events, Quests = quests,
            Chapters = chapters, Items = items, Characters = characters, Anchors = anchors, Text = text,
        };
    }

    private static T Read<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, ContentJson.Options) ?? throw new InvalidDataException("文件内容为空。");
}
