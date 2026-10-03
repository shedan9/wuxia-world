using Godot;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Game.Presentation.Art;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 队伍页（P，或暂停菜单“队伍”；架构文档 9.4.4、UI_DESIGN 5.2）：左为所选人物立绘，中为阵位图，右为同行人物名册与详情。
/// 阵位图与战斗画面同向——我方后排在左、前排在右，敌阵在右侧，每排 0 号在上、2 号在下；
/// 点一位队员再点另一格即换位（该格有人则互换），再点一次取消。调换经 <see cref="GameSession.SetFormation"/> 提交，
/// 下一场剧情战按新阵位站。名册列出在队、离过队与相识未同行的人物（身份、状态、等级、关系、出处与成长方式）。
/// 对话、换图或待开战斗期间只能查看。
/// </summary>
public sealed class PartyPage
{
    private static readonly string[] SlotNames = ["上", "中", "下"];

    private readonly PlaySession _play;
    private readonly VBoxContainer _root = Ui.Column(UiPalette.SpaceL);
    private string _who;
    private string? _picked;

    private PartyPage(PlaySession play)
    {
        _play = play;
        _who = play.Game.World.Hero;
    }

    public Control Root => _root;

    private GameSession Game => _play.Game;
    private WorldState World => Game.World;

    public static Control Build(PlaySession play)
    {
        if (play.Game.Growth is null)
        {
            return Ui.Text("未载入战斗内容，无法显示队伍。", UiTheme.DarkLabel, 22);
        }

        var page = new PartyPage(play);
        page.Rebuild();
        return page.Root;
    }

    public static string CellName(int cell) => $"{(PartyRules.RowOf(cell) == 0 ? "前排" : "后排")}{SlotNames[PartyRules.SlotOf(cell)]}";

    private void Rebuild(string? message = null)
    {
        Ui.ClearChildren(_root);

        var board = Ui.Column(UiPalette.SpaceM, Ui.Text("阵位", UiTheme.SectionLabel), Board(),
            Ui.Text("前排护住后排：对方前排有人时，近身单体招只能打前排；远程与穿透一列的招式不受此限。", UiTheme.MutedLabel, 18, wrap: true));
        var roster = Ui.Column(UiPalette.SpaceM, Ui.Text("同行人物", UiTheme.SectionLabel), Roster(), Ui.Expand(Detail(), vertical: true));
        var body = Ui.Row(UiPalette.SpaceXl, CharacterPage.Portrait(_play, _who), Ui.MinSize(board, 660), Ui.Expand(roster));

        var hint = _picked is { } p
            ? $"已选中{_play.Name(p)}（{CellName(PartyRules.CellOf(World, p))}）：点另一格换位，再点一次取消。"
            : Game.CanManage ? "点一位队员，再点要去的格子；格上有人则互换。下一场战斗按此站位。" : "对话、换图或战斗进行中，只能查看；告一段落后再调整。";
        var status = Ui.Text(message ?? hint, UiTheme.AccentLabel, 20, wrap: true);
        var sheet = Ui.Panel(UiTheme.SheetPanel, Ui.Column(UiPalette.SpaceM, Ui.Expand(body, vertical: true), Ui.Rule(), status));
        _root.AddChild(Ui.Expand(sheet, vertical: true));
    }

    // ── 阵位图 ───────────────────────────────────────────

    /// <summary>
    /// 六格阵位：后排在左、前排在右，同排越往下越近、越向外错开（与战斗画面的站位一致）。
    /// 由远及近添加，近处的人物压住远处的。
    /// </summary>
    private Control Board()
    {
        var ground = new PanelContainer { CustomMinimumSize = new Vector2(660, 590) };
        ground.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            FillA = UiPalette.Surface.Lerp(UiPalette.Trim, 0.08f), FillB = UiPalette.SurfaceShade.Lerp(UiPalette.Trim, 0.12f), Ragged = 1.4f, Seed = 17,
            Grain = UiPalette.Ochre with { A = 0.08f }, Wash = UiPalette.Trim with { A = 0.12f },
            Border = UiPalette.Ochre with { A = 0.55f }, BorderWidth = 1.4f, Brush = true, Overshoot = 0.4f,
        }.Margins(0, 0));
        var stage = new Control { MouseFilter = Control.MouseFilterEnum.Pass };
        ground.AddChild(stage);

        foreach (var (text, x) in new[] { ("后排", 150f), ("前排", 430f) })
        {
            var label = Ui.Text(text, UiTheme.MutedLabel, 18);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            stage.AddChild(Ui.Place(label, 0, 0, x - 60, 8, x + 60, 34));
        }

        var enemy = Ui.Text(Ui.Vertical("敌阵") + "\n▶", UiTheme.MutedLabel, 20);
        enemy.HorizontalAlignment = HorizontalAlignment.Center;
        stage.AddChild(Ui.Place(enemy, 0, 0, 604, 230, 650, 360));

        var at = PartyRules.Cells(World).ToDictionary(x => x.Cell, x => x.Id);
        var names = new List<Control>();
        Control? first = null;
        for (var slot = 0; slot < 3; slot++)
        {
            foreach (var row in new[] { 1, 0 })
            {
                var cell = row * 3 + slot;
                var feet = new Vector2((row == 0 ? 470f : 190f) - slot * 36f, 206f + slot * 158f);
                var origin = feet - FormationCell.Feet;
                var button = Cell(cell, at.GetValueOrDefault(cell), stage, origin, names);
                stage.AddChild(Ui.Place(button, 0, 0, origin.X, origin.Y, origin.X + 196, origin.Y + 156));
                if (at.GetValueOrDefault(cell) == (_picked ?? _who))
                {
                    first = button;
                }
            }
        }

        // 名字最后加，压在所有人物之上，不被近处的人挡住。
        foreach (var name in names)
        {
            stage.AddChild(name);
        }

        first?.CallDeferred(Control.MethodName.GrabFocus);
        return ground;
    }

    /// <summary>一格：站位印记按钮；有人时人物立在印记上（画在按钮之下的同一层，伸出格外不挡点击）。</summary>
    private FormationCell Cell(int cell, string? who, Control stage, Vector2 origin, List<Control> names)
    {
        var button = new FormationCell { Occupied = who is not null, Picked = who is not null && who == _picked, Seed = cell * 1.7f + 0.3f };
        button.Disabled = !Game.CanManage;
        button.TooltipText = CellName(cell);
        button.Pressed += () => PressCell(cell, who);
        if (who is null)
        {
            var empty = Ui.Text(CellName(cell), UiTheme.MutedLabel, 16);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Modulate = Colors.White with { A = 0.55f };
            button.AddChild(Ui.Place(empty, 0, 0, 0, 116, 196, 140));
            return button;
        }

        var feet = origin + FormationCell.Feet;
        if (FigureArt.Find(Looks.Of(who).ArtId) is { } figure)
        {
            var art = new TextureRect
            {
                Texture = figure.Texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, FlipH = true,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            stage.AddChild(Ui.Place(art, 0, 0, feet.X - 80, feet.Y - 176, feet.X + 80, feet.Y + 6));
        }
        else
        {
            var glyph = Ui.Glyph(_play.Name(who)[..1], Looks.Of(who).Tone, 72);
            glyph.MouseFilter = Control.MouseFilterEnum.Ignore;
            stage.AddChild(Ui.Place(glyph, 0, 0, feet.X - 36, feet.Y - 90, feet.X + 36, feet.Y - 18));
        }

        var level = LevelText(who);
        var name = Ui.Text(level.Length > 0 ? $"{_play.Name(who)}　{level}" : _play.Name(who), size: 20);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.MouseFilter = Control.MouseFilterEnum.Ignore;
        name.AddThemeColorOverride("font_outline_color", UiPalette.Surface);
        name.AddThemeConstantOverride("outline_size", 8);
        names.Add(Ui.Place(name, 0, 0, origin.X - 20, origin.Y + 122, origin.X + 216, origin.Y + 152));
        return button;
    }

    private void PressCell(int cell, string? who)
    {
        if (_picked is null)
        {
            if (who is null)
            {
                Rebuild("先点一位队员，再点要去的格子。");
                return;
            }

            _picked = who;
            _who = who;
            Rebuild();
            return;
        }

        var moving = _picked;
        _picked = null;
        if (moving == who)
        {
            Rebuild();
            return;
        }

        var r = Game.SetFormation(moving, cell);
        Rebuild(r.Ok
            ? who is null ? $"{_play.Name(moving)}移到{CellName(cell)}" : $"{_play.Name(moving)}与{_play.Name(who)}互换位置"
            : r.Error);
    }

    // ── 名册与详情 ───────────────────────────────────────

    /// <summary>名册：在队（按队伍次序）→ 离过队 → 相识而未同行的可同行人物。</summary>
    private IEnumerable<string> People()
    {
        var content = Game.Rules.Content;
        var departed = World.Companions.Keys.Where(id => !World.Party.Contains(id));
        var known = World.Met.Where(id => !World.Party.Contains(id) && !World.Companions.ContainsKey(id)
            && content.Characters.TryGetValue(id, out var c) && c.Party != PartyRole.None);
        return World.Party.Concat(departed).Concat(known);
    }

    private Control Roster()
    {
        var group = new ButtonGroup();
        var list = Ui.Column(UiPalette.SpaceS);
        foreach (var id in People())
        {
            var who = id;
            var glyph = Ui.Glyph(_play.Name(id)[..1], Looks.Of(id).Tone, 48);
            list.AddChild(Ui.ListRow(group, () => Select(who), _play.Name(id), $"{RoleText(id)}　·　{StatusText(id)}", LevelText(id), glyph,
                selected: id == _who));
        }

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 360) };
        scroll.AddChild(Ui.Expand(list));
        return scroll;
    }

    private void Select(string who)
    {
        if (who != _who)
        {
            _who = who;
            Rebuild();
        }
    }

    private Control Detail()
    {
        var content = Game.Rules.Content;
        var column = Ui.Column(UiPalette.SpaceS, Ui.Text(_play.Name(_who), UiTheme.TitleLabel, 30));
        if (_who == World.Hero)
        {
            column.AddChild(Ui.Text($"队伍的核心，不会离队。属性、武学与装备见人物分区（{KeyBindings.Label("open_character")}）。", UiTheme.MutedLabel, 18, wrap: true));
            return Ui.Panel(UiTheme.InsetPanel, column);
        }

        var c = content.Characters.GetValueOrDefault(_who);
        if (c?.SourceWork is { } work)
        {
            column.AddChild(Ui.Text($"出自《{_play.Name(work)}》", UiTheme.AccentLabel, 18));
        }

        var r = World.Relationships.GetValueOrDefault(_who);
        var relation = $"信任 {r?.Trust ?? 0}　好感 {r?.Affection ?? 0}";
        if (r is { Commitments.Count: > 0 })
        {
            relation += "　承诺：" + string.Join("、", r.Commitments.Select(x => _play.Text(x + ".name") ?? x));
        }

        column.AddChild(Ui.Text(relation, size: 20));
        column.AddChild(Ui.Text(PartyRules.RoleOf(Game.Rules, _who) == PartyRole.Recruitable
            ? "伙伴：随主角成长，潜能按其所长自动分配。在队时与主角同得经验，离队期间得一半；再入队时若落后，补到比主角低一级。"
            : "暂时同行：随事件来去，实力随其原著阶段，不随主角成长。", UiTheme.MutedLabel, 18, wrap: true));
        if (World.Companions.TryGetValue(_who, out var state) && state.Joins > 1)
        {
            column.AddChild(Ui.Text($"已同行 {state.Joins} 次", UiTheme.MutedLabel, 18));
        }

        return Ui.Panel(UiTheme.InsetPanel, column);
    }

    private string RoleText(string id) =>
        id == World.Hero ? "主角" : PartyRules.RoleOf(Game.Rules, id) == PartyRole.Recruitable ? "伙伴" : "暂时同行";

    private string StatusText(string id)
    {
        if (World.Party.Contains(id))
        {
            return "在队　" + CellName(PartyRules.CellOf(World, id));
        }

        if (World.Reservations.ContainsKey(id))
        {
            return "另有要事";
        }

        return World.Companions.ContainsKey(id) ? "已离队" : "相识";
    }

    /// <summary>等级：主角与伙伴写等级；经典人物不写（不被读作实力排名，见人物页）。</summary>
    private string LevelText(string id)
    {
        var growth = Game.Growth!;
        if (Game.Rules.Content.Characters.TryGetValue(id, out var c) && c.Origin == CharacterOrigin.Canon)
        {
            return "";
        }

        return $"第 {growth.LevelOf(World, id)} 级";
    }
}
