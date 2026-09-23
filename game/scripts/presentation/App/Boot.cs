using Godot;

namespace WuxiaWorld.Game.Presentation.App;

/// <summary>启动场景。M0 直接进入场景目录；第二阶段改为进入主菜单。</summary>
public partial class Boot : Node
{
    public override void _Ready()
    {
        AppHost.Instance.Router.GoTo(DevCapture.Scene ?? ScenePaths.PreviewCatalog);
        DevCapture.CaptureAndQuit(GetTree());
    }
}
