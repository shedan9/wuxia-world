using Godot;
using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 店铺面板（在店铺交互点按 E 打开）：买入 / 卖出两页，左列货物，右侧说明、数量与合计；
/// 银两不足、不收的物品直接写明原因。每笔买卖都是 <see cref="GameSession.Buy"/> / <see cref="GameSession.Sell"/> 一次事务。
/// 版式沿用 M0 行囊页的商店分页（UI_DESIGN 5.2）。
/// </summary>
public sealed class ShopPanel
{
    private readonly PlaySession _play;
    private readonly string _shopId;
    private readonly VBoxContainer _root = Ui.Column(UiPalette.SpaceL);
    private bool _selling;
    private string? _selected;
    private int _quantity = 1;

    private ShopPanel(PlaySession play, string shopId)
    {
        _play = play;
        _shopId = shopId;
    }

    public Control Root => _root;

    private GameSession Game => _play.Game;
    private WorldState World => Game.World;

    public static Control Build(PlaySession play, string shopId)
    {
        var panel = new ShopPanel(play, shopId);
        panel.Rebuild();
        return panel.Root;
    }

    private void Rebuild(string? message = null)
    {
        Ui.ClearChildren(_root);
        var shop = Game.Rules.Content.Shops[_shopId];
        _root.AddChild(Ui.Row(UiPalette.SpaceL, Ui.Seal("店铺"), Ui.Column(4,
                Ui.Text(_play.Name(_shopId), UiTheme.DarkTitleLabel, 40),
                Ui.Text($"{_play.Name(World.MapId)}　·　收购按原价{shop.BuyBackBp / 1000.0:0.#}成", UiTheme.DarkMutedLabel, 18)),
            Ui.Spacer(), Ui.Text($"银 {World.Silver} 两", UiTheme.GiltLabel, 26), Ui.KeyHints(true, ("Esc", "离开"))));

        var items = Game.Rules.Content.Items;
        var goods = _selling
            ? World.Items.Keys.Where(id => Game.Rules.BuyBackOf(shop, id) > 0).ToList()
            : shop.Stock.Select(e => e.Item).ToList();
        if (_selected is null || !goods.Contains(_selected))
        {
            _selected = goods.FirstOrDefault();
            _quantity = 1;
        }

        var mode = new ButtonGroup();
        var header = Ui.Row(UiPalette.SpaceM,
            Ui.Toggle("买入", UiTheme.ChipButton, mode, () => Mode(false), !_selling),
            Ui.Toggle("卖出", UiTheme.ChipButton, mode, () => Mode(true), _selling));
        var list = Ui.Column(4);
        var rows = new ButtonGroup();
        foreach (var id in goods)
        {
            var item = id;
            var def = items[id];
            var price = _selling ? Game.Rules.BuyBackOf(shop, id) : Game.Rules.PriceOf(shop, id);
            var detail = GrowthText.CategoryName(def.Category) + (GrowthText.BonusLine(def.Bonus) is { Length: > 0 } b ? "　" + b : "")
                + (World.CountOf(id) > 0 ? $"　持有 {World.CountOf(id)}" : "");
            // 预先选中的一行在建立时也会触发回调：只在真的换了选择时重建。
            list.AddChild(Ui.ListRow(rows, () =>
            {
                if (_selected != item)
                {
                    _selected = item;
                    _quantity = 1;
                    Rebuild();
                }
            }, _play.Name(id), detail, $"{price} 两", GrowthText.ItemGlyph(def, _play.Name(id)), id == _selected));
        }

        if (goods.Count == 0)
        {
            list.AddChild(Ui.Text(_selling ? "行囊里没有这家店收的东西（任务物品不收；已装上的先卸下）。" : "货架空了。", UiTheme.MutedLabel, wrap: true));
        }

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(Ui.Expand(list));
        var left = Ui.MinSize(Ui.Column(UiPalette.SpaceM, header, Ui.Expand(scroll, vertical: true)), 640);
        var status = Ui.Text(message ?? "", UiTheme.AccentLabel, 20, wrap: true);
        var sheet = Ui.Panel(UiTheme.SheetPanel, Ui.Column(UiPalette.SpaceM,
            Ui.Expand(Ui.Row(UiPalette.SpaceXl, left, Ui.Expand(_selected is { } sel ? Goods(shop, sel) : new Control())), vertical: true), status));
        _root.AddChild(Ui.Expand(sheet, vertical: true));
    }

    private void Mode(bool selling)
    {
        if (_selling != selling)
        {
            _selling = selling;
            _selected = null;
            Rebuild();
        }
    }

    private Control Goods(ShopDefinition shop, string id)
    {
        var def = Game.Rules.Content.Items[id];
        var name = _play.Name(id);
        var price = _selling ? Game.Rules.BuyBackOf(shop, id) : Game.Rules.PriceOf(shop, id);
        var most = _selling ? World.CountOf(id) : Math.Max(1, World.Silver / Math.Max(1, price));
        _quantity = Math.Clamp(_quantity, 1, Math.Max(1, most));
        var total = price * _quantity;
        var enough = _selling || total <= World.Silver;

        var body = Ui.Column(UiPalette.SpaceM,
            Ui.Row(UiPalette.SpaceL, GrowthText.ItemGlyph(def, name, 96), Ui.Column(UiPalette.SpaceS,
                Ui.Text(name, UiTheme.SectionLabel),
                Ui.Text($"{GrowthText.CategoryName(def.Category)}　　{(_selling ? "收购价" : "单价")} {price} 两　　持有 {World.CountOf(id)}", UiTheme.MutedLabel, 18))),
            Ui.Text(_play.Text(id + ".desc") ?? _play.Combat.Describe(id) ?? "", size: 20, wrap: true));
        if (GrowthText.BonusLine(def.Bonus) is { Length: > 0 } bonus)
        {
            var hero = Game.Rules.Content.Progression.Hero;
            var current = def.Slot is { } slot ? World.Builds.GetValueOrDefault(hero)?.Equipped.GetValueOrDefault(slot) : null;
            body.AddChild(Ui.Text($"加成　{bonus}", UiTheme.AccentLabel, 20));
            body.AddChild(Ui.Text($"主角{GrowthText.SlotName(def.Slot!.Value)}栏现为：{(current is null ? "空" : $"{_play.Name(current)}（{GrowthText.BonusLine(Game.Rules.Content.Items[current].Bonus)}）")}",
                UiTheme.MutedLabel, 18, wrap: true));
        }

        body.AddChild(Ui.Rule());
        var qty = Ui.MinSize(Ui.Text(_quantity.ToString()), 64);
        qty.HorizontalAlignment = HorizontalAlignment.Center;
        var minus = Ui.MinSize(Ui.Button("－", onPressed: () => Step(-1), disabled: _quantity <= 1), 56, 48);
        var plus = Ui.MinSize(Ui.Button("＋", onPressed: () => Step(1), disabled: _quantity >= most || def.Slot is not null && !_selling), 56, 48);
        body.AddChild(Ui.Row(UiPalette.SpaceM, Ui.Text("数量"), minus, qty, plus));
        body.AddChild(Ui.Text(_selling ? $"合计得银 {total} 两" : $"合计 {total} 两", size: 22));
        if (!enough)
        {
            var why = Ui.Text($"银两不足：还差 {total - World.Silver} 两", size: 20);
            why.AddThemeColorOverride("font_color", UiPalette.Warm);
            body.AddChild(why);
        }
        else if (!_selling)
        {
            body.AddChild(Ui.Text($"买后剩余 {World.Silver - total} 两", UiTheme.MutedLabel, 18));
        }

        var can = Game.CanManage;
        var deal = Ui.Button(_selling ? "卖出" : "买入", UiTheme.PrimaryButton, () =>
        {
            var n = _quantity;
            var r = _selling ? Game.Sell(_shopId, id, n) : Game.Buy(_shopId, id, n);
            if (r.Ok)
            {
                AppHost.Instance.Sound.Play("notify.item", -8);
            }

            _quantity = 1;
            Rebuild(r.Ok ? (_selling ? $"卖出 {name}{(n > 1 ? $" ×{n}" : "")}，得银 {total} 两" : $"买下 {name}{(n > 1 ? $" ×{n}" : "")}，花去 {total} 两") : r.Error);
        }, disabled: !enough || !can);
        deal.CustomMinimumSize = new Vector2(200, 56);
        body.AddChild(Ui.Row(UiPalette.SpaceM, deal));
        if (def.Slot is not null && !_selling)
        {
            body.AddChild(Ui.Text("买下的装备放进行囊，到人物页“装备”或行囊页换上（快捷键 C / I）。", UiTheme.MutedLabel, 18, wrap: true));
        }

        return Ui.Panel(UiTheme.InsetPanel, body);
    }

    private void Step(int delta)
    {
        _quantity += delta;
        Rebuild();
    }
}
