using Godot;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Game.Presentation.App;

namespace WuxiaWorld.Game.Presentation.Play;

/// <summary>
/// 耐久走查（<c>--soak=N</c>，开发用，开发计划 6.1 T09 与 M2 验收“100 次切图”“重进地图不刷新一次性交互”）：
/// 每到一图先核对交互点、站位人物与世界状态和上次来时一致，再用真实按键开关各界面并快速存档，然后采样资源，
/// 经剧情换图票据去下一张图（与出口、路线同一条换图路径：色幕、重新载入探索页、提交、自动存档）。
/// 不开过场事件，不做交互，世界状态在第一轮换图之后不应再变。
/// </summary>
public partial class ExplorationScreen
{
    /// <summary>每到一图依次注入的按键：札记、人物、行囊、队伍各开一次再 Esc 关掉，M 开关大地图（从 M 打开时选中所在地、交通面板开着，Esc 要按两下），Esc 开暂停菜单再关，F5 快速存档。</summary>
    private static readonly Key[] SoakKeys =
        [Key.J, Key.Escape, Key.C, Key.Escape, Key.I, Key.Escape, Key.P, Key.Escape, Key.M, Key.M, Key.Escape, Key.Escape, Key.F5];

    private sealed record SoakPoint(int Visit, string Map, long Nodes, long Objects, long Orphans, long Managed, long Private, long WorkingSet, int Connections);

    private static readonly List<SoakPoint> SoakSamples = [];
    private static readonly Dictionary<string, string> SoakSeen = new(StringComparer.Ordinal);
    private static readonly List<string> SoakProblems = [];
    private static string? _soakWorld;
    private static ulong _soakStart;

    private async void SoakVisit()
    {
        if (_soakStart == 0)
        {
            _soakStart = Time.GetTicksMsec();
            GD.Print($"[soak] 开始：换图 {DevCapture.Soak} 次，起点 {World.MapId}，主线阶段 {MainStage()}");
        }

        // 等色幕收起、入场动效与到达提示落定。
        await SoakFrames(20);
        while (AppHost.Instance.Router.Busy)
        {
            await SoakFrames(1);
        }

        await SoakFrames(10);
        if (!IsInsideTree() || _leaving)
        {
            return;
        }

        var visit = ++DevCapture.SoakVisits;
        var maps = Game.Rules.Content.Maps.Keys.Order(StringComparer.Ordinal).ToList();
        SoakCheckWorld(visit, maps.Count);

        foreach (var key in SoakKeys)
        {
            foreach (var pressed in new[] { true, false })
            {
                Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
            }

            await SoakFrames(6);
        }

        if (AppHost.Instance.Menu.IsOpen || _modal is not null)
        {
            SoakProblems.Add($"第 {visit} 次（{World.MapId}）：按键走完后仍有界面未关（菜单 {AppHost.Instance.Menu.IsOpen}，弹层 {_modal is not null}）");
            AppHost.Instance.Menu.Close();
            CloseModal();
        }

        // 让 QueueFree 的界面真正释放后再采样。
        await SoakFrames(12);
        SoakSamples.Add(TakeSoakSample(visit));
        SoakDiffNodes(visit);
        if (visit % 10 == 0 || visit == 1)
        {
            var s = SoakSamples[^1];
            GD.Print($"[soak] 第 {visit} 次 {s.Map}：节点 {s.Nodes}，对象 {s.Objects}，孤立节点 {s.Orphans}，托管 {s.Managed / 1024} KB，"
                + $"私有内存 {s.Private / 1024 / 1024} MB，工作集 {s.WorkingSet / 1024 / 1024} MB，常驻信号连接 {s.Connections}");
        }

        if (visit >= DevCapture.Soak)
        {
            SoakReport(maps.Count);
            return;
        }

        var next = maps[(maps.IndexOf(World.MapId) + 1) % maps.Count];
        Go(Game.BeginStoryTravel(new TravelRequest(next, null)));
    }

    /// <summary>
    /// 本图的交互点与站位人物要与上次来时一致；世界状态（除所在地图、落点与修订号）在第一轮换图之后不应再变：
    /// 宝箱拾取后不复现、剧情移走的人物不回来、离队的人不自动归队。
    /// </summary>
    private void SoakCheckWorld(int visit, int mapCount)
    {
        var view = string.Join(" | ", Interactions.Select(i => i.Id).Order(StringComparer.Ordinal))
            + " || " + string.Join(" | ", Actors.Select(a => a.Look.ArtId).Order(StringComparer.Ordinal))
            + " || " + string.Join(",", World.Party);
        if (SoakSeen.TryGetValue(World.MapId, out var before) && before != view && visit > mapCount)
        {
            SoakProblems.Add($"第 {visit} 次（{World.MapId}）：交互点或站位人物与上次不同\n  上次 {before}\n  本次 {view}");
        }

        SoakSeen[World.MapId] = view;
        var c = World.Clone();
        c.MapId = "";
        c.SpawnId = "";
        c.Revision = 0;
        var hash = c.Hash();
        if (visit > mapCount && _soakWorld is not null && hash != _soakWorld)
        {
            SoakProblems.Add($"第 {visit} 次（{World.MapId}）：换图后世界状态变了（事实 {World.Facts.Count}，线索 {World.Clues.Count}，银 {World.Silver}，时辰 {World.Clock}）");
        }

        _soakWorld = hash;
    }

    private static readonly Dictionary<string, Dictionary<string, int>> SoakKinds = new(StringComparer.Ordinal);

    /// <summary>与上次到本图时比，按节点类型（脚本类名或引擎类名）列出数量变化，用于分辨偶发的提示条与真正的泄漏。</summary>
    private void SoakDiffNodes(int visit)
    {
        var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
        void Walk(Node n)
        {
            var name = n.GetScript().Obj is Script script ? System.IO.Path.GetFileNameWithoutExtension(script.ResourcePath) : n.GetClass();
            kinds[name] = kinds.GetValueOrDefault(name) + 1;
            foreach (var c in n.GetChildren())
            {
                Walk(c);
            }
        }

        Walk(GetTree().Root);
        if (SoakKinds.TryGetValue(World.MapId, out var before))
        {
            var diff = kinds.Keys.Union(before.Keys)
                .Select(k => (k, d: kinds.GetValueOrDefault(k) - before.GetValueOrDefault(k)))
                .Where(x => x.d != 0)
                .Select(x => $"{x.k} {x.d:+0;-0}")
                .ToList();
            if (diff.Count > 0)
            {
                GD.Print($"[soak] 第 {visit} 次 {World.MapId} 节点变化：{string.Join("，", diff)}");
            }
        }

        SoakKinds[World.MapId] = kinds;
    }

    private SoakPoint TakeSoakSample(int visit)
    {
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        long Monitor(Performance.Monitor m) => (long)Performance.GetMonitor(m);
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return new SoakPoint(visit, World.MapId, Monitor(Performance.Monitor.ObjectNodeCount), Monitor(Performance.Monitor.ObjectCount),
            Monitor(Performance.Monitor.ObjectOrphanNodeCount), System.GC.GetTotalMemory(forceFullCollection: true), process.PrivateMemorySize64, process.WorkingSet64,
            PersistentConnections());
    }

    /// <summary>常驻对象（场景树、视口、各自动加载节点与其子节点、渲染服务器）上的信号连接总数；页面重进后重复订阅会让它增长。</summary>
    private int PersistentConnections()
    {
        var tree = GetTree();
        var total = Count(tree) + Count(RenderingServer.Singleton) + Count(tree.Root);
        foreach (var child in tree.Root.GetChildren())
        {
            if (child != tree.CurrentScene)
            {
                total += Walk(child);
            }
        }

        return total;

        int Walk(Node node) => Count(node) + node.GetChildren().Sum(Walk);

        static int Count(GodotObject o) =>
            o.GetSignalList().Sum(s => o.GetSignalConnectionList(s["name"].AsStringName()).Count);
    }

    /// <summary>
    /// 结论：同一张图的采样按到访先后比较。跳过前两轮（缓存、着色器与字体字形在头几次进图时建立），
    /// 比较之后的首三轮与末三轮：节点数与信号连接数不应持续增长，托管内存、私有内存与对象数的均值增长在容差内。
    /// </summary>
    private void SoakReport(int mapCount)
    {
        const int rounds = 3;
        var warm = mapCount * 2;
        var steady = SoakSamples.Where(s => s.Visit > warm).ToList();
        if (steady.Count >= mapCount * rounds * 2)
        {
            var first = steady.Take(mapCount * rounds).ToList();
            var last = steady.TakeLast(mapCount * rounds).ToList();

            // 单次采样会碰上正在播放的音效播放器、未消失的提示条等瞬时节点：同一张图末几轮的最小值仍高于首几轮的最大值才算增长。
            foreach (var map in first.Select(s => s.Map).Distinct())
            {
                void Grow(string what, Func<SoakPoint, long> f, string hint = "")
                {
                    var before = first.Where(s => s.Map == map).Max(f);
                    var after = last.Where(s => s.Map == map).Min(f);
                    if (after > before)
                    {
                        SoakProblems.Add($"{map} {what}：首 {rounds} 轮最多 {before}，末 {rounds} 轮最少 {after}{hint}");
                    }
                }

                Grow("节点数", s => s.Nodes);
                Grow("常驻信号连接", s => s.Connections, "，疑似重复订阅");
                Grow("孤立节点", s => s.Orphans);
            }

            var managed = last.Average(s => s.Managed) - first.Average(s => s.Managed);
            var priv = last.Average(s => s.Private) - first.Average(s => s.Private);
            var objects = last.Average(s => s.Objects) - first.Average(s => s.Objects);
            GD.Print($"[soak] 稳定段（第 {warm + 1}–{SoakSamples[^1].Visit} 次）首末轮均值变化：托管内存 {managed / 1024:+0;-0} KB，"
                + $"私有内存 {priv / 1024 / 1024:+0.0;-0.0} MB，对象 {objects:+0;-0}");
            if (managed > 4 * 1024 * 1024)
            {
                SoakProblems.Add($"托管内存增长 {managed / 1024 / 1024:0.0} MB（容差 4 MB）");
            }

            if (priv > 64 * 1024 * 1024)
            {
                SoakProblems.Add($"私有内存增长 {priv / 1024 / 1024:0.0} MB（容差 64 MB）");
            }

            if (objects > 200)
            {
                SoakProblems.Add($"对象数增长 {objects:0}（容差 200）");
            }
        }
        else
        {
            SoakProblems.Add($"换图次数太少（{SoakSamples.Count}），至少要 {warm + mapCount * rounds * 2} 次才能比较");
        }

        if (SoakSamples.Count > 0)
        {
            GD.Print($"[soak] 工作集最高 {SoakSamples.Max(s => s.WorkingSet) / 1024 / 1024} MB，私有内存最高 {SoakSamples.Max(s => s.Private) / 1024 / 1024} MB"
                + "（采样在界面关闭之后，不含切图瞬间的峰值）");
        }

        foreach (var p in SoakProblems)
        {
            GD.Print($"[soak] 问题：{p}");
        }

        GD.Print($"[soak] 结束：换图 {DevCapture.SoakVisits} 次，用时 {(Time.GetTicksMsec() - _soakStart) / 1000.0:0} 秒，"
            + $"主线阶段 {MainStage()}，问题 {SoakProblems.Count} 处");
        GetTree().Quit(SoakProblems.Count == 0 ? 0 : 5);
    }

    private string MainStage()
    {
        var main = Game.Rules.Content.Quests.Values.FirstOrDefault(q => q.Kind == QuestKind.Main);
        return main is not null && World.Quests.TryGetValue(main.Id, out var q) ? $"{q.Status}/{q.Stage}" : "无";
    }

    private async Task SoakFrames(int n)
    {
        for (var i = 0; i < n; i++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
    }
}
