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

    /// <summary>启动即开新游戏。</summary>
    public static bool NewGame { get; private set; }

    /// <summary>启动即读最近一份存档（同标题页“继续旅程”）。</summary>
    public static bool Continue { get; private set; }

    /// <summary>自动走查时先做支线（接委托、查船牌与潮痕、抢先救人）。</summary>
    public static bool Side { get; private set; }

    /// <summary>逐张地图核对摆放：落点能站人、每个交互点在交互距离内走得到；打印结果后退出，有问题时退出码为 3。</summary>
    public static bool CheckStaging { get; private set; }

    /// <summary>自动走查时第一场剧情战按战败暂退处理（核对战败、暂退与重新迎战的流程）。</summary>
    public static bool LoseFirst { get; set; }

    /// <summary>摆放核对发现的问题数（跨场景累计）。</summary>
    public static int StagingProblems { get; set; }

    /// <summary>自动走查的步数；0 为不自动。</summary>
    public static int Autoplay { get; private set; }

    /// <summary>
    /// 自动走查走满步数后停在哪里截图：<c>battle</c> 下一场剧情战打到第 2 轮、<c>choice</c> 下一个对话选项；
    /// 缺省停在探索页。
    /// </summary>
    public static string? Hold { get; private set; }

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
                case "--motion":
                    _keepMotion = true;
                    break;
                case "--size" when value.Split('x') is [var w, var h]:
                    _size = new Vector2I(
                        int.Parse(w, System.Globalization.CultureInfo.InvariantCulture),
                        int.Parse(h, System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "--newgame":
                    NewGame = true;
                    break;
                case "--continue":
                    Continue = true;
                    break;
                case "--side":
                    Side = true;
                    break;
                case "--lose-first":
                    LoseFirst = true;
                    break;
                case "--check-staging":
                    CheckStaging = true;
                    NewGame = true;
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

        var image = tree.Root.GetTexture().GetImage();
        var error = image.SavePng(Output);
        GD.Print(error == Error.Ok ? $"截图已保存：{Output}" : $"截图失败：{error}");
        tree.Quit(error == Error.Ok ? 0 : 1);
    }

    /// <summary>自动走查结束：等画面落定后截图（若给了 --capture）并退出。</summary>
    public static async void FinishAutoplay(SceneTree tree, int exitCode)
    {
        for (var i = 0; i < _settleFrames; i++)
        {
            await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }

        if (Output is not null)
        {
            var error = tree.Root.GetTexture().GetImage().SavePng(Output);
            GD.Print(error == Error.Ok ? $"截图已保存：{Output}" : $"截图失败：{error}");
            exitCode = error == Error.Ok ? exitCode : 1;
        }

        tree.Quit(exitCode);
    }
}
