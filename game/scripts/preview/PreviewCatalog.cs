using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Scenery;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Preview;

/// <summary>M0 场景目录：以卡片列出全部展示页，可直接进入；Esc 从任意页回标题。</summary>
public partial class PreviewCatalog : Control
{
    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(Backdrop.Veiled());

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 96);
        margin.AddThemeConstantOverride("margin_right", 96);
        margin.AddThemeConstantOverride("margin_top", 56);
        margin.AddThemeConstantOverride("margin_bottom", 28);
        AddChild(margin);

        var ready = PreviewPages.All.Count(p => p.ScenePath is not null && SceneRouter.CanGoTo(p.ScenePath));
        var heading = Ui.Column(4,
            Ui.Text("场景目录", UiTheme.DarkTitleLabel, 44),
            Ui.Text("M0 视觉 Demo：每页可直接进入与返回，内容为固定样例数据，不代表玩法已实现。", UiTheme.DarkMutedLabel));
        heading.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var count = Ui.Text($"已完成 {ready} / {PreviewPages.All.Count}", UiTheme.GiltLabel, 24);
        count.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var header = Ui.Row(UiPalette.SpaceL, Ui.Seal("目录"), heading, Ui.Spacer(), count);

        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", UiPalette.SpaceL);
        grid.AddThemeConstantOverride("v_separation", UiPalette.SpaceL);

        Button? first = null;
        foreach (var page in PreviewPages.All)
        {
            var card = PageCard(page);
            grid.AddChild(card);
            if (!card.Disabled)
            {
                first ??= card;
            }
        }

        Motion.Stagger(grid.GetChildren().OfType<Control>(), 0.05f, 0.03f);

        var footer = Ui.Row(UiPalette.SpaceL, Ui.Spacer(),
            Ui.KeyHints(true, ("方向键", "选择"), ("Enter", "进入"), ("Esc", "返回标题")));

        margin.AddChild(Ui.Column(UiPalette.SpaceXl, header, Ui.Rule(dark: true), Ui.Expand(grid, vertical: true), footer));
        first?.GrabFocus();
    }

    private static Button PageCard(PreviewPage page)
    {
        var router = AppHost.Instance.Router;
        var ready = page.ScenePath is not null && SceneRouter.CanGoTo(page.ScenePath);
        var card = new Button
        {
            ThemeTypeVariation = UiTheme.CardButton,
            Disabled = !ready,
            CustomMinimumSize = new Vector2(0, 136),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = page.Id,
            FocusMode = FocusModeEnum.All,
        };
        card.MouseEntered += () =>
        {
            if (!card.Disabled)
            {
                card.GrabFocus();
            }
        };
        if (ready)
        {
            card.Pressed += () => router.GoTo(page.ScenePath!);
        }

        var title = Ui.Text(page.Title, UiTheme.DarkTitleLabel, 28);
        var state = Ui.Text(ready ? "可进入" : "待制作", ready ? UiTheme.GiltLabel : UiTheme.DarkMutedLabel, 17);
        state.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var focus = Ui.Text(page.Focus, UiTheme.DarkMutedLabel, 18, wrap: true);
        var body = Ui.Column(6, Ui.Row(UiPalette.SpaceM, Ui.Expand(title), state), focus);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", 22);
        }

        margin.AddChild(body);
        Ui.IgnoreMouse(margin);

        if (!ready)
        {
            margin.Modulate = new Color(1, 1, 1, 0.55f);
        }

        card.AddChild(margin);
        return card;
    }
}
