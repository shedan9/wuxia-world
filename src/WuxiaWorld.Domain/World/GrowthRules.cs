using WuxiaWorld.Domain.Characters;
using WuxiaWorld.Domain.Combat;
using WuxiaWorld.Domain.Combat.Definitions;

namespace WuxiaWorld.Domain.World;

/// <summary>装配栏位里的心法类：主修、辅修、轻功（天赋单独增删）。</summary>
public enum ArtSlot
{
    Main,
    Support,
    Qinggong,
}

/// <summary>
/// 人物成长与装配（架构文档 8.1–8.2、8.4）：潜能分配、装备、招式与心法装配、武学熟练度（修炼），
/// 以及由这些数据推导出战斗模板。所有改动只作用于调用方给的状态副本，返回失败原因或 null；
/// 应用层在副本上执行、成功后整体提交。目前只有主角可养成，同行者用各自的战斗模板。
/// </summary>
public sealed class GrowthRules(WorldRules world, CombatContent combat)
{
    public WorldRules World { get; } = world;
    public CombatContent Combat { get; } = combat;

    private ProgressionDefinition P => World.Content.Progression;

    public string Hero => P.Hero;

    public bool IsBuildable(string characterId) => characterId == P.Hero;

    public int Level(WorldState s) => World.LevelOf(s.Experience);

    /// <summary>当前等级累计可分配的潜能点。</summary>
    public int PotentialTotal(WorldState s) => StatFormula.PotentialAt(Level(s));

    public int Unspent(WorldState s, string who) =>
        PotentialTotal(s) - (s.Builds.TryGetValue(who, out var b) ? b.Spent : 0);

    public Attributes AttributesOf(WorldState s, string who) =>
        s.Builds.TryGetValue(who, out var b) ? P.BaseAttributes.Plus(b.Allocated) : P.BaseAttributes;

    /// <summary>分配潜能：各项只增不减，合计不超过未分配点数。洗点属后续（架构文档 8.2，安全城镇开放）。</summary>
    public string? Allocate(WorldState s, string who, Attributes add)
    {
        if (!IsBuildable(who))
        {
            return "此人物的成长随剧情与角色模板变化，不由玩家分配";
        }

        if (add.Physique < 0 || add.Strength < 0 || add.Root < 0 || add.Agility < 0 || add.Insight < 0 || add.Total == 0)
        {
            return "潜能只能加点";
        }

        if (add.Total > Unspent(s, who))
        {
            return $"可分配潜能只有 {Unspent(s, who)} 点";
        }

        var b = WorldRules.BuildOf(s, who);
        b.SetAllocated(b.Allocated.Plus(add));
        return null;
    }

    /// <summary>
    /// 按流派推荐比例分配全部未分配潜能（最大余数法，平分时按体魄、臂力、根骨、身法、悟性次序）。
    /// 不改动已分配的点。
    /// </summary>
    public Attributes Recommend(WorldState s, string who)
    {
        var points = Math.Max(0, Unspent(s, who));
        var w = (s.Facts.TryGetValue(WorldRules.StyleFact, out var id) ? P.Styles.FirstOrDefault(x => x.Id == id) : null)?.Recommended
                ?? new Attributes(1, 1, 1, 1, 1);
        return Distribute(points, w);
    }

    /// <summary>按权重把点数分到五项（最大余数法，平分时按体魄、臂力、根骨、身法、悟性次序）；权重全为 0 时平分。</summary>
    public static Attributes Distribute(int points, Attributes weight)
    {
        int[] weights = [weight.Physique, weight.Strength, weight.Root, weight.Agility, weight.Insight];
        if (weights.All(x => x <= 0))
        {
            weights = [1, 1, 1, 1, 1];
        }

        weights = [.. weights.Select(x => Math.Max(0, x))];
        var sum = weights.Sum();
        var give = weights.Select(x => points * x / sum).ToArray();
        var rest = points - give.Sum();
        foreach (var i in Enumerable.Range(0, 5).OrderByDescending(i => points * weights[i] % sum).ThenBy(i => i).Take(rest))
        {
            give[i]++;
        }

        return new Attributes(give[0], give[1], give[2], give[3], give[4]);
    }

    // ── 装备 ─────────────────────────────────────────────

    /// <summary>从行囊装上一件装备；该槽原有的放回行囊。</summary>
    public string? Equip(WorldState s, string who, string itemId)
    {
        if (!IsBuildable(who))
        {
            return "同行者的装备随其角色模板，暂不更换";
        }

        if (!World.Content.Items.TryGetValue(itemId, out var item) || item.Slot is not { } slot)
        {
            return $"{itemId} 不是装备";
        }

        if (s.CountOf(itemId) < 1)
        {
            return "行囊里没有这件装备";
        }

        var b = WorldRules.BuildOf(s, who);
        Take(s, itemId);
        if (b.Equipped.TryGetValue(slot, out var old))
        {
            s.Items[old] = s.CountOf(old) + 1;
        }

        b.Equipped[slot] = itemId;
        return null;
    }

    public string? Unequip(WorldState s, string who, EquipSlot slot)
    {
        if (!IsBuildable(who))
        {
            return "同行者的装备随其角色模板，暂不更换";
        }

        if (!s.Builds.TryGetValue(who, out var b) || !b.Equipped.Remove(slot, out var old))
        {
            return "这一栏没有装备";
        }

        s.Items[old] = s.CountOf(old) + 1;
        return null;
    }

    private static void Take(WorldState s, string itemId)
    {
        if (--s.Items[itemId] == 0)
        {
            s.Items.Remove(itemId);
        }
    }

    /// <summary>已装上装备的加成合计。</summary>
    public StatBonus EquipmentBonus(WorldState s, string who)
    {
        var bonus = StatBonus.None;
        if (s.Builds.TryGetValue(who, out var b))
        {
            foreach (var id in b.Equipped.Values)
            {
                if (World.Content.Items.TryGetValue(id, out var item))
                {
                    bonus = bonus.Plus(item.Bonus);
                }
            }
        }

        return bonus;
    }

    // ── 武学装配 ─────────────────────────────────────────

    /// <summary>已学的招式（按 ID 排序）。</summary>
    public IEnumerable<string> LearnedSkills(WorldState s) => s.Skills.Where(Combat.Skills.ContainsKey);

    /// <summary>已学的某类心法 / 轻功 / 天赋。</summary>
    public IEnumerable<string> LearnedArts(WorldState s, ArtKind kind) =>
        s.Skills.Where(id => Combat.Arts.TryGetValue(id, out var a) && a.Kind == kind);

    /// <summary>整组替换出手栏；须已学、不重复、不超过 6 个，次序即战斗中的招式次序。</summary>
    public string? SetSkills(WorldState s, string who, IReadOnlyList<string> skills)
    {
        if (!IsBuildable(who))
        {
            return "同行者的招式随其角色模板";
        }

        var b = WorldRules.BuildOf(s, who);
        var loadout = LoadoutOf(b) with { Skills = skills };
        if (Errors(loadout, s) is { } error)
        {
            return error;
        }

        b.Skills.Clear();
        b.Skills.AddRange(skills);
        return null;
    }

    /// <summary>装上或卸下（<paramref name="artId"/> 为 null）一门心法或轻功；阴阳相冲合法但有代价，由界面说明。</summary>
    public string? SetArt(WorldState s, string who, ArtSlot slot, string? artId)
    {
        if (!IsBuildable(who))
        {
            return "同行者的心法随其角色模板";
        }

        var b = WorldRules.BuildOf(s, who);
        var l = LoadoutOf(b);
        l = slot switch
        {
            // 主修换成原先的辅修时，两者互换，免得报“同一门心法”。
            ArtSlot.Main => l with { MainArt = artId, SupportArt = l.SupportArt == artId ? l.MainArt : l.SupportArt },
            ArtSlot.Support => l with { SupportArt = artId, MainArt = l.MainArt == artId ? l.SupportArt : l.MainArt },
            _ => l with { Qinggong = artId },
        };
        if (Errors(l, s) is { } error)
        {
            return error;
        }

        (b.MainArt, b.SupportArt, b.Qinggong) = (l.MainArt, l.SupportArt, l.Qinggong);
        return null;
    }

    /// <summary>天赋装上 / 卸下切换（最多 3 个）。</summary>
    public string? ToggleTalent(WorldState s, string who, string talentId)
    {
        if (!IsBuildable(who))
        {
            return "同行者的天赋随其角色模板";
        }

        var b = WorldRules.BuildOf(s, who);
        if (b.Talents.Remove(talentId))
        {
            return null;
        }

        var l = LoadoutOf(b) with { Talents = [.. b.Talents, talentId] };
        if (Errors(l, s) is { } error)
        {
            return error;
        }

        b.Talents.Add(talentId);
        return null;
    }

    public static Loadout LoadoutOf(CharacterBuild b) => new()
    {
        Skills = [.. b.Skills], MainArt = b.MainArt, SupportArt = b.SupportArt, Qinggong = b.Qinggong, Talents = [.. b.Talents],
    };

    /// <summary>装配的全部问题（含阴阳相冲等警告），给界面直接写明代价。</summary>
    public IReadOnlyList<LoadoutIssue> Issues(WorldState s, string who) =>
        s.Builds.TryGetValue(who, out var b) ? LoadoutRules.Validate(LoadoutOf(b), Combat, s.Skills) : [];

    private string? Errors(Loadout l, WorldState s) =>
        LoadoutRules.Validate(l, Combat, s.Skills).FirstOrDefault(i => i.Level == LoadoutIssueLevel.Error)?.Message;

    // ── 修炼 ─────────────────────────────────────────────

    /// <summary>把招式熟练度提高一阶所需修为；已到最高阶或未学为 null。</summary>
    public int? MasteryCost(WorldState s, string who, string skillId)
    {
        if (!s.Skills.Contains(skillId) || !Combat.Skills.ContainsKey(skillId))
        {
            return null;
        }

        var tier = s.Builds.TryGetValue(who, out var b) ? b.MasteryOf(skillId) : 1;
        return tier < P.MaxMastery ? P.MasteryCosts[tier - 1] : null;
    }

    /// <summary>修炼：花修为把一门已学招式提高一阶，效果强度随之提高。</summary>
    public string? Cultivate(WorldState s, string who, string skillId)
    {
        if (!IsBuildable(who))
        {
            return "同行者的武学随其角色模板";
        }

        if (MasteryCost(s, who, skillId) is not { } cost)
        {
            return s.Skills.Contains(skillId) ? "已到最高一阶" : "尚未习得这门招式";
        }

        if (s.Cultivation < cost)
        {
            return $"修为不足：需要 {cost}，现有 {s.Cultivation}";
        }

        var b = WorldRules.BuildOf(s, who);
        s.Cultivation -= cost;
        b.Mastery[skillId] = b.MasteryOf(skillId) + 1;
        return null;
    }

    /// <summary>某阶熟练度带来的效果强度加成（万分比）。</summary>
    public int MasteryBonusBp(int tier) => Math.Max(0, tier - 1) * P.MasteryPowerBp;

    // ── 战斗模板 ─────────────────────────────────────────

    /// <summary>
    /// 由世界状态推导可养成人物的战斗模板：等级、基础属性 + 已分配潜能、装配、装备加成与熟练度。
    /// 每次开战现算，存档里不存派生值（架构文档 11）。
    /// </summary>
    public CombatantTemplate Template(WorldState s, string who)
    {
        if (!IsBuildable(who))
        {
            return CompanionTemplate(s, who);
        }

        var b = s.Builds.TryGetValue(who, out var build) ? build : new CharacterBuild();
        return new CombatantTemplate
        {
            Id = "build." + who,
            Level = Level(s),
            Attributes = AttributesOf(s, who),
            Loadout = LoadoutOf(b),
            Equipment = EquipmentBonus(s, who),
            ArtId = P.ArtId,
            SkillPowerBp = b.Mastery.Where(m => m.Value > 1).ToDictionary(m => m.Key, m => MasteryBonusBp(m.Value), StringComparer.Ordinal),
        };
    }

    public StatBlock Stats(WorldState s, string who) => CombatantFactory.DeriveStats(Template(s, who), Combat);

    // ── 同行者（架构文档 8.4、9.4.4） ─────────────────────

    /// <summary>经典人物的个人战斗模板制作前（M3）共用的占位同行者模板。</summary>
    public const string PlaceholderCompanion = "combatant.placeholder.companion";

    /// <summary>同行者的角色模板；没有模板的人物用占位同行者模板。</summary>
    public CombatantTemplate BaseTemplate(string who) =>
        Combat.Combatant(World.Content.Characters.TryGetValue(who, out var c) && c.Combatant is { } id ? id : PlaceholderCompanion);

    /// <summary>是否随主角成长：可招募伙伴且已入过队（同行记录里有经验）。</summary>
    public bool Grows(WorldState s, string who) =>
        PartyRules.RoleOf(World, who) == PartyRole.Recruitable && s.Companions.ContainsKey(who);

    /// <summary>人物当前等级：主角按经验；可招募伙伴按自己的经验、不低于角色模板等级；暂时同行者即模板等级。</summary>
    public int LevelOf(WorldState s, string who)
    {
        if (IsBuildable(who))
        {
            return Level(s);
        }

        var t = BaseTemplate(who);
        return Grows(s, who) ? Math.Max(t.Level, World.LevelOf(s.Companions[who].Experience)) : t.Level;
    }

    /// <summary>
    /// 同行者的战斗模板：可招募伙伴高出模板等级的每一级，按模板里高出基础属性的部分作权重自动分配潜能
    /// （保留其个人特色，不由玩家加点）；招式、心法与装备随角色模板。暂时同行者原样使用角色模板。
    /// </summary>
    public CombatantTemplate CompanionTemplate(WorldState s, string who)
    {
        var t = BaseTemplate(who);
        var level = LevelOf(s, who);
        if (level <= t.Level)
        {
            return t;
        }

        var b = P.BaseAttributes;
        var a = t.Attributes;
        var weight = new Attributes(a.Physique - b.Physique, a.Strength - b.Strength, a.Root - b.Root, a.Agility - b.Agility, a.Insight - b.Insight);
        var extra = StatFormula.PotentialAt(level) - StatFormula.PotentialAt(t.Level);
        return t with { Level = level, Attributes = a.Plus(Distribute(extra, weight)) };
    }
}
