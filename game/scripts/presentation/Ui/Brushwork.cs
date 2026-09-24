using Godot;

namespace WuxiaWorld.Game.Presentation.Ui;

/// <summary>
/// 笔触绘制（docs/art/UI_DESIGN.md 第 3 节）：带起笔、行笔、收笔粗细变化与飞白的笔画，
/// 卷云角、毛边色块与纸纹。全部由三角形带生成，不经多边形三角化，任意曲线都不会自交报错；
/// 随机量只取决于种子与坐标，同一控件每帧画出的形状相同，不闪动。
/// </summary>
public static class Brushwork
{
    private static ImageTexture? _grain;

    /// <summary>纸纹：绢丝横纹加细碎颗粒，白色 + 透明度，由调用方着色。平铺使用。</summary>
    public static ImageTexture Grain => _grain ??= BuildGrain();

    public static float Hash(float n) => Mathf.PosMod(Mathf.Sin(n) * 43758.5453f, 1f);

    /// <summary>一维平滑噪声，取值 0–1。</summary>
    public static float Noise(float x)
    {
        var i = Mathf.Floor(x);
        var f = x - i;
        var u = f * f * (3 - 2 * f);
        return Mathf.Lerp(Hash(i), Hash(i + 1), u);
    }

    /// <summary>
    /// 一笔：沿折线行笔，起笔略顿、收笔出锋；<paramref name="dry"/> 大于 0 时在两侧加几道
    /// 长短不一的细丝，笔尾断开，做出飞白。
    /// </summary>
    public static void Stroke(Rid item, ReadOnlySpan<Vector2> path, float width, Color color, float seed,
        float dry = 0, float head = 0.08f, float tail = 0.35f)
    {
        var pts = Densify(path, 6);
        if (pts.Length < 2 || color.A <= 0 || width <= 0)
        {
            return;
        }

        var lengths = Cumulative(pts);
        var total = lengths[^1];
        Ribbon(item, pts, lengths, color, t => width * Pressure(t, head, tail) *
            (0.7f + 0.6f * Noise(t * total / 46f + seed * 7.1f)), 0, 0, 1);

        if (dry <= 0)
        {
            return;
        }

        // 飞白：几道细丝贴着主笔两侧，各自在不同位置断开，尾部更碎。
        const int bristles = 4;
        for (var k = 0; k < bristles; k++)
        {
            var side = (k % 2 == 0 ? 1 : -1) * (0.32f + 0.12f * (k / 2));
            var start = Hash(seed * 3.3f + k) * 0.18f;
            var end = 1 - Hash(seed * 5.9f + k * 1.7f) * 0.45f * dry;
            var thin = width * (0.2f + 0.12f * Hash(seed + k * 9.1f));
            Ribbon(item, pts, lengths, color with { A = color.A * (0.55f + 0.35f * Hash(k + seed)) },
                t => thin * Pressure((t - start) / (end - start), 0.1f, 0.4f), side * width, start, end);
        }
    }

    /// <summary>卷云角：沿一边行笔入角，在角内打一个向内收的卷。o 为角点，x、y 为指向面板内部的两个单位向量。</summary>
    public static void CloudCorner(Rid item, Vector2 o, Vector2 x, Vector2 y, float size, float width, Color color, float seed)
    {
        var r = size * 0.3f;
        var c = new Vector2(r * 1.55f, r * 1.55f);
        var top = new List<Vector2> { new(size, r * 0.35f), new(size * 0.62f, r * 0.42f), new(c.X, c.Y - r) };
        // 以 c 为心、半径由 r 收到 0.3r 的卷，从正上方起逆时针（向左、向下）转一圈。
        for (var i = 1; i <= 18; i++)
        {
            var a = i / 18f;
            var angle = -Mathf.Pi / 2 - a * Mathf.Pi * 1.7f;
            var radius = r * (1 - 0.7f * a);
            top.Add(c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }

        Stroke(item, Map(top.ToArray()), width, color, seed, 0, 0.05f, 0.25f);

        // 另一边的短笔，从远处入角，止于卷旁。
        Span<Vector2> side = [new(r * 0.35f, size * 0.95f), new(r * 0.4f, size * 0.6f), new(r * 0.5f, c.Y + r * 0.5f)];
        Stroke(item, Map(side), width * 0.85f, color, seed + 1.3f, 0, 0.05f, 0.5f);
        Dot(item, o + x * (size + width * 3) + y * (r * 0.38f), width * 0.9f, color);
        Dot(item, o + y * (size * 0.95f + width * 3) + x * (r * 0.35f), width * 0.8f, color);
        return;

        Vector2[] Map(ReadOnlySpan<Vector2> local)
        {
            var result = new Vector2[local.Length];
            for (var i = 0; i < local.Length; i++)
            {
                result[i] = o + x * local[i].X + y * local[i].Y;
            }

            return result;
        }
    }

    /// <summary>墨点：略扁的菱形圆点，用作纹饰收尾。</summary>
    public static void Dot(Rid item, Vector2 c, float r, Color color)
    {
        const int n = 10;
        var points = new Vector2[n];
        for (var i = 0; i < n; i++)
        {
            var a = Mathf.Tau * i / n;
            var k = i % 2 == 0 ? 1f : 0.82f;
            points[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.9f) * r * k;
        }

        RenderingServer.CanvasItemAddPolygon(item, points, [color]);
    }

    /// <summary>
    /// 毛边矩形轮廓：沿四边每隔约 10 像素取一点，按噪声内外错开 <paramref name="amp"/> 像素，
    /// 做出绢边、印泥与刷色的不齐边。<paramref name="chamfer"/> 大于 0 时先切角。
    /// </summary>
    public static Vector2[] RaggedRect(Rect2 r, float amp, float seed, float chamfer = 0, float step = 10)
    {
        var corners = Octagon(r, chamfer);
        var result = new List<Vector2>();
        var center = r.GetCenter();
        var walked = 0f;
        for (var i = 0; i < corners.Length; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % corners.Length];
            var len = a.DistanceTo(b);
            var n = Mathf.Max(1, Mathf.CeilToInt(len / step));
            for (var j = 0; j < n; j++)
            {
                var p = a.Lerp(b, (float)j / n);
                var along = walked + len * j / n;
                var push = (Noise(along / 23f + seed * 13.7f) - 0.5f) * 1.4f + (Hash(along * 0.37f + seed) - 0.5f) * 0.6f;
                result.Add(p + (p - center).Normalized() * push * amp);
            }

            walked += len;
        }

        return result.ToArray();
    }

    public static Vector2[] Octagon(Rect2 r, float c)
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

    /// <summary>在多边形内平铺纸纹。</summary>
    public static void GrainFill(Rid item, Vector2[] polygon, Color tint, float scale = 1)
    {
        if (tint.A <= 0)
        {
            return;
        }

        var uvs = new Vector2[polygon.Length];
        for (var i = 0; i < polygon.Length; i++)
        {
            uvs[i] = polygon[i] / (256f * scale);
        }

        RenderingServer.CanvasItemSetDefaultTextureRepeat(item, RenderingServer.CanvasItemTextureRepeat.Enabled);
        RenderingServer.CanvasItemAddPolygon(item, polygon, new[] { tint }, uvs, Grain.GetRid());
    }

    /// <summary>
    /// 刷痕：自左向右一笔刷过的色带。上下缘不齐，左端饱满，右端碎开渐隐；用于菜单与选项的选中底。
    /// </summary>
    public static void Swipe(Rid item, Rect2 r, Color color, float seed, float reach = 1)
    {
        if (color.A <= 0)
        {
            return;
        }

        const int n = 48;
        var top = new Vector2[n + 1];
        var bottom = new Vector2[n + 1];
        var colors = new Color[(n + 1) * 2];
        var width = r.Size.X * reach;
        for (var i = 0; i <= n; i++)
        {
            var t = (float)i / n;
            var x = r.Position.X + width * t;
            // 笔肚在左三分之一处最宽，向右收窄；上下缘各自抖动。
            var body = Mathf.Lerp(1f, 0.55f, Mathf.SmoothStep(0.25f, 1f, t));
            var half = r.Size.Y * 0.5f * body;
            var cy = r.GetCenter().Y + (Noise(t * 3 + seed) - 0.5f) * r.Size.Y * 0.08f;
            top[i] = new Vector2(x, cy - half * (0.92f + 0.16f * Noise(t * 9 + seed * 3.1f)));
            bottom[i] = new Vector2(x, cy + half * (0.92f + 0.16f * Noise(t * 9 + seed * 5.3f)));
            var fade = 1 - Mathf.SmoothStep(0.35f, 1f, t);
            var headIn = Mathf.SmoothStep(0f, 0.03f, t);
            colors[i * 2] = color with { A = color.A * fade * headIn };
            colors[i * 2 + 1] = color with { A = color.A * fade * headIn };
        }

        var vertices = new Vector2[(n + 1) * 2];
        for (var i = 0; i <= n; i++)
        {
            vertices[i * 2] = top[i];
            vertices[i * 2 + 1] = bottom[i];
        }

        RenderingServer.CanvasItemAddTriangleArray(item, Strip(n + 1), vertices, colors);

        // 刷尾的几道断丝。
        for (var k = 0; k < 3; k++)
        {
            var y = r.Position.Y + r.Size.Y * (0.3f + 0.2f * k) + (Hash(seed + k) - 0.5f) * 6;
            var from = r.Position.X + width * (0.4f + 0.1f * Hash(seed * 2 + k));
            var to = r.Position.X + width * (0.62f + 0.14f * Hash(seed * 4 + k));
            Stroke(item, [new Vector2(from, y), new Vector2(to, y + (Hash(k + seed * 7) - 0.5f) * 4)],
                1.8f, color with { A = color.A * 0.4f }, seed + k, 0, 0.1f, 0.7f);
        }
    }

    /// <summary>晕染：中心浓、边缘散开的一团颜色，边缘不齐。</summary>
    public static void Blot(Rid item, Vector2 center, float radius, Color color, float seed)
    {
        const int n = 40;
        var vertices = new Vector2[n + 1];
        var colors = new Color[n + 1];
        var indices = new int[n * 3];
        vertices[0] = center;
        colors[0] = color;
        for (var i = 0; i < n; i++)
        {
            var a = Mathf.Tau * i / n;
            var k = 0.72f + 0.4f * Noise(i * 0.45f + seed * 3.7f) + 0.08f * Hash(i + seed);
            vertices[i + 1] = center + new Vector2(Mathf.Cos(a) * 1.35f, Mathf.Sin(a) * 0.8f) * radius * k;
            colors[i + 1] = color with { A = 0 };
            indices[i * 3] = 0;
            indices[i * 3 + 1] = i + 1;
            indices[i * 3 + 2] = (i + 1) % n + 1;
        }

        RenderingServer.CanvasItemAddTriangleArray(item, indices, vertices, colors);
    }

    // ── 内部 ─────────────────────────────────────────────

    /// <summary>笔压：起笔处由 0.4 升到 1，收笔处落到 0.12。</summary>
    private static float Pressure(float t, float head, float tail)
    {
        t = Mathf.Clamp(t, 0, 1);
        var rise = head > 0 ? Mathf.Lerp(0.4f, 1f, Mathf.SmoothStep(0, head, t)) : 1;
        var fall = tail > 0 ? Mathf.Lerp(1f, 0.12f, Mathf.SmoothStep(1 - tail, 1, t)) : 1;
        return rise * fall;
    }

    private static void Ribbon(Rid item, Vector2[] pts, float[] lengths, Color color, Func<float, float> width,
        float offset, float from, float to)
    {
        var total = lengths[^1];
        var left = new List<Vector2>();
        var right = new List<Vector2>();
        for (var i = 0; i < pts.Length; i++)
        {
            var t = lengths[i] / total;
            if (t < from || t > to)
            {
                continue;
            }

            var prev = pts[Math.Max(0, i - 1)];
            var next = pts[Math.Min(pts.Length - 1, i + 1)];
            var tangent = (next - prev).Normalized();
            var normal = new Vector2(-tangent.Y, tangent.X);
            var w = Mathf.Max(0.35f, width(t)) / 2;
            var p = pts[i] + normal * offset;
            left.Add(p + normal * w);
            right.Add(p - normal * w);
        }

        if (left.Count < 2)
        {
            return;
        }

        var vertices = new Vector2[left.Count * 2];
        for (var i = 0; i < left.Count; i++)
        {
            vertices[i * 2] = left[i];
            vertices[i * 2 + 1] = right[i];
        }

        RenderingServer.CanvasItemAddTriangleArray(item, Strip(left.Count), vertices, [color]);
    }

    private static int[] Strip(int pairs)
    {
        var indices = new int[(pairs - 1) * 6];
        for (var i = 0; i < pairs - 1; i++)
        {
            var a = i * 2;
            indices[i * 6] = a;
            indices[i * 6 + 1] = a + 1;
            indices[i * 6 + 2] = a + 2;
            indices[i * 6 + 3] = a + 1;
            indices[i * 6 + 4] = a + 3;
            indices[i * 6 + 5] = a + 2;
        }

        return indices;
    }

    private static Vector2[] Densify(ReadOnlySpan<Vector2> path, float step)
    {
        var result = new List<Vector2>();
        for (var i = 0; i < path.Length - 1; i++)
        {
            var n = Mathf.Max(1, Mathf.CeilToInt(path[i].DistanceTo(path[i + 1]) / step));
            for (var j = 0; j < n; j++)
            {
                result.Add(path[i].Lerp(path[i + 1], (float)j / n));
            }
        }

        if (path.Length > 0)
        {
            result.Add(path[^1]);
        }

        return result.ToArray();
    }

    private static float[] Cumulative(Vector2[] pts)
    {
        var lengths = new float[pts.Length];
        for (var i = 1; i < pts.Length; i++)
        {
            lengths[i] = lengths[i - 1] + pts[i].DistanceTo(pts[i - 1]);
        }

        if (lengths[^1] <= 0)
        {
            lengths[^1] = 1;
        }

        return lengths;
    }

    private static ImageTexture BuildGrain()
    {
        const int size = 256;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var rng = new RandomNumberGenerator { Seed = 20260924 };
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                // 绢丝：横向长纤维（可平铺的正弦组合）加纵向弱纹，再撒细颗粒。
                var u = x / (float)size * Mathf.Tau;
                var v = y / (float)size * Mathf.Tau;
                var fiber = 0.5f + 0.5f * Mathf.Sin(v * 61 + Mathf.Sin(u * 3) * 2.2f + Mathf.Sin(u * 7 + v * 2) * 0.8f);
                var weft = 0.5f + 0.5f * Mathf.Sin(u * 97 + Mathf.Sin(v * 5) * 1.5f);
                var speck = rng.Randf();
                var a = fiber * 0.45f + weft * 0.2f + (speck > 0.985f ? 0.9f : speck * 0.35f);
                image.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp(a - 0.25f, 0, 1)));
            }
        }

        return ImageTexture.CreateFromImage(image);
    }
}
