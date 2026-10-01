using Godot;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Game.Adapters;

namespace WuxiaWorld.Game.Preview.Samples;

public sealed record SampleChoice(string LineId, string Text);

public sealed record SampleLine(string LineId, string Speaker, string Text, List<SampleChoice> Choices);

/// <summary>
/// 对话展示页的台词：从世界内容包读取一段正式对白图（唯一可编辑来源是 content/dialogue 下的章节文件，未锁稿），
/// 沿每个选项组的第一个选项展开成一条线性台词序列。只供展示页使用；可玩对话由 M2 的 <c>GameSession</c> 驱动。
/// </summary>
public static class DialogueSamples
{
    /// <summary>开场“芦湾醒来”，沿用 M0 展示样例的 line_id。</summary>
    public const string Opening = "dlg.ch01.opening_luwan";

    private const int MaxNodes = 64;

    public static IReadOnlyList<SampleLine> Load(string dialogueId)
    {
        var lines = new List<SampleLine>();
        var world = GeneratedContent.World;
        var def = world?.Dialogues.FirstOrDefault(d => d.Id == dialogueId);
        if (world is null || def is null)
        {
            GD.PushError($"对白 {dialogueId} 无法读取：{GeneratedContent.Error ?? "内容包里没有这段对白"}");
            return lines;
        }

        var node = def.Node(def.Entry);
        for (var step = 0; node is not null && step < MaxNodes; step++)
        {
            string? next;
            switch (node.Type)
            {
                case DialogueNodeType.Line:
                    lines.Add(new SampleLine(node.LineId!, world.Name(node.Speaker!), node.Text!, []));
                    next = node.Next;
                    break;
                case DialogueNodeType.Choice when lines.Count > 0:
                    lines[^1].Choices.AddRange(node.Options.Select(o => new SampleChoice(o.LineId, o.Text)));
                    next = node.Options[0].Next;
                    break;
                case DialogueNodeType.End:
                    next = null;
                    break;
                default:
                    next = node.Next;
                    break;
            }

            node = def.Node(next);
        }

        return lines;
    }
}
