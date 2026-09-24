using Godot;

namespace WuxiaWorld.Game.Presentation.App;

/// <summary>启动场景。进入标题页（主菜单）；标题页可进入 M0 场景目录。</summary>
public partial class Boot : Node
{
    public override void _Ready()
    {
        AppHost.Instance.Router.GoTo(DevCapture.Scene ?? ScenePaths.MainMenu);
        DevCapture.CaptureAndQuit(GetTree());
    }
}
