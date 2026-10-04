using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 寻路与鼠标点地行走：在可走格（每格 <see cref="Cell"/>，格中心与相邻格中点都须站得住人）上广度优先找路。
/// 左键点地面：主角沿路走过去；点在交互菱形附近：走到交互范围内、且最近的交互点正是它的位置后自动交互。
/// 按方向键会立刻接管（取消自动行走）。自动走查（<c>--walk</c>）复用同一套寻路与跟随。
/// </summary>
public abstract partial class ExploreStage
{
    private const float Cell = 24;
    private const float ClickMarkerPick = 50;

    private bool[,]? _grid;
    private Vector2 _gridOrigin;
    private List<Vector2>? _route;
    private TownInteraction? _routeTarget;
    private bool _routeInteract;
    private Vector2 _routeStuckFrom;
    private double _routeStuckClock;

    /// <summary>正沿寻得的路自动行走。</summary>
    public bool AutoWalking => _route is { Count: > 0 };

    /// <summary>
    /// 沿路自动走到能触发 <paramref name="target"/> 的位置；<paramref name="interact"/> 为 true 时到达后自动交互
    /// （鼠标点交互点），为 false 时只走到（自动走查到达后自己注入 E）。走不到返回 false。
    /// </summary>
    public bool WalkToInteraction(TownInteraction target, bool interact)
    {
        var points = Driver?.Interactions ?? Interactions;
        var reach = InteractRange - 30;
        var path = FindPath(c => c.DistanceTo(target.Position) <= reach
            && points.MinBy(p => p.Position.DistanceTo(c))?.Id == target.Id);
        return Follow(path, target, interact);
    }

    /// <summary>沿路自动走到地面某点附近；走不到返回 false。</summary>
    public bool WalkToGround(Vector2 ground)
    {
        var path = FindPath(c => c.DistanceTo(ground) <= Cell * 1.5f);
        return Follow(path, null, false);
    }

    public void StopAutoWalk()
    {
        _route = null;
        _routeTarget = null;
        BotGround = null;
    }

    private bool Follow(List<Vector2>? path, TownInteraction? target, bool interact)
    {
        if (path is null)
        {
            return false;
        }

        _route = path;
        _routeTarget = target;
        _routeInteract = interact;
        _routeStuckFrom = Hero.Ground;
        _routeStuckClock = 0;
        return true;
    }

    /// <summary>每帧：沿路给出行走方向；被挡住 1.5 秒以上就重新寻路，到达后按需交互。</summary>
    private void StepRoute(double delta)
    {
        if (_route is null)
        {
            return;
        }

        var hero = Hero.Ground;
        while (_route.Count > 0 && hero.DistanceTo(_route[0]) < 14)
        {
            _route.RemoveAt(0);
        }

        if (_route.Count > 0)
        {
            BotGround = _route[0] - hero;
            _routeStuckClock += delta;
            if (_routeStuckClock > 1.5)
            {
                if (hero.DistanceTo(_routeStuckFrom) < 20)
                {
                    RouteStuck?.Invoke(hero, _route[0]);
                    var goal = _route[^1];
                    var retry = _routeTarget is { } t ? FindPath(c => c.DistanceTo(t.Position) <= InteractRange - 30) : FindPath(c => c.DistanceTo(goal) <= Cell * 1.5f);
                    if (retry is null || retry.Count == 0)
                    {
                        StopAutoWalk();
                        return;
                    }

                    _route = retry;
                }

                _routeStuckClock = 0;
                _routeStuckFrom = Hero.Ground;
            }

            return;
        }

        BotGround = null;
        var target = _routeTarget;
        var interact = _routeInteract;
        _route = null;
        _routeTarget = null;
        if (interact && target is not null)
        {
            UpdateInteraction();
            if (_near?.Id == target.Id && Driver is { InputLocked: false } driver)
            {
                driver.Interact(target);
            }
            else if (_near?.Id == target.Id && Driver is null)
            {
                _toasts.Push(target.Kind, target.Text, target.Where);
            }
        }
    }

    /// <summary>自动行走卡住时通知（自动走查记问题用）：主角位置、下一格。</summary>
    public event Action<Vector2, Vector2>? RouteStuck;

    /// <summary>鼠标左键点地：点到交互物（自地面到原菱形高度的那一竖条）附近就走去交互，否则走到点下的地面。</summary>
    private bool ClickWalk(Vector2 screen)
    {
        if (Driver is { InputLocked: true })
        {
            return false;
        }

        // 先看点没点到交互物：菱形不再显示，按屏幕上自交互点地面到原菱形高度的竖条取最近者（拾取半径内）。
        TownInteraction? picked = null;
        var best = ClickMarkerPick;
        foreach (var (data, marker) in _interactions)
        {
            var top = _world.Position + marker.Position * _zoom;
            var foot = _world.Position + TownView.P(data.Position, StepZ(data.Position)) * _zoom;
            var d = Geometry2D.GetClosestPointToSegment(screen, foot, top).DistanceTo(screen);
            if (d < best)
            {
                best = d;
                picked = data;
            }
        }

        if (picked is not null)
        {
            if (_near?.Id == picked.Id)
            {
                // 已在交互范围内：直接交互。
                if (Driver is { } driver)
                {
                    driver.Interact(picked);
                }

                return true;
            }

            if (WalkToInteraction(picked, interact: true))
            {
                ShowClick(picked.Position);
                return true;
            }
        }

        var projected = (screen - _world.Position) / _zoom;
        var ground = TownView.GroundAt(projected, StepZ(Hero.Ground));
        if (WalkToGround(ground))
        {
            ShowClick(ground);
            return true;
        }

        return false;
    }

    private void ShowClick(Vector2 ground)
    {
        var ring = new ClickRing { Position = TownView.P(ground, StepZ(ground)) };
        _markers.AddChild(ring);
    }

    /// <summary>从主角所在格出发找到第一个满足 <paramref name="goal"/> 的格子，返回沿途格中心（先回到起点格中心）。</summary>
    public List<Vector2>? FindPath(Func<Vector2, bool> goal)
    {
        _grid ??= BuildGrid();
        var grid = _grid;
        int w = grid.GetLength(0), h = grid.GetLength(1);
        var start = CellOf(Hero.Ground);
        start = (Math.Clamp(start.X, 0, w - 1), Math.Clamp(start.Y, 0, h - 1));
        if (!grid[start.X, start.Y])
        {
            // 主角贴着物件站，所在格判为不可走：从附近最近的可走格出发。
            (int X, int Y)? best = null;
            var bestD = float.MaxValue;
            for (var x = Math.Max(0, start.X - 4); x < Math.Min(w, start.X + 5); x++)
            {
                for (var y = Math.Max(0, start.Y - 4); y < Math.Min(h, start.Y + 5); y++)
                {
                    var d = Center(x, y).DistanceTo(Hero.Ground);
                    if (grid[x, y] && d < bestD)
                    {
                        (best, bestD) = ((x, y), d);
                    }
                }
            }

            if (best is not { } b)
            {
                return null;
            }

            start = b;
        }

        var came = new Dictionary<(int, int), (int, int)> { [start] = start };
        var queue = new Queue<(int X, int Y)>([start]);
        (int X, int Y)? found = null;
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            if (goal(Center(c.X, c.Y)))
            {
                found = c;
                break;
            }

            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    var n = (X: c.X + dx, Y: c.Y + dy);
                    if ((dx == 0 && dy == 0) || n.X < 0 || n.Y < 0 || n.X >= w || n.Y >= h || !grid[n.X, n.Y] || came.ContainsKey(n))
                    {
                        continue;
                    }

                    if (dx != 0 && dy != 0 && (!grid[c.X + dx, c.Y] || !grid[c.X, c.Y + dy]))
                    {
                        continue;
                    }

                    // 两格之间的中点也要站得住，免得格子中心可站、中间却夹着一条细的挡路物。
                    if (!Walkable((Center(c.X, c.Y) + Center(n.X, n.Y)) / 2))
                    {
                        continue;
                    }

                    came[n] = c;
                    queue.Enqueue(n);
                }
            }
        }

        if (found is not { } end)
        {
            return null;
        }

        var path = new List<Vector2>();
        for (var c = end; c != start; c = came[c])
        {
            path.Add(Center(c.X, c.Y));
        }

        path.Reverse();
        path.Insert(0, Center(start.X, start.Y));
        return path;
    }

    private bool[,] BuildGrid()
    {
        var b = Bounds;
        _gridOrigin = b.Position;
        var w = (int)Mathf.Ceil(b.Size.X / Cell);
        var h = (int)Mathf.Ceil(b.Size.Y / Cell);
        var grid = new bool[w, h];
        for (var x = 0; x < w; x++)
        {
            for (var y = 0; y < h; y++)
            {
                grid[x, y] = Walkable(Center(x, y));
            }
        }

        return grid;
    }

    private Vector2 Center(int x, int y) => _gridOrigin + new Vector2((x + 0.5f) * Cell, (y + 0.5f) * Cell);

    private (int X, int Y) CellOf(Vector2 p) => ((int)((p.X - _gridOrigin.X) / Cell), (int)((p.Y - _gridOrigin.Y) / Cell));
}

/// <summary>点地反馈：地面上一圈扩散淡出的金色椭圆环。</summary>
public partial class ClickRing : Node2D
{
    private float _t;

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_t > 0.5f)
        {
            QueueFree();
            return;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        var k = _t / 0.5f;
        var r = 14 + 22 * k;
        var color = UiPalette.Gilt with { A = 0.9f * (1 - k) };
        var points = new Vector2[33];
        for (var i = 0; i <= 32; i++)
        {
            var a = i * Mathf.Tau / 32;
            points[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.5f);
        }

        DrawPolyline(points, color, 2.5f, antialiased: true);
    }
}
