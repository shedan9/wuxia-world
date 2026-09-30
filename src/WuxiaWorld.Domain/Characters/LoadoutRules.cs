using WuxiaWorld.Domain.Combat.Definitions;

namespace WuxiaWorld.Domain.Characters;

public enum LoadoutIssueLevel
{
    /// <summary>不合法，不能带入战斗。</summary>
    Error,

    /// <summary>合法但有代价，界面须直接说明（架构文档 8.2“冲突时 UI 直接说明代价”）。</summary>
    Warning,
}

public sealed record LoadoutIssue(LoadoutIssueLevel Level, string Code, string Message);

/// <summary>装配规则校验（架构文档 8.2）。</summary>
public static class LoadoutRules
{
    public static bool PolarityConflict(ArtDefinition? main, ArtDefinition support) =>
        main is not null && main.Polarity != Polarity.Neutral && support.Polarity != Polarity.Neutral
        && main.Polarity != support.Polarity;

    /// <param name="learned">已习得的招式与心法 ID；为 null 时不检查习得（敌方模板）。</param>
    public static IReadOnlyList<LoadoutIssue> Validate(Loadout l, CombatContent content, IReadOnlySet<string>? learned = null)
    {
        var issues = new List<LoadoutIssue>();
        if (l.Skills.Count > Loadout.MaxSkills)
        {
            issues.Add(new(LoadoutIssueLevel.Error, "too_many_skills", $"主动招式最多 {Loadout.MaxSkills} 个。"));
        }

        if (l.Talents.Count > Loadout.MaxTalents)
        {
            issues.Add(new(LoadoutIssueLevel.Error, "too_many_talents", $"被动天赋最多 {Loadout.MaxTalents} 个。"));
        }

        foreach (var dup in l.Skills.GroupBy(s => s, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            issues.Add(new(LoadoutIssueLevel.Error, "duplicate_skill", $"招式 {dup.Key} 重复装配。"));
        }

        foreach (var id in l.Skills)
        {
            if (!content.Skills.ContainsKey(id))
            {
                issues.Add(new(LoadoutIssueLevel.Error, "unknown_skill", $"未知招式 {id}。"));
            }
            else if (learned is not null && !learned.Contains(id))
            {
                issues.Add(new(LoadoutIssueLevel.Error, "not_learned", $"尚未习得 {id}。"));
            }
        }

        CheckArt(l.MainArt, ArtKind.Inner, "主修心法");
        CheckArt(l.SupportArt, ArtKind.Inner, "辅修心法");
        CheckArt(l.Qinggong, ArtKind.Qinggong, "轻功");
        foreach (var t in l.Talents)
        {
            CheckArt(t, ArtKind.Talent, "被动天赋");
        }

        if (l.MainArt is not null && l.MainArt == l.SupportArt)
        {
            issues.Add(new(LoadoutIssueLevel.Error, "same_inner", "主修与辅修不能是同一门心法。"));
        }

        if (l.SupportArt is not null && content.Arts.TryGetValue(l.SupportArt, out var support)
            && l.MainArt is not null && content.Arts.TryGetValue(l.MainArt, out var main)
            && PolarityConflict(main, support))
        {
            issues.Add(new(LoadoutIssueLevel.Warning, "polarity_conflict", "主修与辅修阴阳相冲：辅修心法的属性加成减半。"));
        }

        return issues;

        void CheckArt(string? id, ArtKind kind, string label)
        {
            if (id is null)
            {
                return;
            }

            if (!content.Arts.TryGetValue(id, out var art))
            {
                issues.Add(new(LoadoutIssueLevel.Error, "unknown_art", $"未知{label} {id}。"));
            }
            else if (art.Kind != kind)
            {
                issues.Add(new(LoadoutIssueLevel.Error, "wrong_art_kind", $"{id} 不能装为{label}。"));
            }
            else if (learned is not null && !learned.Contains(id))
            {
                issues.Add(new(LoadoutIssueLevel.Error, "not_learned", $"尚未习得 {id}。"));
            }
        }
    }
}
