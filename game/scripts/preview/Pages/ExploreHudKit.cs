using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 探索 HUD 的共用部件（docs/art/UI_DESIGN.md 第 5.3 节），供 HUD 界面层页与各探索布景页共用，
/// 版位与样式只在这里定义一次。
/// </summary>
public static class ExploreHudKit
{
    /// <summary>左上：页名章写地区，书法地点名，时辰与天气。</summary>
    public static Control Place(string region, string name, string time)
    {
        var seal = Ui.Seal(region);
        var column = Ui.Column(2, Ui.Text(name, UiTheme.DarkTitleLabel, 30), Ui.Text(time, UiTheme.DarkMutedLabel, 18));
        column.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var panel = Ui.Panel(UiTheme.GlassPanel, Ui.Row(UiPalette.SpaceM, seal, column));
        Motion.Enter(panel, 0.1f, Motion.Normal, rise: -10);
        return Ui.Place(panel, 0, 0, 40, 32, 480, 180);
    }

    /// <summary>右上：泥金卷云角框小地图，下方地区名与 M 大地图。</summary>
    public static Control MiniMapFrame(Control map, string region)
    {
        map.CustomMinimumSize = new Vector2(280, 280);
        var frame = new PanelContainer();
        frame.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            FillA = UiPalette.PanelDark with { A = 0.85f }, FillB = UiPalette.Abyss with { A = 0.85f }, Ragged = 1.8f, Seed = 63,
            Grain = Colors.White with { A = 0.04f }, Border = UiPalette.Gilt with { A = 0.6f }, BorderWidth = 1.4f, Brush = true,
            Corners = CornerStyle.Cloud, CornerSize = 34, CornerWidth = 2,
        }.Margins(8, 8));
        var caption = Ui.Row(UiPalette.SpaceS, Ui.Text(region, UiTheme.GiltLabel, 18), Ui.Spacer(), Ui.KeyHint("M", "大地图"));
        frame.AddChild(Ui.Column(UiPalette.SpaceS, map, caption));
        return Ui.Place(frame, 1, 0, -336, 32, -40, 380);
    }

    /// <summary>左侧：主线阶段与一条支线的目标追踪（默认为第一章样例）。</summary>
    public static Control Tracker() => Tracker(JournalSamples.Quests[0], JournalSamples.Quests[1]);

    public static Control Tracker(SampleQuest main, SampleQuest side)
    {
        var list = Ui.Column(UiPalette.SpaceS,
            Ui.Row(UiPalette.SpaceS, Ui.Text("◆", UiTheme.GiltLabel, 16), Ui.Text($"{main.Kind}　{main.Name}", UiTheme.GiltLabel, 20)));
        foreach (var (text, state) in main.Stages.Where(s => s.State != StageState.Hidden))
        {
            var done = state == StageState.Done;
            var line = Ui.Text($"{(done ? "✓" : "○")}　{text}", done ? UiTheme.DarkMutedLabel : UiTheme.DarkLabel, done ? 17 : 20, wrap: true);
            list.AddChild(line);
        }

        list.AddChild(Ui.Rule(dark: true));
        list.AddChild(Ui.Text($"{side.Kind}　{side.Name}", UiTheme.DarkMutedLabel, 18));
        list.AddChild(Ui.Text($"○　{side.Stages[0].Text}", UiTheme.DarkMutedLabel, 17));
        list.AddChild(Ui.KeyHint("J", "札记"));

        var panel = Ui.Panel(UiTheme.GlassPanel, list);
        Motion.Enter(panel, 0.2f, Motion.Normal, fromX: -20, rise: 0);
        return Ui.Place(panel, 0, 0, 40, 220, 480, 560);
    }

    /// <summary>左下：印鉴头像 + 气血 / 内力细条，不写数值。</summary>
    public static Control Party()
    {
        var row = Ui.Row(UiPalette.SpaceM);
        foreach (var (glyph, name, hp, inner, tone) in new[]
                 {
                     ("主", "主角", 0.88, 0.8, UiPalette.Accent),
                     ("陆", "陆青禾", 0.74, 0.5, UiPalette.Trim),
                 })
        {
            var hpBar = Ui.Bar(UiTheme.HealthBar, hp, 1, 150);
            hpBar.CustomMinimumSize = new Vector2(150, 10);
            var innerBar = Ui.Bar(UiTheme.InnerBar, inner, 1, 150);
            innerBar.CustomMinimumSize = new Vector2(150, 6);
            var bars = Ui.Column(4, Ui.Text(name, UiTheme.DarkLabel, 18), hpBar, innerBar);
            bars.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(Ui.Row(UiPalette.SpaceS, Ui.Glyph(glyph, tone, 56), bars));
        }

        return Ui.Place(Ui.Panel(UiTheme.GlassPanel, row), 0, 1, 40, -128, 520, -40);
    }

    /// <summary>右下：快捷键。</summary>
    public static Control Shortcuts(params (string Key, string Action)[] hints)
    {
        var row = Ui.KeyHints(true, hints);
        return Ui.Place(Ui.Panel(UiTheme.GlassPanel, row), 1, 1, -40 - 150 * hints.Length, -96, -40, -40);
    }
}

/// <summary>画面边缘的目标方向：泥金圆章内一枚朝向目标的箭头，下写目标名；目标进入画面后隐去。</summary>
public partial class GoalPointer : Control
{
    public string Label { get; init; } = "";

    public Vector2 Direction { get; set; } = Vector2.Up;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    public override void _Draw()
    {
        var pulse = Motion.Enabled ? 1 + Mathf.Sin(Time.GetTicksMsec() / 300f) * 0.06f : 1;
        var r = 24 * pulse;
        DrawCircle(Vector2.Zero, r + 3, UiPalette.Abyss with { A = 0.85f });
        DrawArc(Vector2.Zero, r, 0, Mathf.Tau, 40, UiPalette.Gilt with { A = 0.9f }, 2.4f, true);
        var f = Direction.Normalized();
        var side = new Vector2(-f.Y, f.X);
        Vector2[] arrow = [f * r * 0.72f, -f * r * 0.42f + side * r * 0.5f, -f * r * 0.18f, -f * r * 0.42f - side * r * 0.5f];
        DrawColoredPolygon(arrow, UiPalette.Gilt.Lightened(0.15f));
        // 名称写在圆章朝画面内的一侧，不出屏。
        var text = f.Y > 0.5f ? new Vector2(0, -r - 14) : new Vector2(0, r + 26);
        var width = UiFonts.Title.GetStringSize(Label, HorizontalAlignment.Left, -1, 20).X;
        var at = text - new Vector2(width / 2 + Mathf.Clamp(f.X, -1, 1) * width * 0.5f, 0);
        DrawRect(new Rect2(at + new Vector2(-8, -21), new Vector2(width + 16, 28)), UiPalette.Abyss with { A = 0.78f });
        DrawString(UiFonts.Title, at, Label, HorizontalAlignment.Left, -1, 20, UiPalette.Gilt.Lightened(0.2f));
    }

    public override void _Process(double delta)
    {
        if (Visible && Motion.Enabled)
        {
            QueueRedraw();
        }
    }
}

/// <summary>中下交互提示：E + 动作 + 对象，泥金折角，缓慢呼吸。</summary>
public partial class InteractPrompt : PanelContainer
{
    private readonly Label _verb = Ui.Text("", UiTheme.DarkLabel, 24);
    private readonly Label _target = Ui.Text("", UiTheme.GiltLabel, 22);

    public InteractPrompt()
    {
        AddThemeStyleboxOverride("panel", new OrnateBox
        {
            FillA = UiPalette.Abyss with { A = 0.88f }, FillB = UiPalette.PanelDark with { A = 0.85f }, Horizontal = true,
            Ragged = 1.6f, Seed = 65, Border = UiPalette.Gilt with { A = 0.75f }, BorderWidth = 1.3f, Brush = true, Overshoot = 0.6f,
            Corners = CornerStyle.Bracket, CornerSize = 10, CornerWidth = 2, CornerOutset = 4,
        }.Margins(20, 10));
        AddChild(Ui.Row(UiPalette.SpaceM, Ui.KeyHint("E", ""), _verb, _target));
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Show(string verb, string target)
    {
        _verb.Text = verb;
        _target.Text = target;
        Visible = true;
    }

    public override void _Ready() => Motion.Pulse(this, 0.75f, 2.2f);
}

/// <summary>上中通知：朱砂竖笔 + 类别、内容、去处；最多 3 条，新条目上浮淡入。</summary>
public partial class ToastColumn : VBoxContainer
{
    public ToastColumn()
    {
        AddThemeConstantOverride("separation", UiPalette.SpaceS);
        Alignment = AlignmentMode.Begin;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public static ToastColumn Placed(Control parent)
    {
        var stack = new ToastColumn();
        parent.AddChild(Ui.Place(stack, 0.5f, 0, -300, 150, 300, 400));
        return stack;
    }

    public void Push(string kind, string text, string where)
    {
        var toast = new PanelContainer();
        toast.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            FillA = UiPalette.Abyss with { A = 0.9f }, FillB = UiPalette.Abyss with { A = 0.5f }, Horizontal = true,
            Ragged = 1.4f, Seed = 67, Marker = UiPalette.Cinnabar.Lightened(0.1f), MarkerWidth = 4,
        }.Margins(22, 10));
        toast.AddChild(Ui.Row(UiPalette.SpaceM, Ui.Text(kind, UiTheme.GiltLabel, 18), Ui.Text(text, UiTheme.DarkLabel, 22),
            Ui.Spacer(), Ui.Text(where, UiTheme.DarkMutedLabel, 16)));
        AddChild(toast);
        Motion.Enter(toast, 0, Motion.Normal, rise: -12);
        while (GetChildCount() > 3)
        {
            var old = GetChild(0);
            RemoveChild(old);
            old.QueueFree();
        }
    }
}
