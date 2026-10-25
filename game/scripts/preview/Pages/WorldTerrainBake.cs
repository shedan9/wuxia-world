using Godot;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 江湖大地图的离线烘焙（2026-10-25）：按 <see cref="WorldTerrain"/> 算出高度图与地貌权重图，存进 <c>res://assets/maps/</c>，
/// 画布与 <c>world_relief.gdshader</c> 只读贴图。8400×5600 的地图在运行时现算要好几秒，故离线做；改了地形数据须重跑：
/// <c>Godot --path game -- --scene=res://scenes/preview/PieceGuideExport.tscn --region=worldmap-bake --out=任意目录</c>，再 <c>--import</c>。
/// <list type="bullet">
/// <item><c>worldmap.height.exr</c>：单通道半精度高度，每格 <see cref="Cell"/> 像素。海面 0、海底为负（离岸越远越深）；陆地由缓丘、
/// 西高东低的地势、高原与黄土台地抬升、沿山脉的脊状隆起组成，江河两岸下切成河谷，湖为盆地。</item>
/// <item><c>worldmap.biome0/1/2.png</c>：地貌权重（RGBA 各一种，顺序见 <see cref="WorldTerrain.BiomeKinds"/>），边缘带噪声羽化。</item>
/// </list>
/// 全部形状只取决于地形数据与固定种子。
/// </summary>
public static class WorldTerrainBake
{
    /// <summary>每格对应的地图像素。</summary>
    public const int Cell = 6;

    public const string HeightPath = "res://assets/maps/worldmap.height.exr";

    public static string BiomePath(int i) => $"res://assets/maps/worldmap.biome{i}.png";

    /// <summary>地貌边缘的羽化半宽（地图像素）。</summary>
    private const float Feather = 46;

    public static void Run()
    {
        var t = WorldTerrain.Current;
        var w = Mathf.CeilToInt(t.Frame.X / Cell);
        var h = Mathf.CeilToInt(t.Frame.Y / Cell);
        var height = new float[w * h];
        var weights = new float[WorldTerrain.BiomeKinds.Length][];
        for (var k = 0; k < weights.Length; k++)
        {
            weights[k] = new float[w * h];
        }

        var biomeBounds = t.Biomes.Select(b => WorldTerrain.Bounds(b.Poly).Grow(Feather * 2)).ToArray();
        var rangeBounds = t.Ranges.Select(r => WorldTerrain.Bounds(r.Path).Grow(r.Width * 1.4f)).ToArray();
        var rivers = t.Rivers.Select(r => (r.Path, Width: (r.From + r.To) * 0.5f, Valley: 14 + (r.From + r.To) * 1.1f)).ToArray();
        var riverBounds = rivers.Select(r => WorldTerrain.Bounds(r.Path).Grow(r.Valley)).ToArray();

        System.Threading.Tasks.Parallel.For(0, h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                var p = new Vector2((x + 0.5f) * Cell, (y + 0.5f) * Cell);
                var i = y * w + x;
                var coast = t.CoastDistance(p) + (Fbm(new Vector2(p.X / 90f, p.Y / 70f), 3) - 0.5f) * 40;

                // 地貌权重：多边形内为正的有符号距离，加噪声后羽化。
                var land = coast > 0;
                for (var b = 0; b < t.Biomes.Count && land; b++)
                {
                    if (!biomeBounds[b].HasPoint(p))
                    {
                        continue;
                    }

                    var poly = t.Biomes[b].Poly;
                    var d = WorldTerrain.EdgeDistance(poly, p, closed: true) * (WorldTerrain.Inside(poly, p) ? 1 : -1);
                    var wgt = Mathf.SmoothStep(-Feather, Feather, d + (Fbm(p / 150f + new Vector2(b * 3.1f, 1.7f), 3) - 0.5f) * Feather * 1.6f);
                    ref var slot = ref weights[t.Biomes[b].Kind][i];
                    slot = Mathf.Max(slot, wgt);
                }

                height[i] = Height(t, p, coast, i, weights, rangeBounds, rivers, riverBounds);
            }
        });

        var bytes = new byte[w * h * 4];
        Buffer.BlockCopy(height, 0, bytes, 0, bytes.Length);
        var heightImage = Image.CreateFromData(w, h, false, Image.Format.Rf, bytes);
        heightImage.Convert(Image.Format.Rh);
        Check(heightImage.SaveExr(ProjectSettings.GlobalizePath(HeightPath), grayscale: true), HeightPath);

        for (var img = 0; img * 4 < weights.Length; img++)
        {
            var data = new byte[w * h * 4];
            for (var c = 0; c < 4; c++)
            {
                var k = img * 4 + c;
                if (k >= weights.Length)
                {
                    continue;
                }

                for (var i = 0; i < w * h; i++)
                {
                    data[i * 4 + c] = (byte)Mathf.RoundToInt(Mathf.Clamp(weights[k][i], 0, 1) * 255);
                }
            }

            var image = Image.CreateFromData(w, h, false, Image.Format.Rgba8, data);
            Check(image.SavePng(ProjectSettings.GlobalizePath(BiomePath(img))), BiomePath(img));
        }

        GD.Print($"已烘焙舆图地形：{w}×{h} 格（每格 {Cell} 像素）");
    }

    private static void Check(Error error, string path)
    {
        if (error != Error.Ok)
        {
            GD.PrintErr($"写入 {path} 失败：{error}");
        }
    }

    private static float Height(WorldTerrain t, Vector2 p, float coast, int i, float[][] weights, Rect2[] rangeBounds,
        (Vector2[] Path, float Width, float Valley)[] rivers, Rect2[] riverBounds)
    {
        if (coast < 0)
        {
            return -Mathf.Clamp(-coast / 420f, 0.03f, 1f) * 0.6f;
        }

        float W(string kind) => weights[Array.IndexOf(WorldTerrain.BiomeKinds, kind)][i];

        // 底面：缓丘；西高东低（经度 112° 以西略高），高原与黄土台地只略抬一点——抬多了会落进着色器的岩褶色带，高原上满是细纹；近海压低成滨海平原。
        var ground = 0.05f + 0.1f * Fbm(p / 380f, 4) + 0.05f * Fbm(p / 120f + new Vector2(9, 3), 3)
                     + 0.04f * Mathf.SmoothStep(t.Project(112, 30).X, t.Project(95, 30).X, p.X)
                     + 0.03f * Mathf.Max(W("plateau"), W("snow")) + 0.02f * W("loess");
        ground *= Mathf.SmoothStep(0, 160, coast) * 0.7f + 0.3f;

        // 山脉：到走向折线的距离决定山体包络，脊状噪声给出峰与谷（山体本身由平视山峦画在上面，地面只留缓升的山地底色）。
        var mountain = 0f;
        for (var r = 0; r < t.Ranges.Count; r++)
        {
            if (!rangeBounds[r].HasPoint(p))
            {
                continue;
            }

            var range = t.Ranges[r];
            var d = WorldTerrain.EdgeDistance(range.Path, p);
            if (d >= range.Width * 1.3f)
            {
                continue;
            }

            var envelope = Mathf.SmoothStep(range.Width * 1.3f, 0, d + (Fbm(p / 90f + new Vector2(4, 1), 3) - 0.5f) * range.Width * 0.6f);
            mountain = Mathf.Max(mountain, envelope * range.Peak * (0.45f + 0.55f * Ridge(p / 150f, 4)));
        }

        var height = ground + mountain * 0.32f;

        // 河谷：河道两岸只浅浅下凹一成半（谷底仍高于海边沙色的阈值，河边不铺沙）；下切深了，背光一侧会拖出一道深色长条。
        for (var r = 0; r < rivers.Length; r++)
        {
            if (!riverBounds[r].HasPoint(p))
            {
                continue;
            }

            var d = WorldTerrain.EdgeDistance(rivers[r].Path, p);
            if (d < rivers[r].Valley)
            {
                height = Mathf.Lerp(Mathf.Max(0.022f, height * 0.85f), height, Mathf.SmoothStep(rivers[r].Width * 0.5f, rivers[r].Valley, d));
            }
        }

        // 湖：椭圆盆地，湖心略深。
        var lake = t.LakeFactor(p) + (Fbm(p / 30f, 2) - 0.5f) * 0.25f;
        if (lake < 2.2f)
        {
            height = lake < 1 ? -0.04f * (1.2f - lake) : Mathf.Lerp(0.01f, height, Mathf.SmoothStep(1, 2.2f, lake));
        }

        return height;
    }

    // ── 噪声 ─────────────────────────────────────────────

    /// <summary>二维值噪声，取值 0–1。</summary>
    private static float ValueNoise(Vector2 p)
    {
        var i = p.Floor();
        var f = p - i;
        var u = f * f * (Vector2.One * 3 - 2 * f);
        float Corner(float dx, float dy) => Brushwork.Hash((i.X + dx) * 127.1f + (i.Y + dy) * 311.7f);
        return Mathf.Lerp(Mathf.Lerp(Corner(0, 0), Corner(1, 0), u.X), Mathf.Lerp(Corner(0, 1), Corner(1, 1), u.X), u.Y);
    }

    internal static float Fbm(Vector2 p, int octaves)
    {
        var sum = 0f;
        var amp = 0.5f;
        var norm = 0f;
        for (var i = 0; i < octaves; i++)
        {
            sum += amp * ValueNoise(p);
            norm += amp;
            p = p * 2.03f + new Vector2(17.1f, 9.2f);
            amp *= 0.5f;
        }

        return sum / norm;
    }

    /// <summary>脊状噪声：山脊处为 1，谷底趋 0。</summary>
    private static float Ridge(Vector2 p, int octaves)
    {
        var sum = 0f;
        var amp = 0.5f;
        var norm = 0f;
        for (var i = 0; i < octaves; i++)
        {
            var n = 1 - Mathf.Abs(ValueNoise(p) * 2 - 1);
            sum += amp * n * n;
            norm += amp;
            p = p * 2.1f + new Vector2(3.3f, 7.7f);
            amp *= 0.5f;
        }

        return sum / norm;
    }
}
