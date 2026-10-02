using Godot;
using WuxiaWorld.Application.Persistence;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 探索中的菜单（Esc）：继续、保存、读取、札记、返回标题。保存只写手动槽（快速槽由 F5 写，自动槽由换图写）；
/// 读取可读任何有效槽。对话、换图或待开战斗期间不能保存（架构文档 3、11），菜单本身也只在探索时打开。
/// </summary>
public static class GameMenu
{
    /// <param name="slots">直接打开读取槽位页（截图用）。</param>
    public static Control Build(PlaySession play, Action close, Action journal, Action<string> message, bool slots = false)
    {
        var body = Ui.Column(UiPalette.SpaceM);
        if (slots)
        {
            ShowSlots(body, play, close, journal, message, save: false);
        }
        else
        {
            ShowMain(body, play, close, journal, message);
        }

        return body;
    }

    private static void ShowMain(VBoxContainer body, PlaySession play, Action close, Action journal, Action<string> message)
    {
        Ui.ClearChildren(body);
        body.AddChild(Ui.Row(UiPalette.SpaceL, Ui.Seal("暂歇"), Ui.Column(4,
            Ui.Text("菜单", UiTheme.DarkTitleLabel, 40),
            Ui.Text($"{play.Name(play.Game.World.MapId)}　·　{PlaySession.ClockText(play.Game.World.Clock)}", UiTheme.DarkMutedLabel, 18))));
        body.AddChild(Ui.Rule(dark: true));
        Button? first = null;
        void Item(string text, Action action, bool disabled = false, string? tooltip = null)
        {
            var b = Ui.Button(text, UiTheme.MenuItem, action, disabled, tooltip);
            b.CustomMinimumSize = new Vector2(0, 60);
            b.MouseEntered += b.GrabFocus;
            body.AddChild(b);
            first ??= disabled ? null : b;
        }

        Item("继续", close);
        Item("保存进度", () => ShowSlots(body, play, close, journal, message, save: true), !play.Game.CanSave, "对话、换图或战斗进行中不能保存");
        Item("读取进度", () => ShowSlots(body, play, close, journal, message, save: false));
        Item("江湖札记", journal);
        Item("返回标题", () => ConfirmTitle(body, play, close, journal, message));
        body.AddChild(Ui.Spacer(horizontal: false));
        body.AddChild(Ui.Row(UiPalette.SpaceM, Ui.Spacer(), Ui.KeyHints(true, ("↑↓", "选择"), ("Enter", "确认"), ("Esc", "关闭"))));
        first?.CallDeferred(Control.MethodName.GrabFocus);
    }

    private static void ConfirmTitle(VBoxContainer body, PlaySession play, Action close, Action journal, Action<string> message)
    {
        Ui.ClearChildren(body);
        body.AddChild(Ui.Text("返回标题", UiTheme.DarkTitleLabel, 40));
        body.AddChild(Ui.Rule(dark: true));
        body.AddChild(Ui.Text("上次存档之后的进度不会保留。换图时会自动存档，也可以先手动保存。", UiTheme.DarkLabel, 22, wrap: true));
        var yes = Ui.Button("回到标题", UiTheme.PrimaryButton, () =>
        {
            AppHost.Instance.Play = null;
            AppHost.Instance.Router.GoTo(ScenePaths.MainMenu);
        });
        var no = Ui.Button("再想想", UiTheme.DarkButton, () => ShowMain(body, play, close, journal, message));
        foreach (var b in new[] { yes, no })
        {
            b.CustomMinimumSize = new Vector2(200, 56);
        }

        body.AddChild(Ui.Spacer(horizontal: false));
        body.AddChild(Ui.Row(UiPalette.SpaceM, Ui.Spacer(), no, yes));
        no.CallDeferred(Control.MethodName.GrabFocus);
    }

    private static void ShowSlots(VBoxContainer body, PlaySession play, Action close, Action journal, Action<string> message, bool save)
    {
        Ui.ClearChildren(body);
        body.AddChild(Ui.Row(UiPalette.SpaceL, Ui.Text(save ? "保存进度" : "读取进度", UiTheme.DarkTitleLabel, 40), Ui.Spacer(),
            Ui.Button("返回", UiTheme.DarkButton, () => ShowMain(body, play, close, journal, message))));
        body.AddChild(Ui.Rule(dark: true));
        var list = Ui.Column(UiPalette.SpaceS);
        Button? first = null;
        var summaries = play.Saves.List().ToDictionary(s => s.Slot);
        foreach (var slot in SaveSlot.All)
        {
            var summary = summaries.GetValueOrDefault(slot);
            var usable = save ? slot.Kind == SlotKind.Manual : summary?.Header is not null;
            var row = Ui.Button(SlotLine(play, slot, summary), UiTheme.ChoiceButton, usable ? () =>
            {
                if (save)
                {
                    var r = play.Save(slot);
                    message(r.Ok ? $"已保存到{SlotName(slot)}" : $"保存失败：{r.Error}");
                    ShowSlots(body, play, close, journal, message, save);
                }
                else
                {
                    LoadInto(slot, message);
                }
            } : null, disabled: !usable);
            row.Alignment = HorizontalAlignment.Left;
            row.CustomMinimumSize = new Vector2(0, 46);
            row.AddThemeFontSizeOverride("font_size", 20);
            row.MouseEntered += () =>
            {
                if (!row.Disabled)
                {
                    row.GrabFocus();
                }
            };
            list.AddChild(row);
            first ??= usable ? row : null;
        }

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(Ui.Expand(list));
        body.AddChild(Ui.Expand(scroll, vertical: true));
        body.AddChild(Ui.Text(save ? "快速槽由 F5 写入，自动槽在换图后轮换写入。" : "正式存档损坏时会读取上一份备份并提示。", UiTheme.DarkMutedLabel, 16));
        first?.CallDeferred(Control.MethodName.GrabFocus);
    }

    public static string SlotName(SaveSlot slot) => slot.Kind switch
    {
        SlotKind.Manual => $"手动 {slot.Index:00}",
        SlotKind.Quick => "快速",
        _ => $"自动 {slot.Index}",
    };

    public static string SlotLine(PlaySession? play, SaveSlot slot, SlotSummary? summary)
    {
        var head = SlotName(slot);
        if (summary?.Header is not { } h)
        {
            return summary?.Problem is { } p ? $"{head}　·　无法读取：{p}" : $"{head}　·　空";
        }

        var map = play?.Name(h.MapId) ?? h.MapId;
        var backup = summary.BackupOnly ? "　·　仅备份可用" : "";
        return $"{head}　·　{map}　·　{PlaySession.ClockText(h.Clock)}　·　{h.CreatedAt}{backup}";
    }

    /// <summary>读档并进入探索；不相容或读取失败时只提示，不改当前进度。</summary>
    public static void LoadInto(SaveSlot slot, Action<string> message)
    {
        var play = PlaySession.Load(slot, out var error, out var notes);
        if (play is null)
        {
            message($"读取失败：{error}");
            return;
        }

        play.PendingToasts.Add(("存档", $"已读取{SlotName(slot)}"));
        play.PendingToasts.AddRange(notes.Select(n => ("提示", n)));
        AppHost.Instance.Play = play;
        AppHost.Instance.Router.GoTo(ScenePaths.Exploration);
    }
}
