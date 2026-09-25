using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 探索布景页的共用骨架：城镇、客栈室内与野外各页只提供布局（地面、物件、可走区、可交互点）与 HUD 文案，
/// 行走、同行者跟随、前后排序、遮挡淡出、交互提示、镜头与缩放都在这里实现一次。
/// 全部布景经 <see cref="TownView"/> 的同一个2:1 等距正交投影画出（镜头在西南，东西向自左下到右上）。
/// WASD / 方向键按屏幕方向行走（Shift 快走），陆青禾沿足迹跟随；靠近可交互物出现“E + 动作 + 对象”，
/// E 推一条通知或切到交互点指定的布景页，不写存档；滚轮缩放 0.85–1.15（架构文档 10.2）。
/// </summary>
public abstract partial class ExploreStage : Control
{
    protected const float MinZoom = 0.85f;
    protected const float MaxZoom = 1.15f;
    private const float WalkSpeed = 330;
    private const float RunSpeed = 520;
    private const float FootRadius = 18;
    private const float InteractRange = 170;
    private const float FollowGap = 110;

    private static readonly Dictionary<Key, string> Pages = new()
    {
        [Key.M] = "res://scenes/preview/WorldMap.tscn",
        [Key.C] = "res://scenes/preview/Character.tscn",
        [Key.I] = "res://scenes/preview/Inventory.tscn",
        [Key.J] = "res://scenes/preview/Journal.tscn",
    };

    private readonly List<TownPiece> _pieces = [];
    private readonly List<(TownInteraction Data, InteractMarker Marker)> _interactions = [];
    private readonly List<Vector2> _trail = [];
    private readonly List<Rect2> _blockers = [];

    private Node2D _world = null!;
    private Node2D _sorted = null!;
    private InteractPrompt _prompt = null!;
    private ToastColumn _toasts = null!;
    private Control _mini = null!;
    private GoalPointer? _pointer;
    private TownInteraction? _near;
    private Vector2 _camera;
    private Vector2 _heading = new(1, 0);
    private float _luIdle = 1;
    private float _zoom = 1;
    private bool _cameraPlaced;
    private Rect2 _cameraBounds;

    protected WalkerFigure Hero { get; private set; } = null!;

    protected WalkerFigure Lu { get; private set; } = null!;

    /// <summary>贴地层：画在所有排序件之下（地面、驳岸、室内的地砖与后墙）。</summary>
    protected Node2D GroundLayer { get; private set; } = null!;

    /// <summary>悬空层：画在所有排序件之上、交互菱形之下（室内吊灯）。</summary>
    protected Node2D OverheadLayer { get; private set; } = null!;

    protected IEnumerable<TownPiece> Pieces => _pieces;

    /// <summary>镜头中心（布景的投影坐标），供远景视差使用。</summary>
    protected Vector2 CameraCenter => _camera;

    /// <summary>
    /// 镜头可到的范围（投影坐标）。默认取 <see cref="Bounds"/> 四角投影的外框；
    /// 布景不是沿世界轴铺开时（山路的台地沿画面水平展开）由页面直接给出。
    /// </summary>
    protected virtual Rect2? CameraArea => null;

    /// <summary>主线目标：世界平面坐标、离地高度与名称；在画面外时屏幕边缘出现指向它的箭头。</summary>
    protected virtual (Vector2 Ground, float Height, string Label)? Goal => null;

    /// <summary>布景的世界范围（x 东、y 南），镜头不越出其投影外框。</summary>
    protected abstract Rect2 Bounds { get; }

    protected abstract IReadOnlyList<TownInteraction> Interactions { get; }

    /// <summary>左上地点：地区章、地点名、时辰天气。</summary>
    protected abstract (string Region, string Name, string Time) PlaceInfo { get; }

    /// <summary>底部说明条：写明本页是占位样板。</summary>
    protected abstract string Caption { get; }

    /// <summary>进入时主角与陆青禾的位置和缩放；按 <see cref="DevCapture.Tab"/> 与 <see cref="PreviewSession.Arrival"/> 决定。</summary>
    protected abstract (Vector2 Hero, Vector2 Lu, float Zoom) Start(string? arrival);

    /// <summary>搭布景：往 <see cref="GroundLayer"/> 加地面，用 <see cref="Add"/> 加排序件。</summary>
    protected abstract void BuildScene();

    protected abstract bool InWalkArea(Vector2 q);

    protected abstract Control CreateMiniMap(Func<(Vector2 Position, Vector2 Heading)> hero);

    /// <summary>站在某处时的高度（石阶上为负）。</summary>
    protected virtual float StepZ(Vector2 p) => 0;

    /// <summary>每帧的布景小动效（船随水晃……）。</summary>
    protected virtual void Animate(float seconds)
    {
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ClipContents = true;
        var arrival = PreviewSession.Current.Arrival;
        PreviewSession.Current.Arrival = null;
        var (hero, lu, zoom) = Start(arrival);
        _zoom = zoom;
        _trail.Add(lu);
        _trail.Add(hero);

        // 布景内部按 ZIndex 排前后；整体压到 -4000，保证在 HUD（ZIndex 0）之下。
        _world = new Node2D { ZIndex = -4000 };
        AddChild(_world);
        GroundLayer = new Node2D { ZIndex = -10 };
        _world.AddChild(GroundLayer);
        _sorted = new Node2D();
        _world.AddChild(_sorted);
        OverheadLayer = new Node2D { ZIndex = 900 };
        _world.AddChild(OverheadLayer);
        BuildScene();

        Lu = new WalkerFigure { Tone = UiPalette.Trim, Look = FigureLook.Boatwoman };
        Hero = new WalkerFigure { Tone = UiPalette.Accent.Lightened(0.1f) };
        Lu.Place(lu, StepZ(lu));
        Hero.Place(hero, StepZ(hero));
        _sorted.AddChild(Lu);
        _sorted.AddChild(Hero);

        var markers = new Node2D { ZIndex = 1000 };
        _world.AddChild(markers);
        foreach (var item in Interactions)
        {
            var marker = new InteractMarker { Position = TownView.P(item.Position, StepZ(item.Position) + item.MarkerHeight) };
            markers.AddChild(marker);
            _interactions.Add((item, marker));
        }

        if (CameraArea is { } area)
        {
            _cameraBounds = area;
        }
        else
        {
            var b = Bounds;
            var corners = new[] { TownView.P(b.Position), TownView.P(b.End), TownView.P(b.Position.X, b.End.Y), TownView.P(b.End.X, b.Position.Y) };
            _cameraBounds = new Rect2(corners[0], Vector2.Zero);
            foreach (var c in corners) _cameraBounds = _cameraBounds.Expand(c);
        }

        BuildHud();
    }

    /// <summary>加一件参与排序的物件；blocks 为 false 时不挡路（船、屋面）。</summary>
    protected void Add(TownPiece piece, bool blocks = true)
    {
        _sorted.AddChild(piece);
        _pieces.Add(piece);
        if (blocks && !piece.Overhead)
        {
            _blockers.Add(piece.Foot);
        }
    }

    // ── HUD ──────────────────────────────────────────────

    private void BuildHud()
    {
        var (region, name, time) = PlaceInfo;
        AddChild(ExploreHudKit.Place(region, name, time));
        _mini = CreateMiniMap(() => (Hero.Ground, _heading));
        AddChild(ExploreHudKit.MiniMapFrame(_mini, region));
        AddChild(Tracker());
        AddChild(ExploreHudKit.Party());
        AddChild(ExploreHudKit.Shortcuts(("WASD", "行走"), ("Shift", "快走"), ("E", "交互"), ("滚轮", "缩放"), ("M", "地图"), ("Esc", "返回标题")));

        _prompt = new InteractPrompt { Visible = false };
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.AddChild(_prompt);
        AddChild(Ui.Place(center, 0.5f, 1, -300, -250, 300, -180));

        var tag = Ui.Panel(UiTheme.GlassPanel, Ui.Text(Caption, UiTheme.DarkMutedLabel, 16));
        var tagBox = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        tagBox.AddChild(tag);
        AddChild(Ui.Place(tagBox, 0.5f, 1, -420, -150, 420, -106));

        if (Goal is { } goal)
        {
            _pointer = new GoalPointer { Label = goal.Label };
            AddChild(_pointer);
        }

        _toasts = ToastColumn.Placed(this);
    }

    /// <summary>左侧目标追踪；野外等不在第一章的布景换成本地的样例任务。</summary>
    protected virtual Control Tracker() => ExploreHudKit.Tracker();

    // ── 输入 ─────────────────────────────────────────────

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                _zoom = Mathf.Clamp(_zoom * (wheel.ButtonIndex == MouseButton.WheelUp ? 1.05f : 1 / 1.05f), MinZoom, MaxZoom);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.E } when _near is { } near:
                if (near.Scene is { } scene && SceneRouter.CanGoTo(scene))
                {
                    PreviewSession.Current.Arrival = near.Arrival;
                    AppHost.Instance.Router.GoTo(scene);
                }
                else
                {
                    _toasts.Push(near.Kind, near.Text, near.Where);
                }

                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Pressed: true, Echo: false } key when Pages.TryGetValue(key.Keycode, out var page):
                AppHost.Instance.Router.GoTo(page);
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    // ── 每帧：行走、跟随、排序、遮挡、镜头 ────────────────

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        MoveHero(dt);
        FollowTrail(dt);
        DepthSort.Apply(_pieces.Cast<ISortable>().Append(Hero).Append(Lu).ToList());
        UpdateInteraction();
        UpdateOcclusion(dt);
        UpdateCamera(dt);
        UpdatePointer();
        Animate(Time.GetTicksMsec() / 1000f);
        _mini.QueueRedraw();
    }

    private void MoveHero(float dt)
    {
        var input = Vector2.Zero;
        if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left)) input.X -= 1;
        if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right)) input.X += 1;
        if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up)) input.Y -= 1;
        if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down)) input.Y += 1;

        var moving = input != Vector2.Zero;
        if (moving)
        {
            // 按屏幕方向走：换算成地面方向后按世界速度移动；分轴判定以便沿墙滑行。
            var dir = TownView.GroundFromScreen(input).Normalized();
            var speed = Input.IsPhysicalKeyPressed(Key.Shift) ? RunSpeed : WalkSpeed;
            var step = dir * speed * dt;
            var pos = Hero.Ground;
            if (Walkable(pos + new Vector2(step.X, 0))) pos.X += step.X;
            if (Walkable(pos + new Vector2(0, step.Y))) pos.Y += step.Y;
            if (input.X != 0) Hero.Facing = input.X > 0 ? 1 : -1;
            _heading = dir;
            Hero.Phase += (pos - Hero.Ground).Length() / 32;
            Hero.Place(pos, StepZ(pos));
            if (_trail[^1].DistanceTo(pos) > 8)
            {
                _trail.Add(pos);
                if (_trail.Count > 200) _trail.RemoveAt(0);
            }
        }

        if (moving || Hero.Moving)
        {
            Hero.Moving = moving;
            Hero.QueueRedraw();
        }
    }

    /// <summary>
    /// 同行者追向足迹上距主角正好 FollowGap 的点（沿足迹折线连续插值，目标随主角平滑移动，不按足迹点跳格），
    /// 速度随落后距离平滑增减；停步有短暂缓冲、朝向按实际位移换，避免走停与左右在相邻帧间来回切换造成抖动。
    /// 不做碰撞（足迹本身都在可走区内）。
    /// </summary>
    private void FollowTrail(float dt)
    {
        var target = TrailPoint(FollowGap);
        var gap = target - Lu.Ground;
        var dist = gap.Length();
        var step = dist < 1 ? 0 : Mathf.Min(dist, Mathf.Min(RunSpeed * 1.1f, dist * 8) * dt);
        if (step > 0)
        {
            var move = gap / dist * step;
            var pos = Lu.Ground + move;
            Lu.Phase += step / 30;
            var sx = TownView.ScreenX(move);
            if (Mathf.Abs(sx) > step * 0.3f) Lu.Facing = sx > 0 ? 1 : -1;
            Lu.Place(pos, StepZ(pos));
        }

        // 每帧位移低于约 60/秒 才算停步，且须连续 0.15 秒，避免起伏动画在相邻帧间开关。
        _luIdle = step > 60 * dt ? 0 : _luIdle + dt;
        var moving = _luIdle < 0.15f;
        if (moving || Lu.Moving)
        {
            Lu.Moving = moving;
            Lu.QueueRedraw();
        }
    }

    /// <summary>足迹折线（末端接主角当前位置）上距主角 back 的点；足迹不够长时取最早的足迹点。</summary>
    private Vector2 TrailPoint(float back)
    {
        var ahead = Hero.Ground;
        for (var i = _trail.Count - 1; i >= 0; i--)
        {
            var seg = ahead.DistanceTo(_trail[i]);
            if (seg >= back)
            {
                return ahead.Lerp(_trail[i], back / seg);
            }

            back -= seg;
            ahead = _trail[i];
        }

        return ahead;
    }

    private bool Walkable(Vector2 p)
    {
        Span<Vector2> probes = [p, p + new Vector2(FootRadius, 0), p - new Vector2(FootRadius, 0), p + new Vector2(0, FootRadius), p - new Vector2(0, FootRadius)];
        foreach (var q in probes)
        {
            if (!InWalkArea(q))
            {
                return false;
            }

            foreach (var b in _blockers)
            {
                if (b.HasPoint(q))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void UpdateInteraction()
    {
        TownInteraction? near = null;
        var best = InteractRange;
        foreach (var (data, _) in _interactions)
        {
            var d = data.Position.DistanceTo(Hero.Ground);
            if (d < best)
            {
                best = d;
                near = data;
            }
        }

        if (near == _near)
        {
            return;
        }

        _near = near;
        foreach (var (data, marker) in _interactions)
        {
            marker.Near = data == near;
            marker.QueueRedraw();
        }

        if (near is null)
        {
            _prompt.Visible = false;
        }
        else
        {
            _prompt.Show(near.Verb, near.Target);
        }
    }

    /// <summary>排在行人之前（更近镜头）的物件，其画出的面盖住行人头胸时淡到 0.4。</summary>
    private void UpdateOcclusion(float dt)
    {
        var walkers = new[] { Hero, Lu };
        foreach (var piece in _pieces)
        {
            var hidden = piece.Occluder && walkers.Any(w => piece.ZIndex > w.ZIndex && piece.Covers(w));
            var target = hidden ? 0.4f : 1f;
            var a = piece.Modulate.A;
            var next = Motion.Enabled ? Mathf.MoveToward(a, target, dt * 3.5f) : target;
            if (!Mathf.IsEqualApprox(a, next))
            {
                piece.Modulate = Colors.White with { A = next };
            }
        }
    }

    private void UpdateCamera(float dt)
    {
        var target = Hero.Position + new Vector2(0, -80);
        var view = Size / _zoom;
        var b = _cameraBounds;
        target.X = view.X >= b.Size.X ? b.GetCenter().X : Mathf.Clamp(target.X, b.Position.X + view.X / 2, b.End.X - view.X / 2);
        target.Y = view.Y >= b.Size.Y ? b.GetCenter().Y : Mathf.Clamp(target.Y, b.Position.Y + view.Y / 2, b.End.Y - view.Y / 2);
        _camera = _cameraPlaced && Motion.Enabled ? _camera.Lerp(target, 1 - Mathf.Exp(-7 * dt)) : target;
        _cameraPlaced = true;
        _world.Scale = new Vector2(_zoom, _zoom);
        _world.Position = (Size / 2 - _camera * _zoom).Round();
    }

    /// <summary>目标在画面外时，箭头停在屏幕边缘（避开四角 HUD）指向它。</summary>
    private void UpdatePointer()
    {
        if (_pointer is null || Goal is not { } goal)
        {
            return;
        }

        var screen = _world.Position + TownView.P(goal.Ground, StepZ(goal.Ground) + goal.Height) * _zoom;
        // 内框避开四角 HUD：左侧地点与目标追踪、右上小地图、下方队伍与快捷键。
        var inner = new Rect2(530, 110, Size.X - 530 - 380, Size.Y - 110 - 180);
        if (inner.HasPoint(screen))
        {
            _pointer.Visible = false;
            return;
        }

        var center = Size / 2;
        var dir = (screen - center).Normalized();
        // 与内框相交的点：取两轴中先碰到的边。
        var half = inner.Size / 2;
        var t = Mathf.Min(dir.X == 0 ? float.MaxValue : half.X / Mathf.Abs(dir.X), dir.Y == 0 ? float.MaxValue : half.Y / Mathf.Abs(dir.Y));
        _pointer.Visible = true;
        _pointer.Position = (center + dir * t).Round();
        _pointer.Direction = dir;
        _pointer.QueueRedraw();
    }
}
