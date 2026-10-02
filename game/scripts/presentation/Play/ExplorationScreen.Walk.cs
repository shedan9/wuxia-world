using Godot;

namespace WuxiaWorld.Game.Presentation.Play;

/// <summary>
/// 真实行走的自动走查（<c>--autoplay=N --walk</c>，开发用）：不再把主角瞬移到目标旁，而是用布景的寻路
/// （与玩家鼠标点地同一套，见 <c>ExploreStage.Navigation</c>）一路走过去，走到后注入真实的 E 键；码头打开的大地图注入 Enter 启程。
/// 走不到、卡住、到了却被别的交互点抢先高亮，都打印 <c>[walk]</c> 问题并计数（结束时退出码 4），随后兜底继续，
/// 一次走查能把整章的问题都列出来。对话层在此模式下同样改为注入 Enter / 数字键推进。
/// </summary>
public partial class ExplorationScreen
{
    private string? _walkKey;
    private double _keyDelay;
    private bool _stuckHooked;

    /// <summary>真实行走走查累计的问题数（跨场景）。</summary>
    public static int WalkProblems { get; private set; }

    private static void Problem(string text)
    {
        WalkProblems++;
        GD.Print($"[walk] 问题：{text}");
    }

    /// <summary>开始走向一个交互点；返回 false 表示走不到（已记问题，调用方兜底瞬移）。</summary>
    private bool BeginWalk(string key, Vector2 target)
    {
        if (!_stuckHooked)
        {
            _stuckHooked = true;
            _view.RouteStuck += (hero, next) => Problem($"{World.MapId} 走向 {_walkKey} 时卡在 {hero.Round()}（下一格 {next.Round()}），已重新寻路");
        }

        var item = Interactions.FirstOrDefault(i => i.Id == key);
        if (item is null || !_view.WalkToInteraction(item, interact: false))
        {
            Problem($"{World.MapId} 从 {_view.HeroGround.Round()} 走不到 {key}（{target.Round()}）");
            return false;
        }

        _walkKey = key;
        GD.Print($"[walk] {World.MapId} 走向 {key}");
        return true;
    }

    /// <summary>每帧推进行走；到达后注入 E。返回 true 表示这一步还在进行或刚交给交互处理。</summary>
    private bool StepWalk(double delta)
    {
        if (_walkKey is not { } key)
        {
            return false;
        }

        if (_view.AutoWalking)
        {
            return true;
        }

        _walkKey = null;
        var near = _view.NearInteraction;
        if (near?.Id != key)
        {
            Problem($"{World.MapId} 到了 {key} 旁，高亮的却是 {near?.Id ?? "（无）"}");
            Act(key);
            return true;
        }

        PressKey(Key.E);
        _autoWait = 0.6;
        return true;
    }

    /// <summary>注入一次真实按键（按下与抬起），与玩家按键走同一条输入路径。</summary>
    public static void PressKey(Key key)
    {
        foreach (var pressed in new[] { true, false })
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
        }
    }

    /// <summary>大地图开着时：选中的目的地可前往就稍候注入 Enter（默认方式为第一种可用的）；行进中只等。</summary>
    private bool StepTravelPanel(double delta)
    {
        if (_worldMap is null)
        {
            return false;
        }

        _keyDelay += delta;
        if (_keyDelay > 0.6 && _worldMap.ReadyToDepart)
        {
            _keyDelay = 0;
            PressKey(Key.Enter);
        }

        return true;
    }
}
