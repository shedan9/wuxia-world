namespace WuxiaWorld.Game.Preview.Samples;

public enum SampleSide
{
    Ally,
    Enemy,
}

/// <summary>
/// 展示用战斗单位。阵位按架构文档 7.1：每方 2 排 × 3 槽；Row 0 前排、1 后排，Slot 0–2 由远到近。
/// 数值为固定样例，不来自规则计算。
/// </summary>
public sealed record SampleUnit(
    string Id,
    string Name,
    string Glyph,
    SampleSide Side,
    int Row,
    int Slot,
    int Hp,
    int MaxHp,
    int Inner,
    int MaxInner,
    int Stance,
    int MaxStance,
    string[] Statuses,
    string? Intent = null,
    bool Mechanism = false);

/// <summary>第一章旧渡水门机制战的展示样例（STORY 第 2.1 节“冲突”段）；只含原创人物。</summary>
public static class BattleSamples
{
    public const int Round = 3;
    public const int Momentum = 46;

    public static readonly IReadOnlyList<SampleUnit> Units =
    [
        new("char.hero", "主角", "主", SampleSide.Ally, 0, 2, 302, 342, 150, 188, 60, 60, ["◆ 防御"]),
        new("char.lu_qinghe", "陆青禾", "陆", SampleSide.Ally, 1, 1, 214, 290, 96, 188, 40, 50, ["▲ 迅捷"]),
        new("enemy.tang_shouting", "唐守亭", "唐", SampleSide.Enemy, 0, 1, 690, 980, 0, 0, 55, 80, ["▼ 破绽"],
            "蓄力：水门冲击（下轮前排群攻）"),
        new("enemy.escort_a", "押运打手", "押", SampleSide.Enemy, 0, 2, 120, 180, 0, 0, 20, 30, []),
        new("enemy.sluice_gate", "水门机关", "闸", SampleSide.Enemy, 1, 0, 400, 400, 0, 0, 0, 0, [],
            "开闸：每轮水位 +1", Mechanism: true),
        new("enemy.escort_b", "押运打手", "押", SampleSide.Enemy, 1, 2, 64, 180, 0, 0, 8, 30, ["● 流血"]),
    ];

    /// <summary>本轮行动顺序（按速度），首项为当前行动者。</summary>
    public static readonly string[] Order =
        ["char.hero", "enemy.escort_a", "char.lu_qinghe", "enemy.tang_shouting", "enemy.escort_b", "enemy.sluice_gate"];

    public static readonly string[] Log =
    [
        "第 3 轮开始。水门机关：水位升至 2。",
        "陆青禾使出“点篙”，押运打手流血。",
        "唐守亭开始蓄力：下轮将对前排使出水门冲击。",
    ];
}
