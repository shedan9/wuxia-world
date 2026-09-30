using WuxiaWorld.Domain.Characters;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Domain.Common;

namespace WuxiaWorld.Domain.Combat;

public enum Side
{
    Ally,
    Enemy,
}

public enum BattleOutcome
{
    Ongoing,
    Victory,
    Defeat,
    Retreated,
}

/// <summary>单位身上的一个状态实例。</summary>
public sealed class StatusInstance
{
    public required string StatusId { get; init; }
    public int Stacks { get; set; } = 1;

    /// <summary>剩余触发次数；0 表示不随时点消耗。</summary>
    public int Remaining { get; set; }

    public string? SourceId { get; set; }

    /// <summary>施加时正处于同一触发点，本次不消耗（架构文档 7.3“刚施加的同一触发点不重复消费”）。</summary>
    public bool SkipNextTick { get; set; }

    /// <summary>蓄力招 ID（仅 <see cref="CoreIds.Charging"/>）。</summary>
    public string? ChargedSkill { get; set; }

    /// <summary>蓄力招选定的目标（单体招；目标失效时出手前改选）。</summary>
    public string? ChargedTarget { get; set; }

    public StatusInstance Clone() => (StatusInstance)MemberwiseClone();
}

/// <summary>场上单位的可变战斗状态。派生数值在开战时算好并随战斗保存，战后不写回。</summary>
public sealed class BattleUnit
{
    public required string Id { get; init; }
    public required string TemplateId { get; init; }
    public required Side Side { get; init; }
    public required StatBlock Stats { get; init; }
    public required CombatantTemplate Template { get; init; }

    public Position Position { get; set; }
    public int Hp { get; set; }
    public int Inner { get; set; }
    public int Stance { get; set; }
    public bool IsDown => Hp <= 0;

    /// <summary>本轮是否已用过反应（每轮至多 1 次，架构文档 7.1）。</summary>
    public bool ReactionUsed { get; set; }

    /// <summary>首领的控制积累值（<see cref="CombatantTemplate.ControlThreshold"/> &gt; 0 时使用）。</summary>
    public int ControlMeter { get; set; }

    public List<StatusInstance> Statuses { get; init; } = [];

    /// <summary>招式 ID → 剩余冷却次数；枚举时按 ID 排序。</summary>
    public SortedDictionary<string, int> Cooldowns { get; init; } = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Skills { get; init; } = [];
    public IReadOnlyList<ArtDefinition> Arts { get; init; } = [];

    public bool PlayerControlled => Template.Ai == AiProfiles.Player;

    public StatusInstance? FindStatus(string id) => Statuses.Find(s => s.StatusId == id);

    public bool HasStatus(string id) => Statuses.Exists(s => s.StatusId == id);

    public int StacksOf(string id) => FindStatus(id)?.Stacks ?? 0;

    public bool HasTrait(string trait) => Arts.Any(a => a.Traits.Contains(trait, StringComparer.Ordinal));

    public int CooldownOf(string skillId) => Cooldowns.TryGetValue(skillId, out var n) ? n : 0;

    public BattleUnit Clone()
    {
        return new BattleUnit
        {
            Id = Id, TemplateId = TemplateId, Side = Side, Stats = Stats, Template = Template,
            Position = Position, Hp = Hp, Inner = Inner, Stance = Stance, ReactionUsed = ReactionUsed,
            ControlMeter = ControlMeter, Skills = Skills, Arts = Arts,
            Statuses = Statuses.ConvertAll(s => s.Clone()),
            Cooldowns = new SortedDictionary<string, int>(Cooldowns, StringComparer.Ordinal),
        };
    }
}

/// <summary>
/// 一场战斗的完整可序列化状态。内核是 <c>BattleState + BattleCommand → 新状态 + 事件</c>；
/// 同一初始状态、规则版本、随机状态与命令序列必然得到同一结果（架构文档 7.5）。
/// </summary>
public sealed class BattleState
{
    public required string EncounterId { get; init; }
    public int RulesetVersion { get; init; } = StatFormula.RulesetVersion;
    public bool Locked { get; init; }
    public VictoryRule Victory { get; init; }
    public string? VictoryUnit { get; init; }

    // 可变结构体须以字段暴露，调用方才能原地推进随机状态；改成属性会拿到副本。
#pragma warning disable CA1051
    public Pcg32 Rng;
#pragma warning restore CA1051

    public int Round { get; set; }

    /// <summary>本轮行动次序（开轮时按速度排定），<see cref="TurnIndex"/> 指向当前行动者。</summary>
    public List<string> Order { get; init; } = [];

    public int TurnIndex { get; set; }

    /// <summary>当前等待命令的单位；战斗结束时为 null。</summary>
    public string? Pending { get; set; }

    public BattleOutcome Outcome { get; set; } = BattleOutcome.Ongoing;

    /// <summary>按 ID 有序排列的全部单位（含倒下者）。</summary>
    public List<BattleUnit> Units { get; init; } = [];

    /// <summary>双方的“势”，0–100，战后清空（架构文档 7.2）。</summary>
    public int AllyMomentum { get; set; }

    public int EnemyMomentum { get; set; }

    /// <summary>己方队伍的战斗物品数量；按物品 ID 有序。</summary>
    public SortedDictionary<string, int> Items { get; init; } = new(StringComparer.Ordinal);

    /// <summary>已触发的阶段 ID。</summary>
    public SortedSet<string> FiredPhases { get; init; } = new(StringComparer.Ordinal);

    /// <summary>已结算的命令数，用于重放定位。</summary>
    public int CommandCount { get; set; }

    public BattleUnit Unit(string id) =>
        Units.Find(u => u.Id == id) ?? throw new KeyNotFoundException($"场上没有单位 {id}");

    public BattleUnit? TryUnit(string? id) => id is null ? null : Units.Find(u => u.Id == id);

    public IEnumerable<BattleUnit> Living(Side side) => Units.Where(u => u.Side == side && !u.IsDown);

    public int Momentum(Side side) => side == Side.Ally ? AllyMomentum : EnemyMomentum;

    public void SetMomentum(Side side, int value)
    {
        var v = Math.Clamp(value, 0, CombatConstants.MaxMomentum);
        if (side == Side.Ally)
        {
            AllyMomentum = v;
        }
        else
        {
            EnemyMomentum = v;
        }
    }

    public BattleState Clone() => new()
    {
        EncounterId = EncounterId, RulesetVersion = RulesetVersion, Locked = Locked, Victory = Victory,
        VictoryUnit = VictoryUnit, Rng = Rng, Round = Round, Order = [.. Order], TurnIndex = TurnIndex,
        Pending = Pending, Outcome = Outcome, Units = Units.ConvertAll(u => u.Clone()),
        AllyMomentum = AllyMomentum, EnemyMomentum = EnemyMomentum,
        Items = new SortedDictionary<string, int>(Items, StringComparer.Ordinal),
        FiredPhases = new SortedSet<string>(FiredPhases, StringComparer.Ordinal), CommandCount = CommandCount,
    };

    /// <summary>状态哈希：固定次序写入全部规则相关字段（重放一致性校验用）。</summary>
    public string Hash()
    {
        var h = new StateHasher()
            .Add(EncounterId).Add(RulesetVersion).Add((long)Rng.State).Add((long)Rng.Increment)
            .Add(Round).Add(TurnIndex).Add(Pending).Add((int)Outcome)
            .Add(AllyMomentum).Add(EnemyMomentum).Add(CommandCount);
        foreach (var id in Order)
        {
            h.Add(id);
        }

        foreach (var u in Units)
        {
            h.Add(u.Id).Add((int)u.Side).Add(u.Position.Row).Add(u.Position.Slot).Add(u.Hp).Add(u.Inner)
                .Add(u.Stance).Add(u.ReactionUsed).Add(u.ControlMeter);
            foreach (var s in u.Statuses)
            {
                h.Add(s.StatusId).Add(s.Stacks).Add(s.Remaining).Add(s.SourceId).Add(s.SkipNextTick)
                    .Add(s.ChargedSkill).Add(s.ChargedTarget);
            }

            foreach (var (skill, cd) in u.Cooldowns)
            {
                h.Add(skill).Add(cd);
            }
        }

        foreach (var (item, count) in Items)
        {
            h.Add(item).Add(count);
        }

        foreach (var phase in FiredPhases)
        {
            h.Add(phase);
        }

        return h.Hex;
    }
}
