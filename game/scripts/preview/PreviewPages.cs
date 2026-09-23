namespace WuxiaWorld.Game.Preview;

/// <summary>M0 场景目录中的一个展示页。ScenePath 为空表示该页尚未制作。</summary>
public sealed record PreviewPage(string Id, string Title, string Focus, string? ScenePath = null);

/// <summary>展示页清单，与开发计划第 1.2 节一一对应；增删页面时同步文档。</summary>
public static class PreviewPages
{
    public static readonly IReadOnlyList<PreviewPage> All =
    [
        new("preview.main_menu", "主菜单与存档页", "标题、背景、菜单层级、存档卡片"),
        new("preview.world_map", "江湖大地图与交通面板", "山川城镇、路线、标记、坐骑/载具信息"),
        new("preview.explore_town", "城镇 / 街道探索", "斜俯视、建筑比例、人物大小、探索 HUD"),
        new("preview.explore_inn", "客栈 / 室内探索", "遮挡、室内构图、近景材质与灯光"),
        new("preview.explore_wild", "山路 / 野外探索", "植被、山石、前后层次与路径辨识"),
        new("preview.dialogue", "人物对话", "跨作品人物同场、立绘、姓名、文本、选项"),
        new("preview.battle", "回合战斗", "独立侧视、站位、行动条、技能栏、状态"),
        new("preview.character", "角色 / 武学 / 成长", "属性层级、招式说明、装配和升级反馈"),
        new("preview.inventory", "背包 / 装备 / 商店", "道具图标、分类、装备对比、交易面板"),
        new("preview.journal", "任务 / 关系 / 见闻", "目标说明、人物关系与已知信息层级"),
        new("preview.settings", "暂停 / 设置", "文字大小、音量、分辨率选项与返回路径"),
    ];
}
