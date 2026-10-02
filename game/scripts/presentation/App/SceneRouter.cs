using Godot;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.App;

/// <summary>
/// 场景切换入口，本身是最上层的幕布层：切换时经玄黛色幕淡出淡入（docs/art/UI_DESIGN.md 第 6 节）。
/// M0 直接切换场景；M2 起改为 ResourceLoader.LoadThreadedRequest
/// 异步加载并接入旅行事务（架构文档 6.3），调用方接口保持不变。
/// </summary>
public partial class SceneRouter : CanvasLayer
{
    private const float FadeOut = 0.18f;
    private const float FadeIn = 0.32f;

    private readonly ColorRect _curtain = new() { Color = UiPalette.Abyss, MouseFilter = Control.MouseFilterEnum.Ignore };
    private bool _busy;
    private (string Path, bool Instant)? _queued;

    public SceneRouter()
    {
        Layer = 100;
        _curtain.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _curtain.Modulate = Colors.Transparent;
        AddChild(_curtain);
    }

    public string? CurrentPath => GetTree().CurrentScene?.SceneFilePath;

    public bool IsAt(string scenePath) => CurrentPath == scenePath;

    /// <summary>色幕正在淡出淡入（此时不开暂停菜单，免得把幕布停在半截）。</summary>
    public bool Busy => _busy;

    public static bool CanGoTo(string scenePath) => ResourceLoader.Exists(scenePath);

    /// <summary>当前场景是否经 <c>instant</c> 切换进入；为 true 时新场景应跳过入场动效，直接呈现终态。</summary>
    public bool ArrivedInstantly { get; private set; }

    /// <summary>
    /// 切换场景。默认经色幕淡出淡入；<paramref name="instant"/> 为 true 时立即切换、不走色幕，
    /// 用于同一外框内的并列页面（菜单分区 Q / E），切换时不应有明暗闪烁。
    /// </summary>
    public void GoTo(string scenePath, bool instant = false)
    {
        if (!CanGoTo(scenePath))
        {
            GD.PushError($"场景不存在：{scenePath}");
            return;
        }

        if (_busy)
        {
            // 幕布还没走完：记下最后一次请求，幕布收起后再切；不丢请求（游戏流程里换图票据已开，丢了会卡住）。
            _queued = (scenePath, instant);
            return;
        }

        var tree = GetTree();
        ArrivedInstantly = instant;
        if (instant || !Motion.Enabled)
        {
            tree.CallDeferred(SceneTree.MethodName.ChangeSceneToFile, scenePath);
            return;
        }

        _busy = true;
        _curtain.MouseFilter = Control.MouseFilterEnum.Stop;
        var tween = _curtain.CreateTween();
        tween.TweenProperty(_curtain, "modulate:a", 1f, FadeOut);
        tween.TweenCallback(Callable.From(() => tree.ChangeSceneToFile(scenePath)));
        tween.TweenInterval(0.05f);
        tween.TweenProperty(_curtain, "modulate:a", 0f, FadeIn).SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(() =>
        {
            _busy = false;
            _curtain.MouseFilter = Control.MouseFilterEnum.Ignore;
            if (_queued is { } next)
            {
                _queued = null;
                GoTo(next.Path, next.Instant);
            }
        }));
    }
}
