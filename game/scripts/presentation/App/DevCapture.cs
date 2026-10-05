using Godot;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.App;

/// <summary>
/// 截图命令行：<c>Godot --path game -- --scene=res://… --tab=2 --capture=out.png --size=1920x1080</c>。
/// 游戏默认全屏，截图时切回窗口并按 <c>--size</c>（缺省 1920x1080）设窗口尺寸；引擎参数 --resolution 会被全屏设置盖过，不再用于截图。
/// 进入指定场景，等待布局稳定后按逻辑画布保存截图并退出；用于 M0 交付截图和界面自查。
/// 截图模式关闭界面动效（<see cref="Motion.Enabled"/>），画面直接落到终态；加 <c>--motion</c> 保留动效，
/// 配合 <c>--settle=帧数</c> 截取动画过程或落定后的画面，用于检查入场动画与排版是否冲突。未传参数时不做任何事。
/// 游戏流程（M2）：<c>--newgame</c> 启动即开新游戏进入探索页；<c>--autoplay=N</c> 让探索页按主线目标自动交互 N 步
/// （对话取第一个可选项、剧情战按贪心评分打完并确认结算），走完或主线完成后截图（若给了 --capture）并退出，
/// 同时在标准输出逐步打印 <c>[autoplay]</c> 记录，用于在真实引擎里走查整章流程。
/// </summary>
public static class DevCapture
{
    private static int _settleFrames = 30;
    private static bool _keepMotion;
    private static Vector2I _size = new(1920, 1080);

    public static string? Scene { get; private set; }
    public static int Tab { get; private set; }
    public static string? Output { get; private set; }

    /// <summary>展示页的光色时段（<c>--light=day|dusk|night</c>，M3-01 光影截图用）；游戏模式按世界时辰，不读此项。</summary>
    public static WuxiaWorld.Game.Preview.Pages.SceneTime? Light { get; private set; }

    /// <summary>
    /// 截图前依次模拟的输入：鼠标左键点击（逻辑画布坐标，<c>--click=x,y;x,y</c>）与按键（<c>--keys=Escape,C</c>）。
    /// 两个参数可各给多次，按命令行先后顺序执行，用于核对点击与按键交替的操作路径。
    /// </summary>
    private static readonly List<(Vector2? At, Key Key)> Steps = [];

    /// <summary>存档目录改到别处（<c>--saves=目录</c>），走查与存档界面测试不读写玩家真实存档。</summary>
    public static string? SaveDirectory { get; private set; }

    /// <summary>启动即开新游戏。</summary>
    public static bool NewGame { get; private set; }

    /// <summary>启动即读最近一份存档（同标题页“继续旅程”）。</summary>
    public static bool Continue { get; private set; }

    /// <summary>每 3 秒打印各音频总线的峰值电平（开发用，核对声音确实在播放）。</summary>
    public static bool AudioMeter { get; private set; }

    /// <summary>自动走查改为真实行走：寻路走到目标、注入 E / Enter / 数字键，而不是瞬移与直接调用（见 ExplorationScreen.Walk）。</summary>
    public static bool Walk { get; private set; }

    /// <summary>自动走查时先做支线（接委托、查船牌与潮痕、抢先救人）。</summary>
    public static bool Side { get; private set; }

    /// <summary>逐张地图核对摆放：落点能站人、每个交互点在交互距离内走得到；打印结果后退出，有问题时退出码为 3。</summary>
    public static bool CheckStaging { get; private set; }

    /// <summary>自动走查在讨教与同行选择中选哪位侠客（<c>--companion=linghu|huang|xiao</c>）；缺省取第一个选项。</summary>
    public static string? Companion { get; private set; }

    /// <summary>自动走查时第一场剧情战按战败暂退处理（核对战败、暂退与重新迎战的流程）。</summary>
    public static bool LoseFirst { get; set; }

    /// <summary>摆放核对发现的问题数（跨场景累计）。</summary>
    public static int StagingProblems { get; set; }

    /// <summary>
    /// 耐久走查（<c>--soak=N</c>，开发计划 6.1 T09 与 M2 验收）：在各地图间换图 N 次，每到一图注入按键开关札记、人物、行囊、队伍、
    /// 大地图与暂停菜单并快速存档，记录节点数、托管内存、进程私有内存与常驻对象的信号连接数；同时核对重进地图后交互点、站位人物
    /// 与世界状态不变（一次性交互不刷新）。可接在 <c>--autoplay</c> 之后，从章中状态开始。结束打印结论，有问题时退出码为 5。
    /// </summary>
    public static int Soak { get; private set; }

    /// <summary>
    /// 长时稳定性（<c>--endure=分钟</c>，开发计划 M3 验收“连续 2 小时无阻断异常”、架构文档 12.1 稳定性）：在耐久走查的基础上按时长连续跑——
    /// 每到一图开关各界面并快速存档后，用漫游的寻路在交互点之间走 <see cref="EndureStroll"/> 秒，再采样内存与这段漫游的帧耗时；
    /// 每 <see cref="EndureLoadEvery"/> 次到图改为按 F9 读回刚才的快速存档（走读档路径重进本图）。到时后按耐久走查同样的比较出结论，另算内存随时间的增长斜率。
    /// </summary>
    public static double Endure { get; private set; }

    /// <summary>长时稳定性每到一图漫游几秒（<c>--endure-stroll=秒</c>，缺省 45）。</summary>
    public static double EndureStroll { get; private set; } = 45;

    /// <summary>长时稳定性每几次到图改为快速读档一次（<c>--endure-load=N</c>，缺省 5；0 不读档）。</summary>
    public static int EndureLoadEvery { get; private set; } = 5;

    /// <summary>耐久走查已经到过的地图次数（跨场景累计）。</summary>
    public static int SoakVisits { get; set; }

    /// <summary>耐久走查已开始（自动走查结束后或新游戏进图即开始）。</summary>
    public static bool Soaking { get; set; }

    /// <summary>
    /// 探索漫游（<c>--stroll=秒</c>，开发用，性能测量的“典型探索”，开发计划 M3-07）：每张图上用真实寻路在交互点之间来回走，
    /// 不做交互、不开过场，走满给定秒数后经剧情换图票据去下一张图；各图走过一遍后退出。配合 <c>--perf</c> 记帧时间。
    /// </summary>
    public static double Stroll { get; private set; }

    /// <summary>漫游走几轮（<c>--stroll-rounds=2</c>，缺省 1）；第二轮起截图文件名带轮次。</summary>
    public static int StrollRounds { get; private set; } = 1;

    /// <summary>漫游每到一图先在落点截图存到给定目录（<c>--stroll-shots=目录</c>），用于核对性能改动前后画面一致。</summary>
    public static string? StrollShots { get; private set; }

    /// <summary>漫游每到一图先做布景分层归因（<c>--perf-layers</c>，见 ExplorationScreen.LayerBreakdown）。</summary>
    public static bool PerfLayers { get; private set; }

    /// <summary>4 对 6 战斗压测（<c>--battle-stress</c>，开发用，开发计划 M3-07）：战斗原型页直接开 4 名己方对 6 名敌人的自动战斗，按 1 倍速完整播放，打完换种子再开。</summary>
    public static bool BattleStress { get; private set; }

    /// <summary>
    /// 直接跳到第一章某处（<c>--jump=escort_battle</c>，开发用）：开新游戏后在规则层按 <see cref="Application.Dev.ChapterOneRoute"/> 瞬间走到该处
    /// （对话直接走完、战斗按胜利结算、主角潜能按推荐分配），再进入对应画面；配合 <c>--companion</c>、<c>--mentor</c>、<c>--side</c>、<c>--custody</c> 选路线，
    /// 可再接 <c>--autoplay</c> 从这里继续走查。位置见 <see cref="Application.Dev.ChapterOnePoint"/>，写法不分大小写、可带下划线。
    /// </summary>
    public static Application.Dev.ChapterOnePoint? Jump { get; private set; }

    /// <summary>
    /// 演出核对（<c>--cue=对话 ID/节点 ID[,…]</c>，开发用）：进探索页后不开剧情，直接在布景上依次播放这几段演出（各段之间停 1 秒），
    /// 配合 <c>--jump</c> 与 <c>--write-movie</c> 录像核对镜头与走位。
    /// </summary>
    public static string[] Cues { get; private set; } = [];

    /// <summary>演出核对时主角先站到摆放表的这个位置（<c>--cue-hero=anchor:sluice</c>），模拟玩家走到事件锚点后开演。</summary>
    public static string? CueHero { get; private set; }

    /// <summary>讨教人选（<c>--mentor=linghu|huang|xiao</c>，决定主角流派）；不给时同 <see cref="Companion"/>。</summary>
    public static string? Mentor { get; private set; }

    /// <summary>讨教后去后院切磋（<c>--spar=won|lost</c>，跳关时按该结果结算）；不给时不切磋。<c>--jump=spar_battle</c> 时停在切磋开打之前（缺省按 <c>won</c>；<c>--spar=none</c> 不切磋，停在讨教之后）。</summary>
    public static string? Spar { get; private set; }

    /// <summary>副页保管（<c>--custody=public|sealed</c>）。</summary>
    public static string? Custody { get; private set; }

    /// <summary>
    /// 战斗测试台（<c>--battle=escort|sluice|spar</c>，开发用）：直接进战斗原型页，按剧情路线的真实组成开打第一章押运队战、旧渡首领战或讨教后的后院切磋（对手为讨教人选），
    /// 路线由 <c>--companion</c>、<c>--mentor</c>、<c>--side</c> 决定；<c>--level</c>、<c>--variants</c>、<c>--seed</c> 覆盖等级、变体与种子。
    /// </summary>
    public static string? BenchBattle { get; private set; }

    /// <summary>战斗测试台的主角等级（<c>--level=1..8</c>）；不给时按剧情走到该处的等级。</summary>
    public static int? BenchLevel { get; private set; }

    /// <summary>战斗测试台的遭遇变体（<c>--variants=variant.ch01.sluice_jammed,…</c>，<c>none</c> 为不套用）；不给时按路线事实。</summary>
    public static IReadOnlyList<string>? BenchVariants { get; private set; }

    /// <summary>战斗测试台的随机种子（<c>--seed=N</c>）。</summary>
    public static ulong? BenchSeed { get; private set; }

    /// <summary>战斗测试台自动打完（<c>--battle-auto</c>）：开自动战斗、按 1 倍速演出，结束后打印战果并退出（胜 0、败 4）。</summary>
    public static bool BattleAuto { get; private set; }

    /// <summary>
    /// 自动走查的剧情战改为实时播放（<c>--battle-live</c>，开发用，M3-06）：开启自动战斗按 1 倍速完整演出，而不是跳过动画快进，
    /// 用于核对战斗喊声的字幕、配音与节奏。
    /// </summary>
    public static bool BattleLive { get; private set; }

    /// <summary>战斗喊声出现时截图存到给定目录（<c>--bark-shots=目录</c>，文件名为 line_id）。</summary>
    public static string? BarkShots { get; private set; }

    /// <summary>自动走查的步数；0 为不自动。</summary>
    public static int Autoplay { get; private set; }

    /// <summary>本次运行改用的正文字号（<c>--text-size=32</c>，核对最大字号下的版式）；不写入设置文件。</summary>
    public static int? TextSize { get; private set; }

    /// <summary>
    /// 自动走查走满步数后停在哪里截图：<c>battle</c> 下一场剧情战打到第 2 轮、<c>result</c> 下一场剧情战打完停在结算页、<c>choice</c> 下一个对话选项；
    /// <c>focus</c> 不截图，改跑焦点与版式走查（<see cref="FocusAudit"/>）；
    /// 缺省停在探索页。
    /// </summary>
    public static string? Hold { get; private set; }

    /// <summary>
    /// <c>--hold=line:&lt;line_id&gt;</c>：自动走查走到这句台词（或含这一项的选项）时停下截图（不看步数，<c>--autoplay</c> 给足步数即可），
    /// 用于核对某句台词显示时的立绘、姓名牌与版式。
    /// </summary>
    public static string? HoldLine => Autoplay > 0 && Hold is { } hold && hold.StartsWith("line:", StringComparison.Ordinal) ? hold["line:".Length..] : null;

    /// <summary><c>--hold=result:&lt;遭遇 ID&gt;</c>：自动走查打完这场剧情战时停在结算页截图（不看步数），例如讨教后一开局就打的后院切磋。</summary>
    public static string? HoldResult => Autoplay > 0 && Hold is { } hold && hold.StartsWith("result:", StringComparison.Ordinal) ? hold["result:".Length..] : null;

    /// <summary>自动走查已走的步数（探索页每做一次交互加一，跨场景累计）。</summary>
    public static int AutoplaySteps { get; set; }

    /// <summary>自动走查已走满步数，且要求停在 <paramref name="what"/> 上。</summary>
    public static bool Holding(string what) => Autoplay > 0 && AutoplaySteps >= Autoplay && Hold == what;

    public static void Parse()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            var (key, value) = arg.Split('=', 2) switch
            {
                [var k, var v] => (k, v),
                _ => (arg, ""),
            };
            switch (key)
            {
                case "--scene":
                    Scene = value;
                    break;
                case "--tab":
                    Tab = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--capture":
                    Output = value;
                    break;
                case "--light":
                    Light = value switch
                    {
                        "dusk" => WuxiaWorld.Game.Preview.Pages.SceneTime.Dusk,
                        "night" => WuxiaWorld.Game.Preview.Pages.SceneTime.Night,
                        _ => WuxiaWorld.Game.Preview.Pages.SceneTime.Day,
                    };
                    break;
                case "--motion":
                    _keepMotion = true;
                    break;
                case "--size" when value.Split('x') is [var w, var h]:
                    _size = new Vector2I(
                        int.Parse(w, System.Globalization.CultureInfo.InvariantCulture),
                        int.Parse(h, System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "--click":
                    foreach (var pair in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var xy = pair.Split(',');
                        Steps.Add((new Vector2(
                            float.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture),
                            float.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture)), Key.None));
                    }

                    break;
                case "--keys":
                    foreach (var name in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        Steps.Add((null, Enum.Parse<Key>(name, ignoreCase: true)));
                    }

                    break;
                case "--dev":
                    AppHost.DevInfo = true;
                    break;
                case "--saves":
                    SaveDirectory = value;
                    break;
                case "--newgame":
                    NewGame = true;
                    break;
                case "--continue":
                    Continue = true;
                    break;
                case "--audio-meter":
                    AudioMeter = true;
                    break;
                case "--walk":
                    Walk = true;
                    break;
                case "--zoom":
                    WuxiaWorld.Game.Preview.Pages.ExploreStage.DevZoom = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--drive":
                    // 例：--drive=wait:1,right:1.5,up:1,shift+right:1.5（wait 为原地不动）。
                    foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var (name, secs) = part.Split(':') is [var n, var t] ? (n, float.Parse(t, System.Globalization.CultureInfo.InvariantCulture)) : (part, 1f);
                        var run = name.StartsWith("shift+", StringComparison.Ordinal);
                        var dir = Vector2.Zero;
                        foreach (var d in name.Replace("shift+", "", StringComparison.Ordinal).Split('+'))
                        {
                            dir += d switch { "left" => Vector2.Left, "right" => Vector2.Right, "up" => Vector2.Up, "down" => Vector2.Down, _ => Vector2.Zero };
                        }

                        WuxiaWorld.Game.Preview.Pages.ExploreStage.DevDrive.Enqueue((dir, run, secs));
                    }

                    break;
                case "--side":
                    Side = true;
                    break;
                case "--companion":
                    Companion = value;
                    break;
                case "--lose-first":
                    LoseFirst = true;
                    break;
                case "--check-staging":
                    CheckStaging = true;
                    NewGame = true;
                    break;
                case "--soak":
                    Soak = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--endure":
                    Endure = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    Soak = int.MaxValue;
                    break;
                case "--endure-stroll":
                    EndureStroll = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--endure-load":
                    EndureLoadEvery = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--hold":
                    Hold = value;
                    break;
                case "--autoplay":
                    Autoplay = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--settle":
                    _settleFrames = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--perf":
                    PerfProbe.Output = value.Length > 0 ? value : "perf.json";
                    break;
                case "--perf-seconds":
                    PerfProbe.Seconds = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--perf-window":
                    PerfProbe.Window = value;
                    break;
                case "--perf-vsync":
                    PerfProbe.VsyncOff = value == "off";
                    break;
                case "--stroll":
                    Stroll = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    NewGame = !Continue;
                    break;
                case "--stroll-rounds":
                    StrollRounds = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--stroll-shots":
                    StrollShots = value;
                    break;
                case "--perf-layers":
                    PerfLayers = true;
                    break;
                case "--battle-stress":
                    BattleStress = true;
                    Scene = ScenePaths.BattlePrototype;
                    break;
                case "--jump":
                    Jump = Enum.Parse<Application.Dev.ChapterOnePoint>(value.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal), ignoreCase: true);
                    NewGame = true;
                    break;
                case "--cue-hero":
                    CueHero = value;
                    break;
                case "--cue":
                    Cues = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;
                case "--mentor":
                    Mentor = value;
                    break;
                case "--spar":
                    Spar = value;
                    break;
                case "--custody":
                    Custody = value;
                    break;
                case "--battle":
                    BenchBattle = value;
                    Scene = ScenePaths.BattlePrototype;
                    break;
                case "--level":
                    BenchLevel = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--variants":
                    BenchVariants = value == "none" ? [] : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;
                case "--seed":
                    BenchSeed = ulong.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--battle-auto":
                    BattleAuto = true;
                    break;
                case "--battle-live":
                    BattleLive = true;
                    break;
                case "--bark-shots":
                    BarkShots = value;
                    break;
                case "--text-size":
                    TextSize = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
            }
        }

        Motion.Enabled = Output is null || _keepMotion;
        if (Output is not null)
        {
            // 游戏默认全屏（跟随显示器分辨率），引擎参数 --windowed / --resolution 会被项目设置盖过；
            // 截图时先切回窗口，窗口尺寸等切换完成后在 CaptureAndQuit 里按 --size 设定。
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        }
    }

    public static async void CaptureAndQuit(SceneTree tree)
    {
        if (Output is null)
        {
            return;
        }

        // 窗口模式切换要过几帧才落定，之后再设尺寸，否则会被还原成切换前记下的窗口尺寸。
        for (var i = 0; i < 3; i++)
        {
            await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }

        DisplayServer.WindowSetSize(_size);
        if (Autoplay > 0)
        {
            // 自动走查由探索页在走完后调用 FinishAutoplay 截图退出。
            return;
        }

        for (var i = 0; i < _settleFrames; i++)
        {
            await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }

        await PressSteps(tree);
        var image = tree.Root.GetTexture().GetImage();
        var error = image.SavePng(Output);
        GD.Print(error == Error.Ok ? $"截图已保存：{Output}" : $"截图失败：{error}");
        tree.Quit(error == Error.Ok ? 0 : 1);
    }

    /// <summary>
    /// 按命令行顺序注入 <c>--click</c> 与 <c>--keys</c>：点击把逻辑画布坐标换算成窗口坐标，按键用 Godot 键名，
    /// 都与真实输入走同一条路径，每步之后等 20 帧。
    /// </summary>
    private static async Task PressSteps(SceneTree tree)
    {
        foreach (var (at, key) in Steps)
        {
            if (at is { } point)
            {
                var screen = tree.Root.GetFinalTransform() * point;
                foreach (var pressed in new[] { true, false })
                {
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = screen, GlobalPosition = screen });
                }

                GD.Print($"模拟点击 {point}");
            }
            else
            {
                foreach (var pressed in new[] { true, false })
                {
                    Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
                }

                GD.Print($"模拟按键 {key}");
            }

            for (var i = 0; i < 20; i++)
            {
                await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
        }
    }

    /// <summary>自动走查结束：等画面落定后截图（若给了 --capture）并退出。</summary>
    public static async void FinishAutoplay(SceneTree tree, int exitCode)
    {
        var start = Time.GetTicksMsec();
        for (var i = 0; i < _settleFrames; i++)
        {
            await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }

        GD.Print($"走查结束后等待 {_settleFrames} 帧，用时 {(Time.GetTicksMsec() - start) / 1000.0:0.0} 秒");
        await PressSteps(tree);
        if (Output is not null)
        {
            var error = tree.Root.GetTexture().GetImage().SavePng(Output);
            GD.Print(error == Error.Ok ? $"截图已保存：{Output}" : $"截图失败：{error}");
            exitCode = error == Error.Ok ? exitCode : 1;
        }

        tree.Quit(exitCode);
    }
}
