using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Scenery;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview;

/// <summary>
/// 游戏菜单外框（docs/art/UI_DESIGN.md 第 5.2 节）：虚化山水暗底；顶栏左侧页名章与标题、
/// 中间分区签（Q / E 切换）、右侧地点与铜钱；中间玉版页面，顶部子页签（PageUp / PageDown）；
/// 底栏按键提示。Esc 回标题（AppHost）。
/// </summary>
public abstract partial class PreviewScreen : Control
{
    /// <summary>菜单分区与所在场景；顺序即 Q / E 切换顺序。</summary>
    private static readonly (string Name, string Scene)[] Sections =
    [
        ("人物", "res://scenes/preview/Character.tscn"),
        ("行囊", "res://scenes/preview/Inventory.tscn"),
        ("札记", "res://scenes/preview/Journal.tscn"),
        ("设置", "res://scenes/preview/Settings.tscn"),
    ];

    private readonly ButtonGroup _tabGroup = new();
    private readonly List<Button> _tabs = [];
    private MarginContainer _content = null!;

    protected abstract string SealText { get; }
    protected abstract string Title { get; }
    protected abstract string Subtitle { get; }

    /// <summary>页签名称与内容构造函数；每次切换重建内容，保证示例状态从头展示。</summary>
    protected abstract IReadOnlyList<(string Name, Func<Control> Build)> Tabs { get; }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(Backdrop.Veiled());

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 56);
        margin.AddThemeConstantOverride("margin_right", 56);
        margin.AddThemeConstantOverride("margin_top", 28);
        margin.AddThemeConstantOverride("margin_bottom", 22);
        AddChild(margin);

        var page = Ui.Column(UiPalette.SpaceL);
        margin.AddChild(page);
        page.AddChild(BuildHeader());

        var sheet = Ui.Expand(new PanelContainer { ThemeTypeVariation = UiTheme.SheetPanel }, vertical: true);
        page.AddChild(sheet);
        Motion.Enter(sheet, 0.05f, Motion.Normal, rise: 20);

        var strip = Ui.Row(UiPalette.SpaceS);
        var tabs = Tabs;
        for (var i = 0; i < tabs.Count; i++)
        {
            var (name, build) = tabs[i];
            var tab = Ui.Toggle(name, UiTheme.SubTab, _tabGroup, () => Show(build));
            strip.AddChild(tab);
            _tabs.Add(tab);
        }

        strip.AddChild(Ui.Spacer());
        var pager = Ui.KeyHints(false, ("PgUp", "上一页"), ("PgDn", "下一页"));
        pager.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        strip.AddChild(pager);

        _content = Ui.Expand(new MarginContainer(), vertical: true);
        _content.AddThemeConstantOverride("margin_top", UiPalette.SpaceS);
        sheet.AddChild(Ui.Column(UiPalette.SpaceS, strip, Ui.Rule(), _content));

        page.AddChild(BuildFooter());

        var first = _tabs[Math.Clamp(DevCapture.Tab, 0, _tabs.Count - 1)];
        first.ButtonPressed = true;
        first.GrabFocus();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key && key.Keycode is Key.Q or Key.E)
        {
            var current = Array.FindIndex(Sections, s => s.Scene == SceneFilePath);
            var next = Sections[(current + (key.Keycode == Key.E ? 1 : -1) + Sections.Length) % Sections.Length];
            AppHost.Instance.Router.GoTo(next.Scene);
            GetViewport().SetInputAsHandled();
            return;
        }

        var step = @event.IsActionPressed("ui_page_down") ? 1 : @event.IsActionPressed("ui_page_up") ? -1 : 0;
        if (step == 0 || _tabs.Count == 0)
        {
            return;
        }

        var index = _tabs.FindIndex(t => t.ButtonPressed);
        var target = _tabs[(index + step + _tabs.Count) % _tabs.Count];
        target.ButtonPressed = true;
        target.GrabFocus();
        GetViewport().SetInputAsHandled();
    }

    private Control BuildHeader()
    {
        var title = Ui.Text(Title, UiTheme.DarkTitleLabel, 38);
        var heading = Ui.Column(4, title, Ui.Text(Subtitle, UiTheme.DarkMutedLabel));
        heading.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var left = Ui.Row(UiPalette.SpaceL, Ui.Seal(SealText), heading);

        // 分区签：两端是 Q / E 键帽。
        var nav = Ui.Row(UiPalette.SpaceS, Cap("Q"));
        var router = AppHost.Instance.Router;
        var group = new ButtonGroup();
        foreach (var (name, scene) in Sections)
        {
            var here = scene == SceneFilePath;
            var tab = Ui.Button(name, UiTheme.NavTab, here ? null : () => router.GoTo(scene));
            tab.ToggleMode = true;
            tab.ButtonGroup = group;
            tab.ButtonPressed = here;
            tab.FocusMode = FocusModeEnum.None;
            tab.CustomMinimumSize = new Vector2(112, 0);
            nav.AddChild(tab);
        }

        nav.AddChild(Cap("E"));
        nav.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        var place = Ui.Text("芦湾　江南客栈　·　申时", UiTheme.DarkMutedLabel);
        place.HorizontalAlignment = HorizontalAlignment.Right;
        var money = Ui.Row(UiPalette.SpaceS, Ui.Glyph("钱", UiPalette.Gilt.Darkened(0.2f), 30),
            Ui.Text($"{InventorySamples.Money:N0} 文", UiTheme.DarkLabel));
        money.Alignment = BoxContainer.AlignmentMode.End;
        var status = Ui.Column(4, place, money);
        status.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        // 左右两块等宽扩展，分区签保持居中。
        var header = Ui.Row(UiPalette.SpaceL, Ui.Expand(left), nav, Ui.Expand(status));
        Motion.Enter(header, 0, Motion.Normal, rise: -12);
        return header;
    }

    private static Control BuildFooter()
    {
        var note = Ui.Text("固定样例数据，不代表玩法已实现", UiTheme.DarkMutedLabel, 18);
        var hints = Ui.KeyHints(true, ("Q/E", "切换分区"), ("PgUp/PgDn", "切换页签"), ("Enter", "确认"), ("Esc", "返回标题"));
        var bar = Ui.Row(UiPalette.SpaceL, note, Ui.Spacer(), hints);
        note.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return bar;
    }

    private static Control Cap(string key)
    {
        var cap = Ui.KeyHint(key, "");
        cap.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return cap;
    }

    private void Show(Func<Control> build)
    {
        Ui.ClearChildren(_content);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var body = Ui.Expand(build(), vertical: true);
        scroll.AddChild(body);
        _content.AddChild(scroll);
        Motion.Enter(body, 0, Motion.Normal, rise: 10);
    }
}
