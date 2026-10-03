using Godot;
using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.Characters;
using WuxiaWorld.Domain.Combat;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Art;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 人物页（C，或暂停菜单“人物”）：属性与潜能分配、武学装配与修炼、装备，版式沿用 M0 已验收的人物页（UI_DESIGN 5.2），
/// 内容全部读已提交的世界状态，改动经 <see cref="GameSession"/> 的养成事务提交，提交后整页按新状态重建。
/// 只有主角可养成；同行者显示其战斗模板的数值（经典人物只列个人武学，不显示等级与属性）。
/// 对话、换图或待开战斗期间只能查看，操作按钮置灰并写明原因。
/// </summary>
public sealed class CharacterPage
{
    private static readonly string[] TabNames = ["属性", "武学", "装备"];

    private readonly PlaySession _play;
    private readonly VBoxContainer _root = Ui.Column(UiPalette.SpaceL);
    private int _tab;
    private string _who;
    private string? _skill;
    private EquipSlot _slot = EquipSlot.Weapon;
    private string? _candidate;
    private int[] _pending = new int[5];
    // 洗点确认：按下洗点按钮时记下对象（"potential" 或招式 ID），下一次重建显示确认。
    private string? _respec;
    private string? _confirming;

    private CharacterPage(PlaySession play, int tab)
    {
        _play = play;
        _tab = tab;
        _who = Growth.Hero;
    }

    public Control Root => _root;

    private GameSession Game => _play.Game;
    private GrowthRules Growth => Game.Growth!;
    private WorldState World => Game.World;
    private bool Editable => Game.CanManage && Growth.IsBuildable(_who);

    /// <param name="tab">0 属性、1 武学、2 装备。</param>
    public static Control Build(PlaySession play, int tab = 0)
    {
        if (play.Game.Growth is null)
        {
            return Ui.Text("未载入战斗内容，无法显示人物。", UiTheme.DarkLabel, 22);
        }

        var page = new CharacterPage(play, tab);
        page.Rebuild();
        return page.Root;
    }

    private void Rebuild(string? message = null)
    {
        Ui.ClearChildren(_root);
        // 洗点的确认只维持到下一次重建：按了别的按钮，确认自动收起。
        (_confirming, _respec) = (_respec, null);
        var level = Growth.Level(World);

        var tabs = new ButtonGroup();
        var people = new ButtonGroup();
        var bar = Ui.Row(UiPalette.SpaceS);
        foreach (var id in World.Party)
        {
            var who = id;
            bar.AddChild(Ui.Toggle(_play.Name(id), UiTheme.ChipButton, people, () => Switch(who), id == _who));
        }

        bar.AddChild(Ui.Spacer());
        for (var i = 0; i < TabNames.Length; i++)
        {
            var index = i;
            bar.AddChild(Ui.Toggle(TabNames[i], UiTheme.SubTab, tabs, () =>
            {
                if (_tab != index)
                {
                    _tab = index;
                    Rebuild();
                }
            }, i == _tab));
        }

        var body = _tab switch
        {
            1 => Martial(),
            2 => Equipment(),
            _ => Attributes(level),
        };
        // 页底状态行每次重建都新建：旧的随旧容器一起释放。
        var status = Ui.Text(message ?? (Game.CanManage ? "" : "对话、换图或战斗进行中，只能查看；告一段落后再调整。"), UiTheme.AccentLabel, 20, wrap: true);
        var sheet = Ui.Panel(UiTheme.SheetPanel, Ui.Column(UiPalette.SpaceM, bar, Ui.Rule(), Ui.Expand(body, vertical: true), status));
        _root.AddChild(Ui.Expand(sheet, vertical: true));
    }

    private void Switch(string who)
    {
        if (who != _who)
        {
            _who = who;
            _pending = new int[5];
            Rebuild();
        }
    }

    /// <summary>提交一次养成事务：成功按新状态重建，失败只在页底写原因（按钮的确认声由全局界面音效负责）。</summary>
    private void Do(Func<CommitResult> act, string done)
    {
        var r = act();
        Rebuild(r.Ok ? done : r.Error);
    }

    // ── 属性 ─────────────────────────────────────────────

    private Control Attributes(int level)
    {
        var info = Ui.Column(UiPalette.SpaceM, Ui.Row(UiPalette.SpaceL, Ui.Text(_play.Name(_who), UiTheme.TitleLabel), Ui.Spacer()));
        if (!Growth.IsBuildable(_who))
        {
            info.AddChild(Companion());
            return Ui.Row(UiPalette.SpaceXl, Portrait(_play, _who), Ui.Expand(info));
        }

        var next = Game.Rules.NextLevelAt(level);
        var prev = level > 1 ? Game.Rules.NextLevelAt(level - 1) ?? 0 : 0;
        info.AddChild(Ui.Row(UiPalette.SpaceL,
            Ui.Text($"第 {level} 级", size: 28),
            next is { } n ? Ui.Bar(UiTheme.ExpBar, World.Experience - prev, n - prev, 260) : Ui.Spacer(),
            Ui.Text(next is { } m ? $"经验 {World.Experience} / {m}" : $"经验 {World.Experience}　已到本篇等级上限", UiTheme.MutedLabel),
            Ui.Spacer(),
            Ui.Text($"修为 {World.Cultivation}", UiTheme.AccentLabel)));
        info.AddChild(Ui.Rule());

        var unspent = Growth.Unspent(World, _who);
        var basics = GrowthText.Values(Growth.AttributesOf(World, _who));
        var left = unspent - _pending.Sum();
        var rows = Ui.Column(UiPalette.SpaceS,
            Ui.Text(unspent > 0 ? $"可分配潜能 {left} / {unspent}（每升一级得 {StatFormula.PotentialPerLevel} 点）" : "暂无可分配潜能：升级后再来",
                unspent > 0 ? UiTheme.AccentLabel : UiTheme.MutedLabel));
        for (var i = 0; i < 5; i++)
        {
            var index = i;
            var value = Ui.MinSize(Ui.Text(_pending[i] > 0 ? $"{basics[i]} + {_pending[i]}" : basics[i].ToString()), 96);
            value.HorizontalAlignment = HorizontalAlignment.Center;
            var minus = Ui.MinSize(Ui.Button("－", onPressed: () => Pend(index, -1), disabled: !Editable || _pending[i] == 0), 52, 44);
            var plus = Ui.MinSize(Ui.Button("＋", onPressed: () => Pend(index, 1), disabled: !Editable || left <= 0), 52, 44);
            rows.AddChild(Ui.Row(UiPalette.SpaceM, Ui.MinSize(Ui.Text(GrowthText.AttributeNames[i]), 72), minus, value, plus,
                Ui.Text(GrowthText.AttributeHint(i), UiTheme.MutedLabel, 18, wrap: true)));
        }

        // 讨教定下流派之前没有推荐比例，按五项平均分。
        var styled = World.Facts.ContainsKey(WorldRules.StyleFact);
        var recommend = Ui.Button(styled ? "按流派推荐" : "平均分配", onPressed: () =>
        {
            _pending = GrowthText.Values(Growth.Recommend(World, _who));
            Rebuild(styled ? "已按流派推荐填好，确认后才生效" : "已平均分好，确认后才生效");
        }, disabled: !Editable || unspent == 0);
        var reset = Ui.Button("重置", onPressed: () =>
        {
            _pending = new int[5];
            Rebuild();
        }, disabled: _pending.Sum() == 0);
        var confirm = Ui.Button("确认分配", UiTheme.PrimaryButton, () =>
        {
            var add = GrowthText.FromValues(_pending);
            _pending = new int[5];
            Do(() => Game.Allocate(_who, add), "潜能已分配");
        }, disabled: !Editable || _pending.Sum() == 0);
        foreach (var b in new[] { recommend, reset, confirm })
        {
            b.CustomMinimumSize = new Vector2(160, 52);
        }

        rows.AddChild(Ui.Row(UiPalette.SpaceM, recommend, reset, confirm));
        rows.AddChild(Ui.Rule());
        rows.AddChild(RespecRow());

        // 右侧：战斗属性，有待确认的分配时并列“分配后”。
        var now = Growth.Stats(World, _who);
        StatBlock? after = null;
        if (_pending.Sum() > 0)
        {
            var c = World.Clone();
            if (Growth.Allocate(c, _who, GrowthText.FromValues(_pending)) is null)
            {
                after = Growth.Stats(c, _who);
            }
        }

        info.AddChild(Ui.Row(UiPalette.SpaceXl, Ui.MinSize(rows, 620), Ui.Expand(Ui.Panel(UiTheme.InsetPanel, StatGrid(now, after)))));
        return Ui.Row(UiPalette.SpaceXl, Portrait(_play, _who), Ui.Expand(info));
    }

    private void Pend(int index, int delta)
    {
        _pending[index] = Math.Max(0, _pending[index] + delta);
        Rebuild();
    }

    /// <summary>
    /// 洗点一行（架构文档 8.2，城镇开放）：平时是按钮加说明；按下后同一行换成问句与“再想想 / 确认”（<see cref="RespecConfirm"/>）。
    /// 不在城镇、没有可收回的、银两不够时按钮置灰并写明原因。
    /// </summary>
    private Control RespecRow()
    {
        if (_confirming == PotentialKey && Growth.CanRespecHere(World))
        {
            return RespecConfirm(null);
        }

        var refund = Growth.RespecRefund(World, _who, RespecKind.Potential);
        var cost = Growth.RespecCost(World);
        var hint = !Growth.CanRespecHere(World) ? $"洗点只在城镇进行（{string.Join("、", Towns())}）。"
            : refund == 0 ? "分配后的潜能要收回，可在城镇洗点。"
            : World.Silver < cost ? $"银两不足：洗点要 {cost} 两。"
            : $"收回全部 {refund} 点已分配潜能；等级越高花费越多，剧情选择与已学武学不变。";
        var button = RespecButton(null, $"洗点（银 {cost} 两）");
        button.CustomMinimumSize = new Vector2(220, 52);
        return Ui.Row(UiPalette.SpaceM, button, Ui.Expand(Ui.Text(hint, UiTheme.MutedLabel, 18, wrap: true)));
    }

    private const string PotentialKey = "potential";

    private IEnumerable<string> Towns() => Game.Rules.Content.Maps.Values.Where(m => m.Town).Select(m => _play.Name(m.Id));

    /// <summary>洗点按钮（<paramref name="skill"/> 为 null 时洗潜能，否则这门招式退阶）：按下只进入确认，不提交。</summary>
    private Button RespecButton(string? skill, string text)
    {
        var kind = skill is null ? RespecKind.Potential : RespecKind.Mastery;
        return Ui.Button(text, onPressed: () =>
        {
            _respec = skill ?? PotentialKey;
            Rebuild();
        }, disabled: !Editable || !Growth.CanRespecHere(World) || Growth.RespecRefund(World, _who, kind, skill) == 0
                     || World.Silver < Growth.RespecCost(World));
    }

    /// <summary>洗点确认行：问句写明收回多少、花多少银两，焦点落在“再想想”。</summary>
    private HBoxContainer RespecConfirm(string? skill)
    {
        var kind = skill is null ? RespecKind.Potential : RespecKind.Mastery;
        var refund = Growth.RespecRefund(World, _who, kind, skill);
        var cost = Growth.RespecCost(World);
        var no = Ui.Button("再想想", onPressed: () => Rebuild());
        var yes = Ui.Button(skill is null ? "洗点" : "退阶", UiTheme.PrimaryButton, () =>
        {
            _pending = new int[5];
            Do(() => Game.Respec(_who, kind, skill),
                skill is null ? $"已收回 {refund} 点潜能，可重新分配" : $"{_play.Combat.Name(skill)}退回第 1 阶，返还修为 {refund}");
        }, disabled: !Editable);
        no.CustomMinimumSize = new Vector2(140, 52);
        yes.CustomMinimumSize = new Vector2(140, 52);
        // 招式详情在一次重建里可能先后建两遍（选中行的回调 + 显式刷新），先建的那份已离开场景树，不抢焦点。
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(no) && no.IsInsideTree())
            {
                no.GrabFocus();
            }
        }).CallDeferred();
        var ask = skill is null ? $"收回 {refund} 点潜能，花银 {cost} 两？" : $"退回第 1 阶、返还修为 {refund}，花银 {cost} 两？";
        return Ui.Row(UiPalette.SpaceM, Ui.Expand(Ui.Text(ask, UiTheme.AccentLabel, 20, wrap: true)), no, yes);
    }

    /// <summary>战斗属性表；给了 <paramref name="after"/> 时第三列写变化。</summary>
    private static Control StatGrid(StatBlock now, StatBlock? after, string title = "战斗属性")
    {
        var grid = new GridContainer { Columns = after is null ? 2 : 3 };
        grid.AddThemeConstantOverride("h_separation", UiPalette.SpaceXl);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (var (name, get, percent) in GrowthText.Stats)
        {
            grid.AddChild(Ui.Text(name, UiTheme.MutedLabel, 20));
            grid.AddChild(Ui.Text(GrowthText.Format(get(now), percent), size: 20));
            if (after is { } a)
            {
                var delta = get(a) - get(now);
                var change = Ui.Text(delta == 0 ? "" : $"→ {GrowthText.Format(get(a), percent)}　{(delta > 0 ? "▲" : "▼")}", size: 20);
                change.AddThemeColorOverride("font_color", delta > 0 ? UiPalette.Boost : UiPalette.Warm);
                grid.AddChild(change);
            }
        }

        return Ui.Column(UiPalette.SpaceS, Ui.Text(title, UiTheme.SectionLabel), grid);
    }

    /// <summary>
    /// 同行者：可招募伙伴显示随成长现推的数值（等级、经验、属性）；暂时同行的原创人物显示其角色模板；
    /// 经典人物只列个人武学与效果，不显示等级与属性（模板数值是第一章的平衡取值，不被读作实力排名）。
    /// </summary>
    private Control Companion()
    {
        var content = Game.Rules.Content;
        var grows = Growth.Grows(World, _who);
        var column = Ui.Column(UiPalette.SpaceM, Ui.Text(PartyRules.RoleOf(Game.Rules, _who) == PartyRole.Recruitable
            ? "伙伴：随主角成长，不由玩家加点。在队时与主角同得经验，离队期间得一半；再入队时若落后，补到比主角低一级。"
            : "暂时同行：随事件来去，实力随其原著阶段与角色模板，不随主角成长。", UiTheme.MutedLabel, wrap: true));
        if (content.Characters.TryGetValue(_who, out var c) && c.Origin == CharacterOrigin.Canon)
        {
            var arts = Ui.Column(UiPalette.SpaceS, Ui.Text("武学", UiTheme.AccentLabel));
            foreach (var id in Growth.Template(World, _who).Loadout.Skills)
            {
                arts.AddChild(Ui.Row(UiPalette.SpaceM, GrowthText.SkillGlyph(id, _play.Combat.Glyph(id), 48), Ui.Expand(Ui.Column(2,
                    Ui.Text(_play.Combat.Name(id), size: 20),
                    Ui.Text(_play.Text(id + ".desc") ?? "", UiTheme.MutedLabel, 17, wrap: true)))));
            }

            arts.AddChild(Ui.Text("经典人物的实力按所选原著阶段体现，不标等级与属性，也不随主角成长。", UiTheme.MutedLabel, 17, wrap: true));
            column.AddChild(Ui.Panel(UiTheme.InsetPanel, arts));
            return column;
        }

        var template = Growth.Template(World, _who);
        var stats = CombatantFactory.DeriveStats(template, Growth.Combat);
        var level = grows ? $"第 {template.Level} 级　经验 {World.Companions[_who].Experience}" : $"第 {template.Level} 级";
        column.AddChild(Ui.Text($"{level}　招式：{string.Join("、", template.Loadout.Skills.Select(_play.Combat.Name))}", wrap: true));
        column.AddChild(Ui.Panel(UiTheme.InsetPanel, StatGrid(stats, null)));
        return column;
    }

    // ── 武学 ─────────────────────────────────────────────

    private Control Martial()
    {
        if (!Growth.IsBuildable(_who))
        {
            return Ui.Row(UiPalette.SpaceXl, Portrait(_play, _who), Ui.Expand(Companion()));
        }

        var build = World.Builds.GetValueOrDefault(_who) ?? new CharacterBuild();
        var learned = Growth.LearnedSkills(World).ToList();
        _skill ??= build.Skills.FirstOrDefault() ?? learned.FirstOrDefault();

        var slots = Ui.Row(UiPalette.SpaceM);
        for (var i = 0; i < Domain.Combat.Definitions.Loadout.MaxSkills; i++)
        {
            var id = i < build.Skills.Count ? build.Skills[i] : null;
            var name = id is null ? "空" : _play.Combat.Name(id);
            var tile = id is null ? Ui.Glyph("空", UiPalette.TextMuted with { A = 0.5f }, 64) : GrowthText.SkillGlyph(id, name, 64);
            var caption = Ui.Text(id is null ? "空槽" : name, id is null ? UiTheme.MutedLabel : null, 18);
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            slots.AddChild(Ui.MinSize(Ui.Column(4, tile, caption), 72));
        }

        var detail = new MarginContainer();
        var list = Ui.Column(4);
        var group = new ButtonGroup();
        foreach (var id in learned)
        {
            var skill = id;
            var def = Growth.Combat.Skill(id);
            var tier = build.MasteryOf(id);
            list.AddChild(Ui.ListRow(group, () =>
                {
                    _skill = skill;
                    ShowSkill(detail, build, skill);
                }, _play.Combat.Name(id), $"{GrowthText.School(id)}　{GrowthText.Cost(def)}　第 {tier} 阶",
                build.Skills.Contains(id) ? "已装配" : null, GrowthText.SkillGlyph(id, _play.Combat.Glyph(id), 48), id == _skill));
        }

        if (learned.Count == 0)
        {
            list.AddChild(Ui.Text("尚未习得招式。到客栈向三位侠客讨教后，会学到一整套入门武学。", UiTheme.MutedLabel, wrap: true));
        }
        else if (_skill is not null)
        {
            ShowSkill(detail, build, _skill);
        }

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 300) };
        scroll.AddChild(Ui.Expand(list));
        var left = Ui.MinSize(Ui.Column(UiPalette.SpaceM,
            Ui.Row(UiPalette.SpaceM, Ui.Text($"出手栏　{build.Skills.Count} / {Domain.Combat.Definitions.Loadout.MaxSkills}", UiTheme.MutedLabel), Ui.Spacer(),
                Ui.Text($"修为 {World.Cultivation}", UiTheme.AccentLabel)),
            slots, Ui.Rule(), Ui.Text("已习得招式", UiTheme.MutedLabel), Ui.Expand(scroll, vertical: true)), 640);
        // 右栏（招式详情 + 心法）可能比页面高（提示行、天赋多时），放进滚动区，不撑出绢页。
        var right = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        right.AddChild(Ui.Expand(Ui.Column(UiPalette.SpaceL, detail, Arts(build))));
        return Ui.Row(UiPalette.SpaceXl, left, Ui.Expand(right));
    }

    private void ShowSkill(Container host, CharacterBuild build, string id)
    {
        Ui.ClearChildren(host);
        var def = Growth.Combat.Skill(id);
        var name = _play.Combat.Name(id);
        var tier = build.MasteryOf(id);
        var max = Game.Rules.Content.Progression.MaxMastery;
        var bonus = Growth.MasteryBonusBp(tier);
        // 消耗、目标、冷却并成一行，熟练度一行，给下方心法区留出位置。
        var facts = $"{GrowthText.Cost(def)}　·　{GrowthText.RuleText(def.TargetRule)}　·　{(def.Cooldown > 0 ? $"冷却 {def.Cooldown} 次行动" : "无冷却")}";
        var mastery = $"熟练　{GrowthText.Mastery(tier, max)}　第 {tier} 阶" + (bonus > 0 ? $"　效果 +{bonus / 100}%" : "");

        var equipped = build.Skills.Contains(id);
        var toggle = Ui.Button(equipped ? "卸下" : "装配", equipped ? null : UiTheme.PrimaryButton, () =>
        {
            var next = equipped ? build.Skills.Where(s => s != id).ToList() : [.. build.Skills, id];
            Do(() => Game.SetSkills(_who, next), equipped ? $"已卸下{name}" : $"{name}已装上出手栏");
        }, disabled: !Editable || (!equipped && build.Skills.Count >= Domain.Combat.Definitions.Loadout.MaxSkills));
        var cost = Growth.MasteryCost(World, _who, id);
        var train = Ui.Button(cost is { } c ? $"修炼（修为 {c}）" : "已到最高一阶", UiTheme.PrimaryButton,
            () => Do(() => Game.Cultivate(_who, id), $"{name}升到第 {tier + 1} 阶"),
            disabled: !Editable || cost is null || World.Cultivation < cost);
        toggle.CustomMinimumSize = new Vector2(160, 52);
        train.CustomMinimumSize = new Vector2(220, 52);

        // 修炼过的招式可在城镇退回第 1 阶、返还修为（洗点）：按钮并在同一行，确认时整行换成问句。
        var actions = Ui.Row(UiPalette.SpaceM, toggle, train);
        if (tier > 1 && _confirming == id && Growth.CanRespecHere(World))
        {
            actions.Free(); // 卸下 / 修炼这一行不进场景树，当场释放
            actions = RespecConfirm(id);
        }
        else if (tier > 1)
        {
            var back = RespecButton(id, Growth.CanRespecHere(World) ? $"退阶（银 {Growth.RespecCost(World)} 两）" : "退阶须在城镇");
            back.CustomMinimumSize = new Vector2(200, 52);
            actions.AddChild(back);
        }

        var body = Ui.Column(UiPalette.SpaceS,
            Ui.Row(UiPalette.SpaceL, GrowthText.SkillGlyph(id, name, 64), Ui.Column(4,
                Ui.Text(name + "　" + GrowthText.School(id) + (AppHost.DevInfo ? $"　{id}" : ""), UiTheme.SectionLabel),
                Ui.Text(facts, UiTheme.MutedLabel, 18))),
            Ui.Text(mastery, size: 20),
            Ui.Text(_play.Combat.Describe(id) ?? "", size: 20, wrap: true),
            actions);
        if (cost is { } need && World.Cultivation < need)
        {
            body.AddChild(Ui.Text($"修为不足：还差 {need - World.Cultivation}。修为由讨教、战斗与支线获得。", UiTheme.MutedLabel, 18, wrap: true));
        }
        else if (!equipped && build.Skills.Count >= Domain.Combat.Definitions.Loadout.MaxSkills)
        {
            body.AddChild(Ui.Text("出手栏已满：先卸下一招。", UiTheme.MutedLabel, 18));
        }

        host.AddChild(Ui.Panel(UiTheme.InsetPanel, body));
    }

    /// <summary>心法、轻功与天赋：每栏一行，“更换”在已学的同类之间轮换（含空着）；阴阳相冲的代价直接写出。</summary>
    private Control Arts(CharacterBuild build)
    {
        var column = Ui.Column(UiPalette.SpaceS, Ui.Text("心法、轻功与天赋", UiTheme.SectionLabel));
        void Line(string label, string? current, IReadOnlyList<string> options, Func<string?, CommitResult> set)
        {
            var name = current is null ? "未装配" : _play.Combat.Name(current);
            var effect = current is not null && Growth.Combat.Arts.TryGetValue(current, out var art) ? GrowthText.ArtEffect(art) : "";
            // 轮换次序：已学的同类依次，最后是空着。
            var cycle = options.Cast<string?>().Append(null).ToList();
            var next = cycle[(cycle.IndexOf(current) + 1) % cycle.Count];
            var change = Ui.Button(options.Count == 0 ? "未习得" : "更换", onPressed: () => Do(() => set(next), $"{label}改为{(next is null ? "空着" : _play.Combat.Name(next))}"),
                disabled: !Editable || options.Count == 0 || (options.Count == 1 && current == options[0] && label != "辅修"));
            change.CustomMinimumSize = new Vector2(110, 44);
            column.AddChild(Ui.Row(UiPalette.SpaceM, Ui.MinSize(Ui.Text(label, UiTheme.MutedLabel, 20), 64),
                Ui.Expand(Ui.Column(2, Ui.Text(name, size: 20), Ui.Text(effect, UiTheme.MutedLabel, 17, wrap: true))), change));
        }

        var inner = Growth.LearnedArts(World, ArtKind.Inner).ToList();
        Line("主修", build.MainArt, inner, id => Game.SetArt(_who, ArtSlot.Main, id));
        Line("辅修", build.SupportArt, inner.Where(i => i != build.MainArt).ToList(), id => Game.SetArt(_who, ArtSlot.Support, id));
        Line("轻功", build.Qinggong, Growth.LearnedArts(World, ArtKind.Qinggong).ToList(), id => Game.SetArt(_who, ArtSlot.Qinggong, id));
        foreach (var talent in Growth.LearnedArts(World, ArtKind.Talent))
        {
            var on = build.Talents.Contains(talent);
            var t = Ui.Button(on ? "卸下" : "装上", onPressed: () => Do(() => Game.ToggleTalent(_who, talent), on ? "天赋已卸下" : "天赋已装上"), disabled: !Editable);
            t.CustomMinimumSize = new Vector2(110, 44);
            var effect = Growth.Combat.Arts.TryGetValue(talent, out var art) ? GrowthText.ArtEffect(art) : "";
            column.AddChild(Ui.Row(UiPalette.SpaceM, Ui.MinSize(Ui.Text("天赋", UiTheme.MutedLabel, 20), 64),
                Ui.Expand(Ui.Column(2, Ui.Text(_play.Combat.Name(talent) + (on ? "　已装上" : ""), size: 20), Ui.Text(effect, UiTheme.MutedLabel, 17, wrap: true))), t));
        }

        foreach (var issue in Growth.Issues(World, _who).Where(i => i.Level == LoadoutIssueLevel.Warning))
        {
            var warn = Ui.Text("⚠ " + issue.Message, size: 18, wrap: true);
            warn.AddThemeColorOverride("font_color", UiPalette.Warm);
            column.AddChild(warn);
        }

        return Ui.Panel(UiTheme.InsetPanel, column);
    }

    // ── 装备 ─────────────────────────────────────────────

    private Control Equipment()
    {
        if (!Growth.IsBuildable(_who))
        {
            return Ui.Row(UiPalette.SpaceXl, Portrait(_play, _who), Ui.Expand(Companion()));
        }

        var build = World.Builds.GetValueOrDefault(_who) ?? new CharacterBuild();
        var items = Game.Rules.Content.Items;
        var slotGroup = new ButtonGroup();
        var slots = Ui.Column(4, Ui.Text("装备栏", UiTheme.MutedLabel));
        foreach (var slot in Enum.GetValues<EquipSlot>())
        {
            var s = slot;
            var current = build.Equipped.GetValueOrDefault(slot);
            // 预先选中的一行在建立时也会触发回调：只在真的换了选择时重建，免得无限递归。
            slots.AddChild(Ui.ListRow(slotGroup, () =>
            {
                if (_slot != s)
                {
                    _slot = s;
                    _candidate = null;
                    Rebuild();
                }
            }, GrowthText.SlotName(slot), current is null ? "空" : _play.Name(current), null,
                current is null ? null : GrowthText.ItemGlyph(items.GetValueOrDefault(current), _play.Name(current), 48), slot == _slot));
        }

        var equipped = build.Equipped.GetValueOrDefault(_slot);
        var pool = World.Items.Keys.Where(id => items.TryGetValue(id, out var d) && d.Slot == _slot).ToList();
        if (_candidate is not null && !pool.Contains(_candidate))
        {
            _candidate = null;
        }

        _candidate ??= pool.FirstOrDefault();
        var candidates = Ui.Column(4, Ui.Text("行囊里可换上的", UiTheme.MutedLabel));
        var group = new ButtonGroup();
        foreach (var id in pool)
        {
            var item = id;
            candidates.AddChild(Ui.ListRow(group, () =>
            {
                if (_candidate != item)
                {
                    _candidate = item;
                    Rebuild();
                }
            }, _play.Name(id), GrowthText.BonusLine(items[id].Bonus), World.CountOf(id) > 1 ? $"×{World.CountOf(id)}" : null,
                GrowthText.ItemGlyph(items[id], _play.Name(id), 48), id == _candidate));
        }

        if (pool.Count == 0)
        {
            candidates.AddChild(Ui.Text($"行囊里没有可换的{GrowthText.SlotName(_slot)}，可到店铺购买。", UiTheme.MutedLabel, wrap: true));
        }

        return Ui.Row(UiPalette.SpaceXl, Ui.MinSize(slots, 340), Ui.MinSize(candidates, 440), Ui.Expand(Compare(equipped, _candidate)));
    }

    private Control Compare(string? current, string? next)
    {
        var now = Growth.Stats(World, _who);
        StatBlock? after = null;
        if (next is not null)
        {
            var c = World.Clone();
            if (Growth.Equip(c, _who, next) is null)
            {
                after = Growth.Stats(c, _who);
            }
        }

        var title = next is null
            ? $"{GrowthText.SlotName(_slot)}：{(current is null ? "空" : _play.Name(current))}"
            : $"{(current is null ? "空" : _play.Name(current))}　换为　{_play.Name(next)}";
        var desc = next ?? current;
        var equip = Ui.Button("换上", UiTheme.PrimaryButton, () => Do(() => Game.Equip(_who, next!), $"已换上{_play.Name(next!)}"),
            disabled: !Editable || next is null);
        var remove = Ui.Button("卸下", onPressed: () => Do(() => Game.Unequip(_who, _slot), $"已卸下{_play.Name(current!)}，放回行囊"),
            disabled: !Editable || current is null);
        equip.CustomMinimumSize = new Vector2(160, 52);
        remove.CustomMinimumSize = new Vector2(160, 52);
        var body = Ui.Column(UiPalette.SpaceM, Ui.Text(title, UiTheme.SectionLabel));
        if (desc is not null)
        {
            body.AddChild(Ui.Text(_play.Text(desc + ".desc") ?? "", UiTheme.MutedLabel, 18, wrap: true));
        }

        body.AddChild(StatGrid(now, after, after is null ? "当前战斗属性" : "换上后的变化"));
        body.AddChild(Ui.Row(UiPalette.SpaceM, equip, remove));
        return Ui.Panel(UiTheme.InsetPanel, body);
    }

    // ── 立绘 ─────────────────────────────────────────────

    /// <summary>
    /// 立绘框：有对话立绘用立绘，否则用探索与战斗共用的全身形象，都没有则竖排姓名并标“立绘待制作”（UI_DESIGN 5.2）。
    /// </summary>
    internal static Control Portrait(PlaySession play, string who)
    {
        var frame = new PanelContainer { ClipContents = true };
        frame.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            FillA = UiPalette.Accent.Lightened(0.25f), FillB = UiPalette.PanelDark, Ragged = 1.8f, Seed = 55,
            Grain = Colors.White with { A = 0.06f }, Wash = UiPalette.Surface with { A = 0.18f },
            Border = UiPalette.Ochre, BorderWidth = 1.6f, Brush = true, Inner = UiPalette.Gilt with { A = 0.45f }, InnerInset = 7,
            Corners = CornerStyle.Cloud, CornerSize = 34, CornerWidth = 2, CornerColor = UiPalette.Gilt,
        }.Margins(0, 0));
        var stage = new Control { ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.AddChild(stage);
        var portrait = $"res://assets/portraits/{who.Replace("char.", "", StringComparison.Ordinal)}_v1.png";
        var figure = FigureArt.Find(who.Replace("char.", "figure.", StringComparison.Ordinal));
        string note;
        if (ResourceLoader.Exists(portrait))
        {
            var art = new TextureRect
            {
                Texture = GD.Load<Texture2D>(portrait), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            art.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            art.OffsetLeft = -60;
            art.OffsetRight = 60;
            art.OffsetBottom = 180;
            stage.AddChild(art);
            note = "";
        }
        else if (figure is not null)
        {
            var art = new TextureRect
            {
                Texture = figure.Texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, FlipH = true, MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            art.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            art.OffsetTop = 28;
            art.OffsetBottom = -96;
            stage.AddChild(art);
            note = "全身样稿　立绘待制作";
        }
        else
        {
            var name = Ui.Text(Ui.Vertical(play.Name(who)), UiTheme.DarkTitleLabel, 72);
            name.AddThemeColorOverride("font_color", UiPalette.TextOnDark with { A = 0.35f });
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.VerticalAlignment = VerticalAlignment.Center;
            name.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            stage.AddChild(name);
            note = "立绘待制作";
        }

        var caption = Ui.Column(2, Ui.Text(play.Name(who), UiTheme.DarkTitleLabel, 28));
        if (note.Length > 0)
        {
            caption.AddChild(Ui.Text(note, UiTheme.DarkMutedLabel, 16));
        }

        stage.AddChild(Ui.Place(Ui.Panel(UiTheme.GlassPanel, caption), 0, 1, 14, -86, 272, -14));
        return Ui.MinSize(frame, 300, 560);
    }
}
