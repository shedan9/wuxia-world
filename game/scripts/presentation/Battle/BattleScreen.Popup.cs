using Godot;
using WuxiaWorld.Domain.Combat;
using Side = WuxiaWorld.Domain.Combat.Side;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Battle;

using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 头顶标记与悬停信息小窗。场上不常驻状态文字：头顶只留一枚圆印（蓄力“蓄”、被首领单体大招盯上“危”、机关“闸”带层数），
/// 鼠标停在人物轮廓、头顶血条或圆印上时，人物右侧弹出小窗（右边放不下时在左侧），列出数值、速度、意图全文与每个状态的层数、剩余次数和说明；
/// 停在当前招式所指的人身上时，出招预估并入小窗顶部，头顶预估小签隐去。
/// </summary>
public sealed partial class BattleScreen
{
    private PanelContainer _popup = null!;
    private string? _popupUnit;

    /// <summary>鼠标正停在其血条或圆印上的单位（离开场地拾取层时，不因此收起它的小窗）。</summary>
    private string? _barHover;

    private void BuildPopup()
    {
        // 实底：小窗会盖在别的人物与头顶圆印上，半透明会让底下的字透出来。
        _popup = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _popup.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            FillA = UiPalette.Abyss with { A = 0.96f }, FillB = UiPalette.PanelDark with { A = 0.96f }, Ragged = 1.2f, Seed = 71,
            Border = UiPalette.Gilt with { A = 0.55f }, BorderWidth = 1.2f, Brush = true,
        }.Margins(16, 12));
        _fx.AddChild(_popup);
    }

    /// <summary>挂到每名单位的状态条与头顶圆印上：悬停出小窗并当作指向该单位，点击等同点人物。</summary>
    private void HookHover(UnitView view)
    {
        var id = view.Id;
        foreach (var control in new[] { view.Hud, view.Intent })
        {
            control.MouseEntered += () =>
            {
                _barHover = id;
                ShowUnitPopup(id);
                OnUnitHovered(id);
            };
            control.MouseExited += () =>
            {
                if (_barHover == id)
                {
                    _barHover = null;
                }

                if (_popupUnit == id)
                {
                    ShowUnitPopup(null);
                }
            };
            control.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
                {
                    OnUnitClicked(id, click.DoubleClick);
                }
            };
        }
    }

    // ── 头顶标记 ─────────────────────────────────────────

    /// <summary>按内核状态重设全部头顶标记。</summary>
    private void RefreshMarks()
    {
        if (_session is null)
        {
            return;
        }

        var state = _session.State;
        var marked = Marked(state);
        foreach (var unit in state.Units)
        {
            if (_views.TryGetValue(unit.Id, out var view))
            {
                view.ShowMark(MarkFor(unit, marked));
            }
        }

        RebuildPopup();
    }

    /// <summary>被蓄力单体招指着的人：施招者 ID → 目标 ID。</summary>
    private Dictionary<string, string> Marked(BattleState state)
    {
        var marked = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var u in state.Units.Where(u => !u.IsDown))
        {
            if (u.FindStatus(CoreIds.Charging) is { ChargedSkill: { } skill, ChargedTarget: { } target }
                && Targeting.NeedsChosenTarget(_engine.Content.Skill(skill).TargetRule))
            {
                marked[target] = u.Id;
            }
        }

        return marked;
    }

    private IntentMark? MarkFor(BattleUnit unit, Dictionary<string, string> marked)
    {
        if (unit.IsDown)
        {
            return null;
        }

        if (unit.HasStatus(CoreIds.Charging))
        {
            return new IntentMark("蓄", null, UiPalette.Warm, Pulse: true);
        }

        if (MechanismLevel(unit) is { } level)
        {
            return new IntentMark("闸", $"{level.Current}", UiPalette.Accent, Pulse: level.Current == level.Max - 1);
        }

        return marked.ContainsKey(unit.Id) ? new IntentMark("危", null, UiPalette.Warm, Pulse: true) : null;
    }

    private (string Skill, string Status, int Current, int Max)? MechanismLevel(BattleUnit unit)
    {
        if (!unit.Template.HasTag("mechanism") || unit.Skills.Count == 0)
        {
            return null;
        }

        var effect = _engine.Content.Skill(unit.Skills[0]).Effects.FirstOrDefault(e => e.Type == EffectType.RaiseLevel);
        return effect?.Status is { } status
            ? (unit.Skills[0], status, unit.StacksOf(status), _engine.Content.Status(status).MaxStacks)
            : null;
    }

    // ── 悬停小窗 ─────────────────────────────────────────

    private void ShowUnitPopup(string? id)
    {
        if (id == _popupUnit)
        {
            return;
        }

        _popupUnit = id;
        RebuildPopup();
    }

    private void RebuildPopup()
    {
        if (_popupUnit is null || _session?.State.TryUnit(_popupUnit) is not { IsDown: false } unit || !_views.TryGetValue(unit.Id, out var view))
        {
            _popup.Visible = false;
            SyncEstimateTag();
            return;
        }

        Ui.ClearChildren(_popup);
        var column = Ui.Column(4);
        column.CustomMinimumSize = new Vector2(360, 0);
        var side = unit.Side == Side.Ally ? "我方" : unit.Template.HasTag("boss") ? "首领" : unit.Template.HasTag("mechanism") ? "机关" : "敌方";
        column.AddChild(Ui.Row(UiPalette.SpaceS, Ui.Text(view.Name, UiTheme.GiltLabel, 20), Ui.Text(side, UiTheme.DarkMutedLabel, 15)));

        // 此人正是当前招式所指：预估并入小窗，放在名字下方、数值之前（出手前最要紧），头顶小签随之隐去。
        if (_estimateFor == unit.Id && _estimateLines.Count > 0)
        {
            foreach (var (text, variation, fontSize) in _estimateLines)
            {
                column.AddChild(Ui.Text(text, variation, fontSize));
            }

            column.AddChild(Ui.Rule(dark: true));
        }

        var stats = $"气血 {unit.Hp} / {unit.Stats.MaxHp}";
        if (unit.Stats.MaxInner > 0)
        {
            stats += $"　内力 {unit.Inner} / {unit.Stats.MaxInner}";
        }

        column.AddChild(Ui.Text(stats, UiTheme.DarkLabel, 16));
        if (unit.Stats.MaxStance > 0)
        {
            column.AddChild(Ui.Text(unit.HasStatus(CoreIds.Broken)
                ? "架势已破：下一次受伤 +50%，不能反应，蓄力被打断"
                : $"架势 {unit.Stance} / {unit.Stats.MaxStance}（归零露出破绽）", UiTheme.DarkMutedLabel, 15));
        }

        column.AddChild(Ui.Text($"速度 {_engine.EffectiveSpeed(unit)}（身法 {unit.Template.Attributes.Agility}）", UiTheme.DarkMutedLabel, 15));

        foreach (var line in IntentLines(unit))
        {
            var label = Wrapped(line, UiTheme.DarkLabel, 16);
            label.AddThemeColorOverride("font_color", UiPalette.Warm.Lightened(0.45f));
            column.AddChild(label);
        }

        var shown = unit.Statuses.Where(s => s.StatusId != CoreIds.Charging && MechanismLevel(unit)?.Status != s.StatusId).ToList();
        if (shown.Count > 0)
        {
            column.AddChild(Ui.Rule(dark: true));
            foreach (var s in shown)
            {
                var def = _engine.Content.Status(s.StatusId);
                var head = (def.Harmful ? "▼ " : "◆ ") + _bundle.Name(s.StatusId) + (s.Stacks > 1 ? $" ×{s.Stacks}" : "")
                    + (s.Remaining > 0 ? $"　余 {s.Remaining} 次" : "");
                var title = Ui.Text(head, def.Harmful ? UiTheme.DarkLabel : UiTheme.GiltLabel, 16);
                if (def.Harmful)
                {
                    title.AddThemeColorOverride("font_color", UiPalette.Warm.Lightened(0.35f));
                }

                column.AddChild(title);
                if (_bundle.Describe(s.StatusId) is { } desc)
                {
                    column.AddChild(Wrapped(desc, UiTheme.DarkMutedLabel, 14));
                }
            }
        }

        _popup.AddChild(column);
        _popup.Visible = true;
        _popup.ResetSize();
        // 跟在人物右侧，与头顶血条齐平；右边放不下（后排敌人靠画面右缘）时翻到人物左侧。
        var size = _popup.GetCombinedMinimumSize();
        var frame = view.Frame;
        var x = frame.End.X + 12;
        if (x + size.X > 1904)
        {
            x = frame.Position.X - size.X - 12;
        }

        var y = Math.Clamp(frame.Position.Y - 16, 170, 800 - size.Y);
        _popup.Position = new Vector2(Math.Max(16, x), y);
        SyncEstimateTag();
    }

    /// <summary>头顶预估小签只在预估没有并入悬停小窗时显示。</summary>
    private void SyncEstimateTag()
    {
        _estimate.Visible = _estimateLines.Count > 0 && !(_popup.Visible && _popupUnit == _estimateFor);
    }

    private static Label Wrapped(string text, string variation, int size)
    {
        var label = Ui.Text(text, variation, size, wrap: true);
        label.CustomMinimumSize = new Vector2(360, 0);
        return label;
    }

    /// <summary>意图全文：自己在蓄力、自己被蓄力招指着、机关水位。</summary>
    private List<string> IntentLines(BattleUnit unit)
    {
        var lines = new List<string>();
        var state = _session!.State;
        if (unit.FindStatus(CoreIds.Charging) is { ChargedSkill: { } skill } charge)
        {
            var def = _engine.Content.Skill(skill);
            var target = charge.ChargedTarget is { } t && Targeting.NeedsChosenTarget(def.TargetRule) ? $"指向{DisplayName(t)}" : RuleText(def.TargetRule);
            lines.Add($"⚠ 蓄力“{_bundle.Name(skill)}”：下次行动{target}。破招或点穴可打断。");
        }

        if (Marked(state).TryGetValue(unit.Id, out var by) && state.Unit(by).FindStatus(CoreIds.Charging)?.ChargedSkill is { } aimed)
        {
            lines.Add($"⚠ {DisplayName(by)}的“{_bundle.Name(aimed)}”下次行动指向此人：可护援、换位、防御，或打断。");
        }

        if (MechanismLevel(unit) is { } level)
        {
            lines.Add($"“{_bundle.Name(level.Skill)}”：{_bundle.Name(level.Status)} {level.Current} / {level.Max}，满 {level.Max} 层冲击我方前排。");
        }

        return lines;
    }
}
