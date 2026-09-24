using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 城镇 / 街道探索展示页“芦湾河街”（M0-03 江南水镇样板的第一步：人定布局）。
/// 整个街区是一套世界坐标（x 东、y 南、z 上），经 <see cref="TownView"/> 的正交斜视投影画出：
/// 地面由 town_ground 着色器按反投影的世界坐标铺石板、河水与草地，房屋、廊棚、桥栏、摊位等由面搭成（按法线剔除与分阶明暗），
/// 人物与树为立着的精灵；前后次序按占地拓扑排序，行人被挡住时挡住的那件淡出。
/// WASD / 方向键按屏幕方向行走（Shift 快走），同行者陆青禾沿足迹跟随；靠近可交互物出现“E + 动作 + 对象”，
/// E 只推一条通知，不写存档；滚轮缩放 0.85–1.15（架构文档 10.2）。
/// HUD 与探索 HUD 页共用 <see cref="ExploreHudKit"/>。地面与建筑为程序化占位，正式件按架构文档 10.3 由 AI 出件替换。
/// 截图参数 <c>--tab</c>：0 旧渡石痕旁（交互提示）、1 南岸街被屋身遮挡、2 客栈门前、3 廊棚下、4 缩到 0.85 看平桥一带。
/// </summary>
public partial class ExploreTownPreview : Control
{
    private const float MinZoom = 0.85f;
    private const float MaxZoom = 1.15f;
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
    private WalkerFigure _hero = null!;
    private WalkerFigure _lu = null!;
    private InteractPrompt _prompt = null!;
    private ToastColumn _toasts = null!;
    private TownMiniMap _mini = null!;
    private TownInteraction? _near;
    private Vector2 _camera;
    private float _zoom = 1;
    private bool _cameraPlaced;
    private Rect2 _cameraBounds;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ClipContents = true;
        var (hero, lu, zoom) = DevCapture.Tab switch
        {
            1 => (new Vector2(1900, 2950), new Vector2(1780, 2930), 1f),
            2 => (new Vector2(3760, 1440), new Vector2(3640, 1470), 1f),
            3 => (new Vector2(4050, 1700), new Vector2(3930, 1690), 1f),
            4 => (new Vector2(2900, 2300), new Vector2(2900, 2180), MinZoom),
            _ => (TownSamples.Spawn, TownSamples.Spawn + new Vector2(-120, -40), 1f),
        };
        _zoom = zoom;
        _trail.Add(lu);
        _trail.Add(hero);

        // 布景内部按 ZIndex 排前后；整体压到 -4000，保证在 HUD（ZIndex 0）之下。
        _world = new Node2D { ZIndex = -4000 };
        AddChild(_world);
        BuildWorld(hero, lu);
        BuildHud();
    }

    // ── 世界 ─────────────────────────────────────────────

    private void BuildWorld(Vector2 hero, Vector2 lu)
    {
        foreach (var child in _world.GetChildren())
        {
            _world.RemoveChild(child);
            child.QueueFree();
        }

        _pieces.Clear();
        _interactions.Clear();
        _blockers.Clear();
        _near = null;
        _cameraPlaced = false;

        var ground = new Node2D { ZIndex = -10 };
        _world.AddChild(ground);
        BuildGround(ground);
        ground.AddChild(new TownGroundDetail());

        var sorted = new Node2D();
        _world.AddChild(sorted);
        foreach (var house in TownSamples.Houses) Add(new TownHouseNode(house));
        foreach (var part in TownCorridorPart.Build(TownSamples.Corridor)) Add(part);
        foreach (var tree in TownSamples.Trees) Add(new TownTreeNode(tree));
        foreach (var prop in TownSamples.Props) Add(new TownPropNode(prop));
        var bridge = TownSamples.Bridge;
        Add(new TownRailNode(bridge.Position.X + 12, bridge));
        Add(new TownRailNode(bridge.End.X - 12, bridge));

        _lu = new WalkerFigure { Tone = UiPalette.Trim, Boatwoman = true };
        _hero = new WalkerFigure { Tone = UiPalette.Accent.Lightened(0.1f) };
        _lu.Place(lu, StepZ(lu));
        _hero.Place(hero, StepZ(hero));
        sorted.AddChild(_lu);
        sorted.AddChild(_hero);

        var markers = new Node2D { ZIndex = 1000 };
        _world.AddChild(markers);
        foreach (var item in TownSamples.Interactions)
        {
            var marker = new InteractMarker { Position = TownView.P(item.Position, item.MarkerHeight) };
            markers.AddChild(marker);
            _interactions.Add((item, marker));
        }

        var b = TownSamples.Bounds;
        var corners = new[] { TownView.P(b.Position), TownView.P(b.End), TownView.P(b.Position.X, b.End.Y), TownView.P(b.End.X, b.Position.Y) };
        _cameraBounds = new Rect2(corners[0], Vector2.Zero);
        foreach (var c in corners) _cameraBounds = _cameraBounds.Expand(c);

        return;

        void Add(TownPiece piece)
        {
            sorted.AddChild(piece);
            _pieces.Add(piece);
            if (piece is TownPropNode { IsBoat: true } || piece.Overhead)
            {
                return;
            }

            _blockers.Add(piece.Foot);
        }
    }

    /// <summary>地面：先画低处的河面，再画两岸（同一平面 z = 0），岸线以外一律延伸成草地。</summary>
    private static void BuildGround(Node2D parent)
    {
        var shader = GD.Load<Shader>("res://assets/shaders/town_ground.gdshader");
        void Ground(int kind, IEnumerable<Vector2> worldPolygon, float z = 0)
        {
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("kind", kind);
            material.SetShaderParameter("yaw", Mathf.DegToRad(TownView.Yaw));
            material.SetShaderParameter("pitch", Mathf.DegToRad(TownView.Pitch));
            material.SetShaderParameter("scale", TownView.Scale);
            material.SetShaderParameter("plane_z", z);
            parent.AddChild(new Polygon2D { Polygon = worldPolygon.Select(p => TownView.P(p, z)).ToArray(), Material = material });
        }

        const float far = 3000;
        var b = TownSamples.Bounds;
        var (west, east) = (b.Position.X - far, b.End.X + far);
        Ground(2, Rect(new Rect2(west, 1600, east - west, 1400)), TownSamples.WaterLevel);
        Ground(0, [new(west, -far), new(east, -far), .. TownLayout.Edge(TownSamples.NorthBank, east, west)]);
        Ground(0, [.. TownLayout.Edge(TownSamples.SouthBank, west, east), new(east, b.End.Y + far), new(west, b.End.Y + far)]);
        Ground(1, TownLayout.Street);
        Ground(1, Rect(TownSamples.Alley));
        Ground(1, Rect(TownSamples.Plaza));
        Ground(3, TownLayout.SouthBankStrip);
        Ground(1, TownLayout.SouthWalk);
    }

    private static Vector2[] Rect(Rect2 r) => [r.Position, new(r.End.X, r.Position.Y), r.End, new(r.Position.X, r.End.Y)];

    // ── HUD ──────────────────────────────────────────────

    private void BuildHud()
    {
        AddChild(ExploreHudKit.Place("芦湾", "芦湾河街", "申时　·　雨后初晴"));
        _mini = new TownMiniMap { Hero = () => (_hero.Ground, _heading) };
        AddChild(ExploreHudKit.MiniMapFrame(_mini, "芦湾"));
        AddChild(ExploreHudKit.Tracker());
        AddChild(ExploreHudKit.Party());
        AddChild(ExploreHudKit.Shortcuts(("WASD", "行走"), ("Shift", "快走"), ("E", "交互"), ("滚轮", "缩放"), ("M", "地图"), ("Esc", "返回标题")));

        _prompt = new InteractPrompt { Visible = false };
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.AddChild(_prompt);
        AddChild(Ui.Place(center, 0.5f, 1, -300, -250, 300, -180));

        var tag = Ui.Panel(UiTheme.GlassPanel, Ui.Text("斜 45° 视角（街道自左下向右上）布局样板：地面、房屋与人物为程序化占位，正式件待 AI 出件（M0-03）", UiTheme.DarkMutedLabel, 16));
        var tagBox = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        tagBox.AddChild(tag);
        AddChild(Ui.Place(tagBox, 0.5f, 1, -420, -150, 420, -106));

        _toasts = ToastColumn.Placed(this);
    }

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
                _toasts.Push(near.Kind, near.Text, near.Where);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Pressed: true, Echo: false } key when Pages.TryGetValue(key.Keycode, out var page):
                AppHost.Instance.Router.GoTo(page);
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    // ── 每帧：行走、跟随、排序、遮挡、镜头 ────────────────

    private Vector2 _heading = new(1, 0);

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        MoveHero(dt);
        FollowTrail(dt);
        DepthSort.Apply(_pieces.Cast<ISortable>().Append(_hero).Append(_lu).ToList());
        UpdateInteraction();
        UpdateOcclusion(dt);
        UpdateCamera(dt);

        var t = Time.GetTicksMsec() / 1000f;
        foreach (var piece in _pieces)
        {
            if (piece is TownPropNode { IsBoat: true } boat)
            {
                boat.Bob(Motion.Enabled ? Mathf.Sin(t * 1.3f + boat.Foot.Position.X) * 3 : 0);
            }
        }

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
            var pos = _hero.Ground;
            if (Walkable(pos + new Vector2(step.X, 0))) pos.X += step.X;
            if (Walkable(pos + new Vector2(0, step.Y))) pos.Y += step.Y;
            if (input.X != 0) _hero.Facing = input.X > 0 ? 1 : -1;
            _heading = dir;
            _hero.Phase += (pos - _hero.Ground).Length() / 32;
            _hero.Place(pos, StepZ(pos));
            if (_trail[^1].DistanceTo(pos) > 8)
            {
                _trail.Add(pos);
                if (_trail.Count > 200) _trail.RemoveAt(0);
            }
        }

        if (moving || _hero.Moving)
        {
            _hero.Moving = moving;
            _hero.QueueRedraw();
        }
    }

    /// <summary>同行者走向足迹上与主角相距约 FollowGap 的点，不做碰撞（足迹本身都在可走区内）。</summary>
    private void FollowTrail(float dt)
    {
        var target = _trail[0];
        var walked = 0f;
        for (var i = _trail.Count - 1; i > 0; i--)
        {
            walked += _trail[i].DistanceTo(_trail[i - 1]);
            if (walked >= FollowGap)
            {
                target = _trail[i - 1];
                break;
            }
        }

        var gap = target - _lu.Ground;
        var moving = gap.Length() > 4 && _hero.Ground.DistanceTo(_lu.Ground) > FollowGap * 0.8f;
        if (moving)
        {
            var step = Mathf.Min(gap.Length(), RunSpeed * dt);
            var pos = _lu.Ground + gap.Normalized() * step;
            _lu.Phase += step / 30;
            var sx = TownView.ScreenX(gap);
            if (Mathf.Abs(sx) > 2) _lu.Facing = sx > 0 ? 1 : -1;
            _lu.Place(pos, StepZ(pos));
        }

        if (moving || _lu.Moving)
        {
            _lu.Moving = moving;
            _lu.QueueRedraw();
        }
    }

    /// <summary>渡口石阶上的高度：自岸沿向河里逐级降到接近水面。</summary>
    private static float StepZ(Vector2 p)
    {
        var steps = TownSamples.FerrySteps;
        if (!steps.HasPoint(p))
        {
            return 0;
        }

        var bank = TownSamples.NorthBank(p.X);
        return -Mathf.Clamp((p.Y - bank) / (steps.End.Y - bank), 0, 1) * (-TownSamples.WaterLevel - 16);
    }

    private bool Walkable(Vector2 p)
    {
        Span<Vector2> probes = [p, p + new Vector2(FootRadius, 0), p - new Vector2(FootRadius, 0), p + new Vector2(0, FootRadius), p - new Vector2(0, FootRadius)];
        foreach (var q in probes)
        {
            if (!TownLayout.InWalkArea(q))
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
            var d = data.Position.DistanceTo(_hero.Ground);
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
        var walkers = new[] { _hero, _lu };
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
        var target = _hero.Position + new Vector2(0, -80);
        var view = Size / _zoom;
        var b = _cameraBounds;
        target.X = view.X >= b.Size.X ? b.GetCenter().X : Mathf.Clamp(target.X, b.Position.X + view.X / 2, b.End.X - view.X / 2);
        target.Y = view.Y >= b.Size.Y ? b.GetCenter().Y : Mathf.Clamp(target.Y, b.Position.Y + view.Y / 2, b.End.Y - view.Y / 2);
        _camera = _cameraPlaced && Motion.Enabled ? _camera.Lerp(target, 1 - Mathf.Exp(-7 * dt)) : target;
        _cameraPlaced = true;
        _world.Scale = new Vector2(_zoom, _zoom);
        _world.Position = (Size / 2 - _camera * _zoom).Round();
    }
}

/// <summary>布局的派生几何（世界平面坐标）：地面多边形与可走区判定，地面、碰撞与小地图共用一份。</summary>
public static class TownLayout
{
    private const float Step = 60;

    public static readonly Vector2[] Street =
        [.. Edge(_ => TownSamples.StreetNorth, 0, TownSamples.Bounds.End.X), .. Edge(TownSamples.NorthBank, TownSamples.Bounds.End.X, 0)];

    public static readonly Vector2[] SouthWalk =
        [.. Edge(x => TownSamples.SouthBank(x) + 20, 0, TownSamples.Bounds.End.X), .. Edge(_ => TownSamples.SouthWalkEnd, TownSamples.Bounds.End.X, 0)];

    public static readonly Vector2[] SouthBankStrip =
        [.. Edge(TownSamples.SouthBank, 0, TownSamples.Bounds.End.X), .. Edge(x => TownSamples.SouthBank(x) + 20, TownSamples.Bounds.End.X, 0)];

    public static readonly Vector2[] Canal =
        [.. Edge(TownSamples.NorthBank, 0, TownSamples.Bounds.End.X), .. Edge(TownSamples.SouthBank, TownSamples.Bounds.End.X, 0)];

    /// <summary>沿一条随 x 变化的边取点，from → to（可逆向）。</summary>
    public static IEnumerable<Vector2> Edge(Func<float, float> y, float from, float to)
    {
        var dir = Mathf.Sign(to - from);
        for (var x = from; dir > 0 ? x < to : x > to; x += dir * Step) yield return new Vector2(x, y(x));
        yield return new Vector2(to, y(to));
    }

    public static bool InWalkArea(Vector2 q)
    {
        var b = TownSamples.Bounds;
        if (q.X < b.Position.X + 20 || q.X > b.End.X - 20)
        {
            return false;
        }

        if (q.Y >= TownSamples.StreetNorth + 20 && q.Y <= TownSamples.NorthBank(q.X) - 24)
        {
            return true;
        }

        if (TownSamples.Alley.Grow(-20).HasPoint(q) || TownSamples.Plaza.Grow(-20).HasPoint(q) || TownSamples.FerrySteps.Grow(-16).HasPoint(q))
        {
            return true;
        }

        var bridge = TownSamples.Bridge;
        if (q.X > bridge.Position.X + 34 && q.X < bridge.End.X - 34 && q.Y > bridge.Position.Y - 60 && q.Y < bridge.End.Y + 60)
        {
            return true;
        }

        return q.Y >= TownSamples.SouthBank(q.X) + 30 && q.Y <= TownSamples.SouthWalkEnd - 20;
    }
}

/// <summary>贴地的立体细节，画在排序层之下：北岸条石驳岸立面与压顶、渡口石阶、平桥桥面与桥墩、系船桩。</summary>
public partial class TownGroundDetail : Node2D
{
    private readonly List<Face> _faces = [];

    public TownGroundDetail()
    {
        const float step = 60;
        var water = TownSamples.WaterLevel;
        var b = TownSamples.Bounds;
        var run = 0f;
        for (var x = b.Position.X - 3000; x < b.End.X + 3000; x += step)
        {
            var a = new Vector3(x, TownSamples.NorthBank(x), 0);
            var c = new Vector3(x + step, TownSamples.NorthBank(x + step), 0);
            var along = c - a;
            var offset = run;
            run += along.Length();
            _faces.Add(new Face
            {
                Points = [a, c, c + new Vector3(0, 0, water), a + new Vector3(0, 0, water)],
                Normal = new Vector3(-along.Y, along.X, 0).Normalized(), Color = Cel.Stone, Outline = 0,
                Local = TownView.Local(a, along.Normalized(), TownView.Below),
                Decal = ci => Embankment(ci, along.Length(), -water, offset),
            });
        }

        // 渡口石阶：五级，自岸沿逐级下到水边。
        var steps = TownSamples.FerrySteps;
        var bank = TownSamples.NorthBank(steps.GetCenter().X) - 10;
        const int count = 5;
        var depth = (steps.End.Y - bank) / count;
        for (var i = 0; i < count; i++)
        {
            var top = -(i + 1) * (-water - 16) / count;
            _faces.AddRange(Solid.Box(new Vector3(steps.Position.X, bank + i * depth, water), new Vector3(steps.End.X, steps.End.Y, top + 16), Cel.StoneLight, 1.6f));
        }

        // 平桥：桥面条石、东侧桥身、中段桥墩。
        var bridge = TownSamples.Bridge;
        _faces.AddRange(Solid.Box(new Vector3(bridge.Position.X, bridge.Position.Y + 280, water), new Vector3(bridge.End.X, bridge.Position.Y + 340, -30), Cel.Stone, 1.6f));
        _faces.AddRange(Solid.Box(new Vector3(bridge.Position.X, bridge.Position.Y + 640, water), new Vector3(bridge.End.X, bridge.Position.Y + 700, -30), Cel.Stone, 1.6f));
        var deck = Solid.Box(new Vector3(bridge.Position.X, bridge.Position.Y, -30), new Vector3(bridge.End.X, bridge.End.Y, 2), Cel.StoneLight, 2f);
        var topFace = deck[^1];
        deck[^1] = new Face
        {
            Points = topFace.Points, Normal = topFace.Normal, Color = Cel.StoneLight,
            Local = TownView.Local(new Vector3(bridge.Position.X, bridge.Position.Y, 2), Vector3.Right, new Vector3(0, 1, 0)),
            Decal = ci =>
            {
                for (var y = 100f; y < bridge.Size.Y; y += 100)
                {
                    ci.DrawLine(new Vector2(0, y), new Vector2(bridge.Size.X, y), Cel.Ink with { A = 0.45f }, 2f);
                }

                ci.DrawLine(new Vector2(bridge.Size.X / 2, 0), new Vector2(bridge.Size.X / 2, bridge.Size.Y), Cel.Ink with { A = 0.3f }, 2f);
            },
        };
        _faces.AddRange(deck);

        // 系船桩。
        foreach (var x in new[] { 1580f, 1720f, 3500f, 4100f })
        {
            var y = TownSamples.NorthBank(x) - 18;
            _faces.AddRange(Solid.Box(new Vector3(x - 9, y - 9, 0), new Vector3(x + 9, y + 9, 36), Cel.Wood, 1.4f));
        }
    }

    private static void Embankment(CanvasItem ci, float width, float height, float offset)
    {
        // 条石：每层高 26，块长 90，逐层错缝；越近水线越暗，水线上一道苔带。
        var rows = (int)Mathf.Ceil(height / 26);
        for (var r = 0; r < rows; r++)
        {
            var y = r * 26f;
            var shift = Cel.Rand(r, 9) * 90;
            var tone = Cel.Stone.Darkened(0.06f * r);
            ci.DrawRect(new Rect2(0, y, width, 26), tone);
            ci.DrawLine(new Vector2(0, y + 1), new Vector2(width, y + 1), Cel.StoneLight with { A = 0.6f }, 2);
            for (var x = -Mathf.PosMod(offset + shift, 90); x < width; x += 90)
            {
                if (x > 0) ci.DrawLine(new Vector2(x, y), new Vector2(x, y + 26), Cel.Ink with { A = 0.5f }, 1.8f);
            }
        }

        ci.DrawRect(new Rect2(0, height - 16, width, 12), Cel.Leaf.Darkened(0.35f) with { A = 0.7f });
        ci.DrawLine(new Vector2(0, height - 2), new Vector2(width, height - 2), Colors.White with { A = 0.7f }, 3);
    }

    public override void _Draw()
    {
        foreach (var face in _faces)
        {
            face.Draw(this);
        }

        // 北岸压顶：街面边沿一道亮石与墨线。
        var coping = TownLayout.Edge(TownSamples.NorthBank, TownSamples.Bounds.Position.X - 3000, TownSamples.Bounds.End.X + 3000)
            .Select(p => TownView.P(p)).ToArray();
        DrawPolyline(coping, Cel.StoneLight, 8, true);
        DrawPolyline(coping, Cel.Ink, 2, true);
    }
}

/// <summary>小地图：平面图（上北），以主角为中心取 2400×2400 的窗口，画石板街、河、桥、屋舍；石青箭头为主角、泥金菱形为目标。</summary>
public partial class TownMiniMap : Control
{
    private const float Window = 2400;

    public Func<(Vector2 Position, Vector2 Heading)> Hero { get; init; } = () => (Vector2.Zero, Vector2.Right);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
    }

    public override void _Draw()
    {
        var (hero, heading) = Hero();
        var k = Size.X / Window;
        var b = TownSamples.Bounds;
        var origin = new Vector2(Mathf.Clamp(hero.X - Window / 2, b.Position.X, b.End.X - Window), Mathf.Clamp(hero.Y - Window / 2, b.Position.Y, b.End.Y - Window));
        DrawRect(new Rect2(Vector2.Zero, Size), Color.FromHtml("#6E9C74"));
        DrawSetTransform(-origin * k, 0, new Vector2(k, k));

        DrawColoredPolygon(TownLayout.Street, UiPalette.SurfaceShade);
        DrawRect(TownSamples.Alley, UiPalette.SurfaceShade);
        DrawRect(TownSamples.Plaza, UiPalette.SurfaceShade);
        DrawColoredPolygon(TownLayout.SouthWalk, UiPalette.SurfaceShade);
        DrawColoredPolygon(TownLayout.Canal, UiPalette.Trim.Lightened(0.15f));
        DrawRect(TownSamples.Bridge, UiPalette.Surface);
        foreach (var h in TownSamples.Houses)
        {
            DrawRect(new Rect2(h.X0, h.Y0, h.X1 - h.X0, h.Y1 - h.Y0), UiPalette.TextMuted with { A = 0.8f });
        }

        DrawRect(TownSamples.Corridor, UiPalette.TextMuted with { A = 0.45f });

        var g = TownSamples.Goal;
        var r = 9 / k;
        DrawColoredPolygon([g + new Vector2(0, -r), g + new Vector2(r, 0), g + new Vector2(0, r), g + new Vector2(-r, 0)], UiPalette.Gilt.Darkened(0.1f));
        var a = 11 / k;
        var f = heading.Normalized();
        var side = new Vector2(-f.Y, f.X);
        DrawColoredPolygon([hero + f * a * 1.2f, hero - f * a * 0.8f + side * a * 0.8f, hero - f * a * 0.3f, hero - f * a * 0.8f - side * a * 0.8f], UiPalette.Accent);
        DrawArc(hero, 18 / k, 0, Mathf.Tau, 32, UiPalette.Accent with { A = 0.5f }, 1.5f / k, true);

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        DrawString(UiFonts.Title, new Vector2(Size.X - 30, 28), "北", HorizontalAlignment.Left, -1, 20, UiPalette.Text);
    }
}
