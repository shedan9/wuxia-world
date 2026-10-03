using System.Text.Json;
using Godot;

namespace WuxiaWorld.Game.Presentation.Art;

/// <summary>
/// AI 出件的贴图（架构文档 10.3 方案 C 的第三步“拼装”）：<c>res://assets/art/&lt;地区&gt;/&lt;id&gt;.png</c> 与同名 .json。
/// 图由 PieceGuideExport 导出的引导图约束生成，与占位件同一投影、同一画框；json 的 origin 是图像左上角对应的投影坐标，
/// px 是每投影单位的像素数，因此贴回 origin 即与布局、占地、碰撞、排序严丝合缝，占位件的面照旧用于遮挡判定。
/// 没有对应文件的件继续画程序化占位。入库由 tools/ArtGen/place.py 完成。
/// </summary>
public sealed class PieceArt
{
    private static readonly Dictionary<string, PieceArt?> Cache = [];

    /// <summary>
    /// 地面纹理同样常驻缓存：只在 _Draw 里用一次的纹理（客栈方砖地）若不留引用，C# 包装对象被 GC 回收后
    /// 引擎即释放该纹理，已录下的绘制命令退成缺省白图（导出包流程中进客栈后地面发白，2026-10-02）。
    /// </summary>
    private static readonly Dictionary<string, (Texture2D Texture, float WorldSize)?> TextureCache = [];

    /// <summary>
    /// 两个缓存里各项最后被取用时所在的“到访代”（M3-07，架构文档 12.1“只保留当前地图及受限缓存”）：每进一张图代数加一，
    /// 进图时放掉上一张图之前就没再用过的项，只留当前图与上一张图的贴图（往回走不用重载）。当前图正在用的项绝不放，
    /// 不会重演上面说的“已录下的绘制命令退成白图”。各布景类自己用静态字段留着的少数纹理（岩壁、溪岸、草丛图集等）不受影响。
    /// </summary>
    private static readonly Dictionary<string, int> LastUsed = [];

    private static int _generation;

    /// <summary>进一张新图时调用：代数加一，放掉两代以前的缓存项，并促一次回收让引擎真正释放这些纹理。</summary>
    public static void BeginMap()
    {
        _generation++;
        var stale = LastUsed.Where(e => e.Value < _generation - 1).Select(e => e.Key).ToList();
        foreach (var key in stale)
        {
            LastUsed.Remove(key);
            _ = key[0] == 'p' ? Cache.Remove(key[1..]) : TextureCache.Remove(key[1..]);
        }

        if (stale.Count > 0)
        {
            GC.Collect();
        }
    }

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

        LastUsed["p" + id] = _generation;
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

        LastUsed["t" + id] = _generation;
        if (TextureCache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var basePath = $"res://assets/art/{id.Split('.', 2)[0]}/{id}";
        (Texture2D Texture, float WorldSize)? tex = null;
        if (ResourceLoader.Exists($"{basePath}.png") && Godot.FileAccess.FileExists($"{basePath}.json"))
        {
            using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString($"{basePath}.json"));
            tex = (GD.Load<Texture2D>($"{basePath}.png"), doc.RootElement.GetProperty("world_size").GetSingle());
        }

        TextureCache[id] = tex;
        return tex;
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
