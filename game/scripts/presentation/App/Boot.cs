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
            else if (DevCapture.Jump is { } point)
            {
                JumpTo(point);
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

    /// <summary>开发参数 <c>--jump</c>：在规则层沿第一章路线走到指定位置；走不通时报错并回到标题页。</summary>
    private static void JumpTo(Application.Dev.ChapterOnePoint point)
    {
        var play = AppHost.Instance.Play!;
        try
        {
            var route = new Application.Dev.ChapterOneRoute(play.Game)
            {
                Companion = DevCapture.Companion ?? "linghu", Mentor = DevCapture.Mentor, Side = DevCapture.Side, Custody = DevCapture.Custody ?? "public",
                Spar = DevCapture.Spar is "none" ? null : DevCapture.Spar ?? (point == Application.Dev.ChapterOnePoint.SparBattle ? "won" : null),
                AllocatePotential = true,
            };
            var started = Time.GetTicksMsec();
            route.RunTo(point);
            GD.Print($"[jump] 已到 {point}（{route.Companion}{(route.Side ? " + 支线" : "")}）：{play.Game.World.MapId}，主角 {play.Game.Growth!.Level(play.Game.World)} 级，"
                + $"潜能按推荐分配，用时 {Time.GetTicksMsec() - started} 毫秒" + (play.Game.World.Battle is { } b ? $"，待开战斗 {b.Encounter}" : ""));
        }
        catch (InvalidOperationException ex)
        {
            GD.PushError($"跳关失败：{ex.Message}");
            AppHost.Instance.Play = null;
        }
    }
}
