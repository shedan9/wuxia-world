using Godot;

namespace WuxiaWorld.Game.Presentation.Ui;

/// <summary>
/// 纹饰分隔线。默认两端渐隐、正中一粒菱形；<see cref="Lead"/> 为 true 时菱形在左端，
/// 线向右渐隐，用于小节标题后缀。
/// </summary>
public partial class DiamondRule : Control
{
    public bool Dark { get; init; }
    public bool Lead { get; init; }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(CustomMinimumSize.X, 18);
        MouseFilter = MouseFilterEnum.Ignore;
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        var line = (Dark ? UiPalette.Trim : UiPalette.Trim.Darkened(0.1f)) with { A = 0.75f };
        var gem = Dark ? UiPalette.Gilt : UiPalette.Gilt.Darkened(0.15f);
        var y = Size.Y / 2;
        var clear = line with { A = 0 };
        if (Lead)
        {
            Diamond(new Vector2(5, y), 4.5f, gem);
            DrawPolylineColors([new Vector2(14, y), new Vector2(Size.X * 0.6f, y), new Vector2(Size.X, y)],
                [line, line with { A = 0.4f }, clear], 1, true);
            return;
        }

        var c = Size.X / 2;
        DrawPolylineColors([new Vector2(0, y), new Vector2(c - 12, y)], [clear, line], 1, true);
        DrawPolylineColors([new Vector2(c + 12, y), new Vector2(Size.X, y)], [line, clear], 1, true);
        Diamond(new Vector2(c, y), 5, gem);
        Diamond(new Vector2(c, y), 2, Dark ? UiPalette.Abyss : UiPalette.Surface);
    }

    private void Diamond(Vector2 c, float r, Color color) =>
        DrawColoredPolygon([c + new Vector2(0, -r), c + new Vector2(r, 0), c + new Vector2(0, r), c + new Vector2(-r, 0)], color);
}
