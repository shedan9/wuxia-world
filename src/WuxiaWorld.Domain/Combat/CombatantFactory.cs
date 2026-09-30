using WuxiaWorld.Domain.Characters;
using WuxiaWorld.Domain.Combat.Definitions;

namespace WuxiaWorld.Domain.Combat;

/// <summary>由模板（属性、等级、装配、装备）推导战斗单位；阴阳相冲的辅修心法只取一半加成（架构文档 8.2）。</summary>
public static class CombatantFactory
{
    public const int ConflictBonusBp = 5000;

    public static StatBlock DeriveStats(CombatantTemplate t, CombatContent content)
    {
        var bonus = t.Equipment;
        foreach (var (art, bp) in ArtsWithWeight(t.Loadout, content))
        {
            bonus = bonus.Plus(bp == Common.Bp.One ? art.Bonus : art.Bonus.Scaled(bp));
        }

        return StatFormula.Apply(StatFormula.Derive(t.Level, t.Attributes, bonus), t.Overrides);
    }

    /// <summary>装配中的心法、轻功与天赋及其生效比例（万分比）。</summary>
    public static IEnumerable<(ArtDefinition Art, int Bp)> ArtsWithWeight(Loadout l, CombatContent content)
    {
        ArtDefinition? main = l.MainArt is null ? null : content.Art(l.MainArt);
        if (main is not null)
        {
            yield return (main, Common.Bp.One);
        }

        if (l.SupportArt is not null)
        {
            var support = content.Art(l.SupportArt);
            yield return (support, LoadoutRules.PolarityConflict(main, support) ? ConflictBonusBp : Common.Bp.One);
        }

        if (l.Qinggong is not null)
        {
            yield return (content.Art(l.Qinggong), Common.Bp.One);
        }

        foreach (var talent in l.Talents)
        {
            yield return (content.Art(talent), Common.Bp.One);
        }
    }

    public static BattleUnit Create(CombatantTemplate t, string unitId, Side side, Position position, CombatContent content)
    {
        var stats = DeriveStats(t, content);
        return new BattleUnit
        {
            Id = unitId, TemplateId = t.Id, Side = side, Stats = stats, Template = t, Position = position,
            Hp = stats.MaxHp, Inner = stats.MaxInner, Stance = stats.MaxStance,
            Skills = [.. t.Loadout.Skills],
            Arts = [.. ArtsWithWeight(t.Loadout, content).Select(a => a.Art)],
        };
    }
}
