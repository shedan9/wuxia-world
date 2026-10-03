using Godot;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 江湖札记（菜单“札记”分区，M3-05）：绢本页，子页签 任务 · 线索 · 人物 · 见闻 · 对话，版式沿用 M0 已验收的札记页
/// （左列表 520 宽、右详情玉版）。任务、线索与人物读已提交的世界状态；见闻是本局推过的通知（几秒后淡出的那些），
/// 对话是本局显示过的台词与选择，两者都不存档。
/// </summary>
public sealed class JournalPage
{
    private static readonly string[] TabNames = ["任务", "线索", "人物", "见闻", "对话"];

    private static readonly string[] NoticeKinds = ["任务", "目标", "线索", "物品", "银两", "关系", "同行", "武学", "成长"];

    private readonly PlaySession _play;
    private readonly VBoxContainer _root = Ui.Column(UiPalette.SpaceL);
    private readonly MarginContainer _body = Ui.Expand(new MarginContainer(), vertical: true);
    private string? _noticeKind;

    private JournalPage(PlaySession play)
    {
        _play = play;

        // 内容底边让开绢页四角的卷云纹。
        _body.AddThemeConstantOverride("margin_bottom", UiPalette.SpaceL);
    }

    private WorldState World => _play.Game.World;
    private WorldContent Content => _play.Game.Rules.Content;

    /// <summary>上次打开的子页签；本局内记住，重开札记回到原处。</summary>
    private static int _lastTab;

    public static Control Build(PlaySession play, int? tab = null)
    {
        var page = new JournalPage(play);
        page.Assemble(Math.Clamp(tab ?? _lastTab, 0, TabNames.Length - 1));
        return page._root;
    }

    private void Assemble(int tab)
    {
        var strip = Ui.Row(UiPalette.SpaceS);
        var group = new ButtonGroup();
        for (var i = 0; i < TabNames.Length; i++)
        {
            var index = i;
            strip.AddChild(Ui.Toggle(TabNames[i], UiTheme.SubTab, group, () => Show(index), i == tab));
        }

        strip.AddChild(Ui.Spacer());
        var sheet = Ui.Panel(UiTheme.SheetPanel, Ui.Column(UiPalette.SpaceS, strip, Ui.Rule(), _body));
        _root.AddChild(Ui.Expand(sheet, vertical: true));
        Show(tab);
    }

    private void Show(int tab)
    {
        _lastTab = tab;
        Ui.ClearChildren(_body);
        _body.AddChild(tab switch
        {
            1 => Clues(),
            2 => People(),
            3 => Notices(),
            4 => Dialogue(),
            _ => Quests(),
        });
    }

    /// <summary>左列表、右详情的两栏；列表与详情各自滚动。</summary>
    private static Control TwoColumns(Control list, Control detail)
    {
        var left = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        left.AddChild(Ui.Expand(list));
        return Ui.Row(UiPalette.SpaceXl, Ui.Expand(Ui.MinSize(left, 520), horizontal: false, vertical: true), Ui.Expand(detail));
    }

    private static Control Empty(string text) => Ui.Text(text, UiTheme.MutedLabel, wrap: true);

    // ── 任务 ─────────────────────────────────────────────

    private Control Quests()
    {
        var detail = Ui.Expand(new MarginContainer(), vertical: true);
        var list = Ui.Column(4);
        var group = new ButtonGroup();
        var shown = Content.Quests.Values.Where(q => World.QuestStatusOf(q.Id) is not QuestStatus.Locked).ToList();
        var first = true;
        void Group(string title, IEnumerable<QuestDefinition> quests)
        {
            var items = quests.OrderBy(q => q.Kind == QuestKind.Main ? 0 : 1).ThenBy(q => q.Id, StringComparer.Ordinal).ToList();
            if (items.Count == 0)
            {
                return;
            }

            list.AddChild(Ui.Text(title, UiTheme.MutedLabel));
            foreach (var q in items)
            {
                var quest = q;
                list.AddChild(Ui.ListRow(group, () => ShowQuest(detail, quest), _play.Name(q.Id), q.Kind == QuestKind.Main ? "主线" : "支线",
                    StatusText(World.QuestStatusOf(q.Id)), selected: first));
                if (first)
                {
                    ShowQuest(detail, quest);
                    first = false;
                }
            }
        }

        Group("进行中", shown.Where(q => World.QuestStatusOf(q.Id) == QuestStatus.Active));
        Group("可接", shown.Where(q => World.QuestStatusOf(q.Id) == QuestStatus.Available));
        Group("已结束", shown.Where(q => World.QuestStatusOf(q.Id) is QuestStatus.Completed or QuestStatus.Failed or QuestStatus.Abandoned));
        if (first)
        {
            list.AddChild(Empty("还没有接下任何事。"));
        }

        return TwoColumns(list, detail);
    }

    private static string StatusText(QuestStatus s) => s switch
    {
        QuestStatus.Available => "可接",
        QuestStatus.Active => "进行中",
        QuestStatus.Completed => "已完成",
        QuestStatus.Failed => "失败",
        QuestStatus.Abandoned => "已放弃",
        _ => "",
    };

    private void ShowQuest(Container host, QuestDefinition q)
    {
        Ui.ClearChildren(host);
        var status = World.QuestStatusOf(q.Id);
        var body = Ui.Column(UiPalette.SpaceM,
            Ui.Text($"{(q.Kind == QuestKind.Main ? "主线" : "支线")}　　{StatusText(status)}", UiTheme.MutedLabel),
            Ui.Text(_play.Name(q.Id), UiTheme.TitleLabel),
            Ui.Text(_play.Text(q.Id + ".desc") ?? "", wrap: true));

        if (status == QuestStatus.Available && q.Hints.Count > 0)
        {
            body.AddChild(Ui.Rule());
            body.AddChild(Ui.Text("传闻", UiTheme.MutedLabel));
            foreach (var hint in q.Hints)
            {
                body.AddChild(Ui.Text(_play.Text(hint) ?? hint, wrap: true));
            }
        }

        if (World.Quests.TryGetValue(q.Id, out var p))
        {
            body.AddChild(Ui.Rule());
            body.AddChild(Ui.Text("进展", UiTheme.MutedLabel));
            var active = p.Status == QuestStatus.Active;
            foreach (var stage in p.History.Where(s => !(active && s == p.Stage)))
            {
                body.AddChild(Ui.Text("✓　" + (_play.Text($"{q.Id}.stage.{stage}") ?? stage), UiTheme.MutedLabel, wrap: true));
            }

            if (p.Stage is { } current && (active || !p.History.Contains(current)))
            {
                body.AddChild(Ui.Text((active ? "▶　" : "✓　") + (_play.Text($"{q.Id}.stage.{current}") ?? current),
                    active ? UiTheme.AccentLabel : UiTheme.MutedLabel, wrap: true));
            }
        }

        host.AddChild(Ui.Panel(UiTheme.InsetPanel, KeyScroll.Of(body)));
    }

    // ── 线索 ─────────────────────────────────────────────

    private Control Clues()
    {
        var detail = Ui.Expand(new MarginContainer(), vertical: true);
        var list = Ui.Column(4);
        var group = new ButtonGroup();
        var first = true;
        foreach (var id in World.Clues)
        {
            var clue = id;
            list.AddChild(Ui.ListRow(group, () => ShowClue(detail, clue), _play.Name(id), selected: first));
            if (first)
            {
                ShowClue(detail, clue);
                first = false;
            }
        }

        if (first)
        {
            list.AddChild(Empty("尚无线索。"));
        }

        return TwoColumns(list, detail);
    }

    private void ShowClue(Container host, string id)
    {
        Ui.ClearChildren(host);
        var body = Ui.Column(UiPalette.SpaceM, Ui.Text(_play.Name(id), UiTheme.TitleLabel),
            Ui.Text(_play.Text(id + ".desc") ?? "（未写说明）", wrap: true));
        host.AddChild(Ui.Panel(UiTheme.InsetPanel, KeyScroll.Of(body)));
    }

    // ── 人物 ─────────────────────────────────────────────

    private Control People()
    {
        var detail = Ui.Expand(new MarginContainer(), vertical: true);
        var list = Ui.Column(4);
        var group = new ButtonGroup();
        var first = true;
        foreach (var id in World.Met.Where(id => id != World.Hero))
        {
            var who = id;
            var source = Content.Characters.GetValueOrDefault(id)?.SourceWork is { } work ? $"《{_play.Name(work)}》" : "本作人物";
            list.AddChild(Ui.ListRow(group, () => ShowPerson(detail, who), _play.Name(id), source,
                World.Party.Contains(id) ? "同行中" : null, selected: first));
            if (first)
            {
                ShowPerson(detail, who);
                first = false;
            }
        }

        if (first)
        {
            list.AddChild(Empty("还没有结识什么人。"));
        }

        return TwoColumns(list, detail);
    }

    private void ShowPerson(Container host, string id)
    {
        Ui.ClearChildren(host);
        var r = World.Relationships.GetValueOrDefault(id);
        var source = Content.Characters.GetValueOrDefault(id)?.SourceWork is { } work ? $"出自《{_play.Name(work)}》" : "本作人物";
        var body = Ui.Column(UiPalette.SpaceM,
            Ui.Text(source, UiTheme.MutedLabel),
            Ui.Text(_play.Name(id), UiTheme.TitleLabel),
            Ui.Row(UiPalette.SpaceL, Ui.MinSize(Ui.Text("信任"), 80), Ui.Text($"{r?.Trust ?? 0}", UiTheme.AccentLabel)),
            Ui.Row(UiPalette.SpaceL, Ui.MinSize(Ui.Text("好感"), 80), Ui.Text($"{r?.Affection ?? 0}", UiTheme.AccentLabel)));
        var status = World.Party.Contains(id) ? "同行中" : World.Reservations.ContainsKey(id) ? "另有要事" : World.Companions.ContainsKey(id) ? "已离队" : "相识";
        body.AddChild(Ui.Text($"现况　{status}", UiTheme.MutedLabel));
        if (r is { Commitments.Count: > 0 })
        {
            body.AddChild(Ui.Rule());
            body.AddChild(Ui.Text("承诺与立场", UiTheme.MutedLabel));
            foreach (var c in r.Commitments)
            {
                body.AddChild(Ui.Text("·　" + (_play.Text(c + ".name") ?? c), wrap: true));
            }
        }

        body.AddChild(Ui.Rule());
        body.AddChild(Ui.Text("信任与好感随共同经历变化；立场与承诺按事件记录，不换算成分数。", UiTheme.MutedLabel, wrap: true));
        host.AddChild(Ui.Panel(UiTheme.InsetPanel, KeyScroll.Of(body)));
    }

    // ── 见闻（通知记录）──────────────────────────────────

    private Control Notices()
    {
        var list = Ui.Column(UiPalette.SpaceS);
        void Fill()
        {
            Ui.ClearChildren(list);
            var shown = _play.Notices.Where(n => _noticeKind is null || n.Kind == _noticeKind || _noticeKind == "物品" && n.Kind == "银两" || _noticeKind == "任务" && n.Kind == "目标").Reverse().ToList();
            long? day = null;
            foreach (var (clock, kind, text) in shown)
            {
                if (clock != day)
                {
                    day = clock;
                    list.AddChild(Ui.Text(PlaySession.ClockText(clock), UiTheme.MutedLabel, 18));
                }

                list.AddChild(Ui.Row(UiPalette.SpaceM, Ui.MinSize(Ui.Text(kind, UiTheme.AccentLabel, 20), 64), Ui.Text(text, wrap: true)));
            }

            if (shown.Count == 0)
            {
                list.AddChild(Empty(_play.Notices.Count == 0 ? "本次游玩还没有见闻。读档后从读档处重新记起。" : "这一类还没有见闻。"));
            }
        }

        var chips = new ButtonGroup();
        var filters = Ui.Row(UiPalette.SpaceS, Ui.Toggle("全部", UiTheme.ChipButton, chips, () => { _noticeKind = null; Fill(); }, _noticeKind is null));
        foreach (var kind in NoticeKinds.Where(k => k != "银两" && k != "目标"))
        {
            var k = kind;
            filters.AddChild(Ui.Toggle(kind, UiTheme.ChipButton, chips, () => { _noticeKind = k; Fill(); }, _noticeKind == kind));
        }

        Fill();
        return Ui.Column(UiPalette.SpaceM, filters,
            Ui.Text("通知几秒后就会淡出，漏看的都记在这里，新的在上。只记本次游玩，不随存档保存。", UiTheme.MutedLabel, 18, wrap: true),
            KeyScroll.Of(list));
    }

    // ── 对话记录 ─────────────────────────────────────────

    private Control Dialogue()
    {
        var list = Ui.Column(UiPalette.SpaceM);
        foreach (var (speaker, text) in _play.History.TakeLast(300))
        {
            var chosen = speaker == "选择";
            var line = Ui.Row(UiPalette.SpaceL,
                Ui.MinSize(Ui.Text(chosen ? "▸ 你选择" : speaker, chosen ? UiTheme.AccentLabel : UiTheme.SectionLabel, 20), 180),
                Ui.Text(text, chosen ? UiTheme.AccentLabel : null, wrap: true));
            list.AddChild(line);
        }

        if (_play.History.Count == 0)
        {
            list.AddChild(Empty("本次游玩还没有对话。"));
        }

        var scroll = KeyScroll.Of(list);
        scroll.ScrollToEnd();
        return Ui.Column(UiPalette.SpaceM,
            Ui.Text($"本次游玩说过的话与你的选择，旧的在上、新的在下（最多 300 句）。对话中按 {App.KeyBindings.Label("dialogue_log")} 可随时翻看。", UiTheme.MutedLabel, 18, wrap: true),
            scroll);
    }
}
