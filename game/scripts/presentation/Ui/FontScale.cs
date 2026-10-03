namespace WuxiaWorld.Game.Presentation.Ui;

/// <summary>
/// 字体缩放（M3-05，设置“正文字号”）：阅读用的字（32 号及以下：正文、次级文字、小节标题、按钮、页签、对白、提示）
/// 按 <see cref="Factor"/> 放大或缩小；页名、印章、标题字等 34 号以上的展示字不缩放，免得撑破印章与版式。
/// 所有字号经 <see cref="Of"/> 换算——<see cref="Ui.Text"/>、全局主题与各页直接设的字号覆盖都走这里；
/// 改动后重建主题，已打开的页面重建后生效。
/// </summary>
public static class FontScale
{
    /// <summary>默认正文字号；设置里的“正文字号”与它之比即缩放倍数。</summary>
    public const int BaseBody = 24;

    public const int Largest = 32;

    public static float Factor { get; set; } = 1;

    public static int Of(int size) => size <= Largest ? (int)MathF.Round(size * Factor) : size;
}
