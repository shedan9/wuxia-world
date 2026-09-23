using Godot;

namespace WuxiaWorld.Game.Presentation;

/// <summary>UI 语义色与尺寸，取自架构文档 10.4；调整时同步文档。</summary>
public static class UiPalette
{
    public static readonly Color Paper = Color.FromHtml("#F1E7D1");
    public static readonly Color Ink = Color.FromHtml("#252923");
    public static readonly Color PanelDark = Color.FromHtml("#202925");
    public static readonly Color TextOnDark = Color.FromHtml("#F5EBD7");
    public static readonly Color Cinnabar = Color.FromHtml("#943C32");
    public static readonly Color OldGold = Color.FromHtml("#B69A62");

    // 以下为 M0 补充色，对比度按 4.5:1 核算（见架构文档 10.4）。
    /// <summary>宣纸选中/悬停底色。</summary>
    public static readonly Color PaperShade = Color.FromHtml("#E4D6B8");
    /// <summary>宣纸上的次级文字，对宣纸约 5.3:1。</summary>
    public static readonly Color InkMuted = Color.FromHtml("#5E6159");
    /// <summary>深色面板上的次级文字，对深色面板约 6.8:1。</summary>
    public static readonly Color TextOnDarkMuted = Color.FromHtml("#B8AE98");
    /// <summary>青山色：数值提升、增益状态；须配合 ▲ 等符号，不单靠颜色。</summary>
    public static readonly Color Mountain = Color.FromHtml("#3F5A4E");

    public const int FontBody = 24;
    public const int FontSecondary = 20;
    public const int FontTitle = 36;

    public const int SpaceS = 8;
    public const int SpaceM = 16;
    public const int SpaceL = 24;
    public const int SpaceXl = 32;
    public const int SpaceXxl = 48;
}
