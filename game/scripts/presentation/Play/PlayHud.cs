using Godot;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Pages;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 探索 HUD 的游戏版（版式同 docs/art/UI_DESIGN.md 第 5.3 节、M0 已验收的 <see cref="ExploreHudKit"/>）：
/// 目标追踪读已提交的世界状态，每次提交后整体重建。左上地点栏 2026-10-25 按用户要求删去，时辰改写在右上小地图下方（<see cref="ExploreStage.ClockText"/>）。
/// 不设常驻快捷键栏（2026-10-02 用户要求），2026-10-25 起按“只留必须的提示”再精简：追踪框不带札记键帽、主线只列当前目标，
/// 没有进行中的任务时整块不显示；左下队伍与银两条删去（菜单顶栏与暂停页可查）。按键只在需要时出现（交互提示的 E）。
/// </summary>
public static class PlayHud
{
    public static Control Build(PlaySession play)
    {
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        if (Tracker(play) is { } tracker)
        {
            root.AddChild(tracker);
        }

        return root;
    }

    /// <summary>左侧：进行中的主线（当前阶段的目标；阶段没有列出目标时写阶段本身）与进行中的支线当前阶段；没有进行中的任务时为 null。</summary>
    private static Control? Tracker(PlaySession play)
    {
        var w = play.Game.World;
        var content = play.Game.Rules.Content;
        var list = Ui.Column(UiPalette.SpaceS);
        var quests = content.Quests.Values
            .Where(q => w.QuestStatusOf(q.Id) is QuestStatus.Active)
            .OrderBy(q => q.Kind == QuestKind.Main ? 0 : 1).ThenBy(q => q.Priority).ThenBy(q => q.Id, StringComparer.Ordinal)
            .ToList();
        if (quests.Count == 0)
        {
            return null;
        }

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

            if (!main || stage.Objectives.Count == 0)
            {
                list.AddChild(Ui.Text(play.Text($"{q.Id}.stage.{stageId}") ?? stageId, main ? UiTheme.DarkLabel : UiTheme.DarkMutedLabel, main ? 19 : 17, wrap: true));
                continue;
            }

            foreach (var o in stage.Objectives)
            {
                var done = progress.Objectives.Contains(o.Id);
                var text = play.Text($"{q.Id}.objective.{o.Id}") ?? o.Id;
                list.AddChild(Step(done ? "✓" : "○", text + (o.Optional ? "（可选）" : ""), done ? UiTheme.DarkMutedLabel : UiTheme.DarkLabel, done ? 16 : 18));
            }
        }

        var panel = Ui.Panel(UiTheme.GlassPanel, list);

        // 面板高度随内容：放在顶端对齐的竖排里，不撑满整个版位。
        panel.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddChild(panel);
        return Ui.Place(column, 0, 0, 40, 32, 480, 512);
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
}
