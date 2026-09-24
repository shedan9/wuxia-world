using Godot;
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

    public override void _EnterTree()
    {
        Instance = this;
        DevCapture.Parse();
        GetTree().Root.Theme = UiTheme.Build();
        Router = new SceneRouter();
        AddChild(Router);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // M0 展示包：任意预览页按取消键都回到标题；标题页自己处理取消键（关闭弹层）。
        if (@event.IsActionPressed("ui_cancel") && !Router.IsAt(ScenePaths.MainMenu))
        {
            Router.GoTo(ScenePaths.MainMenu);
            GetViewport().SetInputAsHandled();
        }
    }
}
