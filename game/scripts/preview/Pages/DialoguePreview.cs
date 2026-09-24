using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Scenery;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 人物对话展示页，版式见 docs/art/UI_DESIGN.md 第 5.4 节。台词读自
/// game/dialogue/arc01/chapter01.md（未锁稿样例）。Enter / 空格 / 点击推进，数字键选择，
/// L 打开对话记录，H 隐藏界面。截图参数 <c>--tab</c>：0 台词、1 选项、2 对话记录。
/// </summary>
public partial class DialoguePreview : Control
{
    private const float CharsPerSecond = 38;

    /// <summary>说话人 → 立绘；没有立绘的说话人不显示人物，只切换姓名牌。</summary>
    private static readonly Dictionary<string, (string Role, string? Portrait)> Speakers = new()
    {
        ["陆青禾"] = ("芦湾渡工学徒", "res://assets/portraits/lu_qinghe_v1.png"),
        ["主角"] = ("穿越者", null),
    };

    private readonly List<string> _history = [];
    private IReadOnlyList<SampleLine> _lines = [];
    private int _index = -1;
    private Tween? _typing;

    private Control _ui = null!;
    private TextureRect _portrait = null!;
    private PanelContainer _plate = null!;
    private Label _name = null!;
    private Label _role = null!;
    private RichTextLabel _text = null!;
    private Label _lineId = null!;
    private Control _next = null!;
    private VBoxContainer _choices = null!;
    private Control? _log;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _lines = DialogueSamples.Load(DialogueSamples.Chapter01);

        AddChild(new Backdrop());
        _portrait = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(Ui.Place(_portrait, 0, 0, 200, 80, 1100, 1395));

        _ui = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _ui.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_ui);
        _ui.AddChild(BuildSceneTag());
        _ui.AddChild(BuildToolbar());
        _ui.AddChild(BuildBox());
        _ui.AddChild(BuildChoices());

        var target = DevCapture.Tab switch
        {
            1 or 2 => _lines.ToList().FindIndex(l => l.Choices.Count > 0),
            _ => 1,
        };
        for (var i = 0; i <= Math.Max(0, target); i++)
        {
            Advance();
            Finish();
        }

        if (DevCapture.Tab == 2)
        {
            OpenLog();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_log is not null)
        {
            if (@event.IsActionPressed("ui_cancel") || @event is InputEventKey { Pressed: true, Keycode: Key.L })
            {
                CloseLog();
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.L }:
                OpenLog();
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.H }:
                _ui.Visible = !_ui.Visible;
                break;
            case InputEventKey { Pressed: true, Echo: false } key when key.Keycode is >= Key.Key1 and <= Key.Key9:
                Choose((int)(key.Keycode - Key.Key1));
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter or Key.Space }:
                if (!_ui.Visible)
                {
                    _ui.Visible = true;
                }
                else
                {
                    Step();
                }

                break;
            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

    /// <summary>正在逐字显示时先显示完整句；否则进入下一句（有选项时等待选择）。</summary>
    private void Step()
    {
        if (_typing is not null && _typing.IsRunning())
        {
            Finish();
            return;
        }

        if (_index >= 0 && _lines[_index].Choices.Count > 0)
        {
            return;
        }

        if (_index + 1 < _lines.Count)
        {
            Advance();
        }
    }

    private void Advance()
    {
        _index++;
        var line = _lines[_index];
        _history.Add($"{line.Speaker}|{line.Text}");

        var narration = line.Speaker == "旁白";
        _plate.Visible = !narration;
        _name.Text = line.Speaker;
        _role.Text = Speakers.TryGetValue(line.Speaker, out var s) ? s.Role : "";
        _text.Text = line.Text;
        _text.AddThemeColorOverride("default_color", narration ? UiPalette.TextOnDarkMuted : UiPalette.TextOnDark);
        _lineId.Text = $"{line.LineId}　·　未锁稿样例";
        _next.Visible = false;
        _choices.Visible = false;
        UpdatePortrait(line.Speaker);

        _text.VisibleRatio = 0;
        _typing?.Kill();
        _typing = CreateTween();
        _typing.TweenProperty(_text, "visible_ratio", 1f, Math.Max(0.2f, line.Text.Length / CharsPerSecond));
        _typing.TweenCallback(Callable.From(Finish));
    }

    private void Finish()
    {
        _typing?.Kill();
        _text.VisibleRatio = 1;
        var line = _lines[_index];
        _next.Visible = line.Choices.Count == 0 && _index + 1 < _lines.Count;
        if (line.Choices.Count > 0 && !_choices.Visible)
        {
            ShowChoices(line.Choices);
        }
    }

    /// <summary>说话人有立绘时亮起；旁白与主角说话时保留上一位人物但压暗，表示其不在说话。</summary>
    private void UpdatePortrait(string speaker)
    {
        if (Speakers.TryGetValue(speaker, out var s) && s.Portrait is { } path)
        {
            _portrait.Texture = GD.Load<Texture2D>(path);
            Tint(_portrait, Colors.White);
        }
        else if (_portrait.Texture is not null)
        {
            Tint(_portrait, new Color(0.62f, 0.7f, 0.72f));
        }
    }

    private static void Tint(CanvasItem item, Color color)
    {
        if (!Motion.Enabled)
        {
            item.Modulate = color;
            return;
        }

        item.CreateTween().TweenProperty(item, "modulate", color, Motion.Quick);
    }

    private void ShowChoices(List<SampleChoice> choices)
    {
        Ui.ClearChildren(_choices);
        var title = Ui.Panel(UiTheme.GlassPanel, Ui.Text("◆　如何回应", UiTheme.GiltLabel, 22));
        title.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _choices.AddChild(title);
        for (var i = 0; i < choices.Count; i++)
        {
            var index = i;
            var button = Ui.Button($"{i + 1}　{choices[i].Text}", UiTheme.ChoiceButton, () => Choose(index));
            button.Alignment = HorizontalAlignment.Left;
            button.CustomMinimumSize = new Vector2(0, 68);
            button.AddThemeFontSizeOverride("font_size", 26);
            button.MouseEntered += button.GrabFocus;
            _choices.AddChild(button);
        }

        _choices.Visible = true;
        Motion.Stagger(_choices.GetChildren().OfType<Control>(), 0, 0.06f, rise: 0, fromX: 30);
        _choices.GetChild<Button>(1).GrabFocus();
    }

    private void Choose(int index)
    {
        var line = _index >= 0 ? _lines[_index] : null;
        if (line is null || index >= line.Choices.Count || !_choices.Visible)
        {
            return;
        }

        _history.Add($"选择|{line.Choices[index].Text}");
        _choices.Visible = false;
        if (_index + 1 < _lines.Count)
        {
            Advance();
        }
    }

    // ── 版面 ─────────────────────────────────────────────

    private static Control BuildSceneTag()
    {
        var tag = Ui.Panel(UiTheme.GlassPanel, Ui.Column(2,
            Ui.Text("芦湾　河滩", UiTheme.DarkLabel, 22),
            Ui.Text("场景美术待制作：以程序化山水代替", UiTheme.DarkMutedLabel, 16)));
        return Ui.Place(tag, 0, 0, 48, 40, 460, 120);
    }

    private Control BuildToolbar()
    {
        var log = Ui.Button("记录", UiTheme.DarkButton, OpenLog);
        log.FocusMode = FocusModeEnum.None;
        var hide = Ui.Button("隐藏", UiTheme.DarkButton, () => _ui.Visible = false);
        hide.FocusMode = FocusModeEnum.None;
        var auto = Ui.Button("自动", UiTheme.DarkButton, disabled: true, tooltip: "M0 不含自动播放");
        var skip = Ui.Button("快进", UiTheme.DarkButton, disabled: true, tooltip: "M0 不含已读快进");
        var bar = Ui.Row(UiPalette.SpaceS, auto, skip, log, hide);
        foreach (var b in bar.GetChildren().OfType<Button>())
        {
            b.CustomMinimumSize = new Vector2(96, 48);
            b.AddThemeFontSizeOverride("font_size", 20);
        }

        return Ui.Place(bar, 1, 0, -470, 40, -48, 88);
    }

    private Control BuildBox()
    {
        var root = new Control
        {
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 0, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetTop = -330,
        };

        // 底部渐暗，托住对话框。
        var shade = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Width = 4, Height = 64, FillFrom = Vector2.Zero, FillTo = new Vector2(0, 1),
                Gradient = new Gradient { Colors = [UiPalette.Abyss with { A = 0 }, UiPalette.Abyss with { A = 0.7f }] },
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        shade.OffsetTop = -120;
        root.AddChild(shade);

        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            FillA = UiPalette.Abyss with { A = 0.88f }, FillB = UiPalette.PanelDark with { A = 0.96f },
            Chamfer = 14, Border = UiPalette.Trim with { A = 0.6f }, BorderWidth = 1.5f,
            Inner = UiPalette.Trim with { A = 0.18f }, InnerInset = 8,
            Corners = CornerStyle.Hook, CornerSize = 28, CornerWidth = 2,
            Sheen = UiPalette.Gilt with { A = 0.55f }, SheenAtTop = true, SheenInset = 0.3f,
        }.Margins(64, 44));
        box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        box.OffsetLeft = 140;
        box.OffsetRight = -140;
        box.OffsetTop = 24;
        box.OffsetBottom = -40;
        root.AddChild(box);

        _text = new RichTextLabel
        {
            FitContent = true, ScrollActive = false, BbcodeEnabled = false,
            SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore,
        };
        _text.AddThemeFontSizeOverride("normal_font_size", 30);
        _text.AddThemeConstantOverride("line_separation", 12);

        _lineId = Ui.Text("", UiTheme.DarkMutedLabel, 15);
        _lineId.Modulate = new Color(1, 1, 1, 0.7f);
        _next = Ui.Text("◆", UiTheme.GiltLabel, 22);
        Motion.Pulse(_next, 0.2f, 1.0f);
        box.AddChild(Ui.Column(UiPalette.SpaceS, _text, Ui.Row(UiPalette.SpaceM, _lineId, Ui.Spacer(), _next)));

        // 姓名牌：压在对话框左上沿，石青底、金泥边，下挂身份。
        _name = Ui.Text("", UiTheme.DarkTitleLabel, 34);
        _role = Ui.Text("", UiTheme.DarkMutedLabel, 18);
        _role.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        _plate = new PanelContainer();
        _plate.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            FillA = UiPalette.Accent.Lightened(0.05f), FillB = UiPalette.Accent.Darkened(0.25f), Horizontal = true,
            Chamfer = 8, Border = UiPalette.Gilt, BorderWidth = 1.5f,
            Inner = UiPalette.Surface with { A = 0.25f }, InnerInset = 4,
            Shadow = UiPalette.Abyss with { A = 0.4f }, ShadowSize = 6, ShadowOffset = new Vector2(0, 3),
        }.Margins(30, 8));
        _plate.AddChild(Ui.Row(UiPalette.SpaceM, _name, _role));
        Ui.Place(_plate, 0, 0, 196, -12, 196, 48);
        _plate.GrowHorizontal = GrowDirection.End;
        root.AddChild(_plate);

        var hints = Ui.KeyHints(true, ("Enter", "继续"), ("1–3", "选择"), ("L", "记录"), ("H", "隐藏"), ("Esc", "返回标题"));
        hints.Modulate = new Color(1, 1, 1, 0.85f);
        root.AddChild(Ui.Place(hints, 1, 1, -900, -38, -150, -6));
        hints.Alignment = BoxContainer.AlignmentMode.End;
        return root;
    }

    private Control BuildChoices()
    {
        _choices = Ui.Column(UiPalette.SpaceM);
        _choices.Visible = false;
        _choices.Alignment = BoxContainer.AlignmentMode.End;
        return Ui.Place(_choices, 1, 1, -900, -700, -160, -370);
    }

    // ── 对话记录 ─────────────────────────────────────────

    private void OpenLog()
    {
        if (_log is not null)
        {
            return;
        }

        var layer = new Control();
        layer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var veil = new ColorRect { Color = UiPalette.Abyss with { A = 0.7f } };
        veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        layer.AddChild(veil);

        var list = Ui.Column(UiPalette.SpaceL);
        foreach (var entry in _history)
        {
            var parts = entry.Split('|', 2);
            if (parts[0] == "选择")
            {
                list.AddChild(Ui.Text($"▸　你选择：{parts[1]}", UiTheme.GiltLabel, 22));
                continue;
            }

            var name = Ui.MinSize(Ui.Text(parts[0] == "旁白" ? "" : parts[0], UiTheme.DarkTitleLabel, 24), 140);
            var text = Ui.Text(parts[1], parts[0] == "旁白" ? UiTheme.DarkMutedLabel : UiTheme.DarkLabel, 24, wrap: true);
            list.AddChild(Ui.Row(UiPalette.SpaceL, name, text));
        }

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(Ui.Expand(list));
        var panel = new PanelContainer { ThemeTypeVariation = UiTheme.DarkPanel };
        panel.AddChild(Ui.Column(UiPalette.SpaceL,
            Ui.Row(UiPalette.SpaceL, Ui.Text("对话记录", UiTheme.DarkTitleLabel, 38), Ui.Spacer(),
                Ui.KeyHints(true, ("L", "关闭"), ("Esc", "关闭"))),
            Ui.Rule(dark: true),
            Ui.Expand(scroll, vertical: true)));
        layer.AddChild(Ui.Place(panel, 0.5f, 0.5f, -620, -400, 620, 400));
        Motion.Enter(panel, 0, Motion.Normal, rise: 20);
        AddChild(layer);
        _log = layer;
    }

    private void CloseLog()
    {
        if (_log is { } layer)
        {
            _log = null;
            Motion.FadeOut(layer, Motion.Quick, layer.QueueFree);
        }
    }
}
