using Godot;
using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 游戏内对话层（版式同 docs/art/UI_DESIGN.md 第 5.4 节与 M0 已验收的对话页）：叠在探索布景上，
/// 逐节点显示 <see cref="DialogueRunner"/> 停下的台词、演出提示与选项。规则结算全在会话副本上，
/// 走到结束后交回探索页提交；中途不提交任何东西。
/// 心里话（<c>inner</c>）不挂姓名牌、以淡色显示；演出提示的制作说明玩家看不到——特写与标题卡显示画面文字，
/// 场景与动作镜头尚未制作，顶部短暂标出“演出待制作”后自动继续。
/// Enter / 空格 / 点击推进，数字键选择，L 对话记录，H 隐藏界面。
/// </summary>
public partial class DialogueOverlay : Control
{
    private const float CharsPerSecond = 38;
    private const float StageHold = 0.9f;

    /// <summary>有立绘的人物；其余只切换姓名牌。</summary>
    private static readonly Dictionary<string, string> Portraits = new()
    {
        ["char.lu_qinghe"] = "res://assets/portraits/lu_qinghe_v1.png",
    };

    private readonly PlaySession _play;
    private readonly DialogueSession _session;
    private readonly Action<DialogueSession> _ended;
    private Tween? _typing;
    private double _stageTimer = -1;
    private bool _done;

    private Control _ui = null!;
    private Control _box = null!;
    private TextureRect _portrait = null!;
    private PanelContainer _plate = null!;
    private Label _name = null!;
    private RichTextLabel _text = null!;
    private Label _lineId = null!;
    private Control _next = null!;
    private VBoxContainer _choices = null!;
    private Control _card = null!;
    private Label _cardText = null!;
    private Label _cardKind = null!;
    private Control _cue = null!;
    private Label _cueText = null!;
    private Control? _log;

    public DialogueOverlay(PlaySession play, DialogueSession session, Action<DialogueSession> ended)
    {
        _play = play;
        _session = session;
        _ended = ended;
    }

    private DialogueRunner Runner => _session.Runner;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // 不拦鼠标：点击落到未处理输入里推进台词；选项按钮照常接收点击。
        MouseFilter = MouseFilterEnum.Ignore;
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
        _ui.AddChild(BuildToolbar());
        _box = BuildBox();
        _ui.AddChild(_box);
        _ui.AddChild(BuildChoices());
        _ui.AddChild(BuildCard());
        _ui.AddChild(BuildCue());
        Show(first: true);
    }

    public override void _Process(double delta)
    {
        if (DevCapture.Autoplay > 0 && !_done && _log is null)
        {
            if (Runner.AwaitingChoice && DevCapture.Holding("choice"))
            {
                // 停在选项上截图。
                FinishTyping();
                _done = true;
                DevCapture.FinishAutoplay(GetTree(), 0);
                return;
            }

            // 自动走查：读完即进，选项取第一个可选项。
            if (Runner.AwaitingChoice)
            {
                Choose(0);
            }
            else
            {
                FinishTyping();
                Advance();
            }

            return;
        }

        if (_stageTimer < 0)
        {
            return;
        }

        _stageTimer -= delta;
        if (_stageTimer < 0)
        {
            Advance();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_done)
        {
            return;
        }

        if (_log is not null)
        {
            if (@event.IsActionPressed("ui_cancel") || @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.L })
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
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }:
                // 对话中不开菜单、不回标题：吞掉取消键，免得半截对话被丢弃。
                break;
            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

    /// <summary>逐字显示时先显示完整句；否则继续（停在选项上时等待选择）。</summary>
    private void Step()
    {
        if (_typing is not null && _typing.IsRunning())
        {
            FinishTyping();
            return;
        }

        if (Runner.Ended || Runner.AwaitingChoice)
        {
            return;
        }

        Advance();
    }

    private void Advance()
    {
        _stageTimer = -1;
        Runner.Continue();
        Show(first: false);
    }

    private void Choose(int index)
    {
        if (!Runner.AwaitingChoice || !_choices.Visible)
        {
            return;
        }

        var view = Runner.Choices.Where(c => c.Enabled).ElementAtOrDefault(index);
        if (view is null)
        {
            return;
        }

        _play.History.Add(("选择", view.Option.Text));
        _choices.Visible = false;
        Runner.Choose(view.Index);
        Show(first: false);
    }

    /// <summary>显示运行器当前停下的节点；结束则交回探索页。</summary>
    private void Show(bool first)
    {
        _choices.Visible = false;
        _card.Visible = false;
        if (Runner.Current is not { } node)
        {
            _done = true;
            _ended(_session);
            return;
        }

        switch (node.Type)
        {
            case DialogueNodeType.Stage:
                ShowStage(node);
                break;
            case DialogueNodeType.Choice:
                _box.Visible = true;
                ShowChoices();
                break;
            default:
                ShowLine(node);
                break;
        }

        if (first)
        {
            Motion.Enter(_box, 0, Motion.Normal, rise: 20);
        }
    }

    private void ShowLine(DialogueNode node)
    {
        _box.Visible = true;
        _cue.Visible = false;
        var speaker = node.Speaker ?? "";
        var name = _play.Name(speaker);
        var text = node.Text ?? "";
        _play.History.Add((node.Inner ? $"{name}（心里）" : name, text));

        _plate.Visible = !node.Inner;
        _name.Text = name;
        _text.Text = text;
        _text.AddThemeColorOverride("default_color", node.Inner ? UiPalette.TextOnDarkMuted : UiPalette.TextOnDark);
        _lineId.Text = node.Inner ? $"{node.LineId}　·　心里话，不配音　·　未锁稿" : $"{node.LineId}　·　未锁稿";
        _next.Visible = false;
        UpdatePortrait(speaker, node.Inner);

        _text.VisibleRatio = 0;
        _typing?.Kill();
        _typing = CreateTween();
        _typing.TweenProperty(_text, "visible_ratio", 1f, Math.Max(0.2f, text.Length / CharsPerSecond));
        _typing.TweenCallback(Callable.From(FinishTyping));
    }

    private void FinishTyping()
    {
        _typing?.Kill();
        _text.VisibleRatio = 1;
        _next.Visible = true;
    }

    /// <summary>
    /// 演出提示：标题卡与带文字的特写停下等玩家读完；其余（场景、动作、无字特写）是待制作的镜头，
    /// 只在顶部短暂标注后自动继续，制作说明不显示。
    /// </summary>
    private void ShowStage(DialogueNode node)
    {
        _typing?.Kill();
        if (node.Caption is { Length: > 0 } caption && node.Kind is StageKind.Title or StageKind.Closeup)
        {
            _box.Visible = node.Kind != StageKind.Title;
            _cardKind.Text = node.Kind == StageKind.Title ? "" : "特写";
            _cardText.Text = caption;
            _cardText.AddThemeFontSizeOverride("font_size", node.Kind == StageKind.Title ? 64 : 34);
            _card.Visible = true;
            Motion.Enter(_card, 0, Motion.Normal, rise: 12);
            _text.Text = "";
            _plate.Visible = false;
            _lineId.Text = node.CaptionId is { } id ? $"{id}　·　画面文字　·　未锁稿" : "";
            _next.Visible = true;
            _play.History.Add(("画面", caption));
            return;
        }

        _cueText.Text = node.Kind switch
        {
            StageKind.Scene => "演出 · 场景镜头（待制作）",
            StageKind.Action => "演出 · 人物动作（待制作）",
            StageKind.Closeup => "演出 · 物件特写（待制作）",
            _ => "演出（待制作）",
        };
        _cue.Visible = true;
        _cue.Modulate = Colors.White;
        _stageTimer = Motion.Enabled ? StageHold : 0.01;
    }

    private void UpdatePortrait(string speaker, bool inner)
    {
        if (!inner && Portraits.TryGetValue(speaker, out var path))
        {
            _portrait.Texture = GD.Load<Texture2D>(path);
            Tint(_portrait, Colors.White);
        }
        else if (_portrait.Texture is not null)
        {
            // 主角或无立绘者说话：保留上一位人物但压暗。
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

    private void ShowChoices()
    {
        _cue.Visible = false;
        _next.Visible = false;
        Ui.ClearChildren(_choices);
        var title = Ui.Panel(UiTheme.GlassPanel, Ui.Text("◆　如何回应", UiTheme.GiltLabel, 22));
        title.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _choices.AddChild(title);
        var number = 0;
        Button? first = null;
        foreach (var view in Runner.Choices)
        {
            var enabled = view.Enabled;
            var label = enabled ? $"{++number}　{view.Option.Text}" : $"　　{view.Option.Text}";
            var hint = view.Option.LockedHint is { } key ? _play.Text(key) ?? key : null;
            var index = number - 1;
            var button = Ui.Button(label, UiTheme.ChoiceButton, enabled ? () => Choose(index) : null, disabled: !enabled, tooltip: hint);
            button.Alignment = HorizontalAlignment.Left;
            button.CustomMinimumSize = new Vector2(0, 68);
            button.AddThemeFontSizeOverride("font_size", 26);
            if (enabled)
            {
                button.MouseEntered += button.GrabFocus;
                first ??= button;
            }

            _choices.AddChild(button);
            if (!enabled && hint is not null)
            {
                _choices.AddChild(Ui.Text("　　" + hint, UiTheme.DarkMutedLabel, 18));
            }
        }

        _choices.Visible = true;
        Motion.Stagger(_choices.GetChildren().OfType<Control>(), 0, 0.06f, rise: 0, fromX: 30);
        first?.GrabFocus();
    }

    // ── 版面 ─────────────────────────────────────────────

    private Control BuildToolbar()
    {
        var log = Ui.Button("记录", UiTheme.DarkButton, OpenLog);
        log.FocusMode = FocusModeEnum.None;
        var hide = Ui.Button("隐藏", UiTheme.DarkButton, () => _ui.Visible = false);
        hide.FocusMode = FocusModeEnum.None;
        var bar = Ui.Row(UiPalette.SpaceS, log, hide);
        foreach (var b in bar.GetChildren().OfType<Button>())
        {
            b.CustomMinimumSize = new Vector2(96, 48);
            b.AddThemeFontSizeOverride("font_size", 20);
        }

        bar.Alignment = BoxContainer.AlignmentMode.End;
        return Ui.Place(bar, 1, 0, -470, 40, -48, 88);
    }

    private Control BuildBox()
    {
        var root = new Control
        {
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 0, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetTop = -330,
        };

        // 底部渐暗，托住对话框，也把探索画面压到后面。
        var shade = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Width = 4, Height = 64, FillFrom = Vector2.Zero, FillTo = new Vector2(0, 1),
                Gradient = new Gradient { Colors = [UiPalette.Abyss with { A = 0 }, UiPalette.Abyss with { A = 0.78f }] },
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        shade.OffsetTop = -260;
        root.AddChild(shade);

        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            FillA = UiPalette.PanelDark with { A = 0.9f }, FillB = UiPalette.Abyss with { A = 0.96f },
            Ragged = 2.2f, Seed = 51, Grain = Colors.White with { A = 0.045f }, Wash = UiPalette.Accent with { A = 0.18f },
            Border = UiPalette.Gilt with { A = 0.7f }, BorderWidth = 1.5f, Brush = true,
            Inner = UiPalette.Gilt with { A = 0.2f }, InnerInset = 9,
            Corners = CornerStyle.Cloud, CornerSize = 56, CornerWidth = 2.2f, CornerOutset = -2,
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
        _text.AddThemeColorOverride("default_color", UiPalette.TextOnDark);

        _lineId = Ui.Text("", UiTheme.DarkMutedLabel, 15);
        _lineId.Modulate = new Color(1, 1, 1, 0.7f);
        _next = Ui.Text("◆", UiTheme.GiltLabel, 22);
        Motion.Pulse(_next, 0.2f, 1.0f);
        box.AddChild(Ui.Column(UiPalette.SpaceS, _text, Ui.Row(UiPalette.SpaceM, _lineId, Ui.Spacer(), _next)));

        // 姓名牌：压在对话框左上沿的一方朱砂印。
        _name = Ui.Text("", UiTheme.DarkTitleLabel, 34);
        _plate = new PanelContainer();
        _plate.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            FillA = UiPalette.Cinnabar.Lightened(0.05f), FillB = UiPalette.Cinnabar.Darkened(0.15f), Horizontal = true,
            Ragged = 2, Seed = 53, Grain = UiPalette.Surface with { A = 0.22f }, GrainScale = 0.5f,
            Inner = UiPalette.Surface with { A = 0.6f }, InnerInset = 5, Brush = true,
            Shadow = UiPalette.Abyss with { A = 0.4f }, ShadowSize = 6, ShadowOffset = new Vector2(0, 3),
        }.Margins(30, 8));
        _plate.AddChild(_name);
        Ui.Place(_plate, 0, 0, 196, -12, 196, 48);
        _plate.GrowHorizontal = GrowDirection.End;
        root.AddChild(_plate);

        var hints = Ui.KeyHints(true, ("Enter", "继续"), ("1–3", "选择"), ("L", "记录"), ("H", "隐藏"));
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
        return Ui.Place(_choices, 1, 1, -900, -760, -160, -370);
    }

    /// <summary>特写与标题卡：画面正中一方绢底，写物件上的文字或章节标题。</summary>
    private Control BuildCard()
    {
        _cardKind = Ui.Text("", UiTheme.GiltLabel, 18);
        _cardText = Ui.Text("", UiTheme.DisplayLabel, 34, wrap: true);
        _cardText.HorizontalAlignment = HorizontalAlignment.Center;
        var column = Ui.Column(UiPalette.SpaceM, _cardKind, _cardText);
        var panel = new PanelContainer { ThemeTypeVariation = UiTheme.SheetPanel, MouseFilter = MouseFilterEnum.Ignore };
        panel.AddChild(column);
        _card = Ui.Place(panel, 0.5f, 0.5f, -520, -300, 520, 40);
        _card.Visible = false;
        return _card;
    }

    /// <summary>顶部小签：标出此处有一段待制作的演出镜头。</summary>
    private Control BuildCue()
    {
        _cueText = Ui.Text("", UiTheme.DarkMutedLabel, 18);
        var tag = Ui.Panel(UiTheme.GlassPanel, _cueText);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.AddChild(tag);
        _cue = Ui.Place(center, 0.5f, 0, -300, 40, 300, 90);
        _cue.Visible = false;
        return _cue;
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
        foreach (var (speaker, text) in _play.History.TakeLast(200))
        {
            if (speaker == "选择")
            {
                list.AddChild(Ui.Text($"▸　你选择：{text}", UiTheme.GiltLabel, 22));
                continue;
            }

            var name = Ui.MinSize(Ui.Text(speaker, UiTheme.DarkTitleLabel, 24), 160);
            list.AddChild(Ui.Row(UiPalette.SpaceL, name, Ui.Text(text, UiTheme.DarkLabel, 24, wrap: true)));
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

        // 打开时滚到最新一句。
        scroll.CallDeferred(ScrollContainer.MethodName.EnsureControlVisible, list.GetChildCount() > 0 ? list.GetChild<Control>(list.GetChildCount() - 1) : list);
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
