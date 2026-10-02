using System.Globalization;
using Godot;
using WuxiaWorld.Domain.Characters;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>人物、行囊与店铺页共用的显示文字：属性名、装备槽、物品类别、武学说明与字形印鉴。只做显示，不含规则。</summary>
public static class GrowthText
{
    public static readonly string[] AttributeNames = ["体魄", "臂力", "根骨", "身法", "悟性"];

    public static int[] Values(Attributes a) => [a.Physique, a.Strength, a.Root, a.Agility, a.Insight];

    public static Attributes FromValues(IReadOnlyList<int> v) => new(v[0], v[1], v[2], v[3], v[4]);

    public static string AttributeHint(int index) => index switch
    {
        0 => "气血上限、外防与架势",
        1 => "外功",
        2 => "内力上限、内功、内防与控制抗性",
        3 => "速度、闪避与先手",
        _ => "命中与暴击",
    };

    /// <summary>战斗属性：名称、取值、是否以万分比显示为百分数。</summary>
    public static readonly (string Name, Func<StatBlock, int> Get, bool Percent)[] Stats =
    [
        ("气血", s => s.MaxHp, false),
        ("内力", s => s.MaxInner, false),
        ("外功", s => s.ExternalAttack, false),
        ("内功", s => s.InternalAttack, false),
        ("外防", s => s.ExternalDefense, false),
        ("内防", s => s.InternalDefense, false),
        ("速度", s => s.Speed, false),
        ("命中", s => s.Accuracy, false),
        ("闪避", s => s.Evasion, false),
        ("暴击", s => s.CritBp, true),
        ("架势", s => s.MaxStance, false),
        ("控制抗性", s => s.ControlResistBp, true),
    ];

    public static string Format(int value, bool percent) =>
        percent ? (value / 100.0).ToString("0.#", CultureInfo.InvariantCulture) + "%" : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>加成里不为零的条目（名称、带符号的数值）。</summary>
    public static IEnumerable<(string Name, string Value)> Bonus(StatBonus b)
    {
        (string, int, bool)[] all =
        [
            ("气血", b.MaxHp, false), ("内力", b.MaxInner, false), ("外功", b.ExternalAttack, false), ("内功", b.InternalAttack, false),
            ("外防", b.ExternalDefense, false), ("内防", b.InternalDefense, false), ("速度", b.Speed, false), ("命中", b.Accuracy, false),
            ("闪避", b.Evasion, false), ("暴击", b.CritBp, true), ("架势", b.MaxStance, false), ("控制抗性", b.ControlResistBp, true),
        ];
        return all.Where(x => x.Item2 != 0).Select(x => (x.Item1, (x.Item2 > 0 ? "+" : "") + Format(x.Item2, x.Item3)));
    }

    public static string BonusLine(StatBonus b) => string.Join("　", Bonus(b).Select(x => $"{x.Name} {x.Value}"));

    public static string SlotName(EquipSlot slot) => slot switch
    {
        EquipSlot.Weapon => "兵器",
        EquipSlot.Armor => "衣甲",
        EquipSlot.Boots => "鞋",
        EquipSlot.Accessory => "饰物",
        _ => "护符",
    };

    public static string CategoryName(ItemCategory c) => c switch
    {
        ItemCategory.Weapon => "兵器",
        ItemCategory.Armor => "衣甲",
        ItemCategory.Boots => "鞋",
        ItemCategory.Accessory => "饰物",
        ItemCategory.Charm => "护符",
        ItemCategory.Medicine => "药品",
        ItemCategory.Material => "材料",
        ItemCategory.Quest => "任务",
        _ => "杂物",
    };

    /// <summary>物品字形印鉴（正式图标完成前的示意）。</summary>
    public static PanelContainer ItemGlyph(ItemDefinition? item, string name, int size = 56)
    {
        var glyph = name.Length > 0 ? name[^1].ToString() : "物";
        var tone = item?.Category switch
        {
            ItemCategory.Weapon => UiPalette.Text,
            ItemCategory.Armor or ItemCategory.Boots => UiPalette.Boost,
            ItemCategory.Accessory or ItemCategory.Charm => UiPalette.Accent,
            ItemCategory.Medicine => UiPalette.Warm,
            ItemCategory.Quest => UiPalette.Ochre,
            _ => UiPalette.TextMuted,
        };
        return Ui.Glyph(glyph, tone, size);
    }

    public static string School(string id) =>
        id.StartsWith("skill.sword.", StringComparison.Ordinal) ? "剑"
        : id.StartsWith("skill.fist.", StringComparison.Ordinal) ? "拳掌"
        : id.StartsWith("skill.inner.", StringComparison.Ordinal) ? "内功"
        : id.StartsWith("skill.staff.", StringComparison.Ordinal) ? "棍杖"
        : "通用";

    public static PanelContainer SkillGlyph(string id, string name, int size = 56) => Ui.Glyph(name.Length > 0 ? name[0].ToString() : "招", School(id) switch
    {
        "剑" => UiPalette.Text,
        "拳掌" => UiPalette.Warm,
        "内功" => UiPalette.Boost,
        _ => UiPalette.TextMuted,
    }, size);

    public static string RuleText(TargetRule rule) => rule switch
    {
        TargetRule.SingleReachableEnemy => "近身单体",
        TargetRule.SingleAnyEnemy => "远程单体",
        TargetRule.ReachableRowEnemies => "横扫一排",
        TargetRule.ColumnEnemies => "穿透一列",
        TargetRule.AllEnemies => "敌方全体",
        TargetRule.Self => "自身",
        TargetRule.SingleAlly => "己方单体",
        TargetRule.OtherAlly => "一名同伴",
        TargetRule.AllAllies => "己方全体",
        _ => "",
    };

    public static string Cost(SkillDefinition s)
    {
        var parts = new List<string>();
        if (s.InnerCost > 0)
        {
            parts.Add($"内力 {s.InnerCost}");
        }

        if (s.MomentumCost > 0)
        {
            parts.Add($"势 {s.MomentumCost}");
        }

        return parts.Count == 0 ? "无消耗" : string.Join("、", parts);
    }

    public static string Mastery(int tier, int max) => new string('●', tier) + new string('○', Math.Max(0, max - tier));

    public static string Polarity(Polarity p) => p switch
    {
        Domain.Characters.Polarity.Yin => "阴",
        Domain.Characters.Polarity.Yang => "阳",
        _ => "中和",
    };

    public static string ArtKindName(ArtKind k) => k switch
    {
        ArtKind.Inner => "心法",
        ArtKind.Qinggong => "轻功",
        _ => "天赋",
    };

    /// <summary>心法、轻功与天赋的作用，由数据现写，不另存说明文字，免得与数值脱节。</summary>
    public static string ArtEffect(ArtDefinition a)
    {
        var parts = new List<string>();
        if (a.Kind == ArtKind.Inner)
        {
            parts.Add($"{Polarity(a.Polarity)}性");
        }

        if (BonusLine(a.Bonus) is { Length: > 0 } bonus)
        {
            parts.Add(bonus);
        }

        if (a.MeditateBonusBp > 0)
        {
            parts.Add($"调息多回 {a.MeditateBonusBp / 100}% 内力");
        }

        if (a.InnerPerAction > 0)
        {
            parts.Add($"每次行动回内力 {a.InnerPerAction}");
        }

        foreach (var t in a.Traits)
        {
            parts.Add(t switch
            {
                "parry" => "招架：防御中受击时回架势、积势（占用本轮反应）",
                "stance_breaker" => "破势：造成的架势伤害 +20%",
                _ => t,
            });
        }

        return string.Join("；", parts);
    }
}
