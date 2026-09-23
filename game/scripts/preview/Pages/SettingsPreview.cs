using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>暂停 / 设置展示页。选项只改变页面内的预览，不写入配置文件。</summary>
public partial class SettingsPreview : PreviewScreen
{
    private const string PreviewOnly = "M0 只展示选项与状态，不保存、不改变实际窗口与音量。";

    private static readonly string[] SubtitleSpeeds = ["", "很慢", "较慢", "适中", "较快", "很快"];

    protected override string SealText => "设置";
    protected override string Title => "暂停与设置";
    protected override string Subtitle => "游戏暂停时打开；每项都可用鼠标或键盘操作";

    protected override IReadOnlyList<(string Name, Func<Control> Build)> Tabs =>
    [
        ("暂停", BuildPause),
        ("显示", BuildDisplay),
        ("声音", BuildAudio),
        ("文字", BuildText),
        ("辅助", BuildAssist),
    ];

    private static Control BuildPause()
    {
        var menu = Ui.Column(UiPalette.SpaceM,
            Ui.MinSize(Ui.Button("继续游戏", UiTheme.PrimaryButton), 360, 64),
            Ui.MinSize(Ui.Button("保存进度", disabled: true, tooltip: "存档在第二阶段实现"), 360, 64),
            Ui.MinSize(Ui.Button("读取进度", disabled: true, tooltip: "存档在第二阶段实现"), 360, 64),
            Ui.MinSize(Ui.Button("设置"), 360, 64),
            Ui.MinSize(Ui.Button("回到标题"), 360, 64));

        var place = Ui.Column(UiPalette.SpaceS,
            Ui.Text("当前所在", UiTheme.MutedLabel),
            Ui.Text("芦湾　江南客栈", UiTheme.SectionLabel),
            Ui.Text("第一篇《众路归潮》　第一章　江南会客", UiTheme.MutedLabel),
            Ui.Rule(),
            Ui.Text("当前目标", UiTheme.MutedLabel),
            Ui.Text("听令狐冲、黄蓉与萧峰各自说明求援书的疑点。", wrap: true),
            Ui.Rule(),
            Ui.Text("游戏时长 0:42　　江湖时辰 申时", UiTheme.MutedLabel),
            Ui.Text("“保存进度”“读取进度”为禁用状态示例：M0 没有存档功能。", UiTheme.AccentLabel, UiPalette.FontSecondary, wrap: true));

        return Ui.Row(UiPalette.SpaceXxl, menu, Ui.Expand(Ui.Panel(UiTheme.InsetPanel, place)));
    }

    private static Control BuildDisplay()
    {
        var resolution = Options(1, "1600 × 900（窗口）", "1920 × 1080", "2560 × 1440", "3840 × 2160");
        var mode = Options(0, "窗口", "无边框全屏", "独占全屏");
        mode.SetItemDisabled(2, true);
        mode.SetItemTooltip(2, "目标设备验证后开放");

        return Form(
            Setting("分辨率", "界面按 1920 × 1080 逻辑画布缩放输出。", resolution),
            Setting("窗口模式", "独占全屏为禁用示例。", mode),
            Setting("垂直同步", "开启可减少画面撕裂。", Ui.Switch(true)),
            Setting("界面缩放", "只缩放界面，不缩放场景。", Slider(100, 90, 130, 5, v => $"{v:0}%")),
            Setting("帧率上限", null, Options(1, "30", "60", "120", "不限")));
    }

    private static Control BuildAudio() => Form(
        Setting("总音量", null, Slider(80, 0, 100, 1, v => $"{v:0}")),
        Setting("音乐", null, Slider(65, 0, 100, 1, v => $"{v:0}")),
        Setting("音效", null, Slider(75, 0, 100, 1, v => $"{v:0}")),
        Setting("角色语音", "主线对白在第二阶段全配音。", Slider(90, 0, 100, 1, v => $"{v:0}")),
        Setting("失去焦点时静音", null, Ui.Switch(true)));

    private static Control BuildText()
    {
        var sample = Ui.Text(
            "雨后的芦湾水位还高，渡口告示上的船牌号却对不上。先记下亲眼所见，再写猜测。",
            wrap: true);
        var size = Slider(UiPalette.FontBody, 20, 32, 2, v => $"{v:0} 号", out var sizeSlider);
        sizeSlider.ValueChanged += v => sample.AddThemeFontSizeOverride("font_size", (int)v);

        var preview = Ui.Panel(UiTheme.InsetPanel, Ui.Column(UiPalette.SpaceS,
            Ui.Text("文字预览", UiTheme.MutedLabel), sample));

        return Form(
            Setting("正文字号", "拖动后下方预览立即变化；默认 24 号。", size),
            preview,
            Setting("字幕速度", null, Slider(3, 1, 5, 1, v => SubtitleSpeeds[(int)v])),
            Setting("文本立即显示", "关闭逐字显示动画。", Ui.Switch(false)),
            Setting("对白记录保留", null, Options(1, "本章", "最近 200 句", "全部")));
    }

    private static Control BuildAssist()
    {
        var color = Options(0, "关闭", "红绿色弱", "蓝黄色弱");
        return Form(
            Setting("色觉辅助", "状态始终同时显示图标与名称。", color),
            Setting("屏幕震动", "关闭后受击仅保留闪光与音效。", Ui.Switch(true)),
            Setting("减少动效", "缩短页面切换与技能特写。", Ui.Switch(false)),
            Setting("战斗速度", "倍速与跳过动画不改变战斗结果。", Options(0, "1 倍", "2 倍", "跳过动画")),
            Setting("战斗难度", "第二阶段开放。", Options(1, "从容", "寻常", "凶险"), disabled: true));
    }

    private static Control Form(params Control[] rows)
    {
        var column = Ui.Column(UiPalette.SpaceL);
        foreach (var row in rows)
        {
            column.AddChild(row);
        }

        column.AddChild(Ui.Rule());
        column.AddChild(Ui.Text(PreviewOnly, UiTheme.MutedLabel));
        return column;
    }

    private static Control Setting(string name, string? hint, Control control, bool disabled = false)
    {
        var label = Ui.Column(4, Ui.Text(name));
        if (hint is not null)
        {
            label.AddChild(Ui.Text(hint, UiTheme.MutedLabel));
        }

        if (disabled)
        {
            label.AddChild(Ui.Text("暂不可调整", UiTheme.AccentLabel, UiPalette.FontSecondary));
            SetDisabled(control);
        }

        Ui.MinSize(label, 520);
        control.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return Ui.Row(UiPalette.SpaceXl, label, control);
    }

    private static void SetDisabled(Node node)
    {
        switch (node)
        {
            case BaseButton b:
                b.Disabled = true;
                break;
            case Godot.Slider r:
                r.Editable = false;
                break;
        }

        foreach (var child in node.GetChildren())
        {
            SetDisabled(child);
        }
    }

    private static OptionButton Options(int selected, params string[] items)
    {
        var option = new OptionButton { CustomMinimumSize = new Vector2(360, 0) };
        foreach (var item in items)
        {
            option.AddItem(item);
        }

        option.Selected = selected;
        return option;
    }

    private static Control Slider(double value, double min, double max, double step, Func<double, string> format) =>
        Slider(value, min, max, step, format, out _);

    /// <summary>滑杆带右侧数值标签；返回整行容器。</summary>
    private static Control Slider(double value, double min, double max, double step, Func<double, string> format, out HSlider slider)
    {
        var bar = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value,
            CustomMinimumSize = new Vector2(360, 32),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        var readout = Ui.MinSize(Ui.Text(format(value)), 96);
        bar.ValueChanged += v => readout.Text = format(v);
        slider = bar;
        return Ui.Row(UiPalette.SpaceM, bar, readout);
    }
}
