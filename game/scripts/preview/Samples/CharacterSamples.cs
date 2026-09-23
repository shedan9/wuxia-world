namespace WuxiaWorld.Game.Preview.Samples;

/// <summary>展示用招式。字段对应架构文档 9.2 SkillDefinition 的显示部分。</summary>
public sealed record SampleSkill(
    string Id,
    string Name,
    string School,
    string Glyph,
    string Cost,
    string Target,
    string Cooldown,
    string Effect,
    int Mastery,
    (string A, string B)? NextVariant = null);

/// <summary>
/// 展示用人物。经典人物在 M0-06 档案核对前不给数值，只标来源作品与待核项，
/// 避免把样例数值当成实力排名或原著设定。
/// </summary>
public sealed record SampleMember(
    string Id,
    string Name,
    string Source,
    string Role,
    int Level,
    IReadOnlyDictionary<string, int>? Attributes,
    string? Pending = null);

public static class CharacterSamples
{
    public static readonly string[] AttributeNames = ["体魄", "臂力", "根骨", "身法", "悟性"];

    public static readonly IReadOnlyList<SampleMember> Party =
    [
        new("char.hero", "主角", "原创", "穿越者，本作主角", 3,
            new Dictionary<string, int> { ["体魄"] = 6, ["臂力"] = 7, ["根骨"] = 5, ["身法"] = 6, ["悟性"] = 8 }),
        new("char.lu_qinghe", "陆青禾", "原创", "江南渡工学徒，本地引路人", 3,
            new Dictionary<string, int> { ["体魄"] = 5, ["臂力"] = 5, ["根骨"] = 6, ["身法"] = 8, ["悟性"] = 6 }),
        new("char.linghu_chong", "令狐冲", "《笑傲江湖》", "第一章暂时同行", 0, null,
            "原著主要剧情阶段、年龄依据与身份称谓待核（M0-06）"),
        new("char.huang_rong", "黄蓉", "《射雕英雄传》", "第一章暂时同行", 0, null,
            "原著主要剧情阶段、年龄依据与身份称谓待核（M0-06）"),
    ];

    public static readonly IReadOnlyList<SampleSkill> HeroSkills =
    [
        new("skill.sword.break_guard", "破招刺", "剑", "剑", "内力 12", "单体，可接触的敌人", "1 次行动",
            "外招伤害，倍率 110%；削减架势 25。", 2, ("重刺：伤害倍率 +15%", "回锋：命中后回复内力 4")),
        new("skill.sword.chain_thrust", "连环刺", "剑", "剑", "内力 18", "单体，可接触的敌人", "2 次行动",
            "两段外招伤害；目标处于破绽时第二段必定暴击。", 1),
        new("skill.palm.guard_ally", "护身掌", "拳掌", "掌", "内力 10", "相邻同伴", "1 次行动",
            "直到自身下次行动前，替目标承受一次攻击并反击。", 2),
        new("skill.palm.push_back", "推窗掌", "拳掌", "掌", "内力 14", "单体，前排敌人", "2 次行动",
            "外招伤害并使目标换至后排；首领改为削减架势 15。", 1),
        new("skill.inner.steady_breath", "吐纳调息", "内功", "息", "无", "自身", "无",
            "恢复内力 30；直到下次行动前受到伤害 +10%。", 3),
        new("skill.inner.clear_mind", "清心引", "内功", "心", "内力 16", "单体同伴", "3 次行动",
            "驱散一个负面状态，并获得同类抗性 1 次。", 1),
        new("skill.light.wind_step", "听风步", "轻功", "步", "被动", "自身", "—",
            "速度 +6；突进类招式可越过前排。", 1),
    ];

    /// <summary>当前装配的主动招式（上限 6，架构文档 8.2）；null 表示空槽。</summary>
    public static readonly string?[] EquippedActive =
    [
        "skill.sword.break_guard", "skill.sword.chain_thrust", "skill.palm.guard_ally",
        "skill.inner.steady_breath", "skill.inner.clear_mind", null,
    ];

    public const int UnspentPotential = 3;
}
