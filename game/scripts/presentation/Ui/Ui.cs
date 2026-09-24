using Godot;

namespace WuxiaWorld.Game.Presentation.Ui;

/// <summary>UI 节点工厂：统一套用主题变体，页面代码只描述结构。</summary>
public static class Ui
{
    public static Label Text(string text, string? variation = null, int? size = null, bool wrap = false)
    {
        var label = new Label { Text = text, ThemeTypeVariation = variation ?? "" };
        if (size is { } s)
        {
            label.AddThemeFontSizeOverride("font_size", s);
        }

        if (wrap)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            label.CustomMinimumSize = new Vector2(1, 0);
        }

        return label;
    }

    /// <summary>竖排文字：每字一行，用于页名章与竖排名牌。</summary>
    public static string Vertical(string text) => string.Join('\n', text.EnumerateRunes());

    public static VBoxContainer Column(int separation = UiPalette.SpaceM, params Control[] children)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        foreach (var child in children)
        {
            box.AddChild(child);
        }

        return box;
    }

    public static HBoxContainer Row(int separation = UiPalette.SpaceM, params Control[] children)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        foreach (var child in children)
        {
            box.AddChild(child);
        }

        return box;
    }

    public static PanelContainer Panel(string variation, Control child)
    {
        var panel = new PanelContainer { ThemeTypeVariation = variation };
        panel.AddChild(child);
        return panel;
    }

    public static Control Spacer(bool horizontal = true)
    {
        var spacer = new Control();
        if (horizontal)
        {
            spacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }
        else
        {
            spacer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        }

        return spacer;
    }

    public static T Expand<T>(T control, bool horizontal = true, bool vertical = false) where T : Control
    {
        if (horizontal)
        {
            control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }

        if (vertical)
        {
            control.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        }

        return control;
    }

    public static T MinSize<T>(T control, float width, float height = 0) where T : Control
    {
        control.CustomMinimumSize = new Vector2(width, height);
        return control;
    }

    public static Button Button(string text, string? variation = null, Action? onPressed = null, bool disabled = false, string? tooltip = null)
    {
        var button = new Button
        {
            Text = text,
            ThemeTypeVariation = variation ?? "",
            Disabled = disabled,
            TooltipText = tooltip ?? "",
            FocusMode = Control.FocusModeEnum.All,
        };
        if (onPressed is not null)
        {
            button.Pressed += onPressed;
        }

        return button;
    }

    /// <summary>可单选的一组按钮（页签、列表行、分类签）。</summary>
    public static Button Toggle(string text, string variation, ButtonGroup group, Action onSelected, bool selected = false)
    {
        var button = Button(text, variation);
        button.ToggleMode = true;
        button.ButtonGroup = group;
        button.Toggled += on =>
        {
            if (on)
            {
                onSelected();
            }
        };
        button.ButtonPressed = selected;
        return button;
    }

    /// <summary>
    /// 列表行：左侧可选图块，中间标题与副标题，右侧附注；整行是一个可单选的按钮。
    /// </summary>
    public static Button ListRow(ButtonGroup group, Action onSelected, string title, string? detail = null,
        string? trailing = null, Control? leading = null, bool selected = false)
    {
        var row = Toggle("", UiTheme.RowButton, group, onSelected, selected);
        var content = Row(UiPalette.SpaceM);
        content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        content.OffsetLeft = 16;
        content.OffsetRight = -16;
        content.MouseFilter = Control.MouseFilterEnum.Ignore;
        if (leading is not null)
        {
            leading.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            content.AddChild(leading);
        }

        var text = Column(2, Text(title));
        text.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        if (detail is not null)
        {
            text.AddChild(Text(detail, UiTheme.MutedLabel));
        }

        content.AddChild(Expand(text));
        if (trailing is not null)
        {
            var tail = Text(trailing, UiTheme.MutedLabel);
            tail.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            content.AddChild(tail);
        }

        row.AddChild(IgnoreMouse(content));
        row.CustomMinimumSize = new Vector2(0, leading is null && detail is null ? 56 : 80);
        return row;
    }

    /// <summary>开关：文字同时写出“开/关”，状态不只靠颜色表达。</summary>
    public static Button Switch(bool on, bool disabled = false)
    {
        var button = Button("", disabled: disabled);
        button.ToggleMode = true;
        button.ButtonPressed = on;
        button.CustomMinimumSize = new Vector2(120, 0);
        void Refresh(bool value) => button.Text = value ? "● 开" : "○ 关";
        Refresh(on);
        button.Toggled += Refresh;
        return button;
    }

    /// <summary>方印：四字两行的朱砂印，标题旁的落款。</summary>
    public static PanelContainer SquareSeal(string text, int size = 30)
    {
        var chars = text.EnumerateRunes().Select(r => r.ToString()).ToArray();
        var half = (chars.Length + 1) / 2;
        var label = Text(string.Concat(chars[..half]) + '\n' + string.Concat(chars[half..]), UiTheme.SealLabel, size);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AddThemeConstantOverride("line_spacing", -6);
        var seal = Panel(UiTheme.SealPanel, label);
        seal.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return seal;
    }

    /// <summary>朱砂页名章，竖排标题。</summary>
    public static PanelContainer Seal(string text)
    {
        var label = Text(Vertical(text), UiTheme.SealLabel);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        return Panel(UiTheme.SealPanel, label);
    }

    /// <summary>
    /// 字形印鉴：正式图标完成前，以单字加类别色的切角玉牌示意物品或招式类别。
    /// 仅用于 M0，不作为图标美术验收。
    /// </summary>
    public static PanelContainer Glyph(string glyph, Color tone, int size = 56)
    {
        var box = new OrnateBox
        {
            FillA = UiPalette.Surface, FillB = tone.Lerp(UiPalette.Surface, 0.72f), Chamfer = size / 9f,
            Ragged = size >= 72 ? 1.4f : 0.9f, Seed = glyph[0] % 97, Grain = tone with { A = 0.12f }, GrainScale = 0.5f,
            Border = tone, BorderWidth = size >= 72 ? 1.8f : 1.3f, Brush = true, Overshoot = 0.3f,
            Inner = tone with { A = 0.3f }, InnerInset = size / 12f,
        };
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(size, size) };
        panel.AddThemeStyleboxOverride("panel", box);
        var label = Text(glyph, UiTheme.TitleLabel, size / 2);
        label.AddThemeColorOverride("font_color", tone.Darkened(0.1f));
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        panel.AddChild(label);
        return panel;
    }

    public static ProgressBar Bar(string variation, double value, double max, float width = 280)
    {
        return new ProgressBar
        {
            ThemeTypeVariation = variation,
            MaxValue = max,
            Value = value,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(width, 16),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
    }

    /// <summary>分隔线：两端渐隐，正中一粒菱形。</summary>
    public static Control Rule(bool dark = false) => new DiamondRule { Dark = dark };

    /// <summary>小节标题：菱形、书法字、向右渐隐的细线。</summary>
    public static Control Section(string text, bool dark = false)
    {
        var label = Text(text, dark ? UiTheme.DarkTitleLabel : UiTheme.SectionLabel);
        if (dark)
        {
            label.AddThemeFontSizeOverride("font_size", 28);
        }

        var line = Expand(new DiamondRule { Dark = dark, Lead = true });
        line.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return Row(UiPalette.SpaceM, label, line);
    }

    /// <summary>按键提示：键帽加说明，放在界面底部的操作栏。</summary>
    public static Control KeyHint(string key, string action, bool dark = true)
    {
        var cap = Text(key, size: 17);
        cap.AddThemeFontOverride("font", UiFonts.BodyMedium);
        cap.AddThemeColorOverride("font_color", UiPalette.Abyss);
        cap.HorizontalAlignment = HorizontalAlignment.Center;
        var capPanel = MinSize(Panel(UiTheme.KeyCapPanel, cap), 34, 30);
        capPanel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return Row(UiPalette.SpaceS, capPanel, Text(action, dark ? UiTheme.DarkMutedLabel : UiTheme.MutedLabel));
    }

    /// <summary>一组按键提示，项间留出较宽间距。</summary>
    public static HBoxContainer KeyHints(bool dark, params (string Key, string Action)[] hints)
    {
        var row = Row(UiPalette.SpaceXl);
        foreach (var (key, action) in hints)
        {
            row.AddChild(KeyHint(key, action, dark));
        }

        return row;
    }

    /// <summary>按锚点（0 左/上，1 右/下）加偏移摆放，用于不经容器排版的全屏界面。</summary>
    public static T Place<T>(T c, float anchorX, float anchorY, float left, float top, float right, float bottom) where T : Control
    {
        c.AnchorLeft = c.AnchorRight = anchorX;
        c.AnchorTop = c.AnchorBottom = anchorY;
        c.OffsetLeft = left;
        c.OffsetTop = top;
        c.OffsetRight = right;
        c.OffsetBottom = bottom;
        return c;
    }

    /// <summary>让整棵子树不接收鼠标，点击落到外层按钮上（卡片、列表行）。</summary>
    public static T IgnoreMouse<T>(T root) where T : Control
    {
        root.MouseFilter = Control.MouseFilterEnum.Ignore;
        foreach (var node in root.FindChildren("*", owned: false))
        {
            if (node is Control c)
            {
                c.MouseFilter = Control.MouseFilterEnum.Ignore;
            }
        }

        return root;
    }

    public static void ClearChildren(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}
