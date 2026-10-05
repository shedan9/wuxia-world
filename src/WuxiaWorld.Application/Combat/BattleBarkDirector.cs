using WuxiaWorld.Domain.Combat;
using WuxiaWorld.Domain.World;

namespace WuxiaWorld.Application.Combat;

/// <summary>喊声的播放档位：正常、倍速（只说关键句）、静默（跳过动画或快进时只记账、不说）。</summary>
public enum BarkMode
{
    Normal,
    Fast,
    Silent,
}

/// <summary>
/// 战斗喊声挑选（开发计划 M3-06“关键战斗语音”，架构文档 10.5“战斗语音按优先级和冷却控制”）。
/// 表现层按播放次序把内核事件逐条交给 <see cref="Observe"/>，得到此刻该说的那一句或 null；只读事件与单位的静态信息
/// （阵营、最大气血；状态可能已领先于播放进度，所以不读气血等动态值），不改变战果。
/// 规则：
/// <list type="bullet">
/// <item>候选限于本遭遇（或不限遭遇）且说话的单位在场；同一时机多句候选取优先级最高，同级时指定了本遭遇的句子优先（切磋的“承让”压过通用的“快去救人”），再按文件次序。</item>
/// <item>切磋点到为止：没有重伤句（认输线高于重伤线，跌到重伤线时已经收手）。</item>
/// <item>每句一场只说一次；招式句给了 <c>cooldown_rounds</c> 的，隔够轮数可再说。</item>
/// <item>普通句（优先级 1）同一人至少隔 <see cref="OrdinaryGapRounds"/> 轮才再说，免得经典人物每回合都开口。</item>
/// <item>同一人的回合里，后一句须比已说的那句优先级更高（出招、对方重伤、倒下接连发生时不连喊三句）。</item>
/// <item>倒下的人不再开口（除了倒下那一句）；胜利句从仍站着的我方单位里挑，最后出手的我方单位优先。</item>
/// <item>倍速只说优先级 3；静默只记账（一次性的句子照样算说过，免得退回正常速度后补喊旧事）。</item>
/// </list>
/// </summary>
public sealed class BattleBarkDirector
{
    /// <summary>气血跌到这一比例以下算重伤（万分比）。</summary>
    public const int LowHpBp = 3000;

    /// <summary>同一人两句普通句（优先级 1）之间至少隔的轮数。</summary>
    public const int OrdinaryGapRounds = 2;

    private readonly IReadOnlyList<BattleBarkDefinition> _lines;
    private readonly BattleState _state;
    private readonly Dictionary<string, int> _spokenRound = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _ordinaryRound = new(StringComparer.Ordinal);
    private readonly HashSet<string> _down = new(StringComparer.Ordinal);
    private readonly HashSet<string> _lowHp = new(StringComparer.Ordinal);
    private int _round;
    private int _turnPriority;
    private string? _lastAllyActor;

    public BattleBarkDirector(IEnumerable<BattleBarkDefinition> lines, BattleState state)
    {
        _state = state;
        _lines = lines.Where(l => l.Encounter is null || l.Encounter == state.EncounterId).ToList();
        foreach (var u in state.Units.Where(u => u.IsDown))
        {
            _down.Add(u.Id);
        }
    }

    /// <summary>开战的那一句（全场只挑一句）。</summary>
    public BattleBarkDefinition? Start(BarkMode mode) => Pick(BarkTrigger.BattleStart, mode, l => true);

    /// <summary>消费一条事件，返回此刻该说的喊声。</summary>
    public BattleBarkDefinition? Observe(BattleEvent e, BarkMode mode)
    {
        switch (e)
        {
            case RoundStarted r:
                _round = r.Round;
                _turnPriority = 0;
                return null;
            case TurnStarted:
                _turnPriority = 0;
                return null;
            case SkillUsed { IsReaction: false } u:
                if (_state.TryUnit(u.Actor)?.Side == Side.Ally)
                {
                    _lastAllyActor = u.Actor;
                }

                return Pick(BarkTrigger.Skill, mode, l => l.UnitId == u.Actor && l.Skill == u.SkillId);
            case ChargeStarted c:
                return Pick(BarkTrigger.Charge, mode, l => l.UnitId == c.Actor && (l.Skill is null || l.Skill == c.SkillId));
            case ChargeInterrupted c:
                return Pick(BarkTrigger.Interrupted, mode, l => l.UnitId == c.Actor);
            case PhaseTriggered p:
                return Pick(BarkTrigger.Phase, mode, l => l.Phase == p.PhaseId);
            case Damaged d when !_state.Spar && Wounded(d.Target, d.HpAfter):
                return Pick(BarkTrigger.LowHp, mode, l => l.UnitId == d.Target);
            case StatusTicked k when !_state.Spar && Wounded(k.Unit, k.HpAfter):
                return Pick(BarkTrigger.LowHp, mode, l => l.UnitId == k.Unit);
            case UnitDowned d:
            {
                var line = Pick(BarkTrigger.Downed, mode, l => l.UnitId == d.Unit);
                _down.Add(d.Unit);
                return line;
            }

            case BattleEnded { Outcome: BattleOutcome.Victory }:
                _turnPriority = 0;
                return Pick(BarkTrigger.Victory, mode, l => _state.TryUnit(l.UnitId)?.Side == Side.Ally, prefer: _lastAllyActor);
            default:
                return null;
        }
    }

    private BattleBarkDefinition? Pick(BarkTrigger trigger, BarkMode mode, Func<BattleBarkDefinition, bool> match, string? prefer = null)
    {
        BattleBarkDefinition? best = null;
        foreach (var l in _lines)
        {
            if (l.Trigger != trigger || !match(l) || _state.TryUnit(l.UnitId) is null || !Ready(l)
                || (_down.Contains(l.UnitId) && trigger != BarkTrigger.Downed))
            {
                continue;
            }

            if (best is null || l.Priority > best.Priority
                || (l.Priority == best.Priority && l.Encounter is not null && best.Encounter is null)
                || (l.Priority == best.Priority && (l.Encounter is null) == (best.Encounter is null)
                    && prefer is not null && l.UnitId == prefer && best.UnitId != prefer))
            {
                best = l;
            }
        }

        if (best is null || best.Priority <= _turnPriority)
        {
            return null;
        }

        _spokenRound[best.LineId] = _round;
        if (best.Priority == BattleBarkDefinition.MinPriority)
        {
            _ordinaryRound[best.UnitId] = _round;
        }

        _turnPriority = best.Priority;
        return mode switch
        {
            BarkMode.Normal => best,
            BarkMode.Fast when best.Priority >= BattleBarkDefinition.MaxPriority => best,
            _ => null,
        };
    }

    private bool Ready(BattleBarkDefinition l) =>
        (!_spokenRound.TryGetValue(l.LineId, out var at) || (l.CooldownRounds > 0 && _round - at >= l.CooldownRounds))
        && (l.Priority > BattleBarkDefinition.MinPriority || !_ordinaryRound.TryGetValue(l.UnitId, out var last) || _round - last >= OrdinaryGapRounds);

    /// <summary>首次跌到重伤线以下（仍站着）。单位的最大气血是静态的，读当前状态即可。</summary>
    private bool Wounded(string unitId, int hpAfter) =>
        hpAfter > 0 && _state.TryUnit(unitId) is { Stats.MaxHp: > 0 } u
        && (long)hpAfter * 10_000 < (long)u.Stats.MaxHp * LowHpBp && _lowHp.Add(unitId);
}
