using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 任务 / 关系 / 见闻展示页。关系以人物列表与事件摘要为主导航（架构文档 10.4），
/// 见闻分开记录亲见、听闻与推测。
/// </summary>
public partial class JournalPreview : PreviewScreen
{
    protected override string SealText => "札记";
    protected override string Title => "任务、关系与见闻";
    protected override string Subtitle => "当前目标、人物往来与已知信息";

    protected override IReadOnlyList<(string Name, Func<Control> Build)> Tabs =>
    [
        ("任务", BuildQuests),
        ("关系", BuildBonds),
        ("见闻", BuildNotes),
    ];

    private static Control BuildQuests()
    {
        var detail = new MarginContainer();
        var list = Ui.Column(4);
        var group = new ButtonGroup();
        foreach (var kind in new[] { "主线", "支线" })
        {
            list.AddChild(Ui.Text(kind, UiTheme.MutedLabel));
            foreach (var q in JournalSamples.Quests.Where(q => q.Kind == kind))
            {
                list.AddChild(Ui.ListRow(group, () => ShowQuest(detail, q), q.Name, q.Place,
                    q.Stages.Any(s => s.State == StageState.Done) ? "进行中" : "新", selected: q == JournalSamples.Quests[0]));
            }
        }

        list.AddChild(Ui.Text("已完成", UiTheme.MutedLabel));
        list.AddChild(Ui.Text("还没有完成的事件。", UiTheme.MutedLabel));
        return Ui.Row(UiPalette.SpaceXl, Ui.MinSize(list, 520), Ui.Expand(detail));
    }

    private static void ShowQuest(Container host, SampleQuest q)
    {
        Ui.ClearChildren(host);
        var stages = Ui.Column(UiPalette.SpaceS);
        foreach (var (text, state) in q.Stages)
        {
            var line = state switch
            {
                StageState.Done => Ui.Text($"✓　{text}", UiTheme.MutedLabel, UiPalette.FontBody),
                StageState.Current => Ui.Text($"▶　{text}", UiTheme.AccentLabel),
                _ => Ui.Text($"…　{text}", UiTheme.MutedLabel),
            };
            stages.AddChild(line);
        }

        var body = Ui.Column(UiPalette.SpaceM,
            Ui.Text($"{q.Kind}　　{q.Place}", UiTheme.MutedLabel),
            Ui.Text(q.Name, UiTheme.TitleLabel),
            Ui.Text(q.Summary, wrap: true),
            Ui.Rule(),
            Ui.Text("进展", UiTheme.MutedLabel),
            stages);

        if (q.Guarantee is not null)
        {
            body.AddChild(Ui.Rule());
            body.AddChild(Ui.Text("保底线索", UiTheme.MutedLabel));
            body.AddChild(Ui.Text(q.Guarantee, wrap: true));
        }

        if (q.Consequence is not null)
        {
            body.AddChild(Ui.Rule());
            body.AddChild(Ui.Text("做与不做", UiTheme.MutedLabel));
            body.AddChild(Ui.Text(q.Consequence, wrap: true));
        }

        body.AddChild(Ui.Row(UiPalette.SpaceM,
            Ui.MinSize(Ui.Button("在地图上标记", UiTheme.PrimaryButton), 240, 56),
            Ui.MinSize(Ui.Button("设为追踪目标"), 240, 56)));
        body.AddChild(Ui.Text(q.Id, UiTheme.MutedLabel));
        host.AddChild(Ui.Panel(UiTheme.InsetPanel, body));
    }

    private static Control BuildBonds()
    {
        var detail = new MarginContainer();
        var list = Ui.Column(4);
        var group = new ButtonGroup();
        foreach (var b in JournalSamples.Bonds)
        {
            list.AddChild(Ui.ListRow(group, () => ShowBond(detail, b), b.Name, b.Source, b.Kind,
                selected: b == JournalSamples.Bonds[0]));
        }

        return Ui.Row(UiPalette.SpaceXl, Ui.MinSize(list, 520), Ui.Expand(detail));
    }

    private static void ShowBond(Container host, SampleBond b)
    {
        Ui.ClearChildren(host);
        var events = Ui.Column(UiPalette.SpaceS);
        foreach (var e in b.Events)
        {
            events.AddChild(Ui.Text($"·　{e}", wrap: true));
        }

        var body = Ui.Column(UiPalette.SpaceM,
            Ui.Text(b.Source, UiTheme.MutedLabel),
            Ui.Text(b.Name, UiTheme.TitleLabel),
            Ui.Row(UiPalette.SpaceL, Ui.MinSize(Ui.Text("好感"), 80), Ui.Bar(UiTheme.HealthBar, b.Affection, 100, 360), Ui.Text($"{b.Affection}")),
            Ui.Row(UiPalette.SpaceL, Ui.MinSize(Ui.Text("信任"), 80), Ui.Bar(UiTheme.InnerBar, b.Trust, 100, 360), Ui.Text($"{b.Trust}")),
            Ui.Text($"关系　{b.Kind}", UiTheme.AccentLabel),
            Ui.Rule(),
            Ui.Text("共同经历", UiTheme.MutedLabel),
            events,
            Ui.Rule(),
            Ui.Text("立场与承诺按事件记录，不换算成分数；本章尚无承诺。", UiTheme.MutedLabel, wrap: true));
        host.AddChild(Ui.Panel(UiTheme.InsetPanel, body));
    }

    private static Control BuildNotes()
    {
        var detail = new MarginContainer();
        var list = Ui.Column(4);
        var rows = new ButtonGroup();

        void Fill(string? source)
        {
            Ui.ClearChildren(list);
            var first = true;
            foreach (var n in JournalSamples.Notes.Where(n => source is null || n.Source == source))
            {
                list.AddChild(Ui.ListRow(rows, () => ShowNote(detail, n), n.Title, n.Status,
                    null, SourceGlyph(n.Source), first));
                first = false;
            }
        }

        var chips = new ButtonGroup();
        var filters = Ui.Row(UiPalette.SpaceS, Ui.Toggle("全部", UiTheme.ChipButton, chips, () => Fill(null), true));
        foreach (var s in new[] { "亲见", "听闻", "推测" })
        {
            filters.AddChild(Ui.Toggle(s, UiTheme.ChipButton, chips, () => Fill(s)));
        }

        return Ui.Row(UiPalette.SpaceXl,
            Ui.MinSize(Ui.Column(UiPalette.SpaceM, filters, list,
                Ui.Text("亲见是自己看到的，听闻来自他人，推测只是你的想法。", UiTheme.MutedLabel, wrap: true)), 520),
            Ui.Expand(detail));
    }

    private static void ShowNote(Container host, SampleNote n)
    {
        Ui.ClearChildren(host);
        var body = Ui.Column(UiPalette.SpaceM,
            Ui.Row(UiPalette.SpaceL, SourceGlyph(n.Source, 72), Ui.Column(UiPalette.SpaceS,
                Ui.Text(n.Title, UiTheme.SectionLabel),
                Ui.Text($"{n.Source}　　{n.Status}", UiTheme.MutedLabel))),
            Ui.Text(n.Detail, wrap: true),
            Ui.Rule(),
            Ui.Text(n.QuestId is null ? "尚未关联任何任务" : $"关联任务　{JournalSamples.Quests.First(q => q.Id == n.QuestId).Name}",
                UiTheme.MutedLabel));
        if (n.Source == "推测")
        {
            body.AddChild(Ui.Text("推测不会被人物当作证据引用，除非找到亲见或文书佐证。", UiTheme.AccentLabel, wrap: true));
        }

        host.AddChild(Ui.Panel(UiTheme.InsetPanel, body));
    }

    private static PanelContainer SourceGlyph(string source, int size = 48) => Ui.Glyph(source[..1], source switch
    {
        "亲见" => UiPalette.Text,
        "听闻" => UiPalette.Boost,
        _ => UiPalette.Accent,
    }, size);
}
