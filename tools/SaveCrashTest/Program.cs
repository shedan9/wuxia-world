using System.Diagnostics;
using System.Globalization;
using WuxiaWorld.Application.Persistence;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Infrastructure.Saves;

// 人为中断写档测试（开发计划 M2-07 验收“人为中断写档后能恢复上一份有效备份；不覆盖最后有效文件”）。
// 父进程反复启动子进程；子进程先读回槽位，再对同一槽位连续写档，每写成一份打印“ok 序号”；
// 父进程在随机时刻强杀子进程（TerminateProcess，等同任务管理器结束进程），随后用同一套存档代码读槽位，
// 要求：读得出、校验通过、序号不小于子进程最后确认写成的那份。进程级中断不等于断电（系统缓存未落盘），断电另测。
var options = Options.Parse(args);
if (options.Child)
{
    return Child.Run(options);
}

var dir = Path.GetFullPath(options.Directory);
if (Directory.Exists(dir))
{
    Directory.Delete(dir, recursive: true);
}

Directory.CreateDirectory(dir);
var rng = new Random(options.Seed);
var store = new FileSaveStore(dir);
var self = Environment.ProcessPath ?? throw new InvalidOperationException("取不到本程序路径");
long confirmed = 0;
int failures = 0, fromBackup = 0, leftoverTemp = 0, beforeFirstWrite = 0, writes = 0;
var sw = Stopwatch.StartNew();
for (var round = 1; round <= options.Rounds; round++)
{
    var delay = rng.Next(options.MinMs, options.MaxMs);
    long acked = 0;
    long started = -1;
    var psi = new ProcessStartInfo(self)
    {
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    foreach (var a in new[] { "--child", "--dir", dir, "--size", options.SizeKb.ToString(CultureInfo.InvariantCulture) })
    {
        psi.ArgumentList.Add(a);
    }

    using var child = Process.Start(psi) ?? throw new InvalidOperationException("子进程启动失败");
    var problems = new List<string>();
    child.OutputDataReceived += (_, e) =>
    {
        if (e.Data is not { } line)
        {
            return;
        }

        var parts = line.Split(' ', 3);
        switch (parts[0])
        {
            case "ok":
                Interlocked.Exchange(ref acked, long.Parse(parts[1], CultureInfo.InvariantCulture));
                Interlocked.Increment(ref writes);
                break;
            case "start":
                Interlocked.Exchange(ref started, long.Parse(parts[1], CultureInfo.InvariantCulture));
                break;
            default:
                lock (problems)
                {
                    problems.Add(line);
                }

                break;
        }
    };
    child.BeginOutputReadLine();
    Thread.Sleep(delay);
    try
    {
        child.Kill();
    }
    catch (InvalidOperationException)
    {
        // 已自行退出（不应发生：子进程写到被杀为止）。
        problems.Add("子进程在被杀前已退出");
    }

    child.WaitForExit();

    // 子进程启动时读回的序号也须不小于上一轮确认的那份。
    var start = Interlocked.Read(ref started);
    if (start >= 0 && start < confirmed)
    {
        problems.Add($"子进程开局读回序号 {start}，小于已确认的 {confirmed}");
    }

    var last = Interlocked.Read(ref acked);
    if (last == 0)
    {
        beforeFirstWrite++;
    }

    confirmed = Math.Max(confirmed, last);
    leftoverTemp += File.Exists(store.PathOf(Child.Slot) + ".tmp") ? 1 : 0;
    var read = store.Read(Child.Slot);
    if (read.Game is null)
    {
        if (confirmed > 0)
        {
            problems.Add($"读档失败：{read.Error}");
        }
    }
    else
    {
        fromBackup += read.FromBackup ? 1 : 0;
        var seq = read.Game.Header.Sequence;
        if (seq < confirmed)
        {
            problems.Add($"读回序号 {seq}，小于已确认的 {confirmed}（最后有效文件被覆盖）");
        }

        if (read.Game.World.Facts.GetValueOrDefault("crash.seq") != seq.ToString(CultureInfo.InvariantCulture))
        {
            problems.Add($"读回的世界与存档头不符（序号 {seq}）");
        }

        confirmed = Math.Max(confirmed, seq);
    }

    if (problems.Count > 0)
    {
        failures++;
        Console.WriteLine($"[crash] 第 {round} 轮（{delay} ms 后强杀）：{string.Join("；", problems)}");
    }
    else if (options.Verbose)
    {
        Console.WriteLine($"[crash] 第 {round} 轮：{delay} ms 后强杀，确认至 {last}，读回 {read.Game?.Header.Sequence}{(read.FromBackup ? "（备份）" : "")}");
    }
}

Console.WriteLine(
    $"[crash] {options.Rounds} 轮，存档 {new FileInfo(store.PathOf(Child.Slot)).Length / 1024} KB，累计写成 {writes} 份，用时 {sw.Elapsed.TotalSeconds:0.0} 秒；"
    + $"强杀落在首份写成之前 {beforeFirstWrite} 轮，留下临时文件 {leftoverTemp} 轮，读档改用备份 {fromBackup} 轮；失败 {failures} 轮");
return failures == 0 ? 0 : 1;

internal static class Child
{
    public static readonly SaveSlot Slot = SaveSlot.Manual(1);

    public static int Run(Options o)
    {
        var store = new FileSaveStore(o.Directory);
        var read = store.Read(Slot);
        var seq = read.Game?.Header.Sequence ?? 0;
        Console.WriteLine($"start {seq} {(read.FromBackup ? "backup" : "main")}");
        while (true)
        {
            seq++;
            var world = new WorldState { ArcId = "arc01", ChapterId = "chapter01", MapId = "map.crash", SpawnId = "spawn", Clock = seq, Revision = seq, Silver = (int)(seq % 1000) };
            world.Facts["crash.seq"] = seq.ToString(CultureInfo.InvariantCulture);
            var pad = new string('潮', 40);
            for (var i = 0; i < o.SizeKb * 8; i++)
            {
                world.Facts[$"crash.pad.{i:00000}"] = pad;
            }

            var header = new SaveHeader { Sequence = seq, ArcId = world.ArcId, ChapterId = world.ChapterId, MapId = world.MapId, Clock = world.Clock };
            var r = store.Write(Slot, new SaveGame { Header = header, World = world });
            Console.WriteLine(r.Ok ? $"ok {seq}" : $"fail {seq} {r.Error}");
        }
    }
}

internal sealed record Options(bool Child, string Directory, int Rounds, int SizeKb, int MinMs, int MaxMs, int Seed, bool Verbose)
{
    public static Options Parse(string[] args)
    {
        var o = new Options(false, "build/savecrash", 200, 16, 40, 600, 20261003, false);
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => args[++i];
            int Int() => int.Parse(Next(), CultureInfo.InvariantCulture);
            o = args[i] switch
            {
                "--child" => o with { Child = true },
                "--dir" => o with { Directory = Next() },
                "--rounds" => o with { Rounds = Int() },
                "--size" => o with { SizeKb = Int() },
                "--min-ms" => o with { MinMs = Int() },
                "--max-ms" => o with { MaxMs = Int() },
                "--seed" => o with { Seed = Int() },
                "--verbose" => o with { Verbose = true },
                _ => throw new ArgumentException($"未知参数 {args[i]}"),
            };
        }

        return o;
    }
}
