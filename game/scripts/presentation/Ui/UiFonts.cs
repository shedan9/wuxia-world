using Godot;

namespace WuxiaWorld.Game.Presentation.Ui;

/// <summary>
/// 随包字体，均为 SIL OFL 1.1（许可文本同目录，来源见 docs/art/ASSET_LEDGER.md）。
/// 正文思源黑体，标题与印章霞鹜文楷。
/// </summary>
public static class UiFonts
{
    public static readonly Font Body = Load("res://assets/fonts/SourceHanSansCN-Regular.otf");

    public static readonly Font BodyMedium = Load("res://assets/fonts/SourceHanSansCN-Medium.otf");

    public static readonly Font Title = Load("res://assets/fonts/LXGWWenKai-Medium.ttf");

    private static FontFile Load(string path)
    {
        var font = GD.Load<FontFile>(path);
        font.Antialiasing = TextServer.FontAntialiasing.Gray;
        font.Hinting = TextServer.Hinting.Light;
        font.SubpixelPositioning = TextServer.SubpixelPositioning.Auto;
        return font;
    }
}
