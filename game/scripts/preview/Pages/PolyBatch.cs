using Godot;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 多边形合批（M3-07 性能整治）：Godot 2D 每次 <c>DrawPolygon</c> / <c>DrawColoredPolygon</c> 都是一次绘制调用，
/// 布景沿崖边、岸线逐段画的小四边形一张图上有数千次。这里把同一节点里连续、同纹理的多边形先三角化攒起来，
/// 换纹理、要画别的命令（勾线、贴图、变换）之前或画完时再一次提交成一个三角形数组，画面与绘制先后都不变。
/// 用法：<c>using var batch = new PolyBatch(this);</c> 中途要直接调别的 Draw 方法前先 <see cref="Flush"/>。
/// </summary>
internal sealed class PolyBatch(CanvasItem item) : IDisposable
{
    private readonly List<Vector2> _points = [];
    private readonly List<Color> _colors = [];
    private readonly List<Vector2> _uvs = [];
    private readonly List<int> _indices = [];
    private Texture2D? _texture;

    /// <summary>单色多边形（同 <c>DrawColoredPolygon</c>）。</summary>
    public void Add(Vector2[] points, Color color) => Add(points, [.. Enumerable.Repeat(color, points.Length)]);

    /// <summary>逐顶点着色、可带纹理的多边形（同 <c>DrawPolygon</c>）；纹理与上一块不同时先提交已攒的。</summary>
    public void Add(Vector2[] points, Color[] colors, Vector2[]? uvs = null, Texture2D? texture = null)
    {
        if (texture != _texture)
        {
            Flush();
            _texture = texture;
        }

        var triangles = points.Length == 4 && Convex(points) ? [0, 1, 2, 0, 2, 3] : Geometry2D.TriangulatePolygon(points);
        if (triangles.Length == 0)
        {
            return;
        }

        var start = _points.Count;
        _points.AddRange(points);
        _colors.AddRange(colors);
        if (texture is not null)
        {
            _uvs.AddRange(uvs!);
        }

        foreach (var t in triangles)
        {
            _indices.Add(start + t);
        }
    }

    /// <summary>把已攒的三角形提交成一次绘制。</summary>
    public void Flush()
    {
        if (_indices.Count > 0)
        {
            RenderingServer.CanvasItemAddTriangleArray(item.GetCanvasItem(), [.. _indices], [.. _points], [.. _colors],
                _texture is null ? [] : [.. _uvs], texture: _texture?.GetRid() ?? default);
        }

        _points.Clear();
        _colors.Clear();
        _uvs.Clear();
        _indices.Clear();
    }

    public void Dispose() => Flush();

    /// <summary>四边形是否凸（各角叉积同号）；凸四边形直接按扇形分两块，省去三角化。</summary>
    private static bool Convex(Vector2[] p)
    {
        var sign = 0f;
        for (var i = 0; i < 4; i++)
        {
            var cross = (p[(i + 1) % 4] - p[i]).Cross(p[(i + 2) % 4] - p[(i + 1) % 4]);
            if (Mathf.Abs(cross) < 1e-6f)
            {
                continue;
            }

            if (sign != 0 && Mathf.Sign(cross) != Mathf.Sign(sign))
            {
                return false;
            }

            sign = cross;
        }

        return true;
    }
}
