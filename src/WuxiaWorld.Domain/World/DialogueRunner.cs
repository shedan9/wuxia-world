namespace WuxiaWorld.Domain.World;

public sealed record DialogueChoiceView(int Index, DialogueOption Option, bool Enabled);

/// <summary>
/// 对话图执行（架构文档 9.1）：文本、选项、条件分流、效果、跳转、结束与演出提示七种节点。
/// 在调用方给的状态副本上运行；停在台词、演出提示或选项上等待输入，其余节点自动走过。
/// 效果节点与选项效果默认只结算一次（结算键 <c>dlg:对话/节点[/选项]</c>），重进对话不重复发奖。
/// </summary>
public sealed class DialogueRunner
{
    /// <summary>一次输入之间最多自动走过的节点数；超过说明内容有无条件循环。</summary>
    private const int MaxAutoSteps = 256;

    private readonly WorldRules _rules;
    private readonly List<DialogueChoiceView> _choices = [];

    public DialogueRunner(WorldRules rules, DialogueDefinition definition, WorldState state, EffectResult result)
    {
        _rules = rules;
        Definition = definition;
        State = state;
        Result = result;
        RunFrom(definition.Entry);
    }

    public DialogueDefinition Definition { get; }
    public WorldState State { get; }
    public EffectResult Result { get; }

    /// <summary>当前停留的台词、演出提示或选项节点；结束后为 null。</summary>
    public DialogueNode? Current { get; private set; }

    public IReadOnlyList<DialogueChoiceView> Choices => _choices;
    public bool Ended => Current is null;

    /// <summary>已显示的台词与已选选项的 <c>line_id</c>，按先后次序（对话记录与配音覆盖检查用）。演出提示不计入。</summary>
    public List<string> Transcript { get; } = [];

    /// <summary>已播放的演出提示节点 ID（覆盖检查用）。</summary>
    public List<string> StagesPlayed { get; } = [];

    /// <summary>停在选项上，需要 <see cref="Choose"/>；否则停在台词或演出提示上，用 <see cref="Continue"/>。</summary>
    public bool AwaitingChoice => Current is { Type: DialogueNodeType.Choice };

    /// <summary>读完当前台词或播完当前演出提示，继续。</summary>
    public void Continue()
    {
        if (Current is not { Type: DialogueNodeType.Line or DialogueNodeType.Stage } node)
        {
            throw new InvalidOperationException("当前不是台词或演出提示节点。");
        }

        RunFrom(node.Next);
    }

    public void Choose(int index)
    {
        if (Current is not { Type: DialogueNodeType.Choice } node)
        {
            throw new InvalidOperationException("当前不是选项节点。");
        }

        var view = _choices.Find(c => c.Index == index);
        if (view is null || !view.Enabled)
        {
            throw new InvalidOperationException($"选项 {index} 不可选。");
        }

        Transcript.Add(view.Option.LineId);
        if (view.Option.Effects.Count > 0)
        {
            _rules.Apply(State, view.Option.Effects, Result, $"dlg:{Definition.Id}/{node.Id}/{view.Option.LineId}");
        }

        if (!Result.Ok)
        {
            Stop();
            return;
        }

        RunFrom(view.Option.Next);
    }

    private void RunFrom(string? nodeId)
    {
        _choices.Clear();
        for (var step = 0; step < MaxAutoSteps; step++)
        {
            var node = Definition.Node(nodeId);
            if (node is null || node.Type == DialogueNodeType.End)
            {
                Stop();
                return;
            }

            switch (node.Type)
            {
                case DialogueNodeType.Line:
                    Current = node;
                    Transcript.Add(node.LineId!);
                    return;
                case DialogueNodeType.Stage:
                    Current = node;
                    StagesPlayed.Add(node.Id);
                    return;
                case DialogueNodeType.Choice:
                    foreach (var (option, i) in node.Options.Select((o, i) => (o, i)))
                    {
                        var ok = _rules.Check(option.When, State);
                        if (ok || option.LockedHint is not null)
                        {
                            _choices.Add(new DialogueChoiceView(i, option, ok));
                        }
                    }

                    if (!_choices.Exists(c => c.Enabled))
                    {
                        Result.Fail($"{Definition.Id}/{node.Id}：没有可选的选项");
                        Stop();
                        return;
                    }

                    Current = node;
                    return;
                case DialogueNodeType.Branch:
                    nodeId = node.Branches.FirstOrDefault(b => _rules.Check(b.When, State))?.Next ?? node.Next;
                    break;
                case DialogueNodeType.Effect:
                    var key = node.Repeatable ? null : $"dlg:{Definition.Id}/{node.Id}";
                    _rules.Apply(State, node.Effects, Result, key);
                    if (!Result.Ok)
                    {
                        Stop();
                        return;
                    }

                    nodeId = node.Next;
                    break;
                default:
                    nodeId = node.Next;
                    break;
            }
        }

        Result.Fail($"{Definition.Id}：自动节点超过 {MaxAutoSteps} 步，可能有无条件循环");
        Stop();
    }

    private void Stop()
    {
        Current = null;
        _choices.Clear();
    }
}
