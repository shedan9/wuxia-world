using Godot;
using WuxiaWorld.Application.Persistence;
using WuxiaWorld.Game.Adapters;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Scenery;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 存档卡列表（标题页存档弹层与游戏菜单的存读档页共用，版式见 docs/art/UI_DESIGN.md 5.1）：
/// 每张卡含缩略图（存档时的画面；没有时按地图取色的程序化山水）、槽位、章节、地点、江湖时辰与写入时间。
/// 读取模式按写入先后列出全部槽位；保存模式只列 10 个手动槽，覆盖已有存档前先确认。
/// 鼠标悬停或方向键选卡，双击 / Enter / 主按钮执行，Delete 或“删除”按钮删除——删除与覆盖都在底栏原地确认，不另弹窗。
/// </summary>
public sealed class SaveSlotList
{
    private readonly ISaveStore _store;
    private readonly bool _save;
    private readonly Func<SaveSlot, string?> _act;
    private readonly VBoxContainer _cards = Ui.Column(12);
    private readonly HBoxContainer _footer = Ui.Row(UiPalette.SpaceM);
    private readonly Label _status;
    private readonly ScrollContainer _scroll;
    private Dictionary<SaveSlot, SlotSummary> _summaries = [];
    private SaveSlot? _selected;
    private SaveSlot? _focusAfterRefresh;
    private bool _confirming;
    private readonly string _hint;

    /// <param name="act">读取或保存所选槽位；返回要显示在状态行的结果（读档成功会离开当前页，可返回 null）。</param>
    private SaveSlotList(ISaveStore store, bool save, Func<SaveSlot, string?> act, string hint)
    {
        _store = store;
        _save = save;
        _act = act;
        _hint = hint;
        _status = Ui.Text(hint, UiTheme.DarkMutedLabel, 16, wrap: true);

        _scroll = Scroll(_cards);
    }

    /// <summary>卡片放进滚动区：面板高度固定，槽位再多也不撑出面板。内缩一圈留给卡片的焦点折角与投影，免得被滚动区裁掉。</summary>
    private static ScrollContainer Scroll(Control cards)
    {
        var inset = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
        {
            inset.AddThemeConstantOverride($"margin_{side}", 10);
        }

        inset.AddChild(Ui.Expand(cards));
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        scroll.AddChild(Ui.Expand(inset));
        return scroll;
    }

    /// <summary>卡片滚动区（纵向撑满）。</summary>
    public Control Cards => _scroll;

    /// <summary>底栏：键帽提示、删除与读取 / 保存按钮；确认时原地换成问句与两个按钮。</summary>
    public Control Footer => _footer;

    /// <summary>状态行：存读档与删除的结果。</summary>
    public Label Status => _status;

    public int UsedCount => _summaries.Values.Count(s => s.Header is not null);

    /// <summary>每次重读槽位、重建卡片之后（保存、删除后更新“已用”计数等）。</summary>
    public event Action? Refreshed;

    /// <summary>底栏正在确认删除或覆盖时取消确认并返回 true；没有确认在进行时返回 false（Esc 交给外层）。</summary>
    public bool CancelConfirm()
    {
        if (!_confirming)
        {
            return false;
        }

        Refresh(animate: false);
        return true;
    }

    public static SaveSlotList Build(ISaveStore store, bool save, Func<SaveSlot, string?> act, string hint)
    {
        var list = new SaveSlotList(store, save, act, hint);
        list.Refresh();
        return list;
    }

    public void Say(string text)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", UiPalette.Gilt);
    }

    /// <summary>重读槽位并重建卡片；焦点留在原来选中的槽位（没有则第一张）。</summary>
    public void Refresh(bool animate = true)
    {
        _summaries = _store.List().ToDictionary(s => s.Slot);
        var order = _save
            ? _summaries.Values.Where(s => s.Slot.Kind == SlotKind.Manual).OrderBy(s => s.Slot.Index)
            : _summaries.Values.OrderByDescending(s => s.Header?.Sequence ?? -1).ThenBy(s => s.Slot.Kind).ThenBy(s => s.Slot.Index);
        Ui.ClearChildren(_cards);
        var group = new ButtonGroup();
        Button? first = null;
        Button? keep = null;
        foreach (var summary in order)
        {
            var card = Card(summary, group);
            _cards.AddChild(card);
            first ??= card;
            if (summary.Slot == (_focusAfterRefresh ?? _selected))
            {
                keep = card;
            }
        }

        _focusAfterRefresh = null;
        if (_confirming)
        {
            _confirming = false;
            _status.Text = _hint;
            _status.RemoveThemeColorOverride("font_color");
        }

        if (animate)
        {
            Motion.Stagger(_cards.GetChildren().OfType<Control>(), 0.08f, 0.05f, rise: 12);
        }

        ShowFooter();
        if ((keep ?? first) is { } focus)
        {
            focus.ButtonPressed = true;
            focus.CallDeferred(Control.MethodName.GrabFocus);
            if (keep is null)
            {
                // 首次布局前取焦点会让滚动区按未定的尺寸跟随焦点，最上面一张被滚出视野；布局后回到顶端。
                _scroll.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, 0);
            }
        }

        Refreshed?.Invoke();
    }

    private SlotSummary? Selected => _selected is { } s ? _summaries.GetValueOrDefault(s) : null;

    private bool CanAct(SlotSummary? s) => s is not null && (_save || s.Header is not null);

    /// <summary>有存档文件（含读不出的坏档）才可删除。</summary>
    private static bool CanDelete(SlotSummary? s) => s is not null && (s.Header is not null || s.Problem is not null);

    private void ShowFooter()
    {
        Ui.ClearChildren(_footer);
        var selected = Selected;
        var verb = _save ? "保存" : "读取";
        var remove = Ui.MinSize(Ui.Button("删除", UiTheme.DarkButton, AskDelete, disabled: !CanDelete(selected)), 140, 56);
        var act = Ui.MinSize(Ui.Button(verb, UiTheme.PrimaryButton, Act, disabled: !CanAct(selected)), 180, 56);
        _footer.AddChild(Ui.KeyHints(true, ("↑↓", "选择"), ("Enter", verb), ("Del", "删除"), ("Esc", "返回")));
        _footer.AddChild(Ui.Spacer());
        _footer.AddChild(remove);
        _footer.AddChild(act);
    }

    /// <summary>问句写在状态行（面板宽度内换行，不压底栏纹饰），底栏换成键帽与“再想想 / 确认”两个按钮，焦点落在“再想想”。</summary>
    private void Confirm(string question, string yesText, Action yes)
    {
        Ui.ClearChildren(_footer);
        _confirming = true;
        _status.Text = question;
        _status.AddThemeColorOverride("font_color", UiPalette.TextOnDark);
        var no = Ui.MinSize(Ui.Button("再想想", UiTheme.DarkButton, () => Refresh(animate: false)), 140, 56);
        var ok = Ui.MinSize(Ui.Button(yesText, UiTheme.PrimaryButton, yes), 180, 56);
        _footer.AddChild(Ui.KeyHints(true, ("←→", "选择"), ("Enter", "确认"), ("Esc", "取消")));
        _footer.AddChild(Ui.Spacer());
        _footer.AddChild(no);
        _footer.AddChild(ok);
        no.CallDeferred(Control.MethodName.GrabFocus);
        AppHost.Instance.Sound.Play("ui.open", -8);
    }

    private void Act()
    {
        if (Selected is not { } s || !CanAct(s))
        {
            return;
        }

        if (_save && s.Header is not null)
        {
            Confirm($"覆盖{GameMenu.SlotName(s.Slot)}的存档（{MapName(s.Header.MapId)}　{s.Header.CreatedAt}）？", "覆盖", () => Run(s.Slot));
            return;
        }

        Run(s.Slot);
    }

    private void Run(SaveSlot slot)
    {
        var message = _act(slot);
        if (message is null)
        {
            return;
        }

        _focusAfterRefresh = slot;
        Refresh(animate: false);
        Say(message);
    }

    private void AskDelete()
    {
        if (Selected is not { } s || !CanDelete(s))
        {
            return;
        }

        var what = s.Header is { } h ? $"（{MapName(h.MapId)}　{h.CreatedAt}）" : "（无法读取的存档）";
        Confirm($"删除{GameMenu.SlotName(s.Slot)}的存档{what}？连同备份与缩略图一并删除，不能撤销。", "删除", () =>
        {
            var r = _store.Delete(s.Slot);

            // 保存页焦点留在原槽位（可直接再存）；读档页空槽排到后面，焦点回到最新的一份。
            _focusAfterRefresh = _save ? s.Slot : null;
            _selected = _save ? s.Slot : null;
            Refresh(animate: false);
            Say(r.Ok ? $"已删除{GameMenu.SlotName(s.Slot)}" : r.Error ?? "删除失败");
        });
    }

    private Button Card(SlotSummary summary, ButtonGroup group)
    {
        var card = new Button
        {
            ThemeTypeVariation = UiTheme.CardButton, ToggleMode = true, ButtonGroup = group,
            CustomMinimumSize = new Vector2(0, 156), FocusMode = Control.FocusModeEnum.All,
        };
        card.MouseEntered += card.GrabFocus;
        card.FocusEntered += () =>
        {
            card.ButtonPressed = true;
            if (_selected != summary.Slot)
            {
                _selected = summary.Slot;
                ShowFooter();
            }
        };
        card.GuiInput += e =>
        {
            // 在按钮自己的处理之前接住：Enter 执行、Delete 删除、双击执行；单击只选中。
            switch (e)
            {
                case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter }:
                    Act();
                    card.AcceptEvent();
                    break;
                case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Delete }:
                    AskDelete();
                    card.AcceptEvent();
                    break;
                case InputEventMouseButton { Pressed: true, DoubleClick: true, ButtonIndex: MouseButton.Left }:
                    Act();
                    card.AcceptEvent();
                    break;
            }
        };

        var content = new MarginContainer();
        content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach (var (side, value) in new[] { ("left", 18), ("right", 24), ("top", 16), ("bottom", 16) })
        {
            content.AddThemeConstantOverride($"margin_{side}", value);
        }

        card.AddChild(content);

        if (summary.Header is not { } h)
        {
            var text = summary.Problem is { } problem
                ? $"{GameMenu.SlotName(summary.Slot)}　·　无法读取：{problem}"
                : $"—　{GameMenu.SlotName(summary.Slot)}　空白存档位　—";
            var empty = Ui.Text(text, UiTheme.DarkMutedLabel, 22);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.VerticalAlignment = VerticalAlignment.Center;
            content.AddChild(empty);
            Ui.IgnoreMouse(content);
            card.CustomMinimumSize = new Vector2(0, 84);
            return card;
        }

        var thumbFrame = new PanelContainer { CustomMinimumSize = new Vector2(224, 126) };
        thumbFrame.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            Border = UiPalette.Gilt with { A = 0.75f }, BorderWidth = 1.1f, Brush = true, Overshoot = 0.4f, Seed = 69,
            Corners = CornerStyle.Bracket, CornerSize = 12, CornerWidth = 1.6f,
        }.Margins(3, 3));
        thumbFrame.AddChild(Thumbnail(summary, h));
        thumbFrame.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

        var tag = Ui.Text(GameMenu.SlotName(summary.Slot) + (summary.BackupOnly ? "　·　正式存档损坏，可读备份" : ""), UiTheme.GiltLabel, 18);
        var chapter = Ui.Text(ChapterText(h.ChapterId), UiTheme.DarkTitleLabel, 28);
        var place = Ui.Text(MapName(h.MapId), UiTheme.DarkMutedLabel, 18);
        var info = Ui.Expand(Ui.Column(6, tag, chapter, place));
        info.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

        var time = Ui.Text(PlaySession.ClockText(h.Clock), UiTheme.DarkLabel, 26);
        time.AddThemeFontOverride("font", UiFonts.Title);
        time.HorizontalAlignment = HorizontalAlignment.Right;
        var date = Ui.Text(h.CreatedAt, UiTheme.DarkMutedLabel, 17);
        date.HorizontalAlignment = HorizontalAlignment.Right;
        var meta = Ui.Column(4, Ui.Text("江湖时辰", UiTheme.DarkMutedLabel, 16), time, date);
        meta.GetChild<Label>(0).HorizontalAlignment = HorizontalAlignment.Right;
        meta.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

        content.AddChild(Ui.Row(UiPalette.SpaceL, thumbFrame, info, meta));
        Ui.IgnoreMouse(content);
        return card;
    }

    /// <summary>存档时的画面；没有（旧存档、抓图失败、文件损坏）时以按地图取色的山水代替。</summary>
    private Control Thumbnail(SlotSummary summary, SaveHeader h)
    {
        if (SaveThumbnail.Load(_store.ReadThumbnail(summary.Slot, h.Sequence)) is { } texture)
        {
            return new TextureRect
            {
                Texture = texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, ClipContents = true,
            };
        }

        // string.GetHashCode 每次运行随机，这里按字符累加取稳定的取色种子。
        var seed = h.MapId.Aggregate(17, (acc, c) => (acc * 31 + c) % 1000) / 1000f;
        var still = Backdrop.Still(0.2f + seed * 0.6f, seed * 7, 0.2f);
        still.ClipContents = true;
        return still;
    }

    public static string MapName(string mapId) => GeneratedContent.World?.Name(mapId) ?? mapId;

    /// <summary>章节 ID 的显示：chapter.02 → 第二章。</summary>
    public static string ChapterText(string chapterId) =>
        int.TryParse(chapterId.AsSpan(chapterId.LastIndexOf('.') + 1), out var n) && n is > 0 and < 10
            ? $"第{"一二三四五六七八九"[n - 1]}章"
            : chapterId;
}
