using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Scenery;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 探索 HUD 展示页（界面层），版式见 docs/art/UI_DESIGN.md 第 5.3 节：左上地点与时辰、
/// 右上小地图、左侧目标追踪、左下队伍、中下交互提示、上方通知、右下快捷键。
/// 场景为程序化山水占位，三类探索布景（M0-03）完成后此 HUD 叠加到对应场景上。
/// E 触发一次“见闻已记录”通知；截图参数 <c>--tab</c>：0 常态、1 隐藏追踪只留提示。
/// </summary>
public partial class ExploreHudPreview : Control
{
    private VBoxContainer _toasts = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(new Backdrop { Leaves = 18 });
        AddChild(BuildWorld());

        AddChild(BuildPlace());
        AddChild(BuildMiniMap());
        if (DevCapture.Tab != 1)
        {
            AddChild(BuildTracker());
        }

        AddChild(BuildParty());
        AddChild(BuildPrompt());
        AddChild(BuildShortcuts());

        _toasts = Ui.Column(UiPalette.SpaceS);
        _toasts.Alignment = BoxContainer.AlignmentMode.Begin;
        AddChild(Ui.Place(_toasts, 0.5f, 0, -300, 150, 300, 400));
        Toast("见闻", "已记录：亲见的旧渡石痕", "札记 → 见闻");
        Toast("物品", "获得：旧照片残片", "任务物品");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.E })
        {
            Toast("见闻", "已记录：渡口告示上的船牌号", "札记 → 见闻");
            GetViewport().SetInputAsHandled();
        }
    }

    // ── 场景占位 ─────────────────────────────────────────

    private static Control BuildWorld()
    {
        var world = new Control { MouseFilter = MouseFilterEnum.Ignore };
        world.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // 可交互物：渡口告示（木牌占位）与其上方的金泥菱形标记。
        var board = new ColorRect { Color = UiPalette.PanelDark.Lightened(0.2f), Size = new Vector2(110, 80), Position = new Vector2(1080, 640) };
        var post = new ColorRect { Color = UiPalette.Abyss, Size = new Vector2(10, 90), Position = new Vector2(1130, 720) };
        world.AddChild(post);
        world.AddChild(board);
        var marker = Ui.Text("◆", UiTheme.GiltLabel, 30);
        marker.AddThemeColorOverride("font_outline_color", UiPalette.Abyss);
        marker.AddThemeConstantOverride("outline_size", 6);
        marker.Position = new Vector2(1122, 590);
        world.AddChild(marker);
        if (Motion.Enabled)
        {
            var bob = marker.CreateTween().SetLoops().SetTrans(Tween.TransitionType.Sine);
            bob.TweenProperty(marker, "position:y", 578f, 0.8f);
            bob.TweenProperty(marker, "position:y", 590f, 0.8f);
        }

        // 主角与同行者：探索形象按 120–180 逻辑像素的屏幕身高占位（架构文档 10.2）。
        var hero = new BattleStandee { Tone = UiPalette.Accent.Lightened(0.1f), Height = 170 };
        hero.Position = new Vector2(930, 640);
        var lu = new BattleStandee { Tone = UiPalette.Trim, Height = 160 };
        lu.Position = new Vector2(820, 610);
        world.AddChild(lu);
        world.AddChild(hero);

        var tag = Ui.Panel(UiTheme.GlassPanel, Ui.Text("探索场景与探索形象待制作（M0-03）：此页只核对 HUD", UiTheme.DarkMutedLabel, 16));
        world.AddChild(Ui.Place(tag, 0.5f, 1, -300, -150, 300, -110));
        return world;
    }

    // ── 左上：地点与时辰 ─────────────────────────────────

    private static Control BuildPlace()
    {
        var seal = Ui.Seal("芦湾");
        var name = Ui.Text("江南客栈外　渡口", UiTheme.DarkTitleLabel, 30);
        var time = Ui.Text("申时　·　雨后初晴", UiTheme.DarkMutedLabel, 18);
        var column = Ui.Column(2, name, time);
        column.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var panel = Ui.Panel(UiTheme.GlassPanel, Ui.Row(UiPalette.SpaceM, seal, column));
        Motion.Enter(panel, 0.1f, Motion.Normal, rise: -10);
        return Ui.Place(panel, 0, 0, 40, 32, 480, 180);
    }

    // ── 右上：小地图 ─────────────────────────────────────

    private static Control BuildMiniMap()
    {
        var map = new MiniMap { CustomMinimumSize = new Vector2(280, 280) };
        var frame = new PanelContainer();
        frame.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            FillA = UiPalette.Abyss with { A = 0.8f }, Chamfer = 14,
            Border = UiPalette.Trim with { A = 0.7f }, BorderWidth = 1.5f,
            Corners = CornerStyle.Hook, CornerSize = 22, CornerWidth = 2,
        }.Margins(8, 8));
        var caption = Ui.Row(UiPalette.SpaceS, Ui.Text("芦湾", UiTheme.GiltLabel, 18), Ui.Spacer(), Ui.KeyHint("M", "大地图"));
        frame.AddChild(Ui.Column(UiPalette.SpaceS, map, caption));
        return Ui.Place(frame, 1, 0, -336, 32, -40, 380);
    }

    // ── 左侧：目标追踪 ───────────────────────────────────

    private static Control BuildTracker()
    {
        var main = JournalSamples.Quests[0];
        var side = JournalSamples.Quests[1];
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

    // ── 左下：队伍 ───────────────────────────────────────

    private static Control BuildParty()
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
            bars.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(Ui.Row(UiPalette.SpaceS, Ui.Glyph(glyph, tone, 56), bars));
        }

        var panel = Ui.Panel(UiTheme.GlassPanel, row);
        return Ui.Place(panel, 0, 1, 40, -128, 520, -40);
    }

    // ── 中下：交互提示 ───────────────────────────────────

    private static Control BuildPrompt()
    {
        var prompt = new PanelContainer();
        prompt.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            FillA = UiPalette.Abyss with { A = 0.85f }, FillB = UiPalette.PanelDark with { A = 0.85f }, Horizontal = true,
            Chamfer = 8, Border = UiPalette.Gilt with { A = 0.8f }, BorderWidth = 1.5f,
            Corners = CornerStyle.Bracket, CornerSize = 10, CornerWidth = 2, CornerOutset = 4,
        }.Margins(20, 10));
        prompt.AddChild(Ui.Row(UiPalette.SpaceM, Ui.KeyHint("E", ""),
            Ui.Text("查看", UiTheme.DarkLabel, 24), Ui.Text("渡口告示", UiTheme.GiltLabel, 22)));
        Motion.Pulse(prompt, 0.75f, 2.2f);
        return Ui.Place(prompt, 0, 0, 1000, 520, 1300, 580);
    }

    // ── 右下：快捷键 ─────────────────────────────────────

    private static Control BuildShortcuts()
    {
        var hints = Ui.KeyHints(true, ("C", "人物"), ("I", "行囊"), ("J", "札记"), ("M", "地图"), ("Esc", "返回标题"));
        return Ui.Place(Ui.Panel(UiTheme.GlassPanel, hints), 1, 1, -760, -96, -40, -40);
    }

    // ── 通知 ─────────────────────────────────────────────

    private void Toast(string kind, string text, string where)
    {
        var toast = new PanelContainer();
        toast.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            FillA = UiPalette.Abyss with { A = 0.9f }, FillB = UiPalette.Abyss with { A = 0.55f }, Horizontal = true,
            Marker = UiPalette.Gilt, MarkerWidth = 3,
        }.Margins(22, 10));
        toast.AddChild(Ui.Row(UiPalette.SpaceM, Ui.Text(kind, UiTheme.GiltLabel, 18), Ui.Text(text, UiTheme.DarkLabel, 22),
            Ui.Spacer(), Ui.Text(where, UiTheme.DarkMutedLabel, 16)));
        _toasts.AddChild(toast);
        Motion.Enter(toast, 0, Motion.Normal, rise: -12);
        while (_toasts.GetChildCount() > 3)
        {
            var old = _toasts.GetChild(0);
            _toasts.RemoveChild(old);
            old.QueueFree();
        }
    }
}

/// <summary>小地图占位：水道、路径、屋舍色块，主角箭头与目标菱形。正式版由探索地图布局生成。</summary>
public partial class MiniMap : Control
{
    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        var s = Size;
        DrawRect(new Rect2(Vector2.Zero, s), UiPalette.SurfaceShade with { A = 0.9f });
        // 河道：自左上斜向右下。
        Vector2[] river =
        [
            new(0, s.Y * 0.55f), new(s.X * 0.4f, s.Y * 0.62f), new(s.X, s.Y * 0.48f),
            new(s.X, s.Y * 0.68f), new(s.X * 0.45f, s.Y * 0.82f), new(0, s.Y * 0.74f),
        ];
        DrawColoredPolygon(river, UiPalette.Trim.Lightened(0.1f));
        // 屋舍与渡口。
        foreach (var r in new[]
                 {
                     new Rect2(s.X * 0.18f, s.Y * 0.18f, s.X * 0.22f, s.Y * 0.14f),
                     new Rect2(s.X * 0.48f, s.Y * 0.14f, s.X * 0.16f, s.Y * 0.2f),
                     new Rect2(s.X * 0.70f, s.Y * 0.26f, s.X * 0.14f, s.Y * 0.12f),
                     new Rect2(s.X * 0.36f, s.Y * 0.44f, s.X * 0.1f, s.Y * 0.08f),
                 })
        {
            DrawRect(r, UiPalette.TextMuted with { A = 0.55f });
        }

        DrawPolyline([new Vector2(s.X * 0.05f, s.Y * 0.38f), new Vector2(s.X * 0.42f, s.Y * 0.40f),
            new Vector2(s.X * 0.62f, s.Y * 0.36f), new Vector2(s.X * 0.95f, s.Y * 0.42f)], UiPalette.Surface, 4, true);
        DrawLine(new Vector2(s.X * 0.42f, s.Y * 0.40f), new Vector2(s.X * 0.41f, s.Y * 0.56f), UiPalette.Surface, 3, true);

        // 目标：金泥菱形；主角：石青箭头。
        var goal = new Vector2(s.X * 0.62f, s.Y * 0.22f);
        DrawColoredPolygon([goal + new Vector2(0, -9), goal + new Vector2(9, 0), goal + new Vector2(0, 9), goal + new Vector2(-9, 0)], UiPalette.Gilt.Darkened(0.1f));
        var me = new Vector2(s.X * 0.44f, s.Y * 0.47f);
        DrawColoredPolygon([me + new Vector2(0, -12), me + new Vector2(8, 8), me + new Vector2(0, 3), me + new Vector2(-8, 8)], UiPalette.Accent);
        DrawArc(me, 18, 0, Mathf.Tau, 32, UiPalette.Accent with { A = 0.4f }, 1.5f, true);

        // 方位：右上角“北”。
        DrawString(UiFonts.Title, new Vector2(s.X - 30, 28), "北", HorizontalAlignment.Left, -1, 20, UiPalette.Text);
    }
}
