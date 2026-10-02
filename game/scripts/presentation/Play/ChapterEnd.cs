using Godot;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 章终回顾（第一章主线完成后出现一次）：把这一章里玩家做过的选择与结果列出来——讨教对象、同行者与水门援手、
/// 失踪渡工支线、转信副页的处置、结识的人物与所得——再给出“继续游历”（留在客栈自由走动、可补做支线）与“返回标题”。
/// 只读已提交的世界状态，不改任何东西；第二章尚未制作，页底如实写明。
/// </summary>
public static class ChapterEnd
{
    public static Control Build(PlaySession play, Action stay)
    {
        var w = play.Game.World;
        string Fact(string id) => w.Facts.TryGetValue(id, out var v) ? v : "";
        string Who(string key) => key switch
        {
            "linghu" => play.Name("char.linghu_chong"),
            "huang" => play.Name("char.huang_rong"),
            "xiao" => play.Name("char.xiao_feng"),
            _ => "",
        };

        var rows = Ui.Column(UiPalette.SpaceM);
        void Row(string label, string value)
        {
            var name = Ui.MinSize(Ui.Text(label, UiTheme.DarkMutedLabel, 20), 140);
            name.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            rows.AddChild(Ui.Row(UiPalette.SpaceL, name, Ui.Expand(Ui.Text(value, UiTheme.DarkLabel, 22, wrap: true))));
        }

        var skill = w.Skills.FirstOrDefault(s => s.StartsWith("skill.", StringComparison.Ordinal));
        if (Who(Fact("fact.ch01.mentor")) is { Length: > 0 } mentor)
        {
            Row("讨教", $"天亮前向{mentor}讨教{(skill is null ? "" : $"，习得「{play.Combat.Name(skill)}」")}");
        }

        if (Who(Fact("fact.ch01.companion")) is { Length: > 0 } companion)
        {
            var helper = Who(Fact("fact.ch01.helper"));
            Row("同行", $"{companion}同赴旧渡{(helper.Length > 0 ? $"；{helper}赶到水门援手" : "")}");
        }

        Row("失踪渡工", Fact("fact.ch01.side01_result") switch
        {
            "full" => "循船牌与潮痕查到旧渡，抢在开闸前救出杜三篙",
            "late" => "杜三篙救回来了，只是晚了一步，陆青禾心里有些过意不去",
            _ => Fact("fact.ch01.ferryman_freed") == "true" ? "没有接陆青禾的委托；杜三篙在水门一战后获救" : "没有接陆青禾的委托",
        });

        Row("转信副页", Fact("fact.ch01.copy_custody") switch
        {
            "public" => "带回客栈当众念出；签押人人看见，当夜便有人出城报信",
            "sealed" => "交杜三篙密封保管；船夫的名字没有传出去",
            _ => "—",
        });

        var met = w.Met.Where(id => id != w.Hero).Select(play.Name).ToList();
        if (met.Count > 0)
        {
            Row("结识", string.Join("、", met));
        }

        Row("所得", $"经验 {w.Experience}　·　银 {w.Silver} 两　·　线索 {w.Clues.Count} 条");

        var title = Ui.Text("第一章　江南会客", UiTheme.DisplayLabel, 60);
        title.AddThemeColorOverride("font_color", UiPalette.TextOnDark);
        var close = Ui.Text("章　终", UiTheme.GiltLabel, 26);
        var head = Ui.Row(UiPalette.SpaceL, Ui.Seal("章终"), Ui.Column(4, title, close));

        var next = Ui.Text("第二章尚在制作中。现在可以继续在芦湾走动、补做没做完的事，或回到标题。", UiTheme.DarkMutedLabel, 18, wrap: true);
        var stayButton = Ui.Button("继续游历", UiTheme.PrimaryButton, stay);
        var titleButton = Ui.Button("返回标题", UiTheme.DarkButton, () =>
        {
            play.AutoSave(SaveThumbnail.Grab(AppHost.Instance.GetViewport()));
            AppHost.Instance.Play = null;
            AppHost.Instance.Router.GoTo(ScenePaths.MainMenu);
        });
        foreach (var b in new[] { stayButton, titleButton })
        {
            b.CustomMinimumSize = new Vector2(220, 60);
            b.AddThemeFontSizeOverride("font_size", 24);
            b.MouseEntered += b.GrabFocus;
        }

        stayButton.CallDeferred(Control.MethodName.GrabFocus);
        return Ui.Column(UiPalette.SpaceL, head, Ui.Rule(dark: true), Ui.Expand(rows, vertical: true), Ui.Rule(dark: true), next,
            Ui.Row(UiPalette.SpaceM, Ui.Spacer(), titleButton, stayButton));
    }
}
