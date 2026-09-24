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
    /// <summary>卷云角：金泥笔触入角打卷，用于主要面板。</summary>
    Cloud,
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

    /// <summary>true 时外框与内衬线用笔触画（四边各一笔，出角略有余锋），否则为规整细线。</summary>
    public bool Brush { get; set; }
    /// <summary>底色轮廓的毛边幅度（像素）；0 为齐边。</summary>
    public float Ragged { get; set; }
    /// <summary>纸纹着色；透明为不加纸纹。</summary>
    public Color Grain { get; set; } = Colors.Transparent;
    public float GrainScale { get; set; } = 1;
    /// <summary>刷痕色：不为透明时在底色之上自左刷出一道色带（菜单项、选项的选中态）。</summary>
    public Color Swipe { get; set; } = Colors.Transparent;
    public float SwipeReach { get; set; } = 1;
    /// <summary>笔框出角的余锋比例；上下紧挨的卡片取小值，免得笔锋伸进邻卡。</summary>
    public float Overshoot { get; set; } = 1;
    /// <summary>晕染：左上与右下各一团淡墨（或淡彩），打破整齐的矩形；透明为不加。</summary>
    public Color Wash { get; set; } = Colors.Transparent;
    /// <summary>笔触与毛边的随机种子；同一类控件取同一值，形状只随尺寸变化。</summary>
    public float Seed { get; set; } = 1;

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

        // 种子只随尺寸变，入场位移时笔触不跳。
        var seed = Seed * 7.3f + Mathf.Round(rect.Size.X) * 0.013f + Mathf.Round(rect.Size.Y) * 0.029f;

        if (Shadow.A > 0 && ShadowSize > 0)
        {
            // 由外到内叠几层半透明框，近似柔和投影；层数多、每层淡，边缘不出台阶。
            const int layers = 8;
            for (var i = layers; i >= 1; i--)
            {
                var grow = ShadowSize * i / layers;
                var shape = Brushwork.Octagon(new Rect2(rect.Position + ShadowOffset, rect.Size).Grow(grow - ShadowSize * 0.3f),
                    Chamfer + grow * 0.6f);
                RenderingServer.CanvasItemAddPolygon(item, shape, [Shadow with { A = Shadow.A / layers }]);
            }
        }

        var outline = Ragged > 0 ? Brushwork.RaggedRect(rect, Ragged, seed, Chamfer) : Brushwork.Octagon(rect, Chamfer);
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

        if (Wash.A > 0)
        {
            var radius = Mathf.Min(rect.Size.X, rect.Size.Y) * 0.55f;
            Brushwork.Blot(item, rect.Position + new Vector2(radius * 1.1f, radius * 0.9f), radius, Wash, seed);
            Brushwork.Blot(item, rect.End - new Vector2(radius * 1.2f, radius * 0.95f), radius * 0.8f, Wash with { A = Wash.A * 0.7f }, seed + 5);
        }

        Brushwork.GrainFill(item, outline, Grain, GrainScale);

        if (Swipe.A > 0)
        {
            Brushwork.Swipe(item, rect, Swipe, seed, SwipeReach);
        }

        if (Sheen.A > 0)
        {
            var y = SheenAtTop ? rect.Position.Y + SheenWidth / 2 + 1 : rect.End.Y - SheenWidth / 2 - 1;
            var inset = Mathf.Max(Chamfer, rect.Size.X * SheenInset);
            var from = new Vector2(rect.Position.X + inset, y);
            var to = new Vector2(rect.End.X - inset, y);
            if (Brush)
            {
                Brushwork.Stroke(item, [from, to], SheenWidth * 1.3f, Sheen, seed + 4, 0.4f, 0.1f, 0.4f);
            }
            else
            {
                RenderingServer.CanvasItemAddLine(item, from, to, Sheen, SheenWidth);
            }
        }

        if (MarkerWidth > 0 && Marker.A > 0)
        {
            var top = new Vector2(rect.Position.X + MarkerWidth / 2, rect.Position.Y + 4);
            var bottom = new Vector2(rect.Position.X + MarkerWidth / 2, rect.End.Y - 4);
            Brushwork.Stroke(item, [top, bottom], MarkerWidth, Marker, seed + 2, 0, 0.1f, 0.3f);
        }

        if (Diamond)
        {
            var c = new Vector2(rect.Position.X + MarkerWidth + 14, rect.GetCenter().Y);
            DrawDiamond(item, c, 6, CornerColor);
            DrawDiamond(item, c, 2.2f, CornerColor.Darkened(0.5f));
        }

        if (BorderWidth > 0 && Border.A > 0)
        {
            if (Brush)
            {
                BrushFrame(item, rect, Border, BorderWidth, seed, Overshoot);
            }
            else
            {
                Loop(item, outline, Border, BorderWidth);
            }
        }

        if (Inner.A > 0)
        {
            var inner = rect.Grow(-InnerInset);
            if (Brush)
            {
                BrushFrame(item, inner, Inner, 1.1f, seed + 11, Overshoot * 0.5f);
            }
            else
            {
                Loop(item, Brushwork.Octagon(inner, Mathf.Max(0, Chamfer - InnerInset * 0.4f)), Inner, 1);
            }
        }

        if (Corners != CornerStyle.None)
        {
            DrawCorners(item, rect.Grow(CornerOutset), seed);
        }
    }

    /// <summary>四边各一笔：起笔在角外少许，收笔越过下一个角，略带弧度，像手绘的界格。</summary>
    private static void BrushFrame(Rid item, Rect2 r, Color color, float width, float seed, float overshoot)
    {
        var over = Mathf.Min(14, Mathf.Min(r.Size.X, r.Size.Y) * 0.1f) * overshoot;
        var (x0, y0, x1, y1) = (r.Position.X, r.Position.Y, r.End.X, r.End.Y);
        Side(new Vector2(x0 - over, y0), new Vector2(x1 + over * 0.4f, y0), 0);
        Side(new Vector2(x1, y0 - over * 0.4f), new Vector2(x1, y1 + over), 1);
        Side(new Vector2(x1 + over, y1), new Vector2(x0 - over * 0.4f, y1), 2);
        Side(new Vector2(x0, y1 + over * 0.4f), new Vector2(x0, y0 - over), 3);
        return;

        void Side(Vector2 a, Vector2 b, int k)
        {
            var s = seed + k * 3.7f;
            var dir = (b - a).Normalized();
            var normal = new Vector2(-dir.Y, dir.X);
            var bow = (Brushwork.Hash(s) - 0.5f) * Mathf.Min(3f, a.DistanceTo(b) * 0.006f);
            var mid = a.Lerp(b, 0.5f) + normal * bow;
            Brushwork.Stroke(item, [a, mid, b], width * (1.6f + 0.5f * Brushwork.Hash(s + 1)), color, s, 1f, 0.05f, 0.35f);
        }
    }

    private void DrawCorners(Rid item, Rect2 r, float seed)
    {
        var s = Mathf.Min(CornerSize, Mathf.Min(r.Size.X, r.Size.Y) * 0.45f);
        Span<(Vector2 Origin, Vector2 X, Vector2 Y)> corners =
        [
            (r.Position, Vector2.Right, Vector2.Down),
            (new Vector2(r.End.X, r.Position.Y), Vector2.Left, Vector2.Down),
            (new Vector2(r.Position.X, r.End.Y), Vector2.Right, Vector2.Up),
            (r.End, Vector2.Left, Vector2.Up),
        ];

        var index = 0;
        foreach (var (o, x, y) in corners)
        {
            index++;
            if (Corners == CornerStyle.Cloud)
            {
                Brushwork.CloudCorner(item, o, x, y, s, CornerWidth * 1.6f, CornerColor, seed + index);
                continue;
            }

            // 折角：两笔自角点向外出锋，比细线更有分量，缩放后也不发虚。
            Brushwork.Stroke(item, [o, o + x * s], CornerWidth * 1.5f, CornerColor, seed + index, 0, 0.02f, 0.55f);
            Brushwork.Stroke(item, [o, o + y * s], CornerWidth * 1.5f, CornerColor, seed + index + 0.5f, 0, 0.02f, 0.55f);
            if (Corners == CornerStyle.Hook)
            {
                // 回纹：角内一道内折的小钩，再点一粒菱形。
                var k = s * 0.42f;
                var inset = CornerWidth + 3;
                RenderingServer.CanvasItemAddPolyline(item,
                    [o + x * inset + y * (k + inset), o + x * inset + y * inset, o + x * (k + inset) + y * inset],
                    [CornerColor with { A = CornerColor.A * 0.8f }], Mathf.Max(1.5f, CornerWidth - 0.5f));
                DrawDiamond(item, o + x * (s + 7), 3f, CornerColor);
                DrawDiamond(item, o + y * (s + 7), 3f, CornerColor);
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
        RenderingServer.CanvasItemAddPolyline(item, closed, new[] { color }, Mathf.Max(1.5f, width));
    }
}
