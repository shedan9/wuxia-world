using Godot;
using WuxiaWorld.Application.Persistence;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 游戏菜单的内容（由 <see cref="PauseMenu"/> 承载，一局中任意时刻按 Esc 打开）：继续、保存、读取、人物、行囊、札记、设置、返回标题、退出游戏。
/// 保存只写手动槽（快速槽由 F5 写，自动槽由换图写）；对话、换图或战斗进行中不能保存（架构文档 3、11），按钮置灰并写明原因。
/// 读取可读任何有效槽。子页（存读档、札记、设置、确认）按 Esc 回到主页，主页按 Esc 关闭菜单。
/// </summary>
public sealed class GameMenu
{
    private readonly VBoxContainer _body = Ui.Column(UiPalette.SpaceM);
    private readonly PlaySession _play;
    private readonly Action _close;
    private readonly Action<float, float> _resize;

    private GameMenu(PlaySession play, Action close, Action<float, float> resize)
    {
        _play = play;
        _close = close;
        _resize = resize;
    }

    public const float Width = 760;
    public const float Height = 860;

    /// <summary>存读档页更宽，放得下带缩略图的存档卡。</summary>
    public const float SlotsWidth = 1180;

    public Control Root => _body;

    /// <summary>当前在子页时返回主页的动作；在主页时为 null（Esc 即关闭菜单）。</summary>
    public Action? Back { get; private set; }

    /// <param name="resize">切换页面时调整承载面板的宽高（札记页更宽）。</param>
    /// <param name="slots">直接打开读取槽位页（截图用）。</param>
    public static GameMenu Build(PlaySession play, Action close, Action<float, float> resize, bool slots = false)
    {
        var menu = new GameMenu(play, close, resize);
        if (slots)
        {
            menu.ShowSlots(save: false);
        }
        else
        {
            menu.ShowMain();
        }

        return menu;
    }

    private void Page(float width = Width, float height = Height, bool sub = true)
    {
        Ui.ClearChildren(_body);
        _resize(width, height);
        Back = sub ? () => ShowMain() : null;
    }

    private void ShowMain()
    {
        Page(sub: false);
        _body.AddChild(Ui.Row(UiPalette.SpaceL, Ui.Seal("暂歇"), Ui.Column(4,
            Ui.Text("菜单", UiTheme.DarkTitleLabel, 40),
            Ui.Text($"{_play.Name(_play.Game.World.MapId)}　·　{PlaySession.ClockText(_play.Game.World.Clock)}　·　游戏已暂停", UiTheme.DarkMutedLabel, 18))));
        _body.AddChild(Ui.Rule(dark: true));
        Button? first = null;
        Button Item(string text, Action action, bool disabled = false, string? tooltip = null)
        {
            var b = Ui.Button(text, UiTheme.MenuItem, action, disabled, tooltip);
            b.CustomMinimumSize = new Vector2(0, 58);
            b.MouseEntered += b.GrabFocus;
            _body.AddChild(b);
            first ??= disabled ? null : b;
            return b;
        }

        var canSave = _play.Game.CanSave;
        Item("继续", _close);
        Item("保存进度", () => ShowSlots(save: true), !canSave);
        if (!canSave)
        {
            // 置灰的原因直接写出来，不藏在悬停提示里（手柄、键盘都看得到）。
            var why = Ui.Text("对话、换图或战斗进行中不能保存，告一段落后再存；换图时也会自动存档。", UiTheme.DarkMutedLabel, 16, wrap: true);
            why.HorizontalAlignment = HorizontalAlignment.Center;
            _body.AddChild(why);
        }

        Item("读取进度", () => ShowSlots(save: false));
        Item("人物与武学", () => ShowPage(CharacterPage.Build(_play)));
        Item("行囊", () => ShowPage(InventoryPage.Build(_play)));
        Item("江湖札记", ShowJournal);
        Item("江湖设置", () =>
        {
            Page();
            _body.AddChild(SettingsPanel.Build(ShowMain));
        });
        Item("返回标题", () => Confirm("返回标题", "回到标题", () =>
        {
            _close();
            AppHost.Instance.Play = null;
            AppHost.Instance.Router.GoTo(ScenePaths.MainMenu);
        }));
        Item("退出游戏", () => Confirm("退出游戏", "退出", () => _body.GetTree().Quit()));
        _body.AddChild(Ui.Spacer(horizontal: false));
        _body.AddChild(Ui.Row(UiPalette.SpaceM, Ui.Spacer(), Ui.KeyHints(true, ("↑↓", "选择"), ("Enter", "确认"), ("Esc", "继续"))));
        first?.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>人物、行囊页：与探索中按 C / I 打开的是同一页；对话、换图或战斗中只能查看。</summary>
    private void ShowPage(Control page)
    {
        Page(ExplorationScreen.PageWidth, ExplorationScreen.PageHeight);
        _body.AddChild(Ui.Expand(page, vertical: true));
        AppHost.Instance.Sound.Play("ui.page", -4);
    }

    private void ShowJournal()
    {
        Page(1500, 860);
        _body.AddChild(Ui.Expand(Journal.Build(_play), vertical: true));
        AppHost.Instance.Sound.Play("ui.page", -4);
    }

    private void Confirm(string title, string yesText, Action yes)
    {
        Page(Width, 420);
        _body.AddChild(Ui.Text(title, UiTheme.DarkTitleLabel, 40));
        _body.AddChild(Ui.Rule(dark: true));
        _body.AddChild(Ui.Text("上次存档之后的进度不会保留。换图时会自动存档，也可以先手动保存。", UiTheme.DarkLabel, 22, wrap: true));
        var ok = Ui.Button(yesText, UiTheme.PrimaryButton, yes);
        var no = Ui.Button("再想想", UiTheme.DarkButton, ShowMain);
        foreach (var b in new[] { ok, no })
        {
            b.CustomMinimumSize = new Vector2(200, 56);
        }

        _body.AddChild(Ui.Spacer(horizontal: false));
        _body.AddChild(Ui.Row(UiPalette.SpaceM, Ui.Spacer(), no, ok));
        no.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>存读档页：与标题页存档弹层同一套存档卡（缩略图、删除、覆盖确认）。</summary>
    private void ShowSlots(bool save)
    {
        Page(SlotsWidth, Height);
        var list = SaveSlotList.Build(_play.Saves, save, slot =>
        {
            if (!save)
            {
                return LoadSlot(slot);
            }

            var r = _play.Save(slot, _play.MenuFrame);
            return r.Ok ? $"已保存到{SlotName(slot)}" : $"保存失败：{r.Error}";
        }, save ? "只能保存到手动槽；快速槽由 F5 写入，自动槽在换图后轮换写入。" : "正式存档损坏时会读取上一份备份并提示。删除不影响当前进度。");

        // 底栏正在确认删除或覆盖时，Esc 先取消确认，再按一次才回主页。
        Back = () =>
        {
            if (!list.CancelConfirm())
            {
                ShowMain();
            }
        };
        _body.AddChild(Ui.Row(UiPalette.SpaceL, Ui.Text(save ? "保存进度" : "读取进度", UiTheme.DarkTitleLabel, 40), Ui.Spacer(),
            Ui.Button("返回", UiTheme.DarkButton, ShowMain)));
        _body.AddChild(Ui.Rule(dark: true));
        _body.AddChild(Ui.Expand(list.Cards, vertical: true));
        _body.AddChild(list.Status);
        _body.AddChild(list.Footer);
    }

    /// <summary>读档并进入探索；读不成时返回失败原因（不改当前进度），读成时离开当前页、返回 null。</summary>
    public static string? LoadSlot(SaveSlot slot)
    {
        string? message = null;
        LoadInto(slot, m => message = m);
        return message;
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

    /// <summary>读档并进入探索；不相容或读取失败时只提示，不改当前进度。从暂停菜单读档时先关菜单、解除暂停。</summary>
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
        AppHost.Instance.Menu.Close();
        AppHost.Instance.Play = play;
        AppHost.Instance.Router.GoTo(ScenePaths.Exploration);
    }
}
