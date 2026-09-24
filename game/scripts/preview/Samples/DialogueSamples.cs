using System.Text.RegularExpressions;
using Godot;

namespace WuxiaWorld.Game.Preview.Samples;

public sealed record SampleChoice(string LineId, string Text);

public sealed record SampleLine(string LineId, string Speaker, string Text, List<SampleChoice> Choices);

/// <summary>
/// 从对白章节文件读取 M0 展示台词（唯一可编辑来源是 game/dialogue 下的 Markdown，
/// 格式写在文件开头）。只供对话展示页使用；正式对白由 M2 的对话图加载。
/// </summary>
public static partial class DialogueSamples
{
    public const string Chapter01 = "res://dialogue/arc01/chapter01.md";

    public static IReadOnlyList<SampleLine> Load(string path)
    {
        var lines = new List<SampleLine>();
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file is null)
        {
            GD.PushError($"对白文件无法读取：{path}");
            return lines;
        }

        foreach (var raw in file.GetAsText().Split('\n'))
        {
            var text = raw.TrimEnd('\r');
            if (ChoicePattern().Match(text) is { Success: true } choice && lines.Count > 0)
            {
                lines[^1].Choices.Add(new SampleChoice(choice.Groups[1].Value, choice.Groups[2].Value.Trim()));
            }
            else if (LinePattern().Match(text) is { Success: true } line)
            {
                lines.Add(new SampleLine(line.Groups[1].Value, line.Groups[2].Value, line.Groups[3].Value.Trim(), []));
            }
        }

        return lines;
    }

    [GeneratedRegex(@"^- `([^`]+)` \*\*([^*]+)\*\*：(.+)$")]
    private static partial Regex LinePattern();

    [GeneratedRegex(@"^- 〔选项〕 `([^`]+)` (.+)$")]
    private static partial Regex ChoicePattern();
}
