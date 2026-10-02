using Godot;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 江湖札记（J）：任务（走过的阶段与当前阶段）、线索与人物关系，全部读已提交的世界状态；行囊与武学另有专页（I、C）。
/// 正式的分区菜单版式（UI_DESIGN 5.2 菜单外框）属 M3-05；此处先以一页暗色面板呈现，便于试玩核对状态。
/// </summary>
public static class Journal
{
    public static Control Build(PlaySession play)
    {
        var w = play.Game.World;
        var content = play.Game.Rules.Content;

        var quests = Ui.Column(UiPalette.SpaceM, Ui.Section("任务", dark: true));
        var shown = content.Quests.Values
            .Where(q => w.QuestStatusOf(q.Id) is not QuestStatus.Locked)
            .OrderBy(q => w.QuestStatusOf(q.Id) == QuestStatus.Active ? 0 : 1).ThenBy(q => q.Kind == QuestKind.Main ? 0 : 1).ThenBy(q => q.Id, StringComparer.Ordinal);
        foreach (var q in shown)
        {
            quests.AddChild(Quest(play, q, w));
        }

        var clues = Ui.Column(UiPalette.SpaceS, Ui.Section("线索", dark: true));
        foreach (var id in w.Clues)
        {
            clues.AddChild(Entry(play.Name(id), play.Text(id + ".desc")));
        }

        if (w.Clues.Count == 0)
        {
            clues.AddChild(Ui.Text("尚无线索", UiTheme.DarkMutedLabel, 18));
        }


        var people = Ui.Column(UiPalette.SpaceS, Ui.Section("人物", dark: true));
        foreach (var id in w.Met.Where(id => id != w.Hero))
        {
            var r = w.Relationships.TryGetValue(id, out var rel) ? rel : null;
            var party = w.Party.Contains(id) ? "　同行中" : "";
            var note = r is null || (r.Trust == 0 && r.Affection == 0) ? "" : $"信任 {r.Trust}　好感 {r.Affection}";
            people.AddChild(Ui.Row(UiPalette.SpaceM, Ui.MinSize(Ui.Text(play.Name(id) + party, UiTheme.DarkLabel, 20), 220),
                Ui.Text(note, UiTheme.DarkMutedLabel, 18)));
        }

        var level = play.Game.Rules.LevelOf(w.Experience);
        var growth = Ui.Column(UiPalette.SpaceS, Ui.Section("成长", dark: true),
            Ui.Text($"第 {level} 级　经验 {w.Experience}　修为 {w.Cultivation}　银 {w.Silver} 两", UiTheme.DarkLabel, 20),
            Ui.Text("属性、武学与装备见人物页（C），物品见行囊（I）。", UiTheme.DarkMutedLabel, 18, wrap: true));

        var left = Scroll(quests);
        var right = Scroll(Ui.Column(UiPalette.SpaceXl, growth, clues, people));
        var header = Ui.Row(UiPalette.SpaceL, Ui.Seal("札记"), Ui.Column(4,
                Ui.Text("江湖札记", UiTheme.DarkTitleLabel, 40),
                Ui.Text($"{play.Name(w.MapId)}　·　{PlaySession.ClockText(w.Clock)}", UiTheme.DarkMutedLabel, 18)),
            Ui.Spacer(), Ui.KeyHints(true, ("Esc", "关闭")));
        return Ui.Column(UiPalette.SpaceL, header, Ui.Rule(dark: true),
            Ui.Expand(Ui.Row(UiPalette.SpaceXxl, Ui.Expand(left), Ui.Expand(right)), vertical: true));
    }

    private static Control Quest(PlaySession play, QuestDefinition q, WorldState w)
    {
        var p = w.Quests.TryGetValue(q.Id, out var progress) ? progress : null;
        var status = w.QuestStatusOf(q.Id) switch
        {
            QuestStatus.Available => "可接",
            QuestStatus.Active => "进行中",
            QuestStatus.Completed => "已完成",
            QuestStatus.Failed => "失败",
            QuestStatus.Abandoned => "已放弃",
            _ => "",
        };
        var kind = q.Kind == QuestKind.Main ? "主线" : "支线";
        var column = Ui.Column(UiPalette.SpaceS,
            Ui.Row(UiPalette.SpaceM, Ui.Text($"{kind}　{play.Name(q.Id)}", UiTheme.GiltLabel, 24), Ui.Spacer(), Ui.Text(status, UiTheme.DarkMutedLabel, 18)),
            Ui.Text(play.Text(q.Id + ".desc") ?? "", UiTheme.DarkMutedLabel, 18, wrap: true));
        if (w.QuestStatusOf(q.Id) == QuestStatus.Available)
        {
            foreach (var hint in q.Hints)
            {
                column.AddChild(Ui.Text("传闻：" + (play.Text(hint) ?? hint), UiTheme.DarkLabel, 18, wrap: true));
            }
        }

        if (p is not null)
        {
            var active = p.Status == QuestStatus.Active;
            foreach (var stage in p.History.Where(s => !(active && s == p.Stage)))
            {
                column.AddChild(Ui.Text("✓　" + (play.Text($"{q.Id}.stage.{stage}") ?? stage), UiTheme.DarkMutedLabel, 18, wrap: true));
            }

            if (p.Stage is { } current && (active || !p.History.Contains(current)))
            {
                column.AddChild(Ui.Text((active ? "○　" : "✓　") + (play.Text($"{q.Id}.stage.{current}") ?? current), UiTheme.DarkLabel, 20, wrap: true));
            }
        }

        return Ui.Panel(UiTheme.GlassPanel, column);
    }

    private static Control Entry(string title, string? detail)
    {
        var column = Ui.Column(2, Ui.Text(title, UiTheme.DarkLabel, 20));
        if (!string.IsNullOrEmpty(detail))
        {
            column.AddChild(Ui.Text(detail, UiTheme.DarkMutedLabel, 17, wrap: true));
        }

        return column;
    }

    private static ScrollContainer Scroll(Control content)
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(Ui.Expand(content));
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return scroll;
    }
}
