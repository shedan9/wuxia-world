using Godot;

namespace WuxiaWorld.Game.Presentation.Ui;

/// <summary>
/// 全局 UI 主题（架构文档 10.4，视觉规范见 docs/art/UI_DESIGN.md）。
/// “绢本青绿”：浅色“绢本”（阅读与表单）与深色“黛本”（存档、对话、战斗），外加薄墨 HUD 与朱砂印章。
/// 框线、选中与角饰均为笔触（<see cref="Brushwork"/>），不用规整细线。
/// 基础类型按绢面配色；深色面上的控件使用 Dark* 等类型变体。由 AppHost 挂到根窗口。
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
        // 键盘焦点：控件外侧四角的泥金折角，两笔出锋；所有按钮共用，不只靠颜色。
        OrnateBox Focus(float outset = 5) => new()
        {
            Corners = CornerStyle.Bracket, CornerColor = UiPalette.Gilt, CornerSize = 13, CornerWidth = 2.2f,
            CornerOutset = outset,
        };

        // 基础按钮：绢面上的次要操作，四边手绘墨线。
        OrnateBox Plain(Color fill, Color border, float width = 1.3f) => new OrnateBox
        {
            FillA = fill, Ragged = 1.2f, Border = border, BorderWidth = width, Brush = true, Seed = 3, Overshoot = 0.45f,
        }.Margins(24, 10);
        States(t, "Button",
            Plain(UiPalette.Surface with { A = 0.55f }, UiPalette.Text with { A = 0.75f }),
            Plain(UiPalette.SurfaceShade, UiPalette.Accent, 1.6f),
            Plain(UiPalette.Text, UiPalette.Text),
            Plain(Colors.Transparent, UiPalette.TextMuted with { A = 0.35f }),
            Focus());
        Fonts(t, "Button", UiPalette.Text, UiPalette.Accent, UiPalette.Surface, UiPalette.TextMuted with { A = 0.6f });
        t.SetFont("font", "Button", UiFonts.BodyMedium);

        // 主按钮：一块刷出来的石青色，毛边、上亮下暗，内衬一道泥金细笔。
        OrnateBox Jade(float light) => new OrnateBox
        {
            FillA = UiPalette.Accent.Lightened(0.12f + light), FillB = UiPalette.Accent.Darkened(0.22f - light),
            Ragged = 1.8f, Seed = 5, Brush = true,
            Inner = UiPalette.Gilt with { A = 0.75f }, InnerInset = 4,
            Grain = Colors.White with { A = 0.08f },
            Shadow = UiPalette.Abyss with { A = 0.3f }, ShadowSize = 6, ShadowOffset = new Vector2(0, 4),
        }.Margins(30, 12);
        Variation(t, PrimaryButton, "Button");
        States(t, PrimaryButton, Jade(0), Jade(0.08f), Jade(-0.14f),
            new OrnateBox { FillA = UiPalette.TextMuted with { A = 0.22f }, Ragged = 1.8f, Seed = 5 }.Margins(30, 12), Focus());
        Fonts(t, PrimaryButton, UiPalette.TextOnDark, UiPalette.TextOnDark, UiPalette.TextOnDark, UiPalette.TextMuted);

        // 列表行：悬停一道淡刷痕；选中为石青刷痕加左侧朱砂竖笔，不只靠颜色区分。
        OrnateBox Row(Color swipe, float marker) => new OrnateBox
        {
            Swipe = swipe, SwipeReach = 1, Seed = 7,
            Marker = UiPalette.Cinnabar, MarkerWidth = marker,
            Sheen = UiPalette.Ochre with { A = 0.22f }, Brush = true,
        }.Margins(12, 12);
        Variation(t, RowButton, "Button");
        States(t, RowButton,
            Row(Colors.Transparent, 0),
            Row(UiPalette.SurfaceShade with { A = 0.9f }, 0),
            Row(UiPalette.Accent with { A = 0.2f }, 5),
            Row(Colors.Transparent, 0),
            Focus(0));
        Fonts(t, RowButton, UiPalette.Text, UiPalette.Text, UiPalette.Text, UiPalette.TextMuted);
        t.SetFont("font", RowButton, UiFonts.Body);

        // 分类签：小墨框签；选中为浓墨实底加泥金折角。
        OrnateBox Chip(Color fill, Color border, bool corners = false) => new OrnateBox
        {
            FillA = fill, Ragged = 1, Seed = 9, Border = border, BorderWidth = 1.1f, Brush = true, Overshoot = 0.35f,
            Corners = corners ? CornerStyle.Bracket : CornerStyle.None, CornerSize = 7, CornerWidth = 1.6f,
        }.Margins(18, 5);
        Variation(t, ChipButton, "Button");
        States(t, ChipButton,
            Chip(UiPalette.Surface with { A = 0.5f }, UiPalette.Ochre with { A = 0.7f }),
            Chip(UiPalette.SurfaceShade, UiPalette.Accent),
            Chip(UiPalette.Text, UiPalette.Text, true),
            Chip(Colors.Transparent, UiPalette.TextMuted with { A = 0.3f }),
            Focus(3));
        Fonts(t, ChipButton, UiPalette.Text, UiPalette.Accent, UiPalette.Surface, UiPalette.TextMuted);
        t.SetFontSize("font_size", ChipButton, UiPalette.FontSecondary);

        // 顶部分区签（深色面）：选中项自下泛起石青光，底边一笔泥金。
        OrnateBox Nav(float glow, bool line) => new OrnateBox
        {
            FillA = UiPalette.Accent with { A = 0 }, FillB = UiPalette.Accent with { A = glow },
            Sheen = line ? UiPalette.Gilt : Colors.Transparent, SheenWidth = 3, SheenInset = 0.14f, Brush = true, Seed = 11,
        }.Margins(26, 14);
        Variation(t, NavTab, "Button");
        States(t, NavTab, Nav(0, false), Nav(0.25f, false), Nav(0.55f, true), Nav(0, false), Focus(0));
        Fonts(t, NavTab, UiPalette.TextOnDarkMuted, UiPalette.TextOnDark, UiPalette.TextOnDark, UiPalette.TextOnDarkMuted with { A = 0.45f });
        t.SetFont("font", NavTab, UiFonts.Title);
        t.SetFontSize("font_size", NavTab, 30);

        // 页内子签（绢面）：文字签，选中在字下补一笔朱砂。
        OrnateBox Sub(Color line, Color fill) => new OrnateBox
        {
            FillA = fill, Ragged = 1, Sheen = line, SheenWidth = 3.2f, SheenInset = 0.1f, Brush = true, Seed = 13,
        }.Margins(20, 10);
        Variation(t, SubTab, "Button");
        States(t, SubTab,
            Sub(Colors.Transparent, Colors.Transparent),
            Sub(UiPalette.Ochre with { A = 0.55f }, Colors.Transparent),
            Sub(UiPalette.Cinnabar, UiPalette.SurfaceShade with { A = 0.6f }),
            Sub(Colors.Transparent, Colors.Transparent),
            Focus(0));
        Fonts(t, SubTab, UiPalette.TextMuted, UiPalette.Text, UiPalette.Text, UiPalette.TextMuted with { A = 0.5f });
        t.SetFont("font", SubTab, UiFonts.Title);
        t.SetFontSize("font_size", SubTab, 26);

        // 深色面按钮：黛底毛边，旧绢色笔线；按下为石青实底。
        OrnateBox DarkBox(Color fill, Color border) => new OrnateBox
        {
            FillA = fill, Ragged = 1.3f, Seed = 15, Border = border, BorderWidth = 1.2f, Brush = true, Overshoot = 0.45f,
        }.Margins(22, 10);
        Variation(t, DarkButton, "Button");
        States(t, DarkButton,
            DarkBox(UiPalette.Abyss with { A = 0.4f }, UiPalette.TextOnDarkMuted with { A = 0.5f }),
            DarkBox(UiPalette.Accent with { A = 0.55f }, UiPalette.Gilt with { A = 0.85f }),
            DarkBox(UiPalette.Accent, UiPalette.Gilt),
            DarkBox(UiPalette.Abyss with { A = 0.3f }, UiPalette.TextOnDarkMuted with { A = 0.2f }),
            Focus());
        Fonts(t, DarkButton, UiPalette.TextOnDark, UiPalette.TextOnDark, UiPalette.TextOnDark, UiPalette.TextOnDarkMuted with { A = 0.6f });

        // 标题菜单项：无框大字；悬停与键盘选中时，一笔石青自左刷开，左端点一粒朱砂。
        OrnateBox Streak(float a) => new OrnateBox
        {
            Swipe = UiPalette.Accent with { A = a }, SwipeReach = 1, Seed = 17,
            Diamond = a > 0, CornerColor = UiPalette.Cinnabar.Lightened(0.08f),
        }.Margins(48, 8);
        Variation(t, MenuItem, "Button");
        States(t, MenuItem, Streak(0), Streak(0.92f), Streak(1f), Streak(0), Streak(0.92f));
        Fonts(t, MenuItem, UiPalette.Text, UiPalette.TextOnDark, UiPalette.TextOnDark, UiPalette.TextMuted with { A = 0.5f });
        t.SetColor("font_focus_color", MenuItem, UiPalette.TextOnDark);
        t.SetFont("font", MenuItem, UiFonts.Title);
        t.SetFontSize("font_size", MenuItem, 40);
        t.SetColor("font_outline_color", MenuItem, UiPalette.Surface with { A = 0.55f });
        t.SetConstant("outline_size", MenuItem, 4);

        // 卡片按钮（存档卡、目录卡、招式格）：黛底绢纹；悬停、选中出泥金笔框与卷云角。
        OrnateBox Card(Color fill, Color border, bool corners, float swipe = 0) => new OrnateBox
        {
            FillA = fill, FillB = UiPalette.Abyss with { A = 0.92f }, Ragged = 1.4f, Seed = 19,
            Grain = Colors.White with { A = 0.05f }, Swipe = UiPalette.Accent with { A = swipe }, SwipeReach = 0.8f,
            Border = border, BorderWidth = 1.1f, Brush = true, Overshoot = 0.35f,
            Corners = corners ? CornerStyle.Cloud : CornerStyle.None, CornerSize = 30, CornerWidth = 1.8f,
            Shadow = UiPalette.Abyss with { A = 0.35f }, ShadowSize = 10, ShadowOffset = new Vector2(0, 5),
        }.Margins(20, 16);
        Variation(t, CardButton, "Button");
        States(t, CardButton,
            Card(UiPalette.PanelDark with { A = 0.88f }, UiPalette.TextOnDarkMuted with { A = 0.28f }, false),
            Card(UiPalette.PanelDark.Lightened(0.06f), UiPalette.Gilt with { A = 0.7f }, true, 0.35f),
            Card(UiPalette.PanelDark.Lightened(0.08f), UiPalette.Gilt, true, 0.6f),
            Card(UiPalette.PanelDark with { A = 0.5f }, UiPalette.TextOnDarkMuted with { A = 0.12f }, false),
            Focus(6));
        Fonts(t, CardButton, UiPalette.TextOnDark, UiPalette.TextOnDark, UiPalette.TextOnDark, UiPalette.TextOnDarkMuted);

        // 对话选项：黛色长条，悬停/选中时石青一笔刷开、左端朱点。
        OrnateBox Choice(float a, bool mark) => new OrnateBox
        {
            FillA = UiPalette.Abyss with { A = 0.78f }, FillB = UiPalette.PanelDark with { A = 0.55f }, Horizontal = true,
            Ragged = 1.5f, Seed = 21, Swipe = UiPalette.Accent with { A = a }, SwipeReach = 0.9f,
            Border = UiPalette.Gilt with { A = mark ? 0.8f : 0.25f }, BorderWidth = 1.1f, Brush = true,
            Diamond = mark, CornerColor = UiPalette.Cinnabar.Lightened(0.1f),
        }.Margins(48, 12);
        Variation(t, ChoiceButton, "Button");
        States(t, ChoiceButton, Choice(0, false), Choice(0.75f, true), Choice(0.95f, true), Choice(0, false), Choice(0.75f, true));
        Fonts(t, ChoiceButton, UiPalette.TextOnDark, UiPalette.TextOnDark, UiPalette.TextOnDark, UiPalette.TextOnDarkMuted with { A = 0.6f });
        t.SetColor("font_focus_color", ChoiceButton, UiPalette.TextOnDark);
        t.SetConstant("h_separation", ChoiceButton, 16);
    }

    private static void BuildPanels(Theme t)
    {
        t.SetStylebox("panel", "PanelContainer", new StyleBoxEmpty());

        // 绢本：主阅读面。暖绢底加绢纹，毛边；赭石手绘双线界格，四角泥金卷云，下落投影。
        Variation(t, SheetPanel, "PanelContainer");
        t.SetStylebox("panel", SheetPanel, new OrnateBox
        {
            FillA = UiPalette.Surface with { A = 0.98f }, FillB = UiPalette.SurfaceShade with { A = 0.98f },
            Ragged = 2.2f, Seed = 23, Grain = UiPalette.Ochre with { A = 0.09f },
            Border = UiPalette.Ochre with { A = 0.85f }, BorderWidth = 1.6f, Brush = true,
            Inner = UiPalette.Ochre with { A = 0.4f }, InnerInset = 9,
            Wash = UiPalette.Trim with { A = 0.1f },
            Corners = CornerStyle.Cloud, CornerSize = 64, CornerWidth = 2.4f, CornerColor = UiPalette.Gilt.Darkened(0.22f),
            CornerOutset = -2,
            Shadow = UiPalette.Abyss with { A = 0.6f }, ShadowSize = 26, ShadowOffset = new Vector2(0, 12),
        }.Margins(40, 22));

        Variation(t, InsetPanel, "PanelContainer");
        t.SetStylebox("panel", InsetPanel, new OrnateBox
        {
            FillA = UiPalette.SurfaceShade with { A = 0.45f }, FillB = UiPalette.SurfaceShade with { A = 0.8f },
            Ragged = 1.5f, Seed = 25, Border = UiPalette.Ochre with { A = 0.5f }, BorderWidth = 1.1f, Brush = true,
            Corners = CornerStyle.Bracket, CornerSize = 12, CornerWidth = 1.6f, CornerColor = UiPalette.Ochre,
        }.Margins(26, 22));

        // 印章：朱砂印泥，毛边与缺墨，内框一笔绢色。
        Variation(t, SealPanel, "PanelContainer");
        t.SetStylebox("panel", SealPanel, new OrnateBox
        {
            FillA = UiPalette.Cinnabar.Lightened(0.06f), FillB = UiPalette.Cinnabar.Darkened(0.12f),
            Ragged = 2, Seed = 27, Grain = UiPalette.Surface with { A = 0.28f }, GrainScale = 0.5f,
            Inner = UiPalette.Surface with { A = 0.65f }, InnerInset = 5, Brush = true,
            Shadow = UiPalette.Abyss with { A = 0.2f }, ShadowSize = 3, ShadowOffset = new Vector2(0, 2),
        }.Margins(15, 12));

        // 黛本：深色面板（存档、对话框、战斗指令区、结算）。黛底绢纹，泥金笔框与卷云角。
        Variation(t, DarkPanel, "PanelContainer");
        t.SetStylebox("panel", DarkPanel, new OrnateBox
        {
            FillA = UiPalette.PanelDark with { A = 0.95f }, FillB = UiPalette.Abyss with { A = 0.97f },
            Ragged = 2, Seed = 29, Grain = Colors.White with { A = 0.045f },
            Border = UiPalette.Gilt with { A = 0.7f }, BorderWidth = 1.5f, Brush = true,
            Inner = UiPalette.Gilt with { A = 0.22f }, InnerInset = 9,
            Wash = UiPalette.Accent with { A = 0.16f },
            Corners = CornerStyle.Cloud, CornerSize = 58, CornerWidth = 2.2f, CornerColor = UiPalette.Gilt,
            CornerOutset = -2,
            Shadow = UiPalette.Abyss with { A = 0.55f }, ShadowSize = 24, ShadowOffset = new Vector2(0, 10),
        }.Margins(34, 28));

        // 薄墨：HUD 与提示条。一片淡墨色块，毛边，无框无角饰，不抢画面。
        Variation(t, GlassPanel, "PanelContainer");
        t.SetStylebox("panel", GlassPanel, new OrnateBox
        {
            FillA = UiPalette.Abyss with { A = 0.6f }, FillB = UiPalette.PanelDark with { A = 0.74f },
            Ragged = 1.8f, Seed = 31, Grain = Colors.White with { A = 0.04f },
            Sheen = UiPalette.Gilt with { A = 0.35f }, SheenAtTop = true, SheenInset = 0.04f, SheenWidth = 1.2f, Brush = true,
        }.Margins(18, 12));

        Variation(t, KeyCapPanel, "PanelContainer");
        t.SetStylebox("panel", KeyCapPanel, new OrnateBox
        {
            FillA = UiPalette.Surface, FillB = UiPalette.SurfaceShade, Ragged = 0.8f, Seed = 33,
            Sheen = UiPalette.Ochre with { A = 0.6f }, SheenWidth = 2, Brush = true,
        }.Margins(9, 1));

        t.SetStylebox("panel", "TooltipPanel", new OrnateBox
        {
            FillA = UiPalette.PanelDark, FillB = UiPalette.Abyss, Ragged = 1.2f, Seed = 35,
            Border = UiPalette.Gilt with { A = 0.6f }, BorderWidth = 1.1f, Brush = true,
        }.Margins(14, 9));
        t.SetColor("font_color", "TooltipLabel", UiPalette.TextOnDark);
        t.SetFontSize("font_size", "TooltipLabel", UiPalette.FontSecondary);

        t.SetStylebox("panel", "PopupMenu", new OrnateBox
        {
            FillA = UiPalette.PanelDark, FillB = UiPalette.Abyss, Ragged = 1.2f, Seed = 37,
            Border = UiPalette.Gilt with { A = 0.6f }, BorderWidth = 1.1f, Brush = true,
        }.Margins(8, 8));
        t.SetStylebox("hover", "PopupMenu", new OrnateBox
        {
            Swipe = UiPalette.Accent, Seed = 39, Marker = UiPalette.Cinnabar, MarkerWidth = 3,
        });
        t.SetColor("font_color", "PopupMenu", UiPalette.TextOnDark);
        t.SetColor("font_hover_color", "PopupMenu", UiPalette.TextOnDark);
        t.SetColor("font_disabled_color", "PopupMenu", UiPalette.TextOnDarkMuted with { A = 0.6f });
        t.SetFontSize("font_size", "PopupMenu", UiPalette.FontSecondary);
        t.SetConstant("v_separation", "PopupMenu", 14);

        t.SetStylebox("separator", "HSeparator", new StyleBoxLine { Color = UiPalette.Ochre with { A = 0.5f }, Thickness = 2, GrowBegin = -8, GrowEnd = -8 });
        t.SetConstant("separation", "HSeparator", UiPalette.SpaceM);

        var track = new OrnateBox { FillA = UiPalette.Text with { A = 0.07f } };
        t.SetStylebox("scroll", "VScrollBar", track.Margins(3, 0));
        t.SetStylebox("grabber", "VScrollBar", new OrnateBox { FillA = UiPalette.Ochre with { A = 0.6f }, Ragged = 0.8f });
        t.SetStylebox("grabber_highlight", "VScrollBar", new OrnateBox { FillA = UiPalette.Accent, Ragged = 0.8f });
        t.SetStylebox("grabber_pressed", "VScrollBar", new OrnateBox { FillA = UiPalette.Text, Ragged = 0.8f });
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
            FillA = UiPalette.Abyss with { A = 0.24f }, FillB = UiPalette.Abyss with { A = 0.14f }, Ragged = 0.8f, Seed = 43,
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
        FillA = c.Darkened(0.12f), FillB = c.Lightened(0.18f), Horizontal = true, Ragged = 0.8f, Seed = 41,
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
