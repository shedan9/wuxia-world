using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Scenery;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 标题页（主菜单与存档页），版式见 docs/art/UI_DESIGN.md 第 5.1 节。
/// 三种状态：按键开始 → 主菜单 → 存档弹层。截图参数 <c>--tab</c>：0 主菜单、1 按键开始、2 存档弹层。
/// “继续旅程”在 M0 进入场景目录；存档卡为固定样例，不读写存档。
/// </summary>
public partial class MainMenuPreview : Control
{
    private const string PortraitPath = "res://assets/portraits/lu_qinghe_v1.png";

    private Control _pressStart = null!;
    private Control _menu = null!;
    private Control _portrait = null!;
    private Control? _saves;
    private Button _first = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(Backdrop.Clear());
        AddChild(BuildPortrait());
        AddChild(BuildFooterShade());
        AddChild(BuildLogo());

        _menu = BuildMenu();
        _menu.Visible = false;
        AddChild(_menu);

        _pressStart = BuildPressStart();
        AddChild(_pressStart);
        AddChild(BuildFooter());

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
            CloseSaves();
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
            // 呼吸：以脚下为轴极轻微的纵向起伏。
            art.Ready += () =>
            {
                art.PivotOffset = new Vector2(art.Size.X / 2, art.Size.Y);
                var breathe = art.CreateTween().SetLoops().SetTrans(Tween.TransitionType.Sine);
                breathe.TweenProperty(art, "scale", new Vector2(1, 1.006f), 2.4f);
                breathe.TweenProperty(art, "scale", Vector2.One, 2.4f);
            };
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
        var logo = Ui.Column(0, title, Ui.Row(UiPalette.SpaceM, Ui.MinSize(new Control(), 8), arc, rule));
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
        Ui.Place(menu, 0, 0, 104, 420, 720, 960);
        var router = AppHost.Instance.Router;
        var latest = SaveSamples.Latest;

        _first = Item(menu, "继续旅程", () => router.GoTo(ScenePaths.PreviewCatalog));
        var caption = Ui.Text($"{latest.Chapter}　·　{latest.Place}　·　{latest.PlayTime}\nM0 展示包：进入场景目录",
            UiTheme.MutedLabel, 18);
        caption.AddThemeConstantOverride("line_spacing", 2);
        menu.AddChild(Indent(caption, 48, 10));

        Item(menu, "新的旅程", null, "新游戏在第二阶段玩法 Demo 开放");
        Item(menu, "读取存档", OpenSaves);
        Item(menu, "江湖设置", () => router.GoTo("res://scenes/preview/Settings.tscn"));
        Item(menu, "退出游戏", () => GetTree().Quit());

        var disabled = Ui.Text("“新的旅程”为禁用状态示例", UiTheme.MutedLabel, 16);
        menu.AddChild(Indent(disabled, 48, 0));
        return menu;
    }

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

        var group = new ButtonGroup();
        var cards = Ui.Column(14);
        Button? firstCard = null;
        foreach (var save in SaveSamples.Slots)
        {
            var card = SaveCard(save, group);
            cards.AddChild(card);
            firstCard ??= card;
        }

        Motion.Stagger(cards.GetChildren().OfType<Control>(), 0.08f, 0.05f, rise: 12);

        var header = Ui.Row(UiPalette.SpaceL,
            Ui.Seal("存档"),
            Ui.Column(UiPalette.SpaceS,
                Ui.Text("读取存档", UiTheme.DarkTitleLabel, 40),
                Ui.Text("固定样例：选择与悬停状态可操作，不读取实际存档", UiTheme.DarkMutedLabel)),
            Ui.Spacer(),
            Ui.Text("已用 3 / 20", UiTheme.GiltLabel));
        header.GetChild<Control>(1).SizeFlagsVertical = SizeFlags.ShrinkCenter;

        var load = Ui.MinSize(Ui.Button("读取", UiTheme.PrimaryButton, disabled: true, tooltip: "M0 不含存档功能"), 180, 56);
        var remove = Ui.MinSize(Ui.Button("删除", UiTheme.DarkButton, disabled: true, tooltip: "M0 不含存档功能"), 140, 56);
        var footer = Ui.Row(UiPalette.SpaceM,
            Ui.KeyHints(true, ("↑↓", "选择"), ("Enter", "读取"), ("Esc", "关闭")),
            Ui.Spacer(), remove, load);

        panel.AddChild(Ui.Column(UiPalette.SpaceL, header, Ui.Rule(dark: true), Ui.Expand(cards, vertical: true), footer));
        AddChild(layer);
        _saves = layer;

        firstCard!.ButtonPressed = true;
        firstCard.GrabFocus();
        _portrait.CreateTween().TweenProperty(_portrait, "modulate:a", 0.25f, Motion.Normal);
        if (!Motion.Enabled)
        {
            _portrait.Modulate = _portrait.Modulate with { A = 0.25f };
        }
    }

    private void CloseSaves()
    {
        if (_saves is not { } layer)
        {
            return;
        }

        _saves = null;
        Motion.FadeOut(layer, Motion.Quick, layer.QueueFree);
        _portrait.CreateTween().TweenProperty(_portrait, "modulate:a", 1f, Motion.Normal);
        _first.GrabFocus();
    }

    private static Button SaveCard(SampleSave? save, ButtonGroup group)
    {
        var card = new Button
        {
            ThemeTypeVariation = UiTheme.CardButton, ToggleMode = true, ButtonGroup = group,
            CustomMinimumSize = new Vector2(0, 176), FocusMode = FocusModeEnum.All,
        };
        card.MouseEntered += card.GrabFocus;
        card.FocusEntered += () => card.ButtonPressed = true;

        var content = new MarginContainer();
        content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var (side, value) in new[] { ("left", 18), ("right", 24), ("top", 16), ("bottom", 16) })
        {
            content.AddThemeConstantOverride($"margin_{side}", value);
        }

        card.AddChild(content);

        if (save is null)
        {
            var empty = Ui.Text("—　空白存档位　—", UiTheme.DarkMutedLabel, 22);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.VerticalAlignment = VerticalAlignment.Center;
            content.AddChild(empty);
            Ui.IgnoreMouse(content);
            card.CustomMinimumSize = new Vector2(0, 96);
            return card;
        }

        var thumbFrame = new PanelContainer { CustomMinimumSize = new Vector2(250, 141) };
        thumbFrame.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            Border = UiPalette.Trim with { A = 0.8f }, BorderWidth = 1, Chamfer = 5,
            Corners = CornerStyle.Bracket, CornerSize = 10, CornerWidth = 1.5f,
        }.Margins(3, 3));
        var thumb = Backdrop.Still(save.SunX, save.SunX * 7);
        thumb.ClipContents = true;
        thumbFrame.AddChild(thumb);
        thumbFrame.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        var tag = Ui.Text(save.Label, UiTheme.GiltLabel, 18);
        var chapter = Ui.Text(save.Chapter, UiTheme.DarkTitleLabel, 28);
        var place = Ui.Text($"{save.Place}　·　{save.Objective}", UiTheme.DarkMutedLabel, 18);
        var party = Ui.Row(6);
        foreach (var member in save.Party)
        {
            party.AddChild(Ui.Glyph(member, UiPalette.Trim, 34));
        }

        var info = Ui.Expand(Ui.Column(6, tag, chapter, place, party));
        info.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        var time = Ui.Text(save.PlayTime, UiTheme.DarkLabel, 30);
        time.AddThemeFontOverride("font", UiFonts.Title);
        time.HorizontalAlignment = HorizontalAlignment.Right;
        var date = Ui.Text(save.SavedAt, UiTheme.DarkMutedLabel, 17);
        date.HorizontalAlignment = HorizontalAlignment.Right;
        var meta = Ui.Column(4, Ui.Text("游戏时长", UiTheme.DarkMutedLabel, 16), time, date);
        meta.GetChild<Label>(0).HorizontalAlignment = HorizontalAlignment.Right;
        meta.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        content.AddChild(Ui.Row(UiPalette.SpaceL, thumbFrame, info, meta));
        Ui.IgnoreMouse(content);
        return card;
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
        var note = Ui.Text("M0 视觉样例　·　画面为固定样例数据，不代表玩法已实现", UiTheme.DarkMutedLabel, 18);
        var version = Ui.Text("v0.0.1-m0", UiTheme.GiltLabel, 18);
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

    private static MarginContainer Indent(Control child, int left, int bottom)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", left);
        margin.AddThemeConstantOverride("margin_bottom", bottom);
        margin.AddChild(child);
        return margin;
    }
}
