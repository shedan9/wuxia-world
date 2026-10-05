using Godot;
using WuxiaWorld.Application.Persistence;
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

    private sealed record SoakPoint(int Visit, string Map, long Nodes, long Objects, long Orphans, long Managed, long Private, long WorkingSet, int Connections,
        double Minutes, int Frames, double FrameP99, double FrameMax);

    /// <summary>长时稳定性：本图这段漫游的逐帧耗时（毫秒），窗口最小化期间的帧不计。</summary>
    private static readonly List<double> EndureFrames = [];

    /// <summary>长时稳定性：窗口最小化（不绘制）累计的秒数。</summary>
    private static double _endureHidden;

    private static readonly List<SoakPoint> SoakSamples = [];
    private static readonly Dictionary<string, string> SoakSeen = new(StringComparer.Ordinal);
    private static readonly List<string> SoakProblems = [];
    private static string? _soakWorld;
    private static int _soakLoads;
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

        if (DevCapture.Endure > 0)
        {
            // 长时稳定性：在本图用漫游的寻路走一段，期间记帧耗时。
            EndureFrames.Clear();
            _strollLeft = DevCapture.EndureStroll;
            _strollHome = _view.HeroGround;
            _strollLast = null;
            _strolling = true;
            while (_strolling && IsInsideTree() && !_leaving)
            {
                await SoakFrames(1);
            }

            if (!IsInsideTree() || _leaving)
            {
                SoakProblems.Add($"第 {visit} 次（{World.MapId}）：漫游中离开了本图");
                return;
            }
        }

        // 让 QueueFree 的界面真正释放后再采样。
        await SoakFrames(12);
        SoakSamples.Add(TakeSoakSample(visit));
        SoakDiffNodes(visit);
        if (visit % 10 == 0 || visit == 1 || DevCapture.Endure > 0)
        {
            var s = SoakSamples[^1];
            GD.Print($"[soak] 第 {visit} 次 {s.Map}（{s.Minutes:0.0} 分）：节点 {s.Nodes}，对象 {s.Objects}，孤立节点 {s.Orphans}，托管 {s.Managed / 1024} KB，"
                + $"私有内存 {s.Private / 1024 / 1024} MB，工作集 {s.WorkingSet / 1024 / 1024} MB，常驻信号连接 {s.Connections}"
                + (s.Frames > 0 ? $"，漫游 {s.Frames} 帧 P99 {s.FrameP99:0.0} 毫秒、最长 {s.FrameMax:0.0} 毫秒" : ""));
        }

        if (DevCapture.Endure > 0 ? SoakSamples[^1].Minutes >= DevCapture.Endure : visit >= DevCapture.Soak)
        {
            SoakReport(maps.Count);
            return;
        }

        if (DevCapture.Endure > 0 && DevCapture.EndureLoadEvery > 0 && visit % DevCapture.EndureLoadEvery == 0)
        {
            // 读回本次到图时的快速存档：经读档路径（新会话、重新载入探索页）重进本图。
            var failed = false;
            GameMenu.LoadInto(SaveSlot.Quick, m =>
            {
                failed = true;
                SoakProblems.Add($"第 {visit} 次（{World.MapId}）：快速读档失败：{m}");
            });
            if (!failed)
            {
                _soakLoads++;
                return;
            }
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
        var frames = EndureFrames.Order().ToList();
        EndureFrames.Clear();
        return new SoakPoint(visit, World.MapId, Monitor(Performance.Monitor.ObjectNodeCount), Monitor(Performance.Monitor.ObjectCount),
            Monitor(Performance.Monitor.ObjectOrphanNodeCount), System.GC.GetTotalMemory(forceFullCollection: true), process.PrivateMemorySize64, process.WorkingSet64,
            PersistentConnections(), (Time.GetTicksMsec() - _soakStart) / 60000.0, frames.Count,
            frames.Count > 0 ? frames[Math.Min(frames.Count - 1, (int)(frames.Count * 0.99))] : 0, frames.Count > 0 ? frames[^1] : 0);
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
                if (!last.Any(s => s.Map == map))
                {
                    continue;
                }

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

            if (DevCapture.Endure > 0)
            {
                EndureTrend(steady);
                var p99 = (first.Where(s => s.Frames > 0).Select(s => s.FrameP99).DefaultIfEmpty().Average(),
                    last.Where(s => s.Frames > 0).Select(s => s.FrameP99).DefaultIfEmpty().Average());
                GD.Print($"[soak] 漫游帧耗时 P99 均值：首 {rounds} 轮 {p99.Item1:0.0} 毫秒，末 {rounds} 轮 {p99.Item2:0.0} 毫秒；"
                    + $"全程最长 {SoakSamples.Max(s => s.FrameMax):0.0} 毫秒，共 {SoakSamples.Sum(s => s.Frames)} 帧");
                if (p99.Item2 > p99.Item1 * 1.5 + 2)
                {
                    SoakProblems.Add($"漫游帧耗时变慢：P99 均值 {p99.Item1:0.0} → {p99.Item2:0.0} 毫秒");
                }
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

        if (DevCapture.Endure > 0)
        {
            GD.Print($"[soak] 窗口最小化（只走逻辑、不绘制）累计 {_endureHidden / 60:0.0} 分钟，这段时间的帧不计入帧耗时");
        }

        GD.Print($"[soak] 结束：到图 {DevCapture.SoakVisits} 次（其中快速读档重进 {_soakLoads} 次），用时 {(Time.GetTicksMsec() - _soakStart) / 1000.0:0} 秒，"
            + $"主线阶段 {MainStage()}，问题 {SoakProblems.Count} 处");
        GetTree().Quit(SoakProblems.Count == 0 ? 0 : 5);
    }

    /// <summary>
    /// 长时稳定性：稳定段内存与对象数随时间的增长斜率（按地图分组扣掉各图自身的均值后做最小二乘，避免不同地图的布景差异被当成增长）。
    /// 私有内存每小时增长超过 32 MB、托管内存超过 2 MB 记为问题。
    /// </summary>
    private static void EndureTrend(List<SoakPoint> steady)
    {
        double Slope(Func<SoakPoint, double> f)
        {
            double num = 0, den = 0;
            foreach (var g in steady.GroupBy(s => s.Map))
            {
                var t = g.Average(s => s.Minutes);
                var y = g.Average(f);
                foreach (var s in g)
                {
                    num += (s.Minutes - t) * (f(s) - y);
                    den += (s.Minutes - t) * (s.Minutes - t);
                }
            }

            return den > 0 ? num / den * 60 : 0;
        }

        var priv = Slope(s => s.Private / 1024.0 / 1024.0);
        var managed = Slope(s => s.Managed / 1024.0 / 1024.0);
        var working = Slope(s => s.WorkingSet / 1024.0 / 1024.0);
        GD.Print($"[soak] 每小时增长斜率（{steady.Count} 个采样，{steady[0].Minutes:0}–{steady[^1].Minutes:0} 分）：私有内存 {priv:+0.0;-0.0} MB，"
            + $"托管内存 {managed:+0.00;-0.00} MB，工作集 {working:+0.0;-0.0} MB，对象 {Slope(s => s.Objects):+0;-0}，节点 {Slope(s => s.Nodes):+0;-0}");
        if (priv > 32)
        {
            SoakProblems.Add($"私有内存每小时增长 {priv:0.0} MB（容差 32 MB）");
        }

        if (managed > 2)
        {
            SoakProblems.Add($"托管内存每小时增长 {managed:0.00} MB（容差 2 MB）");
        }
    }

    private string MainStage()
    {
        var main = Game.Rules.Content.Quests.Values.FirstOrDefault(q => q.Kind == QuestKind.Main);
        return main is not null && World.Quests.TryGetValue(main.Id, out var q) ? $"{q.Status}/{q.Stage}" : "无";
    }

    /// <summary>
    /// 等 n 帧。长时稳定性按逻辑帧等：窗口最小化时引擎不再绘制、<c>FramePostDraw</c> 不发，按画完的帧等会让走查停住
    /// （2026-10-05 首次 2 小时实跑第 5 次到图后窗口被最小化，就此卡住）；逻辑帧照常推进，走查与内存采样不中断。
    /// </summary>
    private async Task SoakFrames(int n)
    {
        for (var i = 0; i < n; i++)
        {
            if (DevCapture.Endure > 0)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            else
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
        }
    }
}
