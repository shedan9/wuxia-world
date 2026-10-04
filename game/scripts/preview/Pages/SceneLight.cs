using Godot;
using WuxiaWorld.Game.Presentation.Art;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>布景的光色时段（M3-01 光影）：由游戏时辰决定，城镇与客栈按此换光色、投影与灯火。</summary>
public enum SceneTime
{
    /// <summary>卯时至申时：白天（第一章为雨后初晴的申时）。</summary>
    Day,

    /// <summary>酉时：日落。</summary>
    Dusk,

    /// <summary>戌时至寅时：夜。</summary>
    Night,
}

public static class SceneTimes
{
    /// <summary>游戏逻辑时辰（新游戏第一日申时为 0，一个单位一个时辰，同 <c>PlaySession.ClockText</c>）换成光色时段。</summary>
    public static SceneTime FromClock(long clock) => ((clock + 8) % 12) switch
    {
        >= 3 and <= 8 => SceneTime.Day,
        9 => SceneTime.Dusk,
        _ => SceneTime.Night,
    };

    /// <summary>展示页左上的时辰天气（游戏模式由 HUD 按真实时辰写）。</summary>
    public static string Label(SceneTime time, string day) => time switch
    {
        SceneTime.Dusk => "酉时　·　日落",
        SceneTime.Night => "亥时　·　夜",
        _ => day,
    };

    /// <summary>与世界整体调色相抵的倍率：发光件放在被调色的层里，乘上它才保持原色亮度。</summary>
    public static Color Unlit(Color tint) => new(1 / tint.R, 1 / tint.G, 1 / tint.B);
}

/// <summary>
/// 地面投影（M3-01 光影）：立体件的每个面沿受光方向压到地面，所有面并成一块，再以统一的浓淡画出——
/// 放在 <see cref="CanvasGroup"/> 里先合成再整体半透明，重叠处不会叠深。画在贴地层（地面之上、排序件之下），
/// 因此只落在地上，不落在别的件上。
/// </summary>
public partial class SceneShadows : CanvasGroup
{
    private readonly List<Vector2[]> _polygons = [];

    /// <param name="alpha">投影浓度。</param>
    public SceneShadows(float alpha)
    {
        SelfModulate = new Color(1, 1, 1, alpha);
        FitMargin = 0;
        ClearMargin = 0;
    }

    /// <summary>加一件的投影：各面的顶点（世界坐标）沿 shift（每单位高度在地面上的偏移，背着太阳）压到高度 plane 的水平面上。</summary>
    public void Cast(IEnumerable<Vector3[]> faces, Vector2 shift, float plane = 0)
    {
        foreach (var points in faces)
        {
            var poly = points.Select(p => TownView.P(new Vector2(p.X, p.Y) + shift * (p.Z - plane), plane)).ToArray();
            // 与投影方向平行的面压成一条线，三角化会失败；面积太小的跳过（相邻面已经盖住）。
            if (Mathf.Abs(Area(poly)) < 4)
            {
                continue;
            }

            _polygons.Add(Geometry2D.TriangulatePolygon(poly).Length > 0 ? poly : Geometry2D.ConvexHull(poly));
        }
    }

    /// <summary>直接加一块地面上的阴影（投影坐标多边形）。</summary>
    public void AddPolygon(Vector2[] screen) => _polygons.Add(screen);

    private static float Area(Vector2[] p)
    {
        var a = 0f;
        for (var i = 0; i < p.Length; i++)
        {
            a += p[i].Cross(p[(i + 1) % p.Length]);
        }

        return a / 2;
    }

    public override void _Ready()
    {
        var shapes = new Node2D();
        shapes.Draw += () =>
        {
            // 合批：一栋房几十个面，逐个 DrawColoredPolygon 一张图上有数千次绘制调用（M3-07 的 PolyBatch）。
            var ink = new Color(0.1f, 0.16f, 0.2f);
            using var batch = new PolyBatch(shapes);
            foreach (var p in _polygons)
            {
                batch.Add(p, ink);
            }
        };
        AddChild(shapes);
    }
}

/// <summary>
/// 灯火（M3-01 光影）：地上的暖色光斑与灯笼周围的光晕，叠加混合。放在被整体调色的层里时用
/// <see cref="SceneTimes.Unlit"/> 抵消调色，夜里灯光才不被一起压暗。
/// </summary>
public partial class SceneLamps : Node2D
{
    private readonly List<(Vector2 Ground, float Radius, Color Color, float Z)> _pools = [];
    private readonly List<(Vector2 Screen, float Radius, Color Color)> _halos = [];

    public SceneLamps(Color tint)
    {
        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        Modulate = SceneTimes.Unlit(tint);
    }

    public void Pool(Vector2 ground, float radius, Color color, float z = 0) => _pools.Add((ground, radius, color, z));

    public void Halo(Vector2 screen, float radius, Color color) => _halos.Add((screen, radius, color));

    public override void _Draw()
    {
        using var batch = new PolyBatch(this);
        foreach (var (ground, radius, color, z) in _pools)
        {
            InnFloorLight.Pool(batch, ground, radius, color, z);
        }

        foreach (var (c, radius, color) in _halos)
        {
            const int n = 24;
            var edge = color with { A = 0 };
            for (var i = 0; i < n; i++)
            {
                var a0 = Mathf.Tau * i / n;
                var a1 = Mathf.Tau * (i + 1) / n;
                batch.Add([c, c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius], [color, edge, edge]);
            }
        }
    }
}

/// <summary>
/// 件上的发光图（<c>tools/ArtGen/glow_mask.py</c> 从 AI 件取出的窗纸与灯笼）：作件的子节点紧接件本身画出，
/// 前面走过的人照样挡住它；叠加混合并抵消整体调色。
/// </summary>
public partial class PieceGlow : Node2D
{
    private readonly PieceArt _art;
    private readonly Vector2 _anchor;
    private readonly Color _color;

    public PieceGlow(PieceArt art, Vector2 anchor, Color color, Color tint)
    {
        _art = art;
        _anchor = anchor;
        _color = color;
        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        Modulate = SceneTimes.Unlit(tint);
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    public override void _Draw() => DrawTextureRect(_art.Texture, new Rect2(_art.Origin - _anchor, _art.Texture.GetSize() / _art.Px), false, _color);
}

/// <summary>整幅的光色层：自左上到右下的四角渐变（天光、斜照、远处暗下），画在悬空层。</summary>
public partial class SceneWash : Node2D
{
    private readonly Color[] _corners;

    /// <param name="corners">左上、右上、右下、左下四角的颜色（含透明度）。</param>
    public SceneWash(params Color[] corners) => _corners = corners;

    public Rect2 Area { get; set; }

    public override void _Draw()
    {
        var r = Area.Grow(600);
        DrawPolygon([r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y)], _corners);
    }
}
