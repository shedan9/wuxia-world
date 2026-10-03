using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Godot;

namespace WuxiaWorld.Game.Presentation.App;

/// <summary>
/// 性能测量（开发用，开发计划 M3-07，架构文档 12.1）：<c>--perf=报告.json</c> 打开。逐帧记下帧间隔、上一帧的逻辑耗时与视口渲染的
/// CPU / GPU 耗时，按“场景 · 当前活动”分段统计均值、P50 / P95 / P99、最大值与超过 16.7 / 33.3 毫秒的帧数；换场景时记下
/// 加载耗时（发起切换到新场景第一帧画完）与整段过场耗时（色幕淡出到收起）；每 0.25 秒采样进程工作集、私有内存与纹理显存，
/// 过场中逐帧采样以抓切图峰值；战斗页把每次规则执行（玩家命令结算、AI 决策并结算）的耗时报到这里。
/// <para>
/// 选项：<c>--perf-seconds=N</c> 测满 N 秒后写报告退出（缺省不限时，随走查结束退出时写报告）；
/// <c>--perf-window=1920x1080</c> 测量窗口尺寸（缺省 1920x1080，<c>full</c> 保持全屏）；
/// <c>--perf-vsync=off</c> 关垂直同步且不限帧率，测出每帧真实耗时（缺省按玩家设置开垂直同步，测的是玩家实际看到的帧间隔）。
/// </para>
/// 统计不含过场帧（色幕走动、新场景载入）与载入后的 30 帧预热；这些帧单独列在加载与尖峰里。
/// </summary>
public partial class PerfProbe : Node
{
    private const int WarmupFrames = 30;
    private const double FrameBudgetMs = 1000.0 / 60;
    private const double HitchMs = 1000.0 / 30;

    private sealed record Frame(double At, float Ms, float ProcessMs, float RenderCpuMs, float RenderGpuMs, string Phase, bool Steady);

    private sealed record Load(string From, string To, double LoadMs, double TransitionMs, long PeakWorkingSet);

    private sealed record MemorySample(double At, string Phase, long WorkingSet, long Private, long TextureMem, long VideoMem, long DrawCalls, long Objects, long Primitives);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static PerfProbe? Instance { get; private set; }

    /// <summary>报告路径（<c>--perf=</c>）；为 null 时不测。</summary>
    public static string? Output { get; set; }

    public static double Seconds { get; set; }

    public static string Window { get; set; } = "1920x1080";

    public static bool VsyncOff { get; set; }

    /// <summary>当前活动（各页按需写入，如“芦湾街”“芦湾街/对话”“4 对 6”），与场景名一起作为分段键。</summary>
    public static string Activity { get; set; } = "";

    private readonly List<Frame> _frames = [];
    private readonly List<Load> _loads = [];
    private readonly List<MemorySample> _memory = [];
    private readonly Dictionary<string, List<double>> _rules = new(StringComparer.Ordinal);
    private readonly Process _process = Process.GetCurrentProcess();

    private ulong _start;
    private ulong _last;
    private double _memoryClock;
    private int _sinceLoad = WarmupFrames;
    private bool _reported;

    // 节点逻辑耗时：本节点处理优先级最先、Tail 最后，两者之间即本帧全部节点 _Process（不含补间、物理与渲染）。
    // 记在下一帧的帧间隔上：一次帧间隔里包含的正是上一帧的逻辑与渲染。
    private ulong _headAt;
    private double _lastProcessMs;

    /// <summary>处理顺序最后的节点，记下本帧节点逻辑结束的时刻。</summary>
    private sealed partial class Tail : Node
    {
        public override void _Process(double delta)
        {
            if (Instance is { } probe)
            {
                probe._lastProcessMs = (Time.GetTicksUsec() - probe._headAt) / 1000.0;
            }
        }
    }

    // 一次换场景：发起时刻、旧场景、新场景第一帧是否画完、过场开始时刻与过场中的工作集峰值。
    private ulong? _loadStart;
    private ulong _transitionStart;
    private string _loadFrom = "";
    private ulong _oldScene;
    private double? _pendingLoadMs;
    private string _pendingTo = "";
    private long _transitionPeak;
    private bool _wasBusy;

    public static void Begin(Node host)
    {
        if (Output is null || Instance is not null)
        {
            return;
        }

        Instance = new PerfProbe { ProcessMode = ProcessModeEnum.Always, ProcessPriority = int.MinValue };
        host.AddChild(Instance);
        host.AddChild(new Tail { ProcessMode = ProcessModeEnum.Always, ProcessPriority = int.MaxValue });
    }

    /// <summary>规则执行耗时（毫秒），按类别累计。</summary>
    public static void Rule(string kind, double ms)
    {
        if (Instance is not { } probe)
        {
            return;
        }

        if (!probe._rules.TryGetValue(kind, out var list))
        {
            probe._rules[kind] = list = [];
        }

        list.Add(ms);
    }

    /// <summary>分段计时起点：未测量时返回 0，<see cref="Stop"/> 随之不做事（不测时只多一次判断）。</summary>
    public static long Start() => Instance is null ? 0 : Stopwatch.GetTimestamp();

    /// <summary>分段计时终点：把自 <paramref name="started"/> 起的耗时记入 <paramref name="kind"/>。</summary>
    public static void Stop(string kind, long started)
    {
        if (started != 0)
        {
            Rule(kind, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    /// <summary>场景路由即将同步载入新场景（<see cref="SceneTree.ChangeSceneToFile"/> 之前调用）。</summary>
    public static void MarkLoad()
    {
        if (Instance is not { } probe)
        {
            return;
        }

        var tree = probe.GetTree();
        probe._loadStart = Time.GetTicksUsec();
        probe._loadFrom = SceneName(tree.CurrentScene) + (Activity.Length > 0 ? " · " + Activity : "");
        probe._oldScene = tree.CurrentScene?.GetInstanceId() ?? 0;
        if (!AppHost.Instance.Router.Busy)
        {
            // 不走色幕的切换：过场即载入本身。
            probe._transitionStart = probe._loadStart.Value;
            probe._transitionPeak = 0;
        }
    }

    public override async void _Ready()
    {
        _start = _last = Time.GetTicksUsec();
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        RenderingServer.Singleton.Connect(RenderingServer.SignalName.FramePostDraw, Callable.From(OnFramePostDraw));
        if (VsyncOff)
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            Engine.MaxFps = 0;
        }

        GD.Print($"[perf] 开始测量：窗口 {Window}，垂直同步{(VsyncOff ? "关、不限帧率" : "按设置")}，"
            + (Seconds > 0 ? $"测 {Seconds:0} 秒" : "随走查结束"));
        if (Window != "full" && Window.Split('x') is [var w, var h])
        {
            // 模式切换要过几帧才落定，之后再设尺寸（同 AppHost.EnterWindowed）。
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            for (var i = 0; i < 3; i++)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }

            var size = new Vector2I(int.Parse(w, CultureInfo.InvariantCulture), int.Parse(h, CultureInfo.InvariantCulture));
            DisplayServer.WindowSetSize(size);
            var area = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
            DisplayServer.WindowSetPosition(area.Position + (area.Size - size) / 2);
        }
    }

    public override void _Process(double delta)
    {
        var now = Time.GetTicksUsec();
        _headAt = now;
        var ms = (now - _last) / 1000.0;
        _last = now;
        var tree = GetTree();
        var busy = AppHost.Instance.Router.Busy;
        if (busy && !_wasBusy)
        {
            _transitionStart = now;
            _transitionPeak = 0;
        }

        var loading = _loadStart is not null || _pendingLoadMs is not null;
        var inTransition = busy || loading;
        if (inTransition)
        {
            _process.Refresh();
            _transitionPeak = Math.Max(_transitionPeak, _process.WorkingSet64);
        }

        if (_pendingLoadMs is { } loadMs && !busy)
        {
            // 新场景已画出第一帧、色幕也收起：一次换场景结束。
            // 新场景此时已跑过几帧，活动名（如地图名）已由新页写入。
            _pendingTo = SceneName(tree.CurrentScene) + (Activity.Length > 0 ? " · " + Activity : "");
            _loads.Add(new Load(_loadFrom, _pendingTo, loadMs, (now - _transitionStart) / 1000.0, _transitionPeak));
            _pendingLoadMs = null;
            _sinceLoad = 0;
        }

        _wasBusy = busy;
        var phase = SceneName(tree.CurrentScene) + (Activity.Length > 0 ? " · " + Activity : "") + (tree.Paused ? " · 菜单" : "");
        var steady = !inTransition && _sinceLoad >= WarmupFrames && _frames.Count > 0;
        _sinceLoad++;
        var rid = GetViewport().GetViewportRid();
        _frames.Add(new Frame((now - _start) / 1e6, (float)ms,
            (float)_lastProcessMs,
            (float)RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid),
            (float)RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid), phase, steady));

        _memoryClock += delta;
        if (_memoryClock >= 0.25)
        {
            _memoryClock = 0;
            _process.Refresh();
            _memory.Add(new MemorySample((now - _start) / 1e6, phase, _process.WorkingSet64, _process.PrivateMemorySize64,
                (long)Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed),
                (long)Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed),
                (long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
                (long)Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame),
                (long)Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame)));
        }

        if (Seconds > 0 && (now - _start) / 1e6 >= Seconds)
        {
            Report();
            tree.Quit(0);
        }
    }

    private void OnFramePostDraw()
    {
        if (_loadStart is not { } start)
        {
            return;
        }

        var scene = GetTree().CurrentScene;
        if (scene is null || scene.GetInstanceId() == _oldScene)
        {
            return;
        }

        _pendingLoadMs = (Time.GetTicksUsec() - start) / 1000.0;
        _loadStart = null;
    }

    public override void _ExitTree() => Report();

    private static string SceneName(Node? scene) =>
        scene is null ? "（无）" : System.IO.Path.GetFileNameWithoutExtension(scene.SceneFilePath);

    private static double Pct(List<double> sorted, double p) =>
        sorted.Count == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(p / 100 * sorted.Count) - 1, 0, sorted.Count - 1)];

    private static Dictionary<string, object> Stats(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return new Dictionary<string, object>
        {
            ["count"] = sorted.Count,
            ["avg"] = Math.Round(sorted.Count == 0 ? 0 : sorted.Average(), 3),
            ["p50"] = Math.Round(Pct(sorted, 50), 3),
            ["p95"] = Math.Round(Pct(sorted, 95), 3),
            ["p99"] = Math.Round(Pct(sorted, 99), 3),
            ["max"] = Math.Round(sorted.Count == 0 ? 0 : sorted[^1], 3),
        };
    }

    private void Report()
    {
        if (_reported || Output is null)
        {
            return;
        }

        _reported = true;
        _process.Refresh();
        var text = new StringBuilder();
        var phases = new List<Dictionary<string, object>>();
        foreach (var group in _frames.Where(f => f.Steady).GroupBy(f => f.Phase).OrderByDescending(g => g.Count()))
        {
            var frames = group.ToList();
            var ms = Stats(frames.Select(f => (double)f.Ms));
            var over = frames.Count(f => f.Ms > FrameBudgetMs + 0.5);
            var hitch = frames.Count(f => f.Ms > HitchMs);
            var seconds = frames.Sum(f => f.Ms) / 1000;
            var samples = _memory.Where(m => m.Phase == group.Key).ToList();
            long Max(Func<MemorySample, long> f) => samples.Count == 0 ? 0 : samples.Max(f);
            phases.Add(new Dictionary<string, object>
            {
                ["phase"] = group.Key,
                ["seconds"] = Math.Round(seconds, 1),
                ["fps"] = Math.Round(frames.Count / Math.Max(seconds, 1e-6), 1),
                ["frame_ms"] = ms,
                ["over_17ms"] = over,
                ["over_33ms"] = hitch,
                ["process_ms"] = Stats(frames.Select(f => (double)f.ProcessMs)),
                ["render_cpu_ms"] = Stats(frames.Select(f => (double)f.RenderCpuMs)),
                ["render_gpu_ms"] = Stats(frames.Select(f => (double)f.RenderGpuMs)),
                ["working_set_max_mb"] = Max(m => m.WorkingSet) / 1024 / 1024,
                ["texture_max_mb"] = Max(m => m.TextureMem) / 1024 / 1024,
                ["draw_calls_max"] = Max(m => m.DrawCalls),
                ["objects_max"] = Max(m => m.Objects),
                ["primitives_max"] = Max(m => m.Primitives),
            });
            text.AppendLine(CultureInfo.InvariantCulture, $"[perf] {group.Key}：{frames.Count} 帧 {seconds:0.0} 秒，{frames.Count / Math.Max(seconds, 1e-6):0.0} FPS；"
                + $"帧间隔 均 {ms["avg"]} / P95 {ms["p95"]} / P99 {ms["p99"]} / 最大 {ms["max"]} 毫秒，超 17 毫秒 {over} 帧、超 33 毫秒 {hitch} 帧；"
                + $"逻辑 P95 {Stats(frames.Select(f => (double)f.ProcessMs))["p95"]}，渲染 CPU P95 {Stats(frames.Select(f => (double)f.RenderCpuMs))["p95"]}，"
                + $"GPU P95 {Stats(frames.Select(f => (double)f.RenderGpuMs))["p95"]} 毫秒；绘制调用最多 {Max(m => m.DrawCalls)}、"
                + $"绘制对象 {Max(m => m.Objects)}、图元 {Max(m => m.Primitives)}；工作集 {Max(m => m.WorkingSet) / 1024 / 1024} MB、纹理 {Max(m => m.TextureMem) / 1024 / 1024} MB");
        }

        foreach (var load in _loads)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"[perf] 换场景 {load.From} → {load.To}：载入 {load.LoadMs:0} 毫秒，过场 {load.TransitionMs:0} 毫秒，"
                + $"过场中工作集最高 {load.PeakWorkingSet / 1024 / 1024} MB");
        }

        var loadStats = Stats(_loads.Select(l => l.LoadMs));
        var transitionStats = Stats(_loads.Select(l => l.TransitionMs));
        if (_loads.Count > 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"[perf] 换场景 {_loads.Count} 次：载入 P95 {loadStats["p95"]} / 最大 {loadStats["max"]} 毫秒，"
                + $"过场 P95 {transitionStats["p95"]} / 最大 {transitionStats["max"]} 毫秒");
        }

        var rules = _rules.ToDictionary(r => r.Key, r => (object)Stats(r.Value));
        foreach (var (kind, values) in _rules)
        {
            var s = Stats(values);
            text.AppendLine(CultureInfo.InvariantCulture, $"[perf] 规则执行 {kind}：{s["count"]} 次，均 {s["avg"]} / P95 {s["p95"]} / 最大 {s["max"]} 毫秒");
        }

        var spikes = _frames.Skip(1).OrderByDescending(f => f.Ms).Take(12)
            .Select(f => new Dictionary<string, object> { ["at"] = Math.Round(f.At, 2), ["ms"] = Math.Round(f.Ms, 1), ["phase"] = f.Phase, ["steady"] = f.Steady })
            .ToList();
        foreach (var s in spikes.Take(6))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"[perf] 尖峰 第 {s["at"]} 秒 {s["ms"]} 毫秒（{s["phase"]}{((bool)s["steady"] ? "" : "，过场或预热")}）");
        }

        var memory = new Dictionary<string, object>
        {
            ["working_set_max_mb"] = _memory.Count == 0 ? 0 : _memory.Max(m => m.WorkingSet) / 1024 / 1024,
            ["working_set_peak_mb"] = _process.PeakWorkingSet64 / 1024 / 1024,
            ["transition_peak_mb"] = _loads.Count == 0 ? 0 : _loads.Max(l => l.PeakWorkingSet) / 1024 / 1024,
            ["private_max_mb"] = _memory.Count == 0 ? 0 : _memory.Max(m => m.Private) / 1024 / 1024,
            ["texture_max_mb"] = _memory.Count == 0 ? 0 : _memory.Max(m => m.TextureMem) / 1024 / 1024,
            ["video_max_mb"] = _memory.Count == 0 ? 0 : _memory.Max(m => m.VideoMem) / 1024 / 1024,
        };
        text.AppendLine(CultureInfo.InvariantCulture, $"[perf] 内存：工作集采样最高 {memory["working_set_max_mb"]} MB、进程峰值 {memory["working_set_peak_mb"]} MB、"
            + $"过场中最高 {memory["transition_peak_mb"]} MB；私有内存最高 {memory["private_max_mb"]} MB；纹理 {memory["texture_max_mb"]} MB、显存合计 {memory["video_max_mb"]} MB");

        var report = new Dictionary<string, object>
        {
            ["engine"] = Engine.GetVersionInfo()["string"].AsString(),
            ["renderer"] = RenderingServer.GetVideoAdapterName() + " · " + RenderingServer.GetVideoAdapterApiVersion(),
            ["cpu"] = OS.GetProcessorName(),
            ["cores"] = OS.GetProcessorCount(),
            ["window"] = DisplayServer.WindowGetSize().ToString(),
            ["refresh_hz"] = Math.Round(DisplayServer.ScreenGetRefreshRate(), 1),
            ["vsync"] = DisplayServer.WindowGetVsyncMode().ToString(),
            ["debug_build"] = OS.IsDebugBuild(),
            ["seconds"] = Math.Round((Time.GetTicksUsec() - _start) / 1e6, 1),
            ["phases"] = phases,
            ["loads"] = _loads.Select(l => new Dictionary<string, object>
            {
                ["from"] = l.From, ["to"] = l.To, ["load_ms"] = Math.Round(l.LoadMs, 1), ["transition_ms"] = Math.Round(l.TransitionMs, 1),
                ["peak_working_set_mb"] = l.PeakWorkingSet / 1024 / 1024,
            }).ToList(),
            ["load_ms"] = loadStats,
            ["transition_ms"] = transitionStats,
            ["rules_ms"] = rules,
            ["memory"] = memory,
            ["spikes"] = spikes,
        };
        GD.Print(text.ToString().TrimEnd());
        GD.Print($"[perf] 设备：{report["cpu"]}（{report["cores"]} 线程）· {report["renderer"]} · 窗口 {report["window"]} · 刷新率 {report["refresh_hz"]} Hz · "
            + $"垂直同步 {report["vsync"]} · {(OS.IsDebugBuild() ? "调试构建" : "发布构建")}");
        try
        {
            System.IO.File.WriteAllText(Output, JsonSerializer.Serialize(report, JsonOptions));
            var csv = new StringBuilder("at_s,frame_ms,process_ms,render_cpu_ms,render_gpu_ms,steady,phase\n");
            foreach (var f in _frames)
            {
                csv.Append(CultureInfo.InvariantCulture, $"{f.At:0.0000},{f.Ms:0.000},{f.ProcessMs:0.000},{f.RenderCpuMs:0.000},{f.RenderGpuMs:0.000},{(f.Steady ? 1 : 0)},{f.Phase}\n");
            }

            System.IO.File.WriteAllText(System.IO.Path.ChangeExtension(Output, ".frames.csv"), csv.ToString());
            GD.Print($"[perf] 报告已写入 {Output}（逐帧明细 {System.IO.Path.ChangeExtension(Output, ".frames.csv")}）");
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            GD.PushError($"[perf] 报告写入失败：{e.Message}");
        }
    }
}
