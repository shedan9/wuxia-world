using Godot;

namespace WuxiaWorld.Game.Presentation.Ui;

/// <summary>角饰样式：见 docs/art/UI_DESIGN.md 第 3 节“纹饰”。</summary>
public enum CornerStyle
{
    None,
    /// <summary>直角折线，用于焦点与选中框。</summary>
    Bracket,
    /// <summary>回纹钩角，用于面板与卡片。</summary>
    Hook,
}

/// <summary>
/// 游戏界面通用样式框：切角底、两色渐变、外框与内衬线、回纹角饰、柔和投影与左侧标记条。
/// 全部用 RenderingServer 绘制，不依赖贴图，可在任意分辨率下保持清晰；
/// 由 UiTheme 组装成各控件状态，页面代码不直接创建。
/// </summary>
public partial class OrnateBox : StyleBox
{
    public Color FillA { get; set; } = Colors.Transparent;
    /// <summary>渐变终点色；为空时为纯色。</summary>
    public Color? FillB { get; set; }
    /// <summary>true 为左→右渐变，false 为上→下。</summary>
    public bool Horizontal { get; set; }
    /// <summary>切角尺寸（逻辑像素）；0 为直角。</summary>
    public float Chamfer { get; set; }

    public Color Border { get; set; } = Colors.Transparent;
    public float BorderWidth { get; set; }
    /// <summary>内衬线：距外框 InnerInset 的第二道细线，做出双线框。</summary>
    public Color Inner { get; set; } = Colors.Transparent;
    public float InnerInset { get; set; } = 6;

    public CornerStyle Corners { get; set; }
    public Color CornerColor { get; set; } = UiPalette.Gilt;
    public float CornerSize { get; set; } = 18;
    public float CornerWidth { get; set; } = 2;
    /// <summary>角饰外扩；焦点框用正值画在控件外侧。</summary>
    public float CornerOutset { get; set; }

    /// <summary>左侧标记条（列表选中）。</summary>
    public Color Marker { get; set; } = Colors.Transparent;
    public float MarkerWidth { get; set; }
    /// <summary>左侧小菱形（选中项）。</summary>
    public bool Diamond { get; set; }

    public Color Shadow { get; set; } = Colors.Transparent;
    public float ShadowSize { get; set; }
    public Vector2 ShadowOffset { get; set; }

    /// <summary>高光线（按钮的“玉面”反光、页签下划线）。</summary>
    public Color Sheen { get; set; } = Colors.Transparent;
    public float SheenWidth { get; set; } = 1;
    /// <summary>true 画在顶边，false 画在底边。</summary>
    public bool SheenAtTop { get; set; }
    /// <summary>高光线两端内缩比例，0 为通长；页签下划线用 0.2 左右。</summary>
    public float SheenInset { get; set; }

    /// <summary>绘制区外扩，不影响布局。</summary>
    public float Expand { get; set; }

    public OrnateBox Margins(float x, float y)
    {
        ContentMarginLeft = ContentMarginRight = x;
        ContentMarginTop = ContentMarginBottom = y;
        return this;
    }

    public override void _Draw(Rid toCanvasItem, Rect2 rect)
    {
        var item = toCanvasItem;
        rect = rect.Grow(Expand);
        if (rect.Size.X <= 1 || rect.Size.Y <= 1)
        {
            return;
        }

        if (Shadow.A > 0 && ShadowSize > 0)
        {
            // 由外到内叠几层半透明切角框，近似柔和投影。
            const int layers = 5;
            for (var i = layers; i >= 1; i--)
            {
                var grow = ShadowSize * i / layers;
                var shape = Shape(new Rect2(rect.Position + ShadowOffset, rect.Size).Grow(grow), Chamfer + grow * 0.6f);
                RenderingServer.CanvasItemAddPolygon(item, shape, [Shadow with { A = Shadow.A / layers }]);
            }
        }

        var outline = Shape(rect, Chamfer);
        if (FillA.A > 0 || FillB is { A: > 0 })
        {
            var colors = new Color[outline.Length];
            for (var i = 0; i < outline.Length; i++)
            {
                var t = Horizontal
                    ? (outline[i].X - rect.Position.X) / rect.Size.X
                    : (outline[i].Y - rect.Position.Y) / rect.Size.Y;
                colors[i] = FillB is { } b ? FillA.Lerp(b, Mathf.Clamp(t, 0, 1)) : FillA;
            }

            RenderingServer.CanvasItemAddPolygon(item, outline, colors);
        }

        if (Sheen.A > 0)
        {
            var y = SheenAtTop ? rect.Position.Y + SheenWidth / 2 + 1 : rect.End.Y - SheenWidth / 2 - 1;
            var inset = Mathf.Max(Chamfer, rect.Size.X * SheenInset);
            RenderingServer.CanvasItemAddLine(item, new Vector2(rect.Position.X + inset, y),
                new Vector2(rect.End.X - inset, y), Sheen, SheenWidth);
        }

        if (MarkerWidth > 0 && Marker.A > 0)
        {
            RenderingServer.CanvasItemAddRect(item,
                new Rect2(rect.Position.X, rect.Position.Y + 6, MarkerWidth, rect.Size.Y - 12), Marker);
        }

        if (Diamond)
        {
            var c = new Vector2(rect.Position.X + MarkerWidth + 12, rect.GetCenter().Y);
            DrawDiamond(item, c, 5, CornerColor);
        }

        if (BorderWidth > 0 && Border.A > 0)
        {
            Loop(item, outline, Border, BorderWidth);
        }

        if (Inner.A > 0)
        {
            var inner = rect.Grow(-InnerInset);
            Loop(item, Shape(inner, Mathf.Max(0, Chamfer - InnerInset * 0.4f)), Inner, 1);
        }

        if (Corners != CornerStyle.None)
        {
            DrawCorners(item, rect.Grow(CornerOutset));
        }
    }

    private void DrawCorners(Rid item, Rect2 r)
    {
        var s = Mathf.Min(CornerSize, Mathf.Min(r.Size.X, r.Size.Y) * 0.45f);
        Span<(Vector2 Origin, Vector2 X, Vector2 Y)> corners =
        [
            (r.Position, Vector2.Right, Vector2.Down),
            (new Vector2(r.End.X, r.Position.Y), Vector2.Left, Vector2.Down),
            (new Vector2(r.Position.X, r.End.Y), Vector2.Right, Vector2.Up),
            (r.End, Vector2.Left, Vector2.Up),
        ];

        foreach (var (o, x, y) in corners)
        {
            RenderingServer.CanvasItemAddPolyline(item, [o + x * s, o, o + y * s], [CornerColor], CornerWidth, true);
            if (Corners == CornerStyle.Hook)
            {
                // 回纹：角内一道内折的小钩，再点一粒菱形。
                var k = s * 0.42f;
                var inset = CornerWidth + 3;
                RenderingServer.CanvasItemAddPolyline(item,
                    [o + x * inset + y * (k + inset), o + x * inset + y * inset, o + x * (k + inset) + y * inset],
                    [CornerColor with { A = CornerColor.A * 0.8f }], Mathf.Max(1, CornerWidth - 0.5f), true);
                DrawDiamond(item, o + x * (s + 7), 2.5f, CornerColor);
                DrawDiamond(item, o + y * (s + 7), 2.5f, CornerColor);
            }
        }
    }

    private static void DrawDiamond(Rid item, Vector2 c, float r, Color color) =>
        RenderingServer.CanvasItemAddPolygon(item,
            [c + new Vector2(0, -r), c + new Vector2(r, 0), c + new Vector2(0, r), c + new Vector2(-r, 0)], [color]);

    private static void Loop(Rid item, Vector2[] points, Color color, float width)
    {
        var closed = new Vector2[points.Length + 1];
        points.CopyTo(closed, 0);
        closed[^1] = points[0];
        RenderingServer.CanvasItemAddPolyline(item, closed, new[] { color }, width, true);
    }

    /// <summary>切角矩形轮廓（顺时针）。</summary>
    private static Vector2[] Shape(Rect2 r, float c)
    {
        c = Mathf.Min(c, Mathf.Min(r.Size.X, r.Size.Y) * 0.5f);
        if (c <= 0.5f)
        {
            return [r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y)];
        }

        var (x0, y0, x1, y1) = (r.Position.X, r.Position.Y, r.End.X, r.End.Y);
        return
        [
            new(x0 + c, y0), new(x1 - c, y0), new(x1, y0 + c), new(x1, y1 - c),
            new(x1 - c, y1), new(x0 + c, y1), new(x0, y1 - c), new(x0, y0 + c),
        ];
    }
}
