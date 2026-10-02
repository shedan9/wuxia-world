using System.Text.RegularExpressions;
using WuxiaWorld.Domain.Characters;
using WuxiaWorld.Domain.World;

namespace WuxiaWorld.Infrastructure.Content;

/// <summary>
/// 世界内容的语义校验（架构文档 9.3）：ID 格式与重复、跨表引用、落点与出口、无出口地图、任务阶段与分支目标、
/// 无条件自循环、主线保底线索、对白孤立节点与死路选项、台词 ID 唯一、缺失文本。
/// 静态检查只证明数据自洽；剧情可达性另由固定测试存档走查，不宣称能证明所有分支都无死锁。
/// </summary>
public static partial class WorldContentValidator
{
    [GeneratedRegex("^[a-z0-9_]+(\\.[a-z0-9_]+)+$")]
    private static partial Regex IdPattern();

    /// <summary>已弃用的旁白说话人；2026-10-01 起用演出提示节点代替，校验时报错。</summary>
    public const string Narrator = "narrator";

    /// <summary>经典人物考据未完成时的剧情锚点占位。</summary>
    public const string PendingAnchor = "pending";

    public static IReadOnlyList<string> Validate(WorldBundle w, CombatBundle? combat = null)
    {
        var errors = new List<string>();
        void Err(string message) => errors.Add(message);

        var maps = w.Maps.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var quests = w.Quests.GroupBy(q => q.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var dialogues = w.Dialogues.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var events = w.Events.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var items = w.Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        var characters = w.Characters.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var heroes = w.Characters.Where(c => c.Origin == CharacterOrigin.Hero).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var regions = w.Maps.Select(m => m.Region).ToHashSet(StringComparer.Ordinal);
        var encounters = combat?.Encounters.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var combatants = combat?.Combatants.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var itemDefs = w.Items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var shops = w.Shops.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var anchors = w.Anchors.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // 武学 ID 的前缀即种类（世界规则据此自动装配，见 WorldRules.AutoEquip）：招式 skill.*，心法 art.inner.*，轻功 art.qinggong.*，天赋 art.talent.*。
        string? MartialProblem(string id)
        {
            if (combat is null)
            {
                return IdPattern().IsMatch(id) ? null : "ID 格式不对";
            }

            if (id.StartsWith("skill.", StringComparison.Ordinal))
            {
                return combat.Skills.Any(x => x.Id == id) ? null : "招式未定义";
            }

            if (combat.Arts.FirstOrDefault(a => a.Id == id) is not { } art)
            {
                return "不是已定义的招式或心法";
            }

            var prefix = art.Kind switch
            {
                ArtKind.Inner => "art.inner.",
                ArtKind.Qinggong => "art.qinggong.",
                _ => "art.talent.",
            };
            return id.StartsWith(prefix, StringComparison.Ordinal) ? null : $"种类与 ID 前缀不符（应以 {prefix} 开头）";
        }

        var all = w.Maps.Select(x => x.Id).Concat(w.Routes.Select(x => x.Id)).Concat(w.Events.Select(x => x.Id))
            .Concat(w.Quests.Select(x => x.Id)).Concat(w.Dialogues.Select(x => x.Id)).Concat(w.Items.Select(x => x.Id))
            .Concat(w.Characters.Select(x => x.Id)).Concat(w.Shops.Select(x => x.Id)).Concat(w.Anchors.Select(x => x.Id));
        foreach (var group in all.GroupBy(id => id, StringComparer.Ordinal))
        {
            if (group.Count() > 1)
            {
                Err($"重复 ID：{group.Key}");
            }

            if (!IdPattern().IsMatch(group.Key))
            {
                Err($"ID 须为小写 ASCII 点分层：{group.Key}");
            }
        }

        void RequireText(string key)
        {
            if (!w.Text.ContainsKey(key))
            {
                Err($"缺少文本 {key}");
            }
        }

        void SpawnExists(string where, string mapId, string? spawn)
        {
            if (!maps.TryGetValue(mapId, out var m))
            {
                Err($"{where}：地图 {mapId} 不存在");
            }
            else if (spawn is not null && !m.Spawns.Contains(spawn, StringComparer.Ordinal))
            {
                Err($"{where}：地图 {mapId} 没有落点 {spawn}");
            }
        }

        void CheckCondition(string where, Condition? c)
        {
            foreach (var x in Conditions.Flatten(c))
            {
                var ok = x.Type switch
                {
                    ConditionType.All or ConditionType.Any => x.Of.Count > 0,
                    ConditionType.Not => x.Of.Count == 1,
                    ConditionType.HasItem => x.Id is not null && items.Contains(x.Id),
                    ConditionType.QuestStateIs => x.Id is not null && quests.TryGetValue(x.Id, out var q)
                        && (x.Stage is null || q.StageById(x.Stage) is not null),
                    ConditionType.RelationshipAtLeast or ConditionType.CharacterAvailable or ConditionType.PartyContains
                        or ConditionType.CharacterMet => x.Id is not null && characters.Contains(x.Id),
                    ConditionType.FactEquals => x.Id is not null && IdPattern().IsMatch(x.Id) && x.Value is not null,
                    ConditionType.AtRegion => x.Id is not null && regions.Contains(x.Id),
                    ConditionType.AtMap => x.Id is not null && maps.ContainsKey(x.Id),
                    ConditionType.SkillLearned or ConditionType.WorldGateReached or ConditionType.ClueKnown =>
                        x.Id is not null && IdPattern().IsMatch(x.Id),
                    ConditionType.SilverAtLeast => x.Amount >= 0,
                    _ => false,
                };
                if (!ok)
                {
                    Err($"{where}：条件 {x.Type} 引用无效（{x.Id}{(x.Stage is null ? "" : "/" + x.Stage)}）");
                }
            }
        }

        void CheckEffects(string where, IEnumerable<WorldEffect> effects)
        {
            foreach (var e in effects)
            {
                var ok = e.Type switch
                {
                    WorldEffectType.SetFact or WorldEffectType.ClearFact => e.Id is not null && IdPattern().IsMatch(e.Id),
                    WorldEffectType.GrantItem or WorldEffectType.RemoveItem => e.Id is not null && items.Contains(e.Id) && e.Amount > 0,
                    WorldEffectType.ChangeSilver or WorldEffectType.AdvanceClock or WorldEffectType.GrantExperience
                        or WorldEffectType.GrantCultivation => true,
                    WorldEffectType.ChangeRelationship or WorldEffectType.MeetCharacter or WorldEffectType.JoinParty
                        or WorldEffectType.LeaveParty => e.Id is not null && characters.Contains(e.Id),
                    WorldEffectType.AddCommitment => e.Id is not null && characters.Contains(e.Id) && e.Value is not null,
                    WorldEffectType.AddClue => e.Id is not null && IdPattern().IsMatch(e.Id),
                    WorldEffectType.StartQuest or WorldEffectType.FailQuest or WorldEffectType.AbandonQuest =>
                        e.Id is not null && quests.ContainsKey(e.Id),
                    WorldEffectType.CompleteObjective => e.Id is not null && quests.TryGetValue(e.Id, out var q)
                        && q.Stages.Any(s => s.Objectives.Any(o => o.Id == e.Value)),
                    WorldEffectType.LearnSkill => e.Id is not null && MartialProblem(e.Id) is null,
                    WorldEffectType.ReachGate => e.Id is not null && IdPattern().IsMatch(e.Id),
                    WorldEffectType.SetChapter => e.Id is not null && e.Value is not null,
                    WorldEffectType.RequestBattle => e.Id is not null && (encounters is null || encounters.Contains(e.Id)),
                    WorldEffectType.RequestTravel => e.Id is not null && maps.ContainsKey(e.Id),
                    _ => false,
                };
                if (!ok)
                {
                    Err($"{where}：效果 {e.Type} 引用无效（{e.Id}{(e.Value is null ? "" : "/" + e.Value)}）");
                }

                if (e.Type == WorldEffectType.AddClue && e.Id is not null)
                {
                    RequireText(e.Id + ".name");
                }

                if (e.Type == WorldEffectType.RequestTravel && e.Id is not null)
                {
                    SpawnExists(where, e.Id, e.Value);
                }

                CheckEffects(where + "/胜", e.OnVictory);
                CheckEffects(where + "/败", e.OnDefeat);
            }
        }

        // 新游戏
        if (w.NewGame is not { } ng)
        {
            Err("缺少新游戏设置 world/new_game.json");
        }
        else
        {
            SpawnExists("新游戏", ng.Map, ng.Spawn);
            if (ng.Party.Count is < 1 or > WorldRules.MaxParty)
            {
                Err("新游戏：队伍须有主角且不超过 4 人");
            }

            foreach (var c in ng.Party.Where(c => !characters.Contains(c)))
            {
                Err($"新游戏：未定义的人物 {c}");
            }

            CheckEffects("新游戏", ng.Effects);
        }

        // 人物与物品
        foreach (var c in w.Characters)
        {
            RequireText(c.Id + ".name");
            if (c.Origin == CharacterOrigin.Canon && (c.SourceWork is null || c.StoryAnchor is null))
            {
                Err($"{c.Id}：经典人物须写来源作品与剧情锚点（未核验写 pending）");
            }
            else if (c.StoryAnchor is { } anchorId && anchorId != PendingAnchor)
            {
                if (anchors.GetValueOrDefault(anchorId) is not { } a)
                {
                    Err($"{c.Id}：剧情锚点 {anchorId} 未定义");
                }
                else if (a.Character != c.Id || a.Work != c.SourceWork)
                {
                    Err($"{c.Id}：剧情锚点 {anchorId} 属于 {a.Character}（{a.Work}），与人物或来源作品不符");
                }
            }

            if (c.Combatant is not null && combatants is not null && !combatants.Contains(c.Combatant))
            {
                Err($"{c.Id}：战斗模板 {c.Combatant} 不存在");
            }
        }

        // 剧情锚点（ID 重复与格式在上面统一检查）：字段完整、年龄范围合理、改编年龄写明说明、每个锚点都有人物引用（架构文档 2.1、9.3）。
        foreach (var a in w.Anchors)
        {
            var missing = new[] { ("所据文本", a.TextSource), ("章节", a.Chapters), ("原文年龄依据", a.CanonAge), ("身份", a.Identity) }
                .Where(f => string.IsNullOrWhiteSpace(f.Item2)).Select(f => f.Item1).ToList();
            if (missing.Count > 0)
            {
                Err($"{a.Id}：缺少{string.Join("、", missing)}");
            }

            if (a.AgeMin <= 0 || a.AgeMax < a.AgeMin || a.AgeMax > 120)
            {
                Err($"{a.Id}：年龄范围 {a.AgeMin}–{a.AgeMax} 不合理");
            }

            if (a.AgeAdapted && string.IsNullOrWhiteSpace(a.Adaptation))
            {
                Err($"{a.Id}：本作改编年龄须写改编说明（原文年龄、本作年龄与决定依据）");
            }

            if (a.Known.Count == 0)
            {
                Err($"{a.Id}：须列出锚点前已发生的经历");
            }

            if (!characters.Contains(a.Character))
            {
                Err($"{a.Id}：人物 {a.Character} 未定义");
            }
            else if (w.Characters.All(c => c.StoryAnchor != a.Id))
            {
                Err($"{a.Id}：没有人物引用这个锚点");
            }
        }

        foreach (var i in w.Items)
        {
            RequireText(i.Id + ".name");
            if (i.Stack < 1 || i.Price < 0)
            {
                Err($"{i.Id}：堆叠或价格越界");
            }

            if (i.Key && i.Price > 0)
            {
                Err($"{i.Id}：主线必要物品不可出售，不应有价格");
            }

            if (i.Slot is not null && (i.Stack != 1 || i.Bonus == StatBonus.None))
            {
                Err($"{i.Id}：装备须单件（stack 1）并有加成");
            }

            if (i.Slot is null && i.Bonus != StatBonus.None)
            {
                Err($"{i.Id}：只有装备可带属性加成");
            }

            if (i.Slot is not null)
            {
                RequireText(i.Id + ".desc");
            }
        }

        // 店铺
        foreach (var shop in w.Shops)
        {
            RequireText(shop.Id + ".name");
            if (shop.BuyBackBp is < 0 or > 10_000)
            {
                Err($"{shop.Id}：收购折率须在 0–10000 之间");
            }

            if (shop.Stock.Count == 0)
            {
                Err($"{shop.Id}：货单为空");
            }

            foreach (var entry in shop.Stock)
            {
                if (!itemDefs.TryGetValue(entry.Item, out var item))
                {
                    Err($"{shop.Id}：货单物品 {entry.Item} 不存在");
                }
                else if (item.Key || item.Category == ItemCategory.Quest)
                {
                    Err($"{shop.Id}：任务物品 {entry.Item} 不能上货单");
                }
                else if ((entry.Price ?? item.Price) <= 0)
                {
                    Err($"{shop.Id}：{entry.Item} 没有售价");
                }
            }

            if (shop.Stock.GroupBy(e => e.Item).Any(g => g.Count() > 1))
            {
                Err($"{shop.Id}：货单物品重复");
            }
        }

        // 成长设置
        if (w.Progression is not { } prog)
        {
            Err("缺少成长设置 world/progression.json");
        }
        else
        {
            if (!heroes.Contains(prog.Hero))
            {
                Err($"成长设置：{prog.Hero} 不是主角人物");
            }

            if (prog.MaxLevel != StatFormula.MaxLevel)
            {
                Err($"成长设置：经验表给出 {prog.MaxLevel} 级，与规则等级上限 {StatFormula.MaxLevel} 不符");
            }

            if (prog.Experience.Count == 0 || prog.Experience[0] <= 0 || prog.Experience.Zip(prog.Experience.Skip(1)).Any(x => x.Second <= x.First))
            {
                Err("成长设置：经验表须为正且严格递增");
            }

            if (prog.MasteryCosts.Count == 0 || prog.MasteryCosts.Any(c => c <= 0) || prog.MasteryPowerBp <= 0)
            {
                Err("成长设置：熟练度消耗与加成须为正");
            }

            var b = prog.BaseAttributes;
            if (b.Physique < 1 || b.Strength < 1 || b.Root < 1 || b.Agility < 1 || b.Insight < 1)
            {
                Err("成长设置：基础属性须至少为 1");
            }

            foreach (var style in prog.Styles)
            {
                foreach (var id in style.Skills.Concat(style.Arts))
                {
                    if (MartialProblem(id) is { } problem)
                    {
                        Err($"成长设置 流派 {style.Id}：{id} {problem}");
                    }
                }

                if (style.Skills.Count > Domain.Combat.Definitions.Loadout.MaxSkills)
                {
                    Err($"成长设置 流派 {style.Id}：入门招式超过出手栏上限");
                }
            }

            var slots = new HashSet<EquipSlot>();
            foreach (var id in prog.StartingEquipment)
            {
                if (!itemDefs.TryGetValue(id, out var item) || item.Slot is not { } slot)
                {
                    Err($"成长设置：开局装备 {id} 不是已定义的装备");
                }
                else if (!slots.Add(slot))
                {
                    Err($"成长设置：开局装备 {id} 与其他开局装备同槽");
                }
            }

            foreach (var c in prog.CatchUp)
            {
                CheckCondition("成长设置 旧档追赶", c.When);
            }
        }

        foreach (var b in combat?.Items ?? [])
        {
            if (!items.Contains(b.Id))
            {
                Err($"战斗物品 {b.Id} 在物品目录 shared/items/catalog.json 中没有条目");
            }
        }

        // 地图
        foreach (var m in w.Maps)
        {
            RequireText(m.Id + ".name");
            if (!m.Spawns.Contains(m.SafeSpawn, StringComparer.Ordinal))
            {
                Err($"{m.Id}：安全入口 {m.SafeSpawn} 不在落点列表里");
            }

            if (m.Spawns.Count != m.Spawns.Distinct(StringComparer.Ordinal).Count())
            {
                Err($"{m.Id}：落点重复");
            }

            if (m.Exits.Count == 0 && !w.Routes.Any(r => r.From == m.Id))
            {
                Err($"{m.Id}：无出口地图（没有出口也没有路线）");
            }

            foreach (var x in m.Exits)
            {
                SpawnExists($"{m.Id}/{x.Id}", x.To, x.Spawn);
                CheckCondition($"{m.Id}/{x.Id}", x.When);
            }

            foreach (var group in m.Exits.Select(x => x.Id).Concat(m.Interactables.Select(i => i.Id)).GroupBy(id => id))
            {
                if (group.Count() > 1)
                {
                    Err($"{m.Id}：出口或交互物 ID 重复 {group.Key}");
                }
            }

            foreach (var i in m.Interactables)
            {
                var where = $"{m.Id}/{i.Id}";
                RequireText($"{m.Id}.{i.Id}.name"); // 交互提示“E 查看 某物”的对象名
                CheckCondition(where, i.When);
                CheckEffects(where, i.Effects);
                if (i.Dialogue is not null && !dialogues.ContainsKey(i.Dialogue))
                {
                    Err($"{where}：对白 {i.Dialogue} 不存在");
                }

                if (i.Kind == InteractableKind.Shop)
                {
                    if (i.Shop is null || !shops.Contains(i.Shop))
                    {
                        Err($"{where}：店铺 {i.Shop} 不存在");
                    }

                    if (i.Dialogue is not null || i.Effects.Count > 0 || i.Once)
                    {
                        Err($"{where}：店铺交互物只开买卖面板，不带对白、效果，也不是一次性的");
                    }
                }
                else if (i.Shop is not null)
                {
                    Err($"{where}：只有 shop 类交互物可指定店铺");
                }
                else if (i.Dialogue is null && i.Effects.Count == 0)
                {
                    Err($"{where}：交互物既无对白也无效果");
                }
            }
        }

        // 路线
        foreach (var r in w.Routes)
        {
            SpawnExists(r.Id, r.From, null);
            SpawnExists(r.Id, r.To, r.Spawn);
            CheckCondition(r.Id, r.When);
            if (r.Modes.Count == 0)
            {
                Err($"{r.Id}：没有交通方式");
            }

            foreach (var m in r.Modes)
            {
                CheckCondition($"{r.Id}/{m.Mode}", m.When);
                if (m.Silver < 0 || m.Ticks < 0)
                {
                    Err($"{r.Id}/{m.Mode}：费用或时辰为负");
                }
            }

            foreach (var e in r.Encounters)
            {
                if (!events.TryGetValue(e.Event, out var ev))
                {
                    Err($"{r.Id}：途中事件 {e.Event} 不存在");
                }
                else if (ev.Map != r.To)
                {
                    Err($"{r.Id}：途中事件 {e.Event} 须在目的地 {r.To} 结算");
                }

                if (e.ChanceBp is < 0 or > 10000)
                {
                    Err($"{r.Id}：途中事件概率越界");
                }
            }
        }

        // 地区事件
        foreach (var e in w.Events)
        {
            SpawnExists(e.Id, e.Map, null);
            CheckCondition(e.Id, e.When);
            if (!dialogues.ContainsKey(e.Dialogue))
            {
                Err($"{e.Id}：对白 {e.Dialogue} 不存在");
            }

            foreach (var p in e.Participants.Where(p => !characters.Contains(p)))
            {
                Err($"{e.Id}：未定义的参与人物 {p}");
            }

            if (!e.Auto)
            {
                // 需要玩家走近按 E 开始的事件：交互提示写“动作 + 对象”。
                RequireText(e.Id + ".verb");
                RequireText(e.Id + ".name");
            }
        }

        // 任务
        foreach (var q in w.Quests)
        {
            RequireText(q.Id + ".name");
            CheckCondition(q.Id, q.Prerequisites);
            CheckEffects(q.Id + "/奖励", q.Rewards);
            CheckEffects(q.Id + "/失败", q.OnFail);
            CheckEffects(q.Id + "/放弃", q.OnAbandon);
            foreach (var h in q.Hints)
            {
                RequireText(h);
            }

            foreach (var t in q.TriggerMaps)
            {
                SpawnExists(q.Id, t, null);
            }

            if (q.RecoveryMap is not null)
            {
                SpawnExists(q.Id, q.RecoveryMap, null);
            }

            if (q.Stages.Count == 0)
            {
                Err($"{q.Id}：没有阶段");
                continue;
            }

            if (q.Kind == QuestKind.Main && (q.OnAbandon.Count > 0 || q.OnFail.Count > 0 || q.ExpiryNotice is not null))
            {
                Err($"{q.Id}：主线不允许放弃、失败或限时");
            }

            if (q.Kind == QuestKind.Main && q.GuaranteedClue is null)
            {
                Err($"{q.Id}：主线须写保底线索 guaranteed_clue");
            }

            var stageIds = q.Stages.Select(s => s.Id).ToList();
            if (stageIds.Count != stageIds.Distinct(StringComparer.Ordinal).Count())
            {
                Err($"{q.Id}：阶段 ID 重复");
            }

            foreach (var s in q.Stages)
            {
                var where = $"{q.Id}/{s.Id}";
                RequireText($"{q.Id}.stage.{s.Id}");
                CheckEffects(where, s.OnComplete);
                foreach (var target in s.Branches.Select(b => b.Next).Append(s.Next).OfType<string>())
                {
                    if (!stageIds.Contains(target))
                    {
                        Err($"{where}：转入的阶段 {target} 不存在");
                    }

                    if (target == s.Id && s.Objectives.All(o => o.Optional || o.When is null))
                    {
                        Err($"{where}：阶段转回自身，可能无条件自循环");
                    }
                }

                foreach (var b in s.Branches)
                {
                    CheckCondition(where, b.When);
                }

                if (s.Objectives.Select(o => o.Id).Distinct(StringComparer.Ordinal).Count() != s.Objectives.Count)
                {
                    Err($"{where}：目标 ID 重复");
                }

                foreach (var o in s.Objectives)
                {
                    RequireText($"{q.Id}.objective.{o.Id}");
                    CheckCondition($"{where}/{o.Id}", o.When);
                    if (o.Map is not null)
                    {
                        SpawnExists($"{where}/{o.Id}", o.Map, null);
                    }

                    if (o.When is null && !CompletedByEffect(w, q.Id, o.Id))
                    {
                        Err($"{where}/{o.Id}：目标既无条件也没有任何效果完成它");
                    }
                }
            }
        }

        // 主线保底线索：须由支线定义之外的效果授予（对白里的授予是否可达由走查测试覆盖）。
        var sideOnly = new HashSet<object>(w.Quests.Where(x => x.Kind != QuestKind.Main).SelectMany(QuestEffects), ReferenceEqualityComparer.Instance);
        foreach (var q in w.Quests.Where(q => q.GuaranteedClue is not null))
        {
            if (!AllEffects(w).Any(e => e.Type == WorldEffectType.AddClue && e.Id == q.GuaranteedClue && !sideOnly.Contains(e)))
            {
                Err($"{q.Id}：保底线索 {q.GuaranteedClue} 没有支线之外的获取途径");
            }
        }

        // 对白
        var lineIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in w.Dialogues)
        {
            if (d.Status is not ("draft" or "locked"))
            {
                Err($"{d.Id}：status 须为 draft 或 locked");
            }

            var nodes = d.Nodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            if (nodes.Count != d.Nodes.Count)
            {
                Err($"{d.Id}：节点 ID 重复");
            }

            if (!nodes.ContainsKey(d.Entry))
            {
                Err($"{d.Id}：入口节点 {d.Entry} 不存在");
                continue;
            }

            void Target(string where, string? next)
            {
                if (next is not null && !nodes.ContainsKey(next))
                {
                    Err($"{where}：跳转目标 {next} 不存在");
                }
            }

            void Line(string where, string? id)
            {
                if (id is null)
                {
                    Err($"{where}：缺少 line_id");
                }
                else if (!lineIds.Add(id))
                {
                    Err($"{where}：line_id 重复 {id}");
                }
            }

            void CheckInner(string where, DialogueNode n)
            {
                var text = n.Text ?? "";
                var bracketed = text.StartsWith('（') && text.EndsWith('）');
                if (!n.Inner)
                {
                    if (bracketed)
                    {
                        Err($"{where}：整句括号像是心里话，须标 inner（且只限主角）");
                    }

                    return;
                }

                if (n.Speaker is null || !heroes.Contains(n.Speaker))
                {
                    Err($"{where}：只有主角可以有心里话，{n.Speaker} 不行");
                }

                if (bracketed)
                {
                    Err($"{where}：心里话不带括号，由表现层显示样式");
                }

                if (text.Length > DialogueNode.InnerMaxChars)
                {
                    Err($"{where}：心里话 {text.Length} 字，超过 {DialogueNode.InnerMaxChars} 字上限");
                }
            }

            foreach (var n in d.Nodes)
            {
                var where = $"{d.Id}/{n.Id}";
                switch (n.Type)
                {
                    case DialogueNodeType.Line:
                        Line(where, n.LineId);
                        if (string.IsNullOrWhiteSpace(n.Text))
                        {
                            Err($"{where}：台词为空");
                        }

                        if (n.Speaker == Narrator)
                        {
                            Err($"{where}：不再使用旁白，改用演出提示节点（type: stage）由画面表现");
                        }
                        else if (n.Speaker is null || !characters.Contains(n.Speaker))
                        {
                            Err($"{where}：说话人 {n.Speaker} 未定义");
                        }

                        CheckInner(where, n);
                        break;
                    case DialogueNodeType.Stage:
                        if (n.Kind is null)
                        {
                            Err($"{where}：演出提示缺少 kind");
                        }

                        if (string.IsNullOrWhiteSpace(n.Direction))
                        {
                            Err($"{where}：演出提示缺少 direction（制作说明）");
                        }

                        if (n.Kind == StageKind.Title && string.IsNullOrWhiteSpace(n.Caption))
                        {
                            Err($"{where}：标题卡缺少 caption");
                        }

                        if (n.Caption is not null && n.Kind is not (StageKind.Closeup or StageKind.Title))
                        {
                            Err($"{where}：只有特写与标题卡可以显示 caption");
                        }

                        if (n.Caption is not null)
                        {
                            Line(where, n.CaptionId);
                        }

                        if (n.LineId is not null || n.Speaker is not null || n.Text is not null)
                        {
                            Err($"{where}：演出提示不能有台词字段（line_id、speaker、text）");
                        }

                        break;
                    case DialogueNodeType.Choice:
                        if (n.Options.Count == 0)
                        {
                            Err($"{where}：选项节点没有选项");
                        }
                        else if (n.Options.All(o => o.When is not null))
                        {
                            Err($"{where}：所有选项都有条件，可能出现无选项死路");
                        }

                        foreach (var o in n.Options)
                        {
                            Line(where, o.LineId);
                            CheckCondition(where, o.When);
                            CheckEffects(where, o.Effects);
                            Target(where, o.Next);
                        }

                        break;
                    case DialogueNodeType.Branch:
                        if (n.Branches.Count == 0)
                        {
                            Err($"{where}：分流节点没有分支");
                        }

                        foreach (var b in n.Branches)
                        {
                            CheckCondition(where, b.When);
                            Target(where, b.Next);
                        }

                        break;
                    case DialogueNodeType.Effect:
                        if (n.Effects.Count == 0)
                        {
                            Err($"{where}：效果节点没有效果");
                        }

                        CheckEffects(where, n.Effects);
                        break;
                }

                Target(where, n.Next);
                if (n.Next == n.Id)
                {
                    Err($"{where}：节点跳回自身");
                }
            }

            foreach (var orphan in d.Nodes.Select(n => n.Id).Except(Reachable(d, nodes)))
            {
                Err($"{d.Id}/{orphan}：孤立节点（从入口不可达）");
            }

            // 心里话不连续：从一句心里话往后，越过效果、演出提示、跳转与分流，碰到的下一句台词不能还是心里话。
            foreach (var n in d.Nodes.Where(n => n is { Type: DialogueNodeType.Line, Inner: true }))
            {
                if (NextLines(nodes, n.Next).Any(x => x.Inner))
                {
                    Err($"{d.Id}/{n.Id}：心里话后面紧接着又是心里话，合并或删减");
                }
            }
        }

        // 心里话的篇幅：全部对白合计不超过台词的一成。
        var spoken = w.Dialogues.SelectMany(d => d.Nodes).Count(n => n.Type == DialogueNodeType.Line);
        var inner = w.Dialogues.SelectMany(d => d.Nodes).Count(n => n is { Type: DialogueNodeType.Line, Inner: true });
        if (inner * 10 > spoken)
        {
            Err($"心里话 {inner} 句，超过全部台词 {spoken} 句的一成");
        }

        return errors;
    }

    /// <summary>从 <paramref name="start"/> 起，越过非台词节点能直接走到的台词节点。</summary>
    private static IEnumerable<DialogueNode> NextLines(Dictionary<string, DialogueNode> nodes, string? start)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string?>([start]);
        while (stack.TryPop(out var id))
        {
            if (id is null || !seen.Add(id) || !nodes.TryGetValue(id, out var n))
            {
                continue;
            }

            if (n.Type == DialogueNodeType.Line)
            {
                yield return n;
                continue;
            }

            if (n.Type is DialogueNodeType.Choice or DialogueNodeType.End)
            {
                continue;
            }

            foreach (var next in n.Branches.Select(b => b.Next).Append(n.Next))
            {
                stack.Push(next);
            }
        }
    }

    private static HashSet<string> Reachable(DialogueDefinition d, Dictionary<string, DialogueNode> nodes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>([d.Entry]);
        while (stack.TryPop(out var id))
        {
            if (!seen.Add(id) || !nodes.TryGetValue(id, out var n))
            {
                continue;
            }

            foreach (var next in n.Options.Select(o => o.Next).Concat(n.Branches.Select(b => b.Next)).Append(n.Next).OfType<string>())
            {
                stack.Push(next);
            }
        }

        return seen;
    }

    private static bool CompletedByEffect(WorldBundle w, string quest, string objective) =>
        AllEffects(w).Any(e => e.Type == WorldEffectType.CompleteObjective && e.Id == quest && e.Value == objective);

    private static IEnumerable<WorldEffect> QuestEffects(QuestDefinition q) =>
        Expand(q.Rewards.Concat(q.OnFail).Concat(q.OnAbandon).Concat(q.Stages.SelectMany(s => s.OnComplete)));

    /// <summary>内容里出现的全部效果（含战斗胜败效果的嵌套）。</summary>
    public static IEnumerable<WorldEffect> AllEffects(WorldBundle w) =>
        Expand((w.NewGame?.Effects ?? [])
            .Concat(w.Maps.SelectMany(m => m.Interactables.SelectMany(i => i.Effects)))
            .Concat(w.Quests.SelectMany(QuestEffects))
            .Concat(w.Dialogues.SelectMany(d => d.Nodes.SelectMany(n => n.Effects.Concat(n.Options.SelectMany(o => o.Effects))))));

    private static IEnumerable<WorldEffect> Expand(IEnumerable<WorldEffect> effects)
    {
        foreach (var e in effects)
        {
            yield return e;
            foreach (var x in Expand(e.OnVictory.Concat(e.OnDefeat)))
            {
                yield return x;
            }
        }
    }
}
