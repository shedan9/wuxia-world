using Godot;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 探索 HUD 展示页（界面层），版式见 docs/art/UI_DESIGN.md 第 5.3 节：左上地点与时辰、
/// 右上小地图、左侧目标追踪、左下队伍、中下交互提示、上方通知、右下快捷键。
/// 2026-09-30 三类探索布景完成后并入城镇布景：直接叠在“芦湾河街”旧渡石痕旁（可行走，与城镇页相同），
/// 进入时推两条样例通知，E 在石痕旁再推一条。截图参数 <c>--tab</c>：0 常态、1 隐藏追踪只留提示。
/// </summary>
public partial class ExploreHudPreview : ExploreTownPreview
{
    protected override string Caption => "探索 HUD 界面层：叠在芦湾河街布景上核对地点、小地图、目标追踪、队伍、交互提示与通知（M0-05）";

    protected override (Vector2 Hero, Vector2 Lu, float Zoom) Start(string? arrival) =>
        (TownSamples.Spawn, TownSamples.Spawn + new Vector2(-120, -40), 1f);

    protected override Control Tracker() => DevCapture.Tab == 1 ? new Control() : base.Tracker();

    public override void _Ready()
    {
        base._Ready();
        Toast("见闻", "已记录：亲见的旧渡石痕", "札记 → 见闻");
        Toast("物品", "获得：旧照片残片", "任务物品");
    }
}
