using Godot;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 江湖设置（菜单“设置”分区，标题页的“江湖设置”也用它；M3-05）：绢本页，子页签 声音 · 显示 · 文字 · 辅助 · 按键，
/// 版式沿用 M0 已验收的设置页（左说明 520 宽、右控件）。改动即时生效（拖音量就能听到），并写入 <c>user://settings.cfg</c>。
/// 正文字号改动后重建主题与整个菜单框，焦点回到字号滑杆。
/// </summary>
public sealed class SettingsPanel
{
    private static readonly string[] TabNames = ["声音", "显示", "文字", "辅助", "按键"];

    /// <summary>上次所在子页签与重建后要回到的控件（字号改动会重建整框）。</summary>
    private static int _lastTab;
    private static string? _restoreFocus;

    private readonly Action _rebuildFrame;
    private readonly MarginContainer _body = Ui.Expand(new MarginContainer(), vertical: true);
    private readonly Label _status = Ui.Text("", UiTheme.AccentLabel, 20, wrap: true);
    private readonly Dictionary<string, Control> _named = [];

    private SettingsPanel(Action rebuildFrame)
    {
        _rebuildFrame = rebuildFrame;

        // 内容底边让开绢页四角的卷云纹。
        _body.AddThemeConstantOverride("margin_bottom", UiPalette.SpaceS);
    }

    /// <param name="rebuildFrame">字号改动后重建承载的菜单框。</param>
    public static Control Build(Action rebuildFrame, int? tab = null)
    {
        var panel = new SettingsPanel(rebuildFrame);
        var strip = Ui.Row(UiPalette.SpaceS);
        var group = new ButtonGroup();
        var current = Math.Clamp(tab ?? _lastTab, 0, TabNames.Length - 1);
        for (var i = 0; i < TabNames.Length; i++)
        {
            var index = i;
            strip.AddChild(Ui.Toggle(TabNames[i], UiTheme.SubTab, group, () => panel.Show(index), i == current));
        }

        strip.AddChild(Ui.Spacer());
        var sheet = Ui.Panel(UiTheme.SheetPanel, Ui.Column(UiPalette.SpaceS, strip, Ui.Rule(), panel._body, panel._status));
        panel.Show(current);
        sheet.TreeExiting += GameSettings.Save;
        return Ui.Expand(sheet, vertical: true);
    }

    private void Show(int tab)
    {
        _lastTab = tab;
        _named.Clear();
        _status.Text = "";
        Ui.ClearChildren(_body);
        var form = tab switch
        {
            1 => Display(),
            2 => Text(),
            3 => Assist(),
            4 => Keys(),
            _ => Audio(),
        };
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        scroll.AddChild(Ui.Expand(form));
        _body.AddChild(scroll);
        if (_restoreFocus is { } name && _named.TryGetValue(name, out var target))
        {
            _restoreFocus = null;
            target.CallDeferred(Control.MethodName.GrabFocus);
        }
    }

    // ── 各子页 ───────────────────────────────────────────

    private static Control Audio()
    {
        Control Volume(string name, Func<int> get, Action<int> set, string? sample = null, string? hint = null)
        {
            var row = Slider(get(), 0, 100, 5, v => $"{v}", v =>
            {
                set(v);
                GameSettings.ApplyVolumes();
            }, out var slider);
            if (sample is not null)
            {
                // 拖音效音量时放一下样音，好听出大小。
                slider.DragEnded += _ => AppHost.Instance.Sound.Play(sample);
            }

            return Setting(name, hint, row);
        }

        return Form(
            Volume("总音量", () => GameSettings.Master, v => GameSettings.Master = v, "notify.item"),
            Volume("配乐", () => GameSettings.Music, v => GameSettings.Music = v, hint: "只在主要场景与剧情转折处响起"),
            Volume("环境声", () => GameSettings.Ambience, v => GameSettings.Ambience = v, hint: "河水、风声、街巷与客栈的声景"),
            Volume("角色配音", () => GameSettings.Voice, v => GameSettings.Voice = v, hint: "对白配音；说话时配乐与环境声自动压低"),
            Volume("音效", () => GameSettings.Sfx, v => GameSettings.Sfx = v, "battle.hit.blade", "界面、脚步、交互与战斗的声音"));
    }

    private static Control Display()
    {
        var full = Ui.Switch(GameSettings.Fullscreen);
        full.Toggled += on => AppHost.Instance.SetFullscreen(on);
        var dev = Ui.Switch(AppHost.DevInfo);
        dev.Toggled += on => AppHost.DevInfo = on;
        return Form(
            Setting("全屏", "默认全屏，跟随显示器分辨率；窗口按屏幕可用区域的八成大小居中。Alt+Enter 也可切换。", full),
            Setting("开发信息", "台词编号、借景说明、战斗种子等制作用信息。F12 也可切换，各页下次打开时生效。", dev));
    }

    private Control Text()
    {
        var sample = Ui.Text("雨后的芦湾水位还高，渡口告示上的船牌号却对不上。先记下亲眼所见，再写猜测。", wrap: true);
        sample.AddThemeFontSizeOverride("font_size", GameSettings.TextSize);
        var dragging = false;
        var size = Slider(GameSettings.TextSize, GameSettings.TextSizeMin, GameSettings.TextSizeMax, 2, v => $"{v} 号", v =>
        {
            sample.AddThemeFontSizeOverride("font_size", v);
            if (!dragging)
            {
                ApplyTextSize(v);
            }
        }, out var slider);
        slider.DragStarted += () => dragging = true;
        slider.DragEnded += _ =>
        {
            dragging = false;
            ApplyTextSize((int)slider.Value);
        };
        _named["text_size"] = slider;

        var speeds = Ui.Row(UiPalette.SpaceS);
        var group = new ButtonGroup();
        foreach (var (speed, label) in new[] { (TextSpeed.Slow, "慢"), (TextSpeed.Normal, "中"), (TextSpeed.Fast, "快"), (TextSpeed.Instant, "立即") })
        {
            var s = speed;
            var b = Ui.Toggle(label, UiTheme.ChipButton, group, () =>
            {
                GameSettings.TextSpeed = s;
                GameSettings.Save();
            }, GameSettings.TextSpeed == speed);
            b.CustomMinimumSize = new Vector2(84, 44);
            speeds.AddChild(b);
        }

        return Form(
            Setting("正文字号", $"拖动后下方预览立即变化，松手后整个界面换上新字号；默认 {FontScale.BaseBody} 号。页名、印章等标题字不随之缩放。", size),
            Ui.Panel(UiTheme.InsetPanel, Ui.Column(UiPalette.SpaceS, Ui.Text("文字预览", UiTheme.MutedLabel), sample)),
            Setting("文字速度", "对话逐字显示的快慢；“立即”关闭逐字显示。", speeds));
    }

    private void ApplyTextSize(int size)
    {
        if (size == GameSettings.TextSize)
        {
            return;
        }

        GameSettings.TextSize = size;
        GameSettings.Save();
        if (GameSettings.ApplyText())
        {
            _restoreFocus = "text_size";
            AppHost.Instance.RebuildTheme();
            _rebuildFrame();
        }
    }

    private static Control Assist()
    {
        var reduce = Ui.Switch(GameSettings.ReduceMotion);
        reduce.Toggled += on =>
        {
            GameSettings.ReduceMotion = on;
            GameSettings.ApplyText();
            GameSettings.Save();
        };
        var shake = Ui.Switch(GameSettings.HitShake);
        shake.Toggled += on =>
        {
            GameSettings.HitShake = on;
            GameSettings.Save();
        };
        var speeds = Ui.Row(UiPalette.SpaceS);
        var group = new ButtonGroup();
        foreach (var (speed, label) in new[] { (1, "1 倍"), (2, "2 倍") })
        {
            var s = speed;
            var b = Ui.Toggle(label, UiTheme.ChipButton, group, () =>
            {
                GameSettings.BattleSpeed = s;
                GameSettings.Save();
            }, GameSettings.BattleSpeed == speed);
            b.CustomMinimumSize = new Vector2(100, 44);
            speeds.AddChild(b);
        }

        return Form(
            Setting("减少动效", "页面与面板直接出现，不做淡入上浮；提示不再呼吸闪动。战斗演出另用倍速与跳过。", reduce),
            Setting("受击抖动", "关闭后受击只留闪白与音效。", shake),
            Setting("战斗速度", $"开战时的演出倍速；战斗中按 {KeyBindings.Label("battle_speed")} 随时切换，倍速与跳过都不改变结果。", speeds));
    }

    /// <summary>按键：左栏探索，右栏菜单、对话、战斗；每项一行“名称 · 键 · 默认注记”，紧凑排布，一屏放下大半。</summary>
    private Control Keys()
    {
        Control Group(string group)
        {
            var column = Ui.Column(UiPalette.SpaceS, Ui.Section(group));
            foreach (var action in KeyBindings.Actions.Where(a => a.Group == group))
            {
                var button = new KeyCaptureButton(action, (ok, message) =>
                {
                    _status.Text = message ?? "";
                    if (ok)
                    {
                        GameSettings.Save();
                        Refresh();
                    }
                });
                _named["key:" + action.Id] = button;
                var note = Ui.Text(action.Default == KeyBindings.Of(action.Id) ? "" : $"默认 {KeyBindings.KeyName(action.Default)}", UiTheme.MutedLabel, 18);
                note.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                var name = Ui.MinSize(Ui.Text(action.Name), 220);
                name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                column.AddChild(Ui.Row(UiPalette.SpaceL, name, button, note));
            }

            return column;
        }

        var right = Ui.Column(UiPalette.SpaceL, Group(KeyBindings.Menu), Group(KeyBindings.Dialogue), Group(KeyBindings.Battle));
        var reset = Ui.Button("全部恢复默认", null, () =>
        {
            KeyBindings.ResetAll();
            GameSettings.Save();
            _status.Text = "按键已全部恢复默认。";
            _restoreFocus = "reset";
            Refresh();
        }, disabled: KeyBindings.IsDefault);
        reset.CustomMinimumSize = new Vector2(240, 52);
        _named["reset"] = reset;
        var left = Ui.Column(UiPalette.SpaceL, Group(KeyBindings.Explore),
            Ui.Section("固定键"),
            Ui.Text("Esc 取消与菜单；Enter、空格确认与继续对话；方向键行走与移动选择；Tab 切换目标；数字键选对话选项与招式；PgUp / PgDn 切换页签；F12 开发信息；Alt+Enter 全屏。", UiTheme.MutedLabel, 18, wrap: true),
            Ui.Row(UiPalette.SpaceM, reset));
        return Ui.Column(UiPalette.SpaceM,
            Ui.Text("选中一项按 Enter 或点击，再按想用的键；Esc 取消。同一场合里撞上别的操作时两者互换，不同场合可以共用一键。", UiTheme.MutedLabel, 20, wrap: true),
            Ui.Row(UiPalette.SpaceXxl, Ui.Expand(left), Ui.Expand(right)));

        // 改键后重画本页（键名、“默认 X”注记与恢复按钮），焦点回到刚改的那一项。
        void Refresh()
        {
            var focused = _named.FirstOrDefault(kv => kv.Value.HasFocus()).Key;
            _restoreFocus ??= focused;
            var message = _status.Text;
            Show(4);
            _status.Text = message;
        }
    }

    // ── 版式 ─────────────────────────────────────────────

    private static Control Form(params Control[] rows)
    {
        var column = Ui.Column(UiPalette.SpaceL);
        foreach (var row in rows)
        {
            column.AddChild(row);
        }

        return column;
    }

    private static Control Setting(string name, string? hint, Control control)
    {
        var label = Ui.Column(4, Ui.Text(name));
        if (hint is not null)
        {
            label.AddChild(Ui.Text(hint, UiTheme.MutedLabel, wrap: true));
        }

        Ui.MinSize(label, 520);
        label.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return Ui.Row(UiPalette.SpaceXl, label, control);
    }

    /// <summary>滑杆带右侧数值标签；返回整行容器。</summary>
    private static Control Slider(int value, int min, int max, int step, Func<int, string> format, Action<int> changed, out HSlider slider)
    {
        var bar = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value,
            CustomMinimumSize = new Vector2(360, 32),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        var readout = Ui.MinSize(Ui.Text(format(value)), 96);
        bar.ValueChanged += v =>
        {
            readout.Text = format((int)v);
            changed((int)v);
        };
        slider = bar;
        return Ui.Row(UiPalette.SpaceM, bar, readout);
    }
}

/// <summary>
/// 改键按钮：显示当前键；按下后等待下一个按键（Esc 取消），交给 <see cref="KeyBindings.Rebind"/>。
/// 等待期间按键先到这里（取得焦点的控件先于方向键导航与各页快捷键收到按键），一律吃掉，不触发别的操作。
/// </summary>
public partial class KeyCaptureButton : Button
{
    private readonly KeyAction _action;
    private readonly Action<bool, string?> _done;
    private bool _waiting;

    public KeyCaptureButton(KeyAction action, Action<bool, string?> done)
    {
        _action = action;
        _done = done;
        FocusMode = FocusModeEnum.All;
        CustomMinimumSize = new Vector2(160, 40);
        Text = KeyBindings.Label(action.Id);
        Pressed += () =>
        {
            _waiting = true;
            Text = "按下新键……";
        };
        FocusExited += Cancel;
    }

    private void Cancel()
    {
        if (_waiting)
        {
            _waiting = false;
            Text = KeyBindings.Label(_action.Id);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!_waiting)
        {
            return;
        }

        if (@event is InputEventMouseButton { Pressed: true })
        {
            Cancel();
            AcceptEvent();
            return;
        }

        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            if (@event is InputEventKey)
            {
                AcceptEvent();
            }

            return;
        }

        AcceptEvent();
        if (key.Keycode == Key.Escape)
        {
            Cancel();
            _done(false, "已取消改键。");
            return;
        }

        _waiting = false;
        var (ok, message, _) = KeyBindings.Rebind(_action.Id, key.Keycode);
        Text = KeyBindings.Label(_action.Id);
        _done(ok, ok ? message ?? $"“{_action.Name}”改为 {KeyBindings.Label(_action.Id)}。" : message);
    }
}
