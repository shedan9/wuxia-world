using Godot;

namespace WuxiaWorld.Game.Presentation.Ui;

/// <summary>
/// 全局 UI 主题（架构文档 10.4）。基础类型按“宣纸面”配色；深色面上的控件使用
/// 下列类型变体。由 AppHost 挂到根窗口，所有场景共享。
/// </summary>
public static class UiTheme
{
    public const string PrimaryButton = "PrimaryButton";
    public const string RowButton = "RowButton";
    public const string ChipButton = "ChipButton";
    public const string SpineTab = "SpineTab";
    public const string DarkButton = "DarkButton";

    public const string DarkLabel = "DarkLabel";
    public const string DarkMutedLabel = "DarkMutedLabel";
    public const string MutedLabel = "MutedLabel";
    public const string AccentLabel = "AccentLabel";
    public const string TitleLabel = "TitleLabel";
    public const string SectionLabel = "SectionLabel";
    public const string SealLabel = "SealLabel";

    public const string SheetPanel = "SheetPanel";
    public const string InsetPanel = "InsetPanel";
    public const string SealPanel = "SealPanel";
    public const string DarkPanel = "DarkPanel";

    public const string HealthBar = "HealthBar";
    public const string InnerBar = "InnerBar";

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
        t.SetColor("font_color", "Label", UiPalette.Ink);
        t.SetConstant("line_spacing", "Label", 6);

        Variation(t, DarkLabel, "Label");
        t.SetColor("font_color", DarkLabel, UiPalette.TextOnDark);

        Variation(t, DarkMutedLabel, "Label");
        t.SetColor("font_color", DarkMutedLabel, UiPalette.TextOnDarkMuted);
        t.SetFontSize("font_size", DarkMutedLabel, UiPalette.FontSecondary);

        Variation(t, MutedLabel, "Label");
        t.SetColor("font_color", MutedLabel, UiPalette.InkMuted);
        t.SetFontSize("font_size", MutedLabel, UiPalette.FontSecondary);

        Variation(t, AccentLabel, "Label");
        t.SetColor("font_color", AccentLabel, UiPalette.Cinnabar);

        Variation(t, TitleLabel, "Label");
        t.SetFont("font", TitleLabel, UiFonts.Title);
        t.SetFontSize("font_size", TitleLabel, UiPalette.FontTitle);

        Variation(t, SectionLabel, "Label");
        t.SetFont("font", SectionLabel, UiFonts.Title);
        t.SetFontSize("font_size", SectionLabel, 28);

        Variation(t, SealLabel, "Label");
        t.SetFont("font", SealLabel, UiFonts.Title);
        t.SetFontSize("font_size", SealLabel, 34);
        t.SetColor("font_color", SealLabel, UiPalette.Paper);
        t.SetConstant("line_spacing", SealLabel, -4);

        t.SetColor("default_color", "RichTextLabel", UiPalette.Ink);
        t.SetFont("normal_font", "RichTextLabel", UiFonts.Body);
        t.SetFont("bold_font", "RichTextLabel", UiFonts.Title);
        t.SetFontSize("normal_font_size", "RichTextLabel", UiPalette.FontBody);
        t.SetFontSize("bold_font_size", "RichTextLabel", UiPalette.FontBody);
        t.SetConstant("line_separation", "RichTextLabel", 6);
    }

    private static void BuildButtons(Theme t)
    {
        var focus = Box(Colors.Transparent, UiPalette.Ink, 2, 3, expand: 3);

        // 基础按钮：墨线框，宣纸面上的次要操作。
        ButtonStates(t, "Button",
            normal: Box(Colors.Transparent, UiPalette.Ink, 1),
            hover: Box(UiPalette.PaperShade, UiPalette.Ink, 1),
            pressed: Box(UiPalette.Ink, UiPalette.Ink, 1),
            disabled: Box(Colors.Transparent, UiPalette.InkMuted with { A = 0.45f }, 1),
            focus: focus);
        ButtonFonts(t, "Button", UiPalette.Ink, UiPalette.Ink, UiPalette.Paper, UiPalette.InkMuted);

        Variation(t, PrimaryButton, "Button");
        ButtonStates(t, PrimaryButton,
            normal: Box(UiPalette.Cinnabar),
            hover: Box(UiPalette.Cinnabar.Darkened(0.15f)),
            pressed: Box(UiPalette.Cinnabar.Darkened(0.3f)),
            disabled: Box(UiPalette.InkMuted with { A = 0.25f }),
            focus: focus);
        ButtonFonts(t, PrimaryButton, UiPalette.Paper, UiPalette.Paper, UiPalette.Paper, UiPalette.InkMuted);

        // 列表行：选中时底色加深并在左侧出现朱砂竖线，不只靠颜色区分。
        Variation(t, RowButton, "Button");
        var rowPressed = Box(UiPalette.PaperShade, margin: 12);
        rowPressed.BorderColor = UiPalette.Cinnabar;
        rowPressed.BorderWidthLeft = 6;
        ButtonStates(t, RowButton,
            normal: Box(Colors.Transparent, margin: 12),
            hover: Box(UiPalette.PaperShade with { A = 0.55f }, margin: 12),
            pressed: rowPressed,
            disabled: Box(Colors.Transparent, margin: 12),
            focus: Box(Colors.Transparent, UiPalette.Ink, 2, 2, expand: 0));
        ButtonFonts(t, RowButton, UiPalette.Ink, UiPalette.Ink, UiPalette.Ink, UiPalette.InkMuted);

        Variation(t, ChipButton, "Button");
        ButtonStates(t, ChipButton,
            normal: Box(Colors.Transparent, UiPalette.OldGold, 1, 14, marginX: 16, marginY: 4),
            hover: Box(UiPalette.PaperShade, UiPalette.OldGold, 1, 14, marginX: 16, marginY: 4),
            pressed: Box(UiPalette.Ink, UiPalette.Ink, 1, 14, marginX: 16, marginY: 4),
            disabled: Box(Colors.Transparent, UiPalette.InkMuted with { A = 0.3f }, 1, 14, marginX: 16, marginY: 4),
            focus: Box(Colors.Transparent, UiPalette.Ink, 2, 16, expand: 3));
        ButtonFonts(t, ChipButton, UiPalette.Ink, UiPalette.Ink, UiPalette.Paper, UiPalette.InkMuted);
        t.SetFontSize("font_size", ChipButton, UiPalette.FontSecondary);

        // 册页签：深色底上的竖排页签，选中时与宣纸页面连成一片。
        Variation(t, SpineTab, "Button");
        ButtonStates(t, SpineTab,
            normal: Box(Colors.Transparent, UiPalette.OldGold with { A = 0.5f }, 1, 0, marginX: 12, marginY: 16),
            hover: Box(UiPalette.Ink, UiPalette.OldGold, 1, 0, marginX: 12, marginY: 16),
            pressed: Box(UiPalette.Paper, UiPalette.Paper, 1, 0, marginX: 12, marginY: 16),
            disabled: Box(Colors.Transparent, UiPalette.TextOnDarkMuted with { A = 0.3f }, 1, 0, marginX: 12, marginY: 16),
            focus: Box(Colors.Transparent, UiPalette.OldGold, 2, 0, expand: 3));
        ButtonFonts(t, SpineTab, UiPalette.TextOnDark, UiPalette.TextOnDark, UiPalette.Ink, UiPalette.TextOnDarkMuted);
        t.SetFont("font", SpineTab, UiFonts.Title);
        t.SetFontSize("font_size", SpineTab, 26);

        Variation(t, DarkButton, "Button");
        ButtonStates(t, DarkButton,
            normal: Box(Colors.Transparent, UiPalette.OldGold with { A = 0.6f }, 1),
            hover: Box(UiPalette.Ink, UiPalette.OldGold, 1),
            pressed: Box(UiPalette.OldGold, UiPalette.OldGold, 1),
            disabled: Box(Colors.Transparent, UiPalette.TextOnDarkMuted with { A = 0.3f }, 1),
            focus: Box(Colors.Transparent, UiPalette.OldGold, 2, 3, expand: 3));
        ButtonFonts(t, DarkButton, UiPalette.TextOnDark, UiPalette.TextOnDark, UiPalette.PanelDark, UiPalette.TextOnDarkMuted);
    }

    private static void BuildPanels(Theme t)
    {
        t.SetStylebox("panel", "PanelContainer", Box(Colors.Transparent, margin: 0));

        Variation(t, SheetPanel, "PanelContainer");
        t.SetStylebox("panel", SheetPanel, Box(UiPalette.Paper, radius: 0, margin: UiPalette.SpaceXl));

        Variation(t, InsetPanel, "PanelContainer");
        t.SetStylebox("panel", InsetPanel, Box(UiPalette.PaperShade with { A = 0.6f }, UiPalette.OldGold with { A = 0.7f }, 1, 2, margin: UiPalette.SpaceL));

        Variation(t, SealPanel, "PanelContainer");
        t.SetStylebox("panel", SealPanel, Box(UiPalette.Cinnabar, UiPalette.Cinnabar.Lightened(0.2f), 2, 4, marginX: 14, marginY: 10));

        Variation(t, DarkPanel, "PanelContainer");
        t.SetStylebox("panel", DarkPanel, Box(UiPalette.PanelDark with { A = 0.92f }, UiPalette.OldGold with { A = 0.6f }, 1, 2, margin: UiPalette.SpaceL));

        t.SetStylebox("panel", "TooltipPanel", Box(UiPalette.PanelDark, UiPalette.OldGold, 1, 2, marginX: 12, marginY: 8));
        t.SetColor("font_color", "TooltipLabel", UiPalette.TextOnDark);
        t.SetFontSize("font_size", "TooltipLabel", UiPalette.FontSecondary);

        t.SetStylebox("panel", "PopupMenu", Box(UiPalette.PanelDark, UiPalette.OldGold, 1, 2, margin: 8));
        t.SetStylebox("hover", "PopupMenu", Box(UiPalette.Ink, UiPalette.OldGold, 1, 2));
        t.SetColor("font_color", "PopupMenu", UiPalette.TextOnDark);
        t.SetColor("font_hover_color", "PopupMenu", UiPalette.TextOnDark);
        t.SetColor("font_disabled_color", "PopupMenu", UiPalette.TextOnDarkMuted with { A = 0.6f });
        t.SetFontSize("font_size", "PopupMenu", UiPalette.FontSecondary);
        t.SetConstant("v_separation", "PopupMenu", 12);

        var sep = new StyleBoxLine { Color = UiPalette.OldGold with { A = 0.6f }, Thickness = 1 };
        t.SetStylebox("separator", "HSeparator", sep);
        t.SetConstant("separation", "HSeparator", UiPalette.SpaceM);

        var scrollTrack = Box(UiPalette.Ink with { A = 0.08f }, radius: 4, margin: 0);
        var grabber = Box(UiPalette.OldGold, radius: 4, margin: 0);
        t.SetStylebox("scroll", "VScrollBar", scrollTrack);
        t.SetStylebox("grabber", "VScrollBar", grabber);
        t.SetStylebox("grabber_highlight", "VScrollBar", Box(UiPalette.OldGold.Darkened(0.2f), radius: 4, margin: 0));
        t.SetStylebox("grabber_pressed", "VScrollBar", Box(UiPalette.Ink, radius: 4, margin: 0));
    }

    private static void BuildInputs(Theme t)
    {
        // 滑杆：墨色细轨、朱砂已填段。
        t.SetStylebox("slider", "HSlider", Box(UiPalette.Ink with { A = 0.25f }, radius: 2, marginX: 0, marginY: 3));
        t.SetStylebox("grabber_area", "HSlider", Box(UiPalette.Cinnabar, radius: 2, marginX: 0, marginY: 3));
        t.SetStylebox("grabber_area_highlight", "HSlider", Box(UiPalette.Cinnabar.Darkened(0.15f), radius: 2, marginX: 0, marginY: 3));
        t.SetIcon("grabber", "HSlider", Knob(UiPalette.Ink, UiPalette.Paper));
        t.SetIcon("grabber_highlight", "HSlider", Knob(UiPalette.Cinnabar, UiPalette.Paper));
        t.SetIcon("grabber_disabled", "HSlider", Knob(UiPalette.InkMuted, UiPalette.Paper));
        t.SetStylebox("focus", "HSlider", Box(Colors.Transparent, UiPalette.Ink, 2, 3, expand: 6));

        ButtonStates(t, "OptionButton",
            normal: Box(UiPalette.Paper, UiPalette.Ink, 1, marginX: 16, marginY: 8),
            hover: Box(UiPalette.PaperShade, UiPalette.Ink, 1, marginX: 16, marginY: 8),
            pressed: Box(UiPalette.PaperShade, UiPalette.Ink, 1, marginX: 16, marginY: 8),
            disabled: Box(Colors.Transparent, UiPalette.InkMuted with { A = 0.45f }, 1, marginX: 16, marginY: 8),
            focus: Box(Colors.Transparent, UiPalette.Ink, 2, 3, expand: 3));
        ButtonFonts(t, "OptionButton", UiPalette.Ink, UiPalette.Ink, UiPalette.Ink, UiPalette.InkMuted);
        t.SetConstant("modulate_arrow", "OptionButton", 1);

        t.SetStylebox("background", "ProgressBar", Box(UiPalette.Ink with { A = 0.18f }, radius: 2, margin: 0));
        t.SetStylebox("fill", "ProgressBar", Box(UiPalette.InkMuted, radius: 2, margin: 0));
        t.SetColor("font_color", "ProgressBar", UiPalette.Ink);
        t.SetFontSize("font_size", "ProgressBar", 16);

        Variation(t, HealthBar, "ProgressBar");
        t.SetStylebox("fill", HealthBar, Box(UiPalette.Cinnabar, radius: 2, margin: 0));
        Variation(t, InnerBar, "ProgressBar");
        t.SetStylebox("fill", InnerBar, Box(UiPalette.Mountain, radius: 2, margin: 0));
    }

    private static void Variation(Theme t, string name, string baseType) => t.SetTypeVariation(name, baseType);

    private static void ButtonStates(Theme t, string type, StyleBox normal, StyleBox hover, StyleBox pressed, StyleBox disabled, StyleBox focus)
    {
        t.SetStylebox("normal", type, normal);
        t.SetStylebox("hover", type, hover);
        t.SetStylebox("pressed", type, pressed);
        t.SetStylebox("hover_pressed", type, pressed);
        t.SetStylebox("disabled", type, disabled);
        t.SetStylebox("focus", type, focus);
    }

    private static void ButtonFonts(Theme t, string type, Color normal, Color hover, Color pressed, Color disabled)
    {
        t.SetColor("font_color", type, normal);
        t.SetColor("font_focus_color", type, normal);
        t.SetColor("font_hover_color", type, hover);
        t.SetColor("font_pressed_color", type, pressed);
        t.SetColor("font_hover_pressed_color", type, pressed);
        t.SetColor("font_disabled_color", type, disabled);
    }

    private static StyleBoxFlat Box(Color bg, Color? border = null, int borderWidth = 0, int radius = 2,
        int margin = -1, int marginX = 20, int marginY = 10, int expand = 0)
    {
        var box = new StyleBoxFlat { BgColor = bg, DrawCenter = bg.A > 0 };
        if (border is { } b && borderWidth > 0)
        {
            box.BorderColor = b;
            box.SetBorderWidthAll(borderWidth);
        }

        box.SetCornerRadiusAll(radius);
        box.ContentMarginLeft = box.ContentMarginRight = margin >= 0 ? margin : marginX;
        box.ContentMarginTop = box.ContentMarginBottom = margin >= 0 ? margin : marginY;
        if (expand > 0)
        {
            box.SetExpandMarginAll(expand);
        }

        return box;
    }

    /// <summary>滑杆把手：实心圆加宣纸色内环，运行时生成，避免依赖图片资源。</summary>
    private static ImageTexture Knob(Color fill, Color ring)
    {
        const int size = 28;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var c = (size - 1) / 2f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var d = new Vector2(x - c, y - c).Length();
                var color = d <= 7 ? ring : d <= 12.5f ? fill : Colors.Transparent;
                if (d > 12.5f && d < 13.5f)
                {
                    color = fill with { A = 13.5f - d };
                }

                image.SetPixel(x, y, color);
            }
        }

        return ImageTexture.CreateFromImage(image);
    }
}
