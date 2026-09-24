using Godot;

namespace WuxiaWorld.Game.Presentation;

/// <summary>
/// UI 语义色与尺寸，取自架构文档 10.4 与 docs/art/UI_DESIGN.md；调整时同步文档。
/// 2026-09-24 改为青绿山水配色（石青、石绿、水白、深潭），对比度按 4.5:1 核算。
/// 同日重做游戏化界面，补充金泥（纹饰与焦点）与玄潭（最深层底）两色。
/// </summary>
public static class UiPalette
{
    /// <summary>水白：浅色页面底。</summary>
    public static readonly Color Surface = Color.FromHtml("#F2F8F6");
    /// <summary>浅色页面的选中 / 悬停底色。</summary>
    public static readonly Color SurfaceShade = Color.FromHtml("#DCEEEA");
    /// <summary>正文，对水白约 11.4:1。</summary>
    public static readonly Color Text = Color.FromHtml("#173A3F");
    /// <summary>次级文字，对水白约 5.6:1、对选中底约 5.0:1。</summary>
    public static readonly Color TextMuted = Color.FromHtml("#4A686B");
    /// <summary>深潭：深色面板与外框底。</summary>
    public static readonly Color PanelDark = Color.FromHtml("#15424A");
    /// <summary>玄潭：深色面板渐变的底端、遮罩与阴影；深底正文对其约 13.9:1。</summary>
    public static readonly Color Abyss = Color.FromHtml("#0B2A30");
    /// <summary>深色面板正文，约 10.1:1。</summary>
    public static readonly Color TextOnDark = Color.FromHtml("#EDF8F5");
    /// <summary>深色面板次级文字，约 6.2:1。</summary>
    public static readonly Color TextOnDarkMuted = Color.FromHtml("#A3CBC6");
    /// <summary>石青：主要操作、选中标记、页名章；水白字在其上约 5.6:1。</summary>
    public static readonly Color Accent = Color.FromHtml("#1D6A8A");
    /// <summary>石绿：边框与装饰线，不作正文颜色。</summary>
    public static readonly Color Trim = Color.FromHtml("#6FB3A2");
    /// <summary>
    /// 金泥：只用于角饰、菱形标记、焦点框与稀有度，不作正文与大面积底色；
    /// 对深潭约 5.9:1、对玄潭约 8.1:1，可作深色面上的短标签。
    /// </summary>
    public static readonly Color Gilt = Color.FromHtml("#D2BC82");
    /// <summary>竹青：数值提升、增益与内力；须配合 ▲ 等符号，不单靠颜色。</summary>
    public static readonly Color Boost = Color.FromHtml("#26704F");
    /// <summary>杏红：气血、数值下降与警示，全局唯一的红色系；对水白约 5.0:1。</summary>
    public static readonly Color Warm = Color.FromHtml("#B04A36");

    public const int FontBody = 24;
    public const int FontSecondary = 20;
    public const int FontTitle = 36;

    public const int SpaceS = 8;
    public const int SpaceM = 16;
    public const int SpaceL = 24;
    public const int SpaceXl = 32;
    public const int SpaceXxl = 48;
}
