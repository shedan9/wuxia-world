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
/// 设了 <see cref="Driver"/> 时进入游戏模式（M2）：落点、交互点、同行者、站位人物与目标都由驱动方
/// （<c>ExplorationScreen</c>，背后是 <c>GameSession</c>）给出，E 交给驱动方处理；展示页的样例数据与 HUD 部件不再使用。
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
    private readonly List<Follower> _followers = [];
    private readonly List<WalkerFigure> _actors = [];
    private readonly List<Vector2> _trail = [];
    private readonly List<Rect2> _blockers = [];
    private readonly List<Control> _hud = [];

    private Node2D _world = null!;
    private Node2D _sorted = null!;
    private Node2D _markers = null!;
    private InteractPrompt? _prompt;
    private ToastColumn _toasts = null!;
    private Control _mini = null!;
    private GoalPointer? _pointer;
    private TownInteraction? _near;
    private Vector2 _camera;
    private Vector2 _heading = new(1, 0);
    private float _zoom = 1;
    private bool _cameraPlaced;
    private float _stride;
    private Rect2 _cameraBounds;

    protected WalkerFigure Hero { get; private set; } = null!;

    /// <summary>游戏模式的驱动方；为 null 时是 M0 展示页。须在加入场景树前设置。</summary>
    public IExploreDriver? Driver { get; init; }

    /// <summary>主角当前所在（世界平面坐标）。</summary>
    public Vector2 HeroGround => Hero.Ground;

    /// <summary>
    /// 自动走查的“虚拟摇杆”（开发用，<c>--walk</c>）：给出世界平面上的行走方向，主角按快走速度走、照常碰撞与沿墙滑行；
    /// 为 null 时读键盘。
    /// </summary>
    public Vector2? BotGround { get; set; }

    /// <summary>
    /// 开发核对（<c>--drive</c>）：按顺序模拟按住屏幕方向键若干秒（run 为同时按 Shift），用于 <c>--write-movie</c> 录行走 / 快走 / 转身的换帧。
    /// 进第一张可操作的地图后开始，走完即清空。
    /// </summary>
    public static Queue<(Vector2 Dir, bool Run, float Seconds)> DevDrive { get; } = new();

    private float _driveLeft = -1;

    /// <summary>开发核对（<c>--zoom</c>）：镜头倍率，越过游戏内缩放上下限，用于录像核对人物换帧的近景。</summary>
    public static float? DevZoom { get; set; }

    /// <summary>当前高亮、按 E 会触发的交互点。</summary>
    public TownInteraction? NearInteraction => _near;

    /// <summary>布景的世界范围（自动走查寻路用）。</summary>
    public Rect2 WorldBounds => Bounds;

    /// <summary>交互距离（自动走查寻路用）。</summary>
    public static float InteractReach => InteractRange;

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

    private (Vector2 Ground, float Height, string Label)? CurrentGoal => Driver is { } d ? d.Goal : Goal;

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

    /// <summary>世界平面坐标是否在可走区内（不含物件占地）。</summary>
    public abstract bool InWalkArea(Vector2 q);

    /// <summary>小地图；goal 给出当前目标的世界平面坐标（无目标为 null）。</summary>
    protected abstract Control CreateMiniMap(Func<(Vector2 Position, Vector2 Heading)> hero, Func<Vector2?> goal);

    /// <summary>站在某处时的高度（石阶上为负）。</summary>
    protected virtual float StepZ(Vector2 p) => 0;

    /// <summary>脚步声的地面材质：<c>step.&lt;材质&gt;.N</c>（城镇石板、客栈木地板、野外泥土）。</summary>
    protected virtual string StepSurface => "stone";

    /// <summary>光色时段：游戏模式取驱动方（按世界时辰），展示页取 <c>--light</c>，缺省白天。</summary>
    protected SceneTime Light => Driver?.Light ?? DevCapture.Light ?? SceneTime.Day;

    /// <summary>当前的整体调色（<see cref="TintWorld"/>）；发光件用 <see cref="SceneTimes.Unlit"/> 抵消它。</summary>
    protected Color WorldTint { get; private set; } = Colors.White;

    /// <summary>
    /// 整体调色：乘在贴地层、排序层与悬空层上，交互菱形与 HUD 不受影响（河滩雨后偏冷、旧渡与城镇黄昏压暖、夜里压暗偏蓝）。
    /// </summary>
    protected void TintWorld(Color tint)
    {
        WorldTint = tint;
        GroundLayer.Modulate = tint;
        _sorted.Modulate = tint;
        OverheadLayer.Modulate = tint;
    }

    /// <summary>镜头可到范围的投影外框（<see cref="CameraArea"/> 或世界范围四角），光色层按此铺满。</summary>
    protected Rect2 ViewArea
    {
        get
        {
            if (CameraArea is { } area)
            {
                return area;
            }

            var b = Bounds;
            var box = new Rect2(TownView.P(b.Position), Vector2.Zero);
            foreach (var c in new[] { TownView.P(b.End), TownView.P(b.Position.X, b.End.Y), TownView.P(b.End.X, b.Position.Y) }) box = box.Expand(c);
            return box;
        }
    }

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
        Vector2 hero;
        var followers = new List<(FollowerLook Look, Vector2 At)>();
        if (Driver is { } driver)
        {
            (hero, var back, _zoom) = driver.Start;
            foreach (var (look, i) in driver.Followers.Select((l, i) => (l, i)))
            {
                var at = hero + back * FollowGap * (i + 1);
                followers.Add((look, InWalkArea(at) ? at : hero));
            }
        }
        else
        {
            (hero, var lu, _zoom) = Start(arrival);
            followers.Add((new FollowerLook("figure.lu_qinghe", FigureLook.Boatwoman, UiPalette.Trim), lu));
        }

        // 足迹自最远的同行者排到主角，同行者一开始就站在足迹上。
        foreach (var (_, at) in Enumerable.Reverse(followers))
        {
            _trail.Add(at);
        }

        _trail.Add(hero);
        _zoom = DevZoom ?? _zoom;

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

        foreach (var (look, at) in followers)
        {
            AddFollower(look, at);
        }

        Hero = new WalkerFigure { Tone = UiPalette.Accent.Lightened(0.1f) };
        Hero.Place(hero, StepZ(hero));
        _sorted.AddChild(Hero);

        _markers = new Node2D { ZIndex = 1000 };
        _world.AddChild(_markers);
        RefreshInteractions();
        RefreshActors();

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

    // ── 游戏模式：随世界状态刷新 ──────────────────────────

    /// <summary>按当前交互点重建头顶菱形（游戏模式下每次提交世界状态后调用）。</summary>
    public void RefreshInteractions()
    {
        foreach (var (_, marker) in _interactions)
        {
            marker.QueueFree();
        }

        _interactions.Clear();
        _near = null;
        foreach (var item in Driver?.Interactions ?? Interactions)
        {
            // 交互点不在场景里画常驻菱形（2026-10-04 用户：太显眼、降低探索难度），靠近时只出底部“E + 动作 + 对象”提示；
            // 节点仍保留为不可见的锚点，供鼠标点选与自动走查定位。
            var marker = new InteractMarker { Position = TownView.P(item.Position, StepZ(item.Position) + item.MarkerHeight), Visible = false };
            _markers.AddChild(marker);
            _interactions.Add((item, marker));
        }

        if (_prompt is not null)
        {
            _prompt.Visible = false;
        }

        Driver?.NearChanged(null);
    }

    /// <summary>按驱动方给出的站位人物重建（剧情人物出场、离场）。展示页没有站位人物。</summary>
    public void RefreshActors()
    {
        foreach (var actor in _actors)
        {
            _pieces.Remove(actor);
            actor.QueueFree();
        }

        _actors.Clear();
        foreach (var a in Driver?.Actors ?? [])
        {
            var figure = new WalkerFigure { Look = a.Look.Look, Tone = a.Look.Tone, ArtId = a.Look.ArtId, Occluder = false, Facing = a.Facing };
            figure.Place(a.At, StepZ(a.At));
            _sorted.AddChild(figure);
            _pieces.Add(figure);
            _actors.Add(figure);
        }
    }

    /// <summary>同行者变动（入队、离队）后重建跟随者，沿足迹从主角身后排起。</summary>
    public void RefreshFollowers()
    {
        foreach (var f in _followers)
        {
            f.Figure.QueueFree();
        }

        _followers.Clear();
        foreach (var look in Driver?.Followers ?? [])
        {
            AddFollower(look, TrailPoint(FollowGap * (_followers.Count + 1)));
        }
    }

    /// <summary>把主角放到某处（自动走查、剧情换位）；同行者紧随其后，镜头直接跳过去。</summary>
    public void PlaceHero(Vector2 ground)
    {
        if (!Walkable(ground))
        {
            ground = Hero.Ground;
        }

        Hero.Place(ground, StepZ(ground));
        _trail.Clear();
        _trail.Add(ground);
        foreach (var f in _followers)
        {
            f.Figure.Place(ground, StepZ(ground));
        }

        _cameraPlaced = false;
    }

    private void AddFollower(FollowerLook look, Vector2 at)
    {
        var figure = new WalkerFigure { Tone = look.Tone, Look = look.Look, ArtId = look.ArtId };
        figure.Place(at, StepZ(at));
        _sorted.AddChild(figure);
        _followers.Add(new Follower(figure));
    }

    // ── HUD ──────────────────────────────────────────────

    private void BuildHud()
    {
        var region = Driver?.Region ?? PlaceInfo.Region;
        _mini = CreateMiniMap(() => (Hero.Ground, _heading), () => CurrentGoal?.Ground);
        AddHud(ExploreHudKit.MiniMapFrame(_mini, region));
        if (Driver is null)
        {
            var (_, name, time) = PlaceInfo;
            AddChild(ExploreHudKit.Place(region, name, SceneTimes.Label(Light, time)));
            AddChild(Tracker());
            AddChild(ExploreHudKit.Party());
            AddChild(ExploreHudKit.Shortcuts(("WASD", "行走"), ("Shift", "快走"), ("E", "交互"), ("滚轮", "缩放"), ("M", "地图"), ("Esc", "返回标题")));
        }

        _prompt = new InteractPrompt { Visible = false };
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.AddChild(_prompt);
        AddHud(Ui.Place(center, 0.5f, 1, -300, -250, 300, -180));

        var caption = Driver?.Caption ?? Caption;
        if (caption.Length > 0)
        {
            var tag = Ui.Panel(UiTheme.GlassPanel, Ui.Text(caption, UiTheme.DarkMutedLabel, 16));
            var tagBox = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
            tagBox.AddChild(tag);
            AddHud(Ui.Place(tagBox, 0.5f, 1, -420, -150, 420, -106));
        }

        if (Driver is not null || Goal is not null)
        {
            _pointer = new GoalPointer();
            AddChild(_pointer);
        }

        _toasts = ToastColumn.Placed(this);
    }

    private void AddHud(Control control)
    {
        AddChild(control);
        _hud.Add(control);
    }

    /// <summary>布景自带的 HUD（小地图、交互提示、说明条）；对话进行时由驱动方隐去，让出对话层的版位。</summary>
    public bool HudVisible
    {
        set
        {
            foreach (var c in _hud)
            {
                c.Visible = value;
            }
        }
    }

    /// <summary>左侧目标追踪；野外等不在第一章的布景换成本地的样例任务。</summary>
    protected virtual Control Tracker() => ExploreHudKit.Tracker();

    /// <summary>暂停上方通知（对话、菜单打开时隐去并停住计时，关闭后继续）。</summary>
    public bool ToastsPaused
    {
        set => _toasts.Paused = value;
    }

    /// <summary>在上方通知栏推一条通知（见闻、物品……）。</summary>
    public void Toast(string kind, string text, string where) => _toasts.Push(kind, text, where);

    // ── 输入 ─────────────────────────────────────────────

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                if (ClickWalk(click.Position))
                {
                    GetViewport().SetInputAsHandled();
                }

                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                _zoom = Mathf.Clamp(_zoom * (wheel.ButtonIndex == MouseButton.WheelUp ? 1.05f : 1 / 1.05f), MinZoom, MaxZoom);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey when KeyBindings.Pressed(@event, "interact") && Driver is { } driver:
                if (_near is { } target && !driver.InputLocked)
                {
                    driver.Interact(target);
                    GetViewport().SetInputAsHandled();
                }

                break;
            case InputEventKey when KeyBindings.Pressed(@event, "interact") && _near is { } near:
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
            case InputEventKey { Pressed: true, Echo: false } key when Driver is null && Pages.TryGetValue(key.Keycode, out var page):
                AppHost.Instance.Router.GoTo(page);
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    // ── 每帧：行走、跟随、排序、遮挡、镜头 ────────────────

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        StepRoute(delta);
        if (!Scripted(Hero))
        {
            MoveHero(dt);
        }

        for (var i = 0; i < _followers.Count; i++)
        {
            if (!Scripted(_followers[i].Figure))
            {
                FollowTrail(_followers[i], FollowGap * (i + 1), dt);
            }
        }

        DepthSort.Apply(_pieces.Cast<ISortable>().Append(Hero).Concat(_followers.Select(f => f.Figure)).ToList());
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
        if (Driver is not { InputLocked: true })
        {
            // 行走键可在设置里改（KeyBindings）；方向键固定可用。
            if (KeyBindings.Held("move_left") || Input.IsKeyPressed(Key.Left)) input.X -= 1;
            if (KeyBindings.Held("move_right") || Input.IsKeyPressed(Key.Right)) input.X += 1;
            if (KeyBindings.Held("move_up") || Input.IsKeyPressed(Key.Up)) input.Y -= 1;
            if (KeyBindings.Held("move_down") || Input.IsKeyPressed(Key.Down)) input.Y += 1;
        }

        var devRun = false;
        if (DevDrive.Count > 0 && Driver is not { InputLocked: true })
        {
            var (dir, run, seconds) = DevDrive.Peek();
            if (_driveLeft < 0) _driveLeft = seconds;
            input = dir;
            devRun = run;
            _driveLeft -= dt;
            if (_driveLeft <= 0)
            {
                DevDrive.Dequeue();
                _driveLeft = -1;
            }
        }

        if (input != Vector2.Zero && _route is not null)
        {
            // 按方向键接管：取消鼠标点地的自动行走。
            StopAutoWalk();
        }

        var bot = Driver is not { InputLocked: true } ? BotGround : null;
        var moving = input != Vector2.Zero || bot is { } b && b != Vector2.Zero;
        if (moving)
        {
            // 按屏幕方向走：换算成地面方向后按世界速度移动；分轴判定以便沿墙滑行。
            var dir = bot is { } g && g != Vector2.Zero ? g.Normalized() : TownView.GroundFromScreen(input).Normalized();
            if (bot is not null)
            {
                input = new Vector2(TownView.ScreenX(dir), 0);
            }

            var speed = KeyBindings.Held("run") || devRun || bot is not null ? RunSpeed : WalkSpeed;
            var step = dir * speed * dt;
            var pos = Hero.Ground;
            if (Walkable(pos + new Vector2(step.X, 0))) pos.X += step.X;
            if (Walkable(pos + new Vector2(0, step.Y))) pos.Y += step.Y;
            if (input.X != 0) Hero.Facing = input.X > 0 ? 1 : -1;
            Hero.TurnToward(dir);
            _heading = dir;
            // 快走换跑步帧；跑步一步跨得更远（步相按更长的步幅推进），步频只比走路略快。
            Hero.Running = speed >= RunSpeed;
            Hero.Phase += (pos - Hero.Ground).Length() / (Hero.Running ? 44 : 32);
            _stride += (pos - Hero.Ground).Length();
            if (_stride > speed * 0.42f)
            {
                _stride = 0;
                AppHost.Instance.Sound.Play("step." + StepSurface, -13, 0.07f);
            }

            Hero.Place(pos, StepZ(pos));
            if (_trail[^1].DistanceTo(pos) > 8)
            {
                _trail.Add(pos);
                if (_trail.Count > 400) _trail.RemoveAt(0);
            }
        }

        if (moving || Hero.Moving)
        {
            Hero.Moving = moving;
            Hero.QueueRedraw();
        }
    }

    /// <summary>
    /// 同行者追向足迹上距主角正好 back 的点（沿足迹折线连续插值，目标随主角平滑移动，不按足迹点跳格），
    /// 速度随落后距离平滑增减；停步有短暂缓冲、朝向按实际位移换，避免走停与左右在相邻帧间来回切换造成抖动。
    /// 不做碰撞（足迹本身都在可走区内）。多名同行者按 FollowGap 的倍数依次排开。
    /// </summary>
    private void FollowTrail(Follower f, float back, float dt)
    {
        var walker = f.Figure;
        var target = TrailPoint(back);
        var gap = target - walker.Ground;
        var dist = gap.Length();
        var step = dist < 1 ? 0 : Mathf.Min(dist, Mathf.Min(RunSpeed * 1.1f, dist * 8) * dt);
        if (step > 0)
        {
            var move = gap / dist * step;
            var pos = walker.Ground + move;
            // 追赶速度超过走路与快走的中值换跑步帧，回落到走路速度以下才换回（回差避免来回切换）。
            var speed = step / Mathf.Max(dt, 1e-4f);
            if (speed > (WalkSpeed + RunSpeed) / 2) walker.Running = true;
            else if (speed < WalkSpeed * 1.05f) walker.Running = false;
            walker.Phase += step / (walker.Running ? 42 : 30);
            var sx = TownView.ScreenX(move);
            if (Mathf.Abs(sx) > step * 0.3f) walker.Facing = sx > 0 ? 1 : -1;
            walker.TurnToward(move);
            walker.Place(pos, StepZ(pos));
        }

        // 每帧位移低于约 60/秒 才算停步，且须连续 0.15 秒，避免起伏动画在相邻帧间开关。
        f.Idle = step > 60 * dt ? 0 : f.Idle + dt;
        var moving = f.Idle < 0.15f;
        if (moving || walker.Moving)
        {
            walker.Moving = moving;
            walker.QueueRedraw();
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

    /// <summary>摆放核对：某处能否站人（可走区内且不压在物件占地上）。</summary>
    public bool CanStand(Vector2 p) => Walkable(p);

    /// <summary>摆放核对：交互点周围交互距离内有没有能站人的地方（走得到才按得到 E）。</summary>
    public bool CanReach(Vector2 target)
    {
        for (var r = 0f; r < InteractRange - 10; r += 20)
        {
            for (var a = 0; a < 16; a++)
            {
                var p = target + Vector2.FromAngle(a * Mathf.Tau / 16) * r;
                if (Walkable(p))
                {
                    return true;
                }
            }
        }

        return false;
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
        if (Driver is not { InputLocked: true })
        {
            foreach (var (data, _) in _interactions)
            {
                var d = data.Position.DistanceTo(Hero.Ground);
                if (d < best)
                {
                    best = d;
                    near = data;
                }
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
            _prompt!.Visible = false;
        }
        else
        {
            _prompt!.Show(near.Verb, near.Target);
        }

        Driver?.NearChanged(near);
    }

    /// <summary>排在行人之前（更近镜头）的物件，其画出的面盖住行人头胸时淡到 0.4。</summary>
    private void UpdateOcclusion(float dt)
    {
        var walkers = _followers.Select(f => f.Figure).Prepend(Hero).ToArray();

        // 演出镜头对准的地方（物件特写、人物走位）同样不许被近处的树、屋挡住。
        var focus = CameraFocus is { } f ? TownView.P(f.Ground, StepZ(f.Ground)) : (Vector2?)null;
        foreach (var piece in _pieces)
        {
            var hidden = piece.Occluder && (walkers.Any(w => piece.ZIndex > w.ZIndex && piece.Covers(w))
                || focus is { } at && piece.ScreenBox.End.Y > at.Y + 30 && piece.ScreenBox.Grow(-piece.ScreenBox.Size.X * 0.12f) is var box
                    && (box.HasPoint(at + new Vector2(0, -60)) || box.HasPoint(at + new Vector2(0, -160))));
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
        // 演出对准某处时镜头追向那里（剧情演出，见 ExploreStage.Cues），否则跟随主角。
        var target = CameraFocus is { } focus ? TownView.P(focus.Ground, StepZ(focus.Ground) + focus.Height) : Hero.Position + new Vector2(0, -80);
        var zoom = ViewZoom(dt);
        var view = Size / zoom;
        var b = _cameraBounds;
        target.X = view.X >= b.Size.X ? b.GetCenter().X : Mathf.Clamp(target.X, b.Position.X + view.X / 2, b.End.X - view.X / 2);
        target.Y = view.Y >= b.Size.Y ? b.GetCenter().Y : Mathf.Clamp(target.Y, b.Position.Y + view.Y / 2, b.End.Y - view.Y / 2);
        _camera = _cameraPlaced && Motion.Enabled ? _camera.Lerp(target, 1 - Mathf.Exp(-CameraRate * dt)) : target;
        _cameraPlaced = true;
        _world.Scale = new Vector2(zoom, zoom);
        _world.Position = (Size / 2 - _camera * zoom).Round();
    }

    /// <summary>目标在画面外时，箭头停在屏幕边缘（避开四角 HUD）指向它。</summary>
    private void UpdatePointer()
    {
        if (_pointer is null)
        {
            return;
        }

        if (CurrentGoal is not { } goal || Driver is { InputLocked: true })
        {
            _pointer.Visible = false;
            return;
        }

        _pointer.Label = goal.Label;
        var screen = _world.Position + TownView.P(goal.Ground, StepZ(goal.Ground) + goal.Height) * _world.Scale.X;
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

    private sealed class Follower(WalkerFigure figure)
    {
        public WalkerFigure Figure { get; } = figure;

        public float Idle { get; set; } = 1;
    }
}

/// <summary>探索形象的外观：AI 形象 ID、程序化占位装束与主色（没有 AI 形象的人物画占位剪影）。</summary>
public sealed record FollowerLook(string ArtId, FigureLook Look, Color Tone);

/// <summary>站在布景里的剧情人物（事件参与者等），不跟随、不挡路。</summary>
public sealed record ExploreActor(FollowerLook Look, Vector2 At, int Facing);

/// <summary>
/// 游戏模式的驱动方（M2）：<see cref="ExploreStage"/> 只管行走、排序、遮挡与镜头，
/// 其余由驱动方按已提交的世界状态给出，交互经驱动方转成 <c>GameSession</c> 的命令。
/// </summary>
public interface IExploreDriver
{
    /// <summary>主角落点、同行者排开的方向（世界平面单位向量）与缩放。</summary>
    (Vector2 Hero, Vector2 Back, float Zoom) Start { get; }

    /// <summary>小地图下方的地区名。</summary>
    string Region { get; }

    IReadOnlyList<FollowerLook> Followers { get; }

    IReadOnlyList<ExploreActor> Actors { get; }

    IReadOnlyList<TownInteraction> Interactions { get; }

    (Vector2 Ground, float Height, string Label)? Goal { get; }

    /// <summary>光色时段（按世界时辰）。</summary>
    SceneTime Light { get; }

    /// <summary>底部说明条（布景占位说明）；空串不显示。</summary>
    string Caption { get; }

    /// <summary>对话、换图或菜单进行中：主角不走、E 不响应。</summary>
    bool InputLocked { get; }

    void Interact(TownInteraction item);

    void NearChanged(TownInteraction? item);
}
