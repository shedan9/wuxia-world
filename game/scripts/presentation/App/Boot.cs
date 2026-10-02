using Godot;

namespace WuxiaWorld.Game.Presentation.App;

/// <summary>启动场景。进入标题页（主菜单）；标题页可进入 M0 场景目录。</summary>
public partial class Boot : Node
{
    public override void _Ready()
    {
        if (DevCapture.NewGame)
        {
            AppHost.Instance.Play = Play.PlaySession.NewGame(out var error);
            if (error is not null)
            {
                GD.PushError($"无法开始新游戏：{error}");
            }
        }

        if (DevCapture.Continue && Play.PlaySession.Latest(Play.PlaySession.OpenStore()) is { } latest)
        {
            AppHost.Instance.Play = Play.PlaySession.Load(latest.Slot, out var error, out var notes);
            GD.Print(error is null ? $"已读取 {latest.Slot.Stem}：{string.Join("；", notes)}" : $"读档失败：{error}");
        }

        var playing = AppHost.Instance.Play is not null;
        AppHost.Instance.Router.GoTo(DevCapture.Scene ?? (playing ? ScenePaths.Exploration : ScenePaths.MainMenu));
        DevCapture.CaptureAndQuit(GetTree());
    }
}
