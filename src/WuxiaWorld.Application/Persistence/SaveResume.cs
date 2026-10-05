using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.World;

namespace WuxiaWorld.Application.Persistence;

/// <summary>读档后恢复成可玩会话的结果：<see cref="Game"/> 为 null 时 <see cref="Error"/> 写明原因；<see cref="Notes"/> 是要告诉玩家的说明。</summary>
public sealed record SaveResumeResult(GameSession? Game, string? Error, IReadOnlyList<string> Notes);

/// <summary>
/// 把读出的存档恢复成可玩的 <see cref="GameSession"/>（架构文档 11）：先查与当前内容是否相容，不相容则拒绝并说明，
/// 不静默丢失物品或任务；相容时校正出生点、补齐旧档成长数据，并汇总备份、内容版本等说明。
/// 游戏读档与旧档回归测试共用这一段，测试所验即游戏所走。
/// </summary>
public static class SaveResume
{
    public static SaveResumeResult Resume(SaveReadResult read, WorldRules rules, GrowthRules growth, string contentVersion)
    {
        if (read.Game is not { } save)
        {
            return new SaveResumeResult(null, read.Error ?? "存档无法读取", read.Notes);
        }

        var problems = SaveCompatibility.Check(save.World, rules.Content);
        if (problems.Count > 0)
        {
            return new SaveResumeResult(null, "存档与当前内容不相容：" + string.Join("；", problems.Take(4)), read.Notes);
        }

        var game = new GameSession(rules, save.World, growth);
        var notes = new List<string>(read.Notes);
        if (read.FromBackup)
        {
            notes.Add("正式存档已损坏，已读取上一份备份");
        }

        if (save.Header.ContentVersion != contentVersion)
        {
            notes.Add("存档写于另一内容版本，已按当前内容继续");
        }

        if (game.RepairSpawn() is { } repaired)
        {
            notes.Add(repaired);
        }

        if (game.UpgradeLegacy() is { } upgraded)
        {
            notes.Add(upgraded);
        }

        return new SaveResumeResult(game, null, notes);
    }
}
