using Godot;
using WuxiaWorld.Game.Presentation.Play;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.App;

/// <summary>
/// 应用宿主，唯一的全局持久节点（架构文档 5.1）。持有场景路由；
/// 后续的音频管理和启动装配也挂在这里，不为各模块另建单例。
/// </summary>
public partial class AppHost : Node
{
    public static AppHost Instance { get; private set; } = null!;

    public SceneRouter Router { get; private set; } = null!;

    /// <summary>进行中的一局游戏（M2）；标题页、场景目录与 M0 展示页时为 null。</summary>
    public PlaySession? Play { get; set; }

    public override void _EnterTree()
    {
        Instance = this;
        DevCapture.Parse();
        GetTree().Root.Theme = UiTheme.Build();
        Router = new SceneRouter();
        AddChild(Router);
    }

    public override void _Input(InputEvent @event)
    {
        // 游戏默认全屏（跟随显示器分辨率）；Alt+Enter 在全屏与窗口之间切换，先于各页的确认键处理。
        if (@event is InputEventKey { Pressed: true, Echo: false, AltPressed: true } key
            && key.Keycode is Key.Enter or Key.KpEnter)
        {
            var fullscreen = DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;
            if (fullscreen)
            {
                EnterWindowed();
            }
            else
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            }

            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// 切到窗口：窗口按所在屏幕可用区域（扣除任务栏）的 80% 宽高取尺寸并居中，不固定像素，4K 与 1080p 屏上观感一致；
    /// 逻辑画布按 expand 适配窗口宽高比。模式切换要过几帧才落定，之后再设尺寸，否则会被还原成切换前记下的窗口尺寸。
    /// </summary>
    private async void EnterWindowed()
    {
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        for (var i = 0; i < 3; i++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }

        var area = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
        var size = new Vector2I((int)(area.Size.X * 0.8f), (int)(area.Size.Y * 0.8f));
        DisplayServer.WindowSetSize(size);
        DisplayServer.WindowSetPosition(area.Position + (area.Size - size) / 2);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // M0 展示包：任意预览页按取消键都回到标题；标题页自己处理取消键（关闭弹层）。
        // 游戏内的探索与剧情战斗自己处理取消键（菜单），不会落到这里。
        if (@event.IsActionPressed("ui_cancel") && !Router.IsAt(ScenePaths.MainMenu))
        {
            Router.GoTo(ScenePaths.MainMenu);
            GetViewport().SetInputAsHandled();
        }
    }
}
