using System.Security.Cryptography;
using System.Text;
using WuxiaWorld.Application.Persistence;
using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.Characters;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Infrastructure.Content;
using WuxiaWorld.Infrastructure.Saves;

namespace WuxiaWorld.Domain.Tests;

/// <summary>
/// M2-06 伙伴（架构文档 8.4、9.4.4）：阵位（默认格位、调换、离队让位）、可招募伙伴随主角成长、离队有限追赶、
/// 暂时同行者沿用角色模板、同行记录存读档与 v3 存档迁移，以及同行身份的内容校验。读仓库里的正式内容。
/// </summary>
public sealed class PartyTests : IDisposable
{
    private const string Hero = "char.hero";
    private const string Lu = "char.lu_qinghe";
    private const string Linghu = "char.linghu_chong";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wuxia-party-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static ChapterOneWalkthroughTests.Walker NewWalker()
    {
        var rules = ChapterOneWalkthroughTests.Rules();
        return new ChapterOneWalkthroughTests.Walker(rules, GameSession.NewGame(rules, new GrowthRules(rules, TestContent.Real)));
    }

    private static WorldEffect Effect(WorldEffectType type, string? id = null, int amount = 0) => new() { Type = type, Id = id, Amount = amount };

    private static EffectResult Apply(GameSession g, WorldState s, params WorldEffect[] effects)
    {
        var r = new EffectResult();
        g.Rules.Apply(s, effects, r);
        Assert.True(r.Ok, r.Error);
        return r;
    }

    [Fact]
    public void Hero_stands_front_middle_and_lu_qinghe_joins_back_middle_level_with_the_hero()
    {
        var w = NewWalker();
        Assert.Equal([(Hero, 1)], PartyRules.Cells(w.Game.World));

        w.PlayAuto(); // 开场：陆青禾入队
        var s = w.Game.World;
        Assert.Equal([(Hero, 1), (Lu, 4)], PartyRules.Cells(s));
        Assert.Equal(1, s.Companions[Lu].Joins);
        Assert.Equal(s.Experience, s.Companions[Lu].Experience);
        Assert.Equal(5, w.Game.Growth!.LevelOf(s, Lu)); // 不低于角色模板等级
    }

    /// <summary>整章：同行者与援手依次站前排右、前排左；章末二人离队、让出格位，同行记录保留；陆青禾一路与主角同级。</summary>
    [Fact]
    public void Chapter_keeps_lu_qinghe_level_with_the_hero_and_remembers_departed_companions()
    {
        var w = NewWalker();
        w.PlayChapter("linghu", "sealed", side: false);
        var s = w.Game.World;
        var g = w.Game.Growth!;

        Assert.Equal([Hero, Lu], s.Party);
        Assert.Equal([(Hero, 1), (Lu, 4)], PartyRules.Cells(s));
        Assert.Equal([Hero, Lu], s.Formation.Keys);
        Assert.Equal(s.Experience, s.Companions[Lu].Experience);
        Assert.Equal(Math.Max(5, g.Level(s)), g.LevelOf(s, Lu));
        // 同行者令狐冲与旧渡对峙时出手的一位援手都留有同行记录。
        Assert.Equal(2, s.Companions.Keys.Count(id => id is Linghu or "char.huang_rong" or "char.xiao_feng"));

        // 令狐冲是选定的同行者，在码头前入队，旧渡对峙时不重复入队。
        Assert.Equal(1, s.Companions[Linghu].Joins);
    }

    [Fact]
    public void Swapping_cells_trades_places_and_bad_requests_are_rejected()
    {
        var w = NewWalker();
        w.PlayAuto();
        var g = w.Game;

        var r = g.SetFormation(Lu, 1);
        Assert.True(r.Ok, r.Error);
        Assert.Equal([(Hero, 4), (Lu, 1)], PartyRules.Cells(g.World));

        Assert.True(g.SetFormation(Hero, 0).Ok);
        Assert.Equal([(Hero, 0), (Lu, 1)], PartyRules.Cells(g.World));

        var before = g.World.Hash();
        Assert.False(g.SetFormation(Linghu, 2).Ok);
        Assert.False(g.SetFormation(Lu, 6).Ok);
        Assert.Equal(before, g.World.Hash());
    }

    [Fact]
    public void Cells_fill_missing_or_clashing_records_without_changing_state()
    {
        var s = new WorldState { Party = [Hero, Lu, Linghu] };
        s.Formation[Hero] = 4;
        s.Formation[Lu] = 4; // 与主角重叠
        s.Formation["char.gone"] = 2; // 已不在队
        Assert.Equal([(Hero, 4), (Lu, 1), (Linghu, 2)], PartyRules.Cells(s));
        Assert.Equal(3, s.Formation.Count);
    }

    /// <summary>离队期间得一半经验；再入队若落后主角一级以上，补到“主角等级 − 1”的起点，并发追赶通知。</summary>
    [Fact]
    public void Benched_companion_gains_half_and_catches_up_to_one_level_below_on_rejoin()
    {
        var w = NewWalker();
        w.PlayAuto();
        var g = w.Game;
        var s = g.World.Clone();
        var rules = g.Rules;

        Apply(g, s, Effect(WorldEffectType.LeaveParty, Lu));
        Assert.False(s.Formation.ContainsKey(Lu));
        var bench = s.Companions[Lu].Experience;
        Apply(g, s, Effect(WorldEffectType.GrantExperience, amount: 201));
        Assert.Equal(bench + 100, s.Companions[Lu].Experience);

        Apply(g, s, Effect(WorldEffectType.GrantExperience, amount: 200));
        var heroLevel = rules.LevelOf(s.Experience);
        Assert.True(rules.LevelOf(s.Companions[Lu].Experience) < heroLevel - 1);

        var r = Apply(g, s, Effect(WorldEffectType.JoinParty, Lu));
        Assert.Equal(2, s.Companions[Lu].Joins);
        Assert.Equal(rules.ExperienceFor(heroLevel - 1), s.Companions[Lu].Experience);
        Assert.Contains(r.Notices, n => n.Kind == "caught_up" && n.Id == Lu && n.Amount == heroLevel - 1);
        Assert.Equal(4, PartyRules.CellOf(s, Lu));

        // 没落后那么多时不补。
        Apply(g, s, Effect(WorldEffectType.LeaveParty, Lu));
        Apply(g, s, Effect(WorldEffectType.GrantExperience, amount: 2));
        var kept = s.Companions[Lu].Experience;
        Assert.DoesNotContain(Apply(g, s, Effect(WorldEffectType.JoinParty, Lu)).Notices, n => n.Kind == "caught_up");
        Assert.Equal(kept, s.Companions[Lu].Experience);
    }

    /// <summary>可招募伙伴高出模板等级的每级潜能按模板特长分配；暂时同行者原样用（占位）角色模板。</summary>
    [Fact]
    public void Companion_templates_grow_by_their_own_strengths_and_temporary_ones_stay_fixed()
    {
        var w = NewWalker();
        w.PlayAuto();
        var g = w.Game;
        var growth = g.Growth!;
        var s = g.World.Clone();
        var baseLu = growth.BaseTemplate(Lu);
        Assert.Equal(baseLu, growth.Template(s, Lu));

        Apply(g, s, Effect(WorldEffectType.GrantExperience, amount: g.Rules.ExperienceFor(7)));
        var lu = growth.Template(s, Lu);
        Assert.Equal(7, lu.Level);
        Assert.Equal(baseLu.Attributes.Total + StatFormula.PotentialAt(7) - StatFormula.PotentialAt(baseLu.Level), lu.Attributes.Total);
        // 臂力、身法是她的特长（模板高出基础属性最多的两项），加得最多。
        Assert.Equal(baseLu.Attributes.Strength + 2, lu.Attributes.Strength);
        Assert.Equal(baseLu.Attributes.Agility + 2, lu.Attributes.Agility);
        Assert.Equal(baseLu.Loadout, lu.Loadout);

        Apply(g, s, Effect(WorldEffectType.JoinParty, Linghu));
        Assert.Equal(growth.BaseTemplate(Linghu), growth.Template(s, Linghu));
        Assert.Equal(GrowthRules.PlaceholderCompanion, growth.Template(s, Linghu).Id);
    }

    [Fact]
    public void Formation_and_companion_records_survive_a_save_round_trip()
    {
        var w = NewWalker();
        w.PlayAuto();
        Assert.True(w.Game.SetFormation(Lu, 3).Ok);
        var store = new FileSaveStore(_dir);
        store.Write(SaveSlot.Manual(1), new SaveGame { Header = new SaveHeader { Sequence = 1 }, World = w.Game.World });
        var read = store.Read(SaveSlot.Manual(1));
        Assert.True(read.Ok, read.Error);
        Assert.Equal(3, read.Game!.World.Formation[Lu]);
        Assert.Equal(w.Game.World.Companions[Lu].Experience, read.Game.World.Companions[Lu].Experience);
        Assert.Equal(w.Game.World.Hash(), read.Game.World.Hash());
    }

    /// <summary>迁移样本：v3 存档（没有 formation 与 companions）按队伍次序取默认格位，在队伙伴记入队一次、经验与主角持平。</summary>
    [Fact]
    public void Version_three_save_gets_default_cells_and_companion_records()
    {
        var w = NewWalker();
        w.PlayChapter("xiao", "public", side: false);
        var g = w.Game;
        var s = g.World.Clone();
        Apply(g, s, Effect(WorldEffectType.JoinParty, "char.xiao_feng"));
        var header = new SaveHeader { SaveSchemaVersion = 3, Sequence = 2, MapId = s.MapId };
        var payload = System.Text.Json.JsonSerializer.SerializeToNode(new SaveGame { Header = header, World = s }, FileSaveStore.Json)!.AsObject();
        Assert.True(payload["world"]!.AsObject().Remove("formation"));
        Assert.True(payload["world"]!.AsObject().Remove("companions"));
        var text = payload.ToJsonString();
        var sum = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "manual_05.json");
        File.WriteAllText(path, $"{{\"checksum\":\"{sum}\",\"payload\":{text}}}");

        var read = new FileSaveStore(_dir).Read(SaveSlot.Manual(5));
        Assert.True(read.Ok, read.Error);
        Assert.True(read.Migrated);
        Assert.True(File.Exists(path + ".v3.bak"));
        var m = read.Game!.World;
        Assert.Equal([(Hero, 1), (Lu, 4), ("char.xiao_feng", 2)], PartyRules.Cells(m));
        Assert.Equal([Lu, "char.xiao_feng"], m.Companions.Keys);
        Assert.Equal(m.Experience, m.Companions[Lu].Experience);
        Assert.Equal(1, m.Companions[Lu].Joins);
    }

    [Fact]
    public void Validator_requires_a_party_role_for_joiners_and_a_template_for_recruitables()
    {
        var b = ChapterOneWalkthroughTests.Bundle.Value;
        var broken = b with
        {
            Characters =
            [
                .. b.Characters.Select(c => c.Id switch
                {
                    Linghu => c with { Party = PartyRole.None },
                    Lu => c with { Combatant = null },
                    Hero => c with { Party = PartyRole.Temporary },
                    _ => c,
                }),
            ],
        };

        var errors = WorldContentValidator.Validate(broken);
        Assert.Contains(errors, e => e.Contains("JoinParty", StringComparison.Ordinal) && e.Contains(Linghu, StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains(Lu, StringComparison.Ordinal) && e.Contains("可招募伙伴须有战斗模板", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains(Hero, StringComparison.Ordinal) && e.Contains("主角不设同行身份", StringComparison.Ordinal));
        Assert.DoesNotContain(WorldContentValidator.Validate(b), e => e.Contains("同行身份", StringComparison.Ordinal) || e.Contains("JoinParty", StringComparison.Ordinal));
    }
}
