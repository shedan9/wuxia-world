using System.Text.Json;
using Godot;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// AI 全身人物形象（架构文档 10.2）：<c>res://assets/art/figure/figure.&lt;人物&gt;.png</c> 与同名 .json。
/// 所有人物由 tools/ArtGen/figure.py 按同一副骨架生成、四分之三侧身朝画面左侧；json 记脚底中点 foot（像素）与身高 stature（头顶到脚底的像素数），
/// 引擎按“屏幕身高 ÷ stature”缩放、脚底对齐地面，朝右时水平翻转（第一阶段允许，正式 4 / 8 向形象见 M1 起）。
/// 探索与战斗共用同一张图，保证两处形象一致；没有对应文件时调用方继续画程序化占位。入库由 tools/ArtGen/place.py --figure 完成。
/// </summary>
public sealed class FigureArt
{
    private static readonly Dictionary<string, FigureArt?> Cache = [];

    private FigureArt(Texture2D texture, Vector2 foot, float stature)
    {
        Texture = texture;
        Foot = foot;
        Stature = stature;
    }

    public Texture2D Texture { get; }

    /// <summary>脚底中点在图中的像素坐标。</summary>
    public Vector2 Foot { get; }

    /// <summary>头顶到脚底的像素高度（长篙、刀尖高出头顶的部分不计）。</summary>
    public float Stature { get; }

    public static FigureArt? Find(string id)
    {
        if (!PieceArt.Enabled)
        {
            return null;
        }

        if (Cache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var basePath = $"res://assets/art/figure/{id}";
        FigureArt? art = null;
        if (ResourceLoader.Exists($"{basePath}.png") && Godot.FileAccess.FileExists($"{basePath}.json"))
        {
            using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString($"{basePath}.json"));
            var foot = doc.RootElement.GetProperty("foot");
            art = new FigureArt(
                GD.Load<Texture2D>($"{basePath}.png"),
                new Vector2(foot[0].GetSingle(), foot[1].GetSingle()),
                doc.RootElement.GetProperty("stature").GetSingle());
        }

        Cache[id] = art;
        return art;
    }

    /// <summary>
    /// 以 feet（节点局部坐标）为脚底画出，屏幕身高 height；facing 为 1 朝右（翻转源图）、-1 朝左。
    /// lean 为绕脚底的倾斜弧度（走动时身体前倾），stretch 为竖向伸缩（步伐起落）。
    /// </summary>
    public void Draw(CanvasItem ci, Vector2 feet, float height, int facing, float lean = 0, float stretch = 1, Color? modulate = null)
    {
        var k = height / Stature;
        // 源图朝左：facing 为 1（朝右）时水平翻转。
        ci.DrawSetTransform(feet, lean * facing, new Vector2(-k * facing, k * stretch));
        ci.DrawTexture(Texture, -Foot, modulate ?? Colors.White);
        ci.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>画在节点局部坐标中的外框（未倾斜时），用于排序与遮挡判定。</summary>
    public Rect2 Bounds(Vector2 feet, float height, int facing)
    {
        var k = height / Stature;
        var size = Texture.GetSize() * k;
        var left = facing < 0 ? feet.X - Foot.X * k : feet.X - (size.X - Foot.X * k);
        return new Rect2(left, feet.Y - Foot.Y * k, size.X, size.Y);
    }
}
