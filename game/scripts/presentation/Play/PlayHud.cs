using Godot;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Pages;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 探索 HUD 的游戏版（版式同 docs/art/UI_DESIGN.md 第 5.3 节、M0 已验收的 <see cref="ExploreHudKit"/>）：
/// 地点与时辰、目标追踪、队伍全部读已提交的世界状态，每次提交后整体重建。
/// 不设常驻快捷键栏（2026-10-02 用户要求）：按键只在需要时出现（交互提示的 E、追踪框的 J）。
/// </summary>
public static class PlayHud
{
    public static Control Build(PlaySession play)
    {
        var w = play.Game.World;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(ExploreHudKit.Place("芦湾", play.Name(w.MapId), PlaySession.ClockText(w.Clock)));
        root.AddChild(Tracker(play));
        root.AddChild(Party(play));
        return root;
    }

    /// <summary>左侧：进行中的主线（走过的阶段打勾，当前阶段列出目标）与进行中的支线当前阶段。</summary>
    private static Control Tracker(PlaySession play)
    {
        var w = play.Game.World;
        var content = play.Game.Rules.Content;
        var list = Ui.Column(UiPalette.SpaceS);
        var quests = content.Quests.Values
            .Where(q => w.QuestStatusOf(q.Id) is QuestStatus.Active)
            .OrderBy(q => q.Kind == QuestKind.Main ? 0 : 1).ThenBy(q => q.Priority).ThenBy(q => q.Id, StringComparer.Ordinal)
            .ToList();
        var first = true;
        foreach (var q in quests)
        {
            var progress = w.Quests[q.Id];
            var main = q.Kind == QuestKind.Main;
            if (!first)
            {
                list.AddChild(Ui.Rule(dark: true));
            }

            first = false;
            var kind = main ? "主线" : "支线";
            list.AddChild(Ui.Row(UiPalette.SpaceS, Ui.Text(main ? "◆" : "◇", UiTheme.GiltLabel, 16),
                Ui.Text($"{kind}　{play.Name(q.Id)}", main ? UiTheme.GiltLabel : UiTheme.DarkMutedLabel, main ? 20 : 18)));
            if (progress.Stage is not { } stageId || q.StageById(stageId) is not { } stage)
            {
                continue;
            }

            list.AddChild(Ui.Text(play.Text($"{q.Id}.stage.{stageId}") ?? stageId, main ? UiTheme.DarkLabel : UiTheme.DarkMutedLabel, main ? 19 : 17, wrap: true));
            if (!main)
            {
                continue;
            }

            foreach (var o in stage.Objectives)
            {
                var done = progress.Objectives.Contains(o.Id);
                var text = play.Text($"{q.Id}.objective.{o.Id}") ?? o.Id;
                list.AddChild(Step(done ? "✓" : "○", text + (o.Optional ? "（可选）" : ""), done ? UiTheme.DarkMutedLabel : UiTheme.DarkLabel, done ? 16 : 18));
            }
        }

        if (quests.Count == 0)
        {
            var done = content.Quests.Values.FirstOrDefault(q => q.Kind == QuestKind.Main && w.QuestStatusOf(q.Id) == QuestStatus.Completed);
            list.AddChild(Ui.Text(done is null ? "暂无进行中的任务" : $"已完成：{play.Name(done.Id)}", UiTheme.DarkMutedLabel, 18));
        }

        list.AddChild(Ui.KeyHint("J", "札记"));
        var panel = Ui.Panel(UiTheme.GlassPanel, list);

        // 面板高度随内容：放在顶端对齐的竖排里，不撑满整个版位。
        panel.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddChild(panel);
        return Ui.Place(column, 0, 0, 40, 220, 480, 700);
    }

    /// <summary>
    /// 一条目标：勾 / 圈与文字分成两个标签，长目标折行时悬挂缩进在文字列内（同 M0 追踪框）。
    /// </summary>
    private static Control Step(string mark, string text, string variation, int size)
    {
        var bullet = Ui.Text(mark, variation, size);
        bullet.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        return Ui.Row(UiPalette.SpaceS, bullet, Ui.Text(text, variation, size, wrap: true));
    }

    /// <summary>左下：队伍印鉴与姓名、银两。探索中不显示气血（伤势与恢复属 M2-05 成长与物品）。</summary>
    private static Control Party(PlaySession play)
    {
        var w = play.Game.World;
        var row = Ui.Row(UiPalette.SpaceM);
        foreach (var id in w.Party)
        {
            var name = play.Name(id);
            var look = Looks.Of(id);
            var label = Ui.Text(name, UiTheme.DarkLabel, 18);
            label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(Ui.Row(UiPalette.SpaceS, Ui.Glyph(name[..1], look.Tone, 48), label));
        }

        var silver = Ui.Text($"银 {w.Silver} 两", UiTheme.GiltLabel, 18);
        silver.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(Ui.MinSize(new Control(), 8));
        row.AddChild(silver);

        // 左下角面板随人数向右长。
        var placed = Ui.Place(Ui.Panel(UiTheme.GlassPanel, row), 0, 1, 40, -120, 40, -40);
        placed.GrowHorizontal = Control.GrowDirection.End;
        return placed;
    }
}
