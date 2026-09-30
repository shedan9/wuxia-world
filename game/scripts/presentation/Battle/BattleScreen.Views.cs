using Godot;
using WuxiaWorld.Domain.Combat;
using Side = WuxiaWorld.Domain.Combat.Side;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Battle;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

public sealed partial class BattleScreen
{
    private readonly Dictionary<string, UnitView> _views = [];
    private readonly List<string> _log = [];
    private readonly ButtonGroup _slotGroup = new();

    private Label _roundLabel = null!;
    private HBoxContainer _order = null!;
    private Label _momentumLabel = null!;
    private Label _enemyMomentumLabel = null!;
    private Label _speedLabel = null!;
    private VBoxContainer _logList = null!;
    private bool _logExpanded;
    private Label _sceneTitle = null!;

    private Label _actorName = null!;
    private Label _actorState = null!;
    private Control _actorGlyph = null!;
    private HBoxContainer _actorGlyphHost = null!;
    private ProgressBar _actorHp = null!;
    private ProgressBar _actorInner = null!;
    private ProgressBar _actorMomentum = null!;
    private Label _actorHpText = null!;
    private Label _actorInnerText = null!;
    private Label _actorMomentumText = null!;
    private HBoxContainer _slots = null!;
    private Label _info = null!;
    private readonly Dictionary<string, Button> _basicButtons = [];
    private PanelContainer _dock = null!;
    private Control _momentumRow = null!;

    private List<string> _roundOrder = [];
    private string? _current;

    // ── 场上单位 ─────────────────────────────────────────

    private void BuildUnits()
    {
        foreach (var view in _views.Values)
        {
            view.Standee.QueueFree();
            view.Hud.QueueFree();
            view.Intent.QueueFree();
        }

        _views.Clear();
        foreach (var unit in _session!.State.Units)
        {
            AddView(unit);
        }

        SortFigures();
    }

    /// <summary>形象按槽位由远到近排列（近者后画、压在前面）。</summary>
    private void SortFigures()
    {
        var i = 0;
        foreach (var view in _views.Values.OrderBy(v => v.Position.Slot).ThenBy(v => v.Id, StringComparer.Ordinal))
        {
            _figures.MoveChild(view.Standee, i++);
        }
    }

    private UnitView AddView(BattleUnit unit)
    {
        var view = new UnitView(unit, DisplayName(unit.Id));
        _figures.AddChild(view.Standee);
        _huds.AddChild(view.Hud);
        _huds.AddChild(view.Intent);
        view.Layout(animate: false);
        HookHover(view);
        _views[unit.Id] = view;
        return view;
    }

    private void SyncAll()
    {
        if (_session is null)
        {
            return;
        }

        var state = _session.State;
        foreach (var unit in state.Units)
        {
            if (!_views.TryGetValue(unit.Id, out var view))
            {
                view = AddView(unit);
            }

            var moved = view.Position != unit.Position;
            view.Sync(unit);
            if (moved)
            {
                view.Layout(animate: false);
                SortFigures();
            }

            view.Refresh();
        }

        RefreshMarks();
        RefreshTopBar();
        RefreshDock();
    }

    // ── 顶栏：轮次、行动顺序、势 ───────────────────────────

    private Control BuildTopBar()
    {
        _roundLabel = Ui.Text("第 1 轮", UiTheme.SealLabel, 26);
        var round = Ui.Panel(UiTheme.SealPanel, _roundLabel);
        round.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _order = Ui.Row(UiPalette.SpaceS);
        _momentumLabel = Ui.Text("", UiTheme.DarkMutedLabel, 16);
        _enemyMomentumLabel = Ui.Text("", UiTheme.DarkMutedLabel, 16);
        foreach (var label in new[] { _momentumLabel, _enemyMomentumLabel })
        {
            label.MouseFilter = MouseFilterEnum.Pass;
            label.MouseDefaultCursorShape = CursorShape.Help;
        }

        var orderTitle = Ui.Text("本轮行动顺序", UiTheme.DarkMutedLabel, 16);
        orderTitle.MouseFilter = MouseFilterEnum.Pass;
        orderTitle.MouseDefaultCursorShape = CursorShape.Help;
        orderTitle.TooltipText = "每轮开始时按速度从高到低排定，每人每轮行动一次，倒下者跳过。\n"
            + "速度 = 30 + 2×身法 + 轻功加成；同速时身法高者先，再同按固定次序。\n"
            + "迟滞等增减速要到下一轮排序时才生效。悬停各人印鉴可看其速度。";
        _speedLabel = Ui.Text("", UiTheme.GiltLabel, 16);
        var playback = Ui.KeyHints(true, ("F", "倍速"), ("Space", "跳过"), ("P", "自动"));
        playback.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var strip = Ui.Panel(UiTheme.GlassPanel, Ui.Row(UiPalette.SpaceL, round,
            Ui.Expand(Ui.Column(4, Ui.Row(UiPalette.SpaceL, orderTitle, Ui.Spacer(), _momentumLabel, _enemyMomentumLabel, _speedLabel), _order)),
            playback));
        return Ui.Place(strip, 0.5f, 0, -520, 22, 460, 150);
    }

    private void RefreshTopBar()
    {
        if (_session is null)
        {
            return;
        }

        var state = _session.State;
        _roundLabel.Text = $"第 {state.Round} 轮";
        _momentumLabel.Text = $"我方势 {state.AllyMomentum}";
        _momentumLabel.TooltipText = MomentumTip(Side.Ally);
        var threat = EnemyFinishers().Where(f => state.EnemyMomentum >= f.Cost).ToList();
        _enemyMomentumLabel.Text = threat.Count > 0 ? $"敌方势 {state.EnemyMomentum} ⚠" : $"敌方势 {state.EnemyMomentum}";
        _enemyMomentumLabel.AddThemeColorOverride("font_color", threat.Count > 0 ? UiPalette.Warm.Lightened(0.45f) : UiPalette.TextOnDarkMuted);
        _enemyMomentumLabel.TooltipText = MomentumTip(Side.Enemy);
        _speedLabel.Text = (_speed > 1 ? "2× 倍速" : "") + (_auto ? "　自动" : "");
        Ui.ClearChildren(_order);
        var passed = true;
        var living = _roundOrder.Where(id => !(_views.TryGetValue(id, out var gone) && gone.Down)).ToList();
        _order.AddThemeConstantOverride("separation", living.Count > 6 ? 3 : UiPalette.SpaceS);
        for (var i = 0; i < living.Count; i++)
        {
            var id = living[i];
            var unit = state.TryUnit(id);
            var current = id == _current;
            if (current)
            {
                passed = false;
            }

            var ally = unit?.Side == Side.Ally;
            var tone = ally ? UiPalette.Trim : UiPalette.Warm.Lightened(0.15f);
            var name = _views.TryGetValue(id, out var v) ? v.Name : id;
            // 人多时印鉴缩小一档，免得行动顺序挤出顶栏（满编 4 对 6 时十人同列）。
            var crowded = living.Count > 6;
            var glyph = Ui.Glyph(name[..1], tone, current ? (crowded ? 50 : 64) : (crowded ? 38 : 48));
            glyph.SizeFlagsVertical = SizeFlags.ShrinkEnd;
            var label = Ui.Text(current ? "行动中" : name, current ? UiTheme.GiltLabel : UiTheme.DarkMutedLabel, crowded ? 12 : 14);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            var cell = Ui.Column(2, glyph, label);
            cell.SizeFlagsVertical = SizeFlags.ShrinkEnd;
            cell.MouseFilter = MouseFilterEnum.Pass;
            glyph.MouseFilter = MouseFilterEnum.Ignore;
            if (unit is not null)
            {
                cell.TooltipText = $"{name}　速度 {_engine.EffectiveSpeed(unit)}（身法 {unit.Template.Attributes.Agility}）"
                    + (unit.IsDown ? "　已倒下" : "");
            }
            if (passed && _current is not null)
            {
                cell.Modulate = new Color(1, 1, 1, 0.55f);
            }

            if (current)
            {
                var frame = new PanelContainer();
                frame.AddThemeStyleboxOverride("panel", new OrnateBox
                {
                    Corners = CornerStyle.Bracket, CornerColor = UiPalette.Gilt, CornerSize = 12, CornerWidth = 2, CornerOutset = 4,
                });
                frame.AddChild(cell);
                _order.AddChild(frame);
            }
            else
            {
                _order.AddChild(cell);
            }

            if (i < living.Count - 1)
            {
                var arrow = Ui.Text("›", UiTheme.DarkMutedLabel, crowded ? 18 : 24);
                arrow.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                _order.AddChild(arrow);
            }
        }
    }

    /// <summary>场上某方能耗势施展的招式（去重）：(施展者, 招式, 所需势)。</summary>
    private List<(string Unit, string Skill, int Cost)> Finishers(Side side) =>
        [.. _session!.State.Living(side)
            .SelectMany(u => u.Skills.Select(s => (Unit: u.Id, Skill: s, Cost: _engine.Content.Skill(s).MomentumCost)))
            .Where(f => f.Cost > 0)
            .DistinctBy(f => f.Skill)];

    private List<(string Unit, string Skill, int Cost)> EnemyFinishers() => Finishers(Side.Enemy);

    private string MomentumTip(Side side)
    {
        var state = _session!.State;
        var value = state.Momentum(side);
        var head = side == Side.Ally ? $"我方势　{value} / 100（全队共用）" : $"敌方势　{value} / 100（敌方共用）";
        var uses = Finishers(side);
        var body = "积法：命中一次 +5，受伤一次 +3，打出破绽 +10，招架成功 +10。\n战斗中积累，战后清空。";
        var spend = uses.Count == 0
            ? (side == Side.Ally ? "\n当前阵容没有耗势招式。" : "\n在场敌人没有耗势招式。")
            : "\n用处：" + string.Join("；", uses.Select(f => $"{DisplayName(f.Unit)}“{_bundle.Name(f.Skill)}”耗 {f.Cost}"));
        var warn = side == Side.Enemy && uses.Any(f => value >= f.Cost)
            ? "\n⚠ 已够施展：敌人会先蓄力一轮并标出目标，可护援、换位、防御，或破招、点穴打断。"
            : "";
        return head + "\n" + body + spend + warn;
    }

    // ── 日志与场景签 ─────────────────────────────────────

    private Control BuildLog()
    {
        _logList = Ui.Column(4);
        var toggle = Ui.Row(UiPalette.SpaceS, Ui.Text("战斗日志", UiTheme.GiltLabel, 18), Ui.Spacer(), Ui.KeyHint("L", "展开"));
        var panel = Ui.Panel(UiTheme.GlassPanel, Ui.Column(6, toggle, _logList));
        panel.ClipContents = true;
        _logPanel = Ui.Place(panel, 1, 0, -440, 22, -40, 150);
        return _logPanel;
    }

    private PanelContainer _logPanel = null!;

    private void AddLog(string line)
    {
        _log.Add(line);
        if (_log.Count > 300)
        {
            _log.RemoveAt(0);
        }

        RefreshLog();
    }

    private void RefreshLog()
    {
        Ui.ClearChildren(_logList);
        foreach (var line in _log.TakeLast(_logExpanded ? 18 : 3))
        {
            _logList.AddChild(Ui.Text(line, UiTheme.DarkLabel, 16, wrap: true));
        }
    }

    private bool ToggleLog()
    {
        _logExpanded = !_logExpanded;
        _logPanel.OffsetBottom = _logExpanded ? 560 : 150;
        _logPanel.OffsetLeft = _logExpanded ? -560 : -440;
        RefreshLog();
        return true;
    }

    private Control BuildSceneTag()
    {
        _sceneTitle = Ui.Text("", UiTheme.DarkLabel, 20);
        var tag = Ui.Panel(UiTheme.GlassPanel, Ui.Column(2,
            _sceneTitle,
            Ui.Text("M1 战斗原型：结算来自规则内核；动作为补间占位", UiTheme.DarkMutedLabel, 15)));
        return Ui.Place(tag, 0, 0, 40, 30, 470, 110);
    }

    // ── 指令区 ───────────────────────────────────────────

    private Control BuildDock()
    {
        _dock = new PanelContainer
        {
            ThemeTypeVariation = UiTheme.DarkPanel,
            AnchorLeft = 0, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1,
            OffsetLeft = 40, OffsetRight = -40, OffsetTop = -262, OffsetBottom = -24,
        };

        _actorName = Ui.Text("", UiTheme.DarkTitleLabel, 32);
        _actorState = Ui.Text("", UiTheme.GiltLabel, 17);
        _actorState.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        (_actorHp, _actorHpText) = Meter(UiTheme.HealthBar, out var hpRow, "气血");
        (_actorInner, _actorInnerText) = Meter(UiTheme.InnerBar, out var innerRow, "内力");
        (_actorMomentum, _actorMomentumText) = Meter(UiTheme.ExpBar, out var momentumRow, "势");
        momentumRow.MouseFilter = MouseFilterEnum.Pass;
        momentumRow.MouseDefaultCursorShape = CursorShape.Help;
        _momentumRow = momentumRow;
        var bars = Ui.Column(8, Ui.Row(UiPalette.SpaceS, _actorName, _actorState), hpRow, innerRow, momentumRow);
        _actorGlyphHost = Ui.Row(0);
        _actorGlyph = Ui.Glyph("主", UiPalette.Accent, 96);
        _actorGlyphHost.AddChild(_actorGlyph);
        _actorGlyphHost.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var actor = Ui.Row(UiPalette.SpaceL, _actorGlyphHost, bars);

        _info = Ui.Text("", UiTheme.DarkMutedLabel, 17, wrap: true);
        _info.CustomMinimumSize = new Vector2(860, 0);
        _slots = Ui.Row(UiPalette.SpaceM);
        var skills = Ui.Column(UiPalette.SpaceS, _slots, _info);

        var basics = new GridContainer { Columns = 2 };
        basics.AddThemeConstantOverride("h_separation", UiPalette.SpaceS);
        basics.AddThemeConstantOverride("v_separation", UiPalette.SpaceS);
        foreach (var (key, label, action) in new (string, string, Action)[]
                 {
                     ("D", "防御", () => Submit(new Defend(_session!.AwaitingPlayer!.Id))),
                     ("R", "调息", () => Submit(new Meditate(_session!.AwaitingPlayer!.Id))),
                     ("I", "物品", CycleItem),
                     ("S", "换位", EnterSwap),
                     ("X", "撤退", () => Submit(new Retreat(_session!.AwaitingPlayer!.Id))),
                 })
        {
            var button = Ui.Button(label, UiTheme.DarkButton, () =>
            {
                if (AcceptingCommand)
                {
                    action();
                }
            });
            button.CustomMinimumSize = new Vector2(132, 40);
            button.AddThemeFontSizeOverride("font_size", 19);
            var cap = Ui.KeyHint(key, "");
            cap.MouseFilter = MouseFilterEnum.Ignore;
            basics.AddChild(Ui.Row(4, cap, button));
            _basicButtons[key] = button;
        }

        var hints = Ui.KeyHints(true, ("Tab", "换目标"), ("Enter", "施展"));
        var right = Ui.Column(UiPalette.SpaceS, basics, hints);
        _dock.AddChild(Ui.Row(UiPalette.SpaceXl, actor, VerticalRule(), skills, Ui.Spacer(), right));
        return _dock;
    }

    private static (ProgressBar, Label) Meter(string variation, out Control row, string label)
    {
        var bar = Ui.Bar(variation, 0, 1, 220);
        var text = Ui.Text("", UiTheme.DarkMutedLabel, 16);
        row = Ui.Row(UiPalette.SpaceS, Ui.MinSize(Ui.Text(label, UiTheme.DarkLabel, 18), 44), bar, text);
        return (bar, text);
    }

    private static Control VerticalRule()
    {
        var line = new ColorRect { Color = UiPalette.Trim with { A = 0.35f }, CustomMinimumSize = new Vector2(1, 0) };
        line.SizeFlagsVertical = SizeFlags.ExpandFill;
        return line;
    }

    private void RefreshDock()
    {
        var actor = _session?.AwaitingPlayer;
        var ready = actor is not null && _queue.Count == 0 && !_playing;
        _dock.Modulate = new Color(1, 1, 1, ready ? 1f : 0.55f);
        if (actor is null)
        {
            var pending = _session?.State.Pending is { } p ? DisplayName(p) : null;
            _actorState.Text = _session?.Ended == true ? "战斗结束" : pending is not null ? $"{pending} 行动中" : "";
            Ui.ClearChildren(_slots);
            _info.Text = "";
            HideTargeting();
            return;
        }

        var name = DisplayName(actor.Id);
        _actorName.Text = name;
        _actorState.Text = ready ? "请下令" : "结算中";
        _actorGlyph.QueueFree();
        _actorGlyph = Ui.Glyph(name[..1], actor.Id == "char.hero" ? UiPalette.Accent : UiPalette.Trim, 96);
        _actorGlyphHost.AddChild(_actorGlyph);
        SetMeter(_actorHp, _actorHpText, actor.Hp, actor.Stats.MaxHp);
        SetMeter(_actorInner, _actorInnerText, actor.Inner, actor.Stats.MaxInner);
        SetMeter(_actorMomentum, _actorMomentumText, _session!.State.AllyMomentum, CombatConstants.MaxMomentum);
        _momentumRow.TooltipText = MomentumTip(Side.Ally);
        _actorMomentum.TooltipText = _momentumRow.TooltipText;

        foreach (var (key, button) in _basicButtons)
        {
            var reason = key switch
            {
                "X" => _engine.Validate(_session.State, new Retreat(actor.Id)),
                "I" => _session.State.Items.Count == 0 ? "没有可用物品" : null,
                _ => null,
            };
            button.Disabled = reason is not null;
            button.TooltipText = reason ?? "";
            if (key == "I")
            {
                button.Text = $"物品 {_session.State.Items.Values.Sum()}";
            }
        }

        Ui.ClearChildren(_slots);
        _slots.AddChild(SkillSlot(actor, CoreIds.BasicAttack, "A"));
        for (var i = 0; i < actor.Skills.Count; i++)
        {
            _slots.AddChild(SkillSlot(actor, actor.Skills[i], $"{i + 1}"));
        }

        if (ready)
        {
            // 同一角色再次行动时沿用上次的招式（仍可用时），目标按当前局面重算。
            var keep = _skillActor == actor.Id && _skill is not null && Usable(actor, _skill) is null ? _skill : DefaultSkill(actor);
            _skillActor = actor.Id;
            SelectSkill(keep);
        }
    }

    private static void SetMeter(ProgressBar bar, Label text, int value, int max)
    {
        bar.MaxValue = Math.Max(1, max);
        bar.Value = value;
        text.Text = $"{value} / {max}";
    }

    private string DefaultSkill(BattleUnit actor) =>
        actor.Skills.FirstOrDefault(s => Usable(actor, s) is null && _engine.Content.Skill(s).TargetsEnemies) ?? CoreIds.BasicAttack;

    /// <summary>此刻能否施展；不能时返回原因（冷却、内力、势、无目标）。</summary>
    private string? Usable(BattleUnit actor, string skill)
    {
        var def = _engine.Content.Skill(skill);
        var candidates = Targeting.Candidates(_session!.State, actor, def.TargetRule);
        return _engine.Validate(_session.State, new UseSkill(actor.Id, skill, candidates.Count > 0 ? candidates[0].Id : null));
    }

    private Button SkillSlot(BattleUnit actor, string skillId, string key)
    {
        var def = _engine.Content.Skill(skillId);
        var reason = Usable(actor, skillId);
        var cooldown = actor.CooldownOf(skillId);
        var name = _bundle.Name(skillId);
        var button = new Button
        {
            ThemeTypeVariation = UiTheme.CardButton, ToggleMode = true, ButtonGroup = _slotGroup,
            CustomMinimumSize = new Vector2(116, 116), Disabled = reason is not null,
            ButtonPressed = _mode == Mode.Skill && _skill == skillId,
            TooltipText = reason is null ? name : $"{name}：{reason}",
        };
        button.SetMeta("skill", skillId);

        var body = Ui.Column(2);
        body.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        body.Alignment = BoxContainer.AlignmentMode.Center;
        var glyph = Ui.Glyph(name[..1], skillId == CoreIds.BasicAttack ? UiPalette.TextMuted : UiPalette.Trim, 48);
        glyph.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        var caption = Ui.Text(name, UiTheme.DarkLabel, 17);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        // 卡面只写消耗，冷却写在下方说明行里，免得三项挤出卡面。
        var costText = def.InnerCost == 0 && def.MomentumCost == 0 ? "无消耗"
            : string.Join(" · ", new[] { def.InnerCost > 0 ? $"内 {def.InnerCost}" : null, def.MomentumCost > 0 ? $"势 {def.MomentumCost}" : null }.OfType<string>());
        var cost = Ui.Text(costText, UiTheme.DarkMutedLabel, 14);
        cost.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(glyph);
        body.AddChild(caption);
        body.AddChild(cost);
        button.AddChild(Ui.IgnoreMouse(body));
        var cap = Ui.KeyHint(key, "");
        Ui.Place(cap, 0, 0, 6, 6, 40, 34);
        button.AddChild(Ui.IgnoreMouse(cap));

        if (reason is not null)
        {
            var veil = new ColorRect { Color = UiPalette.Abyss with { A = 0.72f }, MouseFilter = MouseFilterEnum.Ignore };
            veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            button.AddChild(veil);
            var note = Ui.Text(cooldown > 0 ? $"冷却 {cooldown}" : reason.TrimEnd('。'), UiTheme.GiltLabel, cooldown > 0 ? 22 : 16);
            note.HorizontalAlignment = HorizontalAlignment.Center;
            note.VerticalAlignment = VerticalAlignment.Center;
            note.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
            note.AddThemeColorOverride("font_outline_color", UiPalette.Abyss);
            note.AddThemeConstantOverride("outline_size", 6);
            body.Modulate = new Color(1, 1, 1, 0.35f);
            button.AddChild(Ui.Place(note, 0, 0, 4, 0, 112, 116));
        }
        else
        {
            button.Pressed += () =>
            {
                if (AcceptingCommand)
                {
                    SelectSkill(skillId);
                }
            };
        }

        return button;
    }

    private static string CostText(SkillDefinition def)
    {
        var parts = new List<string>();
        if (def.InnerCost > 0)
        {
            parts.Add($"内 {def.InnerCost}");
        }

        if (def.MomentumCost > 0)
        {
            parts.Add($"势 {def.MomentumCost}");
        }

        if (def.Cooldown > 0)
        {
            parts.Add($"冷却 {def.Cooldown}");
        }

        return parts.Count == 0 ? "无消耗" : string.Join(" · ", parts);
    }
}
