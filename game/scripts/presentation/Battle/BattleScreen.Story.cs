using Godot;
using WuxiaWorld.Application.Combat;
using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.Combat;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Play;

namespace WuxiaWorld.Game.Presentation.Battle;

/// <summary>
/// 剧情战（M2，架构文档 3 的 Exploration → Battle → Result → Exploration）：世界状态里有待开战斗时，本页不显示配置，
/// 按请求的遭遇与当前队伍开打；结算页确认后经 <see cref="GameSession.SettleBattle"/> 以 <c>battle_instance_id</c> 一次性提交，
/// 再回探索页。战败可再战（新实例）或暂退（执行战败收束、回本图安全入口）。
/// 主角由世界状态现推战斗模板（等级、潜能、装备、装配与熟练度，见 <see cref="Domain.World.GrowthRules.Template"/>）；
/// 经典人物援手在人物锚点核验（M2-10）前用占位同行者模板，显示真名。
/// 战斗用药读行囊里的数量，用掉的随结算一并从行囊扣除（无论胜负，重复结算不重复扣）。
/// </summary>
public sealed partial class BattleScreen
{
    /// <summary>陆青禾之外的同行者依次站：前排右、前排左、后排右。</summary>
    private static readonly Position[] CompanionSlots = [new(0, 2), new(0, 0), new(1, 2)];

    private PlaySession? _story;
    private PendingBattle? _pending;

    private void StartStory(PlaySession play)
    {
        _story = play;
        var w = play.Game.World;
        _pending = w.Battle!;
        var content = _engine.Content;

        // 讨教所学的流派只用于开发说明；主角的数值与招式一律来自世界状态里的成长构成（M2-05）。
        _build = w.Skills.Any(s => s.StartsWith("skill.fist.", StringComparison.Ordinal)) ? 1
            : w.Skills.Any(s => s.StartsWith("skill.inner.", StringComparison.Ordinal)) ? 2 : 0;
        var growth = play.Game.Growth!;
        var allies = new List<AllyEntry> { new(growth.Template(w, growth.Hero), growth.Hero, new Position(0, 1)) };
        var slot = 0;
        foreach (var id in w.Party.Skip(1).Take(BattleSetup.MaxAllies - 1))
        {
            var template = play.Game.Rules.Content.Characters.TryGetValue(id, out var c) && c.Combatant is { } t ? t : "combatant.placeholder.companion";
            var at = id == "char.lu_qinghe" ? new Position(1, 1) : CompanionSlots[slot++ % CompanionSlots.Length];
            allies.Add(new(content.Combatant(template), id, at));
        }

        var items = w.Items.Where(i => content.Items.ContainsKey(i.Key) && i.Value > 0).ToDictionary(i => i.Key, i => i.Value);
        var setup = new BattleSetup { EncounterId = _pending.Encounter, Seed = StorySeed(_pending.InstanceId, w.RngState), Allies = allies, Items = items };
        Begin(setup, AppHost.DevInfo
            ? $"{_bundle.Name(setup.EncounterId)}：主角 {growth.Level(w)} 级（{Builds[_build].Label}）· 第 {_pending.Attempt} 次 · 种子 {setup.Seed}"
            : _pending.Attempt > 1 ? $"再战{_bundle.Name(setup.EncounterId)}" : $"{_bundle.Name(setup.EncounterId)}，开战");
        if (DevCapture.Holding("battle"))
        {
            // 停在第 2 轮轮到我方时截图。
            FastForward(stopWhen: s => s.State.Round >= 2 && s.AwaitingPlayer is not null);
            DevCapture.FinishAutoplay(GetTree(), 0);
        }
        else if (DevCapture.Autoplay > 0 && DevCapture.LoseFirst)
        {
            // 核对战败流程：不打，直接按战败暂退结算一次。
            DevCapture.LoseFirst = false;
            GD.Print($"[autoplay] 战斗 {_pending.InstanceId}：按战败暂退");
            CallDeferred(MethodName.GiveUpForAutoplay);
        }
        else if (DevCapture.Autoplay > 0)
        {
            FastForward(stopWhen: s => s.Ended);
        }
    }

    private void GiveUpForAutoplay() => SettleStory(BattleEnd.Defeat, giveUp: true);

    /// <summary>自动走查：结算页出现后，胜利确认、战败暂退（回探索页后由“迎战”事件重开）。</summary>
    private void AutoplayResult()
    {
        if (DevCapture.Autoplay <= 0 || _story is null || !_resultOpen)
        {
            return;
        }

        var victory = _session!.State.Outcome == BattleOutcome.Victory;
        GD.Print($"[autoplay] 战斗 {_pending!.InstanceId}：{_session.State.Outcome}，第 {_session.State.Round} 轮");
        StoryResultKey(victory ? Key.Enter : Key.B);
    }

    /// <summary>战斗种子由战斗实例与世界随机流确定：同一存档重开同一场得到同样的随机序列。</summary>
    private static ulong StorySeed(string instanceId, ulong worldState)
    {
        var h = 14695981039346656037UL;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(instanceId))
        {
            h = (h ^ b) * 1099511628211UL;
        }

        return h ^ worldState;
    }

    /// <summary>结算页上的确认：胜利继续；战败或撤退再战 / 暂退。</summary>
    private bool StoryResultKey(Key key)
    {
        var victory = _session!.State.Outcome == BattleOutcome.Victory;
        switch (key)
        {
            case Key.Enter or Key.KpEnter or Key.Space when victory:
                SettleStory(BattleEnd.Victory, giveUp: false);
                return true;
            case Key.R when !victory:
                SettleStory(Ended(), giveUp: false);
                return true;
            case Key.B or Key.Enter or Key.KpEnter when !victory:
                SettleStory(Ended(), giveUp: true);
                return true;
            default:
                return false;
        }
    }

    /// <summary>本场用掉的行囊物品：开战带入的数量减去战场上剩下的。</summary>
    private Dictionary<string, int> Consumed()
    {
        var session = _session!;
        var left = session.State.Items;
        return session.Record.Setup.Items
            .Select(i => (i.Key, Used: i.Value - left.GetValueOrDefault(i.Key)))
            .Where(i => i.Used > 0)
            .ToDictionary(i => i.Key, i => i.Used);
    }

    private BattleEnd Ended() => _session!.State.Outcome == BattleOutcome.Defeat ? BattleEnd.Defeat : BattleEnd.Retreated;

    /// <summary>
    /// 提交战果。胜利发奖（经验取遭遇定义）；战败若可重试则实例号递增后重开或暂退，不可重试则执行战败效果。
    /// 提交失败（例如已结算过）只提示，不重复发奖。
    /// </summary>
    private void SettleStory(BattleEnd end, bool giveUp)
    {
        var play = _story!;
        var game = play.Game;
        var pending = _pending!;
        var encounter = _engine.Content.Encounter(pending.Encounter);
        var victory = end == BattleEnd.Victory;
        var consumed = Consumed();
        var r = game.SettleBattle(pending.InstanceId, end, victory ? encounter.Experience : 0, consumed, victory ? encounter.Cultivation : 0);
        var notes = new List<(string, string)>();
        if (!r.Ok)
        {
            notes.Add(("提示", $"战果未提交：{r.Error}"));
        }
        else
        {
            // 战斗里用掉的药写成“用去”，不当作交出物品。
            notes.AddRange(r.Notices.Where(n => n.Kind != "item_lost" || !consumed.ContainsKey(n.Id)).Select(play.Describe).OfType<(string, string)>());
            notes.AddRange(consumed.Select(c => ("物品", $"用去 {play.Name(c.Key)}{(c.Value > 1 ? $" ×{c.Value}" : "")}")));
        }

        if (end != BattleEnd.Victory && game.World.Battle is not null)
        {
            if (!giveUp)
            {
                StartStory(play);
                return;
            }

            var back = game.GiveUpBattle();
            notes.AddRange(back.Notices.Select(play.Describe).OfType<(string, string)>());
            notes.Add(("提示", "暂且退下，回到此地的安全处"));
        }

        if (r.Travel is { } travel && game.World.Battle is null)
        {
            play.Arriving = game.BeginStoryTravel(travel);
        }

        play.PendingToasts.AddRange(notes);
        if (game.World.Battle is null && play.Arriving is null && play.AutoSave() is { Ok: false } save)
        {
            play.PendingToasts.Add(("存档", $"自动存档失败：{save.Error}"));
        }

        _story = null;
        AppHost.Instance.Router.GoTo(ScenePaths.Exploration);
    }
}
