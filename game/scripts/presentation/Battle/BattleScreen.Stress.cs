using Godot;
using WuxiaWorld.Domain.Combat;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Game.Presentation.App;

namespace WuxiaWorld.Game.Presentation.Battle;

/// <summary>
/// 4 对 6 战斗压测（<c>--battle-stress</c>，开发用，开发计划 M3-07“4 对 6 战斗达到玩法 Demo 性能目标”）：第一章最大的一场是 4 对 4（另有 1 名增援），
/// 这里在内存中临时加一场 6 名敌人站满两排三槽的遭遇（唐守亭领押运打手、钩手与护院，均为第一章已有的模板与形象），
/// 己方为主角、陆青禾、令狐冲、萧峰 4 人；自动战斗按 1 倍速完整播放出手、受击、飘字与横幅，打完停 1 秒结算页后换种子再开。
/// 这场遭遇只存在于本次运行，不进内容包、不进存档。
/// </summary>
public sealed partial class BattleScreen
{
    private const string StressEncounterId = "dev.battle.stress_4v6";

    private static readonly EncounterDefinition StressEncounter = new()
    {
        Id = StressEncounterId,
        Enemies =
        [
            new EncounterSlot { Template = "combatant.enemy.escort", UnitId = "enemy.escort_a", Row = 0, Slot = 0 },
            new EncounterSlot { Template = "combatant.enemy.tang_shouting", UnitId = "enemy.tang_shouting", Row = 0, Slot = 1 },
            new EncounterSlot { Template = "combatant.enemy.escort", UnitId = "enemy.escort_b", Row = 0, Slot = 2 },
            new EncounterSlot { Template = "combatant.enemy.hookman", UnitId = "enemy.hook_a", Row = 1, Slot = 0 },
            new EncounterSlot { Template = "combatant.enemy.yard_guard", UnitId = "enemy.yard_guard", Row = 1, Slot = 1 },
            new EncounterSlot { Template = "combatant.enemy.hookman", UnitId = "enemy.hook_b", Row = 1, Slot = 2 },
        ],
    };

    private double _stressRestart;

    private static CombatContent WithStress(CombatContent c) =>
        new(c.Skills.Values, c.Statuses.Values, c.Arts.Values, c.Items.Values, c.Combatants.Values, c.Encounters.Values.Append(StressEncounter), c.Counters);

    private void StartStress(ulong seed)
    {
        var content = _engine.Content;
        var allies = new List<AllyEntry>
        {
            new(content.Combatant("combatant.hero.sword"), "char.hero", new Position(0, 1)),
            new(content.Combatant("combatant.lu_qinghe"), "char.lu_qinghe", new Position(1, 1)),
            new(content.Combatant("combatant.linghu_chong"), "char.linghu_chong", new Position(0, 2)),
            new(content.Combatant("combatant.xiao_feng"), "char.xiao_feng", new Position(0, 0)),
        };
        _auto = true;
        _speed = 1;
        Begin(new BattleSetup { EncounterId = StressEncounterId, Seed = seed, Allies = allies, Items = StartingItems }, $"4 对 6 压测 · 种子 {seed}");
        _sceneTitle.Text = "4 对 6 压测　·　自动战斗";
        PerfProbe.Activity = "4 对 6";
    }

    /// <summary>压测：结算页停 1 秒后换种子再开一场。</summary>
    private void StepStress(double delta)
    {
        if (!DevCapture.BattleStress || !_resultOpen)
        {
            return;
        }

        _stressRestart += delta;
        if (_stressRestart >= 1)
        {
            _stressRestart = 0;
            StartStress(_seed + 1);
        }
    }
}
