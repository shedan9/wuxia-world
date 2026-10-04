using System.Text.Json;
using Godot;

namespace WuxiaWorld.Game.Presentation.Art;

/// <summary>
/// AI 全身人物形象（架构文档 10.2）：<c>res://assets/art/figure/figure.&lt;人物&gt;.png</c> 与同名 .json。
/// 所有人物由 tools/ArtGen/figure.py 按同一副骨架生成、四分之三侧身朝画面右侧（右肩在画面左、靠近观者）；json 记脚底中点 foot（像素）与身高 stature（头顶到脚底的像素数），
/// 引擎按“屏幕身高 ÷ stature”缩放、脚底对齐地面，朝左时水平翻转（第一阶段允许，正式 4 / 8 向形象见 M1 起）。
/// 探索与战斗共用同一张图，保证两处形象一致；没有对应文件时调用方继续画程序化占位。入库由 tools/ArtGen/place.py --figure 完成。
/// 战斗道具（<c>battle.&lt;道具&gt;</c>，如水门机关）用同一格式：foot 为底边中点、stature 为整高，入库由 place.py --prop 完成，
/// 文件按 id 前缀放在 <c>assets/art/&lt;前缀&gt;/</c>。
/// </summary>
public sealed class FigureArt
{
    private static readonly Dictionary<string, FigureArt?> Cache = [];

    private FigureArt(Texture2D texture, Vector2 foot, float stature, float? keepX)
    {
        Texture = texture;
        Foot = foot;
        Stature = stature;
        KeepX = keepX;
    }

    public Texture2D Texture { get; }

    private Image? _image;

    /// <summary>脚底中点在图中的像素坐标。</summary>
    public Vector2 Foot { get; }

    /// <summary>头顶到脚底的像素高度（长篙、刀尖高出头顶的部分不计）。</summary>
    public float Stature { get; }

    /// <summary>竖直长道具（陆青禾的长篙）左缘的贴图 x：裙摆摆动不横移这一列及其右侧（json 可选键 keep_x）。</summary>
    public float? KeepX { get; }

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

        var basePath = $"res://assets/art/{id.Split('.', 2)[0]}/{id}";
        FigureArt? art = null;
        if (ResourceLoader.Exists($"{basePath}.png") && Godot.FileAccess.FileExists($"{basePath}.json"))
        {
            using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString($"{basePath}.json"));
            var foot = doc.RootElement.GetProperty("foot");
            art = new FigureArt(
                GD.Load<Texture2D>($"{basePath}.png"),
                new Vector2(foot[0].GetSingle(), foot[1].GetSingle()),
                doc.RootElement.GetProperty("stature").GetSingle(),
                doc.RootElement.TryGetProperty("keep_x", out var keep) ? keep.GetSingle() : null);
        }

        Cache[id] = art;
        return art;
    }

    /// <summary>
    /// 以 feet（节点局部坐标）为脚底画出，屏幕身高 height；facing 为 1 朝右（源图原样）、-1 朝左（翻转源图）。
    /// lean 为绕脚底的倾斜弧度（走动时身体前倾），stretch 为竖向伸缩（步伐起落）。
    /// </summary>
    public void Draw(CanvasItem ci, Vector2 feet, float height, int facing, float lean = 0, float stretch = 1, Color? modulate = null)
    {
        var k = height / Stature;
        // 源图朝右：facing 为 -1（朝左）时水平翻转。
        ci.DrawSetTransform(feet, lean * facing, new Vector2(k * facing, k * stretch));
        ci.DrawTexture(Texture, -Foot, modulate ?? Colors.White);
        ci.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>
    /// 同 <see cref="Draw"/>，但贴图四周外扩 pad 个贴图像素画出（外扩处 UV 超出 0–1，由着色器取透明），
    /// 供裙摆摆动着色器（figure_skirt）把裙摆横移到原外框以外。
    /// </summary>
    public void DrawPadded(CanvasItem ci, Vector2 feet, float height, int facing, float pad)
    {
        var k = height / Stature;
        ci.DrawSetTransform(feet, 0, new Vector2(k * facing, k));
        var size = Texture.GetSize();
        var grow = new Vector2(pad, pad);
        ci.DrawTextureRectRegion(Texture, new Rect2(-Foot - grow, size + grow * 2), new Rect2(-grow, size + grow * 2));
        ci.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>
    /// 与 <see cref="Draw"/> 同一变换下，节点局部坐标 point 处贴图的不透明度（0–1），用于按人物轮廓点选。
    /// 首次调用时取出贴图像素并缓存（压缩格式先解压）。
    /// </summary>
    public float AlphaAt(Vector2 point, Vector2 feet, float height, int facing)
    {
        if (_image is null)
        {
            _image = Texture.GetImage();
            if (_image.IsCompressed())
            {
                _image.Decompress();
            }
        }

        var k = height / Stature;
        var texel = new Vector2((point.X - feet.X) / (k * facing), (point.Y - feet.Y) / k) + Foot;
        var x = (int)texel.X;
        var y = (int)texel.Y;
        return x < 0 || y < 0 || x >= _image.GetWidth() || y >= _image.GetHeight() ? 0 : _image.GetPixel(x, y).A;
    }

    /// <summary>画在节点局部坐标中的外框（未倾斜时），用于排序与遮挡判定。</summary>
    public Rect2 Bounds(Vector2 feet, float height, int facing)
    {
        var k = height / Stature;
        var size = Texture.GetSize() * k;
        var left = facing > 0 ? feet.X - Foot.X * k : feet.X - (size.X - Foot.X * k);
        return new Rect2(left, feet.Y - Foot.Y * k, size.X, size.Y);
    }
}
