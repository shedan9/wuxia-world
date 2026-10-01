namespace WuxiaWorld.Domain.World;

/// <summary>已校验的世界内容索引：地图、路线、地区事件、任务、对白、物品、人物与新游戏设置。</summary>
public sealed class WorldContent
{
    public WorldContent(
        IEnumerable<MapDefinition> maps,
        IEnumerable<RouteDefinition> routes,
        IEnumerable<StoryEventDefinition> events,
        IEnumerable<QuestDefinition> quests,
        IEnumerable<DialogueDefinition> dialogues,
        IEnumerable<ItemDefinition> items,
        IEnumerable<CharacterDefinition> characters,
        NewGameDefinition newGame)
    {
        Maps = Index(maps, m => m.Id);
        Routes = Index(routes, r => r.Id);
        Events = Index(events, e => e.Id);
        Quests = Index(quests, q => q.Id);
        Dialogues = Index(dialogues, d => d.Id);
        Items = Index(items, i => i.Id);
        Characters = Index(characters, c => c.Id);
        NewGame = newGame;
        EventsByMap = Events.Values.GroupBy(e => e.Map, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<StoryEventDefinition>)[.. g.OrderBy(e => e.Priority).ThenBy(e => e.Id, StringComparer.Ordinal)],
                StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, MapDefinition> Maps { get; }
    public IReadOnlyDictionary<string, RouteDefinition> Routes { get; }
    public IReadOnlyDictionary<string, StoryEventDefinition> Events { get; }
    public IReadOnlyDictionary<string, QuestDefinition> Quests { get; }
    public IReadOnlyDictionary<string, DialogueDefinition> Dialogues { get; }
    public IReadOnlyDictionary<string, ItemDefinition> Items { get; }
    public IReadOnlyDictionary<string, CharacterDefinition> Characters { get; }
    public NewGameDefinition NewGame { get; }

    /// <summary>地图 → 该图的地区事件，已按优先级与 ID 排好。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<StoryEventDefinition>> EventsByMap { get; }

    public string? RegionOf(string mapId) => Maps.TryGetValue(mapId, out var m) ? m.Region : null;

    // 重复 ID 由内容校验报告；这里保留第一份，保证索引可建。
    private static SortedDictionary<string, T> Index<T>(IEnumerable<T> items, Func<T, string> key)
    {
        var map = new SortedDictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            map.TryAdd(key(item), item);
        }

        return map;
    }
}
