using Godot;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.App;

/// <summary>
/// 截图命令行：<c>Godot --path game -- --scene=res://… --tab=2 --capture=out.png --size=1920x1080</c>。
/// 游戏默认全屏，截图时切回窗口并按 <c>--size</c>（缺省 1920x1080）设窗口尺寸；引擎参数 --resolution 会被全屏设置盖过，不再用于截图。
/// 进入指定场景，等待布局稳定后按逻辑画布保存截图并退出；用于 M0 交付截图和界面自查。
/// 截图模式关闭界面动效（<see cref="Motion.Enabled"/>），画面直接落到终态；加 <c>--motion</c> 保留动效，
/// 配合 <c>--settle=帧数</c> 截取动画过程或落定后的画面，用于检查入场动画与排版是否冲突。未传参数时不做任何事。
/// </summary>
public static class DevCapture
{
    private static int _settleFrames = 30;
    private static bool _keepMotion;
    private static Vector2I _size = new(1920, 1080);

    public static string? Scene { get; private set; }
    public static int Tab { get; private set; }
    public static string? Output { get; private set; }

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
        for (var i = 0; i < _settleFrames; i++)
        {
            await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }

        var image = tree.Root.GetTexture().GetImage();
        var error = image.SavePng(Output);
        GD.Print(error == Error.Ok ? $"截图已保存：{Output}" : $"截图失败：{error}");
        tree.Quit(error == Error.Ok ? 0 : 1);
    }
}
