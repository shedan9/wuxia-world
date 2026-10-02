using Godot;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 江湖设置（标题页与游戏菜单共用的暗色面板）：五路音量、全屏、对话文字速度与开发信息开关。
/// 改动即时生效（拖音量就能听到），关闭时写入 <c>user://settings.cfg</c>。
/// </summary>
public static class SettingsPanel
{
    public static Control Build(Action close)
    {
        var body = Ui.Column(UiPalette.SpaceL);
        body.AddChild(Ui.Row(UiPalette.SpaceL, Ui.Seal("设置"), Ui.Column(4,
            Ui.Text("江湖设置", UiTheme.DarkTitleLabel, 40),
            Ui.Text("改动立即生效，关闭时保存", UiTheme.DarkMutedLabel, 18))));
        body.AddChild(Ui.Rule(dark: true));

        var form = Ui.Column(UiPalette.SpaceM);
        form.AddChild(Ui.Section("声音", dark: true));
        HSlider? first = null;
        void Volume(string name, Func<int> get, Action<int> set, string? sample = null)
        {
            var slider = Slider(get(), v =>
            {
                set(v);
                GameSettings.ApplyVolumes();
            }, out var row);
            if (sample is not null)
            {
                // 拖音效音量时放一下样音，好听出大小。
                slider.DragEnded += _ => AppHost.Instance.Sound.Play(sample);
            }

            first ??= slider;
            form.AddChild(Row(name, row));
        }

        Volume("总音量", () => GameSettings.Master, v => GameSettings.Master = v, "notify.item");
        Volume("配乐", () => GameSettings.Music, v => GameSettings.Music = v);
        Volume("环境声", () => GameSettings.Ambience, v => GameSettings.Ambience = v);
        Volume("角色配音", () => GameSettings.Voice, v => GameSettings.Voice = v);
        Volume("音效", () => GameSettings.Sfx, v => GameSettings.Sfx = v, "battle.hit.blade");

        form.AddChild(Ui.Section("显示与文字", dark: true));
        var full = Ui.Switch(GameSettings.Fullscreen);
        full.Toggled += on => AppHost.Instance.SetFullscreen(on);
        form.AddChild(Row("全屏", full, "Alt+Enter 也可切换"));

        var speeds = Ui.Row(UiPalette.SpaceS);
        var group = new ButtonGroup();
        foreach (var (speed, label) in new[] { (TextSpeed.Slow, "慢"), (TextSpeed.Normal, "中"), (TextSpeed.Fast, "快"), (TextSpeed.Instant, "立即") })
        {
            var s = speed;
            var b = Ui.Toggle(label, UiTheme.ChipButton, group, () => GameSettings.TextSpeed = s, GameSettings.TextSpeed == speed);
            b.CustomMinimumSize = new Vector2(84, 44);
            speeds.AddChild(b);
        }

        form.AddChild(Row("文字速度", speeds, "对话逐字显示的快慢"));
        var dev = Ui.Switch(AppHost.DevInfo);
        dev.Toggled += on => AppHost.DevInfo = on;
        form.AddChild(Row("开发信息", dev, "台词编号、借景说明等，F12 也可切换"));

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(Ui.Expand(form));
        body.AddChild(Ui.Expand(scroll, vertical: true));
        body.AddChild(Ui.Rule(dark: true));
        var done = Ui.Button("关闭", UiTheme.PrimaryButton, () =>
        {
            GameSettings.Save();
            close();
        });
        done.CustomMinimumSize = new Vector2(200, 56);
        body.AddChild(Ui.Row(UiPalette.SpaceM, Ui.KeyHints(true, ("←→", "调整"), ("Esc", "关闭")), Ui.Spacer(), done));
        first?.CallDeferred(Control.MethodName.GrabFocus);
        body.TreeExiting += GameSettings.Save;
        return body;
    }

    private static Control Row(string name, Control control, string? hint = null)
    {
        var label = Ui.MinSize(Ui.Column(2, Ui.Text(name, UiTheme.DarkLabel, 22)), 240);
        if (hint is not null)
        {
            label.AddChild(Ui.Text(hint, UiTheme.DarkMutedLabel, 15));
        }

        label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return Ui.Row(UiPalette.SpaceL, label, control);
    }

    private static HSlider Slider(int value, Action<int> changed, out Control row)
    {
        var bar = new HSlider
        {
            MinValue = 0,
            MaxValue = 100,
            Step = 5,
            Value = value,
            CustomMinimumSize = new Vector2(280, 32),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        var readout = Ui.MinSize(Ui.Text($"{value}", UiTheme.DarkLabel, 22), 64);
        bar.ValueChanged += v =>
        {
            readout.Text = $"{(int)v}";
            changed((int)v);
        };
        row = Ui.Row(UiPalette.SpaceM, bar, readout);
        return bar;
    }
}
