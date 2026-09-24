using Godot;

namespace WuxiaWorld.Game.Presentation;

/// <summary>
/// UI 语义色与尺寸，取自架构文档 10.4 与 docs/art/UI_DESIGN.md；调整时同步文档。
/// 2026-09-24 第三版“绢本青绿”：山水保持青绿，界面改用青绿山水画自身的颜料——
/// 绢色纸面、黛色深面、朱砂印、赭石与泥金——让暖面与冷景互衬，不再处处同一青绿色。
/// 对比度按 WCAG 相对亮度核算，正文不低于 4.5:1。
/// </summary>
public static class UiPalette
{
    /// <summary>绢：浅色阅读面（人物、行囊、札记、设置），暖黄绢色。</summary>
    public static readonly Color Surface = Color.FromHtml("#EFE5CC");
    /// <summary>绢影：绢面上的选中 / 悬停底与渐变终点。</summary>
    public static readonly Color SurfaceShade = Color.FromHtml("#E2D2AC");
    /// <summary>墨：绢面正文，对绢约 11.8:1。</summary>
    public static readonly Color Text = Color.FromHtml("#2A2822");
    /// <summary>淡墨：绢面次级文字，对绢约 5.9:1、对绢影约 4.9:1。</summary>
    public static readonly Color TextMuted = Color.FromHtml("#5E5546");
    /// <summary>黛：深色面板底（存档、对话框、战斗指令区、HUD），偏蓝的墨青。</summary>
    public static readonly Color PanelDark = Color.FromHtml("#1C3042");
    /// <summary>玄黛：深色渐变底端、遮罩与投影。</summary>
    public static readonly Color Abyss = Color.FromHtml("#0F1B27");
    /// <summary>深面正文（月白），对黛约 11.5:1。</summary>
    public static readonly Color TextOnDark = Color.FromHtml("#F3ECDB");
    /// <summary>深面次级文字（旧绢），对黛约 6.9:1。</summary>
    public static readonly Color TextOnDarkMuted = Color.FromHtml("#C4B899");
    /// <summary>石青：主要操作、选中与链接；绢字在其上与对绢均约 4.8:1。</summary>
    public static readonly Color Accent = Color.FromHtml("#1D6A8A");
    /// <summary>石绿：装饰笔线、我方描边，不作正文颜色。</summary>
    public static readonly Color Trim = Color.FromHtml("#5FA792");
    /// <summary>
    /// 泥金：角饰、卷云、菱形与焦点框、深色面上的短标签；对黛约 7.3:1。
    /// 不作大面积底色，不作绢面正文。
    /// </summary>
    public static readonly Color Gilt = Color.FromHtml("#D8BC78");
    /// <summary>
    /// 朱砂：印章（页名章、姓名章、轮次章）与选中项的朱点，是界面上最醒目的一点颜色；
    /// 属纹饰色，不表达数值状态。绢字在其上约 4.8:1。
    /// </summary>
    public static readonly Color Cinnabar = Color.FromHtml("#B3382A");
    /// <summary>赭石：绢面的笔线与小标题、木轴与山脚；对绢约 4.6:1，可作短标签。</summary>
    public static readonly Color Ochre = Color.FromHtml("#8E5A2E");
    /// <summary>竹青：数值提升、增益；须配合 ▲ 等符号，不单靠颜色。</summary>
    public static readonly Color Boost = Color.FromHtml("#2E6E45");
    /// <summary>杏红：气血、数值下降、敌方意图与警示，唯一表达状态的红色；对绢约 4.7:1。</summary>
    public static readonly Color Warm = Color.FromHtml("#A9432F");

    public const int FontBody = 24;
    public const int FontSecondary = 20;
    public const int FontTitle = 36;

    public const int SpaceS = 8;
    public const int SpaceM = 16;
    public const int SpaceL = 24;
    public const int SpaceXl = 32;
    public const int SpaceXxl = 48;
}
