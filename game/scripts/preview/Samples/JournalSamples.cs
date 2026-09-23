namespace WuxiaWorld.Game.Preview.Samples;

public enum StageState
{
    Done,
    Current,
    Hidden,
}

public sealed record SampleQuest(
    string Id,
    string Name,
    string Kind,
    string Place,
    string Summary,
    IReadOnlyList<(string Text, StageState State)> Stages,
    string? Guarantee,
    string? Consequence);

public sealed record SampleBond(
    string Name,
    string Source,
    string Kind,
    int Affection,
    int Trust,
    IReadOnlyList<string> Events);

/// <summary>见闻条目。Source 区分亲见、听闻与推测（剧情文档 2.1 开端）。</summary>
public sealed record SampleNote(string Title, string Source, string Status, string Detail, string? QuestId);

/// <summary>任务 / 关系 / 见闻页的固定样例，取自剧情文档第一章“江南会客”的前 15 分钟。</summary>
public static class JournalSamples
{
    public static readonly IReadOnlyList<SampleQuest> Quests =
    [
        new("quest.main.01.jiangnan_guest", "江南会客", "主线", "芦湾　江南客栈",
            "三封求援书同时寄到客栈，称芦湾旧渡有一队赈粮船失踪。信里还暗示三位收信人中有人泄露了救援行程。",
            [
                ("在芦湾醒来，随陆青禾进入客栈", StageState.Done),
                ("记录旧渡石痕", StageState.Done),
                ("听令狐冲、黄蓉与萧峰各自说明求援书的疑点", StageState.Current),
                ("后续目标在推进后显示", StageState.Hidden),
            ],
            "“水位与船牌不符”可由乔红绡或渡口告示得知，不会因错过调查而卡住主线。",
            null),
        new("quest.side.01.missing_ferryman", "失踪渡工", "支线", "芦湾渡口",
            "赈粮船失踪后，渡口有一名渡工也下落不明。有人说他最后一次撑船去了旧渡方向。",
            [
                ("核对渡口登记的船牌", StageState.Current),
                ("寻找旧渡附近的潮痕", StageState.Hidden),
                ("后续目标在推进后显示", StageState.Hidden),
            ],
            null,
            "先救出渡工，旧渡之战的水门压力少一轮；未做时战后仍会救出关键证人，但报酬与陆青禾的信任减少。"),
    ];

    public static readonly IReadOnlyList<SampleBond> Bonds =
    [
        new("陆青禾", "原创", "友情", 35, 40,
            ["发现你倒在芦湾，带你进客栈求助", "担心作证会丢了渡口的活计，还没开口"]),
        new("乔红绡", "原创", "初识", 20, 25,
            ["把三封求援书摊在桌上，要众人先查船夫下落"]),
        new("令狐冲", "《笑傲江湖》", "初识", 15, 10,
            ["认为求援书的语气像冒名旧友"]),
        new("黄蓉", "《射雕英雄传》", "初识", 10, 15,
            ["指出纸出自本地驿站，却盖着远处的转信章"]),
        new("萧峰", "《天龙八部》", "初识", 15, 20,
            ["先问失踪的船夫是否还活着"]),
    ];

    public static readonly IReadOnlyList<SampleNote> Notes =
    [
        new("旧渡石痕", "亲见", "已记录", "雨后在芦湾旧渡的石阶上看到的刻痕，形状与现代残片上的拓印相近。", "quest.main.01.jiangnan_guest"),
        new("石痕与现代残片有关", "推测", "待查", "只是你的猜想：两者纹样相近，但还没有任何人或文书能证明它们有关。", null),
        new("求援书的转信章", "听闻", "待查", "黄蓉说：纸出自本地驿站，却盖着远处的转信章。你还没有亲自比对驿站用纸。", "quest.main.01.jiangnan_guest"),
        new("水位与船牌不符", "亲见", "已证实", "渡口告示登记的船牌吃水，与当日水位对不上。乔红绡也证实了这一点。", "quest.main.01.jiangnan_guest"),
        new("三人中有人泄密", "听闻", "存疑", "求援书中的暗示，尚无旁证。三人彼此都有疑问，先把救人放在前面。", "quest.main.01.jiangnan_guest"),
    ];
}
