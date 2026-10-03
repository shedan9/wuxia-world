using Godot;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Presentation.Play;

/// <summary>
/// 探索漫游（<c>--stroll=秒</c>，开发用，开发计划 M3-07 的“典型探索”性能场景）：每到一图，用与玩家点地同一套寻路
/// 在交互点之间来回走（每次挑离主角最远的一个），镜头一路跟随，不做交互、不开过场；走满给定秒数后经剧情换图票据
/// （与出口同一条换图路径：色幕、重新载入探索页）去下一张没到过的图，各图走过一遍后退出。帧时间由 <see cref="PerfProbe"/> 记录，
/// 按“探索页 · 地图名”分段。
/// </summary>
public partial class ExplorationScreen
{
    private static readonly HashSet<string> StrollVisited = new(StringComparer.Ordinal);
    private static int _strollRound = 1;
    private double _strollLeft;
    private bool _strolling;
    private string? _strollLast;
    private Vector2 _strollHome;
    private bool _strollPaused;
    private string? _layeredAt;

    private async void StrollVisit()
    {
        if (!StrollVisited.Add(World.MapId))
        {
            GD.Print($"[stroll] 各图已走过一遍（{StrollVisited.Count} 张），结束");
            GetTree().Quit(0);
            return;
        }

        var round = _strollRound;

        if (DevCapture.StrollShots is { } shots)
        {
            // 进图落点截一张（核对性能改动前后画面不变）。
            await SoakFrames(40);
            var path = System.IO.Path.Combine(shots, World.MapId + (round > 1 ? $".r{round}" : "") + ".png");
            GD.Print($"[stroll] 截图 {path}：{GetViewport().GetTexture().GetImage().SavePng(path)}");
        }

        if (DevCapture.PerfLayers)
        {
            await LayerBreakdown();
        }

        await SoakFrames(30);
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        await SoakFrames(10);
        GD.Print($"[stroll] {World.MapId} 第 {round} 轮落定：纹理 {Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed) / 1024 / 1024:0} MB，"
            + $"显存合计 {Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1024 / 1024:0} MB");
        _strollLeft = DevCapture.Stroll;
        _strolling = true;
        _strollHome = _view.HeroGround;
        GD.Print($"[stroll] {World.MapId}：漫游 {DevCapture.Stroll:0} 秒，交互点 {Interactions.Count} 个");
    }

    /// <summary>
    /// 布景分层归因（<c>--perf-layers</c>，配合 <c>--stroll</c>）：在进图落点，把世界里的二维节点按脚本类名分组，逐组隐藏 20 帧，
    /// 与全显时比较绘制调用、绘制对象、图元与渲染耗时，按省下的绘制调用排序打印。隐藏父节点会连带子节点，组间可能重复计。
    /// </summary>
    private async Task LayerBreakdown()
    {
        await SoakFrames(30);
        var groups = new Dictionary<string, List<CanvasItem>>(StringComparer.Ordinal);
        void Walk(Node n)
        {
            if (n is Node2D item && item.Visible)
            {
                var name = item.GetType().Name;
                if (!groups.TryGetValue(name, out var list))
                {
                    groups[name] = list = [];
                }

                list.Add(item);
            }

            foreach (var c in n.GetChildren())
            {
                Walk(c);
            }
        }

        Walk(this);
        var rid = GetViewport().GetViewportRid();
        async Task<(double Draws, double Objects, double Prims, double Cpu, double Gpu)> Measure()
        {
            await SoakFrames(8);
            double d = 0, o = 0, p = 0, cpu = 0, gpu = 0;
            const int n = 20;
            for (var i = 0; i < n; i++)
            {
                await SoakFrames(1);
                d += Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
                o += Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
                p += Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
                cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid);
                gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid);
            }

            return (d / n, o / n, p / n, cpu / n, gpu / n);
        }

        var all = await Measure();
        GD.Print($"[layers] {World.MapId} 全显：绘制调用 {all.Draws:0}、对象 {all.Objects:0}、图元 {all.Prims:0}、渲染 CPU {all.Cpu:0.00}、GPU {all.Gpu:0.00} 毫秒；{groups.Count} 组");
        var rows = new List<(string Name, int Count, double Draws, double Objects, double Prims, double Cpu, double Gpu)>();
        foreach (var (name, items) in groups)
        {
            foreach (var item in items)
            {
                item.Visible = false;
            }

            var m = await Measure();
            foreach (var item in items)
            {
                item.Visible = true;
            }

            rows.Add((name, items.Count, all.Draws - m.Draws, all.Objects - m.Objects, all.Prims - m.Prims, all.Cpu - m.Cpu, all.Gpu - m.Gpu));
        }

        foreach (var r in rows.OrderByDescending(r => r.Draws).Take(16))
        {
            GD.Print($"[layers]   {r.Name} ×{r.Count}：省绘制调用 {r.Draws:0}、对象 {r.Objects:0}、图元 {r.Prims:0}、渲染 CPU {r.Cpu:0.00}、GPU {r.Gpu:0.00} 毫秒");
        }
    }

    private async void StrollLayers()
    {
        GD.Print($"[layers] 停靠 {_strollLast}（{_view.HeroGround.Round()}）");
        await LayerBreakdown();
        _strollPaused = false;
        PickNextStop();
    }

    /// <summary>每帧推进漫游；返回 true 表示漫游接管了本帧。</summary>
    private bool StepStroll(double delta)
    {
        if (!_strolling || _leaving)
        {
            return _strolling;
        }

        if (AppHost.Instance.Router.Busy || _strollPaused)
        {
            return true;
        }

        _strollLeft -= delta;
        if (_strollLeft <= 0)
        {
            _strolling = false;
            var maps = Game.Rules.Content.Maps.Keys.Order(StringComparer.Ordinal).ToList();
            var next = maps.FirstOrDefault(m => !StrollVisited.Contains(m));
            if (next is null && _strollRound < DevCapture.StrollRounds)
            {
                // 再走一轮（核对换图释放贴图缓存后重进各图画面不变）。
                _strollRound++;
                StrollVisited.Clear();
                StrollVisited.Add(World.MapId);
                next = maps.FirstOrDefault(m => !StrollVisited.Contains(m));
                StrollVisited.Remove(World.MapId);
            }

            if (next is null)
            {
                GD.Print($"[stroll] 各图已走过一遍（{StrollVisited.Count} 张），结束");
                GetTree().Quit(0);
                return true;
            }

            Go(Game.BeginStoryTravel(new TravelRequest(next, null)));
            return true;
        }

        if (_view.AutoWalking)
        {
            return true;
        }

        if (DevCapture.PerfLayers && _strollLast is not null && _layeredAt != _strollLast)
        {
            // 每到一处停靠点都做一次分层归因（同一张图上镜头所见不同，热点也不同）。
            _strollPaused = true;
            _layeredAt = _strollLast;
            StrollLayers();
            return true;
        }

        PickNextStop();
        return true;
    }
    /// <summary>挑下一处停靠点走过去；哪儿都走不到时结束本图漫游。</summary>
    private void PickNextStop()
    {
        // 走到了（或上一次没走成）：在各交互点与进图落点之间，挑离主角最远、且不是刚去过的一处再走（交互点少的图也能来回走）。
        var hero = _view.HeroGround;
        var stops = Interactions.Select(i => (Id: i.Id, At: i.Position, Item: (TownInteraction?)i)).Append((Id: "落点", At: _strollHome, Item: null));
        foreach (var (id, at, item) in stops.Where(s => s.Id != _strollLast).OrderByDescending(s => s.At.DistanceSquaredTo(hero)))
        {
            if (item is not null ? _view.WalkToInteraction(item, interact: false) : _view.WalkToGround(at))
            {
                _strollLast = id;
                return;
            }
        }

        GD.Print($"[stroll] {World.MapId} 从 {hero.Round()} 哪个交互点都走不到");
        _strollLeft = 0;
    }
}
