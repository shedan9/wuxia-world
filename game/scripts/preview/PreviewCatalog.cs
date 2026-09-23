using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.App;

namespace WuxiaWorld.Game.Preview;

/// <summary>M0 场景目录：列出全部展示页，可直接进入；按 Esc 从任意页返回。</summary>
public partial class PreviewCatalog : Control
{
    public override void _Ready()
    {
        var background = new ColorRect { Color = UiPalette.PanelDark };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", UiPalette.SpaceXxl);
        }
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", UiPalette.SpaceM);
        margin.AddChild(column);

        column.AddChild(MakeLabel("武侠世界 · 场景目录", UiPalette.FontTitle, UiPalette.TextOnDark));
        column.AddChild(MakeLabel("M0 视觉 Demo：固定样例数据，不代表玩法已实现。Esc 返回目录。",
            UiPalette.FontSecondary, UiPalette.OldGold));

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", UiPalette.SpaceL);
        grid.AddThemeConstantOverride("v_separation", UiPalette.SpaceM);
        column.AddChild(grid);

        Button? first = null;
        foreach (var page in PreviewPages.All)
        {
            var button = MakePageButton(page);
            grid.AddChild(button);
            if (!button.Disabled)
            {
                first ??= button;
            }
        }

        first?.GrabFocus();
    }

    private static Button MakePageButton(PreviewPage page)
    {
        var router = AppHost.Instance.Router;
        var ready = page.ScenePath is not null && SceneRouter.CanGoTo(page.ScenePath);
        var button = new Button
        {
            Text = ready ? $"{page.Title}\n{page.Focus}" : $"{page.Title}（待制作）\n{page.Focus}",
            Disabled = !ready,
            CustomMinimumSize = new Vector2(800, 88),
            Alignment = HorizontalAlignment.Left,
            TooltipText = page.Id,
        };
        button.AddThemeFontSizeOverride("font_size", UiPalette.FontSecondary);
        if (ready)
        {
            button.Pressed += () => router.GoTo(page.ScenePath!);
        }

        return button;
    }

    private static Label MakeLabel(string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }
}
