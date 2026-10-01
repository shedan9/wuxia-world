using WuxiaWorld.Domain.World;

namespace WuxiaWorld.Application.Persistence;

public enum SlotKind
{
    Manual,
    Quick,
    Auto,
}

/// <summary>存档槽（架构文档 11）：10 个手动槽、1 个快速槽、3 个轮换自动槽。</summary>
public readonly record struct SaveSlot(SlotKind Kind, int Index)
{
    public const int ManualSlots = 10;
    public const int AutoSlots = 3;

    public static SaveSlot Manual(int index) => new(SlotKind.Manual, index);

    public static SaveSlot Quick => new(SlotKind.Quick, 1);

    public static SaveSlot Auto(int index) => new(SlotKind.Auto, index);

    public bool IsValid => Kind switch
    {
        SlotKind.Manual => Index is >= 1 and <= ManualSlots,
        SlotKind.Quick => Index == 1,
        SlotKind.Auto => Index is >= 1 and <= AutoSlots,
        _ => false,
    };

    /// <summary>文件名主干（不含扩展名），如 <c>manual_01</c>、<c>quick</c>、<c>auto_2</c>。</summary>
    public string Stem => Kind switch
    {
        SlotKind.Manual => $"manual_{Index:00}",
        SlotKind.Quick => "quick",
        _ => $"auto_{Index}",
    };

    public static IEnumerable<SaveSlot> All =>
        Enumerable.Range(1, ManualSlots).Select(Manual).Append(Quick).Concat(Enumerable.Range(1, AutoSlots).Select(Auto));
}

/// <summary>存档头：版本、内容与规则版本、显示用摘要。时间戳只做显示，不参与规则计算。</summary>
public sealed record SaveHeader
{
    public int SaveSchemaVersion { get; init; }
    public string GameVersion { get; init; } = "";
    public string ContentVersion { get; init; } = "";
    public int RulesetVersion { get; init; }

    /// <summary>写入时的本地时间（ISO 8601），仅供槽位卡片显示。</summary>
    public string CreatedAt { get; init; } = "";

    /// <summary>全局递增的写入序号；自动槽轮换按它找最旧的一份。</summary>
    public long Sequence { get; init; }

    public string ArcId { get; init; } = "";
    public string ChapterId { get; init; } = "";
    public string MapId { get; init; } = "";
    public long Clock { get; init; }

    /// <summary>世界状态哈希，读档后复核数据完整。</summary>
    public string WorldHash { get; init; } = "";
}

public sealed record SaveGame
{
    public required SaveHeader Header { get; init; }
    public required WorldState World { get; init; }
}

public sealed record SlotSummary(SaveSlot Slot, SaveHeader? Header, string? Problem, bool BackupOnly);

public sealed record SaveWriteResult(bool Ok, string? Error, SaveSlot Slot)
{
    public static SaveWriteResult Fail(SaveSlot slot, string error) => new(false, error, slot);
}

public sealed record SaveReadResult(SaveGame? Game, string? Error, bool FromBackup, bool Migrated, IReadOnlyList<string> Notes)
{
    public bool Ok => Game is not null;
}

/// <summary>存档端口（架构文档 5.1 领域端口）：文件实现在 Infrastructure。</summary>
public interface ISaveStore
{
    /// <summary>写入槽位：先写临时文件并复核，再替换正式文件，原有效文件转为备份。失败时保留原有效存档。</summary>
    SaveWriteResult Write(SaveSlot slot, SaveGame game);

    /// <summary>读取槽位：正式文件损坏时退回上一份有效备份，并在结果中说明。</summary>
    SaveReadResult Read(SaveSlot slot);

    IReadOnlyList<SlotSummary> List();

    /// <summary>下一个自动槽：空槽优先，否则序号最旧的一个。</summary>
    SaveSlot NextAutoSlot();

    /// <summary>下一份写入应使用的序号。</summary>
    long NextSequence();
}

/// <summary>读档前检查存档与当前内容是否相容：删除或改名的 ID 不得静默丢弃（架构文档 11）。</summary>
public static class SaveCompatibility
{
    public static IReadOnlyList<string> Check(WorldState w, WorldContent content)
    {
        var problems = new List<string>();
        void Missing<T>(string kind, IEnumerable<string> ids, IReadOnlyDictionary<string, T> table)
        {
            foreach (var id in ids.Where(id => !table.ContainsKey(id)))
            {
                problems.Add($"{kind} {id} 在当前内容中不存在");
            }
        }

        Missing("地图", [w.MapId], content.Maps);
        Missing("物品", w.Items.Keys, content.Items);
        Missing("任务", w.Quests.Keys, content.Quests);
        Missing("人物", w.Party.Concat(w.Reservations.Keys), content.Characters);
        foreach (var (id, q) in w.Quests)
        {
            if (content.Quests.TryGetValue(id, out var def) && q.Stage is { } stage && def.StageById(stage) is null)
            {
                problems.Add($"任务 {id} 的阶段 {stage} 在当前内容中不存在");
            }
        }

        if (w.QueuedEvent is { } ev && !content.Events.ContainsKey(ev))
        {
            problems.Add($"途中事件 {ev} 在当前内容中不存在");
        }

        return problems;
    }
}
