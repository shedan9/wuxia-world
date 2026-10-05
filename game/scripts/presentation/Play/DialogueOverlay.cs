using Godot;
using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Audio;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 游戏内对话层（版式同 docs/art/UI_DESIGN.md 第 5.4 节与 M0 已验收的对话页）：叠在探索布景上，
/// 逐节点显示 <see cref="DialogueRunner"/> 停下的台词、演出提示与选项。规则结算全在会话副本上，
/// 走到结束后交回探索页提交；中途不提交任何东西。
/// 心里话（<c>inner</c>）不挂姓名牌、以淡色显示；演出提示的制作说明玩家看不到——特写与标题卡显示画面文字，
/// 场景、动作与无字特写收起对话框、拉上电影黑边停一拍（场景镜头更长，开场的场景镜头从黑场淡入）后自动继续，点击可跳过。
/// 台词编号、锁稿与配音状态只在开发信息打开时显示（F12 / <c>--dev</c>）。
/// 配音（M2-09）：台词显示时按 <c>line_id</c> 播放，换句、选择、演出或结束时停止；R 重播当前句；
/// 心里话不播放；缺音或文字已改（音频过期）时只显示字幕，并在台词编号旁注明。
/// Enter / 空格 / 点击推进，数字键选择，R 重播，L 对话记录，H 隐藏界面。
/// </summary>
public partial class DialogueOverlay : Control
{
    private const float BarHeight = 96;

    /// <summary>主角立绘：放在右侧、水平翻转，朝向左侧的对面人物。</summary>
    private const string HeroPortraitPath = "res://assets/portraits/hero_v1.png";

    /// <summary>不在说话、正在听的人物立绘压暗的色调。</summary>
    private static readonly Color Listening = new(0.62f, 0.7f, 0.72f);

    /// <summary>对面人物立绘：<c>res://assets/portraits/&lt;人物&gt;_v1.png</c>（主角另占右侧立绘位）；没有的人物只切换姓名牌。</summary>
    private static string? PortraitOf(string speaker) =>
        speaker.StartsWith("char.", StringComparison.Ordinal) && speaker != "char.hero"
        && $"res://assets/portraits/{speaker["char.".Length..]}_v1.png" is var path && ResourceLoader.Exists(path) ? path : null;

    private readonly PlaySession _play;
    private readonly DialogueSession _session;
    private readonly Action<DialogueSession> _ended;
    private readonly ICuePlayer? _cues;
    private Tween? _typing;
    private double _stageTimer = -1;
    private double _walkKeyDelay = 0.3;

    /// <summary>自动推进的倒计时（秒）；负数为不计时。每句台词显示时重置，换句、选择与结束都会清掉，旧句的计时不会推进新句。</summary>
    private double _autoTimer = -1;
    private bool _done;

    private Control _ui = null!;
    private Control _box = null!;
    private TextureRect _portrait = null!;
    private TextureRect _hero = null!;
    private PanelContainer _plate = null!;
    private Label _name = null!;
    private RichTextLabel _text = null!;
    private Label _lineId = null!;
    private Control _next = null!;
    private VBoxContainer _choices = null!;
    private Control _card = null!;
    private PanelContainer _cardPanel = null!;
    private Label _cardText = null!;
    private Label _cardKind = null!;
    private Control _barTop = null!;
    private Control _barBottom = null!;
    private bool _barsShown;
    private ColorRect _blackout = null!;
    private Tween? _blackoutTween;
    private Control? _log;

    public DialogueOverlay(PlaySession play, DialogueSession session, Action<DialogueSession> ended, ICuePlayer? cues = null)
    {
        _play = play;
        _session = session;
        _ended = ended;
        _cues = cues;
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

        // 主角立绘贴右侧，与对面人物左右相对；第一次开口时才亮出。
        _hero = new TextureRect
        {
            Texture = ResourceLoader.Exists(HeroPortraitPath) ? GD.Load<Texture2D>(HeroPortraitPath) : null,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            FlipH = true,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = Colors.Transparent,
        };
        AddChild(Ui.Place(_hero, 1, 0, -1100, 80, -200, 1395));

        _ui = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _ui.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_ui);
        _ui.AddChild(BuildToolbar());
        _box = BuildBox();
        _ui.AddChild(_box);
        _ui.AddChild(BuildChoices());
        _ui.AddChild(BuildCard());
        BuildBars();

        // 黑场压在立绘与黑边之上、对话界面之下：标题卡浮在黑场里。
        _blackout = new ColorRect { Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _blackout.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_blackout);
        MoveChild(_blackout, _ui.GetIndex());
        Show(first: true);
    }

    public override void _Process(double delta)
    {
        if (DevCapture.Autoplay > 0 && !_done && _log is null)
        {
            if (DevCapture.HoldLine is { } line
                && (Runner.Current?.LineId == line || Runner.AwaitingChoice && Runner.Choices.Any(c => c.Option.LineId == line)))
            {
                // 停在指定台词（或含这一项的选项）上截图。
                FinishTyping();
                _done = true;
                DevCapture.FinishAutoplay(GetTree(), 0);
                return;
            }

            if (Runner.AwaitingChoice && DevCapture.Holding("choice"))
            {
                // 停在选项上截图。
                FinishTyping();
                _done = true;
                DevCapture.FinishAutoplay(GetTree(), 0);
                return;
            }

            if (DevCapture.Walk)
            {
                // 真实行走走查：注入按键推进（第一下补全逐字、第二下继续），选项按 1。
                _walkKeyDelay -= delta;
                if (_walkKeyDelay <= 0)
                {
                    _walkKeyDelay = 0.15;
                    ExplorationScreen.PressKey(Runner.AwaitingChoice && _choices.Visible ? Key.Key1 + AutoplayChoice() : Key.Enter);
                }

                return;
            }

            // 自动走查：读完即进，选项取第一个可选项（--companion 指定时讨教与同行选那位侠客）。
            if (Runner.AwaitingChoice)
            {
                Choose(AutoplayChoice());
            }
            else
            {
                FinishTyping();
                Advance();
            }

            return;
        }

        TickAutoAdvance(delta);
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
            if (@event.IsActionPressed("ui_cancel") || KeyBindings.Pressed(@event, "dialogue_log"))
            {
                CloseLog();
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        switch (@event)
        {
            // 记录、隐藏、重播三键可在设置里改（KeyBindings）；继续键（Enter、空格、左键）与数字选项固定。
            case InputEventKey when KeyBindings.Pressed(@event, "dialogue_log"):
                OpenLog();
                break;
            case InputEventKey when KeyBindings.Pressed(@event, "dialogue_hide"):
                _ui.Visible = !_ui.Visible;
                break;
            case InputEventKey when KeyBindings.Pressed(@event, "dialogue_replay"):
                AppHost.Instance.Voice.Replay();
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

    /// <summary>
    /// 自动推进（设置“文字”页）：整句显示完、配音说完（重播时等重播完）、界面未隐藏、没开记录时才倒计时；
    /// 有配音的说完后停 0.6 秒，无配音的按每秒 6 字留读完的时间（至少 1.5 秒）；标题卡与带字特写同样按字数停留。选项不自动。
    /// </summary>
    private void TickAutoAdvance(double delta)
    {
        if (!GameSettings.AutoAdvance || _autoTimer < 0 || _log is not null || !_ui.Visible
            || (_typing is not null && _typing.IsRunning()) || Runner.AwaitingChoice || Runner.Ended)
        {
            return;
        }

        if (AppHost.Instance.Voice.Playing)
        {
            _autoTimer = 0.6;
            return;
        }

        _autoTimer -= delta;
        if (_autoTimer < 0)
        {
            if (AppHost.DevInfo)
            {
                GD.Print($"[dialogue] 自动推进：{Runner.Current?.LineId ?? Runner.Current?.CaptionId}");
            }

            Advance();
        }
    }

    private void Advance()
    {
        _stageTimer = -1;
        _autoTimer = -1;
        _cues?.Finish();
        AppHost.Instance.Voice.Stop();
        Runner.Continue();
        Show(first: false);
    }

    /// <summary>自动走查要选的可选项序号：有以 <c>.choice.&lt;--companion&gt;</c> 结尾的选项就选它，否则第一个。</summary>
    private int AutoplayChoice()
    {
        if (DevCapture.Companion is not { } who)
        {
            return 0;
        }

        var index = Runner.Choices.Where(c => c.Enabled).ToList().FindIndex(c => c.Option.LineId.EndsWith(".choice." + who, StringComparison.Ordinal));
        return Math.Max(0, index);
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
        _autoTimer = -1;
        _cues?.Finish();
        AppHost.Instance.Voice.Stop();
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
            AppHost.Instance.Voice.Stop();
            _ended(_session);
            return;
        }

        switch (node.Type)
        {
            case DialogueNodeType.Stage:
                ShowStage(node, first);
                break;
            case DialogueNodeType.Choice:
                Bars(false);
                Blackout(false, instant: false);

                // 一段对话以选项开头（前面还没有台词）时不摆空对话框，选项单独浮在画面上。
                _box.Visible = _text.Text.Length > 0;
                _plate.Visible = _name.Text.Length > 0 && _box.Visible;
                ShowChoices();
                break;
            default:
                ShowLine(node);
                break;
        }

        if (first && _box.Visible)
        {
            Motion.Enter(_box, 0, Motion.Normal, rise: 20);
        }
    }

    private void ShowLine(DialogueNode node)
    {
        Bars(false);
        Blackout(false, instant: false);
        _box.Visible = true;
        var speaker = node.Speaker ?? "";
        var name = _play.Name(speaker);
        var text = node.Text ?? "";
        _play.History.Add((node.Inner ? $"{name}（心里）" : name, text));

        _plate.Visible = !node.Inner;
        _name.Text = name;
        _text.Text = text;
        _text.AddThemeColorOverride("default_color", node.Inner ? UiPalette.TextOnDarkMuted : UiPalette.TextOnDark);
        var voice = node.Inner ? "心里话，不配音" : VoiceNote(node);
        _autoTimer = AppHost.Instance.Voice.Playing ? 0.6 : Math.Max(1.5, text.Length / 6.0);
        _lineId.Text = $"{node.LineId}　·　未锁稿　·　{voice}";
        _next.Visible = false;
        UpdatePortrait(speaker, node.Inner);

        _text.VisibleRatio = 0;
        _typing?.Kill();
        var cps = GameSettings.CharsPerSecond;
        if (cps <= 0)
        {
            FinishTyping();
            return;
        }

        _typing = CreateTween();
        _typing.TweenProperty(_text, "visible_ratio", 1f, Math.Max(0.2f, text.Length / cps));
        _typing.TweenCallback(Callable.From(FinishTyping));
    }

    /// <summary>播放这句配音（自动走查时不出声），返回写在台词编号旁的说明。</summary>
    private static string VoiceNote(DialogueNode node)
    {
        var voice = AppHost.Instance.Voice;
        if (DevCapture.Autoplay > 0 && DevCapture.Output is not null)
        {
            return "自动走查不播放配音";
        }

        return voice.Play(node.LineId!, node.Text ?? "") switch
        {
            VoiceState.Playing => voice.Status == "final" ? "配音" : "试听配音",
            VoiceState.Stale => "配音过期（文字已改），只显示字幕",
            VoiceState.Broken => "配音文件损坏，只显示字幕",
            _ => "缺配音，只显示字幕",
        };
    }

    private void FinishTyping()
    {
        _typing?.Kill();
        _text.VisibleRatio = 1;
        _next.Visible = true;
    }

    /// <summary>
    /// 演出提示：标题卡与带文字的特写停下等玩家读完；场景、动作与无字特写收起对话框、拉上黑边，
    /// 有演出脚本（<see cref="CueScripts"/>）的按脚本摇镜头、让人物走位，演完自动继续，没有的停一拍；点击可跳到演出结束。
    /// 有演出的节点两侧立绘退下，让出画面。制作说明不显示。
    /// </summary>
    private void ShowStage(DialogueNode node, bool first)
    {
        _typing?.Kill();
        _box.Visible = false;
        var cue = _cues?.Play(Runner.Definition.Id, node, Runner.State);
        if (cue is not null && node.Kind != StageKind.Title)
        {
            Tint(_portrait, Colors.Transparent);
            Tint(_hero, Colors.Transparent);
        }

        if (node.Caption is { Length: > 0 } caption && node.Kind is StageKind.Title or StageKind.Closeup)
        {
            Bars(node.Kind == StageKind.Title);
            if (node.Kind == StageKind.Title)
            {
                // 章名标题卡：黑场托底，开篇直接黑场，章末从画面淡入黑场。
                Blackout(true, instant: first);
            }

            _cardKind.Text = "◇";
            _cardKind.Visible = node.Kind != StageKind.Title;
            _cardText.Text = caption;
            _cardText.AddThemeFontSizeOverride("font_size", node.Kind == StageKind.Title ? 64 : 34);
            if (node.Kind == StageKind.Title)
            {
                // 章名：黑场上的大字，不套纸卡。
                _cardPanel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
                _cardText.AddThemeColorOverride("font_color", UiPalette.Surface);
                _card.OffsetTop = -120;
                _card.OffsetBottom = 60;
            }
            else
            {
                _cardPanel.RemoveThemeStyleboxOverride("panel");
                _cardText.RemoveThemeColorOverride("font_color");

                // 特写的纸卡放在画面下部，让出镜头推近的物件（告示、船牌）。
                _card.OffsetTop = cue is null ? -300 : 130;
                _card.OffsetBottom = cue is null ? 40 : 400;
            }

            _card.Visible = true;
            Motion.Enter(_card, 0, Motion.Normal, rise: 12);
            if (node.Kind == StageKind.Title)
            {
                Tint(_portrait, Colors.Transparent);
                Tint(_hero, Colors.Transparent);
            }

            _play.History.Add(("画面", caption));

            // 自动推进时，标题卡与带字特写也按字数停留后继续（至少 2.5 秒）。
            _autoTimer = Math.Max(2.5, caption.Length / 6.0);
            return;
        }

        Bars(true);
        if (cue is not null)
        {
            // 立绘已在上面退下。
        }
        else if (node.Kind == StageKind.Scene)
        {
            // 场景镜头：人物退到一边，让出画面。
            Tint(_portrait, Colors.Transparent);
            Tint(_hero, Colors.Transparent);
        }
        else
        {
            Dim(_portrait);
            Dim(_hero);
        }

        if (first && node.Kind == StageKind.Scene)
        {
            // 一段对话以场景镜头开头（进客栈）：从黑场淡入。
            Blackout(true, instant: true);
        }

        Blackout(false, instant: false);

        var hold = node.Kind switch
        {
            StageKind.Scene => first ? 2.2 : 1.6,
            StageKind.Action => 1.1,
            _ => 0.9,
        };
        _stageTimer = cue is { } length ? length + 0.25 : Motion.Enabled ? hold : 0.01;
    }

    /// <summary>黑场淡入 / 淡出。</summary>
    private void Blackout(bool on, bool instant)
    {
        _blackoutTween?.Kill();
        if (instant || !Motion.Enabled)
        {
            _blackout.Visible = on;
            _blackout.Modulate = on ? Colors.White : Colors.Transparent;
            return;
        }

        if (!on && !_blackout.Visible)
        {
            return;
        }

        _blackout.Visible = true;
        _blackoutTween = _blackout.CreateTween();
        _blackoutTween.TweenProperty(_blackout, "modulate:a", on ? 1f : 0f, on ? 0.8f : 1.1f).SetEase(Tween.EaseType.Out);
        if (!on)
        {
            _blackoutTween.TweenCallback(Callable.From(() => _blackout.Visible = false));
        }
    }

    /// <summary>电影黑边：演出时从上下拉入，回到台词时收起。</summary>
    private void Bars(bool show)
    {
        if (_barsShown == show)
        {
            return;
        }

        _barsShown = show;
        var h = show ? BarHeight : 0;
        if (!Motion.Enabled)
        {
            _barTop.OffsetBottom = h;
            _barBottom.OffsetTop = -h;
            return;
        }

        var tween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_barTop, "offset_bottom", h, Motion.Normal);
        tween.TweenProperty(_barBottom, "offset_top", -h, Motion.Normal);
    }

    /// <summary>上下黑边：压在两侧立绘之上、对话界面之下。</summary>
    private void BuildBars()
    {
        _barTop = new ColorRect { Color = Colors.Black with { A = 0.92f }, MouseFilter = MouseFilterEnum.Ignore };
        _barTop.AnchorRight = 1;
        _barBottom = new ColorRect { Color = Colors.Black with { A = 0.92f }, MouseFilter = MouseFilterEnum.Ignore };
        _barBottom.AnchorTop = 1;
        _barBottom.AnchorBottom = 1;
        _barBottom.AnchorRight = 1;
        AddChild(_barTop);
        AddChild(_barBottom);
        MoveChild(_barTop, _hero.GetIndex() + 1);
        MoveChild(_barBottom, _hero.GetIndex() + 2);
    }

    private void UpdatePortrait(string speaker, bool inner)
    {
        var hero = inner || speaker == "char.hero";
        if (hero && _hero.Texture is not null)
        {
            // 主角说话（含心里话）：亮出右侧主角立绘，对面那位人物保留但压暗，像在听他说。
            Tint(_hero, Colors.White);
            Dim(_portrait);
            return;
        }

        // 别人说话：主角在一旁听着，立绘压暗。
        Dim(_hero);
        if (!inner && PortraitOf(speaker) is { } path)
        {
            _portrait.Texture = GD.Load<Texture2D>(path);
            Tint(_portrait, Colors.White);
        }
        else if (_portrait.Texture is null)
        {
            return;
        }
        else if (hero)
        {
            // 没有主角立绘时：保留对面那位人物但压暗。
            Dim(_portrait);
        }
        else
        {
            // 换了一位还没有立绘的人物说话：上一位的立绘退下，免得看着像她在说。
            Tint(_portrait, Colors.Transparent);
        }
    }

    /// <summary>已亮出的立绘压暗；还没出场或已退下的不动。</summary>
    private static void Dim(TextureRect portrait)
    {
        if (portrait.Texture is not null && portrait.Modulate.A > 0.01f)
        {
            Tint(portrait, Listening);
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

            // 选项长或字号放大时折行，不越出画面右缘。
            button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            button.AddThemeFontSizeOverride("font_size", FontScale.Of(26));
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
            b.AddThemeFontSizeOverride("font_size", FontScale.Of(20));
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
        _text.AddThemeFontSizeOverride("normal_font_size", FontScale.Of(30));
        _text.AddThemeConstantOverride("line_separation", 12);
        _text.AddThemeColorOverride("default_color", UiPalette.TextOnDark);

        _lineId = Ui.Text("", UiTheme.DarkMutedLabel, 15);
        _lineId.Modulate = new Color(1, 1, 1, 0.7f);
        _next = Ui.Text("◆", UiTheme.GiltLabel, 22);
        Motion.Pulse(_next, 0.2f, 1.0f);
        // 台词编号一行只在开发信息打开时显示；继续标记始终在右下。
        _lineId.Visible = AppHost.DevInfo;
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

        // 不放常驻按键提示：继续有右下的◆标记，记录、隐藏在右上有按钮，按键在设置里可查。
        // 对话框底板（PanelContainer）默认拦鼠标，点在框内会被吃掉、推进不了台词；框内没有按钮，整块放行。
        Ui.IgnoreMouse(root);
        return root;
    }

    private Control BuildChoices()
    {
        _choices = Ui.Column(UiPalette.SpaceM);
        _choices.Visible = false;
        _choices.Alignment = BoxContainer.AlignmentMode.End;
        // 宽 820：第一章最长的选项（24 字）在默认字号下连序号排一行；更长或字号放大时再折行。
        Ui.Place(_choices, 1, 1, -980, -760, -160, -370);

        // 选项多、折行或字号放大时向上长，不压到对话框上。
        _choices.GrowVertical = GrowDirection.Begin;
        return _choices;
    }

    /// <summary>特写与标题卡：画面正中一方绢底，写物件上的文字或章节标题。</summary>
    private Control BuildCard()
    {
        _cardKind = Ui.Text("", UiTheme.GiltLabel, 18);
        _cardText = Ui.Text("", UiTheme.DisplayLabel, 34, wrap: true);
        _cardText.HorizontalAlignment = HorizontalAlignment.Center;
        _cardKind.HorizontalAlignment = HorizontalAlignment.Center;
        var more = Ui.Text("◆", UiTheme.GiltLabel, 20);
        more.HorizontalAlignment = HorizontalAlignment.Center;
        Motion.Pulse(more, 0.2f, 1.0f);
        var column = Ui.Column(UiPalette.SpaceM, _cardKind, _cardText, more);
        var panel = new PanelContainer { ThemeTypeVariation = UiTheme.SheetPanel, MouseFilter = MouseFilterEnum.Ignore };
        _cardPanel = panel;
        panel.AddChild(column);
        _card = Ui.Place(panel, 0.5f, 0.5f, -520, -300, 520, 40);
        _card.Visible = false;
        return _card;
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
                Ui.MinSize(Ui.Button("关闭", UiTheme.DarkButton, CloseLog), 120, 52)),
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
