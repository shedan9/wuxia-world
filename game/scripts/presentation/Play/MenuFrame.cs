using Godot;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Scenery;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>游戏菜单的分区；顺序即 Q / E 切换顺序。</summary>
public enum MenuSection
{
    Character,
    Party,
    Inventory,
    Journal,
    Settings,
}

/// <summary>
/// 游戏菜单外框（M3-05，UI_DESIGN 5.2，版式沿用 M0 已验收的 <c>PreviewScreen</c>）：虚化的游戏画面加玄黛遮罩；
/// 顶栏左为页名章、页名与说明，中为分区签（两端 Q / E 键帽，可改键），右为地点、时辰与银两；中部为各分区自己的绢本页；
/// 底栏为键帽提示。分区之间立即切换，不走色幕、不做整页入场（2026-09-30 用户要求）。
/// 探索中按人物 / 队伍 / 行囊 / 札记的快捷键直接打开对应分区，再按同一键或 Esc 关闭；从暂停菜单进入时 Esc 回到暂停菜单。
/// 标题页的“江湖设置”也用此框，只有设置一个分区（<paramref name="play"/> 为 null）。
/// </summary>
public sealed class MenuFrame
{
    private sealed record Info(MenuSection Section, string Name, string Seal, string Title, string Note, string? HotKey);

    private static readonly Info[] All =
    [
        new(MenuSection.Character, "人物", "人物", "人物与武学", "属性与潜能、武学装配与修炼、装备", "open_character"),
        new(MenuSection.Party, "队伍", "队伍", "队伍与同行", "阵位、同行人物与信任", "open_party"),
        new(MenuSection.Inventory, "行囊", "行囊", "行囊", "随身物品、药品与任务物件", "open_inventory"),
        new(MenuSection.Journal, "札记", "札记", "江湖札记", "任务、线索、人物、见闻与对话记录", "open_journal"),
        new(MenuSection.Settings, "设置", "设置", "江湖设置", "声音、显示、文字、辅助与按键；改动立即生效", null),
    ];

    private readonly PlaySession? _play;
    private readonly Info[] _sections;
    private readonly Control _root = Ui.MinSize(new Control(), 0);
    private readonly MarginContainer _content = Ui.Expand(new MarginContainer(), vertical: true);
    private readonly HBoxContainer _header = Ui.Row(UiPalette.SpaceL);
    private readonly HBoxContainer _footer = Ui.Row(UiPalette.SpaceL);
    private readonly string _backLabel;
    private Info _current;

    private MenuFrame(PlaySession? play, MenuSection section, Texture2D? scene, string backLabel)
    {
        _play = play;
        _sections = play is null ? [All[^1]] : All;
        _current = _sections.FirstOrDefault(s => s.Section == section) ?? _sections[0];
        _backLabel = backLabel;

        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        if (scene is not null)
        {
            // 游戏画面缩成小图后再拉伸，线性过滤即成虚化底；上压玄黛遮罩，与 M0 菜单的“虚化山水 + 遮罩 72%”同一观感。
            var blur = new TextureRect
            {
                Texture = scene,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            };
            blur.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _root.AddChild(blur);
            var veil = new ColorRect { Color = UiPalette.Abyss with { A = 0.72f } };
            veil.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _root.AddChild(veil);
        }
        else
        {
            _root.AddChild(Backdrop.Veiled());
        }

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 56);
        margin.AddThemeConstantOverride("margin_right", 56);
        margin.AddThemeConstantOverride("margin_top", 28);
        margin.AddThemeConstantOverride("margin_bottom", 22);
        margin.AddChild(Ui.Column(UiPalette.SpaceL, _header, _content, _footer));
        _root.AddChild(margin);
    }

    public Control Root => _root;

    public MenuSection Section => _current.Section;

    /// <summary>当前分区的页面控件（焦点检查用）。</summary>
    public Control? Page => _content.GetChildCount() > 0 && _content.GetChild(0).GetChildCount() > 0 ? _content.GetChild(0).GetChild<Control>(0) : null;

    /// <summary>Esc（或右键）时调用：由承载层决定回暂停菜单还是关闭。</summary>
    public Action? Back { get; set; }

    /// <param name="scene">打开菜单前一帧的游戏画面（虚化作底）；null 时用虚化山水。</param>
    /// <param name="backLabel">底栏 Esc 键帽后的说明，如“返回游戏”“返回菜单”。</param>
    /// <param name="tab">进入时的子页签（人物页 0 属性、1 武学、2 装备；札记、设置按各自顺序）；null 为默认。</param>
    public static MenuFrame Build(PlaySession? play, MenuSection section, Texture2D? scene, string backLabel, int? tab = null)
    {
        var frame = new MenuFrame(play, section, scene, backLabel);
        frame.Show(frame._current, entering: true, tab);
        return frame;
    }

    /// <summary>把视口上一帧缩成小图（虚化底用）；抓不到时返回 null。</summary>
    public static Texture2D? Blurred(Viewport viewport)
    {
        var image = viewport.GetTexture()?.GetImage();
        if (image is null || image.IsEmpty())
        {
            return null;
        }

        // 先用 Lanczos 缩到约 1/12（按面积取平均，不出锯齿），再在内存里用双线性放大回半幅：
        // 模糊在图里做完，不依赖显示时的过滤方式（直接放大小图会出马赛克块）。
        var (w, h) = (image.GetWidth(), image.GetHeight());
        image.Convert(Image.Format.Rgb8);
        image.Resize(Math.Max(1, w / 12), Math.Max(1, h / 12), Image.Interpolation.Lanczos);
        image.Resize(Math.Max(1, w / 2), Math.Max(1, h / 2), Image.Interpolation.Bilinear);
        return ImageTexture.CreateFromImage(image);
    }

    public void Show(MenuSection section)
    {
        if (_sections.FirstOrDefault(s => s.Section == section) is { } info)
        {
            Show(info, entering: false);
        }
    }

    private void Show(Info info, bool entering, int? tab = null)
    {
        var changed = info != _current;
        _current = info;
        BuildHeader(entering);
        BuildFooter();
        Ui.ClearChildren(_content);
        var page = info.Section switch
        {
            MenuSection.Character => CharacterPage.Build(_play!, tab ?? 0),
            MenuSection.Party => PartyPage.Build(_play!),
            MenuSection.Inventory => InventoryPage.Build(_play!),
            MenuSection.Journal => JournalPage.Build(_play!, tab),
            _ => SettingsPanel.Build(Rebuild, tab),
        };
        // 页面放进竖向滚动区：平时页面正好铺满；字号放大后页面比画面高时可滚动，焦点移到哪里就滚到哪里。
        // 横向不滚动、也不把页面的最小宽度往上传（ShowNever）：页面再宽也不撑开外框，越界由焦点走查（--focus-audit）报出来再改页面。
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever, FollowFocus = true };
        scroll.AddChild(Ui.Expand(page, vertical: true));
        _content.AddChild(scroll);
        if (entering)
        {
            Motion.Enter(page, 0.05f, Motion.Normal, rise: 20);
        }

        FocusFirst(page);
        Settle(scroll);
        if (changed)
        {
            AppHost.Instance.Sound.Play("ui.page", -4);
        }
    }

    /// <summary>只重建顶栏与底栏（银两变了、改了键）；页面与焦点不动。</summary>
    public void RefreshHeader()
    {
        BuildHeader(entering: false);
        BuildFooter();
    }

    /// <summary>字号等设置改动后整框重建（主题已换，旧控件的字号覆盖不会自己变）。</summary>
    private void Rebuild() => Show(_current, entering: false);

    /// <summary>
    /// 页面刚建好时排版还没落定，滚动区按焦点自动滚动会滚过头（字号放大后页面顶部被裁）：
    /// 等两帧排版落定，先回到顶部；焦点所在的控件这时完全看不见才滚过去。
    /// </summary>
    private static async void Settle(ScrollContainer scroll)
    {
        // 外框建好时还没进场景树：取主循环的场景树等帧。
        var tree = (SceneTree)Engine.GetMainLoop();
        for (var i = 0; i < 2; i++)
        {
            await scroll.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (!GodotObject.IsInstanceValid(scroll) || !scroll.IsInsideTree())
            {
                return;
            }
        }

        scroll.ScrollVertical = 0;

        // 顶部优先：焦点项在第一屏里露得出来就不再滚（整块控件比如阵位格可能比可见区高），完全看不见时才滚过去。
        if (scroll.GetViewport().GuiGetFocusOwner() is { } owner && scroll.IsAncestorOf(owner)
            && !scroll.GetGlobalRect().Intersects(owner.GetGlobalRect()))
        {
            scroll.EnsureControlVisible(owner);
        }
    }

    /// <summary>页面自己没抢焦点时，焦点落在页面的第一个可聚焦控件上（键盘、手柄一进来就有“当前项”）。</summary>
    public static void FocusFirst(Control page)
    {
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(page) || !page.IsInsideTree())
            {
                return;
            }

            var owner = page.GetViewport().GuiGetFocusOwner();
            if (owner is not null && GodotObject.IsInstanceValid(owner) && page.IsAncestorOf(owner) && owner.IsVisibleInTree())
            {
                return;
            }

            FirstFocusable(page)?.GrabFocus();
        }).CallDeferred();
    }

    public static Control? FirstFocusable(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Control { Visible: true } c)
            {
                if (c.FocusMode == Control.FocusModeEnum.All && c is not BaseButton { Disabled: true })
                {
                    // 一组单选（页签、筛选签）里先落在已选中的那一个上。
                    return c is BaseButton { ButtonGroup: { } group } && group.GetPressedButton() is Control { Visible: true } pressed ? pressed : c;
                }

                if (FirstFocusable(c) is { } inner)
                {
                    return inner;
                }
            }
        }

        return null;
    }

    private void BuildHeader(bool entering)
    {
        Ui.ClearChildren(_header);
        // 说明与地点超长（字号放大）时以省略号收尾，不把顶栏撑宽、把底栏挤出画面。
        var note = Ui.Text(_current.Note, UiTheme.DarkMutedLabel);
        note.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        note.TooltipText = _current.Note;
        var heading = Ui.Expand(Ui.Column(4, Ui.Text(_current.Title, UiTheme.DarkTitleLabel, 38), note));
        heading.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var left = Ui.Row(UiPalette.SpaceL, Ui.Seal(_current.Seal), heading);

        var nav = Ui.Row(UiPalette.SpaceS);
        if (_sections.Length > 1)
        {
            nav.AddChild(Cap(KeyBindings.Label("section_prev")));
            var group = new ButtonGroup();
            foreach (var s in _sections)
            {
                var target = s;
                var tab = Ui.Button(s.Name, UiTheme.NavTab, () => Show(target, entering: false));
                tab.ToggleMode = true;
                tab.ButtonGroup = group;
                tab.ButtonPressed = s == _current;
                tab.FocusMode = Control.FocusModeEnum.None;
                tab.CustomMinimumSize = new Vector2(112, 0);
                nav.AddChild(tab);
            }

            nav.AddChild(Cap(KeyBindings.Label("section_next")));
        }

        nav.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

        var status = Ui.Column(4);
        if (_play is not null)
        {
            var w = _play.Game.World;
            var place = Ui.Text($"{_play.Name(w.MapId)}　·　{PlaySession.ClockText(w.Clock)}", UiTheme.DarkMutedLabel);
            place.HorizontalAlignment = HorizontalAlignment.Right;
            place.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            var money = Ui.Row(UiPalette.SpaceS, Ui.Glyph("银", UiPalette.Gilt.Darkened(0.2f), 30), Ui.Text($"{w.Silver} 两", UiTheme.DarkLabel));
            money.Alignment = BoxContainer.AlignmentMode.End;
            status.AddChild(place);
            status.AddChild(money);
        }

        status.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

        // 左右两块等宽扩展，分区签保持居中。
        _header.AddChild(Ui.Expand(left));
        _header.AddChild(nav);
        _header.AddChild(Ui.Expand(status));
        if (entering)
        {
            Motion.Enter(_header, 0, Motion.Normal, rise: -12);
        }
    }

    private void BuildFooter()
    {
        Ui.ClearChildren(_footer);
        var hints = new List<(string, string)>();
        if (_sections.Length > 1)
        {
            hints.Add(($"{KeyBindings.Label("section_prev")}/{KeyBindings.Label("section_next")}", "切换分区"));
        }

        if (_current.Section is MenuSection.Character or MenuSection.Inventory or MenuSection.Journal or MenuSection.Settings)
        {
            hints.Add(("PgUp/PgDn", "切换页签"));
        }

        _footer.AddChild(Ui.Spacer());
        _footer.AddChild(Ui.KeyHints(true, hints.ToArray()));
        _footer.AddChild(Ui.KeyActions(true, ("Esc", _backLabel, () => Back?.Invoke())));
    }

    private static Control Cap(string key)
    {
        var cap = Ui.KeyHint(key, "");
        cap.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return cap;
    }

    /// <summary>
    /// 外框的按键：Q / E 换分区，PgUp / PgDn 换当前页的子页签，人物 / 队伍 / 行囊 / 札记键跳到对应分区（已在该分区时关闭），Esc 返回。
    /// 返回是否已处理。页内控件（列表、滑杆、改键按钮）先于此处收到按键。
    /// </summary>
    public bool HandleKey(InputEvent e)
    {
        if (e.IsActionPressed("ui_cancel"))
        {
            Back?.Invoke();
            return true;
        }

        if (_sections.Length > 1 && (KeyBindings.Pressed(e, "section_prev") || KeyBindings.Pressed(e, "section_next")))
        {
            var step = KeyBindings.Pressed(e, "section_next") ? 1 : -1;
            var index = Array.IndexOf(_sections, _current);
            Show(_sections[(index + step + _sections.Length) % _sections.Length], entering: false);
            return true;
        }

        var page = e.IsActionPressed("ui_page_down") ? 1 : e.IsActionPressed("ui_page_up") ? -1 : 0;
        if (page != 0)
        {
            return CycleSubTab(page);
        }

        foreach (var s in _sections)
        {
            if (s.HotKey is { } hot && KeyBindings.Pressed(e, hot))
            {
                if (s == _current)
                {
                    Back?.Invoke();
                }
                else
                {
                    Show(s, entering: false);
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>按下当前页的上一个 / 下一个子页签（绢页顶部的书法页签）。</summary>
    private bool CycleSubTab(int step)
    {
        var tabs = new List<Button>();
        Collect(_content, tabs);
        if (tabs.Count < 2)
        {
            return false;
        }

        var index = Math.Max(0, tabs.FindIndex(t => t.ButtonPressed));
        var target = tabs[(index + step + tabs.Count) % tabs.Count];
        target.ButtonPressed = true;

        // 有的页按页签会整页重建（旧页签随之释放）：等重建完，把焦点给新的当前页签。
        Callable.From(() =>
        {
            var fresh = new List<Button>();
            Collect(_content, fresh);
            fresh.FirstOrDefault(t => t.ButtonPressed && t.IsInsideTree())?.GrabFocus();
        }).CallDeferred();
        AppHost.Instance.Sound.Play("ui.page", -6);
        return true;

        static void Collect(Node node, List<Button> into)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is Button { Visible: true, Disabled: false } b && b.ThemeTypeVariation == UiTheme.SubTab)
                {
                    into.Add(b);
                }
                else if (child is Control { Visible: true })
                {
                    Collect(child, into);
                }
            }
        }
    }
}
