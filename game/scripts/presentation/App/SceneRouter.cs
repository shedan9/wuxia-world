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

    public SceneRouter()
    {
        Layer = 100;
        _curtain.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _curtain.Modulate = Colors.Transparent;
        AddChild(_curtain);
    }

    public string? CurrentPath => GetTree().CurrentScene?.SceneFilePath;

    public bool IsAt(string scenePath) => CurrentPath == scenePath;

    public static bool CanGoTo(string scenePath) => ResourceLoader.Exists(scenePath);

    public void GoTo(string scenePath)
    {
        if (!CanGoTo(scenePath))
        {
            GD.PushError($"场景不存在：{scenePath}");
            return;
        }

        if (_busy)
        {
            return;
        }

        var tree = GetTree();
        if (!Motion.Enabled)
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
        }));
    }
}
