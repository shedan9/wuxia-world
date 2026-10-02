using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using WuxiaWorld.Application.Persistence;
using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.Characters;
using WuxiaWorld.Domain.Combat;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Infrastructure.Content;
using WuxiaWorld.Infrastructure.Saves;

namespace WuxiaWorld.Domain.Tests;

/// <summary>
/// M2-05 人物成长（架构文档 8.1–8.2、8.4、9.4.2）：等级与潜能、装备、装配、修炼、店铺买卖、
/// 由世界状态推导战斗模板，以及 v1 存档迁移后的旧档补齐。读仓库里的正式内容。
/// </summary>
public sealed class GrowthTests : IDisposable
{
    private const string Hero = "char.hero";
    private const string Shop = "shop.luwan.general";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wuxia-growth-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static (WorldRules Rules, GrowthRules Growth) Rules()
    {
        var rules = ChapterOneWalkthroughTests.Rules();
        return (rules, new GrowthRules(rules, TestContent.Real));
    }

    /// <summary>走到讨教之后、选同行者之前（芦湾客栈）。</summary>
    private static ChapterOneWalkthroughTests.Walker AfterTraining(string mentor = "linghu")
    {
        var (rules, growth) = Rules();
        var w = new ChapterOneWalkthroughTests.Walker(rules, GameSession.NewGame(rules, growth));
        w.PlayAuto();
        w.Interact("stone_marks");
        w.Exit("to_street");
        w.Exit("to_inn");
        w.PlayAuto();
        w.Pick("choice." + mentor);
        w.PlayEvent("event.ch01.mentor_choice");
        return w;
    }

    [Fact]
    public void New_game_hero_starts_at_level_one_wearing_the_starting_jacket()
    {
        var (rules, growth) = Rules();
        var g = GameSession.NewGame(rules, growth);
        var b = g.World.Builds[Hero];
        Assert.Equal(1, growth.Level(g.World));
        Assert.Equal(0, growth.Unspent(g.World, Hero));
        Assert.Equal("item.armor.cloth_jacket", b.Equipped[EquipSlot.Armor]);
        Assert.Equal(0, g.World.CountOf("item.armor.cloth_jacket"));
        Assert.Empty(b.Skills);
    }

    [Theory]
    [InlineData("linghu", "sword", "art.inner.clear_mind")]
    [InlineData("huang", "inner", "art.inner.calm_breath")]
    [InlineData("xiao", "fist", "art.inner.iron_shirt")]
    public void Training_teaches_and_equips_the_whole_style_kit_and_reaches_level_five(string mentor, string style, string mainArt)
    {
        var w = AfterTraining(mentor);
        var s = w.Game.World;
        var b = s.Builds[Hero];
        Assert.Equal(4, b.Skills.Count);
        Assert.All(b.Skills, id => Assert.StartsWith($"skill.{style}.", id, StringComparison.Ordinal));
        Assert.Equal(mainArt, b.MainArt);
        Assert.Equal(5, w.Game.Growth!.Level(s));
        Assert.Equal(StatFormula.PotentialAt(5), w.Game.Growth.Unspent(s, Hero));
        Assert.Equal(40, s.Cultivation);
        Assert.Empty(w.Game.Growth.Issues(s, Hero));
    }

    [Fact]
    public void Recommended_allocation_plus_the_style_weapon_matches_the_m1_preset_exactly()
    {
        // M1 平衡按 5 级预设模板调过；讨教后按推荐分配、换上流派兵器、脱下开局衣物，应与预设完全相同。
        foreach (var (mentor, style, weapon) in new[]
                 {
                     ("linghu", "sword", "item.weapon.iron_sword"), ("huang", "inner", "item.charm.warm_jade"),
                     ("xiao", "fist", "item.weapon.iron_bracers"),
                 })
        {
            var w = AfterTraining(mentor);
            var g = w.Game;
            Assert.True(g.Allocate(Hero, g.Growth!.Recommend(g.World, Hero)).Ok);
            Assert.Equal(0, g.Growth.Unspent(g.World, Hero));
            Assert.True(g.Unequip(Hero, EquipSlot.Armor).Ok);
            w.Exit("out");
            Assert.True(g.Buy(Shop, weapon).Ok);
            Assert.True(g.Equip(Hero, weapon).Ok);

            var preset = TestContent.Real.Combatant($"combatant.hero.{style}");
            var mine = g.Growth.Template(g.World, Hero);
            Assert.Equal(preset.Level, mine.Level);
            Assert.Equal(preset.Attributes, mine.Attributes);
            Assert.Equal(preset.Loadout.Skills.Order(StringComparer.Ordinal), mine.Loadout.Skills.Order(StringComparer.Ordinal));
            Assert.Equal(CombatantFactory.DeriveStats(preset, TestContent.Real), g.Growth.Stats(g.World, Hero));
        }
    }

    [Fact]
    public void Allocation_is_bounded_and_cannot_be_taken_back()
    {
        var g = AfterTraining().Game;
        Assert.False(g.Allocate(Hero, new Attributes(13, 0, 0, 0, 0)).Ok);
        Assert.False(g.Allocate(Hero, new Attributes(-1, 2, 0, 0, 0)).Ok);
        Assert.False(g.Allocate("char.lu_qinghe", new Attributes(1, 0, 0, 0, 0)).Ok);
        Assert.True(g.Allocate(Hero, new Attributes(2, 1, 0, 0, 0)).Ok);
        Assert.Equal(9, g.Growth!.Unspent(g.World, Hero));
        Assert.Equal(new Attributes(7, 6, 5, 5, 5), g.Growth.AttributesOf(g.World, Hero));
    }

    [Fact]
    public void Shop_trades_only_where_it_stands_and_keeps_silver_and_items_consistent()
    {
        var w = AfterTraining();
        var g = w.Game;
        var silver = g.World.Silver;
        Assert.False(g.Buy(Shop, "item.weapon.iron_sword").Ok); // 客栈里没有这家店

        w.Exit("out");
        Assert.False(g.Buy(Shop, "item.quest.relay_copy").Ok); // 不在货单
        Assert.True(g.Buy(Shop, "item.weapon.iron_sword").Ok);
        Assert.Equal(silver - 24, g.World.Silver);
        Assert.False(g.Buy(Shop, "item.armor.leather_vest").Ok); // 银两不够，什么都不改
        Assert.Equal(silver - 24, g.World.Silver);
        Assert.Equal(0, g.World.CountOf("item.armor.leather_vest"));

        Assert.False(g.Sell(Shop, "item.quest.modern_photo").Ok); // 主线必要物品不收
        Assert.True(g.Equip(Hero, "item.weapon.iron_sword").Ok);
        Assert.False(g.Sell(Shop, "item.weapon.iron_sword").Ok); // 装上的不在行囊里
        Assert.True(g.Unequip(Hero, EquipSlot.Weapon).Ok);
        Assert.True(g.Sell(Shop, "item.weapon.iron_sword").Ok);
        Assert.Equal(silver - 24 + 12, g.World.Silver);

        var rations = g.World.CountOf("item.misc.dry_rations");
        Assert.True(g.Buy(Shop, "item.misc.dry_rations", 3).Ok);
        Assert.Equal(rations + 3, g.World.CountOf("item.misc.dry_rations"));
    }

    [Fact]
    public void Equipping_moves_items_between_pack_and_slot_without_creating_or_losing_any()
    {
        var w = AfterTraining();
        var g = w.Game;
        w.Exit("out");
        Assert.True(g.Buy(Shop, "item.armor.leather_vest").Ok);
        Assert.True(g.Equip(Hero, "item.armor.leather_vest").Ok);
        var b = g.World.Builds[Hero];
        Assert.Equal("item.armor.leather_vest", b.Equipped[EquipSlot.Armor]);
        Assert.Equal(1, g.World.CountOf("item.armor.cloth_jacket"));
        Assert.Equal(0, g.World.CountOf("item.armor.leather_vest"));
        Assert.False(g.Equip(Hero, "item.armor.leather_vest").Ok);
        Assert.False(g.Equip(Hero, "item.medicine.golden_sore").Ok);
        Assert.True(g.Equip(Hero, "item.armor.cloth_jacket").Ok);
        var jacket = g.Growth!.Stats(g.World, Hero).MaxHp;
        Assert.True(g.Equip(Hero, "item.armor.leather_vest").Ok);
        Assert.Equal(jacket + 20, g.Growth.Stats(g.World, Hero).MaxHp); // 粗布短打 +20，牛皮坎肩 +40
    }

    [Fact]
    public void Loadout_changes_are_checked_against_learned_arts_and_limits()
    {
        var g = AfterTraining().Game;
        var skills = g.World.Builds[Hero].Skills.ToList();
        Assert.True(g.SetSkills(Hero, [skills[1], skills[0]]).Ok);
        Assert.Equal([skills[1], skills[0]], g.World.Builds[Hero].Skills);
        Assert.False(g.SetSkills(Hero, ["skill.fist.sweep"]).Ok); // 未学
        Assert.False(g.SetSkills(Hero, [skills[0], skills[0]]).Ok);
        Assert.False(g.SetArt(Hero, ArtSlot.Main, "art.inner.iron_shirt").Ok); // 未学
        Assert.True(g.SetArt(Hero, ArtSlot.Qinggong, null).Ok);
        Assert.Null(g.World.Builds[Hero].Qinggong);
    }

    [Fact]
    public void Cultivation_raises_mastery_and_strengthens_the_skill_in_battle()
    {
        var g = AfterTraining().Game;
        const string skill = "skill.sword.pierce";
        Assert.Equal(30, g.Growth!.MasteryCost(g.World, Hero, skill));
        var before = g.Growth.Template(g.World, Hero);
        Assert.True(g.Cultivate(Hero, skill).Ok);
        Assert.Equal(10, g.World.Cultivation);
        Assert.Equal(2, g.World.Builds[Hero].MasteryOf(skill));
        Assert.False(g.Cultivate(Hero, skill).Ok); // 修为不足（下一阶要 60）
        Assert.False(g.Cultivate(Hero, "skill.fist.sweep").Ok); // 未学

        var after = g.Growth.Template(g.World, Hero);
        Assert.Equal(Common.Bp.One, before.PowerOf(skill));
        Assert.Equal(Common.Bp.One + 500, after.PowerOf(skill));
        Assert.Equal(Common.Bp.One, after.PowerOf("skill.sword.probe"));

        // 同一目标的伤害预估随熟练度提高。
        int MaxDamage(CombatantTemplate hero)
        {
            var engine = new BattleEngine(TestContent.Real);
            var (state, _) = engine.Start(new BattleSetup
            {
                EncounterId = "battle.01.escort_skirmish", Seed = 1, Allies = [new AllyEntry(hero, "char.hero", new Position(0, 1))],
            });
            return engine.Estimate(state, "char.hero", skill, "enemy.escort_a")!.DamageMax;
        }

        Assert.True(MaxDamage(after) > MaxDamage(before));
    }

    [Fact]
    public void Management_is_refused_during_dialogue_and_pending_battles()
    {
        var w = AfterTraining();
        var g = w.Game;
        var d = g.StartEvent(g.Events.First(e => !e.Auto).Id);
        Assert.False(g.CanManage);
        Assert.False(g.Allocate(Hero, new Attributes(1, 0, 0, 0, 0)).Ok);
        g.CancelDialogue(d);
        Assert.True(g.CanManage);
    }

    [Fact]
    public void Level_up_is_announced_when_experience_crosses_a_threshold()
    {
        var (rules, growth) = Rules();
        var g = GameSession.NewGame(rules, growth);
        var c = g.World.Clone();
        var r = new EffectResult();
        rules.Apply(c, [new WorldEffect { Type = WorldEffectType.GrantExperience, Amount = 95 }], r);
        Assert.Contains(r.Notices, n => n.Kind == "level_up" && n.Amount == 4);
        Assert.Equal(4, rules.LevelOf(c.Experience));
        Assert.Equal(140, rules.NextLevelAt(4));
        Assert.Null(rules.NextLevelAt(8));
        Assert.Equal(8, rules.LevelOf(100_000));
    }

    [Fact]
    public void Builds_survive_a_save_round_trip()
    {
        var w = AfterTraining();
        var g = w.Game;
        g.Allocate(Hero, new Attributes(1, 2, 3, 0, 0));
        g.Cultivate(Hero, "skill.sword.probe");
        var store = new FileSaveStore(_dir);
        store.Write(SaveSlot.Manual(1), new SaveGame { Header = new SaveHeader { Sequence = 1 }, World = g.World });
        var read = store.Read(SaveSlot.Manual(1));
        Assert.True(read.Ok, read.Error);
        Assert.Equal(g.World.Hash(), read.Game!.World.Hash());
        Assert.Equal("item.armor.cloth_jacket", read.Game.World.Builds[Hero].Equipped[EquipSlot.Armor]);
    }

    /// <summary>
    /// 迁移样本：把讨教之后的世界写成“v1 存档”（去掉 v2 新增的修为与成长构成、讨教只学到一招的旧内容效果），
    /// 由当前程序读取：结构迁移补空字段，读档后补齐流派武学、开局衣物与追赶经验。
    /// </summary>
    [Fact]
    public void Version_one_save_is_migrated_and_the_hero_build_is_filled_in()
    {
        var w = AfterTraining("xiao");
        var old = w.Game.World.Clone();
        old.Builds.Clear();
        old.Cultivation = 0;
        old.Experience = 0; // v1 时代讨教不给经验
        old.Skills.Clear();
        old.Skills.Add("skill.fist.shield_palm"); // v1 讨教只学一招
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "manual_05.json");
        File.WriteAllText(path, V1File(old));

        var read = new FileSaveStore(_dir).Read(SaveSlot.Manual(5));
        Assert.True(read.Ok, read.Error);
        Assert.True(read.Migrated);
        Assert.True(File.Exists(path + ".v1.bak"));

        var (rules, growth) = Rules();
        var g = new GameSession(rules, read.Game!.World, growth);
        Assert.NotNull(g.UpgradeLegacy());
        Assert.Null(g.UpgradeLegacy()); // 只补一次
        var b = g.World.Builds[Hero];
        Assert.Equal(["skill.fist.counter_stance", "skill.fist.heavy_palm", "skill.fist.shield_palm", "skill.fist.sweep"], b.Skills.Order(StringComparer.Ordinal));
        Assert.Equal("art.inner.iron_shirt", b.MainArt);
        Assert.Equal(["art.talent.parry"], b.Talents);
        Assert.Equal("item.armor.cloth_jacket", b.Equipped[EquipSlot.Armor]);
        Assert.Equal(5, growth.Level(g.World));
        Assert.Equal(40, g.World.Cultivation);
    }

    /// <summary>按 v1 结构写一份存档文件（世界里没有 cultivation 与 builds 字段）。</summary>
    private static string V1File(WorldState world)
    {
        var header = new SaveHeader { SaveSchemaVersion = 1, Sequence = 9, MapId = world.MapId };
        var payload = System.Text.Json.JsonSerializer.SerializeToNode(new SaveGame { Header = header, World = world }, FileSaveStore.Json)!.AsObject();
        var w = payload["world"]!.AsObject();
        w.Remove("cultivation");
        w.Remove("builds");
        var text = payload.ToJsonString();
        var sum = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        return $"{{\"checksum\":\"{sum}\",\"payload\":{text}}}";
    }

    [Fact]
    public void Validator_reports_bad_growth_shop_and_equipment_data()
    {
        var b = ChapterOneWalkthroughTests.Bundle.Value;
        var broken = b with
        {
            Items = [.. b.Items, new ItemDefinition { Id = "item.weapon.blank", Category = ItemCategory.Weapon, Stack = 1, Price = 1 }],
            Shops = [.. b.Shops, new ShopDefinition { Id = "shop.test.bad", Stock = [new ShopEntry { Item = "item.quest.relay_copy" }] }],
            Progression = b.Progression! with { Experience = [20, 10] },
            Maps =
            [
                .. b.Maps.Select(m => m.Id != "map.jiangnan.inn" ? m : m with
                {
                    Interactables = [.. m.Interactables, new MapInteractable { Id = "ghost_shop", Kind = InteractableKind.Shop, Shop = "shop.none" }],
                }),
            ],
            Chapters =
            [
                b.Chapters[0] with
                {
                    Dialogues =
                    [
                        b.Chapters[0].Dialogues[0] with
                        {
                            Nodes =
                            [
                                .. b.Chapters[0].Dialogues[0].Nodes.Select(n => n.Type != DialogueNodeType.Effect && n.Effects.Count == 0 ? n : n with
                                {
                                    Effects = [.. n.Effects, new WorldEffect { Type = WorldEffectType.LearnSkill, Id = "art.inner.parry_typo" }],
                                }),
                            ],
                        },
                        .. b.Chapters[0].Dialogues.Skip(1),
                    ],
                },
            ],
        };
        var errors = WorldContentValidator.Validate(broken, TestContent.Bundle);
        Assert.Contains(errors, e => e.Contains("item.weapon.blank", StringComparison.Ordinal) && e.Contains("加成", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("shop.test.bad", StringComparison.Ordinal) && e.Contains("任务物品", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("经验表", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("ghost_shop", StringComparison.Ordinal) && e.Contains("shop.none", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("art.inner.parry_typo", StringComparison.Ordinal));
    }
}
