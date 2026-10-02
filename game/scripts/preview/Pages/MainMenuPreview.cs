using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Application.Persistence;
using WuxiaWorld.Game.Adapters;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Play;
using WuxiaWorld.Game.Presentation.Scenery;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 标题页（主菜单与存档页），版式见 docs/art/UI_DESIGN.md 第 5.1 节。
/// 三种状态：按键开始 → 主菜单 → 存档弹层。截图参数 <c>--tab</c>：0 主菜单、1 按键开始、2 存档弹层。
/// M2 起接上真实存档：“继续旅程”读最近写入的一份存档，“新的旅程”开新游戏，“读取存档”列出全部槽位（user://saves）；
/// M0 场景目录（展示页与战斗原型）改由“场景目录”进入。
/// </summary>
public partial class MainMenuPreview : Control
{
    private const string PortraitPath = "res://assets/portraits/lu_qinghe_v1.png";

    private Control _pressStart = null!;
    private Control _menu = null!;
    private Control _portrait = null!;
    private Control _footer = null!;
    private Control? _saves;
    private Button _first = null!;
    private Button _continue = null!;
    private Label _status = null!;
    private SaveSlotList? _slotList;
    private Vector2 _parallax;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // 回到标题即结束当前这一局（未存的进度由菜单的确认提示过）；场景目录与展示页不属于游戏进行中。
        AppHost.Instance.Play = null;
        AppHost.Instance.Sound.PlayMusic("bgm.town.luwan");
        AppHost.Instance.Sound.PlayAmbience();
        AddChild(Backdrop.Clear());
        AddChild(BuildPortrait());
        AddChild(BuildFooterShade());
        AddChild(BuildLogo());

        _menu = BuildMenu();
        _menu.Visible = false;
        AddChild(_menu);

        _pressStart = BuildPressStart();
        AddChild(_pressStart);
        _footer = BuildFooter();
        AddChild(_footer);

        switch (DevCapture.Tab)
        {
            case 1:
                break;
            case 2:
                ShowMenu();
                OpenSaves();
                break;
            default:
                ShowMenu();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (!Motion.Enabled || Size.X <= 0)
        {
            return;
        }

        // 立绘视差：比背景山水移得多，人物站在景前。
        var mouse = (GetLocalMousePosition() / Size - new Vector2(0.5f, 0.5f)).Clamp(new Vector2(-0.5f, -0.5f), new Vector2(0.5f, 0.5f));
        var target = -mouse * new Vector2(28, 12);
        _parallax = _parallax.Lerp(target, (float)Mathf.Min(1, delta * 4));
        _portrait.Position = _parallax;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_pressStart.Visible && @event is InputEventKey { Pressed: true } or InputEventMouseButton { Pressed: true }
            or InputEventJoypadButton { Pressed: true })
        {
            ShowMenu();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("ui_cancel") && _saves is not null)
        {
            // 底栏正在确认删除时 Esc 只取消确认。
            if (_slotList?.CancelConfirm() != true)
            {
                CloseSaves();
            }

            GetViewport().SetInputAsHandled();
        }
    }

    private void ShowMenu()
    {
        _pressStart.Visible = false;
        _menu.Visible = true;
        Motion.Stagger(_menu.GetChildren().OfType<Control>(), 0.05f, 0.06f, rise: 0, fromX: -24);
        _first.GrabFocus();
    }

    // ── 立绘 ──────────────────────────────────────────────

    private Control BuildPortrait()
    {
        var root = new Control { MouseFilter = MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // 身后一团水白柔光，把人物从山水里托出来。
        var glow = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Width = 256, Height = 256, Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
                Gradient = new Gradient { Colors = [UiPalette.Surface with { A = 0.7f }, UiPalette.Surface with { A = 0 }] },
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        Ui.Place(glow, 1, 0, -1200, -60, 50, 1140);
        root.AddChild(glow);

        var art = new TextureRect
        {
            Texture = GD.Load<Texture2D>(PortraitPath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        Ui.Place(art, 1, 0, -900, 56, 0, 1372);
        root.AddChild(art);

        // 竖排名牌：印章写名，薄玻璃条写身份。
        var name = Ui.Seal("陆青禾");
        var role = Ui.Text(Ui.Vertical("芦湾渡工学徒"), UiTheme.DarkMutedLabel);
        role.HorizontalAlignment = HorizontalAlignment.Center;
        role.AddThemeConstantOverride("line_spacing", -2);
        var plate = Ui.Column(UiPalette.SpaceS, name, Ui.Panel(UiTheme.GlassPanel, role));
        Ui.Place(plate, 1, 0, -724, 96, -660, 560);
        root.AddChild(plate);

        if (Motion.Enabled)
        {
            // 不做缩放“呼吸”：极慢的亚像素纵向缩放会让线稿逐像素爬动、看起来一顿一顿，还会拉伸五官。
            // 改为随鼠标的整体视差（_Process），人物只在玩家移动鼠标时轻移。
            Motion.Enter(art, 0.1f, 0.9f, rise: 0, fromX: 40);
            Motion.Enter(plate, 0.6f, Motion.Normal, rise: 20);
        }

        _portrait = root;
        return root;
    }

    // ── 标题字 ────────────────────────────────────────────

    private static Control BuildLogo()
    {
        var title = Ui.Text("武侠世界", UiTheme.DisplayLabel);
        title.AddThemeFontOverride("font", new FontVariation { BaseFont = UiFonts.Title, SpacingGlyph = 14 });

        var arc = Ui.Text("第一篇　众路归潮", UiTheme.SectionLabel, 30);
        arc.AddThemeColorOverride("font_color", UiPalette.Accent);
        arc.AddThemeColorOverride("font_outline_color", UiPalette.Surface with { A = 0.7f });
        arc.AddThemeConstantOverride("outline_size", 6);

        var rule = Ui.MinSize(new DiamondRule { Lead = true }, 220);
        rule.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        // 标题后压一方朱砂印，是整页最亮的一点暖色。
        var seal = Ui.SquareSeal("江湖", 34);
        seal.Rotation = Mathf.DegToRad(-3);
        var logo = Ui.Column(0, Ui.Row(UiPalette.SpaceL, title, seal),
            Ui.Row(UiPalette.SpaceM, Ui.MinSize(new Control(), 8), arc, rule));
        Ui.Place(logo, 0, 0, 128, 72, 1100, 330);
        Motion.Enter(logo, 0.15f, 0.8f, rise: 24);
        return logo;
    }

    // ── 按任意键 ─────────────────────────────────────────

    private static Control BuildPressStart()
    {
        var label = Ui.Text("—　按任意键开始　—", UiTheme.SectionLabel, 30);
        label.AddThemeColorOverride("font_outline_color", UiPalette.Surface with { A = 0.7f });
        label.AddThemeConstantOverride("outline_size", 6);
        Ui.Place(label, 0, 0, 170, 560, 800, 620);
        Motion.Pulse(label);
        return label;
    }

    // ── 主菜单 ───────────────────────────────────────────

    private Control BuildMenu()
    {
        var menu = Ui.Column(4);
        Ui.Place(menu, 0, 0, 104, 420, 720, 1000);
        var router = AppHost.Instance.Router;
        _continue = Item(menu, "继续旅程", ContinueLatest);
        _status = Ui.Text("", UiTheme.MutedLabel, 18);
        _status.AddThemeConstantOverride("line_spacing", 2);
        menu.AddChild(Indent(_status, 48, 10));

        var start = Item(menu, "新的旅程", NewGame);
        Item(menu, "读取存档", OpenSaves);
        Item(menu, "场景目录", () => router.GoTo(ScenePaths.PreviewCatalog));
        Item(menu, "江湖设置", OpenSettings);
        Item(menu, "退出游戏", () => GetTree().Quit());
        _first = RefreshLatest() ? _continue : start;
        return menu;
    }

    /// <summary>按最近一份有效存档更新“继续旅程”与其下的摘要（存档弹层里删除存档后关闭时再调一次）；有存档时返回 true。</summary>
    private bool RefreshLatest()
    {
        var latest = PlaySession.Latest(PlaySession.OpenStore());
        _continue.Disabled = latest is null;
        _continue.TooltipText = latest is null ? "还没有存档：请开始新的旅程" : "";
        _status.Text = latest?.Header is { } h
            ? $"{SaveSlotList.ChapterText(h.ChapterId)}　·　{SaveSlotList.MapName(h.MapId)}　·　{PlaySession.ClockText(h.Clock)}" + "\n" + $"{GameMenu.SlotName(latest.Slot)}存档　{h.CreatedAt}"
            : "尚无存档";
        return latest is not null;
    }

    private void ContinueLatest()
    {
        if (PlaySession.Latest(PlaySession.OpenStore()) is { } latest)
        {
            GameMenu.LoadInto(latest.Slot, Status);
        }
    }

    private void NewGame()
    {
        if (PlaySession.NewGame(out var error) is not { } play)
        {
            Status($"无法开始：{error}");
            return;
        }

        AppHost.Instance.Play = play;
        AppHost.Instance.Router.GoTo(ScenePaths.Exploration);
    }

    private void Status(string text) => _status.Text = text;

    private static Button Item(Container menu, string text, Action? onPressed, string? disabledReason = null)
    {
        var button = Ui.Button(text, UiTheme.MenuItem, onPressed, disabled: onPressed is null, tooltip: disabledReason);
        button.Alignment = HorizontalAlignment.Left;
        button.CustomMinimumSize = new Vector2(540, 72);
        button.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        // 鼠标悬停即取得焦点：鼠标与键盘共用同一个“选中项”。
        button.MouseEntered += () =>
        {
            if (!button.Disabled)
            {
                button.GrabFocus();
            }
        };
        menu.AddChild(button);
        return button;
    }

    // ── 存档弹层 ─────────────────────────────────────────

    private void OpenSaves()
    {
        if (_saves is not null)
        {
            return;
        }

        var layer = new Control();
        layer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var veil = new ColorRect { Color = UiPalette.Abyss with { A = 0.55f } };
        veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        veil.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                CloseSaves();
            }
        };
        layer.AddChild(veil);
        Motion.FadeIn(veil, Motion.Normal);

        var panel = new PanelContainer { ThemeTypeVariation = UiTheme.DarkPanel };
        Ui.Place(panel, 1, 0, -1090, 72, -96, 972);
        layer.AddChild(panel);
        Motion.Enter(panel, 0, Motion.Normal, rise: 0, fromX: 60);

        var list = SaveSlotList.Build(PlaySession.OpenStore(), save: false, GameMenu.LoadSlot,
            "正式存档损坏时读取上一份备份并提示；删除会连同备份与缩略图一并删除。");
        _slotList = list;

        var used = Ui.Text("", UiTheme.GiltLabel);
        void Count() => used.Text = $"已用 {list.UsedCount} / {SaveSlot.All.Count()}";
        Count();
        list.Refreshed += Count;
        var header = Ui.Row(UiPalette.SpaceL,
            Ui.Seal("存档"),
            Ui.Column(UiPalette.SpaceS,
                Ui.Text("读取存档", UiTheme.DarkTitleLabel, 40),
                Ui.Text("手动 10、快速 1、自动 3", UiTheme.DarkMutedLabel)),
            Ui.Spacer(),
            used);
        header.GetChild<Control>(1).SizeFlagsVertical = SizeFlags.ShrinkCenter;

        panel.AddChild(Ui.Column(UiPalette.SpaceM, header, Ui.Rule(dark: true), list.Cards, list.Status, list.Footer));
        AddChild(layer);
        _saves = layer;

        // 弹层打开时立绘淡到 25%，标题页底栏（声明、键帽、版本号）隐去，由弹层自己的键帽栏接替。
        FadeTo(_portrait, 0.25f);
        FadeTo(_footer, 0);
    }

    /// <summary>江湖设置：与游戏菜单共用的设置面板，叠在标题页中央；Esc 或“关闭”收起。</summary>
    private void OpenSettings()
    {
        if (_saves is not null)
        {
            return;
        }

        var layer = new Control();
        layer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var veil = new ColorRect { Color = UiPalette.Abyss with { A = 0.6f } };
        veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        layer.AddChild(veil);
        Motion.FadeIn(veil, Motion.Normal);
        var panel = new PanelContainer { ThemeTypeVariation = UiTheme.DarkPanel };
        panel.AddChild(SettingsPanel.Build(CloseSaves));
        layer.AddChild(Ui.Place(panel, 0.5f, 0.5f, -560, -480, 560, 480));
        Motion.Enter(panel, 0, Motion.Normal, rise: 20);
        AddChild(layer);
        _saves = layer;
        _slotList = null;
        FadeTo(_portrait, 0.3f);
    }

    private void CloseSaves()
    {
        if (_saves is not { } layer)
        {
            return;
        }

        _saves = null;
        _slotList = null;
        Motion.FadeOut(layer, Motion.Quick, layer.QueueFree);
        FadeTo(_portrait, 1);
        FadeTo(_footer, 1);
        if (!RefreshLatest() && _first == _continue)
        {
            _first = _menu.GetChildren().OfType<Button>().First(b => !b.Disabled);
        }

        _first.GrabFocus();
    }

    // ── 底栏 ─────────────────────────────────────────────

    private static Control BuildFooterShade()
    {
        var shade = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Width = 4, Height = 64, FillFrom = new Vector2(0, 0), FillTo = new Vector2(0, 1),
                Gradient = new Gradient { Colors = [UiPalette.Abyss with { A = 0 }, UiPalette.Abyss with { A = 0.78f }] },
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        shade.AnchorLeft = 0;
        shade.AnchorRight = 1;
        shade.AnchorTop = 1;
        shade.AnchorBottom = 1;
        shade.OffsetTop = -190;
        return shade;
    }

    private static Control BuildFooter()
    {
        var note = Ui.Text("开发试玩版　·　第一篇第一章“江南会客”", UiTheme.DarkMutedLabel, 18);
        var version = Ui.Text("v0.0.2-m2", UiTheme.GiltLabel, 18);
        var bar = Ui.Row(UiPalette.SpaceXl, note, Ui.Spacer(),
            Ui.KeyHints(true, ("↑↓", "选择"), ("Enter", "确认"), ("Esc", "返回")), version);
        foreach (var child in bar.GetChildren().OfType<Control>())
        {
            child.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        }

        bar.AnchorLeft = 0;
        bar.AnchorRight = 1;
        bar.AnchorTop = 1;
        bar.AnchorBottom = 1;
        bar.OffsetLeft = 64;
        bar.OffsetRight = -64;
        bar.OffsetTop = -76;
        bar.OffsetBottom = -28;
        return bar;
    }

    // ── 工具 ─────────────────────────────────────────────

    private static void FadeTo(CanvasItem item, float alpha)
    {
        if (Motion.Enabled)
        {
            item.CreateTween().TweenProperty(item, "modulate:a", alpha, Motion.Normal);
        }
        else
        {
            item.Modulate = item.Modulate with { A = alpha };
        }
    }

    private static MarginContainer Indent(Control child, int left, int bottom)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", left);
        margin.AddThemeConstantOverride("margin_bottom", bottom);
        margin.AddChild(child);
        return margin;
    }
}
