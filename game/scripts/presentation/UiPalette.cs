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

    public const int FontBody = 24;
    public const int FontSecondary = 20;
    public const int FontTitle = 36;

    public const int SpaceS = 8;
    public const int SpaceM = 16;
    public const int SpaceL = 24;
    public const int SpaceXl = 32;
    public const int SpaceXxl = 48;
}
