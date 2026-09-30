using WuxiaWorld.Domain.Combat;
using WuxiaWorld.Domain.Combat.Ai;

namespace WuxiaWorld.Application.Combat;

/// <summary>
/// 一场战斗的应用层会话：持有内核状态，替 AI 单位决策，并逐条记录命令与行动后的状态哈希，
/// 供重放与问题诊断（架构文档 7.5“每次行动记录状态哈希、种子状态、命令和结算摘要”）。
/// 表现层只通过这里提交命令、读取事件与只读状态；倍速与跳过动画只影响事件播放，不影响结算。
/// </summary>
public sealed class BattleSession
{
    private readonly BattleEngine _engine;

    public BattleSession(BattleEngine engine, BattleSetup setup, string contentVersion = "")
    {
        _engine = engine;
        var (state, events) = engine.Start(setup);
        State = state;
        StartEvents = events;
        Record = new BattleRecord(setup, contentVersion, state.RulesetVersion, state.Hash());
    }

    public BattleEngine Engine => _engine;
    public BattleState State { get; }
    public IReadOnlyList<BattleEvent> StartEvents { get; }
    public BattleRecord Record { get; }

    public bool Ended => State.Outcome != BattleOutcome.Ongoing;

    /// <summary>当前轮到玩家下令的单位；轮到 AI 或已结束时为 null。</summary>
    public BattleUnit? AwaitingPlayer => State.Pending is { } id && State.Unit(id) is { PlayerControlled: true } u ? u : null;

    public bool AwaitingAi => State.Pending is { } id && !State.Unit(id).PlayerControlled;

    /// <summary>提交玩家命令。被拒绝的命令不记录、不改变状态。</summary>
    public CommandResult Submit(BattleCommand command)
    {
        if (AwaitingPlayer?.Id != command.Actor)
        {
            return CommandResult.Reject("还没轮到该角色行动。");
        }

        return Apply(command);
    }

    /// <summary>让当前 AI 单位行动一次；不是 AI 的回合时返回 null。</summary>
    public CommandResult? StepAi()
    {
        if (!AwaitingAi)
        {
            return null;
        }

        var command = BattleAi.Decide(_engine, State);
        var result = Apply(command);
        if (!result.Accepted)
        {
            throw new BattleInvariantException($"AI 提交了非法命令 {command}：{result.Rejection}", State.Hash());
        }

        return result;
    }

    private CommandResult Apply(BattleCommand command)
    {
        var result = _engine.Submit(State, command);
        if (result.Accepted)
        {
            Record.Add(command, State.Hash());
        }

        return result;
    }

    /// <summary>
    /// 按记录重放：从同一开战输入出发、依次提交记录中的命令，逐条比对状态哈希。
    /// 返回首个不一致的命令序号；全部一致返回 null。
    /// </summary>
    public static int? Replay(BattleEngine engine, BattleRecord record)
    {
        var (state, _) = engine.Start(record.Setup);
        if (state.Hash() != record.InitialHash)
        {
            return -1;
        }

        for (var i = 0; i < record.Entries.Count; i++)
        {
            var entry = record.Entries[i];
            var result = engine.Submit(state, entry.Command);
            if (!result.Accepted || state.Hash() != entry.HashAfter)
            {
                return i;
            }
        }

        return null;
    }
}

public sealed record BattleRecordEntry(int Index, BattleCommand Command, string HashAfter);

/// <summary>战斗记录：开战输入 + 规则与内容版本 + 命令序列与逐条哈希。</summary>
public sealed class BattleRecord(BattleSetup setup, string contentVersion, int rulesetVersion, string initialHash)
{
    private readonly List<BattleRecordEntry> _entries = [];

    public BattleSetup Setup { get; } = setup;
    public string ContentVersion { get; } = contentVersion;
    public int RulesetVersion { get; } = rulesetVersion;
    public string InitialHash { get; } = initialHash;
    public IReadOnlyList<BattleRecordEntry> Entries => _entries;

    public string FinalHash => _entries.Count > 0 ? _entries[^1].HashAfter : InitialHash;

    internal void Add(BattleCommand command, string hash) => _entries.Add(new BattleRecordEntry(_entries.Count, command, hash));
}
