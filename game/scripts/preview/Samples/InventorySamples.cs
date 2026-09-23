namespace WuxiaWorld.Game.Preview.Samples;

public enum SampleItemKind
{
    Weapon,
    Armor,
    Medicine,
    Quest,
    Misc,
}

/// <summary>展示用物品。Stats 为显示字符串对，不参与任何计算。</summary>
public sealed record SampleItem(
    string Id,
    string Name,
    SampleItemKind Kind,
    string Glyph,
    int Count,
    string Description,
    IReadOnlyList<(string Stat, int Value)> Stats,
    string? Trait = null,
    int Price = 0);

/// <summary>M0 行囊、装备、商店页的固定样例。数值为示意，不是平衡数据。</summary>
public static class InventorySamples
{
    public const int Money = 360;

    public static string KindName(SampleItemKind kind) => kind switch
    {
        SampleItemKind.Weapon => "兵器",
        SampleItemKind.Armor => "护具",
        SampleItemKind.Medicine => "药物",
        SampleItemKind.Quest => "任务",
        _ => "杂物",
    };

    public static readonly IReadOnlyList<SampleItem> Bag =
    [
        new("item.weapon.green_steel_sword", "青钢剑", SampleItemKind.Weapon, "剑", 1,
            "江南铁铺常见的青钢长剑，剑脊略窄，适合刺挑。",
            [("外功", 18), ("命中", 6)], "破招：削减架势时额外 +10%"),
        new("item.weapon.ferry_pole", "旧渡船篙", SampleItemKind.Weapon, "棍", 1,
            "渡口常用的长船篙，竹节缠着麻绳，横扫时能护住身边人。",
            [("外功", 12), ("外防", 4)], "护援：替相邻同伴承受一次攻击"),
        new("item.armor.plain_jacket", "粗布短打", SampleItemKind.Armor, "衣", 1,
            "渡工常穿的短衣，便于上下船。", [("外防", 6), ("速度", 1)]),
        new("item.armor.straw_cape", "蓑衣", SampleItemKind.Armor, "蓑", 1,
            "雨季出门必备，披上后行动略沉。", [("外防", 8), ("内防", 4), ("速度", -2)]),
        new("item.medicine.wound_powder", "金创药", SampleItemKind.Medicine, "药", 3,
            "外敷止血，战斗中对一名同伴使用。", [("恢复气血", 120)]),
        new("item.medicine.breath_pill", "回气丹", SampleItemKind.Medicine, "丹", 1,
            "服下后内息渐匀，战斗中使用。", [("恢复内力", 40)]),
        new("item.misc.dry_rations", "干粮", SampleItemKind.Misc, "粮", 5,
            "麦饼与咸菜，旅途中休息时食用。", [("休息恢复", 30)]),
        new("item.quest.plea_letter_copy", "求援书抄本", SampleItemKind.Quest, "信", 1,
            "客栈桌上三封求援书之一的抄本。纸出自本地驿站，却盖着远处的转信章。", []),
        new("item.quest.ferry_tag", "芦湾船牌", SampleItemKind.Quest, "牌", 1,
            "渡口告示上登记的船牌号与当日水位对不上。", []),
        new("item.quest.modern_photo", "残破的照片", SampleItemKind.Quest, "影", 1,
            "参观现代展馆时留下的残片照片，夹着一张旧渡石痕的拓印。", []),
    ];

    public static readonly IReadOnlyList<SampleItem> Shop =
    [
        Bag[4] with { Price = 60, Count = 12 },
        Bag[5] with { Price = 120, Count = 4 },
        Bag[6] with { Price = 10, Count = 30 },
        Bag[3] with { Price = 90, Count = 2 },
        new("item.weapon.fine_sword", "精钢剑", SampleItemKind.Weapon, "剑", 1,
            "铺子里最好的一口剑，剑身有细密锻纹。",
            [("外功", 26), ("命中", 8)], "破招：削减架势时额外 +15%", 480),
    ];

    /// <summary>装备页的五个槽位（架构文档 8.4）与当前装备。</summary>
    public static readonly IReadOnlyList<(string Slot, SampleItem? Equipped)> Slots =
    [
        ("武器", Bag[0]),
        ("衣甲", Bag[2]),
        ("鞋", null),
        ("饰物", null),
        ("护符", null),
    ];

    /// <summary>主角当前面板，用于装备前后对比。</summary>
    public static readonly IReadOnlyList<(string Stat, int Value)> HeroStats =
    [
        ("外功", 62), ("内功", 48), ("外防", 30), ("内防", 26), ("速度", 44), ("命中", 18),
    ];
}
