using WuxiaWorld.Domain.Characters;
using WuxiaWorld.Domain.Combat;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Infrastructure.Content;

namespace WuxiaWorld.Domain.Tests;

public class ContentTests
{
    [Fact]
    public void Repository_combat_content_validates_without_errors()
    {
        var errors = CombatContentValidator.Validate(TestContent.Bundle);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public void Bundle_round_trips_through_json()
    {
        var bundle = TestContent.Bundle;
        var parsed = CombatBundle.Parse(bundle.Serialize());
        Assert.Equal(bundle.ContentVersion, parsed.ContentVersion);
        Assert.Equal(bundle.Skills.Count, parsed.Skills.Count);
        Assert.Equal(bundle.Serialize(), parsed.Serialize());
    }

    [Fact]
    public void Demo_has_eight_to_twelve_player_skills_and_three_inner_arts()
    {
        var b = TestContent.Bundle;
        // 主角可学的招式（三套流派预设所用）；同行者的个人招式（独孤九剑、打狗棒法、降龙十八掌）不计入。
        var player = b.Combatants.Where(c => c.Id.StartsWith("combatant.hero.", StringComparison.Ordinal))
            .SelectMany(c => c.Loadout.Skills).Distinct(StringComparer.Ordinal).Count();
        Assert.InRange(player, 8, 12);
        Assert.Equal(3, b.Arts.Count(a => a.Kind == ArtKind.Inner));
        Assert.InRange(b.Arts.Count(a => a.Kind == ArtKind.Qinggong), 1, 2);
    }

    [Fact]
    public void Three_hero_builds_share_the_same_growth_and_equipment_budget()
    {
        var b = TestContent.Bundle;
        var builds = new[] { "combatant.hero.sword", "combatant.hero.fist", "combatant.hero.inner" }
            .Select(id => b.Combatants.Single(c => c.Id == id)).ToList();
        Assert.All(builds, t => Assert.Equal(5, t.Level));
        var budget = 25 + StatFormula.PotentialAt(5);
        Assert.All(builds, t => Assert.Equal(budget, t.Attributes.Total));
        Assert.All(builds, t => Assert.Equal(12, EquipmentPoints(t.Equipment)));
        Assert.All(builds, t => Assert.DoesNotContain(LoadoutRules.Validate(t.Loadout, TestContent.Real),
            i => i.Level == LoadoutIssueLevel.Error));
    }

    [Fact]
    public void Loadout_rules_reject_overfull_and_warn_on_polarity_conflict()
    {
        var content = TestContent.Real;
        var tooMany = new Loadout { Skills = ["skill.sword.probe", "skill.sword.break_guard", "skill.sword.chain_thrust", "skill.sword.pierce", "skill.fist.heavy_palm", "skill.fist.sweep", "skill.inner.qi_bolt"] };
        Assert.Contains(LoadoutRules.Validate(tooMany, content), i => i.Code == "too_many_skills");

        var conflict = new Loadout { MainArt = "art.inner.calm_breath", SupportArt = "art.inner.iron_shirt" };
        var issues = LoadoutRules.Validate(conflict, content);
        Assert.Contains(issues, i => i is { Code: "polarity_conflict", Level: LoadoutIssueLevel.Warning });

        // 相冲时辅修加成减半：铁布衫外防 +14 只得 +7。
        var withConflict = CombatantFactory.DeriveStats(new CombatantTemplate { Id = "t.a", Loadout = conflict }, content);
        var alone = CombatantFactory.DeriveStats(new CombatantTemplate { Id = "t.b", Loadout = new Loadout { MainArt = "art.inner.calm_breath" } }, content);
        Assert.Equal(7, withConflict.ExternalDefense - alone.ExternalDefense);

        var wrongKind = new Loadout { Qinggong = "art.inner.iron_shirt" };
        Assert.Contains(LoadoutRules.Validate(wrongKind, content), i => i.Code == "wrong_art_kind");
    }

    [Fact]
    public void Validator_reports_broken_references()
    {
        var b = TestContent.Bundle with
        {
            Skills = [.. TestContent.Bundle.Skills, new SkillDefinition
            {
                Id = "skill.test.bad", Effects = [new EffectDefinition { Type = EffectType.ApplyStatus, Status = "status.nope" }],
            }],
        };
        var errors = CombatContentValidator.Validate(b);
        Assert.Contains(errors, e => e.Contains("status.nope", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("skill.test.bad.name", StringComparison.Ordinal));
    }

    private static int EquipmentPoints(StatBonus e) =>
        e.MaxHp / 10 + e.MaxInner / 10 + e.ExternalAttack + e.InternalAttack + e.ExternalDefense + e.InternalDefense + e.Speed + e.Accuracy / 2 + e.Evasion / 2;
}
