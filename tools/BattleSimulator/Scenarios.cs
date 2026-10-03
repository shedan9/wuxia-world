using WuxiaWorld.Domain.Characters;
using WuxiaWorld.Domain.Combat.Definitions;

namespace WuxiaWorld.Tools.BattleSimulator;

/// <summary>
/// 模拟场景。第一章两场取正式内容；三个“挑战”是只在模拟器里存在的平衡测试集，
/// 分别针对三种流派的强项（架构文档 8.3）：高防单体、保护脆弱同伴、长战与多状态。
/// </summary>
internal static class Scenarios
{
    /// <param name="Guest">第三名同行者：默认用内容里的占位同行者作 M1 基线对比；给了 --party 时换成经典人物的角色模板。</param>
    public sealed record Scenario(string Id, string Title, string SuitedBuild, bool Guest = false);

    public static readonly Scenario[] All =
    [
        new("battle.01.escort_skirmish", "第一章·押运队冲突", "-"),
        new("battle.01.old_ferry_sluice", "第一章·旧渡水门（首领，三人）", "-", Guest: true),
        new("battle.01.old_ferry_sluice", "第一章·旧渡水门（首领，仅主角与陆青禾）", "-"),
        new("sim.challenge.armored", "挑战·铁甲护卫（高防单体）", "sword"),
        new("sim.challenge.protect", "挑战·飞钩围攻（保护后排）", "fist"),
        new("sim.challenge.attrition", "挑战·车轮缠斗（长战多状态）", "inner"),
    ];

    public static readonly CombatantTemplate[] Combatants =
    [
        new()
        {
            // 高防单体：蓄力重击（破架势或点穴可打断）+ 第 7 轮起狂暴，考验爆发与预判。
            Id = "sim.enemy.iron_guard", Level = 6, Attributes = new Attributes(12, 9, 6, 4, 4),
            Loadout = new Loadout { Skills = ["skill.enemy.escort_chop", "sim.skill.crushing_blow"] },
            Equipment = new StatBonus { ExternalDefense = 30, InternalDefense = 40 },
            Overrides = new StatOverride { MaxHp = 900, MaxStance = 70 },
            Tags = ["armored"], Ai = AiProfiles.BossTang, ControlThreshold = 2,
        },
        new()
        {
            Id = "sim.enemy.mystic", Level = 5, Attributes = new Attributes(5, 4, 9, 7, 6),
            Loadout = new Loadout { Skills = ["skill.inner.qi_bolt"] },
            Overrides = new StatOverride { MaxHp = 200 }, Ai = AiProfiles.Skirmisher,
        },
    ];

    public static readonly SkillDefinition[] Skills =
    [
        new()
        {
            Id = "sim.skill.crushing_blow", Tags = ["blade", "external"], TargetRule = TargetRule.SingleReachableEnemy,
            Cooldown = 2, Charged = true,
            Effects =
            [
                new EffectDefinition { Type = EffectType.Damage, Kind = DamageKind.External, ScaleBp = 26_000 },
                new EffectDefinition { Type = EffectType.StanceDamage, Amount = 40 },
            ],
        },
    ];

    public static readonly StatusDefinition[] Statuses =
    [
        new() { Id = "sim.status.enraged", Duration = 0, DamageDealtBp = 10_000 },
    ];

    public static readonly EncounterDefinition[] Encounters =
    [
        new()
        {
            Id = "sim.challenge.armored",
            Enemies =
            [
                new EncounterSlot { Template = "sim.enemy.iron_guard", UnitId = "enemy.iron_guard", Row = 0, Slot = 1 },
            ],
            Phases =
            [
                new EncounterPhase
                {
                    Id = "sim.phase.enrage", When = PhaseTriggerKind.RoundAtLeast, Value = 7,
                    Statuses = [new PhaseStatus("enemy.iron_guard", "sim.status.enraged")],
                },
            ],
        },
        new()
        {
            Id = "sim.challenge.protect",
            Enemies =
            [
                new EncounterSlot { Template = "combatant.enemy.escort", UnitId = "enemy.escort_a", Row = 0, Slot = 1 },
                new EncounterSlot { Template = "combatant.enemy.escort", UnitId = "enemy.escort_b", Row = 0, Slot = 2 },
                new EncounterSlot { Template = "combatant.enemy.hookman", UnitId = "enemy.hook_a", Row = 1, Slot = 0 },
                new EncounterSlot { Template = "combatant.enemy.hookman", UnitId = "enemy.hook_b", Row = 1, Slot = 2 },
            ],
        },
        new()
        {
            Id = "sim.challenge.attrition",
            Enemies =
            [
                new EncounterSlot { Template = "combatant.enemy.escort", UnitId = "enemy.escort_a", Row = 0, Slot = 0 },
                new EncounterSlot { Template = "combatant.enemy.escort", UnitId = "enemy.escort_b", Row = 0, Slot = 1 },
                new EncounterSlot { Template = "combatant.enemy.escort", UnitId = "enemy.escort_c", Row = 0, Slot = 2 },
                new EncounterSlot { Template = "sim.enemy.mystic", UnitId = "enemy.mystic", Row = 1, Slot = 1 },
            ],
        },
    ];
}
