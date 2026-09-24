using Godot;

namespace WuxiaWorld.Game.Presentation.Ui;

/// <summary>
/// 界面动效（docs/art/UI_DESIGN.md 第 6 节）：入场淡入上浮、依次入场、呼吸闪烁。
/// 时长统一取 <see cref="Quick"/> / <see cref="Normal"/>；截图模式与“减少动效”时直接落到终态。
/// </summary>
public static class Motion
{
    public const float Quick = 0.16f;
    public const float Normal = 0.32f;
    public const float Slow = 0.6f;

    /// <summary>为 false 时所有动效直接完成（截图、减少动效）。</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>淡入并自下方 <paramref name="rise"/> 像素处上浮到位；等一帧布局稳定后开始。</summary>
    public static void Enter(Control control, float delay = 0, float duration = Normal, float rise = 16, float fromX = 0)
    {
        if (!Enabled)
        {
            return;
        }

        control.Modulate = control.Modulate with { A = 0 };
        if (control.IsNodeReady())
        {
            Start();
        }
        else
        {
            control.Ready += Start;
        }

        async void Start()
        {
            await control.ToSignal(control.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!GodotObject.IsInstanceValid(control))
            {
                return;
            }

            var end = control.Position;
            control.Position = end + new Vector2(fromX, rise);
            var tween = control.CreateTween().SetParallel().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            tween.TweenProperty(control, "modulate:a", 1f, duration).SetDelay(delay);
            tween.TweenProperty(control, "position", end, duration).SetDelay(delay);
        }
    }

    /// <summary>对一组节点依次调用 <see cref="Enter"/>。</summary>
    public static void Stagger(IEnumerable<Control> controls, float start = 0, float step = 0.05f, float rise = 14, float fromX = 0)
    {
        var i = 0;
        foreach (var c in controls)
        {
            Enter(c, start + step * i++, Normal, rise, fromX);
        }
    }

    /// <summary>透明度往复（“按任意键”提示、可交互标记）。</summary>
    public static void Pulse(CanvasItem item, float low = 0.35f, float period = 1.6f)
    {
        if (!Enabled)
        {
            return;
        }

        var tween = item.CreateTween().SetLoops().SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(item, "modulate:a", low, period / 2);
        tween.TweenProperty(item, "modulate:a", 1f, period / 2);
    }

    /// <summary>淡出后执行回调。</summary>
    public static void FadeOut(CanvasItem item, float duration, Action? done = null)
    {
        if (!Enabled)
        {
            item.Modulate = item.Modulate with { A = 0 };
            done?.Invoke();
            return;
        }

        var tween = item.CreateTween();
        tween.TweenProperty(item, "modulate:a", 0f, duration);
        if (done is not null)
        {
            tween.TweenCallback(Callable.From(done));
        }
    }

    public static void FadeIn(CanvasItem item, float duration)
    {
        if (!Enabled)
        {
            item.Modulate = item.Modulate with { A = 1 };
            return;
        }

        item.Modulate = item.Modulate with { A = 0 };
        item.CreateTween().TweenProperty(item, "modulate:a", 1f, duration);
    }
}
