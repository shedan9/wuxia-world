using System.Globalization;
using System.Text;
using WuxiaWorld.Application.Combat;
using WuxiaWorld.Domain.Combat;
using WuxiaWorld.Domain.Combat.Ai;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Domain.Common;
using WuxiaWorld.Infrastructure.Content;
using WuxiaWorld.Tools.BattleSimulator;

// 用法：dotnet run --project tools/BattleSimulator -- [--runs 200] [--seed 1] [--scenario <id>] [--csv build/sim/result.csv]
// 每个场景 × 主角流派 × 策略跑 runs 个连续种子；陆青禾固定用贪心评分出招。输出胜率、平均轮数、剩余气血、内力消耗与用药。
var runs = 200;
ulong seed0 = 1;
string? only = null;
string? csvPath = null;
string? root = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--runs": runs = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--seed": seed0 = ulong.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--scenario": only = args[++i]; break;
        case "--csv": csvPath = args[++i]; break;
        case "--content": root = args[++i]; break;
        default: throw new ArgumentException($"未知参数 {args[i]}");
    }
}

root ??= FindRepoRoot();
var bundle = CombatContentLoader.LoadDirectory(Path.Combine(root, "content"));
var errors = CombatContentValidator.Validate(bundle);
if (errors.Count > 0)
{
    Console.Error.WriteLine("内容校验失败：\n" + string.Join("\n", errors));
    return 1;
}

var content = new CombatContent(bundle.Skills.Concat(Scenarios.Skills), bundle.Statuses.Concat(Scenarios.Statuses), bundle.Arts, bundle.Items,
    bundle.Combatants.Concat(Scenarios.Combatants), bundle.Encounters.Concat(Scenarios.Encounters), bundle.Counters);
var engine = new BattleEngine(content);
var builds = new[] { "sword", "fist", "inner" };
var csv = new StringBuilder("scenario,build,policy,runs,win_rate,avg_rounds,p90_rounds,avg_party_hp_pct,avg_inner_spent,avg_items,timeouts\n");

Console.WriteLine($"内容版本 {bundle.ContentVersion} · 规则版本 {bundle.RulesetVersion} · 每组 {runs} 局，种子 {seed0}–{seed0 + (ulong)runs - 1}");
foreach (var scenario in Scenarios.All.Where(s => only is null || s.Id == only))
{
    Console.WriteLine();
    Console.WriteLine($"■ {scenario.Title}（{scenario.Id}）{(scenario.SuitedBuild == "-" ? "" : $"· 针对流派：{scenario.SuitedBuild}")}");
    Console.WriteLine("  流派    策略     胜率    平均轮  P90轮  剩余气血  内力消耗  用药  超时");
    foreach (var build in builds)
    {
        foreach (var policyName in new[] { build, "greedy", "basic" })
        {
            var policy = Policies.ByName(policyName);
            var stats = Run(engine, content, scenario, build, policy, runs, seed0);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {build,-7} {policyName,-7} {stats.WinRate,6:P0}  {stats.AvgRounds,6:F1}  {stats.P90Rounds,5}  {stats.AvgHpPct,7:P0}  {stats.AvgInner,8:F0}  {stats.AvgItems,4:F1}  {stats.Timeouts,4}"));
            csv.Append(CultureInfo.InvariantCulture,
                $"{scenario.Id},{build},{policyName},{runs},{stats.WinRate:F3},{stats.AvgRounds:F2},{stats.P90Rounds},{stats.AvgHpPct:F3},{stats.AvgInner:F1},{stats.AvgItems:F2},{stats.Timeouts}\n");
        }
    }
}

if (csvPath is not null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(csvPath))!);
    File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(false));
    Console.WriteLine($"\n已写出 {csvPath}");
}

return 0;

static Stats Run(BattleEngine engine, CombatContent content, Scenarios.Scenario scenario, string build, IPolicy policy, int runs, ulong seed0)
{
    var greedy = Policies.ByName("greedy");
    int wins = 0, timeouts = 0;
    var rounds = new List<int>();
    double hpPct = 0, inner = 0, items = 0;
    for (var r = 0; r < runs; r++)
    {
        var setup = new BattleSetup
        {
            EncounterId = scenario.Id,
            Seed = seed0 + (ulong)r,
            Allies =
            [
                new AllyEntry(content.Combatant($"combatant.hero.{build}"), "char.hero", new Position(0, 1)),
                new AllyEntry(content.Combatant("combatant.lu_qinghe"), "char.lu_qinghe", new Position(1, 1)),
                .. scenario.Guest ? [new AllyEntry(content.Combatant("combatant.placeholder.companion"), "char.guest", new Position(0, 2))] : Array.Empty<AllyEntry>(),
            ],
            Items = new Dictionary<string, int> { [Policies.GoldenSore] = 2, [Policies.QiPill] = 1 },
        };
        var session = new BattleSession(engine, setup);
        var spent = 0;
        var used = 0;
        for (var step = 0; step < 600 && !session.Ended; step++)
        {
            CommandResult result;
            if (session.AwaitingPlayer is { } unit)
            {
                var command = (unit.Id == "char.hero" ? policy : greedy).Decide(engine, session.State, unit);
                result = session.Submit(command);
                if (!result.Accepted)
                {
                    result = session.Submit(new Defend(unit.Id));
                }

                used += command is UseItem && result.Accepted ? 1 : 0;
            }
            else
            {
                result = session.StepAi()!;
            }

            spent += result.Events.OfType<InnerChanged>().Where(c => c.Delta < 0 && c.Unit.StartsWith("char.", StringComparison.Ordinal)).Sum(c => -c.Delta);
        }

        if (!session.Ended)
        {
            timeouts++;
        }

        if (session.State.Outcome == BattleOutcome.Victory)
        {
            wins++;
        }

        rounds.Add(session.State.Round);
        var party = session.State.Units.Where(u => u.Side == Side.Ally).ToList();
        hpPct += party.Sum(u => u.Hp) / (double)party.Sum(u => u.Stats.MaxHp);
        inner += spent;
        items += used;
    }

    rounds.Sort();
    return new Stats(wins / (double)runs, rounds.Average(), rounds[(int)(rounds.Count * 0.9)], hpPct / runs, inner / runs, items / runs, timeouts);
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WuxiaWorld.sln")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName ?? Directory.GetCurrentDirectory();
}

internal sealed record Stats(double WinRate, double AvgRounds, int P90Rounds, double AvgHpPct, double AvgInner, double AvgItems, int Timeouts);
