using System.Text.Json;
using Godot;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// AI 出件的贴图（架构文档 10.3 方案 C 的第三步“拼装”）：<c>res://assets/art/&lt;地区&gt;/&lt;id&gt;.png</c> 与同名 .json。
/// 图由 PieceGuideExport 导出的引导图约束生成，与占位件同一投影、同一画框；json 的 origin 是图像左上角对应的投影坐标，
/// px 是每投影单位的像素数，因此贴回 origin 即与布局、占地、碰撞、排序严丝合缝，占位件的面照旧用于遮挡判定。
/// 没有对应文件的件继续画程序化占位。入库由 tools/ArtGen/place.py 完成。
/// </summary>
public sealed class PieceArt
{
    private static readonly Dictionary<string, PieceArt?> Cache = [];

    private PieceArt(Texture2D texture, Vector2 origin, float px)
    {
        Texture = texture;
        Origin = origin;
        Px = px;
    }

    /// <summary>导出引导图时关闭，保证引导图永远取自布局与程序化占位，而不是已入库的 AI 件。</summary>
    public static bool Enabled { get; set; } = true;

    public Texture2D Texture { get; }

    public Vector2 Origin { get; }

    public float Px { get; }

    /// <summary>投影坐标下的画框。</summary>
    public Rect2 Frame => new(Origin, Texture.GetSize() / Px);

    public static PieceArt? Find(string id)
    {
        if (!Enabled)
        {
            return null;
        }

        if (Cache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var region = id.Split('.', 2)[0];
        var basePath = $"res://assets/art/{region}/{id}";
        PieceArt? art = null;
        if (ResourceLoader.Exists($"{basePath}.png") && Godot.FileAccess.FileExists($"{basePath}.json"))
        {
            using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString($"{basePath}.json"));
            var origin = doc.RootElement.GetProperty("origin");
            art = new PieceArt(
                GD.Load<Texture2D>($"{basePath}.png"),
                new Vector2(origin[0].GetSingle(), origin[1].GetSingle()),
                doc.RootElement.GetProperty("px").GetSingle());
        }

        Cache[id] = art;
        return art;
    }

    /// <summary>地面纹理：<c>&lt;id&gt;.png</c> 与记有 world_size（一格纹理覆盖的世界边长）的 json；着色器按世界坐标循环取样。</summary>
    public static (Texture2D Texture, float WorldSize)? FindTexture(string id)
    {
        if (!Enabled)
        {
            return null;
        }

        var basePath = $"res://assets/art/{id.Split('.', 2)[0]}/{id}";
        if (!ResourceLoader.Exists($"{basePath}.png") || !Godot.FileAccess.FileExists($"{basePath}.json"))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString($"{basePath}.json"));
        return (GD.Load<Texture2D>($"{basePath}.png"), doc.RootElement.GetProperty("world_size").GetSingle());
    }

    /// <summary>
    /// 给 town_ground 着色器的一块地面接上 AI 纹理；纹理未入库或关闭贴图时保持程序化纹样。
    /// tint 逐通道相乘调到青绿基调，desat 先收饱和度，macro 为大尺度明暗起伏的幅度（打破平铺循环），contrast 小于 1 时向平均色收拢。
    /// </summary>
    public static void ApplyGround(ShaderMaterial material, string id, Vector3 tint, float desat = 0, float macro = 0, float contrast = 1)
    {
        if (FindTexture(id) is not { } tex)
        {
            return;
        }

        material.SetShaderParameter("use_tex", true);
        material.SetShaderParameter("ground_tex", tex.Texture);
        material.SetShaderParameter("tex_world", tex.WorldSize);
        material.SetShaderParameter("tex_tint", tint);
        material.SetShaderParameter("tex_desat", desat);
        material.SetShaderParameter("tex_macro", macro);
        material.SetShaderParameter("tex_contrast", contrast);
    }

    /// <summary>画在节点局部坐标里：节点的 Position 是它在投影坐标中的原点。</summary>
    public void Draw(CanvasItem ci, Vector2 nodePosition) => ci.DrawTextureRect(Texture, new Rect2(Origin - nodePosition, Texture.GetSize() / Px), false);
}

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
