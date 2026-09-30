using WuxiaWorld.Domain.Combat.Definitions;

namespace WuxiaWorld.Domain.Combat;

/// <summary>合法目标（架构文档 7.1：前排保护后排；远程、穿透可打破限制）。结果按阵位、再按 ID 稳定排序。</summary>
public static class Targeting
{
    public static Side Opposite(Side side) => side == Side.Ally ? Side.Enemy : Side.Ally;

    /// <summary>对方当前可接触的一排：前排有可战者为前排，否则为后排。</summary>
    public static int ReachableRow(BattleState state, Side targetSide) =>
        state.Living(targetSide).Any(u => u.Position.Row == 0) ? 0 : 1;

    public static IReadOnlyList<BattleUnit> Candidates(BattleState state, BattleUnit actor, TargetRule rule)
    {
        var foes = Opposite(actor.Side);
        IEnumerable<BattleUnit> list = rule switch
        {
            TargetRule.SingleReachableEnemy or TargetRule.ReachableRowEnemies =>
                state.Living(foes).Where(u => u.Position.Row == ReachableRow(state, foes)),
            TargetRule.SingleAnyEnemy or TargetRule.ColumnEnemies or TargetRule.AllEnemies => state.Living(foes),
            TargetRule.Self => [actor],
            TargetRule.SingleAlly or TargetRule.AllAllies => state.Living(actor.Side),
            TargetRule.OtherAlly => state.Living(actor.Side).Where(u => u.Id != actor.Id),
            _ => [],
        };
        return Order(list);
    }

    public static IReadOnlyList<BattleUnit> Order(IEnumerable<BattleUnit> units) =>
        [.. units.OrderBy(u => u.Position.Row).ThenBy(u => u.Position.Slot).ThenBy(u => u.Id, StringComparer.Ordinal)];

    /// <summary>需要玩家指定一名目标的规则（单体与“穿透一列”）。</summary>
    public static bool NeedsChosenTarget(TargetRule rule) =>
        rule is TargetRule.SingleReachableEnemy or TargetRule.SingleAnyEnemy or TargetRule.SingleAlly or TargetRule.OtherAlly
            or TargetRule.ColumnEnemies;

    /// <summary>
    /// 实际受影响的单位：单体规则取所选目标（须在候选内）；“穿透一列”取所选目标所在槽位的全部敌人（前排在先）；
    /// 其余群体规则取全部候选。
    /// </summary>
    public static IReadOnlyList<BattleUnit>? Resolve(BattleState state, BattleUnit actor, TargetRule rule, string? chosen)
    {
        var candidates = Candidates(state, actor, rule);
        if (!NeedsChosenTarget(rule))
        {
            return candidates;
        }

        var hit = candidates.FirstOrDefault(u => u.Id == chosen);
        if (hit is null)
        {
            return null;
        }

        return rule == TargetRule.ColumnEnemies ? [.. candidates.Where(u => u.Position.Slot == hit.Position.Slot)] : [hit];
    }
}
