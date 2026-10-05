using Godot;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Pages;

namespace WuxiaWorld.Game.Presentation.Play;

/// <summary>对话层借以播放演出提示的接口：有演出脚本的节点返回演出时长，没有的返回 null（照旧停一拍）。</summary>
public interface ICuePlayer
{
    /// <summary>开始播放某段对话的某个演出提示；state 为对话进行中的世界副本（演出按其中的事实挑分支）。</summary>
    float? Play(string dialogueId, DialogueNode node, WorldState state);

    /// <summary>当前演出立即走到结束状态（玩家点击跳过、换句）。</summary>
    void Finish();

    /// <summary>整段对话结束：交还人物与镜头。</summary>
    void EndDialogue();
}

/// <summary>
/// 剧情演出导演（M3，架构文档 9.4.18）：把对白里的演出提示（场景镜头、人物动作、物件特写）按 <see cref="CueScripts"/>
/// 写好的时间线播在探索布景上——摇镜头与推近、人物走位 / 跑位 / 跃起 / 换姿势 / 出场淡入、脚步与音效、水花、黑场转场。
/// 只改画面，不改世界状态：对话结束后剧情人物按世界状态重建，镜头回到主角。演出可随时跳到结束状态。
/// </summary>
public partial class CueDirector : Node, ICuePlayer
{
    private readonly ExploreStage _view;
    private readonly MapStage _staging;
    private readonly Dictionary<string, WalkerFigure> _spawned = new(StringComparer.Ordinal);
    private readonly List<Running> _running = [];
    private ColorRect _black = null!;
    private readonly Control _layer;
    private double _clock;

    public CueDirector(ExploreStage view, MapStage staging, Control layer)
    {
        _view = view;
        _staging = staging;
        _layer = layer;
    }

    public override void _Ready()
    {
        // 黑场压在对话层之下（转场时对话框已收起，章名与特写卡浮在上面）。
        _black = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = Colors.Transparent, Visible = false };
        _black.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_black);
        _layer.MoveChild(_black, 0);
    }

    public float? Play(string dialogueId, DialogueNode node, WorldState state) => Play($"{dialogueId}/{node.Id}", state);

    /// <summary>按“对话 ID/节点 ID”播放一段演出（开发核对 <c>--cue</c> 也走这里）。</summary>
    public float? Play(string key, WorldState state)
    {
        Finish();
        if (!CueScripts.All.TryGetValue(key, out var script))
        {
            return null;
        }

        var ctx = new CueContext(_view, _staging, state);
        var cue = new Cue().Bind(ctx);
        script(ctx, cue);
        if (AppHost.DevInfo)
        {
            GD.Print($"[cue] {key}：{cue.Steps.Count} 步，{cue.Length:0.0} 秒");
        }

        _clock = 0;
        foreach (var step in cue.Steps)
        {
            _running.Add(new Running(step));
        }

        // 减少动效时直接给出结束画面。
        if (!Motion.Enabled)
        {
            Finish();
            return Math.Min(cue.Length, 0.8f);
        }

        return cue.Length;
    }

    public override void _Process(double delta)
    {
        if (_running.Count == 0)
        {
            return;
        }

        _clock += delta;
        foreach (var r in _running)
        {
            if (r.Done || _clock < r.Step.At)
            {
                continue;
            }

            if (!r.Started)
            {
                r.Started = true;
                r.Step.Start(this);
            }

            r.Done = r.Step.Tick(this, (float)(_clock - r.Step.At), (float)delta);
        }

        _running.RemoveAll(r => r.Done);
    }

    public void Finish()
    {
        foreach (var r in _running.OrderBy(r => r.Step.At))
        {
            if (!r.Started)
            {
                r.Step.Start(this);
            }

            r.Step.Snap(this);
        }

        _running.Clear();
    }

    public void EndDialogue()
    {
        Finish();
        _black.Visible = false;
        _black.Modulate = Colors.Transparent;
        _spawned.Clear();
        _view.ReleaseCues();
    }

    /// <summary>开发核对：依次播放几段演出，各段之间停 1 秒，播完交还人物与镜头。</summary>
    public async void PlaySequence(IReadOnlyList<string> keys, WorldState state)
    {
        foreach (var key in keys)
        {
            if (Play(key, state) is not { } length)
            {
                GD.PushWarning($"演出核对：没有 {key} 的演出脚本");
                continue;
            }

            GD.Print($"[cue] 播放 {key}（{length:0.0} 秒）");
            await ToSignal(GetTree().CreateTimer(length + 1), SceneTreeTimer.SignalName.Timeout);
        }

        Finish();
        GD.Print("[cue] 演出核对播完");
    }

    // ── 时间线各步用到的操作 ────────────────────────────

    internal ExploreStage View => _view;

    internal ColorRect Black => _black;

    /// <summary>
    /// 按键名找人物：<c>hero</c> 为主角，<c>char.*</c> 先找画面上的同行者与站位人物，再找本段演出加上的；
    /// 其余键名（<c>escort.1</c>）只找本段演出加上的。找到后由演出接管。
    /// </summary>
    internal WalkerFigure? Find(string who)
    {
        WalkerFigure? figure = null;
        if (who == "hero")
        {
            figure = _view.HeroFigure;
        }
        else if (_spawned.TryGetValue(who, out var spawned) && IsInstanceValid(spawned))
        {
            figure = spawned;
        }
        else if (who.StartsWith("char.", StringComparison.Ordinal))
        {
            var art = Looks.Of(who).ArtId;
            figure = _view.Figures.FirstOrDefault(f => f.ArtId == art && IsInstanceValid(f));
        }

        if (figure is not null)
        {
            _view.Script(figure, true);
        }

        return figure;
    }

    /// <summary>找人物，画面上没有就在 at 处加一位（<c>char.*</c> 按人物外观，<c>escort.*</c> 为押运打手）。</summary>
    internal WalkerFigure Ensure(string who, Vector2 at, int facing)
    {
        if (Find(who) is { } figure)
        {
            return figure;
        }

        var look = who.StartsWith("escort", StringComparison.Ordinal) ? Looks.Escort : Looks.Of(who);
        figure = _view.Spawn(look, at, facing);
        _spawned[who] = figure;
        _view.Script(figure, true);
        return figure;
    }

    private sealed class Running(CueStep step)
    {
        public CueStep Step { get; } = step;

        public bool Started { get; set; }

        public bool Done { get; set; }
    }
}

/// <summary>演出脚本可读的布景与世界：摆放表里的位置、人物站位与事实。</summary>
public sealed class CueContext(ExploreStage view, MapStage staging, WorldState state)
{
    public WorldState State => state;

    public ExploreStage View => view;

    /// <summary>摆放表里的位置（<c>interact:*</c>、<c>anchor:*</c>、<c>exit:*</c>）。</summary>
    public Vector2 Point(string key) => staging.Points.TryGetValue(key, out var p) ? p : view.HeroGround;

    /// <summary>剧情人物在本图的站位（“事件 ID/人物 ID”优先，再按人物 ID）。</summary>
    public Vector2 Stand(string who, string? eventId = null) =>
        eventId is not null && staging.Stand.TryGetValue(eventId + "/" + who, out var s) ? s.At
        : staging.Stand.TryGetValue(who, out s) ? s.At
        : view.HeroGround;

    public Vector2 Hero => view.HeroGround;

    /// <summary>画面上某位人物现在的位置（不在画面上时取主角位置）。</summary>
    public Vector2 At(string who)
    {
        var art = who == "hero" ? view.HeroFigure.ArtId : Looks.Of(who).ArtId;
        return view.Figures.FirstOrDefault(f => f.ArtId == art)?.Ground ?? view.HeroGround;
    }

    public bool Has(string who) => view.Figures.Any(f => f.ArtId == Looks.Of(who).ArtId);

    public string? Fact(string id) => state.Facts.TryGetValue(id, out var v) ? v : null;
}
