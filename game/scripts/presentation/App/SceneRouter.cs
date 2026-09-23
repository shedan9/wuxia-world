using Godot;

namespace WuxiaWorld.Game.Presentation.App;

/// <summary>
/// 场景切换入口。M0 直接切换场景；M2 起改为 ResourceLoader.LoadThreadedRequest
/// 异步加载并接入旅行事务（架构文档 6.3），调用方接口保持不变。
/// </summary>
public sealed class SceneRouter
{
    private readonly SceneTree _tree;

    public SceneRouter(SceneTree tree) => _tree = tree;

    public string? CurrentPath => _tree.CurrentScene?.SceneFilePath;

    public bool IsAt(string scenePath) => CurrentPath == scenePath;

    public static bool CanGoTo(string scenePath) => ResourceLoader.Exists(scenePath);

    public void GoTo(string scenePath)
    {
        if (!CanGoTo(scenePath))
        {
            GD.PushError($"场景不存在：{scenePath}");
            return;
        }

        _tree.CallDeferred(SceneTree.MethodName.ChangeSceneToFile, scenePath);
    }
}
