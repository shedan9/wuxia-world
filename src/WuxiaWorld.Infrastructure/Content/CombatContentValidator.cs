using System.Text.RegularExpressions;
using WuxiaWorld.Domain.Characters;
using WuxiaWorld.Domain.Combat;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Domain.Common;

namespace WuxiaWorld.Infrastructure.Content;

/// <summary>
/// 战斗内容的语义校验（架构文档 9.3）：ID 格式与重复、跨表引用、内核必需定义、数值范围、阵位、阶段引用与文本缺失。
/// 静态检查只证明数据自洽，不证明数值平衡；平衡由战斗模拟器与试玩检验。
/// </summary>
public static partial class CombatContentValidator
{
    [GeneratedRegex("^[a-z0-9_]+(\\.[a-z0-9_]+)+$")]
    private static partial Regex IdPattern();

    public static IReadOnlyList<string> Validate(CombatBundle b)
    {
        var errors = new List<string>();
        void Err(string message) => errors.Add(message);

        var all = b.Skills.Select(s => s.Id).Concat(b.Statuses.Select(s => s.Id)).Concat(b.Arts.Select(a => a.Id))
            .Concat(b.Items.Select(i => i.Id)).Concat(b.Combatants.Select(c => c.Id)).Concat(b.Encounters.Select(e => e.Id));
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

        var skills = b.Skills.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var statuses = b.Statuses.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var arts = b.Arts.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var combatants = b.Combatants.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var id in CoreIds.RequiredStatuses.Where(id => !statuses.ContainsKey(id)))
        {
            Err($"缺少内核必需的状态 {id}");
        }

        if (!skills.ContainsKey(CoreIds.BasicAttack))
        {
            Err($"缺少内核必需的招式 {CoreIds.BasicAttack}");
        }

        foreach (var s in b.Skills)
        {
            if (s.InnerCost < 0 || s.MomentumCost < 0 || s.MomentumCost > CombatConstants.MaxMomentum || s.Cooldown < 0)
            {
                Err($"{s.Id}：消耗或冷却越界");
            }

            if (s.Effects.Count == 0)
            {
                Err($"{s.Id}：没有效果");
            }

            CheckEffects(s.Id, s.Effects);
            RequireText(s.Id + ".name");
        }

        foreach (var st in b.Statuses)
        {
            if (st.MaxStacks < 1 || st.Duration < 0)
            {
                Err($"{st.Id}：叠层或持续次数越界");
            }

            if (st.GrantOnEnd is not null && !statuses.ContainsKey(st.GrantOnEnd))
            {
                Err($"{st.Id}：grant_on_end 引用了不存在的状态 {st.GrantOnEnd}");
            }

            RequireText(st.Id + ".name");
        }

        foreach (var a in b.Arts)
        {
            RequireText(a.Id + ".name");
        }

        foreach (var i in b.Items)
        {
            CheckEffects(i.Id, i.Effects);
            RequireText(i.Id + ".name");
        }

        foreach (var c in b.Counters)
        {
            if (c.Bp is < CombatConstants.CounterMinBp or > CombatConstants.CounterMaxBp)
            {
                Err($"克制倍率 {c.SkillTag}→{c.TargetTag} 超出 0.8–1.25");
            }
        }

        foreach (var c in b.Combatants)
        {
            if (c.Level is < 1 or > StatFormula.MaxLevel + 4)
            {
                Err($"{c.Id}：等级越界");
            }

            foreach (var skill in c.Loadout.Skills.Where(sk => !skills.ContainsKey(sk)))
            {
                Err($"{c.Id}：装配了不存在的招式 {skill}");
            }

            if (c.Loadout.Skills.Count > Loadout.MaxSkills || c.Loadout.Talents.Count > Loadout.MaxTalents)
            {
                Err($"{c.Id}：装配超出上限");
            }

            CheckArt(c.Id, c.Loadout.MainArt, ArtKind.Inner);
            CheckArt(c.Id, c.Loadout.SupportArt, ArtKind.Inner);
            CheckArt(c.Id, c.Loadout.Qinggong, ArtKind.Qinggong);
            foreach (var t in c.Loadout.Talents)
            {
                CheckArt(c.Id, t, ArtKind.Talent);
            }

            if (c.Ai is not (AiProfiles.Player or AiProfiles.Brawler or AiProfiles.Skirmisher or AiProfiles.Guardian
                or AiProfiles.BossTang or AiProfiles.Mechanism or AiProfiles.Dummy or AiProfiles.Sparring))
            {
                Err($"{c.Id}：未知 AI 档案 {c.Ai}");
            }

            RequireText(c.Id + ".name");
        }

        foreach (var e in b.Encounters)
        {
            CheckEncounter(e);
            RequireText(e.Id + ".name");
        }

        return errors;

        void CheckEffects(string owner, IReadOnlyList<EffectDefinition> effects)
        {
            foreach (var e in effects)
            {
                if (e.ChanceBp is < 0 or > 10_000)
                {
                    Err($"{owner}：几率越界");
                }

                var needsStatus = e.Type is EffectType.ApplyStatus or EffectType.RaiseLevel;
                if (needsStatus && (e.Status is null || !statuses.ContainsKey(e.Status)))
                {
                    Err($"{owner}：引用了不存在的状态 {e.Status}");
                }

                if (e.BonusVsStatus is not null && !statuses.ContainsKey(e.BonusVsStatus))
                {
                    Err($"{owner}：bonus_vs_status 引用了不存在的状态 {e.BonusVsStatus}");
                }

                if (e.ScaleByStatus is not null && !statuses.ContainsKey(e.ScaleByStatus))
                {
                    Err($"{owner}：scale_by_status 引用了不存在的状态 {e.ScaleByStatus}");
                }

                if (e.Type == EffectType.Cleanse && string.IsNullOrEmpty(e.Tag))
                {
                    Err($"{owner}：驱散效果缺少 tag");
                }

                if (e.Type == EffectType.Damage && e.ScaleBp <= 0 && e.Fixed <= 0)
                {
                    Err($"{owner}：伤害效果倍率与固定值都为 0");
                }
            }
        }

        void CheckArt(string owner, string? id, ArtKind kind)
        {
            if (id is null)
            {
                return;
            }

            if (!arts.TryGetValue(id, out var art))
            {
                Err($"{owner}：引用了不存在的心法 / 轻功 / 天赋 {id}");
            }
            else if (art.Kind != kind)
            {
                Err($"{owner}：{id} 类型不符（应为 {kind}）");
            }
        }

        void CheckEncounter(EncounterDefinition e)
        {
            var units = new HashSet<string>(StringComparer.Ordinal);
            var cells = new HashSet<Position>();
            if (e.Enemies.Count is 0 or > BattleSetup.MaxEnemies)
            {
                Err($"{e.Id}：敌方须为 1–{BattleSetup.MaxEnemies} 人");
            }

            foreach (var slot in e.Enemies)
            {
                CheckSlot(slot, initial: true);
            }

            foreach (var phase in e.Phases)
            {
                foreach (var slot in phase.Spawn)
                {
                    CheckSlot(slot, initial: false);
                }
            }

            if (e.Victory == VictoryRule.DefeatUnit && (e.VictoryUnit is null || !units.Contains(e.VictoryUnit)))
            {
                Err($"{e.Id}：胜利条件指向不存在的单位 {e.VictoryUnit}");
            }

            if (e.Victory == VictoryRule.Spar)
            {
                if (e.YieldBp is <= 0 or >= Bp.One)
                {
                    Err($"{e.Id}：切磋的认输线须在 1–9999 万分比之间（现为 {e.YieldBp}）");
                }

                if (e.VictoryUnit is not null && !units.Contains(e.VictoryUnit))
                {
                    Err($"{e.Id}：切磋的认输对象指向不存在的单位 {e.VictoryUnit}");
                }
            }
            else if (e.YieldBp != 0)
            {
                Err($"{e.Id}：只有切磋（victory: spar）才设认输线");
            }

            if (e.Backdrop is { } backdrop && !backdrop.StartsWith("battle.", StringComparison.Ordinal))
            {
                Err($"{e.Id}：战斗布景 ID 应以 battle. 开头（{backdrop}）");
            }

            if (e.Victory is VictoryRule.DefeatAll or VictoryRule.Spar && !e.Enemies.Any(s => combatants.TryGetValue(s.Template, out var t) && t.CountsForVictory))
            {
                Err($"{e.Id}：没有计入胜利的敌人");
            }

            foreach (var phase in e.Phases)
            {
                if (phase.When != PhaseTriggerKind.RoundAtLeast && (phase.Unit is null || !units.Contains(phase.Unit)))
                {
                    Err($"{e.Id}/{phase.Id}：触发条件指向不存在的单位 {phase.Unit}");
                }

                foreach (var ps in phase.Statuses)
                {
                    if (!units.Contains(ps.Unit))
                    {
                        Err($"{e.Id}/{phase.Id}：状态目标不存在 {ps.Unit}");
                    }

                    if (!statuses.ContainsKey(ps.Status))
                    {
                        Err($"{e.Id}/{phase.Id}：状态不存在 {ps.Status}");
                    }
                }

                RequireText(phase.Id + ".name");
            }

            var initialUnits = e.Enemies.Select(s => s.UnitId ?? s.Template).ToHashSet(StringComparer.Ordinal);
            var variantIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var v in e.Variants)
            {
                if (!variantIds.Add(v.Id))
                {
                    Err($"{e.Id}：变体 ID 重复 {v.Id}");
                }

                if (string.IsNullOrEmpty(v.WhenFact))
                {
                    Err($"{e.Id}/{v.Id}：变体缺少 when_fact");
                }

                if (v.Statuses.Count == 0 && v.Hp.Count == 0)
                {
                    Err($"{e.Id}/{v.Id}：变体没有任何改动");
                }

                foreach (var ps in v.Statuses)
                {
                    if (!initialUnits.Contains(ps.Unit))
                    {
                        Err($"{e.Id}/{v.Id}：状态目标不在开局阵容里 {ps.Unit}");
                    }

                    if (ps.Remove || !statuses.ContainsKey(ps.Status))
                    {
                        Err($"{e.Id}/{v.Id}：状态不存在或写了 remove {ps.Status}");
                    }
                }

                foreach (var hp in v.Hp)
                {
                    if (!initialUnits.Contains(hp.Unit))
                    {
                        Err($"{e.Id}/{v.Id}：气血目标不在开局阵容里 {hp.Unit}");
                    }

                    if (hp.Bp is <= 0 or > 10_000)
                    {
                        Err($"{e.Id}/{v.Id}：气血比例须在 1–10000 之间");
                    }
                }

                RequireText(v.Id + ".name");
            }

            if (e.Locked)
            {
                RequireText(e.Id + ".locked");
            }

            void CheckSlot(EncounterSlot slot, bool initial)
            {
                var id = slot.UnitId ?? slot.Template;
                if (!combatants.ContainsKey(slot.Template))
                {
                    Err($"{e.Id}：引用了不存在的战斗单位 {slot.Template}");
                }

                if (!units.Add(id))
                {
                    Err($"{e.Id}：场上单位 ID 重复 {id}（同一模板多次出场须给 unit_id）");
                }

                var p = new Position(slot.Row, slot.Slot);
                if (!p.IsValid)
                {
                    Err($"{e.Id}：{id} 阵位越界");
                }
                else if (initial && !cells.Add(p))
                {
                    Err($"{e.Id}：{id} 与他人占同一阵位");
                }
            }
        }

        void RequireText(string key)
        {
            if (!b.Text.ContainsKey(key))
            {
                Err($"缺少文本 {key}");
            }
        }
    }
}
