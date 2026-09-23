using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Preview;

/// <summary>
/// M0 界面展示页的共用册页框架：左上朱砂印章与标题，左侧竖排册页签，
/// 中间宣纸页面承载页签内容。PageUp / PageDown 切换页签，Esc 返回目录（AppHost）。
/// </summary>
public abstract partial class PreviewScreen : Control
{
    private readonly ButtonGroup _tabGroup = new();
    private readonly List<Button> _tabs = [];
    private PanelContainer _sheet = null!;

    protected abstract string SealText { get; }
    protected abstract string Title { get; }
    protected abstract string Subtitle { get; }

    /// <summary>页签名称与内容构造函数；每次切换重建内容，保证示例状态从头展示。</summary>
    protected abstract IReadOnlyList<(string Name, Func<Control> Build)> Tabs { get; }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        var ground = new ColorRect { Color = UiPalette.PanelDark, MouseFilter = MouseFilterEnum.Ignore };
        ground.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(ground);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", UiPalette.SpaceXxl);
        margin.AddThemeConstantOverride("margin_right", UiPalette.SpaceXxl);
        margin.AddThemeConstantOverride("margin_top", UiPalette.SpaceXl);
        margin.AddThemeConstantOverride("margin_bottom", UiPalette.SpaceL);
        AddChild(margin);

        var page = Ui.Column(UiPalette.SpaceL);
        margin.AddChild(page);

        page.AddChild(BuildHeader());

        var body = Ui.Expand(Ui.Row(0), vertical: true);
        page.AddChild(body);

        var spine = Ui.Column(UiPalette.SpaceS);
        spine.CustomMinimumSize = new Vector2(64, 0);
        body.AddChild(spine);

        _sheet = Ui.Expand(new PanelContainer { ThemeTypeVariation = UiTheme.SheetPanel }, vertical: true);
        body.AddChild(_sheet);

        var tabs = Tabs;
        for (var i = 0; i < tabs.Count; i++)
        {
            var (name, build) = tabs[i];
            var tab = Ui.Toggle(Ui.Vertical(name), UiTheme.SpineTab, _tabGroup, () => Show(build));
            tab.TooltipText = name;
            spine.AddChild(tab);
            _tabs.Add(tab);
        }

        page.AddChild(Ui.Text("固定样例数据，不代表玩法已实现。PageUp / PageDown 切换页签。", UiTheme.DarkMutedLabel));

        var first = _tabs[Math.Clamp(DevCapture.Tab, 0, _tabs.Count - 1)];
        first.ButtonPressed = true;
        first.GrabFocus();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        var step = @event.IsActionPressed("ui_page_down") ? 1 : @event.IsActionPressed("ui_page_up") ? -1 : 0;
        if (step == 0 || _tabs.Count == 0)
        {
            return;
        }

        var current = _tabs.FindIndex(t => t.ButtonPressed);
        var next = _tabs[(current + step + _tabs.Count) % _tabs.Count];
        next.ButtonPressed = true;
        next.GrabFocus();
        GetViewport().SetInputAsHandled();
    }

    private Control BuildHeader()
    {
        var title = Ui.Text(Title, UiTheme.TitleLabel);
        title.AddThemeColorOverride("font_color", UiPalette.TextOnDark);

        var heading = Ui.Column(UiPalette.SpaceS, title, Ui.Text(Subtitle, UiTheme.DarkMutedLabel));
        heading.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        var back = Ui.Button("返回目录（Esc）", UiTheme.DarkButton,
            () => AppHost.Instance.Router.GoTo(ScenePaths.PreviewCatalog));
        back.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        return Ui.Row(UiPalette.SpaceL, Ui.Seal(SealText), heading, Ui.Spacer(), back);
    }

    private void Show(Func<Control> build)
    {
        Ui.ClearChildren(_sheet);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(Ui.Expand(build(), vertical: true));
        _sheet.AddChild(scroll);
    }
}
