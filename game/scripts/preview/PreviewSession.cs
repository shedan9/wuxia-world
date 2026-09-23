namespace WuxiaWorld.Game.Preview;

/// <summary>
/// M0 展示包的样例数据来源（架构文档 1.3）。只驱动 UI 显示，不含规则计算；
/// 第二阶段由应用服务提供同样形状的显示模型后，此类连同 preview 目录一起退役。
/// 样例对白不写在这里，须存入 docs/dialogue 的章节文件并标明未锁稿。
/// </summary>
public sealed class PreviewSession
{
    public static PreviewSession Current { get; } = new();

    private PreviewSession()
    {
    }
}
