using Godot;

namespace WuxiaWorld.Game.Presentation.Ui;

/// <summary>
/// 全局 UI 主题（架构文档 10.4，视觉规范见 docs/art/UI_DESIGN.md）。
/// 两种面：浅色“玉版”（阅读与表单）与深色“潭影”（菜单外框、HUD、对话、战斗）。
/// 基础类型按玉版配色；深色面上的控件使用 Dark* 等类型变体。由 AppHost 挂到根窗口。
/// </summary>
public static class UiTheme
{
    public const string PrimaryButton = "PrimaryButton";
    public const string RowButton = "RowButton";
    public const string ChipButton = "ChipButton";
    public const string NavTab = "NavTab";
    public const string SubTab = "SubTab";
    public const string DarkButton = "DarkButton";
    public const string MenuItem = "MenuItem";
    public const string CardButton = "CardButton";
    public const string ChoiceButton = "ChoiceButton";

    public const string DarkLabel = "DarkLabel";
    public const string DarkMutedLabel = "DarkMutedLabel";
    public const string MutedLabel = "MutedLabel";
    public const string AccentLabel = "AccentLabel";
    public const string GiltLabel = "GiltLabel";
    public const string TitleLabel = "TitleLabel";
    public const string DarkTitleLabel = "DarkTitleLabel";
    public const string SectionLabel = "SectionLabel";
    public const string SealLabel = "SealLabel";
    public const string DisplayLabel = "DisplayLabel";

    public const string SheetPanel = "SheetPanel";
    public const string InsetPanel = "InsetPanel";
    public const string SealPanel = "SealPanel";
    public const string DarkPanel = "DarkPanel";
    public const string GlassPanel = "GlassPanel";
    public const string KeyCapPanel = "KeyCapPanel";

    public const string HealthBar = "HealthBar";
    public const string InnerBar = "InnerBar";
    public const string ExpBar = "ExpBar";

    public static Theme Build()
    {
        var t = new Theme
        {
            DefaultFont = UiFonts.Body,
            DefaultFontSize = UiPalette.FontBody,
        };

        BuildLabels(t);
        BuildButtons(t);
        BuildPanels(t);
        BuildInputs(t);
        return t;
    }

    private static void BuildLabels(Theme t)
    {
        t.SetColor("font_color", "Label", UiPalette.Text);
        t.SetConstant("line_spacing", "Label", 6);

        Label(t, DarkLabel, UiPalette.TextOnDark);
        Label(t, DarkMutedLabel, UiPalette.TextOnDarkMuted, UiPalette.FontSecondary);
        Label(t, MutedLabel, UiPalette.TextMuted, UiPalette.FontSecondary);
        Label(t, AccentLabel, UiPalette.Accent);
        Label(t, GiltLabel, UiPalette.Gilt, UiPalette.FontSecondary);
        Label(t, TitleLabel, UiPalette.Text, UiPalette.FontTitle, UiFonts.Title);
        Label(t, DarkTitleLabel, UiPalette.TextOnDark, UiPalette.FontTitle, UiFonts.Title);
        Label(t, SectionLabel, UiPalette.Text, 28, UiFonts.Title);
        Label(t, SealLabel, UiPalette.Surface, 34, UiFonts.Title);
        t.SetConstant("line_spacing", SealLabel, -4);

        // 标题字：大号书法字配一道浅色描边，在山水背景上保持轮廓。
        Label(t, DisplayLabel, UiPalette.Text, 120, UiFonts.Title);
        t.SetColor("font_outline_color", DisplayLabel, UiPalette.Surface with { A = 0.75f });
        t.SetConstant("outline_size", DisplayLabel, 10);
        t.SetColor("font_shadow_color", DisplayLabel, UiPalette.Abyss with { A = 0.25f });
        t.SetConstant("shadow_offset_x", DisplayLabel, 0);
        t.SetConstant("shadow_offset_y", DisplayLabel, 6);
        t.SetConstant("shadow_outline_size", DisplayLabel, 18);

        t.SetColor("default_color", "RichTextLabel", UiPalette.Text);
        t.SetFont("normal_font", "RichTextLabel", UiFonts.Body);
        t.SetFont("bold_font", "RichTextLabel", UiFonts.BodyMedium);
        t.SetFont("italics_font", "RichTextLabel", UiFonts.Title);
        t.SetFontSize("normal_font_size", "RichTextLabel", UiPalette.FontBody);
        t.SetFontSize("bold_font_size", "RichTextLabel", UiPalette.FontBody);
        t.SetFontSize("italics_font_size", "RichTextLabel", UiPalette.FontBody);
        t.SetConstant("line_separation", "RichTextLabel", 8);
    }

    private static void BuildButtons(Theme t)
    {
        // 键盘焦点：控件外侧四角的金泥折线，所有按钮共用，不只靠颜色。
        OrnateBox Focus(float outset = 4) => new()
        {
            Corners = CornerStyle.Bracket, CornerColor = UiPalette.Gilt, CornerSize = 12, CornerWidth = 2,
            CornerOutset = outset,
        };

        // 基础按钮：玉版上的次要操作，切角细框。
        OrnateBox Plain(Color fill, Color border, float width = 1) =>
            new OrnateBox { FillA = fill, Border = border, BorderWidth = width, Chamfer = 5 }.Margins(22, 10);
        States(t, "Button",
            Plain(UiPalette.Surface with { A = 0.6f }, UiPalette.Text with { A = 0.7f }),
            Plain(UiPalette.SurfaceShade, UiPalette.Accent, 1.5f),
            Plain(UiPalette.Text, UiPalette.Text),
            Plain(Colors.Transparent, UiPalette.TextMuted with { A = 0.35f }),
            Focus());
        Fonts(t, "Button", UiPalette.Text, UiPalette.Accent, UiPalette.Surface, UiPalette.TextMuted with { A = 0.6f });
        t.SetFont("font", "Button", UiFonts.BodyMedium);

        // 主按钮：石青玉面，上亮下暗，内衬一道浅线与底部反光。
        OrnateBox Jade(float light) => new OrnateBox
        {
            FillA = UiPalette.Accent.Lightened(0.14f + light), FillB = UiPalette.Accent.Darkened(0.18f - light),
            Chamfer = 7, Border = UiPalette.Abyss with { A = 0.35f }, BorderWidth = 1,
            Inner = UiPalette.Surface with { A = 0.28f }, InnerInset = 3,
            Sheen = UiPalette.Surface with { A = 0.35f }, SheenAtTop = true, SheenInset = 0.08f,
            Shadow = UiPalette.Abyss with { A = 0.28f }, ShadowSize = 4, ShadowOffset = new Vector2(0, 3),
        }.Margins(28, 12);
        Variation(t, PrimaryButton, "Button");
        States(t, PrimaryButton, Jade(0), Jade(0.08f), Jade(-0.14f),
            new OrnateBox { FillA = UiPalette.TextMuted with { A = 0.22f }, Chamfer = 7 }.Margins(28, 12), Focus());
        Fonts(t, PrimaryButton, UiPalette.Surface, UiPalette.Surface, UiPalette.Surface, UiPalette.TextMuted);

        // 列表行：悬停淡入底色；选中为自左渐隐的选中底加石青竖条，不只靠颜色区分。
        OrnateBox Row(Color fill, float marker) => new OrnateBox
        {
            FillA = fill, FillB = fill with { A = 0 }, Horizontal = true,
            Marker = UiPalette.Accent, MarkerWidth = marker,
            Sheen = UiPalette.Trim with { A = 0.28f },
        }.Margins(12, 12);
        Variation(t, RowButton, "Button");
        States(t, RowButton,
            Row(Colors.Transparent, 0),
            Row(UiPalette.SurfaceShade with { A = 0.7f }, 0),
            Row(UiPalette.SurfaceShade, 5),
            Row(Colors.Transparent, 0),
            Focus(0));
        Fonts(t, RowButton, UiPalette.Text, UiPalette.Text, UiPalette.Text, UiPalette.TextMuted);
        t.SetFont("font", RowButton, UiFonts.Body);

        // 分类签：小切角签；选中为深色实底加金泥角点。
        OrnateBox Chip(Color fill, Color border, bool corners = false) => new OrnateBox
        {
            FillA = fill, Border = border, BorderWidth = 1, Chamfer = 4,
            Corners = corners ? CornerStyle.Bracket : CornerStyle.None, CornerSize = 6, CornerWidth = 1.5f,
        }.Margins(18, 5);
        Variation(t, ChipButton, "Button");
        States(t, ChipButton,
            Chip(UiPalette.Surface with { A = 0.5f }, UiPalette.Trim),
            Chip(UiPalette.SurfaceShade, UiPalette.Accent),
            Chip(UiPalette.Text, UiPalette.Text, true),
            Chip(Colors.Transparent, UiPalette.TextMuted with { A = 0.3f }),
            Focus(3));
        Fonts(t, ChipButton, UiPalette.Text, UiPalette.Accent, UiPalette.Surface, UiPalette.TextMuted);
        t.SetFontSize("font_size", ChipButton, UiPalette.FontSecondary);

        // 顶部分区签（深色面）：选中项自下而上泛起石青光，底边一道金泥线。
        OrnateBox Nav(float glow, bool line) => new OrnateBox
        {
            FillA = UiPalette.Accent with { A = 0 }, FillB = UiPalette.Accent with { A = glow },
            Sheen = line ? UiPalette.Gilt : Colors.Transparent, SheenWidth = 3, SheenInset = 0.18f,
        }.Margins(26, 14);
        Variation(t, NavTab, "Button");
        States(t, NavTab, Nav(0, false), Nav(0.25f, false), Nav(0.55f, true), Nav(0, false), Focus(0));
        Fonts(t, NavTab, UiPalette.TextOnDarkMuted, UiPalette.TextOnDark, UiPalette.TextOnDark, UiPalette.TextOnDarkMuted with { A = 0.45f });
        t.SetFont("font", NavTab, UiFonts.Title);
        t.SetFontSize("font_size", NavTab, 30);

        // 页内子签（玉版）：文字签，选中加石青下划线。
        OrnateBox Sub(Color line, Color fill) => new OrnateBox
        {
            FillA = fill, Sheen = line, SheenWidth = 3, SheenInset = 0.12f,
        }.Margins(20, 10);
        Variation(t, SubTab, "Button");
        States(t, SubTab,
            Sub(Colors.Transparent, Colors.Transparent),
            Sub(UiPalette.Trim with { A = 0.6f }, Colors.Transparent),
            Sub(UiPalette.Accent, UiPalette.SurfaceShade with { A = 0.5f }),
            Sub(Colors.Transparent, Colors.Transparent),
            Focus(0));
        Fonts(t, SubTab, UiPalette.TextMuted, UiPalette.Text, UiPalette.Accent, UiPalette.TextMuted with { A = 0.5f });
        t.SetFont("font", SubTab, UiFonts.Title);
        t.SetFontSize("font_size", SubTab, 26);

        // 深色面按钮。
        OrnateBox DarkBox(Color fill, Color border) =>
            new OrnateBox { FillA = fill, Border = border, BorderWidth = 1, Chamfer = 5 }.Margins(22, 10);
        Variation(t, DarkButton, "Button");
        States(t, DarkButton,
            DarkBox(UiPalette.Abyss with { A = 0.35f }, UiPalette.Trim with { A = 0.55f }),
            DarkBox(UiPalette.Accent with { A = 0.45f }, UiPalette.Trim),
            DarkBox(UiPalette.Trim, UiPalette.Trim),
            DarkBox(UiPalette.Abyss with { A = 0.3f }, UiPalette.TextOnDarkMuted with { A = 0.25f }),
            Focus());
        Fonts(t, DarkButton, UiPalette.TextOnDark, UiPalette.TextOnDark, UiPalette.Abyss, UiPalette.TextOnDarkMuted with { A = 0.65f });

        // 标题菜单项：无框大字；悬停与键盘选中时，石青墨痕自左铺开，左端一粒金泥菱形。
        OrnateBox Streak(float a) => new OrnateBox
        {
            FillA = UiPalette.Accent with { A = a }, FillB = UiPalette.Accent with { A = 0 }, Horizontal = true,
            Diamond = a > 0, CornerColor = UiPalette.Gilt,
        }.Margins(44, 8);
        Variation(t, MenuItem, "Button");
        States(t, MenuItem, Streak(0), Streak(0.9f), Streak(1f), Streak(0), Streak(0.9f));
        Fonts(t, MenuItem, UiPalette.Text, UiPalette.Surface, UiPalette.Surface, UiPalette.TextMuted with { A = 0.5f });
        t.SetColor("font_focus_color", MenuItem, UiPalette.Surface);
        t.SetFont("font", MenuItem, UiFonts.Title);
        t.SetFontSize("font_size", MenuItem, 40);
        t.SetColor("font_outline_color", MenuItem, UiPalette.Surface with { A = 0.6f });
        t.SetConstant("outline_size", MenuItem, 4);

        // 卡片按钮（存档卡、人物卡）：深色玻璃底，悬停/选中出金泥回纹角。
        OrnateBox Card(Color fill, Color border, bool corners) => new OrnateBox
        {
            FillA = fill, FillB = UiPalette.Abyss with { A = 0.9f }, Chamfer = 8,
            Border = border, BorderWidth = 1,
            Corners = corners ? CornerStyle.Hook : CornerStyle.None, CornerSize = 16,
            Shadow = UiPalette.Abyss with { A = 0.35f }, ShadowSize = 8, ShadowOffset = new Vector2(0, 4),
        }.Margins(20, 16);
        Variation(t, CardButton, "Button");
        States(t, CardButton,
            Card(UiPalette.PanelDark with { A = 0.85f }, UiPalette.Trim with { A = 0.35f }, false),
            Card(UiPalette.Accent with { A = 0.7f }, UiPalette.Trim, true),
            Card(UiPalette.Accent, UiPalette.Gilt, true),
            Card(UiPalette.PanelDark with { A = 0.5f }, UiPalette.Trim with { A = 0.15f }, false),
            Focus(5));
        Fonts(t, CardButton, UiPalette.TextOnDark, UiPalette.TextOnDark, UiPalette.TextOnDark, UiPalette.TextOnDarkMuted);

        // 对话选项：深色长条，悬停/选中时左端石青条与金泥菱形。
        OrnateBox Choice(float a, bool mark) => new OrnateBox
        {
            FillA = UiPalette.Abyss with { A = 0.72f }, FillB = UiPalette.Accent with { A = a }, Horizontal = true,
            Chamfer = 6, Border = UiPalette.Trim with { A = mark ? 0.9f : 0.35f }, BorderWidth = 1,
            Marker = UiPalette.Trim, MarkerWidth = mark ? 4 : 0, Diamond = mark, CornerColor = UiPalette.Gilt,
        }.Margins(44, 12);
        Variation(t, ChoiceButton, "Button");
        States(t, ChoiceButton, Choice(0.2f, false), Choice(0.6f, true), Choice(0.85f, true), Choice(0.05f, false), Choice(0.6f, true));
        Fonts(t, ChoiceButton, UiPalette.TextOnDark, UiPalette.TextOnDark, UiPalette.TextOnDark, UiPalette.TextOnDarkMuted with { A = 0.6f });
        t.SetColor("font_focus_color", ChoiceButton, UiPalette.TextOnDark);
        t.SetConstant("h_separation", ChoiceButton, 16);
    }

    private static void BuildPanels(Theme t)
    {
        t.SetStylebox("panel", "PanelContainer", new StyleBoxEmpty());

        // 玉版：主内容纸面，双线框、金泥回纹角、投影。
        Variation(t, SheetPanel, "PanelContainer");
        t.SetStylebox("panel", SheetPanel, new OrnateBox
        {
            FillA = UiPalette.Surface with { A = 0.97f }, FillB = UiPalette.SurfaceShade with { A = 0.97f },
            Chamfer = 12, Border = UiPalette.Trim, BorderWidth = 1.5f,
            Inner = UiPalette.Trim with { A = 0.45f }, InnerInset = 7,
            Corners = CornerStyle.Hook, CornerSize = 30, CornerWidth = 2.5f, CornerColor = UiPalette.Gilt.Darkened(0.12f),
            Shadow = UiPalette.Abyss with { A = 0.55f }, ShadowSize = 22, ShadowOffset = new Vector2(0, 10),
        }.Margins(40, 22));

        Variation(t, InsetPanel, "PanelContainer");
        t.SetStylebox("panel", InsetPanel, new OrnateBox
        {
            FillA = UiPalette.Surface with { A = 0.7f }, FillB = UiPalette.SurfaceShade with { A = 0.85f },
            Chamfer = 8, Border = UiPalette.Trim with { A = 0.7f }, BorderWidth = 1,
            Corners = CornerStyle.Bracket, CornerSize = 10, CornerWidth = 1.5f, CornerColor = UiPalette.Trim.Darkened(0.2f),
        }.Margins(26, 22));

        Variation(t, SealPanel, "PanelContainer");
        t.SetStylebox("panel", SealPanel, new OrnateBox
        {
            FillA = UiPalette.Accent.Lightened(0.08f), FillB = UiPalette.Accent.Darkened(0.2f), Chamfer = 4,
            Border = UiPalette.Gilt, BorderWidth = 1.5f, Inner = UiPalette.Surface with { A = 0.35f }, InnerInset = 4,
        }.Margins(14, 12));

        // 潭影：深色面板（菜单弹层、存档、对话框）。
        Variation(t, DarkPanel, "PanelContainer");
        t.SetStylebox("panel", DarkPanel, new OrnateBox
        {
            FillA = UiPalette.PanelDark with { A = 0.94f }, FillB = UiPalette.Abyss with { A = 0.96f },
            Chamfer = 12, Border = UiPalette.Trim with { A = 0.55f }, BorderWidth = 1.5f,
            Inner = UiPalette.Trim with { A = 0.18f }, InnerInset = 7,
            Corners = CornerStyle.Hook, CornerSize = 26, CornerWidth = 2, CornerColor = UiPalette.Gilt with { A = 0.9f },
            Shadow = UiPalette.Abyss with { A = 0.5f }, ShadowSize = 20, ShadowOffset = new Vector2(0, 8),
        }.Margins(32, 28));

        // 薄玻璃：HUD 与提示条，不加角饰，避免抢画面。
        Variation(t, GlassPanel, "PanelContainer");
        t.SetStylebox("panel", GlassPanel, new OrnateBox
        {
            FillA = UiPalette.Abyss with { A = 0.62f }, FillB = UiPalette.Abyss with { A = 0.78f }, Chamfer = 6,
            Border = UiPalette.Trim with { A = 0.35f }, BorderWidth = 1,
        }.Margins(18, 12));

        Variation(t, KeyCapPanel, "PanelContainer");
        t.SetStylebox("panel", KeyCapPanel, new OrnateBox
        {
            FillA = UiPalette.TextOnDark with { A = 0.95f }, FillB = UiPalette.TextOnDarkMuted, Chamfer = 3,
            Border = UiPalette.Abyss with { A = 0.5f }, BorderWidth = 1,
            Sheen = UiPalette.Abyss with { A = 0.35f }, SheenWidth = 2,
        }.Margins(9, 1));

        t.SetStylebox("panel", "TooltipPanel", new OrnateBox
        {
            FillA = UiPalette.PanelDark, FillB = UiPalette.Abyss, Chamfer = 5,
            Border = UiPalette.Trim with { A = 0.7f }, BorderWidth = 1,
            Corners = CornerStyle.Bracket, CornerSize = 8, CornerWidth = 1.5f,
        }.Margins(14, 9));
        t.SetColor("font_color", "TooltipLabel", UiPalette.TextOnDark);
        t.SetFontSize("font_size", "TooltipLabel", UiPalette.FontSecondary);

        t.SetStylebox("panel", "PopupMenu", new OrnateBox
        {
            FillA = UiPalette.PanelDark, FillB = UiPalette.Abyss, Chamfer = 5,
            Border = UiPalette.Trim with { A = 0.7f }, BorderWidth = 1,
        }.Margins(8, 8));
        t.SetStylebox("hover", "PopupMenu", new OrnateBox
        {
            FillA = UiPalette.Accent, FillB = UiPalette.Accent with { A = 0.2f }, Horizontal = true,
            Marker = UiPalette.Gilt, MarkerWidth = 3,
        });
        t.SetColor("font_color", "PopupMenu", UiPalette.TextOnDark);
        t.SetColor("font_hover_color", "PopupMenu", UiPalette.TextOnDark);
        t.SetColor("font_disabled_color", "PopupMenu", UiPalette.TextOnDarkMuted with { A = 0.6f });
        t.SetFontSize("font_size", "PopupMenu", UiPalette.FontSecondary);
        t.SetConstant("v_separation", "PopupMenu", 14);

        // 分隔线：两端渐隐、正中一粒菱形（Ui.Rule 负责菱形）。
        t.SetStylebox("separator", "HSeparator", new StyleBoxLine { Color = UiPalette.Trim with { A = 0.6f }, Thickness = 1, GrowBegin = -8, GrowEnd = -8 });
        t.SetConstant("separation", "HSeparator", UiPalette.SpaceM);

        var track = new OrnateBox { FillA = UiPalette.Text with { A = 0.07f } };
        t.SetStylebox("scroll", "VScrollBar", track.Margins(3, 0));
        t.SetStylebox("grabber", "VScrollBar", new OrnateBox { FillA = UiPalette.Trim, Chamfer = 2 });
        t.SetStylebox("grabber_highlight", "VScrollBar", new OrnateBox { FillA = UiPalette.Accent, Chamfer = 2 });
        t.SetStylebox("grabber_pressed", "VScrollBar", new OrnateBox { FillA = UiPalette.Text, Chamfer = 2 });
    }

    private static void BuildInputs(Theme t)
    {
        // 滑杆：细轨，石青已填段，菱形把手。
        t.SetStylebox("slider", "HSlider", new OrnateBox
        {
            FillA = UiPalette.Text with { A = 0.16f }, Border = UiPalette.Text with { A = 0.2f }, BorderWidth = 1,
        }.Margins(0, 3));
        t.SetStylebox("grabber_area", "HSlider", new OrnateBox
        {
            FillA = UiPalette.Trim, FillB = UiPalette.Accent, Horizontal = true,
        }.Margins(0, 3));
        t.SetStylebox("grabber_area_highlight", "HSlider", new OrnateBox
        {
            FillA = UiPalette.Trim, FillB = UiPalette.Accent.Lightened(0.1f), Horizontal = true,
        }.Margins(0, 3));
        t.SetIcon("grabber", "HSlider", Knob(UiPalette.Accent, UiPalette.Gilt));
        t.SetIcon("grabber_highlight", "HSlider", Knob(UiPalette.Accent.Lightened(0.15f), UiPalette.Surface));
        t.SetIcon("grabber_disabled", "HSlider", Knob(UiPalette.TextMuted, UiPalette.Surface));
        t.SetStylebox("focus", "HSlider", new OrnateBox
        {
            Corners = CornerStyle.Bracket, CornerColor = UiPalette.Gilt, CornerSize = 10, CornerWidth = 2, CornerOutset = 6,
        });

        OrnateBox Field(Color fill, Color border) =>
            new OrnateBox { FillA = fill, Border = border, BorderWidth = 1, Chamfer = 5 }.Margins(18, 9);
        States(t, "OptionButton",
            Field(UiPalette.Surface, UiPalette.Text with { A = 0.6f }),
            Field(UiPalette.SurfaceShade, UiPalette.Accent),
            Field(UiPalette.SurfaceShade, UiPalette.Accent),
            Field(Colors.Transparent, UiPalette.TextMuted with { A = 0.35f }),
            new OrnateBox { Corners = CornerStyle.Bracket, CornerColor = UiPalette.Gilt, CornerSize = 12, CornerWidth = 2, CornerOutset = 4 });
        Fonts(t, "OptionButton", UiPalette.Text, UiPalette.Text, UiPalette.Text, UiPalette.TextMuted);
        t.SetConstant("modulate_arrow", "OptionButton", 1);

        // 进度条：暗槽加内框，填充为横向渐变并在顶边带一道高光。
        t.SetStylebox("background", "ProgressBar", new OrnateBox
        {
            FillA = UiPalette.Abyss with { A = 0.22f }, FillB = UiPalette.Abyss with { A = 0.12f },
            Border = UiPalette.Abyss with { A = 0.3f }, BorderWidth = 1, Chamfer = 2,
        });
        t.SetStylebox("fill", "ProgressBar", BarFill(UiPalette.TextMuted));
        t.SetColor("font_color", "ProgressBar", UiPalette.Text);
        t.SetFontSize("font_size", "ProgressBar", 16);

        Variation(t, HealthBar, "ProgressBar");
        t.SetStylebox("fill", HealthBar, BarFill(UiPalette.Warm));
        Variation(t, InnerBar, "ProgressBar");
        t.SetStylebox("fill", InnerBar, BarFill(UiPalette.Accent));
        Variation(t, ExpBar, "ProgressBar");
        t.SetStylebox("fill", ExpBar, BarFill(UiPalette.Gilt.Darkened(0.25f)));
    }

    private static OrnateBox BarFill(Color c) => new()
    {
        FillA = c.Darkened(0.12f), FillB = c.Lightened(0.18f), Horizontal = true, Chamfer = 2,
        Sheen = Colors.White with { A = 0.35f }, SheenAtTop = true,
    };

    private static void Label(Theme t, string name, Color color, int? size = null, Font? font = null)
    {
        Variation(t, name, "Label");
        t.SetColor("font_color", name, color);
        if (size is { } s)
        {
            t.SetFontSize("font_size", name, s);
        }

        if (font is not null)
        {
            t.SetFont("font", name, font);
        }
    }

    private static void Variation(Theme t, string name, string baseType) => t.SetTypeVariation(name, baseType);

    private static void States(Theme t, string type, StyleBox normal, StyleBox hover, StyleBox pressed, StyleBox disabled, StyleBox focus)
    {
        t.SetStylebox("normal", type, normal);
        t.SetStylebox("hover", type, hover);
        t.SetStylebox("pressed", type, pressed);
        t.SetStylebox("hover_pressed", type, pressed);
        t.SetStylebox("disabled", type, disabled);
        t.SetStylebox("focus", type, focus);
    }

    private static void Fonts(Theme t, string type, Color normal, Color hover, Color pressed, Color disabled)
    {
        t.SetColor("font_color", type, normal);
        t.SetColor("font_focus_color", type, normal);
        t.SetColor("font_hover_color", type, hover);
        t.SetColor("font_pressed_color", type, pressed);
        t.SetColor("font_hover_pressed_color", type, pressed);
        t.SetColor("font_disabled_color", type, disabled);
    }

    /// <summary>滑杆把手：菱形玉扣，外圈金泥，运行时生成。</summary>
    private static ImageTexture Knob(Color fill, Color ring)
    {
        const int size = 30;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var c = (size - 1) / 2f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var d = Mathf.Abs(x - c) + Mathf.Abs(y - c);
                var color = d <= 8 ? fill.Lightened(0.15f) : d <= 11 ? fill : d <= 13.5f ? ring : Colors.Transparent;
                if (d > 13.5f && d < 14.5f)
                {
                    color = ring with { A = 14.5f - d };
                }

                image.SetPixel(x, y, color);
            }
        }

        return ImageTexture.CreateFromImage(image);
    }
}
