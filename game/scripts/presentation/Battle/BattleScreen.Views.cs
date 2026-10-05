using Godot;
using WuxiaWorld.Domain.Combat;
using Side = WuxiaWorld.Domain.Combat.Side;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Game.Presentation.App;
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
    private ProgressBar _actorHp = null!;
    private ProgressBar _actorInner = null!;
    private Label _actorHpText = null!;
    private Label _actorInnerText = null!;
    private HBoxContainer _slots = null!;
    private readonly Dictionary<string, Button> _basicButtons = [];
    private PanelContainer _dock = null!;

    private List<string> _roundOrder = [];
    private string? _current;

    // 局部刷新（M3-07）：行动顺序、招式卡按内容键比较，键不变就不重建；日志复用标签只改文字。
    private string? _orderKey;
    private string? _dockKey;

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
        // 带剑术招式的人物用持剑帧组（有该组帧的形象才生效，如学剑术的主角；令狐冲的基础帧本就持剑）。
        var skills = _engine.Content.Skills;
        var armed = unit.Skills.Any(id => skills.TryGetValue(id, out var skill) && skill.HasTag("sword")) ? "sword" : null;
        var view = new UnitView(unit, DisplayName(unit.Id), armed);
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
        var playback = Ui.KeyHints(true, (KeyBindings.Label("battle_speed"), "倍速"), (KeyBindings.Label("battle_skip"), "跳过"), (KeyBindings.Label("battle_auto"), "自动"));
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
        RefreshOrder();
    }

    /// <summary>
    /// 顶栏行动顺序。每格的外观只由（人物、是否行动中、是否已行动、人多缩小、提示文字）决定，拼成格键：
    /// 整条不变就不动；变了只重建键变了的格，其余格与箭头摘下后按新次序挂回（M3-07，原先每条事件都整条删了重建）。
    /// </summary>
    private void RefreshOrder()
    {
        var state = _session!.State;
        var living = _roundOrder.Where(id => !(_views.TryGetValue(id, out var gone) && gone.Down)).ToList();
        // 人多时印鉴缩小一档，免得行动顺序挤出顶栏（满编 4 对 6 时十人同列）。
        var crowded = living.Count > 6;
        var cells = new List<(string Id, string Key, bool Current, bool Dim, string Tip)>(living.Count);
        var passed = true;
        foreach (var id in living)
        {
            var unit = state.TryUnit(id);
            var current = id == _current;
            if (current)
            {
                passed = false;
            }

            var name = _views.TryGetValue(id, out var v) ? v.Name : id;
            var tip = unit is null ? "" : $"{name}　速度 {_engine.EffectiveSpeed(unit)}（身法 {unit.Template.Attributes.Agility}）" + (unit.IsDown ? "　已倒下" : "");
            var dim = passed && _current is not null;
            cells.Add((id, $"{id}|{current}|{dim}|{crowded}|{tip}", current, dim, tip));
        }

        var orderKey = string.Join(";", cells.Select(c => c.Key)) + "|" + crowded;
        if (orderKey == _orderKey)
        {
            return;
        }

        _orderKey = orderKey;
        var wanted = cells.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        var reuse = new Dictionary<string, Control>(StringComparer.Ordinal);
        var arrows = new Stack<Control>();
        foreach (var child in _order.GetChildren().OfType<Control>())
        {
            _order.RemoveChild(child);
            if (child.HasMeta(OrderKeyMeta) && wanted.Contains(child.GetMeta(OrderKeyMeta).AsString()))
            {
                reuse[child.GetMeta(OrderKeyMeta).AsString()] = child;
            }
            else if (child.HasMeta(ArrowMeta) && child.GetMeta(ArrowMeta).AsBool() == crowded)
            {
                arrows.Push(child);
            }
            else
            {
                child.QueueFree();
            }
        }

        _order.AddThemeConstantOverride("separation", crowded ? 3 : UiPalette.SpaceS);
        for (var i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            if (!reuse.Remove(c.Key, out var cell))
            {
                cell = OrderCell(c.Id, c.Current, c.Dim, crowded, c.Tip);
                cell.SetMeta(OrderKeyMeta, c.Key);
            }

            _order.AddChild(cell);
            if (i < cells.Count - 1)
            {
                if (!arrows.TryPop(out var arrow))
                {
                    arrow = Ui.Text("›", UiTheme.DarkMutedLabel, crowded ? 18 : 24);
                    arrow.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                    arrow.SetMeta(ArrowMeta, crowded);
                }

                _order.AddChild(arrow);
            }
        }

        foreach (var left in reuse.Values.Concat(arrows))
        {
            left.QueueFree();
        }
    }

    private const string OrderKeyMeta = "order_key";
    private const string ArrowMeta = "order_arrow";

    private Control OrderCell(string id, bool current, bool dim, bool crowded, string tip)
    {
        var ally = _session!.State.TryUnit(id)?.Side == Side.Ally;
        var tone = ally ? UiPalette.Trim : UiPalette.Warm.Lightened(0.15f);
        var name = _views.TryGetValue(id, out var v) ? v.Name : id;
        var glyph = Ui.Glyph(name[..1], tone, current ? (crowded ? 50 : 64) : (crowded ? 38 : 48));
        glyph.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        var label = Ui.Text(current ? "行动中" : name, current ? UiTheme.GiltLabel : UiTheme.DarkMutedLabel, crowded ? 12 : 14);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var cell = Ui.Column(2, glyph, label);
        cell.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        cell.MouseFilter = MouseFilterEnum.Pass;
        glyph.MouseFilter = MouseFilterEnum.Ignore;
        cell.TooltipText = tip;
        if (dim)
        {
            cell.Modulate = new Color(1, 1, 1, 0.55f);
        }

        if (!current)
        {
            return cell;
        }

        var frame = new PanelContainer();
        frame.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            Corners = CornerStyle.Bracket, CornerColor = UiPalette.Gilt, CornerSize = 12, CornerWidth = 2, CornerOutset = 4,
        });
        frame.AddChild(cell);
        return frame;
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
        var toggle = Ui.Row(UiPalette.SpaceS, Ui.Text("战斗日志", UiTheme.GiltLabel, 18), Ui.Spacer(), Ui.KeyHint(KeyBindings.Label("battle_log"), "展开"));
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

    /// <summary>日志行复用标签：新一行到来只改各行文字，不删了重建。</summary>
    private void RefreshLog()
    {
        var lines = _log.TakeLast(_logExpanded ? 18 : 3).ToList();
        while (_logList.GetChildCount() < lines.Count)
        {
            _logList.AddChild(Ui.Text("", UiTheme.DarkLabel, 16, wrap: true));
        }

        var labels = _logList.GetChildren();
        for (var i = 0; i < labels.Count; i++)
        {
            var label = (Label)labels[i];
            label.Visible = i < lines.Count;
            if (i < lines.Count && label.Text != lines[i])
            {
                label.Text = lines[i];
            }
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
        var story = AppHost.Instance.Play is { Game.World.Battle: not null };
        var note = story
            ? "剧情战：结算写回世界；动作为补间占位"
            : "M1 战斗原型：结算来自规则内核；动作为补间占位";
        var column = Ui.Column(2, _sceneTitle);
        if (!story || AppHost.DevInfo)
        {
            // 剧情战的开发说明只在开发信息打开时显示（F12 / --dev）；原型页本身就是开发页，照常显示。
            column.AddChild(Ui.Text(note, UiTheme.DarkMutedLabel, 15, wrap: true));
        }

        var tag = Ui.Panel(UiTheme.GlassPanel, column);
        tag.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        var holder = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        holder.AddChild(tag);
        return Ui.Place(holder, 0, 0, 40, 30, 430, 140);
    }

    // ── 指令区 ───────────────────────────────────────────

    /// <summary>
    /// 指令区（2026-10-05 用户要求精简）：画面底部居中的一条窄黛本，只留当前角色名与气血 / 内力两条细条、招式格、五个基本指令。
    /// 势在顶栏已有（我方势），不再重复；招式说明不常驻，悬停招式格或换招时由其上方的说明小签给出；不放键帽提示行。
    /// </summary>
    private Control BuildDock()
    {
        _dock = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 1, AnchorBottom = 1,
            OffsetBottom = -18,

            // 宽度随内容（招式格数、字号）向两侧长，高度向上长，底边不越出画面。
            GrowHorizontal = GrowDirection.Both,
            GrowVertical = GrowDirection.Begin,
        };

        // 与黛本同一套黛底、泥金笔框与卷云角，角饰与边距缩小，配窄条。
        _dock.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            FillA = UiPalette.PanelDark with { A = 0.95f }, FillB = UiPalette.Abyss with { A = 0.97f },
            Ragged = 1.6f, Seed = 29, Grain = Colors.White with { A = 0.045f },
            Border = UiPalette.Gilt with { A = 0.7f }, BorderWidth = 1.4f, Brush = true,
            Inner = UiPalette.Gilt with { A = 0.2f }, InnerInset = 6,
            Wash = UiPalette.Accent with { A = 0.16f },
            Corners = CornerStyle.Cloud, CornerSize = 30, CornerWidth = 1.8f, CornerColor = UiPalette.Gilt,
            CornerOutset = -2,
            Shadow = UiPalette.Abyss with { A = 0.5f }, ShadowSize = 18, ShadowOffset = new Vector2(0, 8),
        }.Margins(26, 12));

        _actorName = Ui.Text("", UiTheme.DarkTitleLabel, 24);
        _actorState = Ui.Text("", UiTheme.GiltLabel, 14);
        _actorState.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        (_actorHp, _actorHpText) = Meter(UiTheme.HealthBar, out var hpRow, "血");
        (_actorInner, _actorInnerText) = Meter(UiTheme.InnerBar, out var innerRow, "内");
        var actor = Ui.Column(6, Ui.Row(UiPalette.SpaceS, _actorName, _actorState), hpRow, innerRow);
        actor.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        _slots = Ui.Row(UiPalette.SpaceS);

        var basics = new GridContainer { Columns = 3 };
        basics.AddThemeConstantOverride("h_separation", 6);
        basics.AddThemeConstantOverride("v_separation", 6);
        basics.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        foreach (var (key, label, action) in new (string, string, Action)[]
                 {
                     ("battle_defend", "防御", () => Submit(new Defend(_session!.AwaitingPlayer!.Id))),
                     ("battle_meditate", "调息", () => Submit(new Meditate(_session!.AwaitingPlayer!.Id))),
                     ("battle_item", "物品", CycleItem),
                     ("battle_swap", "换位", EnterSwap),
                     ("battle_retreat", "撤退", () => Submit(new Retreat(_session!.AwaitingPlayer!.Id))),
                 })
        {
            var button = Ui.Button(label, UiTheme.DarkButton, () =>
            {
                if (AcceptingCommand)
                {
                    action();
                }
            });
            button.CustomMinimumSize = new Vector2(86, 34);
            button.AddThemeFontSizeOverride("font_size", FontScale.Of(16));
            basics.AddChild(button);
            _basicButtons[key] = button;
        }

        _dock.AddChild(Ui.Row(UiPalette.SpaceL, actor, VerticalRule(), _slots, VerticalRule(), basics));
        BuildInfoCard();
        return _dock;
    }

    private static (ProgressBar, Label) Meter(string variation, out Control row, string label)
    {
        var bar = Ui.Bar(variation, 0, 1, 132);
        bar.CustomMinimumSize = new Vector2(132, 8);
        bar.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var text = Ui.Text("", UiTheme.DarkMutedLabel, 13);
        row = Ui.Row(6, Ui.Text(label, UiTheme.DarkLabel, 14), bar, Ui.MinSize(text, 70));
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
            _dockKey = null;
            ClearInfo();
            HideTargeting();
            return;
        }

        var session = _session!;
        var name = DisplayName(actor.Id);
        _actorName.Text = name;
        _actorState.Text = ready ? "" : "结算中";
        SetMeter(_actorHp, _actorHpText, actor.Hp, actor.Stats.MaxHp);
        SetMeter(_actorInner, _actorInnerText, actor.Inner, actor.Stats.MaxInner);

        foreach (var (key, button) in _basicButtons)
        {
            var reason = key switch
            {
                "battle_retreat" => _engine.Validate(session.State, new Retreat(actor.Id)),
                "battle_item" => session.State.Items.Count == 0 ? "没有可用物品" : null,
                _ => null,
            };
            button.Disabled = reason is not null;
            button.TooltipText = reason ?? "";
            if (key == "battle_item")
            {
                button.Text = $"物品 {session.State.Items.Values.Sum()}";
            }
        }

        // 招式卡只由（人物、各招能否施展及原因、冷却、键帽）决定：都没变就沿用现有卡面，选中态由 SelectSkill 另行同步。
        var slots = new List<(string Skill, string Key, string? Reason)> { (CoreIds.BasicAttack, KeyBindings.Label("battle_attack"), Usable(actor, CoreIds.BasicAttack)) };
        slots.AddRange(actor.Skills.Select((s, i) => (s, $"{i + 1}", Usable(actor, s))));
        var dockKey = actor.Id + "|" + string.Join(";", slots.Select(s => $"{s.Skill},{s.Key},{s.Reason},{actor.CooldownOf(s.Skill)}"));
        if (dockKey != _dockKey)
        {
            _dockKey = dockKey;
            Ui.ClearChildren(_slots);
            foreach (var (skill, key, reason) in slots)
            {
                _slots.AddChild(SkillSlot(actor, skill, key, reason));
            }
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

    private Button SkillSlot(BattleUnit actor, string skillId, string key, string? reason)
    {
        var def = _engine.Content.Skill(skillId);
        var cooldown = actor.CooldownOf(skillId);
        var name = _bundle.Name(skillId);
        var button = new Button
        {
            ThemeTypeVariation = UiTheme.CardButton, ToggleMode = true, ButtonGroup = _slotGroup,
            CustomMinimumSize = new Vector2(86, 80), Disabled = reason is not null,
            ButtonPressed = _mode == Mode.Skill && _skill == skillId,
        };
        button.SetMeta("skill", skillId);
        // 说明不用系统提示框：悬停时在格子上方弹出说明小签（与换招时同一张）。
        button.MouseEntered += () => HoverSlot(button, skillId);
        button.MouseExited += () => HoverSlot(null, null);

        var body = Ui.Column(2);
        body.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        body.Alignment = BoxContainer.AlignmentMode.Center;
        // 卡面用短名（“破刀式”），全名写在提示与下方说明行。
        var shortName = _bundle.ShortName(skillId);
        var glyph = Ui.Glyph(_bundle.Glyph(skillId), skillId == CoreIds.BasicAttack ? UiPalette.TextMuted : UiPalette.Trim, 34);
        glyph.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        var caption = Ui.Text(shortName, UiTheme.DarkLabel, 14);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        // 卡面只写消耗，冷却写在下方说明行里，免得三项挤出卡面。
        var costText = def.InnerCost == 0 && def.MomentumCost == 0 ? "无消耗"
            : string.Join(" ", new[] { def.InnerCost > 0 ? $"内{def.InnerCost}" : null, def.MomentumCost > 0 ? $"势{def.MomentumCost}" : null }.OfType<string>());
        var cost = Ui.Text(costText, UiTheme.DarkMutedLabel, 12);
        cost.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(glyph);
        body.AddChild(caption);
        body.AddChild(cost);
        button.AddChild(Ui.IgnoreMouse(body));
        // 数字键是招式格本身的标识（UI 规范保留），缩成左上角一个小字。
        var cap = Ui.Text(key, UiTheme.GiltLabel, 13);
        cap.AddThemeColorOverride("font_outline_color", UiPalette.Abyss);
        cap.AddThemeConstantOverride("outline_size", 4);
        Ui.Place(cap, 0, 0, 5, 2, 30, 20);
        button.AddChild(Ui.IgnoreMouse(cap));

        if (reason is not null)
        {
            var veil = new ColorRect { Color = UiPalette.Abyss with { A = 0.72f }, MouseFilter = MouseFilterEnum.Ignore };
            veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            button.AddChild(veil);
            var note = Ui.Text(cooldown > 0 ? $"冷却 {cooldown}" : reason.TrimEnd('。'), UiTheme.GiltLabel, cooldown > 0 ? 18 : 13);
            note.HorizontalAlignment = HorizontalAlignment.Center;
            note.VerticalAlignment = VerticalAlignment.Center;
            note.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
            note.AddThemeColorOverride("font_outline_color", UiPalette.Abyss);
            note.AddThemeConstantOverride("outline_size", 6);
            body.Modulate = new Color(1, 1, 1, 0.35f);
            button.AddChild(Ui.Place(note, 0, 0, 3, 0, 83, 80));
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
