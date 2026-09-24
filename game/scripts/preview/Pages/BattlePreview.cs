using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Scenery;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 回合战斗展示页，版式见 docs/art/UI_DESIGN.md 第 5.5 节；规则依据架构文档第 7 节。
/// 上方行动顺序与轮次，场上单位头顶气血、架势、状态与敌方意图，下方指令区（当前角色、
/// 招式栏、基础行动），目标旁显示预估效果。1–6 选招，Tab 换目标，Enter 播放一段攻击预览，
/// V 切换胜利结算。截图参数 <c>--tab</c>：0 选招、1 胜利结算。全部为固定样例。
/// </summary>
public partial class BattlePreview : Control
{
    private const float GroundY = 540;

    private readonly Dictionary<string, (BattleStandee Standee, Control Hud)> _units = [];
    private readonly List<string> _log = [.. BattleSamples.Log];
    private readonly ButtonGroup _skillGroup = new();
    private List<SampleUnit> _targets = [];
    private int _target;
    private SampleSkill _skill = CharacterSamples.HeroSkills[0];

    private Control _field = null!;
    private PanelContainer _reticle = null!;
    private PanelContainer _estimate = null!;
    private Label _skillInfo = null!;
    private VBoxContainer _logList = null!;
    private Control? _result;
    private bool _busy;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(new Backdrop { Defocus = 0.35f, Veil = 0.12f, Leaves = 12 });

        _field = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _field.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_field);
        BuildUnits();

        _reticle = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        _reticle.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            Corners = CornerStyle.Bracket, CornerColor = UiPalette.Gilt, CornerSize = 26, CornerWidth = 3,
        });
        _field.AddChild(_reticle);
        Motion.Pulse(_reticle, 0.55f, 1.2f);

        _estimate = new PanelContainer { ThemeTypeVariation = UiTheme.GlassPanel, MouseFilter = MouseFilterEnum.Ignore };
        _field.AddChild(_estimate);

        AddChild(BuildTopBar());
        AddChild(BuildLog());
        AddChild(BuildDock());
        AddChild(BuildSceneTag());

        _targets = BattleSamples.Units.Where(u => u.Side == SampleSide.Enemy && u.Row == 0).ToList();
        _target = _targets.FindIndex(u => u.Id == "enemy.tang_shouting");
        CallDeferred(MethodName.RefreshTarget);

        if (DevCapture.Tab == 1)
        {
            ShowResult();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.V:
                if (_result is null)
                {
                    ShowResult();
                }
                else
                {
                    HideResult();
                }

                break;
            case Key.Tab when _result is null:
                _target = (_target + 1) % _targets.Count;
                RefreshTarget();
                break;
            case Key.Enter or Key.KpEnter when _result is null:
                PlayAttack();
                break;
            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

    // ── 场上单位 ─────────────────────────────────────────

    private static Vector2 Feet(SampleUnit u)
    {
        // 侧视斜列：同排槽位越靠前（Slot 越大）越靠下、越向外，形成纵深。
        var x = u.Side == SampleSide.Ally ? (u.Row == 0 ? 800 : 540) : (u.Row == 0 ? 1110 : 1400);
        return new Vector2(x + u.Slot * (u.Side == SampleSide.Ally ? -70 : 70), GroundY + u.Slot * 72);
    }

    private void BuildUnits()
    {
        // 先摆全部人物，再摆状态条与意图签，保证信息层永远压在人物上面。
        var overlays = new List<Control>();
        foreach (var u in BattleSamples.Units.OrderBy(u => u.Slot))
        {
            var scale = 0.86f + u.Slot * 0.07f;
            var height = (u.Id == "enemy.tang_shouting" ? 330 : 290) * scale * (u.Mechanism ? 0.85f : 1);
            var tone = u.Side == SampleSide.Ally
                ? (u.Id == "char.hero" ? UiPalette.Accent.Lightened(0.1f) : UiPalette.Trim)
                : (u.Mechanism ? UiPalette.TextMuted : UiPalette.PanelDark.Lightened(0.15f));
            var standee = new BattleStandee
            {
                Tone = tone, FacingLeft = u.Side == SampleSide.Enemy, Mechanism = u.Mechanism, Height = height,
            };
            var feet = Feet(u);
            standee.Position = new Vector2(feet.X - height * 0.275f, feet.Y - height);
            _field.AddChild(standee);

            // 状态条放在脚下，意图签放在头顶，互不遮挡。
            var hud = UnitHud(u);
            overlays.Add(hud);
            hud.Position = new Vector2(feet.X - 92, feet.Y + 6);
            _units[u.Id] = (standee, hud);
            if (u.Intent is not null)
            {
                var intent = IntentTag(u.Intent);
                overlays.Add(intent);
                // 机关的意图签下移一行，避免与首领的意图签叠在一起。
                intent.Position = new Vector2(feet.X - 150, feet.Y - height - (u.Mechanism ? 8 : 52));
            }
        }

        foreach (var overlay in overlays)
        {
            _field.AddChild(overlay);
        }
    }

    private static Control UnitHud(SampleUnit u)
    {
        var name = Ui.Text(u.Name, UiTheme.DarkLabel, 18);
        var hp = Ui.Bar(UiTheme.HealthBar, u.Hp, u.MaxHp, 160);
        hp.CustomMinimumSize = new Vector2(160, 10);
        var hpText = Ui.Text($"{u.Hp}", UiTheme.DarkMutedLabel, 15);
        var column = Ui.Column(3, Ui.Row(UiPalette.SpaceS, Ui.Expand(name), hpText), hp);

        if (u.MaxInner > 0)
        {
            var inner = Ui.Bar(UiTheme.InnerBar, u.Inner, u.MaxInner, 160);
            inner.CustomMinimumSize = new Vector2(160, 6);
            column.AddChild(inner);
        }

        var footer = Ui.Row(6);
        if (u.MaxStance > 0)
        {
            footer.AddChild(StancePips(u.Stance, u.MaxStance));
        }

        column.AddChild(footer);
        if (u.Statuses.Length > 0)
        {
            var statuses = footer;
            foreach (var s in u.Statuses)
            {
                var harmful = s.StartsWith('▼') || s.StartsWith('●');
                var tag = Ui.Text(s, harmful ? UiTheme.DarkLabel : UiTheme.GiltLabel, 16);
                if (harmful)
                {
                    tag.AddThemeColorOverride("font_color", UiPalette.Warm.Lightened(0.35f));
                }

                statuses.AddChild(tag);
            }
        }

        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            FillA = UiPalette.Abyss with { A = 0.72f }, Chamfer = 4, Border = UiPalette.Trim with { A = 0.3f }, BorderWidth = 1,
        }.Margins(10, 6));
        panel.AddChild(column);
        return panel;
    }

    /// <summary>敌方意图：杏红边框的警示签，提前一轮告知（架构文档 7.1）。</summary>
    private static Control IntentTag(string text)
    {
        var intent = new PanelContainer();
        intent.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            FillA = UiPalette.Abyss with { A = 0.85f }, Chamfer = 5,
            Border = UiPalette.Warm.Lightened(0.2f), BorderWidth = 1.5f,
            Marker = UiPalette.Warm, MarkerWidth = 4,
        }.Margins(14, 6));
        intent.AddChild(Ui.Text($"⚠ 意图　{text}", UiTheme.DarkLabel, 17));
        return intent;
    }

    /// <summary>架势：每 10 点一格，满格为实，空格为框。</summary>
    private static Control StancePips(int value, int max)
    {
        var row = Ui.Row(3);
        var cells = (int)Math.Ceiling(max / 10.0);
        var filled = (int)Math.Ceiling(value / 10.0);
        for (var i = 0; i < cells; i++)
        {
            var pip = new Panel { CustomMinimumSize = new Vector2(10, 8), SizeFlagsVertical = SizeFlags.ShrinkCenter, TooltipText = "架势" };
            pip.AddThemeStyleboxOverride("panel", new OrnateBox
            {
                FillA = i < filled ? UiPalette.Gilt : Colors.Transparent,
                Border = UiPalette.Gilt with { A = 0.7f }, BorderWidth = 1,
            });
            row.AddChild(pip);
        }

        return row;
    }

    private void RefreshTarget()
    {
        var target = _targets[_target];
        var (standee, _) = _units[target.Id];
        var rect = new Rect2(standee.Position, standee.Size).Grow(14);
        _reticle.Position = rect.Position;
        _reticle.Size = rect.Size;

        Ui.ClearChildren(_estimate);
        var broken = target.Statuses.Any(s => s.Contains("破绽"));
        var dmg = broken ? "71–78（破绽 +20%）" : "58–64";
        _estimate.AddChild(Ui.Column(4,
            Ui.Text($"{_skill.Name} → {target.Name}", UiTheme.GiltLabel, 18),
            Ui.Text($"预计伤害　{dmg}", UiTheme.DarkLabel, 20),
            Ui.Text("削减架势 25　·　命中 92%", UiTheme.DarkMutedLabel, 17)));
        // 预估放在两军之间的空地上、目标左侧。
        _estimate.ResetSize();
        _estimate.Position = new Vector2(rect.Position.X - 330, rect.Position.Y + rect.Size.Y * 0.3f);
    }

    // ── 攻击预览 ─────────────────────────────────────────

    private void PlayAttack()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        var (hero, _) = _units["char.hero"];
        var target = _targets[_target];
        var (foe, _) = _units[target.Id];
        var home = hero.Position;
        var strike = new Vector2(foe.Position.X - hero.Size.X * 0.9f, home.Y);

        var tween = CreateTween();
        tween.TweenProperty(hero, "position", strike, 0.18f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(() => Hit(foe, target)));
        tween.TweenInterval(0.22f);
        tween.TweenProperty(hero, "position", home, 0.3f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(() => _busy = false));
    }

    private void Hit(BattleStandee foe, SampleUnit target)
    {
        var flash = foe.CreateTween();
        foe.Modulate = new Color(2.2f, 2.2f, 2.2f);
        flash.TweenProperty(foe, "modulate", Colors.White, 0.25f);

        var origin = foe.Position;
        var shake = foe.CreateTween();
        for (var i = 0; i < 4; i++)
        {
            shake.TweenProperty(foe, "position", origin + new Vector2(i % 2 == 0 ? 8 : -8, 0), 0.03f);
        }

        shake.TweenProperty(foe, "position", origin, 0.03f);

        Float(foe, "−61", UiPalette.Surface, 56, 0);
        Float(foe, "架势 −25", UiPalette.Gilt, 26, 0.12f);
        AddLog($"主角使出“{_skill.Name}”，{target.Name}受到 61 点伤害，架势 −25。");
    }

    private void Float(Control anchor, string text, Color color, int size, float delay)
    {
        var label = Ui.Text(text, UiTheme.DisplayLabel, size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", UiPalette.Abyss with { A = 0.85f });
        label.AddThemeConstantOverride("outline_size", 8);
        label.Position = anchor.Position + new Vector2(anchor.Size.X * 0.5f - 40, anchor.Size.Y * 0.2f - delay * 200);
        label.Modulate = new Color(1, 1, 1, 0);
        _field.AddChild(label);
        var tween = label.CreateTween().SetParallel();
        tween.TweenProperty(label, "modulate:a", 1f, 0.08f).SetDelay(delay);
        tween.TweenProperty(label, "position:y", label.Position.Y - 70, 0.8f).SetDelay(delay).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tween.Chain().TweenProperty(label, "modulate:a", 0f, 0.25f);
        tween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }

    // ── 顶栏：轮次与行动顺序 ─────────────────────────────

    private static Control BuildTopBar()
    {
        var round = Ui.Panel(UiTheme.SealPanel, Ui.Text($"第 {BattleSamples.Round} 轮", UiTheme.SealLabel, 26));
        round.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        var order = Ui.Row(UiPalette.SpaceS);
        for (var i = 0; i < BattleSamples.Order.Length; i++)
        {
            var u = BattleSamples.Units.First(x => x.Id == BattleSamples.Order[i]);
            var current = i == 0;
            var tone = u.Side == SampleSide.Ally ? UiPalette.Trim : UiPalette.Warm.Lightened(0.15f);
            var glyph = Ui.Glyph(u.Glyph, tone, current ? 76 : 58);
            glyph.SizeFlagsVertical = SizeFlags.ShrinkEnd;
            var label = Ui.Text(current ? "行动中" : u.Name, current ? UiTheme.GiltLabel : UiTheme.DarkMutedLabel, 15);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            var cell = Ui.Column(2, glyph, label);
            cell.SizeFlagsVertical = SizeFlags.ShrinkEnd;
            if (current)
            {
                var frame = new PanelContainer();
                frame.AddThemeStyleboxOverride("panel", new OrnateBox
                {
                    Corners = CornerStyle.Bracket, CornerColor = UiPalette.Gilt, CornerSize = 12, CornerWidth = 2, CornerOutset = 4,
                });
                frame.AddChild(cell);
                order.AddChild(frame);
            }
            else
            {
                order.AddChild(cell);
            }

            if (i < BattleSamples.Order.Length - 1)
            {
                var arrow = Ui.Text("›", UiTheme.DarkMutedLabel, 28);
                arrow.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                order.AddChild(arrow);
            }
        }

        var strip = Ui.Panel(UiTheme.GlassPanel, Ui.Row(UiPalette.SpaceL, round,
            Ui.Column(4, Ui.Text("本轮行动顺序", UiTheme.DarkMutedLabel, 16), order)));
        return Ui.Place(strip, 0.5f, 0, -500, 22, 440, 150);
    }

    private Control BuildLog()
    {
        _logList = Ui.Column(4);
        RefreshLog();
        var toggle = Ui.Row(UiPalette.SpaceS, Ui.Text("战斗日志", UiTheme.GiltLabel, 18), Ui.Spacer(),
            Ui.KeyHint("L", "展开"));
        var panel = Ui.Panel(UiTheme.GlassPanel, Ui.Column(6, toggle, _logList));
        return Ui.Place(panel, 1, 0, -420, 22, -40, 150);
    }

    private void AddLog(string line)
    {
        _log.Add(line);
        RefreshLog();
    }

    private void RefreshLog()
    {
        Ui.ClearChildren(_logList);
        foreach (var line in _log.TakeLast(2))
        {
            _logList.AddChild(Ui.Text(line, UiTheme.DarkLabel, 16, wrap: true));
        }
    }

    private static Control BuildSceneTag()
    {
        var tag = Ui.Panel(UiTheme.GlassPanel, Ui.Column(2,
            Ui.Text("旧渡水门　·　剧情战", UiTheme.DarkLabel, 20),
            Ui.Text("战斗布景与战斗形象待制作：剪影仅示意站位", UiTheme.DarkMutedLabel, 15)));
        return Ui.Place(tag, 0, 0, 40, 30, 420, 110);
    }

    // ── 指令区 ───────────────────────────────────────────

    private Control BuildDock()
    {
        var hero = BattleSamples.Units[0];
        var dock = new PanelContainer
        {
            ThemeTypeVariation = UiTheme.DarkPanel,
            AnchorLeft = 0, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1,
            OffsetLeft = 40, OffsetRight = -40, OffsetTop = -262, OffsetBottom = -24,
        };

        // 左：当前行动者。
        var name = Ui.Text("主角", UiTheme.DarkTitleLabel, 32);
        var status = Ui.Text("行动中", UiTheme.GiltLabel, 17);
        status.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        var bars = Ui.Column(8,
            Ui.Row(UiPalette.SpaceS, name, status),
            Meter("气血", UiTheme.HealthBar, hero.Hp, hero.MaxHp),
            Meter("内力", UiTheme.InnerBar, hero.Inner, hero.MaxInner),
            Meter("势", UiTheme.ExpBar, BattleSamples.Momentum, 100));
        var actor = Ui.Row(UiPalette.SpaceL, Ui.Glyph(hero.Glyph, UiPalette.Accent, 96), bars);
        actor.GetChild<Control>(0).SizeFlagsVertical = SizeFlags.ShrinkCenter;

        // 中：招式栏与当前招式说明。
        _skillInfo = Ui.Text("", UiTheme.DarkMutedLabel, 17, wrap: true);
        _skillInfo.CustomMinimumSize = new Vector2(820, 0);
        var slots = Ui.Row(UiPalette.SpaceM);
        for (var i = 0; i < CharacterSamples.EquippedActive.Length; i++)
        {
            slots.AddChild(SkillSlot(i));
        }

        var skills = Ui.Column(UiPalette.SpaceS, slots, _skillInfo);

        // 右：基础行动。
        var basics = new GridContainer { Columns = 2 };
        basics.AddThemeConstantOverride("h_separation", UiPalette.SpaceS);
        basics.AddThemeConstantOverride("v_separation", UiPalette.SpaceS);
        foreach (var (key, label, tip) in new (string, string, string?)[]
                 {
                     ("A", "普通攻击", null), ("D", "防御", null), ("R", "调息", null),
                     ("I", "物品", null), ("S", "换位", null), ("X", "撤退", "剧情战不能撤退"),
                 })
        {
            var button = Ui.Button($"{label}", UiTheme.DarkButton, tip is null ? () => { } : null, tip is not null, tip);
            button.CustomMinimumSize = new Vector2(150, 50);
            button.AddThemeFontSizeOverride("font_size", 20);
            var cap = Ui.KeyHint(key, "");
            cap.MouseFilter = MouseFilterEnum.Ignore;
            basics.AddChild(Ui.Row(4, cap, button));
        }

        var hints = Ui.KeyHints(true, ("1–6", "选招"), ("Tab", "换目标"), ("Enter", "施展"), ("V", "结算预览"), ("Esc", "返回标题"));
        var right = Ui.Column(UiPalette.SpaceS, basics, hints);

        dock.AddChild(Ui.Row(UiPalette.SpaceXl, actor, VerticalRule(), skills, Ui.Spacer(), right));
        return dock;
    }

    private static Control VerticalRule()
    {
        var line = new ColorRect { Color = UiPalette.Trim with { A = 0.35f }, CustomMinimumSize = new Vector2(1, 0) };
        line.SizeFlagsVertical = SizeFlags.ExpandFill;
        return line;
    }

    private static Control Meter(string label, string variation, int value, int max)
    {
        var bar = Ui.Bar(variation, value, max, 240);
        var text = Ui.Text($"{value} / {max}", UiTheme.DarkMutedLabel, 16);
        return Ui.Row(UiPalette.SpaceS, Ui.MinSize(Ui.Text(label, UiTheme.DarkLabel, 18), 44), bar, text);
    }

    private Control SkillSlot(int index)
    {
        var id = CharacterSamples.EquippedActive[index];
        var skill = CharacterSamples.HeroSkills.FirstOrDefault(s => s.Id == id);
        var cooling = skill?.Id == "skill.sword.chain_thrust";
        var button = new Button
        {
            ThemeTypeVariation = UiTheme.CardButton, ToggleMode = true, ButtonGroup = _skillGroup,
            CustomMinimumSize = new Vector2(124, 124), Disabled = skill is null || cooling,
            TooltipText = skill is null ? "空槽" : cooling ? $"{skill.Name}：冷却中，1 次行动后可用" : skill.Name,
        };

        var body = Ui.Column(2);
        body.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        body.Alignment = BoxContainer.AlignmentMode.Center;
        var glyph = Ui.Glyph(skill?.Glyph ?? "空", skill is null ? UiPalette.TextMuted : UiPalette.Trim, 54);
        glyph.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        var caption = Ui.Text(skill?.Name ?? "空槽", UiTheme.DarkLabel, 17);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        var cost = Ui.Text(skill?.Cost ?? "", UiTheme.DarkMutedLabel, 14);
        cost.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(glyph);
        body.AddChild(caption);
        body.AddChild(cost);
        button.AddChild(Ui.IgnoreMouse(body));

        var key = Ui.KeyHint($"{index + 1}", "");
        Ui.Place(key, 0, 0, 6, 6, 40, 34);
        button.AddChild(Ui.IgnoreMouse(key));

        if (cooling)
        {
            // 冷却：压暗并写出剩余次数。
            var veil = new ColorRect { Color = UiPalette.Abyss with { A = 0.72f }, MouseFilter = MouseFilterEnum.Ignore };
            veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            button.AddChild(veil);
            var cd = Ui.Text("冷却 1", UiTheme.GiltLabel, 22);
            cd.HorizontalAlignment = HorizontalAlignment.Center;
            cd.VerticalAlignment = VerticalAlignment.Center;
            cd.AddThemeColorOverride("font_outline_color", UiPalette.Abyss);
            cd.AddThemeConstantOverride("outline_size", 6);
            body.Modulate = new Color(1, 1, 1, 0.35f);
            button.AddChild(Ui.Place(cd, 0, 0, 0, 0, 124, 124));
        }

        if (skill is not null && !cooling)
        {
            button.Toggled += on =>
            {
                if (on)
                {
                    SelectSkill(skill);
                }
            };
            button.MouseEntered += button.GrabFocus;
            button.FocusEntered += () => button.ButtonPressed = true;
            if (index == 0)
            {
                button.ButtonPressed = true;
                SelectSkill(skill);
                button.Ready += button.GrabFocus;
            }
        }

        return button;
    }

    private void SelectSkill(SampleSkill skill)
    {
        _skill = skill;
        _skillInfo.Text = $"{skill.Name}　·　{skill.Cost}　·　{skill.Target}　·　{skill.Effect}";
        if (_targets.Count > 0)
        {
            RefreshTarget();
        }
    }

    // ── 结算 ─────────────────────────────────────────────

    private void ShowResult()
    {
        var layer = new Control();
        layer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var veil = new ColorRect { Color = UiPalette.Abyss with { A = 0.72f } };
        veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        layer.AddChild(veil);
        Motion.FadeIn(veil, Motion.Normal);

        var title = Ui.Text("旧渡解围", UiTheme.DisplayLabel, 96);
        title.AddThemeColorOverride("font_color", UiPalette.TextOnDark);
        title.AddThemeColorOverride("font_outline_color", UiPalette.Accent with { A = 0.6f });
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var sub = Ui.Text("战斗胜利　·　第 7 轮　·　被锁渡工已获救", UiTheme.GiltLabel, 24);
        sub.HorizontalAlignment = HorizontalAlignment.Center;

        var gains = Ui.Column(UiPalette.SpaceM,
            Ui.Section("所得", dark: true),
            Gain("经验", "+120", "主角 等级 3　140 → 260 / 300"),
            Gain("修为", "+40", "用于武学熟练度"),
            Gain("物品", "转信副页 ×1", "任务物品：签押线索"),
            Gain("铜钱", "+80 文", "360 → 440 文"));
        var bonds = Ui.Column(UiPalette.SpaceM,
            Ui.Section("人物反馈", dark: true),
            Gain("陆青禾", "信任 ▲ 5", "你先救了人，没有追着唐守亭打"),
            Gain("伤势", "无", "全员未失去战斗能力"));

        var panel = new PanelContainer { ThemeTypeVariation = UiTheme.DarkPanel };
        panel.AddChild(Ui.Column(UiPalette.SpaceL, title, sub, Ui.Rule(dark: true),
            Ui.Row(UiPalette.SpaceXxl, Ui.Expand(gains), Ui.Expand(bonds)),
            Ui.Rule(dark: true),
            Ui.Row(UiPalette.SpaceM, Ui.Text("固定样例：奖励与关系变化不来自规则结算", UiTheme.DarkMutedLabel, 16), Ui.Spacer(),
                Ui.KeyHints(true, ("V", "返回战斗"), ("Enter", "继续")))));
        layer.AddChild(Ui.Place(panel, 0.5f, 0.5f, -760, -320, 760, 320));
        Motion.Enter(panel, 0.1f, Motion.Slow, rise: 30);
        AddChild(layer);
        _result = layer;
    }

    private void HideResult()
    {
        if (_result is { } layer)
        {
            _result = null;
            Motion.FadeOut(layer, Motion.Quick, layer.QueueFree);
        }
    }

    private static Control Gain(string label, string value, string note)
    {
        var v = Ui.Text(value, UiTheme.DarkLabel, 24);
        if (value.Contains('▲'))
        {
            v.AddThemeColorOverride("font_color", UiPalette.Trim.Lightened(0.2f));
        }

        return Ui.Row(UiPalette.SpaceL, Ui.MinSize(Ui.Text(label, UiTheme.DarkMutedLabel, 20), 96), Ui.MinSize(v, 200),
            Ui.Text(note, UiTheme.DarkMutedLabel, 18));
    }
}
