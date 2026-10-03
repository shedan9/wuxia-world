using Godot;
using WuxiaWorld.Application.Persistence;
using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.Characters;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Game.Adapters;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Infrastructure.Content;
using WuxiaWorld.Infrastructure.Saves;

namespace WuxiaWorld.Game.Presentation.Play;

/// <summary>
/// 一局游戏在表现层的宿主（架构文档 5.2 Session 的界面一侧）：持有 <see cref="GameSession"/>、内容包与存档端口，
/// 并在场景之间传递“到达后要提交的切换”和“刚结束的战斗”。只由 <c>AppHost</c> 持有一份，不另设全局单例。
/// 规则改动一律经 <see cref="Game"/> 的事务；这里只做装配、存读档与文字查表。
/// </summary>
public sealed class PlaySession
{
    /// <summary>当前开发构建的版本标识，写进存档头。</summary>
    public const string GameVersion = "m2-dev";

    private PlaySession(GameSession game, WorldBundle world, CombatBundle combat, ISaveStore saves)
    {
        Game = game;
        World = world;
        Combat = combat;
        Saves = saves;
    }

    public GameSession Game { get; }
    public WorldBundle World { get; }
    public CombatBundle Combat { get; }
    public ISaveStore Saves { get; }

    /// <summary>已开始、等待目的地场景加载完成后提交的切换（架构文档 6.3）。目的地场景取走后清空。</summary>
    public Transition? Arriving { get; set; }

    /// <summary>上一场景留给探索页显示的提示（战斗结算、读档说明等），探索页显示后清空。</summary>
    public List<(string Kind, string Text)> PendingToasts { get; } = [];

    /// <summary>累计游戏时长（秒）：一局进行中、场景树未暂停时由 <c>AppHost</c> 逐帧累加，存进存档头，读档时接着记。</summary>
    public double PlaySeconds { get; set; }

    public static string DurationText(double seconds)
    {
        var minutes = (long)(seconds / 60);
        return $"{minutes / 60}:{minutes % 60:00}";
    }

    /// <summary>本局已显示的台词与选择（对话记录用，不存档）。</summary>
    public List<(string Speaker, string Text)> History { get; } = [];

    /// <summary>本局已推过的世界通知（札记“见闻”页用，不存档，最多留 <see cref="NoticeLimit"/> 条）：通知几秒后淡出，事后在这里查。</summary>
    public List<(long Clock, string Kind, string Text)> Notices { get; } = [];

    public const int NoticeLimit = 300;

    public void LogNotice(string kind, string text)
    {
        Notices.Add((Game.World.Clock, kind, text));
        if (Notices.Count > NoticeLimit)
        {
            Notices.RemoveRange(0, Notices.Count - NoticeLimit);
        }
    }

    /// <summary>存档目录：<c>user://saves</c>（Windows 下在 %APPDATA%\Godot\app_userdata\武侠世界\saves）；开发参数 <c>--saves</c> 可改到别处。</summary>
    public static ISaveStore OpenStore() => new FileSaveStore(DevCapture.SaveDirectory ?? ProjectSettings.GlobalizePath("user://saves"));

    public static PlaySession? NewGame(out string? error)
    {
        if (!LoadContent(out var world, out var combat, out error))
        {
            return null;
        }

        var rules = new WorldRules(world.ToContent());
        var game = GameSession.NewGame(rules, new GrowthRules(rules, combat.ToContent()));
        return new PlaySession(game, world, combat, OpenStore());
    }

    /// <summary>读档：先查存档与当前内容是否相容，不相容则拒绝并说明，不静默丢失物品或任务（架构文档 11）。</summary>
    public static PlaySession? Load(SaveSlot slot, out string? error, out IReadOnlyList<string> notes)
    {
        notes = [];
        if (!LoadContent(out var world, out var combat, out error))
        {
            return null;
        }

        var store = OpenStore();
        var read = store.Read(slot);
        if (read.Game is not { } save)
        {
            error = read.Error ?? "存档无法读取";
            return null;
        }

        var rules = new WorldRules(world.ToContent());
        var problems = SaveCompatibility.Check(save.World, rules.Content);
        if (problems.Count > 0)
        {
            error = "存档与当前内容不相容：" + string.Join("；", problems.Take(4));
            return null;
        }

        var game = new GameSession(rules, save.World, new GrowthRules(rules, combat.ToContent()));
        var list = new List<string>(read.Notes);
        if (read.FromBackup)
        {
            list.Add("正式存档已损坏，已读取上一份备份");
        }

        if (save.Header.ContentVersion != world.ContentVersion)
        {
            list.Add("存档写于另一内容版本，已按当前内容继续");
        }

        if (game.RepairSpawn() is { } repaired)
        {
            list.Add(repaired);
        }

        if (game.UpgradeLegacy() is { } upgraded)
        {
            list.Add(upgraded);
        }

        notes = list;
        return new PlaySession(game, world, combat, store) { PlaySeconds = save.Header.PlaySeconds };
    }

    private static bool LoadContent(out WorldBundle world, out CombatBundle combat, out string? error)
    {
        world = null!;
        combat = null!;
        if (GeneratedContent.World is not { } w || GeneratedContent.Combat is not { } c)
        {
            error = GeneratedContent.Error ?? "内容包读取失败";
            return false;
        }

        var errors = WorldContentValidator.Validate(w, c);
        if (errors.Count > 0)
        {
            error = "内容包校验失败：" + string.Join("；", errors.Take(4));
            return false;
        }

        (world, combat, error) = (w, c, null);
        return true;
    }

    // ── 存档 ─────────────────────────────────────────────

    private readonly List<(SaveSlot Slot, long Sequence)> _awaitingThumbnail = [];

    /// <summary>打开暂停菜单那一帧抓下的画面（菜单里手动保存用它作缩略图，画面里没有菜单）。</summary>
    public byte[]? MenuFrame { get; set; }

    /// <summary>有存档还等着补缩略图（换图与战斗后的自动存档在新画面揭开后由探索页补上）。</summary>
    public bool AwaitingThumbnail => _awaitingThumbnail.Count > 0;

    /// <summary>
    /// 写入槽位。对话、换图或待开战斗期间拒绝（架构文档 3）。
    /// <paramref name="thumbnail"/> 为空时记下待补，探索页画面就绪后经 <see cref="FlushThumbnails"/> 补写。
    /// </summary>
    public SaveWriteResult Save(SaveSlot slot, byte[]? thumbnail = null)
    {
        if (!Game.CanSave)
        {
            return SaveWriteResult.Fail(slot, "对话、换图或战斗进行中，暂不能保存");
        }

        var w = Game.World;
        var header = new SaveHeader
        {
            GameVersion = GameVersion,
            ContentVersion = World.ContentVersion,
            RulesetVersion = StatFormula.RulesetVersion,
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            Sequence = Saves.NextSequence(),
            ArcId = w.ArcId,
            ChapterId = w.ChapterId,
            MapId = w.MapId,
            Clock = w.Clock,
            PlaySeconds = (long)PlaySeconds,
        };
        var result = Saves.Write(slot, new SaveGame { Header = header, World = w });
        if (result.Ok)
        {
            _awaitingThumbnail.RemoveAll(a => a.Slot == slot);
            if (thumbnail is null || !Saves.WriteThumbnail(slot, header.Sequence, thumbnail))
            {
                _awaitingThumbnail.Add((slot, header.Sequence));
            }
        }

        return result;
    }

    /// <summary>自动存档：轮换写入空槽或最旧的自动槽；不能保存时跳过。</summary>
    public SaveWriteResult? AutoSave(byte[]? thumbnail = null) => Game.CanSave ? Save(Saves.NextAutoSlot(), thumbnail) : null;

    /// <summary>把当前画面补作待补存档的缩略图。</summary>
    public void FlushThumbnails(byte[] jpeg)
    {
        foreach (var (slot, sequence) in _awaitingThumbnail)
        {
            Saves.WriteThumbnail(slot, sequence, jpeg);
        }

        _awaitingThumbnail.Clear();
    }

    /// <summary>最近写入的一份有效存档（标题页“继续旅程”）。</summary>
    public static SlotSummary? Latest(ISaveStore store) =>
        store.List().Where(s => s.Header is not null).OrderByDescending(s => s.Header!.Sequence).FirstOrDefault();

    // ── 文字 ─────────────────────────────────────────────

    public string Name(string id) => World.Name(id);

    public string? Text(string key) => World.Text.TryGetValue(key, out var t) ? t : null;

    /// <summary>逻辑时辰的显示：新游戏从第一日申时开始，一个单位为一个时辰。</summary>
    public static string ClockText(long clock)
    {
        const string hours = "子丑寅卯辰巳午未申酉戌亥";
        var t = clock + 8;
        return $"第{ChineseNumber(t / 12 + 1)}日　{hours[(int)(t % 12)]}时";
    }

    private static string ChineseNumber(long n)
    {
        const string digits = "〇一二三四五六七八九";
        return n switch
        {
            < 10 => digits[(int)n].ToString(),
            < 20 => "十" + (n % 10 == 0 ? "" : digits[(int)(n % 10)].ToString()),
            _ => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    /// <summary>世界提示的显示文字（类别、内容）；不需要提示的返回 null。</summary>
    public (string Kind, string Text)? Describe(WorldNotice n) => n.Kind switch
    {
        "item_gained" => ("物品", $"获得 {Name(n.Id)}{(n.Amount > 1 ? $" ×{n.Amount}" : "")}"),
        "item_lost" => ("物品", $"交出 {Name(n.Id)}{(n.Amount > 1 ? $" ×{n.Amount}" : "")}"),
        "silver" => ("银两", n.Amount >= 0 ? $"得银 {n.Amount} 两" : $"花去 {-n.Amount} 两"),
        "trust" => ("关系", $"{Name(n.Id)} 信任{(n.Amount >= 0 ? "上升" : "下降")}"),
        "affection" => ("关系", $"{Name(n.Id)} 好感{(n.Amount >= 0 ? "上升" : "下降")}"),
        "clue" => ("线索", $"已记录：{Name(n.Id)}"),
        "joined" => ("同行", $"{Name(n.Id)} 加入队伍"),
        "left" => ("同行", $"{Name(n.Id)} 离开队伍"),
        "caught_up" => ("同行", $"{Name(n.Id)} 离队期间也未荒废，追到第 {n.Amount} 级"),
        "skill" => ("武学", $"习得 {Combat.Name(n.Id)}"),
        "experience" => ("成长", $"经验 +{n.Amount}"),
        "cultivation" => ("成长", $"修为 +{n.Amount}"),
        "level_up" => ("成长", $"升到第 {n.Amount} 级：得 {StatFormula.PotentialPerLevel} 点潜能（{KeyBindings.Label("open_character")} 人物页分配）"),
        "mastery" => ("武学", $"{Combat.Name(n.Id)} 熟练度提升"),
        "respec.potential" => ("成长", $"洗点：收回 {n.Amount} 点潜能，可重新分配"),
        "respec.mastery" => ("武学", n.Id.StartsWith("skill.", StringComparison.Ordinal)
            ? $"{Combat.Name(n.Id)} 退回第 1 阶：返还修为 {n.Amount}"
            : $"熟练度全部退回第 1 阶：返还修为 {n.Amount}"),
        "quest_started" => ("任务", $"开始：{Name(n.Id)}"),
        "quest_available" => ("任务", Text(n.Id + ".hint") ?? $"可接：{Name(n.Id)}"),
        "quest_completed" => ("任务", $"完成：{Name(n.Id)}"),
        "quest_failed" => ("任务", $"失败：{Name(n.Id)}"),
        "quest_abandoned" => ("任务", $"放弃：{Name(n.Id)}"),
        _ => null,
    };
}
