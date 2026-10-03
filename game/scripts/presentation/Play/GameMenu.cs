using Godot;
using WuxiaWorld.Application.Persistence;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 暂停页（由 <see cref="PauseMenu"/> 承载，一局中任意时刻按 Esc 打开；UI_DESIGN 5.7，M3-05）：
/// 场景保留并虚化；左侧黛本竖栏列出 继续、保存、读取、人物、队伍、行囊、札记、设置、返回标题、退出游戏；
/// 右侧玻璃面板写当前所在、目标、同行、时辰与游戏时长。保存 / 读取的存档卡在右侧展开（竖栏留着，Esc 收回）；
/// 人物到设置五项进入全屏的分区菜单（<see cref="MenuFrame"/>），Esc 回到本页；返回标题与退出先经居中确认框。
/// 对话、换图或战斗进行中不能保存（架构文档 3、11），按钮置灰并在下方写明原因。
/// </summary>
public sealed class GameMenu
{
    private readonly PlaySession _play;
    private readonly Action _close;
    private readonly Action<MenuSection> _openSection;
    private readonly Control _root = Ui.MinSize(new Control(), 0);
    private readonly VBoxContainer _items = Ui.Column(0);
    private readonly Control _right = Ui.MinSize(new Control(), 0);
    private Control? _confirm;
    private Action? _beforeConfirm;
    private Button? _lastItem;
    private SaveSlotList? _slots;

    private GameMenu(PlaySession play, Action close, Action<MenuSection> openSection)
    {
        _play = play;
        _close = close;
        _openSection = openSection;
    }

    public const float ColumnWidth = 560;

    /// <summary>存读档面板宽，放得下带缩略图的存档卡。</summary>
    public const float SlotsWidth = 1180;

    public Control Root => _root;

    /// <summary>当前有可收回的东西（存档卡、确认框）时返回收回动作；在竖栏上时为 null（Esc 即关闭菜单）。</summary>
    public Action? Back { get; private set; }

    /// <param name="scene">打开菜单前一帧的游戏画面（虚化作底）。</param>
    /// <param name="slots">直接打开读取槽位（截图用）。</param>
    public static GameMenu Build(PlaySession play, Texture2D? scene, Action close, Action<MenuSection> openSection, bool slots = false)
    {
        var menu = new GameMenu(play, close, openSection);
        menu.Assemble(scene);
        if (slots)
        {
            menu.ShowSlots(save: false);
        }

        return menu;
    }

    private void Assemble(Texture2D? scene)
    {
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        if (scene is not null)
        {
            var blur = new TextureRect
            {
                Texture = scene,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            };
            blur.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _root.AddChild(blur);
        }

        // 遮罩比分区菜单轻：暂停时仍看得出身在何处。
        var veil = new ColorRect { Color = UiPalette.Abyss with { A = 0.5f } };
        veil.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(veil);

        var column = new PanelContainer { ThemeTypeVariation = UiTheme.DarkPanel };
        Ui.Place(column, 0, 0, -12, -12, ColumnWidth, 12);
        column.SetAnchor(Side.Bottom, 1);
        column.OffsetBottom = 12;
        column.AddChild(BuildColumn());
        _root.AddChild(column);
        Motion.Enter(column, 0, Motion.Normal, rise: 0, fromX: -40);

        _right.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _right.OffsetLeft = ColumnWidth;
        _right.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(_right);
        ShowInfo();
    }

    private Control BuildColumn()
    {
        var body = Ui.Column(UiPalette.SpaceM);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", 36);
        }

        margin.AddThemeConstantOverride("margin_top", 48);
        margin.AddThemeConstantOverride("margin_bottom", 36);
        margin.AddChild(body);

        body.AddChild(Ui.Row(UiPalette.SpaceL, Ui.Seal("暂歇"), Ui.Column(4,
            Ui.Text("江湖暂歇", UiTheme.DarkTitleLabel, 40),
            Ui.Text("游戏已暂停", UiTheme.DarkMutedLabel, 18))));
        body.AddChild(Ui.Rule(dark: true));
        body.AddChild(_items);

        Button Item(string text, Action action, bool disabled = false)
        {
            var b = Ui.Button(text, UiTheme.MenuItem, () => action(), disabled);
            b.CustomMinimumSize = new Vector2(0, 58);
            b.Alignment = HorizontalAlignment.Left;
            b.FocusEntered += () => _lastItem = b;
            _items.AddChild(b);
            return b;
        }

        var canSave = _play.Game.CanSave;
        var first = Item("继续", _close);
        Item("保存进度", () => ShowSlots(save: true), !canSave);
        if (!canSave)
        {
            // 置灰的原因直接写出来，不藏在悬停提示里（手柄、键盘都看得到）。
            _items.AddChild(Ui.Text("对话、换图或战斗进行中不能保存，告一段落后再存；换图时也会自动存档。", UiTheme.DarkMutedLabel, 16, wrap: true));
        }

        Item("读取进度", () => ShowSlots(save: false));
        Item("人物与武学", () => _openSection(MenuSection.Character));
        Item("队伍", () => _openSection(MenuSection.Party));
        Item("行囊", () => _openSection(MenuSection.Inventory));
        Item("江湖札记", () => _openSection(MenuSection.Journal));
        Item("江湖设置", () => _openSection(MenuSection.Settings));
        Item("返回标题", () => Confirm("返回标题", "回到标题", () =>
        {
            _close();
            AppHost.Instance.Play = null;
            AppHost.Instance.Router.GoTo(ScenePaths.MainMenu);
        }));
        Item("退出游戏", () => Confirm("退出游戏", "退出", () => _root.GetTree().Quit()));
        body.AddChild(Ui.Spacer(horizontal: false));
        body.AddChild(Ui.KeyHints(true, ("↑↓", "选择"), ("Enter", "确认"), ("Esc", "继续")));
        first.CallDeferred(Control.MethodName.GrabFocus);
        return margin;
    }

    /// <summary>从分区菜单回来时，焦点回到进入前的那一项。</summary>
    public void Refocus()
    {
        var target = _lastItem is { } b && GodotObject.IsInstanceValid(b) ? b : MenuFrame.FirstFocusable(_items);
        target?.CallDeferred(Control.MethodName.GrabFocus);
    }

    // ── 右侧：当前情形 ───────────────────────────────────

    private void ShowInfo()
    {
        Ui.ClearChildren(_right);
        _slots = null;
        Back = null;
        var w = _play.Game.World;
        var content = _play.Game.Rules.Content;
        var info = Ui.Column(UiPalette.SpaceM,
            Ui.Text("当前所在", UiTheme.DarkMutedLabel, 18),
            Ui.Text(_play.Name(w.MapId), UiTheme.DarkTitleLabel, 40),
            Ui.Text($"第一篇《众路归潮》　{SaveSlotList.ChapterText(w.ChapterId)}", UiTheme.DarkMutedLabel, 20),
            Ui.Rule(dark: true),
            Ui.Text("当前目标", UiTheme.DarkMutedLabel, 18));

        var main = content.Quests.Values.Where(q => q.Kind == QuestKind.Main && w.QuestStatusOf(q.Id) == QuestStatus.Active)
            .OrderBy(q => q.Priority).FirstOrDefault();
        if (main is not null && w.Quests[main.Id].Stage is { } stage)
        {
            info.AddChild(Ui.Text($"◆ {_play.Name(main.Id)}", UiTheme.GiltLabel, 20));
            info.AddChild(Ui.Text(_play.Text($"{main.Id}.stage.{stage}") ?? stage, UiTheme.DarkLabel, 22, wrap: true));
        }
        else
        {
            info.AddChild(Ui.Text(main is null ? "本章主线已了结。" : "—", UiTheme.DarkLabel, 22, wrap: true));
        }

        info.AddChild(Ui.Rule(dark: true));
        info.AddChild(Ui.Text("同行", UiTheme.DarkMutedLabel, 18));
        info.AddChild(Ui.Text(string.Join("　", w.Party.Select(_play.Name)), UiTheme.DarkLabel, 22, wrap: true));
        info.AddChild(Ui.Rule(dark: true));
        info.AddChild(Ui.Row(UiPalette.SpaceXl,
            Ui.Column(4, Ui.Text("江湖时辰", UiTheme.DarkMutedLabel, 18), Ui.Text(PlaySession.ClockText(w.Clock), UiTheme.DarkLabel, 22)),
            Ui.Column(4, Ui.Text("游戏时长", UiTheme.DarkMutedLabel, 18), Ui.Text(PlaySession.DurationText(_play.PlaySeconds), UiTheme.DarkLabel, 22)),
            Ui.Column(4, Ui.Text("银两", UiTheme.DarkMutedLabel, 18), Ui.Text($"{w.Silver} 两", UiTheme.DarkLabel, 22))));

        var panel = Ui.Panel(UiTheme.GlassPanel, info);
        Ui.Place(panel, 1, 0.5f, -720, -300, -96, 300);
        panel.GrowVertical = Control.GrowDirection.Both;
        _right.AddChild(panel);
        Motion.Enter(panel, 0.04f, Motion.Normal, rise: 0, fromX: 40);
    }

    // ── 存读档 ───────────────────────────────────────────

    /// <summary>存读档：与标题页存档弹层同一套存档卡（缩略图、删除、覆盖确认），在竖栏右侧展开。</summary>
    private void ShowSlots(bool save)
    {
        Ui.ClearChildren(_right);
        var list = SaveSlotList.Build(_play.Saves, save, slot =>
        {
            if (!save)
            {
                return LoadSlot(slot);
            }

            var r = _play.Save(slot, _play.MenuFrame);
            return r.Ok ? $"已保存到{SlotName(slot)}" : $"保存失败：{r.Error}";
        }, save ? "只能保存到手动槽；快速槽由 F5 写入，自动槽在换图后轮换写入。" : "正式存档损坏时会读取上一份备份并提示。删除不影响当前进度。");
        _slots = list;

        // 底栏正在确认删除或覆盖时，Esc 先取消确认，再按一次才收起存档卡。
        Back = () =>
        {
            if (!list.CancelConfirm())
            {
                ShowInfo();
                Refocus();
            }
        };
        var panel = new PanelContainer { ThemeTypeVariation = UiTheme.DarkPanel };
        panel.AddChild(Ui.Column(UiPalette.SpaceM,
            Ui.Row(UiPalette.SpaceL, Ui.Text(save ? "保存进度" : "读取进度", UiTheme.DarkTitleLabel, 40), Ui.Spacer(),
                Ui.Button("收起", UiTheme.DarkButton, () => Back?.Invoke())),
            Ui.Rule(dark: true),
            Ui.Expand(list.Cards, vertical: true),
            list.Status,
            list.Footer));
        Ui.Place(panel, 0.5f, 0, -SlotsWidth / 2, 56, SlotsWidth / 2, 0);
        panel.SetAnchor(Side.Bottom, 1);
        panel.OffsetBottom = -56;
        _right.AddChild(panel);
        Motion.Enter(panel, 0, Motion.Normal, rise: 0, fromX: 40);
    }

    // ── 确认 ─────────────────────────────────────────────

    /// <summary>居中确认框（UI_DESIGN 5.8）：标题、一句后果说明，“再想想”在左、确认在右，焦点默认在“再想想”，Esc 等于取消。</summary>
    private void Confirm(string title, string yesText, Action yes)
    {
        var shade = new ColorRect { Color = UiPalette.Abyss with { A = 0.5f } };
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var panel = new PanelContainer { ThemeTypeVariation = UiTheme.DarkPanel };
        var ok = Ui.Button(yesText, UiTheme.PrimaryButton, yes);
        var no = Ui.Button("再想想", UiTheme.DarkButton, CloseConfirm);
        foreach (var b in new[] { ok, no })
        {
            b.CustomMinimumSize = new Vector2(200, 56);
        }

        panel.AddChild(Ui.Column(UiPalette.SpaceM,
            Ui.Text(title, UiTheme.DarkTitleLabel, 40),
            Ui.Rule(dark: true),
            Ui.Text("上次存档之后的进度不会保留。换图时会自动存档，也可以先手动保存。", UiTheme.DarkLabel, 22, wrap: true),
            Ui.Spacer(horizontal: false),
            Ui.Row(UiPalette.SpaceM, Ui.Spacer(), no, ok)));
        Ui.Place(panel, 0.5f, 0.5f, -340, -170, 340, 170);
        var layer = new Control();
        layer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(shade);
        layer.AddChild(panel);
        _root.AddChild(layer);
        Motion.Enter(panel, 0, Motion.Quick, rise: 12);
        CloseConfirm();
        _confirm = layer;
        _beforeConfirm = Back;
        Back = CloseConfirm;
        no.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void CloseConfirm()
    {
        if (_confirm is { } layer)
        {
            _confirm = null;
            layer.QueueFree();
            Back = _beforeConfirm;
            _beforeConfirm = null;
            Refocus();
        }
    }

    // ── 存档的公共文字与读档（标题页、探索页共用）──────────

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
