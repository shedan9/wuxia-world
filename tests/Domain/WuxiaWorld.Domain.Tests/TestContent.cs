using WuxiaWorld.Application.Combat;
using WuxiaWorld.Domain.Characters;
using WuxiaWorld.Domain.Combat;
using WuxiaWorld.Domain.Combat.Ai;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Infrastructure.Content;

namespace WuxiaWorld.Domain.Tests;

/// <summary>读取仓库里的正式战斗内容，并可叠加仅供测试的单位与遭遇。</summary>
internal static class TestContent
{
    private static readonly Lazy<CombatBundle> Lazy = new(() => CombatContentLoader.LoadDirectory(Path.Combine(RepoRoot(), "content")));

    public static CombatBundle Bundle => Lazy.Value;

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WuxiaWorld.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("找不到仓库根目录（WuxiaWorld.sln）。");
    }

    public static CombatContent With(IEnumerable<CombatantTemplate>? combatants = null, IEnumerable<EncounterDefinition>? encounters = null,
        IEnumerable<SkillDefinition>? skills = null)
    {
        var b = Bundle;
        return new CombatContent(
            b.Skills.Concat(skills ?? []), b.Statuses, b.Arts, b.Items,
            b.Combatants.Concat(combatants ?? []), b.Encounters.Concat(encounters ?? []), b.Counters);
    }

    public static CombatContent Real => With();

    /// <summary>测试用简单单位：给定属性、招式与 AI；可覆盖气血与速度。</summary>
    public static CombatantTemplate Unit(string id, string ai = AiProfiles.Player, int speed = 0, int hp = 0, string[]? skills = null,
        string[]? tags = null, int controlThreshold = 0, int stance = -1, string[]? talents = null, int level = 3)
    {
        return new CombatantTemplate
        {
            Id = id, Level = level, Ai = ai, Tags = tags ?? [], ControlThreshold = controlThreshold,
            Attributes = new Attributes(5, 5, 5, 5, 5),
            Loadout = new Loadout { Skills = skills ?? [], Talents = talents ?? [] },
            Overrides = new StatOverride
            {
                Speed = speed > 0 ? speed : null, MaxHp = hp > 0 ? hp : null, MaxStance = stance >= 0 ? stance : null,
            },
        };
    }

    public static EncounterDefinition Encounter(string id, params (string Template, string UnitId, int Row, int Slot)[] enemies) => new()
    {
        Id = id,
        Enemies = [.. enemies.Select(e => new EncounterSlot { Template = e.Template, UnitId = e.UnitId, Row = e.Row, Slot = e.Slot })],
    };

    public static AllyEntry Ally(CombatantTemplate t, string id, int row, int slot) => new(t, id, new Position(row, slot));

    /// <summary>自动打完：玩家单位按贪心评分出招（与敌方莽攻同一评分），其余交给 AI。返回回合数上限内的会话。</summary>
    public static BattleSession Autoplay(BattleEngine engine, BattleSetup setup, int maxCommands = 400)
    {
        var session = new BattleSession(engine, setup);
        for (var i = 0; i < maxCommands && !session.Ended; i++)
        {
            if (session.AwaitingPlayer is { } unit)
            {
                var result = session.Submit(BattleAi.BestAttack(engine, session.State, unit, skirmish: false));
                Assert.True(result.Accepted, result.Rejection);
            }
            else
            {
                session.StepAi();
            }
        }

        return session;
    }
}
