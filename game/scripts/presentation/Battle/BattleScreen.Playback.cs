using Godot;
using WuxiaWorld.Domain.Combat;
using Side = WuxiaWorld.Domain.Combat.Side;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Battle;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>事件播放：逐条消费内核事件，更新单位画面、飘字、横幅与日志；一批播完按内核状态校正。</summary>
public sealed partial class BattleScreen
{
    private readonly Queue<BattleEvent> _queue = new();
    private bool _playing;
    private bool _skipping;
    private bool _needsSync;
    private float _speed = 1;
    private double _aiDelay;
    private bool _auto;

    private bool ToggleAuto()
    {
        _auto = !_auto;
        Toast(_auto ? "自动战斗：开（P 关闭）" : "自动战斗：关");
        RefreshTopBar();
        return true;
    }

    private void ResetPlayback()
    {
        _queue.Clear();
        _playing = false;
        _needsSync = false;
        _roundOrder = [];
        _current = null;
        HideTargeting();
    }

    private void Enqueue(IReadOnlyList<BattleEvent> events)
    {
        foreach (var e in events)
        {
            _queue.Enqueue(e);
        }

        _needsSync = true;
        RefreshDock();
    }

    private bool ToggleSpeed()
    {
        _speed = _speed > 1 ? 1 : 2;
        RefreshTopBar();
        return true;
    }

    /// <summary>跳过：余下事件不播动画，直接落到内核状态。</summary>
    private bool SkipPlayback()
    {
        if (_queue.Count == 0)
        {
            return false;
        }

        _skipping = true;
        Pump();
        _skipping = false;
        return true;
    }

    /// <summary>每帧推进：播放队列 → 批次校正 → 轮到 AI 时让其行动 → 战斗结束时显示结算。</summary>
    private void Pump()
    {
        if (_session is null)
        {
            return;
        }

        while (!_playing && _queue.Count > 0)
        {
            var e = _queue.Dequeue();
            var duration = Play(e, animate: !_skipping && Motion.Enabled);
            if (duration > 0 && !_skipping && Motion.Enabled)
            {
                _playing = true;
                GetTree().CreateTimer(duration / _speed).Timeout += () => _playing = false;
            }
        }

        if (_playing || _queue.Count > 0)
        {
            return;
        }

        if (_needsSync)
        {
            _needsSync = false;
            SyncAll();
        }

        if (_session.Ended)
        {
            if (!_resultOpen && !_setupOpen)
            {
                ShowResult();
            }

            return;
        }

        if (_auto && AcceptingCommand)
        {
            if (!_skipping && Motion.Enabled && _aiDelay < 0.25 / _speed)
            {
                _aiDelay += GetProcessDeltaTime();
                return;
            }

            _aiDelay = 0;
            Submit(Domain.Combat.Ai.BattleAi.BestAttack(_engine, _session.State, _session.AwaitingPlayer!, skirmish: false));
            return;
        }

        if (_session.AwaitingAi)
        {
            // AI 行动前留一小段停顿，让玩家看清轮到谁；跳过与截图模式不停顿。
            if (!_skipping && Motion.Enabled && _aiDelay < 0.25 / _speed)
            {
                _aiDelay += GetProcessDeltaTime();
                return;
            }

            _aiDelay = 0;
            var result = _session.StepAi();
            if (result is not null)
            {
                Enqueue(result.Events);
            }
        }
    }

    /// <summary>播放一条事件并返回它占用的时长（秒，1 倍速）。</summary>
    private float Play(BattleEvent e, bool animate)
    {
        switch (e)
        {
            case RoundStarted r:
                _roundOrder = [.. r.Order];
                _current = null;
                RefreshTopBar();
                AddLog($"—— 第 {r.Round} 轮 ——");
                if (animate)
                {
                    Banner($"第 {r.Round} 轮");
                }

                return animate ? 0.5f : 0;

            case TurnStarted t:
                _current = t.Actor;
                RefreshTopBar();
                return 0.05f;

            case SkillUsed u:
            {
                var name = _bundle.Name(u.SkillId);
                AddLog($"{DisplayName(u.Actor)}{(u.IsReaction ? "反击，" : "")}使出“{name}”" + (u.Targets.Count == 1 && u.Targets[0] != u.Actor ? $"，指向{DisplayName(u.Targets[0])}。" : "。"));
                if (!_views.TryGetValue(u.Actor, out var actor) || !animate)
                {
                    return 0;
                }

                Float(actor, name, UiPalette.Gilt, 28, 0, rise: 50, above: true);
                var target = u.Targets.Count > 0 && _views.TryGetValue(u.Targets[0], out var tv) && tv.Side != actor.Side ? tv : null;
                if (target is not null)
                {
                    Lunge(actor, target);
                    return 0.42f;
                }

                Glow(actor);
                return 0.3f;
            }

            case Damaged d:
            {
                var view = _views[d.Target];
                view.Hp = d.HpAfter;
                view.Refresh(animate);
                AddLog($"{DisplayName(d.Target)}受到 {d.Amount} 点{(d.Kind == DamageKind.Internal ? "内劲" : "")}伤害{(d.Crit ? "（暴击）" : "")}。");
                if (animate)
                {
                    Flash(view);
                    Float(view, $"−{d.Amount}", d.Crit ? UiPalette.Gilt : UiPalette.Surface, d.Crit ? 64 : 52, 0);
                }

                return animate ? 0.3f : 0;
            }

            case Missed m:
                AddLog($"{DisplayName(m.Target)}闪开了{DisplayName(m.Source)}的“{_bundle.Name(m.SkillId)}”。");
                if (animate)
                {
                    Float(_views[m.Target], "闪避", UiPalette.TextOnDarkMuted, 34, 0);
                }

                return animate ? 0.25f : 0;

            case StanceDamaged s:
                _views[s.Target].Stance = s.StanceAfter;
                _views[s.Target].Refresh();
                if (animate)
                {
                    Float(_views[s.Target], $"架势 −{s.Amount}", UiPalette.Gilt, 24, 0.12f);
                }

                return animate ? 0.1f : 0;

            case StanceBroken b:
                AddLog($"{DisplayName(b.Target)}架势被破，露出破绽！");
                if (animate)
                {
                    Float(_views[b.Target], "破绽！", UiPalette.Warm.Lightened(0.3f), 44, 0.1f);
                }

                return animate ? 0.35f : 0;

            case Healed h:
                _views[h.Target].Hp = h.HpAfter;
                _views[h.Target].Refresh();
                AddLog($"{DisplayName(h.Target)}恢复 {h.Amount} 点气血。");
                if (animate)
                {
                    Float(_views[h.Target], $"+{h.Amount}", UiPalette.Trim.Lightened(0.3f), 48, 0);
                }

                return animate ? 0.3f : 0;

            case InnerChanged c:
                _views[c.Unit].Inner = c.InnerAfter;
                _views[c.Unit].Refresh();
                if (animate && c.Delta > 0)
                {
                    Float(_views[c.Unit], $"内力 +{c.Delta}", UiPalette.Accent.Lightened(0.4f), 24, 0.1f);
                }

                return 0;

            case StanceRestored s:
                _views[s.Unit].Stance = s.StanceAfter;
                _views[s.Unit].Refresh();
                return 0;

            case StatusApplied a:
            {
                var view = _views[a.Target];
                view.SetStatus(a.StatusId, a.Stacks);
                view.Refresh();
                if (a.StatusId is not (CoreIds.Defend or CoreIds.Meditating or CoreIds.Charging or CoreIds.Broken or CoreIds.ControlGuard))
                {
                    var harmful = _engine.Content.Status(a.StatusId).Harmful;
                    AddLog($"{DisplayName(a.Target)}{(harmful ? "陷入" : "获得")}“{_bundle.Name(a.StatusId)}”。");
                    if (animate)
                    {
                        Float(view, _bundle.Name(a.StatusId), harmful ? UiPalette.Warm.Lightened(0.4f) : UiPalette.Trim.Lightened(0.3f), 26, 0.2f);
                    }
                }

                return animate ? 0.08f : 0;
            }

            case StatusResisted r:
                AddLog($"{DisplayName(r.Target)}抵住了“{_bundle.Name(r.StatusId)}”。");
                if (animate)
                {
                    Float(_views[r.Target], "抵住", UiPalette.TextOnDarkMuted, 26, 0.2f);
                }

                return animate ? 0.15f : 0;

            case StatusRemoved r:
                _views[r.Target].RemoveStatus(r.StatusId);
                _views[r.Target].Refresh();
                if (r.Reason == StatusEndReason.Cleansed)
                {
                    AddLog($"{DisplayName(r.Target)}的“{_bundle.Name(r.StatusId)}”被驱散。");
                }
                else if (r.StatusId == CoreIds.Broken)
                {
                    AddLog($"{DisplayName(r.Target)}架势回稳。");
                }

                if (r.StatusId == CoreIds.Charging)
                {
                    RefreshMarks();
                }

                return 0;

            case ControlAccumulated c:
                AddLog($"{DisplayName(c.Target)}穴道受震（{c.Meter}/{c.Threshold}），再中一次即被制住。");
                if (animate)
                {
                    Float(_views[c.Target], $"受震 {c.Meter}/{c.Threshold}", UiPalette.Gilt, 26, 0.2f);
                }

                return animate ? 0.2f : 0;

            case ChargeStarted c:
            {
                RefreshMarks();
                AddLog($"{DisplayName(c.Actor)}开始蓄力：下次行动将施展“{_bundle.Name(c.SkillId)}”！");
                if (animate)
                {
                    Banner($"{DisplayName(c.Actor)} 蓄力：{_bundle.Name(c.SkillId)}", UiPalette.Warm.Lightened(0.35f));
                    Glow(_views[c.Actor]);
                }

                return animate ? 0.6f : 0;
            }

            case ChargeInterrupted c:
                RefreshMarks();
                AddLog($"{DisplayName(c.Actor)}的蓄力被打断！");
                if (animate)
                {
                    Float(_views[c.Actor], "打断！", UiPalette.Gilt, 46, 0.1f);
                }

                return animate ? 0.4f : 0;

            case GuardIntercepted g:
                AddLog($"{DisplayName(g.Guardian)}挡在{DisplayName(g.Protected)}身前。");
                if (animate)
                {
                    Float(_views[g.Guardian], "护援", UiPalette.Trim.Lightened(0.3f), 34, 0);
                    Glow(_views[g.Guardian]);
                }

                return animate ? 0.3f : 0;

            case Parried p:
                _views[p.Unit].Stance = p.StanceAfter;
                _views[p.Unit].Refresh();
                AddLog($"{DisplayName(p.Unit)}招架，回稳架势并积势。");
                if (animate)
                {
                    Float(_views[p.Unit], "招架", UiPalette.Gilt, 32, 0.15f);
                }

                return animate ? 0.2f : 0;

            case Defended d:
                AddLog($"{DisplayName(d.Actor)}摆出防御。");
                if (animate)
                {
                    Float(_views[d.Actor], "防御", UiPalette.Trim.Lightened(0.3f), 30, 0, above: true);
                }

                return animate ? 0.3f : 0;

            case Meditated m:
                AddLog($"{DisplayName(m.Actor)}调息回气（受伤加重至下次行动）。");
                if (animate)
                {
                    Float(_views[m.Actor], "调息", UiPalette.Accent.Lightened(0.4f), 30, 0, above: true);
                    Glow(_views[m.Actor]);
                }

                return animate ? 0.35f : 0;

            case ItemUsed i:
                AddLog($"{DisplayName(i.Actor)}使用{_bundle.Name(i.ItemId)}（余 {i.Remaining}）。");
                if (animate)
                {
                    Float(_views[i.Actor], _bundle.Name(i.ItemId), UiPalette.Trim.Lightened(0.3f), 28, 0, above: true);
                }

                return animate ? 0.3f : 0;

            case Swapped s:
            {
                AddLog($"{DisplayName(s.Actor)}换到{(s.To.Row == 0 ? "前排" : "后排")}。");
                _views[s.Actor].Position = s.To;
                _views[s.Actor].Layout(animate);
                if (s.Other is { } other)
                {
                    _views[other].Position = s.From;
                    _views[other].Layout(animate);
                }

                SortFigures();

                return animate ? 0.35f : 0;
            }

            case RetreatAttempted r:
                AddLog(r.Success ? "撤退成功。" : $"撤退失败（成功率 {r.ChanceBp / 100}%）。");
                if (animate)
                {
                    Banner(r.Success ? "全身而退" : "未能脱身");
                }

                return animate ? 0.5f : 0;

            case UnitDowned d:
                _views[d.Unit].Hp = 0;
                _views[d.Unit].Refresh(animate);
                AddLog($"{DisplayName(d.Unit)}倒下。");
                return animate ? 0.4f : 0;

            case PhaseTriggered p:
                AddLog(_bundle.Name(p.PhaseId));
                if (animate)
                {
                    Banner(_bundle.Name(p.PhaseId), UiPalette.Gilt);
                }

                return animate ? 0.9f : 0;

            case UnitSpawned s:
            {
                var view = AddView(_session!.State.Unit(s.Unit));
                SortFigures();
                AddLog($"{DisplayName(s.Unit)}加入战斗。");
                if (animate)
                {
                    view.Standee.Modulate = Colors.Transparent;
                    view.Standee.CreateTween().TweenProperty(view.Standee, "modulate", Colors.White, 0.4f);
                }

                return animate ? 0.4f : 0;
            }

            case LevelRaised l:
                _views[l.Unit].ShowMark(new IntentMark("闸", $"{(l.Released ? 0 : l.Level)}", UiPalette.Accent, Pulse: !l.Released && l.Level == l.Max - 1));
                AddLog(l.Released ? $"{DisplayName(l.Unit)}开闸：水势冲向我方前排！" : $"{DisplayName(l.Unit)}：{_bundle.Name(l.StatusId)} {l.Level}/{l.Max}。");
                if (animate)
                {
                    if (l.Released)
                    {
                        Banner("开闸！水势冲向前排", UiPalette.Accent.Lightened(0.45f));
                    }
                    else
                    {
                        Float(_views[l.Unit], $"{_bundle.Name(l.StatusId)} {l.Level}/{l.Max}", UiPalette.Accent.Lightened(0.45f), 30, 0);
                    }
                }

                return animate ? (l.Released ? 0.7f : 0.35f) : 0;

            case ActionSkipped s:
                AddLog($"{DisplayName(s.Actor)}被{_bundle.Name(s.StatusId)}，无法行动。");
                if (animate)
                {
                    Float(_views[s.Actor], "无法行动", UiPalette.Warm.Lightened(0.4f), 32, 0, above: true);
                }

                return animate ? 0.45f : 0;

            case StatusTicked t:
                _views[t.Unit].Hp = t.HpAfter;
                _views[t.Unit].Refresh(animate);
                AddLog($"{DisplayName(t.Unit)}因{_bundle.Name(t.StatusId)}失去 {t.Damage} 点气血。");
                if (animate)
                {
                    Float(_views[t.Unit], $"−{t.Damage}", UiPalette.Warm.Lightened(0.35f), 40, 0);
                }

                return animate ? 0.3f : 0;

            case MomentumChanged:
                return 0;

            case BattleEnded end:
                AddLog(end.Outcome switch
                {
                    BattleOutcome.Victory => "战斗胜利。",
                    BattleOutcome.Defeat => "我方全员失去战斗能力。",
                    _ => "脱离战斗。",
                });
                return animate ? 0.4f : 0;

            default:
                return 0;
        }
    }

    // ── 动作占位（正式骨骼动作待 M3-02） ─────────────────────

    private void Lunge(UnitView actor, UnitView target)
    {
        var standee = actor.Standee;
        var home = standee.Position;
        var toward = target.Standee.Position.X > home.X ? 1 : -1;
        var reach = Math.Abs(target.Standee.Position.X - home.X) - target.Standee.Size.X * 0.9f;
        var strike = home + new Vector2(toward * Math.Max(40, reach), (target.Standee.Position.Y - home.Y) * 0.4f);
        var tween = standee.CreateTween();
        tween.TweenProperty(standee, "position", strike, 0.16f / _speed).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        tween.TweenInterval(0.12f / _speed);
        tween.TweenProperty(standee, "position", home, 0.22f / _speed).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }

    private void Flash(UnitView view)
    {
        var standee = view.Standee;
        standee.Modulate = new Color(2.2f, 2.2f, 2.2f);
        standee.CreateTween().TweenProperty(standee, "modulate", Colors.White, 0.25f / _speed);
        var origin = view.FeetNow;
        var h = standee.Height;
        var home = new Vector2(origin.X - h * 0.275f, origin.Y - h);
        var shake = standee.CreateTween();
        for (var i = 0; i < 4; i++)
        {
            shake.TweenProperty(standee, "position", home + new Vector2(i % 2 == 0 ? 8 : -8, 0), 0.03f / _speed);
        }

        shake.TweenProperty(standee, "position", home, 0.03f / _speed);
    }

    private void Glow(UnitView view)
    {
        var standee = view.Standee;
        var tween = standee.CreateTween();
        tween.TweenProperty(standee, "modulate", new Color(1.35f, 1.35f, 1.2f), 0.12f / _speed);
        tween.TweenProperty(standee, "modulate", Colors.White, 0.25f / _speed);
    }

    private void Float(UnitView anchor, string text, Color color, int size, float delay, float rise = 70, bool above = false)
    {
        var label = Ui.Text(text, UiTheme.DisplayLabel, size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", UiPalette.Abyss with { A = 0.85f });
        label.AddThemeConstantOverride("outline_size", 8);
        label.MouseFilter = MouseFilterEnum.Ignore;
        var frame = anchor.Frame;
        label.Position = new Vector2(frame.Position.X + frame.Size.X * 0.5f - 40, frame.Position.Y + (above ? -20 : frame.Size.Y * 0.2f) - delay * 200);
        label.Modulate = new Color(1, 1, 1, 0);
        _fx.AddChild(label);
        var tween = label.CreateTween().SetParallel();
        tween.TweenProperty(label, "modulate:a", 1f, 0.08f / _speed).SetDelay(delay / _speed);
        tween.TweenProperty(label, "position:y", label.Position.Y - rise, 0.8f / _speed).SetDelay(delay / _speed)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tween.Chain().TweenProperty(label, "modulate:a", 0f, 0.25f / _speed);
        tween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }

    private void Banner(string text, Color? color = null)
    {
        _banner.Text = text;
        _banner.AddThemeColorOverride("font_color", color ?? UiPalette.TextOnDark);
        var tween = _banner.CreateTween();
        _banner.Modulate = Colors.Transparent;
        tween.TweenProperty(_banner, "modulate", Colors.White, 0.15f / _speed);
        tween.TweenInterval(0.55f / _speed);
        tween.TweenProperty(_banner, "modulate", Colors.Transparent, 0.3f / _speed);
    }
}
