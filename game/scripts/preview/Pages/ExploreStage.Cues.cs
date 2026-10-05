using Godot;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 剧情演出用的布景接口（M3，见架构文档 9.4.18）：演出导演（<c>CueDirector</c>）借这些方法摇镜头、让人物走位、跃起、换姿势、
/// 淡入淡出与播水花。被演出接管的人物不再跟随足迹、不受方向键驱动；演出结束后交还，剧情人物随下一次
/// <see cref="RefreshActors"/> 按世界状态重建，同行者沿足迹走回主角身后，镜头回到主角。
/// </summary>
public abstract partial class ExploreStage
{
    private readonly HashSet<WalkerFigure> _scripted = [];
    private float? _zoomOverride;
    private float _viewZoom = -1;

    /// <summary>镜头对准的地面点（世界平面坐标）与离地高度；为 null 时跟随主角。</summary>
    public (Vector2 Ground, float Height)? CameraFocus { get; set; }

    /// <summary>镜头追向目标的速率（每秒，指数趋近）：平常 7，演出摇镜头取 1.5–3。</summary>
    public float CameraRate { get; set; } = 7;

    /// <summary>演出指定的镜头倍率（越过玩家缩放的 0.85–1.15）；为 null 时用玩家的缩放。镜头倍率同样平滑过渡。</summary>
    public float? CameraZoom
    {
        get => _zoomOverride;
        set => _zoomOverride = value;
    }

    /// <summary>主角形象。</summary>
    public WalkerFigure HeroFigure => Hero;

    /// <summary>画面上的全部人物：主角、同行者与站位人物（含演出临时加上的）。</summary>
    public IEnumerable<WalkerFigure> Figures => _followers.Select(f => f.Figure).Prepend(Hero).Concat(_actors);

    /// <summary>脚步声的地面材质（演出走位也按它响脚步）。</summary>
    public string Surface => StepSurface;

    /// <summary>某处的地面高度（石阶、堤顶、栈桥）。</summary>
    public float GroundZ(Vector2 p) => StepZ(p);

    /// <summary>世界平面上某处能否站人（可走区内且不压物件），演出摆位核对用。</summary>
    public bool Standable(Vector2 p) => Walkable(p);

    /// <summary>演出临时加一位站位人物（不跟随、不挡路）；下一次 <see cref="RefreshActors"/> 时随站位人物一起清掉。</summary>
    public WalkerFigure Spawn(FollowerLook look, Vector2 at, int facing)
    {
        var figure = new WalkerFigure { Look = look.Look, Tone = look.Tone, ArtId = look.ArtId, Occluder = false, Facing = facing };
        figure.Place(at, StepZ(at));
        _sorted.AddChild(figure);
        _pieces.Add(figure);
        _actors.Add(figure);
        return figure;
    }

    /// <summary>演出接管 / 交还一位人物：接管期间不跟随足迹、主角不受方向键驱动。</summary>
    public void Script(WalkerFigure figure, bool on)
    {
        if (on)
        {
            _scripted.Add(figure);
        }
        else
        {
            _scripted.Remove(figure);
        }
    }

    /// <summary>交还全部人物，镜头回到主角。</summary>
    public void ReleaseCues()
    {
        foreach (var f in _scripted)
        {
            if (IsInstanceValid(f))
            {
                f.Moving = false;
                f.Lift = 0;
                f.QueueRedraw();
            }
        }

        _scripted.Clear();
        CameraFocus = null;
        CameraRate = 7;
        _zoomOverride = null;
    }

    /// <summary>把人物摆到某处（按地面高度）；主角移动时同步足迹，交还后同行者沿足迹跟上。</summary>
    public void PlaceFigure(WalkerFigure figure, Vector2 ground)
    {
        figure.Place(ground, StepZ(ground));
        if (figure == Hero && _trail[^1].DistanceTo(ground) > 8)
        {
            _trail.Add(ground);
            if (_trail.Count > 400) _trail.RemoveAt(0);
        }
    }

    /// <summary>布景上加一个短暂特效（水花……），画在人物之上，播完自行释放。</summary>
    public void AddEffect(Node2D effect, Vector2 ground)
    {
        effect.Position = TownView.P(ground, StepZ(ground));
        effect.ZIndex = 950;
        _world.AddChild(effect);
    }

    /// <summary>镜头立即到位（跳过演出时）。</summary>
    public void SnapCamera() => _cameraPlaced = false;

    private bool Scripted(WalkerFigure figure) => _scripted.Contains(figure);

    /// <summary>当前生效的镜头倍率：演出倍率或玩家缩放，平滑过渡。</summary>
    private float ViewZoom(float dt)
    {
        var target = _zoomOverride ?? _zoom;
        _viewZoom = _viewZoom < 0 || !_cameraPlaced || !Motion.Enabled ? target : Mathf.Lerp(_viewZoom, target, 1 - Mathf.Exp(-CameraRate * dt));
        return _viewZoom;
    }
}

/// <summary>人物踏水溅起的水花：一圈贴地水环与十来颗上溅下落的水滴，0.7 秒播完自行释放。</summary>
public partial class SplashFx : Node2D
{
    private const float Life = 0.7f;
    private float _t;
    private readonly float _scale;

    public SplashFx(float scale = 1) => _scale = scale;

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_t >= Life)
        {
            QueueFree();
            return;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        var k = _t / Life;
        var fade = 1 - k;
        var water = new Color(0.86f, 0.95f, 0.97f, 0.85f * fade);
        var ring = 30 + 70 * Mathf.Sqrt(k);

        // 贴地水环按 2:1 斜视压扁。
        DrawSetTransform(Vector2.Zero, 0, new Vector2(1, 0.5f));
        DrawArc(Vector2.Zero, ring * _scale, 0, Mathf.Tau, 40, water, 3, true);
        DrawArc(Vector2.Zero, ring * 1.4f * _scale, 0, Mathf.Tau, 40, water with { A = 0.5f * fade }, 2.5f, true);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        for (var i = 0; i < 12; i++)
        {
            var angle = -Mathf.Pi * (0.12f + 0.76f * i / 11f);
            var speed = 260 + 120 * Cel.Rand(31, i);
            var v = Vector2.FromAngle(angle) * speed;
            var p = (v * _t + new Vector2(0, 0.5f * 900 * _t * _t)) * _scale;
            if (p.Y > 6)
            {
                continue;
            }

            DrawCircle(p, (4.5f - 2.5f * k) * _scale, water);
        }
    }
}
