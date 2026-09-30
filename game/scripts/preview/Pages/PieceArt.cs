using Godot;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>AI 地面纹理的调色系数：按纹理平均色（先收饱和度）对齐原程序化地面的中间色。</summary>
internal static class GroundTint
{
    public static readonly Vector3 Grass = new(1.67f, 1.57f, 2.07f);
    public static readonly Vector3 Meadow = new(1.55f, 1.56f, 2.04f);
    public static readonly Vector3 Dirt = new(1.02f, 1.12f, 1.12f);
}

/// <summary>给程序化搭出的面贴 AI 纹理（石阶等平整几何件：逐件出件画不好，改为面贴纹理）。</summary>
internal static class FaceTexture
{
    /// <summary>
    /// 给一组四边形面贴纹理：局部 x 沿第一条边、y 沿最后一条边；纹理偏移取面原点的世界坐标（水平面按 A / D，立面按沿边长度与高度），
    /// 相邻面接缝处纹理连续。tone 与纹理相乘。
    /// </summary>
    public static List<Face> Apply(List<Face> faces, Texture2D texture, float world, Color tone, float outline, float sideDarken = 0.3f)
    {
        var px = texture.GetSize() / world;
        var result = new List<Face>();
        foreach (var f in faces)
        {
            // 纹理按面内矩形取样，只贴四边形面；多边形截面（圆木端头等）保留底色。
            if (f.Points.Length != 4)
            {
                result.Add(f);
                continue;
            }

            var (p0, p1, p3) = (f.Points[0], f.Points[1], f.Points[^1]);
            var (u, v) = (p1 - p0, p3 - p0);
            var size = new Vector2(u.Length(), v.Length());
            var ad = new Vector2(p0.X + p0.Y, p0.Y - p0.X) / Mathf.Sqrt2;
            var offset = f.Normal.IsEqualApprox(TownView.Above)
                ? ad
                : new Vector2(Mathf.Abs(f.Normal.X + f.Normal.Y) > Mathf.Abs(f.Normal.Y - f.Normal.X) ? ad.Y : ad.X, -p0.Z);
            // 踏面与朝镜头的踢面在当前光向下同为受光面，踢面单独压暗一阶，台阶才分得出层次；斜顶石栏按水平面取色。
            var faceTone = f.Normal.Z > 0.5f ? tone : tone.Darkened(sideDarken);
            result.Add(new Face
            {
                Points = f.Points, Normal = f.Normal, Color = f.Color, Outline = outline, Lit = f.Lit,
                Local = TownView.Local(p0, u / size.X, v / size.Y),
                Decal = ci => ci.DrawTextureRectRegion(texture, new Rect2(Vector2.Zero, size), new Rect2(offset * px, size * px), faceTone),
            });
        }

        return result;
    }
}
