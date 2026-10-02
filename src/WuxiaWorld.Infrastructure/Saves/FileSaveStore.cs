using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WuxiaWorld.Application.Persistence;
using WuxiaWorld.Domain.World;

namespace WuxiaWorld.Infrastructure.Saves;

/// <summary>把旧版存档 JSON 原地升级一版（<c>vN → vN+1</c>）。迁移只改 JSON，不接触领域对象。</summary>
public interface ISaveMigration
{
    int From { get; }

    void Migrate(JsonObject payload);
}

/// <summary>
/// UTF-8 JSON 文件存档（架构文档 11）。文件形如 <c>{"checksum":"…","payload":{"header":…,"world":…}}</c>，
/// 校验和为 payload 原文的 SHA-256，只用于发现损坏，不防作弊。
/// 写入：临时文件 → 落盘 → 回读复核 → 替换正式文件（原有效文件转为 <c>.bak</c>）；正式文件已损坏时不让它顶掉有效备份。
/// 读取：正式文件损坏退回备份；旧版本按顺序迁移，迁移前另存 <c>.vN.bak</c>；更新版本拒绝加载。
/// </summary>
public sealed class FileSaveStore : ISaveStore
{
    /// <summary>当前存档结构版本。改动存档结构时递增，并在 <see cref="Migrations"/> 中加入上一版的迁移。</summary>
    public const int CurrentSchemaVersion = 4;

    public static readonly JsonSerializerOptions Json = CreateOptions();

    private readonly int _schemaVersion;
    private readonly IReadOnlyList<ISaveMigration> _migrations;

    public FileSaveStore(string directory, int schemaVersion = CurrentSchemaVersion, IReadOnlyList<ISaveMigration>? migrations = null)
    {
        Directory = directory;
        _schemaVersion = schemaVersion;
        _migrations = migrations ?? Migrations;
    }

    /// <summary>正式迁移表，按起始版本排列。</summary>
    public static IReadOnlyList<ISaveMigration> Migrations { get; } = [new V1ToV2(), new V2ToV3(), new V3ToV4()];

    public string Directory { get; }

    public string PathOf(SaveSlot slot) => Path.Combine(Directory, slot.Stem + ".json");

    public SaveWriteResult Write(SaveSlot slot, SaveGame game)
    {
        if (!slot.IsValid)
        {
            return SaveWriteResult.Fail(slot, $"无效槽位 {slot}");
        }

        var path = PathOf(slot);
        var temp = path + ".tmp";
        var backup = path + ".bak";
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var header = game.Header with { SaveSchemaVersion = _schemaVersion, WorldHash = game.World.Hash() };
            var payload = JsonSerializer.Serialize(new SaveGame { Header = header, World = game.World }, Json);
            var bytes = Encoding.UTF8.GetBytes($"{{\"checksum\":\"{Checksum(payload)}\",\"payload\":{payload}}}");
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (Parse(File.ReadAllBytes(temp)).Error is { } verify)
            {
                File.Delete(temp);
                return SaveWriteResult.Fail(slot, $"写入后复核失败：{verify}");
            }

            if (File.Exists(path) && Parse(File.ReadAllBytes(path)).Error is null)
            {
                File.Replace(temp, path, backup, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temp, path, overwrite: true);
            }

            return new SaveWriteResult(true, null, slot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            return SaveWriteResult.Fail(slot, $"存档写入失败，原有存档未改动：{ex.Message}");
        }
    }

    public SaveReadResult Read(SaveSlot slot)
    {
        var path = PathOf(slot);
        var notes = new List<string>();
        var main = TryRead(path);
        if (main.Game is not null)
        {
            return Finish(path, main, fromBackup: false, notes);
        }

        if (main.Error is not null)
        {
            notes.Add($"正式存档不可用：{main.Error}");
        }

        var backup = TryRead(path + ".bak");
        if (backup.Game is not null)
        {
            notes.Add("已改用上一份有效备份");
            return Finish(path + ".bak", backup, fromBackup: true, notes);
        }

        var error = main.Error ?? backup.Error ?? "槽位为空";
        return new SaveReadResult(null, error, false, false, notes);
    }

    public IReadOnlyList<SlotSummary> List()
    {
        var list = new List<SlotSummary>();
        foreach (var slot in SaveSlot.All)
        {
            var path = PathOf(slot);
            var main = TryRead(path);
            if (main.Game is not null)
            {
                list.Add(new SlotSummary(slot, main.Game.Header, null, false));
                continue;
            }

            var backup = TryRead(path + ".bak");
            list.Add(backup.Game is not null
                ? new SlotSummary(slot, backup.Game.Header, main.Error, true)
                : new SlotSummary(slot, null, main.Error ?? backup.Error, false));
        }

        return list;
    }

    public SaveSlot NextAutoSlot()
    {
        var autos = List().Where(s => s.Slot.Kind == SlotKind.Auto).ToList();
        var empty = autos.FirstOrDefault(s => s.Header is null);
        if (empty is not null)
        {
            return empty.Slot;
        }

        return autos.OrderBy(s => s.Header!.Sequence).ThenBy(s => s.Slot.Index).First().Slot;
    }

    public long NextSequence() => List().Select(s => s.Header?.Sequence ?? 0).DefaultIfEmpty(0).Max() + 1;

    /// <summary>缩略图文件：<c>&lt;槽位&gt;.s&lt;写入序号&gt;.jpg</c>。按序号对应存档，正式存档换了而缩略图没跟上时不会张冠李戴。</summary>
    public string ThumbnailPathOf(SaveSlot slot, long sequence) =>
        Path.Combine(Directory, $"{slot.Stem}.s{sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)}.jpg");

    public SaveDeleteResult Delete(SaveSlot slot)
    {
        if (!slot.IsValid)
        {
            return new SaveDeleteResult(false, $"无效槽位 {slot}");
        }

        if (!System.IO.Directory.Exists(Directory))
        {
            return new SaveDeleteResult(true, null);
        }

        try
        {
            // 正式文件、.bak、.tmp、迁移前原件 .vN.bak 与各序号的缩略图；槽位主干互不为前缀（manual_01、quick、auto_1）。
            foreach (var file in System.IO.Directory.GetFiles(Directory, slot.Stem + ".*"))
            {
                File.Delete(file);
            }

            return new SaveDeleteResult(true, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SaveDeleteResult(false, $"删除失败：{ex.Message}");
        }
    }

    public bool WriteThumbnail(SaveSlot slot, long sequence, byte[] jpeg)
    {
        if (!slot.IsValid || jpeg.Length == 0)
        {
            return false;
        }

        var path = ThumbnailPathOf(slot, sequence);
        var temp = path + ".tmp";
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllBytes(temp, jpeg);
            File.Move(temp, path, overwrite: true);

            // 只留正式存档与备份对应的两张，其余旧序号的缩略图删掉。
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path };
            if (TryRead(PathOf(slot) + ".bak").Game is { } backup)
            {
                keep.Add(ThumbnailPathOf(slot, backup.Header.Sequence));
            }

            foreach (var old in System.IO.Directory.GetFiles(Directory, slot.Stem + ".s*.jpg").Where(f => !keep.Contains(f)))
            {
                TryDelete(old);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            return false;
        }
    }

    public byte[]? ReadThumbnail(SaveSlot slot, long sequence)
    {
        var path = ThumbnailPathOf(slot, sequence);
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record Loaded(SaveGame? Game, string? Error, int FromVersion);

    private Loaded TryRead(string path)
    {
        if (!File.Exists(path))
        {
            return new Loaded(null, null, 0);
        }

        try
        {
            return Parse(File.ReadAllBytes(path));
        }
        catch (IOException ex)
        {
            return new Loaded(null, ex.Message, 0);
        }
    }

    private Loaded Parse(byte[] bytes)
    {
        JsonObject payload;
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            var root = doc.RootElement;
            if (!root.TryGetProperty("checksum", out var sum) || !root.TryGetProperty("payload", out var body))
            {
                return new Loaded(null, "文件结构不完整", 0);
            }

            var raw = body.GetRawText();
            if (Checksum(raw) != sum.GetString())
            {
                return new Loaded(null, "校验和不符（文件损坏或被截断）", 0);
            }

            payload = JsonNode.Parse(raw)!.AsObject();
        }
        catch (JsonException ex)
        {
            return new Loaded(null, $"无法解析：{ex.Message}", 0);
        }

        var version = payload["header"]?["save_schema_version"]?.GetValue<int>() ?? 0;
        if (version > _schemaVersion)
        {
            return new Loaded(null, $"存档来自更新的版本（结构版本 {version}，本程序支持到 {_schemaVersion}），请更新游戏", version);
        }

        for (var v = version; v < _schemaVersion; v++)
        {
            var step = _migrations.FirstOrDefault(m => m.From == v);
            if (step is null)
            {
                return new Loaded(null, $"缺少结构版本 {v} → {v + 1} 的迁移", version);
            }

            step.Migrate(payload);
            payload["header"]!["save_schema_version"] = v + 1;
        }

        try
        {
            var game = payload.Deserialize<SaveGame>(Json) ?? throw new JsonException("存档为空");
            if (version == _schemaVersion && game.World.Hash() != game.Header.WorldHash)
            {
                return new Loaded(null, "世界状态哈希与存档头不符", version);
            }

            return new Loaded(game, null, version);
        }
        catch (JsonException ex)
        {
            return new Loaded(null, $"存档内容无法读取：{ex.Message}", version);
        }
    }

    private SaveReadResult Finish(string path, Loaded loaded, bool fromBackup, List<string> notes)
    {
        var migrated = loaded.FromVersion < _schemaVersion;
        if (migrated)
        {
            // 迁移前的原文件另存一份，不覆盖已有的同版本备份。
            var keep = $"{path}.v{loaded.FromVersion}.bak";
            if (!File.Exists(keep))
            {
                File.Copy(path, keep);
            }

            notes.Add($"存档已从结构版本 {loaded.FromVersion} 迁移到 {_schemaVersion}，原文件保留为 {Path.GetFileName(keep)}");
        }

        return new SaveReadResult(loaded.Game, null, fromBackup, migrated, notes);
    }

    private static string Checksum(string payload) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时文件或旧缩略图删不掉不影响正式存档；下次写入会覆盖或再清理。
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            // 领域集合以序数比较器初始化；读档时填充现有集合，保持排序与哈希稳定。
            PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
            IgnoreReadOnlyProperties = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        return options;
    }
}

/// <summary>
/// v1 → v2（2026-10-02，M2-05 人物成长）：世界状态新增修为 <c>cultivation</c> 与人物成长构成 <c>builds</c>。
/// 迁移只补空字段；主角构成留空，读档后由 <c>GameSession.UpgradeLegacy</c> 按内容补齐武学、开局衣物与追赶经验。
/// </summary>
internal sealed class V1ToV2 : ISaveMigration
{
    public int From => 1;

    public void Migrate(JsonObject payload)
    {
        if (payload["world"] is not JsonObject world)
        {
            throw new InvalidDataException("存档缺少 world");
        }

        world["cultivation"] ??= 0;
        world["builds"] ??= new JsonObject();
    }
}

/// <summary>
/// v2 → v3（2026-10-02，M2-02 江湖大地图）：世界状态新增到过的小地图 <c>visited</c>。旧档无从知道到过哪里，
/// 只补当前所在的地图；其余地标在下次到达时记下（大地图上暂显示为“未到访”，不影响能否前往）。
/// </summary>
internal sealed class V2ToV3 : ISaveMigration
{
    public int From => 2;

    public void Migrate(JsonObject payload)
    {
        if (payload["world"] is not JsonObject world)
        {
            throw new InvalidDataException("存档缺少 world");
        }

        if (world["visited"] is null)
        {
            var visited = new JsonArray();
            if (world["map_id"]?.GetValue<string>() is { Length: > 0 } map)
            {
                visited.Add(map);
            }

            world["visited"] = visited;
        }
    }
}

/// <summary>
/// v3 → v4（2026-10-02，M2-06 伙伴）：世界状态新增阵位 <c>formation</c> 与同行记录 <c>companions</c>。
/// 旧档的阵位按队伍次序取默认格位（与此前剧情战写死的站位相同：主角前排 1 号、第二人后排 1 号、其后前排 2 号、前排 0 号）；
/// 在队伙伴记入队一次、经验与主角持平（暂时同行者的经验不被使用）。已离队的人此前没有记录，按未同行处理。
/// </summary>
internal sealed class V3ToV4 : ISaveMigration
{
    public int From => 3;

    public void Migrate(JsonObject payload)
    {
        if (payload["world"] is not JsonObject world)
        {
            throw new InvalidDataException("存档缺少 world");
        }

        var party = world["party"] is JsonArray a ? a.Select(x => x!.GetValue<string>()).ToList() : [];
        var experience = world["experience"]?.GetValue<int>() ?? 0;
        if (world["formation"] is null)
        {
            var formation = new JsonObject();
            for (var i = 0; i < party.Count && i < PartyRules.DefaultOrder.Length; i++)
            {
                formation[party[i]] = PartyRules.DefaultOrder[i];
            }

            world["formation"] = formation;
        }

        if (world["companions"] is null)
        {
            var companions = new JsonObject();
            foreach (var id in party.Skip(1))
            {
                companions[id] = new JsonObject { ["joins"] = 1, ["experience"] = experience };
            }

            world["companions"] = companions;
        }
    }
}
