using Godot;
using WuxiaWorld.Application.Combat;
using WuxiaWorld.Domain.Combat;
using Side = WuxiaWorld.Domain.Combat.Side;
using WuxiaWorld.Domain.Combat.Ai;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Game.Adapters;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Infrastructure.Content;

namespace WuxiaWorld.Game.Presentation.Battle;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// M1 战斗原型（开发计划 M1-07）：界面沿用 M0 已认可的战斗版式（设计规范 5.5），数值与结果全部来自规则内核。
/// 玩家命令经 <see cref="BattleSession"/> 提交，内核立即结算并返回事件；本页按次序消费事件播放补间动作、飘字与日志，
/// 动画不反向决定结果，倍速与跳过只改变播放（架构文档 7.5）。事件播放期间不接受命令，切出窗口不会多执行行动。
/// 正式骨骼动作尚未制作，这里的出手、受击为补间占位。
/// P 键切换自动战斗（玩家单位按与敌方同一套贪心评分出招），用于观战与验证播放。
/// 截图参数 <c>--tab</c>：0 配置页、1 押运队冲突选招（单体描边）、2 旧渡水门（首领蓄力后）、3 结算、4 旧渡水门自动战斗（配合 --motion 截取播放过程）、
/// 5 横篙横扫一排的范围、6 穿林剑穿透一列的范围、7 悬停唐守亭弹出信息小窗、8 悬停敌方势的说明、
/// 9 鼠标停在押运打手与唐守亭重叠处的押运打手身上（核对按轮廓拾取）、10 横篙横扫一排时悬停押运打手乙（群体招预估并入小窗）。
/// </summary>
public sealed partial class BattleScreen : Control
{
    private static readonly (string Id, string Label, string Note)[] Builds =
    [
        ("sword", "剑术破招", "试剑看破 → 破势削架势 → 破绽时连环刺；首领蓄力时破招打断"),
        ("fist", "拳掌护援", "前排承伤：护身掌替同伴挡招、沉肘反击、防御时招架积势，势满扫堂"),
        ("inner", "内功调息", "劈空劲远取、回气诀疗伤驱散、点穴打断，势满周天归元；调息时受伤加重"),
    ];

    private static readonly string[] EncounterIds = ["battle.01.escort_skirmish", "battle.01.old_ferry_sluice"];

    private static readonly Dictionary<string, int> StartingItems = new()
    {
        ["item.medicine.golden_sore"] = 2, ["item.medicine.qi_pill"] = 1, ["item.medicine.clear_heart"] = 1,
    };

    private CombatBundle _bundle = null!;
    private BattleEngine _engine = null!;
    private BattleSession? _session;
    private int _build;
    private int _encounter;
    private ulong _seed;

    private Control _field = null!;
    private readonly Control _ground = new();
    private readonly Control _figures = new();
    private readonly Control _hits = new();
    private readonly Control _huds = new();
    private readonly Control _fx = new();
    private Control _overlay = null!;
    private Label _banner = null!;
    private Label _toast = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        if (GeneratedContent.Combat is not { } bundle)
        {
            ShowFatal(GeneratedContent.Error ?? "战斗内容包读取失败。");
            return;
        }

        var errors = CombatContentValidator.Validate(bundle);
        if (errors.Count > 0)
        {
            ShowFatal("内容包校验失败：\n" + string.Join("\n", errors.Take(8)));
            return;
        }

        _bundle = bundle;
        _engine = new BattleEngine(bundle.ToContent());
        _seed = (ulong)Time.GetTicksUsec();

        AddChild(new BattleBackdrop { ArtId = "battle.ferry_dusk" });
        _field = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _field.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_field);

        // 场上分层：地面范围标记 → 形象（按槽位由远到近排序）→ 拾取层 → 状态条与意图签 → 预估与飘字。不用 ZIndex，免得压过弹层。
        foreach (var layer in new[] { _ground, _figures, _hits, _huds, _fx })
        {
            layer.MouseFilter = MouseFilterEnum.Ignore;
            layer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _field.AddChild(layer);
        }

        // 拾取层铺满场地：鼠标移到哪个人物的轮廓上就选中谁（按不透明像素，由前往后找），不再用矩形点选区。
        _hits.MouseFilter = MouseFilterEnum.Stop;
        _hits.GuiInput += OnFieldInput;
        _hits.MouseExited += () => ShowUnitPopup(_barHover);

        BuildTargeting();
        BuildPopup();
        AddChild(BuildTopBar());
        AddChild(BuildLog());
        AddChild(BuildDock());
        AddChild(BuildSceneTag());

        _banner = Ui.Text("", UiTheme.DisplayLabel, 44);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.AddThemeColorOverride("font_color", UiPalette.TextOnDark);
        _banner.AddThemeColorOverride("font_outline_color", UiPalette.Abyss with { A = 0.9f });
        _banner.AddThemeConstantOverride("outline_size", 10);
        _banner.MouseFilter = MouseFilterEnum.Ignore;
        Ui.Place(_banner, 0.5f, 0, -700, 250, 700, 320);
        _banner.Modulate = Colors.Transparent;
        AddChild(_banner);

        _toast = Ui.Text("", UiTheme.DarkLabel, 20);
        _toast.HorizontalAlignment = HorizontalAlignment.Center;
        _toast.AddThemeColorOverride("font_outline_color", UiPalette.Abyss);
        _toast.AddThemeConstantOverride("outline_size", 8);
        _toast.MouseFilter = MouseFilterEnum.Ignore;
        Ui.Place(_toast, 0.5f, 1, -600, -312, 600, -276);
        _toast.Modulate = Colors.Transparent;
        AddChild(_toast);

        _overlay = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_overlay);

        // 世界状态里有待开的剧情战：直接按请求开打，不显示原型配置（M2）。
        if (AppHost.Instance.Play is { Game.World.Battle: not null } play)
        {
            StartStory(play);
            return;
        }

        switch (DevCapture.Tab)
        {
            case 1:
                StartBattle(0, 0, 20260930);
                break;
            case 2:
                StartBattle(1, 0, 20260930);
                FastForward(stopWhen: s => s.State.Units.Any(u => u.HasStatus(CoreIds.Charging)) && s.AwaitingPlayer is not null);
                break;
            case 3:
                StartBattle(0, 1, 20260930);
                FastForward(stopWhen: s => s.Ended);
                break;
            case 4:
                StartBattle(1, 2, 20260930);
                _auto = true;
                break;
            case 5:
                StartBattle(0, 0, 20260930);
                Pump();
                SelectSkill("skill.staff.pole_sweep");
                break;
            case 6:
                StartBattle(1, 0, 20260930);
                FastForward(stopWhen: s => s.AwaitingPlayer?.Id == "char.hero" && s.State.Round >= 2
                    && _engine.Validate(s.State, new UseSkill("char.hero", "skill.sword.pierce", "enemy.hookman")) is null);
                SelectSkill("skill.sword.pierce");
                _targetIndex = Math.Max(0, _targets.IndexOf("enemy.hookman"));
                RefreshTargeting();
                break;
            case 10:
                StartBattle(0, 0, 20260930);
                Pump();
                SelectSkill("skill.staff.pole_sweep");
                CallDeferred(MethodName.HoverUnitForCapture, "enemy.escort_b");
                break;
            case 9:
                StartBattle(1, 0, 20260930);
                FastForward(stopWhen: s => s.AwaitingPlayer?.Id == "char.hero");
                CallDeferred(MethodName.HoverOverlapForCapture);
                break;
            case 7 or 8:
                StartBattle(1, 0, 20260930);
                FastForward(stopWhen: s => s.State.EnemyMomentum >= 50 && s.AwaitingPlayer is not null);
                CallDeferred(MethodName.HoverForCapture, DevCapture.Tab == 7);
                break;
            default:
                ShowSetup();
                break;
        }
    }

    public override void _Process(double delta)
    {
        Pump();
        AutoplayResult();
    }

    /// <summary>截图用：在指定人物画框里找一个按轮廓拾取正落在此人身上的点，把鼠标移过去。</summary>
    private void HoverUnitForCapture(string id)
    {
        var frame = _views[id].Standee.GetGlobalRect();
        for (var y = frame.Position.Y + frame.Size.Y * 0.3f; y < frame.End.Y; y += 6)
        {
            for (var x = frame.Position.X; x < frame.End.X; x += 6)
            {
                var p = new Vector2(x, y);
                if (PickUnit(p) == id)
                {
                    Input.WarpMouse(p);
                    Input.ParseInputEvent(new InputEventMouseMotion { Position = p, GlobalPosition = p });
                    return;
                }
            }
        }

        GD.Print($"未找到 {id} 的拾取点");
    }

    /// <summary>截图用：在押运打手的轮廓内找一个同时落在唐守亭画框里的点，把鼠标移过去。</summary>
    private void HoverOverlapForCapture()
    {
        var escort = _views["enemy.escort_a"].Standee;
        var tang = _views["enemy.tang_shouting"].Standee;
        var tangFrame = tang.GetGlobalRect();
        var frame = escort.GetGlobalRect();
        for (var y = frame.Position.Y + frame.Size.Y * 0.3f; y < frame.End.Y; y += 6)
        {
            for (var x = frame.Position.X; x < frame.End.X; x += 6)
            {
                var p = new Vector2(x, y);
                if (tangFrame.HasPoint(p) && escort.HitTest(escort.GetGlobalTransform().AffineInverse() * p)
                    && !tang.HitTest(tang.GetGlobalTransform().AffineInverse() * p))
                {
                    Input.WarpMouse(p);
                    Input.ParseInputEvent(new InputEventMouseMotion { Position = p, GlobalPosition = p });
                    GD.Print($"拾取测试点 {p}，拾取结果 {PickUnit(p)}");
                    return;
                }
            }
        }

        GD.Print("未找到重叠点");
    }

    /// <summary>截图用：把鼠标移到唐守亭状态条或敌方势上，让信息小窗 / 悬停说明弹出。</summary>
    private void HoverForCapture(bool stance)
    {
        var rect = stance ? _views["enemy.tang_shouting"].Standee.GetGlobalRect() : _enemyMomentumLabel.GetGlobalRect();
        var point = rect.GetCenter();
        Input.WarpMouse(point);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = point, GlobalPosition = point });
    }

    // ── 开战与重开 ───────────────────────────────────────

    private void StartBattle(int encounter, int build, ulong seed)
    {
        _encounter = encounter;
        _build = build;
        _seed = seed;
        CloseOverlay();

        var content = _engine.Content;
        var allies = new List<AllyEntry>
        {
            new(content.Combatant($"combatant.hero.{Builds[build].Id}"), "char.hero", new Position(0, 1)),
            new(content.Combatant("combatant.lu_qinghe"), "char.lu_qinghe", new Position(1, 1)),
        };
        if (EncounterIds[encounter] == "battle.01.old_ferry_sluice")
        {
            // 正式 Demo 由经典人物援手（第 2.1 节“冲突”段）；人物锚点核验（M2-10）前以占位同行者代替。
            allies.Add(new(content.Combatant("combatant.placeholder.companion"), "char.companion", new Position(0, 2)));
        }

        var setup = new BattleSetup { EncounterId = EncounterIds[encounter], Seed = seed, Allies = allies, Items = StartingItems };
        Begin(setup, $"{_bundle.Name(setup.EncounterId)}：主角（{Builds[build].Label}）· 种子 {seed}");
    }

    /// <summary>按开战输入建立会话并开始播放；原型配置与剧情战共用。</summary>
    private void Begin(BattleSetup setup, string logLine)
    {
        CloseOverlay();
        _seed = setup.Seed;
        _session = new BattleSession(_engine, setup, _bundle.ContentVersion);
        AppHost.Instance.Sound.PlayMusic(setup.EncounterId.EndsWith("sluice", StringComparison.Ordinal) ? "bgm.boss.old_ferry" : "bgm.battle.common", 0.8f);
        // 第一章两场战斗都在河边（押运队登岸、旧渡水门）：配乐底下留一层河水声；开场拔刃一响。
        AppHost.Instance.Sound.PlayAmbience("amb.river");
        AppHost.Instance.Sound.Play("battle.draw", -4);
        ResetPlayback();
        _popupUnit = null;
        _popup.Visible = false;
        BuildUnits();
        _sceneTitle.Text = $"{_bundle.Name(setup.EncounterId)}　·　{(_session.State.Locked ? "剧情战" : "普通战")}";
        _log.Clear();
        AddLog(logLine);
        Enqueue(_session.StartEvents);
    }

    private void Restart(bool newSeed) => StartBattle(_encounter, _build, newSeed ? (ulong)Time.GetTicksUsec() : _seed);

    /// <summary>截图用：跳过动画，玩家单位按贪心评分出招，直到条件满足。</summary>
    private void FastForward(Func<BattleSession, bool> stopWhen)
    {
        var session = _session!;
        _skipping = true;
        for (var i = 0; i < 600 && !stopWhen(session) && !session.Ended; i++)
        {
            var result = session.AwaitingPlayer is { } unit
                ? session.Submit(BattleAi.BestAttack(_engine, session.State, unit, skirmish: false))
                : session.StepAi()!;
            Enqueue(result.Events);
            Pump();
        }

        _skipping = false;
        Pump();
    }

    // ── 输入 ─────────────────────────────────────────────

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_bundle is null || @event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        if (_setupOpen)
        {
            if (SetupKey(key.Keycode))
            {
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (_resultOpen)
        {
            if (_story is not null ? StoryResultKey(key.Keycode) : ResultKey(key.Keycode))
            {
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        var handled = key.Keycode switch
        {
            Key.P => ToggleAuto(),
            Key.F => ToggleSpeed(),
            Key.Space => SkipPlayback(),
            Key.L => ToggleLog(),
            _ => false,
        };
        if (!handled && AcceptingCommand)
        {
            handled = CommandKey(key.Keycode, key.ShiftPressed);
        }

        if (handled)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    private bool CommandKey(Key keycode, bool shift)
    {
        switch (keycode)
        {
            case Key.A:
                SelectSkill(CoreIds.BasicAttack);
                return true;
            case >= Key.Key1 and <= Key.Key6:
            {
                var index = (int)(keycode - Key.Key1);
                var actor = _session!.AwaitingPlayer!;
                if (index < actor.Skills.Count)
                {
                    SelectSkill(actor.Skills[index]);
                }

                return true;
            }

            case Key.Tab or Key.Right or Key.Down:
                CycleTarget(shift ? -1 : 1);
                return true;
            case Key.Left or Key.Up:
                CycleTarget(-1);
                return true;
            case Key.Enter or Key.KpEnter:
                Confirm();
                return true;
            case Key.D:
                Submit(new Defend(_session!.AwaitingPlayer!.Id));
                return true;
            case Key.R:
                Submit(new Meditate(_session!.AwaitingPlayer!.Id));
                return true;
            case Key.I:
                CycleItem();
                return true;
            case Key.S:
                EnterSwap();
                return true;
            case Key.X:
                Submit(new Retreat(_session!.AwaitingPlayer!.Id));
                return true;
            case Key.Escape when _mode != Mode.Skill:
                SelectSkill(_skill ?? CoreIds.BasicAttack);
                return true;
            default:
                return false;
        }
    }

    private bool AcceptingCommand => _session is { } s && s.AwaitingPlayer is not null && _queue.Count == 0 && !_playing;

    private void Submit(BattleCommand command)
    {
        if (!AcceptingCommand)
        {
            return;
        }

        var result = _session!.Submit(command);
        if (!result.Accepted)
        {
            Toast(result.Rejection ?? "无法执行。");
            return;
        }

        HideTargeting();
        Enqueue(result.Events);
    }

    private void Toast(string text)
    {
        _toast.Text = text;
        _toast.AddThemeColorOverride("font_color", UiPalette.Warm.Lightened(0.45f));
        var tween = _toast.CreateTween();
        _toast.Modulate = Colors.White;
        tween.TweenInterval(1.4f);
        tween.TweenProperty(_toast, "modulate:a", 0f, 0.4f);
    }

    private void ShowFatal(string message)
    {
        AddChild(Scenery.Backdrop.Veiled());
        var panel = Ui.Panel(UiTheme.DarkPanel, Ui.Column(UiPalette.SpaceM,
            Ui.Text("战斗原型无法启动", UiTheme.DarkTitleLabel, 34),
            Ui.Text(message, UiTheme.DarkLabel, 20, wrap: true),
            Ui.KeyHints(true, ("Esc", "返回标题"))));
        AddChild(Ui.Place(panel, 0.5f, 0.5f, -560, -200, 560, 200));
    }

    private static string RuleText(TargetRule rule) => rule switch
    {
        TargetRule.SingleReachableEnemy => "近身单体",
        TargetRule.SingleAnyEnemy => "远程单体",
        TargetRule.ReachableRowEnemies => "横扫一排",
        TargetRule.ColumnEnemies => "穿透一列",
        TargetRule.AllEnemies => "敌方全体",
        TargetRule.Self => "自身",
        TargetRule.SingleAlly => "己方单体",
        TargetRule.OtherAlly => "一名同伴",
        TargetRule.AllAllies => "己方全体",
        _ => "",
    };

    /// <summary>场上显示名：同模板多人时按 ID 尾字母加甲乙丙。</summary>
    private string DisplayName(string unitId)
    {
        if (_story is not null && unitId.StartsWith("char.", StringComparison.Ordinal))
        {
            return _story.Name(unitId);
        }

        if (_session?.State.TryUnit(unitId) is not { } unit)
        {
            return unitId;
        }

        var name = _bundle.Name(unit.TemplateId);
        var twins = _session.State.Units.Count(u => u.TemplateId == unit.TemplateId);
        if (twins > 1 && unitId.Length > 2 && unitId[^2] == '_')
        {
            name += unitId[^1] switch { 'a' => "甲", 'b' => "乙", 'c' => "丙", 'd' => "丁", _ => "" };
        }

        return name;
    }
}
