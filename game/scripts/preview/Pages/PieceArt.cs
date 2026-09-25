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

    /// <summary>画在节点局部坐标里：节点的 Position 是它在投影坐标中的原点。</summary>
    public void Draw(CanvasItem ci, Vector2 nodePosition) => ci.DrawTextureRect(Texture, new Rect2(Origin - nodePosition, Texture.GetSize() / Px), false);
}
