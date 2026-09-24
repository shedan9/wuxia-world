using Godot;

namespace WuxiaWorld.Game.Presentation.Ui;

/// <summary>
/// 纹饰分隔线（笔触）。默认两笔自中间向两端出锋、正中一粒菱形；<see cref="Lead"/> 为 true 时菱形在左端，
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
        // 一笔淡墨横线：左起重、向右出锋；正中（或左端）一粒泥金菱形压一点朱砂。
        var line = (Dark ? UiPalette.Gilt : UiPalette.Ochre) with { A = Dark ? 0.45f : 0.6f };
        var gem = Dark ? UiPalette.Gilt : UiPalette.Gilt.Darkened(0.2f);
        var y = Size.Y / 2;
        var item = GetCanvasItem();
        var seed = Mathf.Round(Size.X) * 0.017f;
        if (Lead)
        {
            Diamond(new Vector2(6, y), 5.5f, gem);
            Diamond(new Vector2(6, y), 2, UiPalette.Cinnabar);
            Brushwork.Stroke(item, [new Vector2(16, y), new Vector2(Size.X * 0.5f, y + 0.6f), new Vector2(Size.X, y)],
                2.4f, line, seed, 0.8f, 0.03f, 0.7f);
            return;
        }

        var c = Size.X / 2;
        Brushwork.Stroke(item, [new Vector2(c - 16, y), new Vector2(c * 0.4f, y + 0.6f), new Vector2(0, y)], 2.4f, line, seed, 0.8f, 0.03f, 0.75f);
        Brushwork.Stroke(item, [new Vector2(c + 16, y), new Vector2(c * 1.6f, y - 0.6f), new Vector2(Size.X, y)], 2.4f, line, seed + 3, 0.8f, 0.03f, 0.75f);
        Diamond(new Vector2(c, y), 6.5f, gem);
        Diamond(new Vector2(c, y), 2.4f, UiPalette.Cinnabar);
    }

    private void Diamond(Vector2 c, float r, Color color) =>
        DrawColoredPolygon([c + new Vector2(0, -r), c + new Vector2(r, 0), c + new Vector2(0, r), c + new Vector2(-r, 0)], color);
}
