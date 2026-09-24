namespace WuxiaWorld.Game.Preview.Samples;

/// <summary>标题页存档卡的展示样例。字段对应架构文档存档契约中的摘要部分，不是实际存档。</summary>
public sealed record SampleSave(
    string Label,
    bool Auto,
    string Chapter,
    string Place,
    string Objective,
    string PlayTime,
    string SavedAt,
    string[] Party,
    float SunX);

public static class SaveSamples
{
    /// <summary>最近一次存档，“继续旅程”读取它。</summary>
    public static SampleSave Latest => Slots[0]!;

    /// <summary>空位为 null。</summary>
    public static readonly IReadOnlyList<SampleSave?> Slots =
    [
        new("自动存档", true, "第一篇　第一章　江南会客", "芦湾　江南客栈",
            "听三位来客各自说明求援书的疑点", "0:42:18", "2026-09-24　21:36",
            ["主", "陆", "令", "黄"], 0.74f),
        new("存档 01", false, "第一篇　第一章　江南会客", "芦湾　旧渡口",
            "核对船牌与水位", "0:31:05", "2026-09-24　21:12",
            ["主", "陆"], 0.30f),
        new("存档 02", false, "第一篇　第一章　江南会客", "芦湾　河滩",
            "跟随陆青禾前往客栈", "0:04:51", "2026-09-24　20:40",
            ["主"], 0.52f),
        null,
    ];
}
