using Godot;

namespace WuxiaWorld.Game.Presentation.Play;

/// <summary>
/// 存档缩略图（架构文档 11）：取视口上一帧画面，居中裁成 16:9 后缩到 448×252（卡片 224×126 的两倍，高分屏不糊），存 JPEG。
/// 缩略图与存档数据分开，抓不到或读不出时卡片退回按地图取色的程序化山水。
/// </summary>
public static class SaveThumbnail
{
    public const int Width = 448;
    public const int Height = 252;

    /// <summary>抓取视口已画好的上一帧；在打开菜单的同一帧调用，画面里还没有菜单。</summary>
    public static byte[]? Grab(Viewport viewport)
    {
        var image = viewport.GetTexture()?.GetImage();
        if (image is null || image.IsEmpty())
        {
            return null;
        }

        var (w, h) = (image.GetWidth(), image.GetHeight());
        var cropW = Math.Min(w, h * Width / Height);
        var cropH = Math.Min(h, w * Height / Width);
        if (cropW != w || cropH != h)
        {
            image = image.GetRegion(new Rect2I((w - cropW) / 2, (h - cropH) / 2, cropW, cropH));
        }

        image.Convert(Image.Format.Rgb8);
        image.Resize(Width, Height, Image.Interpolation.Lanczos);
        return image.SaveJpgToBuffer(0.85f);
    }

    public static Texture2D? Load(byte[]? jpeg)
    {
        if (jpeg is not { Length: > 0 })
        {
            return null;
        }

        var image = new Image();
        return image.LoadJpgFromBuffer(jpeg) == Error.Ok && !image.IsEmpty() ? ImageTexture.CreateFromImage(image) : null;
    }
}
