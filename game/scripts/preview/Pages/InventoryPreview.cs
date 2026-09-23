using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>背包 / 装备 / 商店展示页。选择、比较与购买均为页面内的固定状态演示。</summary>
public partial class InventoryPreview : PreviewScreen
{
    protected override string SealText => "行囊";
    protected override string Title => "行囊、装备与商店";
    protected override string Subtitle => "物品分类、装备前后对比与交易面板";

    protected override IReadOnlyList<(string Name, Func<Control> Build)> Tabs =>
    [
        ("行囊", BuildBag),
        ("装备", BuildEquipment),
        ("商店", BuildShop),
    ];

    private static Control BuildBag()
    {
        var detail = new MarginContainer();
        var list = Ui.Column(4);
        var rows = new ButtonGroup();

        void Fill(SampleItemKind? filter)
        {
            Ui.ClearChildren(list);
            var first = true;
            foreach (var item in InventorySamples.Bag.Where(i => filter is null || i.Kind == filter))
            {
                list.AddChild(Ui.ListRow(rows, () => ShowItem(detail, item), item.Name,
                    InventorySamples.KindName(item.Kind), $"× {item.Count}", Glyph(item), first));
                first = false;
            }
        }

        var chips = new ButtonGroup();
        var filters = Ui.Row(UiPalette.SpaceS, Ui.Toggle("全部", UiTheme.ChipButton, chips, () => Fill(null), true));
        foreach (var kind in Enum.GetValues<SampleItemKind>())
        {
            filters.AddChild(Ui.Toggle(InventorySamples.KindName(kind), UiTheme.ChipButton, chips, () => Fill(kind)));
        }

        Fill(null);
        var left = Ui.MinSize(Ui.Column(UiPalette.SpaceM, filters, list,
            Ui.Text($"铜钱 {InventorySamples.Money}　　负重 14 / 40", UiTheme.MutedLabel)), 620);
        return Ui.Row(UiPalette.SpaceXl, left, Ui.Expand(detail));
    }

    private static void ShowItem(Container host, SampleItem item)
    {
        Ui.ClearChildren(host);
        var body = Ui.Column(UiPalette.SpaceM,
            Ui.Row(UiPalette.SpaceL, Glyph(item, 96),
                Ui.Column(UiPalette.SpaceS, Ui.Text(item.Name, UiTheme.SectionLabel),
                    Ui.Text($"{InventorySamples.KindName(item.Kind)}　　持有 {item.Count}", UiTheme.MutedLabel))),
            Ui.Text(item.Description, wrap: true));

        if (item.Stats.Count > 0)
        {
            body.AddChild(Ui.Rule());
            foreach (var (stat, value) in item.Stats)
            {
                body.AddChild(Ui.Row(UiPalette.SpaceM, Ui.MinSize(Ui.Text(stat, UiTheme.MutedLabel), 160), Ui.Text(Signed(value))));
            }
        }

        if (item.Trait is not null)
        {
            body.AddChild(Ui.Text($"词条　{item.Trait}", UiTheme.AccentLabel));
        }

        body.AddChild(Ui.Rule());
        var quest = item.Kind == SampleItemKind.Quest;
        var actions = item.Kind switch
        {
            SampleItemKind.Weapon or SampleItemKind.Armor => Ui.Button("装备", UiTheme.PrimaryButton),
            SampleItemKind.Medicine or SampleItemKind.Misc => Ui.Button("使用", UiTheme.PrimaryButton),
            _ => Ui.Button("查看线索", UiTheme.PrimaryButton),
        };
        body.AddChild(Ui.Row(UiPalette.SpaceM, Ui.MinSize(actions, 200, 56),
            Ui.MinSize(Ui.Button("丢弃", disabled: quest), 160, 56)));
        if (quest)
        {
            body.AddChild(Ui.Text("任务物品不能丢弃或出售。", UiTheme.MutedLabel));
        }

        host.AddChild(Ui.Panel(UiTheme.InsetPanel, body));
    }

    private static Control BuildEquipment()
    {
        var compare = new MarginContainer();
        var candidates = Ui.Column(4);
        var slotGroup = new ButtonGroup();
        var slots = Ui.Column(4);

        void PickSlot(string slot, SampleItem? equipped)
        {
            Ui.ClearChildren(candidates);
            var group = new ButtonGroup();
            var pool = InventorySamples.Bag.Where(i => SlotOf(i) == slot).ToList();
            if (pool.Count == 0)
            {
                candidates.AddChild(Ui.Text("行囊里没有可装在此处的物品。", UiTheme.MutedLabel));
                Ui.ClearChildren(compare);
                return;
            }

            // 默认选中第一件未装备的候选，直接展示对比状态。
            var initial = pool.FirstOrDefault(i => i != equipped) ?? pool[0];
            foreach (var item in pool)
            {
                var tag = item == equipped ? "已装备" : null;
                candidates.AddChild(Ui.ListRow(group, () => ShowCompare(compare, equipped, item), item.Name,
                    item.Trait, tag, Glyph(item, 48), item == initial));
            }
        }

        var firstSlot = true;
        foreach (var (slot, equipped) in InventorySamples.Slots)
        {
            slots.AddChild(Ui.ListRow(slotGroup, () => PickSlot(slot, equipped), slot, equipped?.Name ?? "空", selected: firstSlot));
            firstSlot = false;
        }

        var who = new ButtonGroup();
        var party = Ui.Row(UiPalette.SpaceS,
            Ui.Toggle("主角", UiTheme.ChipButton, who, () => { }, true),
            Ui.Toggle("陆青禾", UiTheme.ChipButton, who, () => { }));

        return Ui.Column(UiPalette.SpaceL, party,
            Ui.Row(UiPalette.SpaceXl,
                Ui.MinSize(Ui.Column(UiPalette.SpaceS, Ui.Text("装备栏", UiTheme.MutedLabel), slots), 320),
                Ui.MinSize(Ui.Column(UiPalette.SpaceS, Ui.Text("可替换", UiTheme.MutedLabel), candidates), 460),
                Ui.Expand(compare)));
    }

    private static void ShowCompare(Container host, SampleItem? current, SampleItem next)
    {
        Ui.ClearChildren(host);
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", UiPalette.SpaceXl);
        grid.AddThemeConstantOverride("v_separation", UiPalette.SpaceS);
        foreach (var header in new[] { "属性", "当前", "替换后", "变化" })
        {
            grid.AddChild(Ui.Text(header, UiTheme.MutedLabel));
        }

        foreach (var (stat, value) in InventorySamples.HeroStats)
        {
            var delta = Bonus(next, stat) - Bonus(current, stat);
            grid.AddChild(Ui.Text(stat));
            grid.AddChild(Ui.Text(value.ToString()));
            grid.AddChild(Ui.Text((value + delta).ToString()));
            var change = Ui.Text(delta switch { > 0 => $"▲ {delta}", < 0 => $"▼ {-delta}", _ => "—" });
            change.AddThemeColorOverride("font_color", delta switch
            {
                > 0 => UiPalette.Mountain,
                < 0 => UiPalette.Cinnabar,
                _ => UiPalette.InkMuted,
            });
            grid.AddChild(change);
        }

        var same = next == current;
        var body = Ui.Column(UiPalette.SpaceM,
            Ui.Text($"{current?.Name ?? "空"}　换为　{next.Name}", UiTheme.SectionLabel),
            grid,
            Ui.Rule(),
            Ui.Text($"词条变化　{current?.Trait ?? "无"}　→　{next.Trait ?? "无"}", wrap: true),
            Ui.Row(UiPalette.SpaceM,
                Ui.MinSize(Ui.Button(same ? "已装备" : "装备", UiTheme.PrimaryButton, disabled: same), 200, 56),
                Ui.MinSize(Ui.Button("卸下", disabled: current is null), 160, 56)));
        host.AddChild(Ui.Panel(UiTheme.InsetPanel, body));
    }

    private static Control BuildShop()
    {
        var detail = new MarginContainer();
        var list = Ui.Column(4);
        var group = new ButtonGroup();
        var first = true;
        foreach (var item in InventorySamples.Shop)
        {
            list.AddChild(Ui.ListRow(group, () => ShowGoods(detail, item), item.Name,
                $"存货 {item.Count}", $"{item.Price} 文", Glyph(item), first));
            first = false;
        }

        var mode = new ButtonGroup();
        var header = Ui.Row(UiPalette.SpaceM,
            Ui.Text("芦湾渡口货摊", UiTheme.SectionLabel),
            Ui.Spacer(),
            Ui.Toggle("买入", UiTheme.ChipButton, mode, () => { }, true),
            Ui.Toggle("卖出", UiTheme.ChipButton, mode, () => { }));

        return Ui.Row(UiPalette.SpaceXl,
            Ui.MinSize(Ui.Column(UiPalette.SpaceM, header, list,
                Ui.Text("精钢剑演示“铜钱不足”的禁用状态。", UiTheme.MutedLabel)), 620),
            Ui.Expand(detail));
    }

    private static void ShowGoods(Container host, SampleItem item)
    {
        Ui.ClearChildren(host);
        var quantity = 1;
        var qtyLabel = Ui.MinSize(Ui.Text(""), 64);
        qtyLabel.HorizontalAlignment = HorizontalAlignment.Center;
        var total = Ui.Text("");
        var after = Ui.Text("", UiTheme.MutedLabel);
        var reason = Ui.Text("", UiTheme.AccentLabel);
        var buy = Ui.MinSize(Ui.Button("买入", UiTheme.PrimaryButton), 200, 56);

        void Refresh()
        {
            var cost = quantity * item.Price;
            var enough = cost <= InventorySamples.Money;
            qtyLabel.Text = quantity.ToString();
            total.Text = $"合计 {cost} 文";
            after.Text = $"买后剩余 {InventorySamples.Money - cost} 文";
            after.Visible = enough;
            reason.Text = enough ? "" : $"铜钱不足：还差 {cost - InventorySamples.Money} 文";
            reason.Visible = !enough;
            buy.Disabled = !enough;
        }

        var minus = Ui.MinSize(Ui.Button("－", onPressed: () => { quantity = Math.Max(1, quantity - 1); Refresh(); }), 56, 48);
        var plus = Ui.MinSize(Ui.Button("＋", onPressed: () => { quantity = Math.Min(item.Count, quantity + 1); Refresh(); }), 56, 48);
        Refresh();

        var body = Ui.Column(UiPalette.SpaceM,
            Ui.Row(UiPalette.SpaceL, Glyph(item, 96),
                Ui.Column(UiPalette.SpaceS, Ui.Text(item.Name, UiTheme.SectionLabel),
                    Ui.Text($"{InventorySamples.KindName(item.Kind)}　　单价 {item.Price} 文", UiTheme.MutedLabel))),
            Ui.Text(item.Description, wrap: true),
            Ui.Rule(),
            Ui.Row(UiPalette.SpaceM, Ui.Text("数量"), minus, qtyLabel, plus),
            total, after, reason,
            Ui.Row(UiPalette.SpaceM, buy),
            Ui.Text($"持有铜钱 {InventorySamples.Money} 文", UiTheme.MutedLabel));
        host.AddChild(Ui.Panel(UiTheme.InsetPanel, body));
    }

    private static int Bonus(SampleItem? item, string stat) =>
        item?.Stats.Where(s => s.Stat == stat).Sum(s => s.Value) ?? 0;

    private static string? SlotOf(SampleItem item) => item.Kind switch
    {
        SampleItemKind.Weapon => "武器",
        SampleItemKind.Armor => "衣甲",
        _ => null,
    };

    private static string Signed(int value) => value > 0 ? $"+{value}" : value.ToString();

    private static PanelContainer Glyph(SampleItem item, int size = 56) => Ui.Glyph(item.Glyph, item.Kind switch
    {
        SampleItemKind.Weapon => UiPalette.Ink,
        SampleItemKind.Armor => UiPalette.Mountain,
        SampleItemKind.Medicine => UiPalette.Cinnabar,
        SampleItemKind.Quest => UiPalette.OldGold.Darkened(0.4f),
        _ => UiPalette.InkMuted,
    }, size);
}
