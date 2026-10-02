using System.Text;
using System.Text.Json.Nodes;
using WuxiaWorld.Application.Persistence;
using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Infrastructure.Saves;

namespace WuxiaWorld.Domain.Tests;

public sealed class SaveStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wuxia-save-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static WorldState RichWorld()
    {
        var g = GameSession.NewGame(WorldFixture.Rules());
        var d = g.StartEvent("event.test.meet");
        while (!d.Runner.Ended)
        {
            if (!d.Runner.AwaitingChoice)
            {
                d.Runner.Continue();
            }
            else
            {
                d.Runner.Choose(0);
            }
        }

        g.FinishDialogue(d);
        var s = g.World.Clone();
        s.Battle = new PendingBattle { RequestId = "req.x", Encounter = "battle.x", OnVictory = [WorldEffect.Item("item.test.pill")] };
        s.MapDeltas.Add("map.test.street/chest");
        return s;
    }

    private static SaveGame Game(WorldState w, long sequence = 1) => new()
    {
        Header = new SaveHeader { GameVersion = "test", ContentVersion = "c", RulesetVersion = 2, Sequence = sequence, MapId = w.MapId },
        World = w,
    };

    [Fact]
    public void Save_round_trip_preserves_the_world_hash()
    {
        var store = new FileSaveStore(_dir);
        var w = RichWorld();
        Assert.True(store.Write(SaveSlot.Manual(1), Game(w)).Ok);
        var read = store.Read(SaveSlot.Manual(1));
        Assert.True(read.Ok, read.Error);
        Assert.False(read.FromBackup);
        Assert.Equal(w.Hash(), read.Game!.World.Hash());
        Assert.Equal(w.Hash(), read.Game.Header.WorldHash);
        Assert.Equal(FileSaveStore.CurrentSchemaVersion, read.Game.Header.SaveSchemaVersion);
        Assert.Equal("req.x#1", read.Game.World.Battle!.InstanceId);
        Assert.Single(read.Game.World.Battle.OnVictory);
    }

    [Fact]
    public void Corrupted_slot_falls_back_to_the_last_valid_backup()
    {
        var store = new FileSaveStore(_dir);
        var first = RichWorld();
        var second = first.Clone();
        second.Silver = 999;
        store.Write(SaveSlot.Quick, Game(first, 1));
        store.Write(SaveSlot.Quick, Game(second, 2));

        // 模拟写到一半断电：正式文件被截断。
        var path = store.PathOf(SaveSlot.Quick);
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]);

        var read = store.Read(SaveSlot.Quick);
        Assert.True(read.Ok, read.Error);
        Assert.True(read.FromBackup);
        Assert.Equal(first.Hash(), read.Game!.World.Hash());
        Assert.Contains(read.Notes, n => n.Contains("备份", StringComparison.Ordinal));

        // 正式文件已坏时再写：新文件替换坏文件，不让坏文件顶掉有效备份。
        var third = first.Clone();
        third.Silver = 3;
        store.Write(SaveSlot.Quick, Game(third, 3));
        Assert.Equal(third.Hash(), store.Read(SaveSlot.Quick).Game!.World.Hash());
        File.WriteAllText(path, "{}");
        Assert.Equal(first.Hash(), store.Read(SaveSlot.Quick).Game!.World.Hash());
    }

    [Fact]
    public void Tampered_payload_is_detected_by_checksum_and_leftover_temp_files_are_ignored()
    {
        var store = new FileSaveStore(_dir);
        store.Write(SaveSlot.Manual(2), Game(RichWorld()));
        var path = store.PathOf(SaveSlot.Manual(2));
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"silver\":20", "\"silver\":99999", StringComparison.Ordinal));
        File.WriteAllText(path + ".tmp", "garbage");
        var read = store.Read(SaveSlot.Manual(2));
        Assert.False(read.Ok);
        Assert.Contains("校验和", read.Error);
    }

    [Fact]
    public void Newer_schema_is_refused_and_older_schema_is_migrated_with_a_backup()
    {
        var w = RichWorld();
        const int current = FileSaveStore.CurrentSchemaVersion;
        var newer = new FileSaveStore(_dir, schemaVersion: current + 1, migrations: [.. FileSaveStore.Migrations, new RenameFactMigration()]);
        newer.Write(SaveSlot.Manual(3), Game(w));
        var refused = new FileSaveStore(_dir).Read(SaveSlot.Manual(3));
        Assert.False(refused.Ok);
        Assert.Contains("更新的版本", refused.Error);

        // 迁移样本：当前版本的存档在“下一版”程序里读取，事实键改名。
        var old = new FileSaveStore(_dir);
        var oldWorld = w.Clone();
        oldWorld.Facts["fact.test.old_name"] = "kept";
        old.Write(SaveSlot.Manual(4), Game(oldWorld));
        var read = newer.Read(SaveSlot.Manual(4));
        Assert.True(read.Ok, read.Error);
        Assert.True(read.Migrated);
        Assert.Equal("kept", read.Game!.World.Facts["fact.test.new_name"]);
        Assert.False(read.Game.World.Facts.ContainsKey("fact.test.old_name"));
        Assert.True(File.Exists(newer.PathOf(SaveSlot.Manual(4)) + $".v{current}.bak"));

        var missing = new FileSaveStore(_dir, schemaVersion: current + 2, migrations: [.. FileSaveStore.Migrations, new RenameFactMigration()]);
        Assert.Contains("迁移", missing.Read(SaveSlot.Manual(4)).Error);
    }

    [Fact]
    public void Auto_slots_rotate_to_the_oldest_and_listing_reports_every_slot()
    {
        var store = new FileSaveStore(_dir);
        var w = RichWorld();
        for (var i = 0; i < 4; i++)
        {
            var slot = store.NextAutoSlot();
            store.Write(slot, Game(w, store.NextSequence()));
        }

        var autos = store.List().Where(s => s.Slot.Kind == SlotKind.Auto).ToList();
        Assert.Equal(3, autos.Count);
        Assert.Equal([4L, 2L, 3L], autos.Select(s => s.Header!.Sequence));
        Assert.Equal(SaveSlot.Auto(2), store.NextAutoSlot());
        Assert.Equal(14, store.List().Count);
        Assert.False(store.Write(new SaveSlot(SlotKind.Manual, 11), Game(w)).Ok);
    }

    [Fact]
    public void Compatibility_check_names_ids_missing_from_current_content()
    {
        var w = RichWorld();
        w.Items["item.removed"] = 1;
        w.Quests[WorldFixture.Main].Stage = "renamed_stage";
        var problems = SaveCompatibility.Check(w, WorldFixture.Content());
        Assert.Contains(problems, p => p.Contains("item.removed", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("renamed_stage", StringComparison.Ordinal));
        Assert.Empty(SaveCompatibility.Check(RichWorld(), WorldFixture.Content()));
    }

    private sealed class RenameFactMigration : ISaveMigration
    {
        public int From => FileSaveStore.CurrentSchemaVersion;

        public void Migrate(JsonObject payload)
        {
            var facts = payload["world"]!["facts"]!.AsObject();
            if (facts.Remove("fact.test.old_name", out var value))
            {
                facts["fact.test.new_name"] = value;
            }
        }
    }
}
