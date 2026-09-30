using Godot;
using WuxiaWorld.Domain.Combat;
using Side = WuxiaWorld.Domain.Combat.Side;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Domain.Common;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Battle;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;
using Cell = WuxiaWorld.Domain.Combat.Definitions.Position;

public sealed partial class BattleScreen
{
    private enum Mode
    {
        Skill,
        Item,
        Swap,
    }

    private Mode _mode = Mode.Skill;
    private string? _skill;
    private string? _skillActor;
    private string? _item;
    private readonly List<string> _targets = [];
    private readonly List<Position> _cells = [];
    private int _targetIndex;

    private readonly RangeMarker _range = new() { Visible = false };
    private PanelContainer _estimate = null!;

    /// <summary>当前预估的各行与所指单位（换位到空位时为 null）；鼠标停在该单位上时，预估并入其悬停小窗，头顶小签隐去。</summary>
    private List<(string Text, string Variation, int Size)> _estimateLines = [];
    private string? _estimateFor;

    private void BuildTargeting()
    {
        _ground.AddChild(_range);
        _estimate = new PanelContainer { ThemeTypeVariation = UiTheme.GlassPanel, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _fx.AddChild(_estimate);
    }

    private void HideTargeting()
    {
        _range.Clear();
        foreach (var view in _views.Values)
        {
            view.Standee.Outline = null;
        }

        _estimate.Visible = false;
        if (_estimateLines.Count > 0)
        {
            _estimateLines = [];
            _estimateFor = null;
            RebuildPopup();
        }
    }

    /// <summary>描出受影响者的轮廓，并在脚下铺范围标记（群体招式铺整片范围）。</summary>
    private void MarkTargets(BattleUnit actor, IReadOnlyList<BattleUnit> affected, TargetRule? rule, BattleUnit focus)
    {
        foreach (var view in _views.Values)
        {
            view.Standee.Outline = null;
        }

        var side = focus.Side;
        var tone = side == actor.Side ? UiPalette.Trim.Lightened(0.35f) : UiPalette.Gilt;
        foreach (var unit in affected)
        {
            _views[unit.Id].Standee.Outline = tone;
        }

        var state = _session!.State;
        var hits = affected.Where(u => !u.IsDown).Select(u => u.Position).ToHashSet();
        IEnumerable<Cell> area = rule switch
        {
            TargetRule.ReachableRowEnemies => Enumerable.Range(0, Cell.Slots).Select(s => new Cell(Targeting.ReachableRow(state, side), s)),
            TargetRule.ColumnEnemies => Enumerable.Range(0, Cell.Rows).Select(r => new Cell(r, focus.Position.Slot)),
            TargetRule.AllEnemies or TargetRule.AllAllies => Enumerable.Range(0, Cell.Rows * Cell.Slots).Select(i => new Cell(i / Cell.Slots, i % Cell.Slots)),
            _ => hits,
        };
        _range.Show(side, area.Select(c => (c, hits.Contains(c))), tone);
    }

    private BattleUnit? Actor => _session?.AwaitingPlayer;

    private void SelectSkill(string skill)
    {
        if (Actor is not { } actor)
        {
            return;
        }

        var reason = Usable(actor, skill);
        if (reason is not null)
        {
            Toast($"{_bundle.Name(skill)}：{reason}");
            return;
        }

        var previous = _targets.Count > _targetIndex && _targetIndex >= 0 ? _targets[_targetIndex] : null;
        _mode = Mode.Skill;
        _skill = skill;
        _item = null;
        _targets.Clear();
        var def = _engine.Content.Skill(skill);
        _targets.AddRange(Targeting.Candidates(_session!.State, actor, def.TargetRule).Select(u => u.Id));
        _targetIndex = Math.Max(0, previous is null ? DefaultTargetIndex(def) : _targets.IndexOf(previous));
        if (_targetIndex < 0 || previous is null)
        {
            _targetIndex = DefaultTargetIndex(def);
        }

        foreach (var child in _slots.GetChildren().OfType<Button>())
        {
            child.SetPressedNoSignal(child.HasMeta("skill") && child.GetMeta("skill").AsString() == skill);
        }

        var desc = _bundle.Describe(skill);
        _info.Text = $"{_bundle.Name(skill)}　·　{CostText(def)}　·　{RuleText(def.TargetRule)}" + (desc is null ? "" : $"　·　{desc}");
        RefreshTargeting();
    }

    /// <summary>默认目标：敌方选首领或气血最少者，己方选气血比例最低者。</summary>
    private int DefaultTargetIndex(SkillDefinition def)
    {
        if (_targets.Count == 0)
        {
            return 0;
        }

        var state = _session!.State;
        var units = _targets.Select(state.Unit).ToList();
        var best = def.TargetsEnemies
            ? units.Where(u => !u.Template.HasTag("mechanism")).OrderByDescending(u => u.Template.HasTag("boss")).ThenBy(u => u.Hp).FirstOrDefault()
            : units.OrderBy(u => (long)u.Hp * Bp.One / Math.Max(1, u.Stats.MaxHp)).FirstOrDefault();
        return Math.Max(0, units.IndexOf(best ?? units[0]));
    }

    private void CycleTarget(int step)
    {
        var count = _mode == Mode.Swap ? _cells.Count : _targets.Count;
        if (count == 0 || NeedsNoTarget())
        {
            return;
        }

        _targetIndex = ((_targetIndex + step) % count + count) % count;
        RefreshTargeting();
    }

    private bool NeedsNoTarget() => _mode == Mode.Skill && _skill is not null
        && !Targeting.NeedsChosenTarget(_engine.Content.Skill(_skill).TargetRule);

    private void CycleItem()
    {
        if (Actor is not { } actor || _session!.State.Items.Count == 0)
        {
            Toast("没有可用物品。");
            return;
        }

        var items = _session.State.Items.Keys.ToList();
        var next = _mode == Mode.Item && _item is not null ? (items.IndexOf(_item) + 1) % items.Count : 0;
        _mode = Mode.Item;
        _item = items[next];
        _targets.Clear();
        var def = _engine.Content.Item(_item);
        _targets.AddRange(Targeting.Candidates(_session.State, actor, def.TargetRule).Select(u => u.Id));
        _targetIndex = Math.Max(0, _targets.IndexOf(actor.Id));
        foreach (var child in _slots.GetChildren().OfType<Button>())
        {
            child.SetPressedNoSignal(false);
        }

        _info.Text = $"物品：{_bundle.Name(_item)}（余 {_session.State.Items[_item]}）　·　再按 I 换一种，Tab 换目标，Enter 使用，Esc 取消";
        RefreshTargeting();
    }

    private void EnterSwap()
    {
        if (Actor is not { } actor)
        {
            return;
        }

        _mode = Mode.Swap;
        _cells.Clear();
        for (var row = 0; row < Cell.Rows; row++)
        {
            for (var slot = 0; slot < Cell.Slots; slot++)
            {
                var p = new Position(row, slot);
                if (p != actor.Position)
                {
                    _cells.Add(p);
                }
            }
        }

        _targetIndex = 0;
        foreach (var child in _slots.GetChildren().OfType<Button>())
        {
            child.SetPressedNoSignal(false);
        }

        _info.Text = "换位：消耗本次行动，移到空位或与同伴互换　·　Tab 选位置，Enter 确认，Esc 取消";
        RefreshTargeting();
    }

    private void Confirm()
    {
        if (Actor is not { } actor)
        {
            return;
        }

        switch (_mode)
        {
            case Mode.Skill when _skill is not null:
                Submit(new UseSkill(actor.Id, _skill, NeedsNoTarget() || _targets.Count == 0 ? null : _targets[_targetIndex]));
                break;
            case Mode.Item when _item is not null && _targets.Count > 0:
                Submit(new UseItem(actor.Id, _item, _targets[_targetIndex]));
                break;
            case Mode.Swap when _cells.Count > 0:
                Submit(new Swap(actor.Id, _cells[_targetIndex].Row, _cells[_targetIndex].Slot));
                break;
        }
    }

    /// <summary>光标下的人物：按绘制次序由前往后，取第一个轮廓命中者；倒下者不挡。</summary>
    private string? PickUnit(Vector2 globalPoint)
    {
        for (var i = _figures.GetChildCount() - 1; i >= 0; i--)
        {
            if (_figures.GetChild(i) is not BattleStandee standee)
            {
                continue;
            }

            var view = _views.Values.FirstOrDefault(v => v.Standee == standee);
            if (view is null || view.Down)
            {
                continue;
            }

            if (standee.HitTest(standee.GetGlobalTransform().AffineInverse() * globalPoint))
            {
                return view.Id;
            }
        }

        return null;
    }

    private void OnFieldInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion motion:
            {
                var id = PickUnit(_hits.GetGlobalTransform() * motion.Position);
                var pickable = id is not null && AcceptingCommand && _mode != Mode.Swap && _targets.Contains(id);
                _hits.MouseDefaultCursorShape = pickable ? CursorShape.PointingHand : CursorShape.Arrow;
                ShowUnitPopup(id);
                if (id is not null)
                {
                    OnUnitHovered(id);
                }

                break;
            }

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                if (PickUnit(_hits.GetGlobalTransform() * click.Position) is { } picked)
                {
                    OnUnitClicked(picked, click.DoubleClick);
                }

                break;
        }
    }

    private void OnUnitClicked(string id, bool doubleClick)
    {
        if (!AcceptingCommand || _mode == Mode.Swap)
        {
            return;
        }

        var index = _targets.IndexOf(id);
        if (index < 0)
        {
            return;
        }

        if (index == _targetIndex || doubleClick)
        {
            _targetIndex = index;
            Confirm();
            return;
        }

        _targetIndex = index;
        RefreshTargeting();
    }

    /// <summary>
    /// 悬停即指向：单体招换目标；不需指定目标的群体招（横扫一排、全体）出招时不传目标，悬停只换预估所按的人，
    /// 预估数值随之改按此人计算并并入其悬停小窗。
    /// </summary>
    private void OnUnitHovered(string id)
    {
        if (!AcceptingCommand || _mode == Mode.Swap)
        {
            return;
        }

        var index = _targets.IndexOf(id);
        if (index >= 0 && index != _targetIndex)
        {
            _targetIndex = index;
            RefreshTargeting();
        }
    }

    private void RefreshTargeting()
    {
        if (Actor is not { } actor || !AcceptingCommand)
        {
            HideTargeting();
            return;
        }

        var lines = new List<(string Text, string Variation, int Size)>();
        Vector2 anchor;
        if (_mode == Mode.Swap)
        {
            if (_cells.Count == 0)
            {
                HideTargeting();
                return;
            }

            var cell = _cells[Math.Clamp(_targetIndex, 0, _cells.Count - 1)];
            var other = _session!.State.Units.Find(u => u.Side == Side.Ally && u.Position == cell);
            foreach (var view in _views.Values)
            {
                view.Standee.Outline = view.Id == other?.Id || view.Id == actor.Id ? UiPalette.Trim.Lightened(0.35f) : null;
            }

            _range.Show(actor.Side, [(cell, true)], UiPalette.Trim.Lightened(0.35f));
            lines.Add(($"{(cell.Row == 0 ? "前排" : "后排")}{"远中近"[cell.Slot]}位　" + (other is null ? "空位" : $"与{DisplayName(other.Id)}互换"), UiTheme.DarkLabel, 18));
            lines.Add((cell.Row == 0 ? "前排：护住后排，先挨近身攻击" : "后排：前排有人时近身攻击够不着", UiTheme.DarkMutedLabel, 15));
            anchor = other is not null ? _views[other.Id].HeadAnchor : UnitView.Feet(actor.Side, cell) - new Vector2(0, 320);
            _estimateFor = other?.Id;
        }
        else
        {
            if (_targets.Count == 0)
            {
                HideTargeting();
                return;
            }

            _targetIndex = Math.Clamp(_targetIndex, 0, _targets.Count - 1);
            var target = _session!.State.Unit(_targets[_targetIndex]);
            anchor = _views[target.Id].HeadAnchor;
            _estimateFor = target.Id;
            if (_mode == Mode.Skill && _skill is not null)
            {
                var rule = _engine.Content.Skill(_skill).TargetRule;
                var affected = Targeting.Resolve(_session.State, actor, rule, target.Id) ?? [target];
                MarkTargets(actor, affected, rule, target);
            }
            else
            {
                MarkTargets(actor, [target], null, target);
            }

            if (_mode == Mode.Item && _item is not null)
            {
                foreach (var e in _engine.Content.Item(_item).Effects)
                {
                    lines.Add((EffectLine(e), UiTheme.DarkLabel, 18));
                }
            }
            else if (_skill is not null)
            {
                SkillLines(actor, target, lines);
            }
        }

        lines.RemoveAll(l => l.Text.Length == 0);
        _estimateLines = lines;
        Ui.ClearChildren(_estimate);
        RebuildPopup();
        if (lines.Count == 0)
        {
            return;
        }

        var column = Ui.Column(2);
        foreach (var (text, variation, size) in lines)
        {
            var label = Ui.Text(text, variation, size);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            column.AddChild(label);
        }

        _estimate.AddChild(column);
        // 预估挂在目标头顶（标记圆印之上）：招式与目标已由招式栏高亮和轮廓描边表明，这里只给数值与需要留意的后果。
        _estimate.ResetSize();
        var box = _estimate.Size;
        _estimate.Position = new Vector2(
            Math.Clamp(anchor.X - box.X / 2, 16, _fx.Size.X - box.X - 16),
            Math.Max(anchor.Y - box.Y - 6, 132));
    }

    private void SkillLines(BattleUnit actor, BattleUnit target, List<(string, string, int)> lines)
    {
        var def = _engine.Content.Skill(_skill!);
        var group = !Targeting.NeedsChosenTarget(def.TargetRule) || def.TargetRule == TargetRule.ColumnEnemies;
        if (def.TargetRule == TargetRule.Self)
        {
            // 对自身的招式：说明已在招式栏下方，头顶不再重复。
            return;
        }

        // 招式名与单个目标已由招式栏高亮和轮廓描边表明，只有群体招式标出波及人数。
        var affected = Targeting.Resolve(_session!.State, actor, def.TargetRule, target.Id)?.Count ?? 1;
        if (group)
        {
            lines.Add((def.TargetRule == TargetRule.ColumnEnemies ? $"所在一列（{affected} 人）" : $"{RuleText(def.TargetRule)}（{affected} 人）", UiTheme.GiltLabel, 16));
        }

        if (def.Charged)
        {
            lines.Add(("蓄力招：本次只蓄力，下次行动出手", UiTheme.DarkMutedLabel, 15));
        }

        var e = _engine.Estimate(_session!.State, actor.Id, def.Id, target.Id);
        if (e is null)
        {
            return;
        }

        if (e.GuardedBy is { } guard)
        {
            lines.Add(($"⚠ {DisplayName(guard)}护着对方，单体攻击会被代受", UiTheme.DarkLabel, 16));
        }

        if (e.Hostile)
        {
            if (e.DamageMax > 0)
            {
                lines.Add(($"命中 {Bp.ToPercent(e.HitBp)}%　伤害 {e.DamageMin}–{e.DamageMax}", UiTheme.DarkLabel, 19));
                lines.Add(($"暴击 {Bp.ToPercent(e.CritBp)}% 至多 {e.CritDamageMax}", UiTheme.DarkMutedLabel, 15));
            }
            else
            {
                lines.Add(($"命中 {Bp.ToPercent(e.HitBp)}%", UiTheme.DarkLabel, 19));
            }

            if (e.Stance > 0)
            {
                lines.Add(($"削架势 {e.Stance}" + (e.Breaks ? "　· 将打出破绽" : $"（余 {_session.State.Unit(e.TargetId).Stance}）"), e.Breaks ? UiTheme.GiltLabel : UiTheme.DarkMutedLabel, 15));
            }

            if (_session.State.Unit(e.TargetId).HasStatus(CoreIds.Charging) && (e.Breaks || def.Effects.Any(x => x.Status == CoreIds.Stunned)))
            {
                lines.Add(("可打断对方蓄力", UiTheme.GiltLabel, 16));
            }
        }
        else if (e.Heal > 0)
        {
            lines.Add(($"疗伤 +{e.Heal}", UiTheme.DarkLabel, 19));
        }
    }

    private static string EffectLine(EffectDefinition e) => e.Type switch
    {
        EffectType.Heal => $"恢复气血 {e.Amount}",
        EffectType.RestoreInner => $"恢复内力 {e.Amount}",
        EffectType.Cleanse => $"驱散{(e.Tag == "bleed" ? "流血" : "负面状态")} {e.Count} 个",
        EffectType.RestoreStance => $"回架势 {e.Amount}",
        _ => "",
    };
}
