using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 角色 / 武学 / 成长展示页。派生属性按架构文档 7.4 的 v0 公式在页面内示意换算，
/// 不是规则实现；M1 起由 Domain 提供同名显示模型。
/// </summary>
public partial class CharacterPreview : PreviewScreen
{
    private static int _member;

    protected override string SealText => "人物";
    protected override string Title => "人物、武学与成长";
    protected override string Subtitle => "属性层级、招式装配与升级前后对比";

    protected override IReadOnlyList<(string Name, Func<Control> Build)> Tabs =>
    [
        ("属性", () => WithParty(BuildAttributes)),
        ("武学", () => WithParty(BuildSkills)),
        ("成长", () => WithParty(BuildGrowth)),
    ];

    /// <summary>页签共用的队伍选择条；切换人物只重建下方内容。</summary>
    private static Control WithParty(Func<SampleMember, Control> build)
    {
        var body = new MarginContainer();
        var group = new ButtonGroup();
        var bar = Ui.Row(UiPalette.SpaceS);
        for (var i = 0; i < CharacterSamples.Party.Count; i++)
        {
            var index = i;
            var member = CharacterSamples.Party[i];
            bar.AddChild(Ui.Toggle(member.Name, UiTheme.ChipButton, group, () =>
            {
                _member = index;
                Ui.ClearChildren(body);
                body.AddChild(build(member));
            }, i == _member));
        }

        bar.AddChild(Ui.Spacer());
        bar.AddChild(Ui.Text("队伍 4 / 4", UiTheme.MutedLabel));
        return Ui.Column(UiPalette.SpaceL, bar, body);
    }

    private static Control BuildAttributes(SampleMember m)
    {
        var info = Ui.Column(UiPalette.SpaceM,
            Ui.Text(m.Name, UiTheme.TitleLabel),
            Ui.Text($"{m.Source}　　{m.Role}", UiTheme.MutedLabel));

        if (m.Attributes is null)
        {
            info.AddChild(Pending(m));
            return Ui.Row(UiPalette.SpaceXl, Portrait(m), Ui.Expand(info));
        }

        info.AddChild(Ui.Row(UiPalette.SpaceL,
            Ui.Text($"等级 {m.Level}"),
            Ui.Bar("", 140, 300, 240),
            Ui.Text("经验 140 / 300", UiTheme.MutedLabel)));
        var stats = Derived(m.Attributes, m.Level);
        info.AddChild(Ui.Row(UiPalette.SpaceL,
            Ui.MinSize(Ui.Text("气血"), 80), Ui.Bar(UiTheme.HealthBar, stats["最大气血"] - 40, stats["最大气血"], 360),
            Ui.Text($"{stats["最大气血"] - 40} / {stats["最大气血"]}")));
        info.AddChild(Ui.Row(UiPalette.SpaceL,
            Ui.MinSize(Ui.Text("内力"), 80), Ui.Bar(UiTheme.InnerBar, stats["最大内力"], stats["最大内力"], 360),
            Ui.Text($"{stats["最大内力"]} / {stats["最大内力"]}")));
        info.AddChild(Ui.Rule());

        var basics = Ui.Column(UiPalette.SpaceS, Ui.Text("基础属性", UiTheme.SectionLabel));
        foreach (var name in CharacterSamples.AttributeNames)
        {
            basics.AddChild(StatLine(name, m.Attributes[name].ToString()));
        }

        var derived = Ui.Column(UiPalette.SpaceS, Ui.Text("战斗属性", UiTheme.SectionLabel));
        foreach (var (name, value) in stats)
        {
            derived.AddChild(StatLine(name, value.ToString()));
        }

        info.AddChild(Ui.Row(UiPalette.SpaceXxl, Ui.MinSize(basics, 320), Ui.MinSize(derived, 320),
            Ui.Expand(Ui.Panel(UiTheme.InsetPanel, Ui.Column(UiPalette.SpaceS,
                Ui.Text("主修内功", UiTheme.MutedLabel), Ui.Text("吐纳法（原创基础心法）"),
                Ui.Text("辅助心法", UiTheme.MutedLabel), Ui.Text("未装配"),
                Ui.Text("轻功", UiTheme.MutedLabel), Ui.Text("听风步"),
                Ui.Text("天赋", UiTheme.MutedLabel), Ui.Text("旁观者清：首次识破敌方意图时获得 10 势", wrap: true))))));

        return Ui.Row(UiPalette.SpaceXl, Portrait(m), Ui.Expand(info));
    }

    private static Control BuildSkills(SampleMember m)
    {
        if (m.Attributes is null)
        {
            return Ui.Row(UiPalette.SpaceXl, Portrait(m), Ui.Expand(Ui.Column(UiPalette.SpaceM,
                Ui.Text(m.Name, UiTheme.TitleLabel),
                Ui.Text("经典武学须在原著资料核对后映射到战斗模板（架构文档 8.3），此处暂不展示。", wrap: true),
                Pending(m))));
        }

        if (m.Id != "char.hero")
        {
            return Ui.Text($"{m.Name}的武学页与主角共用同一版式，M0 只展示主角样例。", UiTheme.MutedLabel);
        }

        var detail = new MarginContainer();
        var slots = new GridContainer { Columns = 6 };
        slots.AddThemeConstantOverride("h_separation", UiPalette.SpaceM);
        foreach (var id in CharacterSamples.EquippedActive)
        {
            var skill = CharacterSamples.HeroSkills.FirstOrDefault(s => s.Id == id);
            var tile = skill is null ? Ui.Glyph("空", UiPalette.TextMuted with { A = 0.5f }, 72) : SkillGlyph(skill, 72);
            var caption = Ui.Text(skill?.Name ?? "空槽", skill is null ? UiTheme.MutedLabel : null, UiPalette.FontSecondary);
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            slots.AddChild(Ui.Column(UiPalette.SpaceS, tile, caption));
        }

        var list = Ui.Column(4);
        var group = new ButtonGroup();
        var first = true;
        foreach (var skill in CharacterSamples.HeroSkills)
        {
            var equipped = CharacterSamples.EquippedActive.Contains(skill.Id);
            list.AddChild(Ui.ListRow(group, () => ShowSkill(detail, skill, equipped), skill.Name,
                $"{skill.School}　{skill.Cost}", equipped ? "已装配" : null, SkillGlyph(skill, 48), first));
            first = false;
        }

        var left = Ui.MinSize(Ui.Column(UiPalette.SpaceM,
            Ui.Text("主动招式　5 / 6", UiTheme.MutedLabel), slots, Ui.Rule(),
            Ui.Text("已习得", UiTheme.MutedLabel), list), 620);
        return Ui.Row(UiPalette.SpaceXl, left, Ui.Expand(detail));
    }

    private static void ShowSkill(Container host, SampleSkill skill, bool equipped)
    {
        Ui.ClearChildren(host);
        var mastery = new string('●', skill.Mastery) + new string('○', 5 - skill.Mastery);
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", UiPalette.SpaceXl);
        grid.AddThemeConstantOverride("v_separation", UiPalette.SpaceS);
        foreach (var (k, v) in new[] { ("消耗", skill.Cost), ("目标", skill.Target), ("冷却", skill.Cooldown), ("熟练", $"{mastery}　第 {skill.Mastery} 阶") })
        {
            grid.AddChild(Ui.Text(k, UiTheme.MutedLabel));
            grid.AddChild(Ui.Text(v));
        }

        var body = Ui.Column(UiPalette.SpaceM,
            Ui.Row(UiPalette.SpaceL, SkillGlyph(skill, 96), Ui.Column(UiPalette.SpaceS,
                Ui.Text(skill.Name, UiTheme.SectionLabel),
                Ui.Text($"{skill.School}　　{skill.Id}", UiTheme.MutedLabel))),
            grid,
            Ui.Rule(),
            Ui.Text(skill.Effect, wrap: true));

        if (skill.NextVariant is var (a, b) && a is not null)
        {
            var choice = new ButtonGroup();
            body.AddChild(Ui.Text($"升至第 {skill.Mastery + 1} 阶时二选一变化式", UiTheme.AccentLabel));
            body.AddChild(Ui.Row(UiPalette.SpaceM,
                Ui.Toggle(a, UiTheme.ChipButton, choice, () => { }, true),
                Ui.Toggle(b, UiTheme.ChipButton, choice, () => { })));
        }

        var passive = skill.Cost == "被动";
        body.AddChild(Ui.Row(UiPalette.SpaceM,
            Ui.MinSize(Ui.Button(equipped ? "卸下" : "装配", equipped ? null : UiTheme.PrimaryButton, disabled: passive), 200, 56),
            Ui.MinSize(Ui.Button("演示动作", disabled: true, tooltip: "动作预览在战斗页"), 200, 56)));
        if (passive)
        {
            body.AddChild(Ui.Text("轻功在“轻功”栏装配，不占主动招式格。", UiTheme.MutedLabel));
        }

        host.AddChild(Ui.Panel(UiTheme.InsetPanel, body));
    }

    private static Control BuildGrowth(SampleMember m)
    {
        if (m.Attributes is null)
        {
            return Ui.Row(UiPalette.SpaceXl, Portrait(m), Ui.Expand(Ui.Column(UiPalette.SpaceM,
                Ui.Text(m.Name, UiTheme.TitleLabel),
                Ui.Text("暂时同行的经典人物不由玩家分配潜能，成长随剧情与角色模板变化。", wrap: true))));
        }

        var baseline = m.Attributes;
        // 预填两点，打开即可看到升级前后对比；剩余一点留给玩家操作。
        var added = CharacterSamples.AttributeNames.ToDictionary(n => n, n => n is "体魄" or "根骨" ? 1 : 0);
        var remaining = Ui.Text("");
        var rows = Ui.Column(UiPalette.SpaceM);
        var compare = Ui.Column(UiPalette.SpaceS);
        var confirm = Ui.MinSize(Ui.Button("确认分配", UiTheme.PrimaryButton), 200, 56);

        void Refresh()
        {
            var left = CharacterSamples.UnspentPotential - added.Values.Sum();
            remaining.Text = $"可分配潜能 {left}";
            confirm.Disabled = left == CharacterSamples.UnspentPotential;

            var before = Derived(baseline, m.Level);
            var after = Derived(baseline.ToDictionary(p => p.Key, p => p.Value + added[p.Key]), m.Level + 1);
            Ui.ClearChildren(compare);
            compare.AddChild(Ui.Text($"升至 {m.Level + 1} 级：战斗属性变化", size: 28));
            foreach (var (name, value) in after)
            {
                var delta = value - before[name];
                var line = StatLine(name, delta > 0 ? $"{before[name]}  →  {value}　▲ {delta}" : value.ToString());
                if (delta > 0)
                {
                    line.GetChild<Label>(1).AddThemeColorOverride("font_color", UiPalette.Boost);
                }

                compare.AddChild(line);
            }
        }

        foreach (var name in CharacterSamples.AttributeNames)
        {
            var value = Ui.MinSize(Ui.Text(""), 88);
            value.HorizontalAlignment = HorizontalAlignment.Center;
            void Update() => value.Text = added[name] > 0 ? $"{baseline[name]} + {added[name]}" : baseline[name].ToString();
            var minus = Ui.MinSize(Ui.Button("－", onPressed: () =>
            {
                added[name] = Math.Max(0, added[name] - 1);
                Update();
                Refresh();
            }), 56, 48);
            var plus = Ui.MinSize(Ui.Button("＋", onPressed: () =>
            {
                if (added.Values.Sum() < CharacterSamples.UnspentPotential)
                {
                    added[name]++;
                }

                Update();
                Refresh();
            }), 56, 48);
            Update();
            rows.AddChild(Ui.Row(UiPalette.SpaceM, Ui.MinSize(Ui.Text(name), 100), minus, value, plus,
                Ui.Text(AttributeHint(name), UiTheme.MutedLabel)));
        }

        Refresh();
        var reset = Ui.MinSize(Ui.Button("重置"), 160, 56);
        var left = Ui.MinSize(Ui.Column(UiPalette.SpaceM,
            Ui.Text($"等级 {m.Level}　→　{m.Level + 1}", size: 28),
            remaining, rows, Ui.Rule(),
            Ui.Text("修为点 120：用于武学熟练度，与经验分开累计。", UiTheme.MutedLabel),
            Ui.Row(UiPalette.SpaceM, confirm, reset)), 760);
        reset.Pressed += () =>
        {
            // 重建本页即可回到初始状态。
            var host = left.GetParent().GetParent<Container>();
            Ui.ClearChildren(host);
            host.AddChild(BuildGrowth(m));
        };

        return Ui.Row(UiPalette.SpaceXxl, left, Ui.Expand(Ui.Panel(UiTheme.InsetPanel, compare)));
    }

    /// <summary>架构文档 7.4 v0 公式的显示用换算（不含装备以外的加成）。</summary>
    private static Dictionary<string, int> Derived(IReadOnlyDictionary<string, int> a, int level) => new()
    {
        ["最大气血"] = 180 + 22 * a["体魄"] + 10 * level,
        ["最大内力"] = 80 + 18 * a["根骨"] + 6 * level,
        ["外功"] = 20 + 3 * a["臂力"] + 2 * level + 18,
        ["内功"] = 20 + 3 * a["根骨"] + 2 * level,
        ["速度"] = 30 + 2 * a["身法"] + 6,
    };

    private static string AttributeHint(string name) => name switch
    {
        "体魄" => "气血上限",
        "臂力" => "外功",
        "根骨" => "内力上限与内功",
        "身法" => "速度与先手",
        _ => "修习效率与领悟分支",
    };

    private static HBoxContainer StatLine(string name, string value) =>
        Ui.Row(UiPalette.SpaceM, Ui.MinSize(Ui.Text(name, UiTheme.MutedLabel), 140), Ui.Text(value));

    private static Control Pending(SampleMember m) => Ui.Panel(UiTheme.InsetPanel, Ui.Column(UiPalette.SpaceS,
        Ui.Text("人物档案待核", UiTheme.AccentLabel),
        Ui.Text(m.Pending ?? "", wrap: true),
        Ui.Text("经典人物的实力由角色模板与剧情表现体现；核对完成前不展示数值，避免被读作实力排名。", UiTheme.MutedLabel, wrap: true)));

    /// <summary>已有立绘的人物（资产台账已登记）。</summary>
    private static readonly Dictionary<string, string> Portraits = new()
    {
        ["char.lu_qinghe"] = "res://assets/portraits/lu_qinghe_v1.png",
    };

    /// <summary>
    /// 立绘框：绢本上一方石青到黛的画框，立绘自下而上铺满、底部渐隐；没有立绘的人物以竖排姓名占位并标注待制作。
    /// </summary>
    private static Control Portrait(SampleMember m)
    {
        var frame = new PanelContainer { ClipContents = true };
        frame.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            FillA = UiPalette.Accent.Lightened(0.25f), FillB = UiPalette.PanelDark, Ragged = 1.8f, Seed = 55,
            Grain = Colors.White with { A = 0.06f }, Wash = UiPalette.Surface with { A = 0.18f },
            Border = UiPalette.Ochre, BorderWidth = 1.6f, Brush = true, Inner = UiPalette.Gilt with { A = 0.45f }, InnerInset = 7,
            Corners = CornerStyle.Cloud, CornerSize = 34, CornerWidth = 2, CornerColor = UiPalette.Gilt,
        }.Margins(0, 0));

        var stage = new Control { ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
        frame.AddChild(stage);
        if (Portraits.TryGetValue(m.Id, out var path))
        {
            var art = new TextureRect
            {
                Texture = GD.Load<Texture2D>(path),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            art.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            art.OffsetLeft = -60;
            art.OffsetRight = 60;
            art.OffsetBottom = 180;
            stage.AddChild(art);
        }
        else
        {
            var name = Ui.Text(Ui.Vertical(m.Name), UiTheme.DarkTitleLabel, 72);
            name.AddThemeColorOverride("font_color", UiPalette.TextOnDark with { A = 0.35f });
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.VerticalAlignment = VerticalAlignment.Center;
            name.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            stage.AddChild(name);
        }

        var caption = Ui.Panel(UiTheme.GlassPanel, Ui.Column(2,
            Ui.Text(m.Name, UiTheme.DarkTitleLabel, 28),
            Ui.Text(Portraits.ContainsKey(m.Id) ? m.Role : "立绘待制作", UiTheme.DarkMutedLabel, 16)));
        stage.AddChild(Ui.Place(caption, 0, 1, 14, -86, 272, -14));
        return Ui.MinSize(frame, 300, 580);
    }

    private static PanelContainer SkillGlyph(SampleSkill skill, int size) => Ui.Glyph(skill.Glyph, skill.School switch
    {
        "剑" => UiPalette.Text,
        "拳掌" => UiPalette.Warm,
        "内功" => UiPalette.Boost,
        _ => UiPalette.TextMuted,
    }, size);
}
