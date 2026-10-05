using Godot;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Pages;

namespace WuxiaWorld.Game.Presentation.Play;

/// <summary>
/// 一段演出的时间线（<see cref="CueScripts"/> 编写、<see cref="CueDirector"/> 播放）。<see cref="At"/> 定下其后各步的开始时刻（秒），
/// 每步可在 <see cref="CueDirector.Finish"/> 时直接跳到结束状态。演出总长取各步结束时刻与 <see cref="Hold"/> 的最大值，
/// 对话层按这个时长停住后自动继续。人物键名见 <see cref="CueDirector.Find"/>。
/// </summary>
public sealed class Cue
{
    public const float WalkSpeed = 300;
    public const float RunSpeed = 520;

    private readonly Dictionary<string, Vector2> _planned = new(StringComparer.Ordinal);
    private float _at;
    private float _lastEnd;
    private CueContext? _ctx;

    internal List<CueStep> Steps { get; } = [];

    /// <summary>演出总长（秒）。</summary>
    public float Length { get; private set; }

    internal Cue Bind(CueContext ctx)
    {
        _ctx = ctx;
        return this;
    }

    /// <summary>其后各步从第 t 秒开始。</summary>
    public Cue At(float t)
    {
        _at = t;
        return this;
    }

    /// <summary>其后各步接在上一步做完之后开始（先走到堤上，再走下石阶）。</summary>
    public Cue After(float pause = 0)
    {
        _at = _lastEnd + pause;
        return this;
    }

    /// <summary>演出至少停 t 秒（动作做完后留一拍）。</summary>
    public Cue Hold(float t)
    {
        Length = Math.Max(Length, t);
        return this;
    }

    /// <summary>镜头摇向地面某处（height 为对准点离地高度），rate 越小越慢；zoom 给出时推近 / 拉远到该倍率。seconds 只计入演出时长。</summary>
    public Cue Pan(Vector2 ground, float rate = 2.4f, float height = 90, float? zoom = null, float seconds = 1.4f) =>
        Add(new PanStep(ground, height, rate, zoom), seconds);

    /// <summary>人物走（或跑）到某处；face 给出时到达后转向（1 朝画面右、-1 朝左）。</summary>
    public Cue Walk(string who, Vector2 to, bool run = false, int? face = null, float speed = 0)
    {
        var v = speed > 0 ? speed : run ? RunSpeed : WalkSpeed;
        var from = Where(who);
        _planned[who] = to;
        return Add(new WalkStep(who, to, v, run, face), from.DistanceTo(to) / v);
    }

    /// <summary>人物从当前位置（或 from）跃到某处，腾空最高 height 像素。</summary>
    public Cue Leap(string who, Vector2 to, float seconds = 0.75f, float height = 150, Vector2? from = null, int? face = null)
    {
        _planned[who] = to;
        return Add(new LeapStep(who, from, to, seconds, height, face), seconds);
    }

    /// <summary>人物在某处淡入出场（画面上已有则挪过去再淡入）。</summary>
    public Cue Appear(string who, Vector2 at, int facing, float fade = 0.4f, string? pose = null)
    {
        _planned[who] = at;
        return Add(new AppearStep(who, at, facing, fade, pose), fade);
    }

    /// <summary>人物淡出（仍在画面里、不可见）。</summary>
    public Cue Vanish(string who, float fade = 0.3f) => Add(new VanishStep(who, fade), fade);

    /// <summary>换姿势帧（<c>down</c>、<c>guard</c>……）；null 回到站姿。</summary>
    public Cue Pose(string who, string? pose) => Add(new PoseStep(who, pose), 0);

    /// <summary>转向：facing 1 朝画面右、-1 朝左；back 为背对镜头。</summary>
    public Cue Face(string who, int facing, bool back = false) => Add(new FaceStep(who, facing, back), 0);

    public Cue Sound(string id, float db = 0) => Add(new SoundStep(id, db), 0);

    /// <summary>某处溅起水花。</summary>
    public Cue Splash(Vector2 ground, float scale = 1) => Add(new SplashStep(ground, scale), 0.7f);

    /// <summary>黑场转场：fadeIn 秒压黑，停 hold 秒（全黑时执行 onDark），fadeOut 秒亮回。</summary>
    public Cue Black(float fadeIn = 0.7f, float hold = 0.6f, float fadeOut = 0.9f, Action<CueDirector>? onDark = null) =>
        Add(new BlackStep(fadeIn, hold, fadeOut, onDark), fadeIn + hold + fadeOut);

    /// <summary>某人物在演出里的位置：本段已安排的落点，否则画面上的当前位置。</summary>
    public Vector2 Where(string who) => _planned.TryGetValue(who, out var p) ? p : _ctx?.At(who) ?? Vector2.Zero;

    private Cue Add(CueStep step, float duration)
    {
        step.At = _at;
        Steps.Add(step);
        _lastEnd = _at + duration;
        Length = Math.Max(Length, _lastEnd);
        return this;
    }
}

/// <summary>时间线的一步：到时刻时 Start，之后每帧 Tick（返回 true 为做完），跳过时 Snap 到结束状态。</summary>
internal abstract class CueStep
{
    public float At { get; set; }

    public virtual void Start(CueDirector d)
    {
    }

    /// <summary>t 为本步已进行的秒数。</summary>
    public virtual bool Tick(CueDirector d, float t, float dt) => true;

    public virtual void Snap(CueDirector d)
    {
    }

    protected static float Ease(float x) => x < 0.5f ? 2 * x * x : 1 - Mathf.Pow(-2 * x + 2, 2) / 2;
}

internal sealed class PanStep(Vector2 ground, float height, float rate, float? zoom) : CueStep
{
    public override void Start(CueDirector d)
    {
        d.View.CameraFocus = (ground, height);
        d.View.CameraRate = rate;
        if (zoom is { } z)
        {
            d.View.CameraZoom = z;
        }
    }

    public override void Snap(CueDirector d) => d.View.SnapCamera();
}

internal sealed class WalkStep(string who, Vector2 to, float speed, bool run, int? face) : CueStep
{
    private WalkerFigure? _f;
    private float _stride;

    public override void Start(CueDirector d)
    {
        _f = d.Find(who);
        if (_f is null)
        {
            GD.PushWarning($"演出：画面上没有 {who}，走位略过");
        }
    }

    public override bool Tick(CueDirector d, float t, float dt)
    {
        if (_f is null || !GodotObject.IsInstanceValid(_f))
        {
            return true;
        }

        var gap = to - _f.Ground;
        var dist = gap.Length();
        if (dist < 2)
        {
            Arrive(d);
            return true;
        }

        var step = Math.Min(dist, speed * dt);
        var move = gap / dist * step;
        var sx = TownView.ScreenX(move);
        if (Mathf.Abs(sx) > step * 0.3f) _f.Facing = sx > 0 ? 1 : -1;
        _f.TurnToward(move);
        _f.Running = run;
        _f.Moving = true;
        _f.Phase += step / (run ? 44 : 32);
        d.View.PlaceFigure(_f, _f.Ground + move);
        _f.QueueRedraw();
        _stride += step;
        if (_stride > speed * 0.42f)
        {
            _stride = 0;
            AppHost.Instance.Sound.Play("step." + d.View.Surface, -15, 0.07f);
        }

        return false;
    }

    public override void Snap(CueDirector d)
    {
        if (_f is not null && GodotObject.IsInstanceValid(_f))
        {
            d.View.PlaceFigure(_f, to);
            Arrive(d);
        }
    }

    private void Arrive(CueDirector d)
    {
        _f!.Moving = false;
        _f.Running = false;
        if (face is { } dir)
        {
            _f.Facing = dir;
            _f.Back = false;
        }

        _f.QueueRedraw();
    }
}

internal sealed class LeapStep(string who, Vector2? from, Vector2 to, float seconds, float height, int? face) : CueStep
{
    private WalkerFigure? _f;
    private Vector2 _from;

    public override void Start(CueDirector d)
    {
        _f = from is { } f0 ? d.Ensure(who, f0, face ?? 1) : d.Find(who);
        if (_f is null)
        {
            return;
        }

        _from = from ?? _f.Ground;
        _f.SelfModulate = Colors.White;
        var sx = TownView.ScreenX(to - _from);
        _f.Facing = face ?? (sx >= 0 ? 1 : -1);
        _f.Back = false;
        _f.Running = true;
        _f.Moving = true;
        AppHost.Instance.Sound.Play("travel.whoosh", -10, 0.05f);
    }

    public override bool Tick(CueDirector d, float t, float dt)
    {
        if (_f is null || !GodotObject.IsInstanceValid(_f))
        {
            return true;
        }

        var k = Math.Min(1, t / seconds);
        var e = Ease(k);
        d.View.PlaceFigure(_f, _from.Lerp(to, e));
        _f.Lift = height * 4 * k * (1 - k);
        _f.Phase = Mathf.Pi * 0.5f; // 腾空保持跨步的那一帧
        _f.QueueRedraw();
        if (k >= 1)
        {
            Land(d);
            return true;
        }

        return false;
    }

    public override void Snap(CueDirector d)
    {
        if (_f is not null && GodotObject.IsInstanceValid(_f))
        {
            d.View.PlaceFigure(_f, to);
            Land(d);
        }
    }

    private void Land(CueDirector d)
    {
        _f!.Lift = 0;
        _f.Moving = false;
        _f.Running = false;
        _f.QueueRedraw();
        AppHost.Instance.Sound.Play("step." + d.View.Surface, -8, 0.05f);
    }
}

internal sealed class AppearStep(string who, Vector2 at, int facing, float fade, string? pose) : CueStep
{
    private WalkerFigure? _f;

    public override void Start(CueDirector d)
    {
        _f = d.Ensure(who, at, facing);
        _f.Pose = pose;
        _f.Facing = facing;
        _f.Back = false;
        d.View.PlaceFigure(_f, at);
        _f.SelfModulate = Colors.White with { A = 0 };
        _f.QueueRedraw();
    }

    public override bool Tick(CueDirector d, float t, float dt)
    {
        if (_f is null || !GodotObject.IsInstanceValid(_f))
        {
            return true;
        }

        var k = fade <= 0 ? 1 : Math.Min(1, t / fade);
        _f.SelfModulate = Colors.White with { A = k };
        return k >= 1;
    }

    public override void Snap(CueDirector d)
    {
        if (_f is not null && GodotObject.IsInstanceValid(_f))
        {
            _f.SelfModulate = Colors.White;
        }
    }
}

internal sealed class VanishStep(string who, float fade) : CueStep
{
    private WalkerFigure? _f;

    public override void Start(CueDirector d) => _f = d.Find(who);

    public override bool Tick(CueDirector d, float t, float dt)
    {
        if (_f is null || !GodotObject.IsInstanceValid(_f))
        {
            return true;
        }

        var k = fade <= 0 ? 1 : Math.Min(1, t / fade);
        _f.SelfModulate = Colors.White with { A = 1 - k };
        return k >= 1;
    }

    public override void Snap(CueDirector d)
    {
        if (_f is not null && GodotObject.IsInstanceValid(_f))
        {
            _f.SelfModulate = Colors.White with { A = 0 };
        }
    }
}

internal sealed class PoseStep(string who, string? pose) : CueStep
{
    public override void Start(CueDirector d)
    {
        if (d.Find(who) is { } f)
        {
            f.Pose = pose;
            d.View.PlaceFigure(f, f.Ground);
            f.QueueRedraw();
        }
    }
}

internal sealed class FaceStep(string who, int facing, bool back) : CueStep
{
    public override void Start(CueDirector d)
    {
        if (d.Find(who) is { } f)
        {
            f.Facing = facing;
            f.Back = back;
            f.QueueRedraw();
        }
    }
}

internal sealed class SoundStep(string id, float db) : CueStep
{
    public override void Start(CueDirector d) => AppHost.Instance.Sound.Play(id, db);
}

internal sealed class SplashStep(Vector2 ground, float scale) : CueStep
{
    public override void Start(CueDirector d)
    {
        if (Motion.Enabled)
        {
            d.View.AddEffect(new SplashFx(scale), ground);
        }
    }
}

internal sealed class BlackStep(float fadeIn, float hold, float fadeOut, Action<CueDirector>? onDark) : CueStep
{
    private bool _dark;

    public override void Start(CueDirector d) => d.Black.Visible = true;

    public override bool Tick(CueDirector d, float t, float dt)
    {
        float a;
        if (t < fadeIn)
        {
            a = t / fadeIn;
        }
        else if (t < fadeIn + hold)
        {
            a = 1;
            Dark(d);
        }
        else
        {
            Dark(d);
            a = 1 - Math.Min(1, (t - fadeIn - hold) / fadeOut);
        }

        d.Black.Modulate = Colors.White with { A = a };
        if (t >= fadeIn + hold + fadeOut)
        {
            d.Black.Visible = false;
            return true;
        }

        return false;
    }

    public override void Snap(CueDirector d)
    {
        Dark(d);
        d.Black.Modulate = Colors.Transparent;
        d.Black.Visible = false;
    }

    private void Dark(CueDirector d)
    {
        if (!_dark)
        {
            _dark = true;
            onDark?.Invoke(d);
        }
    }
}
