using WuxiaWorld.Domain.Combat.Definitions;

namespace WuxiaWorld.Domain.World;

/// <summary>
/// 剧情战的开局变体由世界事实决定（同行者先手、支线结果，架构文档 7.6）：开战时现算，不进存档；
/// 同一存档重开同一场时事实不变，变体也就不变。
/// </summary>
public static class EncounterVariants
{
    public static IReadOnlyList<string> Active(EncounterDefinition encounter, WorldState s) =>
        [.. encounter.Variants.Where(v => s.Facts.TryGetValue(v.WhenFact, out var value) && value == v.WhenValue).Select(v => v.Id)];
}
