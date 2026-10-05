using Godot;
using WuxiaWorld.Application.Dev;
using WuxiaWorld.Domain.Combat;
using Side = WuxiaWorld.Domain.Combat.Side;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Play;

namespace WuxiaWorld.Game.Presentation.Battle;

/// <summary>
/// 战斗测试台（开发用）：战斗原型页的“剧情路线”组成。开新游戏后在规则层沿 <see cref="ChapterOneRoute"/> 瞬间走到押运队战或旧渡首领战之前
/// （对话直接走完、前一场按胜利结算、主角潜能按推荐分配），再用 <see cref="Application.World.GameSession.StoryBattleSetup"/> 取开战输入——
/// 同行者、援手、阵位、等级、装备、武学、行囊药品与先手变体都与正式流程一致。可改主角等级与变体；结算不写回任何存档，结算页 R / N 按同一组成重开。
/// 命令行：<c>--battle=escort|sluice|spar</c>，配 <c>--companion</c>、<c>--mentor</c>、<c>--side</c>、<c>--level</c>、<c>--variants</c>、<c>--seed</c>、<c>--battle-auto</c>（见 <see cref="DevCapture"/>）。
/// </summary>
public sealed partial class BattleScreen
{
    private static readonly (string Id, string Name)[] BenchCompanions = [("linghu", "令狐冲"), ("huang", "黄蓉"), ("xiao", "萧峰")];

    /// <summary>组成：false 为原型（三套流派同等预算），true 为剧情路线。</summary>
    private bool _benchStory;

    private int _benchCompanion;
    private bool _benchSide;

    /// <summary>主角等级覆盖；0 为按剧情走到该处的等级。</summary>
    private int _benchLevel;

    private IReadOnlyList<string>? _benchVariants;
    private bool _benchReported;

    /// <summary>主角流派对应的讨教人选：剑术令狐冲、拳掌萧峰、内功黄蓉。</summary>
    private static string MentorOf(int build) => Builds[build].Id switch
    {
        "sword" => "linghu",
        "fist" => "xiao",
        _ => "huang",
    };

    private static int BuildOf(string mentor)
    {
        var id = mentor switch
        {
            "linghu" => "sword",
            "xiao" => "fist",
            _ => "inner",
        };
        return Math.Max(0, Array.FindIndex(Builds, b => b.Id == id));
    }

    /// <summary>命令行 <c>--battle</c>：按参数定好组成，直接开打。</summary>
    private void StartBenchFromArgs()
    {
        _benchStory = true;
        _encounter = DevCapture.BenchBattle switch
        {
            "sluice" => 1,
            "spar" => 2,
            _ => 0,
        };
        var companion = DevCapture.Companion ?? "linghu";
        _benchCompanion = Math.Max(0, Array.FindIndex(BenchCompanions, c => c.Id == companion));
        _build = BuildOf(DevCapture.Mentor ?? companion);
        _benchSide = DevCapture.Side;
        _benchLevel = DevCapture.BenchLevel ?? 0;
        _benchVariants = DevCapture.BenchVariants;
        StartBattle(_encounter, _build, DevCapture.BenchSeed ?? 20261004);
        if (DevCapture.BattleAuto)
        {
            _speed = 1;
            _auto = true;
        }
    }

    /// <summary>按剧情路线造出开战输入；走不通时返回 null 并给出原因。</summary>
    private BattleSetup? StoryBenchSetup(int encounter, int build, ulong seed, out string note)
    {
        var play = PlaySession.NewGame(out var error);
        if (play is null)
        {
            note = $"无法开新游戏：{error}";
            return null;
        }

        var companion = BenchCompanions[_benchCompanion];
        var spar = EncounterIds[encounter] == SparSlot;
        var route = new ChapterOneRoute(play.Game)
        {
            Companion = companion.Id, Mentor = MentorOf(build), Side = _benchSide, AllocatePotential = true, Spar = spar ? "won" : null,
        };
        var sluice = EncounterIds[encounter].EndsWith("sluice", StringComparison.Ordinal);
        try
        {
            // 先停在开战的那段对话之前改等级、分潜能（战斗待开时不能调整养成），再走进战斗。
            // 切磋停在讨教之前：讨教后路线会先按推荐分潜能再去找人切磋。
            route.RunTo(spar ? ChapterOnePoint.Mentor : sluice ? ChapterOnePoint.Sluice : ChapterOnePoint.OldFerry);
            if (_benchLevel > 0)
            {
                route.SetHeroLevel(_benchLevel);
            }

            route.AllocateRecommended();
            route.RunTo(spar ? ChapterOnePoint.SparBattle : sluice ? ChapterOnePoint.SluiceBattle : ChapterOnePoint.EscortBattle);
        }
        catch (InvalidOperationException ex)
        {
            note = ex.Message;
            return null;
        }

        var setup = play.Game.StoryBattleSetup(seed);
        if (_benchVariants is { } variants)
        {
            setup = setup with { Variants = variants };
        }

        var level = play.Game.Growth!.Level(play.Game.World);
        note = $"{_bundle.Name(setup.EncounterId)}（测试台）：{companion.Name}同行{(_benchSide ? "、做支线" : "")} · 主角 {level} 级（{Builds[build].Label}）"
            + $" · 变体 {(setup.Variants.Count > 0 ? string.Join("、", setup.Variants.Select(v => _bundle.Name(v))) : "无")} · 种子 {seed}";
        return setup;
    }

    /// <summary>主角等级覆盖在“按剧情”（0）与 1–8 级之间循环调整。</summary>
    private void StepBenchLevel(int delta)
    {
        _benchLevel = Math.Clamp(_benchLevel + delta, 0, 8);
        ShowSetup();
    }

    /// <summary>测试台自动打完（<c>--battle-auto</c>）：结算页出现后打印战果并退出，胜 0、败 4。</summary>
    private void ReportBench()
    {
        if (!DevCapture.BattleAuto || !_benchStory || !_resultOpen || _benchReported)
        {
            return;
        }

        _benchReported = true;
        var state = _session!.State;
        var allies = string.Join("、", state.Units.Where(u => u.Side == Side.Ally)
            .Select(u => $"{DisplayName(u.Id)} {Math.Max(0, u.Hp)}/{u.Stats.MaxHp}"));
        GD.Print($"[bench] {state.EncounterId}：{state.Outcome}，第 {state.Round} 轮；我方 {allies}");
        GetTree().Quit(state.Outcome == BattleOutcome.Victory ? 0 : 4);
    }
}
