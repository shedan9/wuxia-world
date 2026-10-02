using Godot;
using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 行囊页（I，或暂停菜单“行囊”）：按类别列出行囊里的物品与身上已装备的，右侧看说明、加成与操作。
/// 版式沿用 M0 已验收的行囊页（UI_DESIGN 5.2）。装备可直接给主角换上；药品只在战斗中使用（探索中不掉血）；
/// 任务物品不能出售，丢弃不开放（主线必要物品不可丢，其余也暂不需要）。
/// </summary>
public sealed class InventoryPage
{
    private static readonly (string Name, Func<ItemDefinition, bool> Match)[] Filters =
    [
        ("全部", _ => true),
        ("装备", i => i.Slot is not null),
        ("药品", i => i.Category == ItemCategory.Medicine),
        ("任务", i => i.Category == ItemCategory.Quest),
        ("杂物", i => i.Category is ItemCategory.Misc or ItemCategory.Material),
    ];

    private readonly PlaySession _play;
    private readonly VBoxContainer _root = Ui.Column(UiPalette.SpaceL);
    private int _filter;
    private string? _selected;

    private InventoryPage(PlaySession play) => _play = play;

    public Control Root => _root;

    private GameSession Game => _play.Game;
    private WorldState World => Game.World;

    public static Control Build(PlaySession play)
    {
        var page = new InventoryPage(play);
        page.Rebuild();
        return page.Root;
    }

    private void Rebuild(string? message = null)
    {
        Ui.ClearChildren(_root);
        _root.AddChild(Ui.Row(UiPalette.SpaceL, Ui.Seal("行囊"), Ui.Column(4,
                Ui.Text("行囊", UiTheme.DarkTitleLabel, 40),
                Ui.Text($"{_play.Name(World.MapId)}　·　{PlaySession.ClockText(World.Clock)}", UiTheme.DarkMutedLabel, 18)),
            Ui.Spacer(), Ui.Text($"银 {World.Silver} 两", UiTheme.GiltLabel, 26), Ui.KeyHints(true, ("Esc", "返回"))));

        var items = Game.Rules.Content.Items;
        var hero = Game.Rules.Content.Progression.Hero;
        var worn = World.Builds.GetValueOrDefault(hero)?.Equipped.Values.ToList() ?? [];
        var chips = new ButtonGroup();
        var filters = Ui.Row(UiPalette.SpaceS);
        for (var i = 0; i < Filters.Length; i++)
        {
            var index = i;
            filters.AddChild(Ui.Toggle(Filters[i].Name, UiTheme.ChipButton, chips, () =>
            {
                if (_filter != index)
                {
                    _filter = index;
                    _selected = null;
                    Rebuild();
                }
            }, i == _filter));
        }

        // 行囊在前，身上已装上的另列，标“已装上”。键为“bag:ID”或“worn:ID”，同一件东西两处都有时分得开。
        var entries = World.Items.Keys.Where(id => items.TryGetValue(id, out var d) && Filters[_filter].Match(d)).Select(id => ("bag:" + id, id))
            .Concat(worn.Where(id => items.TryGetValue(id, out var d) && Filters[_filter].Match(d)).Select(id => ("worn:" + id, id)))
            .ToList();
        if (_selected is null || entries.All(e => e.Item1 != _selected))
        {
            _selected = entries.FirstOrDefault().Item1;
        }

        var list = Ui.Column(4);
        var rows = new ButtonGroup();
        foreach (var (key, id) in entries)
        {
            var k = key;
            var wornHere = key.StartsWith("worn:", StringComparison.Ordinal);
            var def = items[id];
            var count = wornHere ? 1 : World.CountOf(id);
            // 预先选中的一行在建立时也会触发回调：只在真的换了选择时重建。
            list.AddChild(Ui.ListRow(rows, () =>
                {
                    if (_selected != k)
                    {
                        _selected = k;
                        Rebuild();
                    }
                }, _play.Name(id), GrowthText.CategoryName(def.Category) + (def.Bonus is { } b && GrowthText.BonusLine(b) is { Length: > 0 } line ? "　" + line : ""),
                wornHere ? "已装上" : count > 1 ? $"× {count}" : null, GrowthText.ItemGlyph(def, _play.Name(id)), k == _selected));
        }

        if (entries.Count == 0)
        {
            list.AddChild(Ui.Text("这一类里还没有东西。", UiTheme.MutedLabel));
        }

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(Ui.Expand(list));
        var left = Ui.MinSize(Ui.Column(UiPalette.SpaceM, filters, Ui.Expand(scroll, vertical: true)), 640);
        var detail = _selected is { } sel ? Detail(sel) : new Control();
        var status = Ui.Text(message ?? "", UiTheme.AccentLabel, 20, wrap: true);
        var sheet = Ui.Panel(UiTheme.SheetPanel, Ui.Column(UiPalette.SpaceM,
            Ui.Expand(Ui.Row(UiPalette.SpaceXl, left, Ui.Expand(detail)), vertical: true), status));
        _root.AddChild(Ui.Expand(sheet, vertical: true));
    }

    private Control Detail(string key)
    {
        var worn = key.StartsWith("worn:", StringComparison.Ordinal);
        var id = key[(key.IndexOf(':', StringComparison.Ordinal) + 1)..];
        var def = Game.Rules.Content.Items[id];
        var name = _play.Name(id);
        var body = Ui.Column(UiPalette.SpaceM,
            Ui.Row(UiPalette.SpaceL, GrowthText.ItemGlyph(def, name, 96), Ui.Column(UiPalette.SpaceS,
                Ui.Text(name, UiTheme.SectionLabel),
                Ui.Text($"{GrowthText.CategoryName(def.Category)}　　{(worn ? "主角身上" : $"行囊 {World.CountOf(id)}")}" + (def.Price > 0 && !def.Key ? $"　　价 {def.Price} 两" : ""),
                    UiTheme.MutedLabel, 18))),
            Ui.Text(_play.Text(id + ".desc") ?? Combat(id) ?? "", size: 20, wrap: true));

        var bonus = GrowthText.Bonus(def.Bonus).ToList();
        if (bonus.Count > 0)
        {
            body.AddChild(Ui.Rule());
            foreach (var (stat, value) in bonus)
            {
                body.AddChild(Ui.Row(UiPalette.SpaceM, Ui.MinSize(Ui.Text(stat, UiTheme.MutedLabel, 20), 140), Ui.Text(value, size: 20)));
            }
        }

        body.AddChild(Ui.Rule());
        var hero = Game.Rules.Content.Progression.Hero;
        if (def.Slot is { } slot)
        {
            var growth = Game.Growth;
            var canManage = Game.CanManage && growth is not null;
            var act = worn
                ? Ui.Button("卸下", onPressed: () => Do(() => Game.Unequip(hero, slot), $"已卸下{name}，放回行囊"), disabled: !canManage)
                : Ui.Button($"给{_play.Name(hero)}换上", UiTheme.PrimaryButton, () => Do(() => Game.Equip(hero, id), $"已换上{name}"), disabled: !canManage);
            act.CustomMinimumSize = new Vector2(220, 56);
            body.AddChild(Ui.Row(UiPalette.SpaceM, act));
            var current = World.Builds.GetValueOrDefault(hero)?.Equipped.GetValueOrDefault(slot);
            body.AddChild(Ui.Text(worn ? $"{GrowthText.SlotName(slot)}栏" : $"{GrowthText.SlotName(slot)}栏现为：{(current is null ? "空" : _play.Name(current))}，换下的放回行囊。",
                UiTheme.MutedLabel, 18, wrap: true));
            if (!canManage)
            {
                body.AddChild(Ui.Text("对话、换图或战斗进行中，告一段落后再换。", UiTheme.MutedLabel, 18));
            }
        }
        else if (def.Category == ItemCategory.Medicine)
        {
            body.AddChild(Ui.Text("药品在战斗中由人物使用；探索中不会受伤，无需服用。", UiTheme.MutedLabel, 18, wrap: true));
        }
        else if (def.Key)
        {
            body.AddChild(Ui.Text("主线必要物品：不能出售或丢弃。", UiTheme.MutedLabel, 18));
        }

        return Ui.Panel(UiTheme.InsetPanel, body);
    }

    /// <summary>战斗物品（药品）说明在战斗文本表里。</summary>
    private string? Combat(string id) => _play.Combat.Describe(id);

    private void Do(Func<CommitResult> act, string done)
    {
        var r = act();
        Rebuild(r.Ok ? done : r.Error);
    }
}
