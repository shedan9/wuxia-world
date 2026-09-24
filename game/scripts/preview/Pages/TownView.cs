using Godot;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 城镇布景的统一视角：正交斜俯视投影。世界坐标 x 向东、y 向南、z 向上（约厘米）。
/// 2026-09-25 用户定为：水平转 45°、下俯 45°，镜头在西南方，东西向的街道自左下延伸到右上（Yaw = -45°）；
/// 此前转 +45°（镜头在东南、街道自左上到右下）被否定。可见的墙面为南墙与西墙。公式对任意转角成立。
/// 地面、墙、屋顶、桥、驳岸和人物全部经这一个投影，避免“俯视地面 + 平视房屋”的拼接感。
/// 投影是线性的，同一水平面上的点可由着色器反算回世界坐标（town_ground.gdshader）。
/// </summary>
public static class TownView
{
    /// <summary>水平转角（度）：-45，镜头在西南方，东西向街道在画面上自左下向右上。</summary>
    public const float Yaw = -45;

    /// <summary>俯角（度）：45。</summary>
    public const float Pitch = 45;

    /// <summary>世界单位到逻辑像素的比例：人高 175 在俯 45° 下约 150 逻辑像素（架构文档 10.2 的 120–180）。</summary>
    public const float Scale = 1.2f;

    /// <summary>受光方向（指向太阳）：午后日在西南偏高处，南墙最亮、朝北的坡面暗一阶。</summary>
    public static readonly Vector3 Sun = new Vector3(-0.35f, 0.62f, 0.72f).Normalized();

    private static readonly float _c = Mathf.Cos(Mathf.DegToRad(Yaw));
    private static readonly float _s = Mathf.Sin(Mathf.DegToRad(Yaw));
    private static readonly float _cp = Mathf.Cos(Mathf.DegToRad(Pitch));
    private static readonly float _sp = Mathf.Sin(Mathf.DegToRad(Pitch));

    /// <summary>世界的竖直方向（z 向上）；注意 Godot 的 Vector3.Up/Down 是 y 轴，不能用。</summary>
    public static readonly Vector3 Above = new(0, 0, 1);

    public static readonly Vector3 Below = new(0, 0, -1);

    /// <summary>竖直方向的投影比例：人物、树等立着画的精灵按此缩放高度。</summary>
    public static float Upright => _cp * Scale;

    public static Vector2 P(Vector3 w) => new((w.X * _c - w.Y * _s) * Scale, ((w.X * _s + w.Y * _c) * _sp - w.Z * _cp) * Scale);

    public static Vector2 P(float x, float y, float z = 0) => P(new Vector3(x, y, z));

    public static Vector2 P(Vector2 ground, float z = 0) => P(new Vector3(ground.X, ground.Y, z));

    /// <summary>指向镜头的方向：面的法线与之点积为正才朝向镜头。</summary>
    public static Vector3 ToCamera => new(_s * _cp, _c * _cp, _sp);

    /// <summary>离镜头的远近：值越大越近。</summary>
    public static float Depth(Vector3 w) => w.Dot(ToCamera);

    public static float Depth(Vector2 ground) => ground.X * _s + ground.Y * _c;

    public static bool Facing(Vector3 normal) => normal.Dot(ToCamera) > 1e-3f;

    /// <summary>屏幕方向（x 向右、y 向下）换成地面上的行走方向，未归一化。</summary>
    public static Vector2 GroundFromScreen(Vector2 screen) =>
        new(screen.X * _c + screen.Y * _s, -screen.X * _s + screen.Y * _c);

    /// <summary>地面方向在屏幕上的水平分量：决定精灵朝左还是朝右。</summary>
    public static float ScreenX(Vector2 ground) => ground.X * _c - ground.Y * _s;

    /// <summary>三阶赛璐璐明暗：1 受光、约 0.84 侧光、约 0.7 背光。</summary>
    public static float Light(Vector3 normal)
    {
        var l = normal.Dot(Sun);
        return l > 0.55f ? 1f : l > 0.15f ? 0.84f : 0.7f;
    }

    /// <summary>面内局部坐标到屏幕的仿射变换：局部 x 沿 u、局部 y 沿 down（均为世界单位），原点在 origin。</summary>
    public static Transform2D Local(Vector3 origin, Vector3 u, Vector3 down) => new(P(u), P(down), P(origin));
}

/// <summary>布景的一个面：世界坐标多边形、外法线、底色，可带面内贴花（窗、门、瓦垄……）。</summary>
public sealed class Face
{
    public required Vector3[] Points { get; init; }
    public required Vector3 Normal { get; init; }
    public required Color Color { get; init; }

    /// <summary>贴花：在 <see cref="Local"/> 坐标系下绘制，面内局部单位 = 世界单位。</summary>
    public Action<CanvasItem>? Decal { get; init; }

    public Transform2D Local { get; init; } = Transform2D.Identity;

    public float Outline { get; init; } = 2f;

    /// <summary>按受光方向压暗；水面、贴地细节等不参与明暗。</summary>
    public bool Lit { get; init; } = true;

    public Vector3 Center
    {
        get
        {
            var sum = Vector3.Zero;
            foreach (var p in Points) sum += p;
            return sum / Points.Length;
        }
    }

    private static readonly Color ShadeTint = new(0.10f, 0.17f, 0.26f);

    public void Draw(CanvasItem ci)
    {
        if (Normal != Vector3.Zero && !TownView.Facing(Normal))
        {
            return;
        }

        var pts = new Vector2[Points.Length];
        for (var i = 0; i < pts.Length; i++) pts[i] = TownView.P(Points[i]);
        ci.DrawColoredPolygon(pts, Color);
        if (Decal is { } decal)
        {
            ci.DrawSetTransformMatrix(Local);
            decal(ci);
            ci.DrawSetTransformMatrix(Transform2D.Identity);
        }

        if (Lit && Normal != Vector3.Zero)
        {
            var light = TownView.Light(Normal);
            if (light < 1)
            {
                ci.DrawColoredPolygon(pts, ShadeTint with { A = (1 - light) * 1.25f });
            }
        }

        if (Outline > 0)
        {
            Cel.Outline(ci, pts, Outline);
        }
    }
}

/// <summary>由世界坐标搭面的工具：盒、沿各轴挤出的截面体。输出已剔除底面，并按远近排好。</summary>
public static class Solid
{
    public static List<Face> Box(Vector3 min, Vector3 max, Color color, float outline = 2f) =>
        ExtrudeZ([new(min.X, min.Y), new(max.X, min.Y), new(max.X, max.Y), new(min.X, max.Y)], min.Z, max.Z, color, color, outline);

    /// <summary>平面多边形（x, y）竖直挤出 z0–z1：侧面 + 顶面。</summary>
    public static List<Face> ExtrudeZ(Vector2[] profile, float z0, float z1, Color side, Color top, float outline = 2f)
    {
        profile = Ccw(profile);
        var faces = new List<Face>();
        for (var i = 0; i < profile.Length; i++)
        {
            var a = profile[i];
            var b = profile[(i + 1) % profile.Length];
            var edge = b - a;
            // (x, y) 逆时针时外法线为 (dy, -dx)。
            var n = new Vector3(edge.Y, -edge.X, 0).Normalized();
            faces.Add(new Face
            {
                Points = [new(a.X, a.Y, z0), new(b.X, b.Y, z0), new(b.X, b.Y, z1), new(a.X, a.Y, z1)],
                Normal = n, Color = side, Outline = outline,
            });
        }

        Sort(faces);
        faces.Add(new Face { Points = profile.Select(p => new Vector3(p.X, p.Y, z1)).ToArray(), Normal = TownView.Above, Color = top, Outline = outline });
        return faces;
    }

    /// <summary>截面（y, z）沿 x 挤出 x0–x1：两端截面 + 各边侧面。</summary>
    public static List<Face> ExtrudeX(Vector2[] profile, float x0, float x1, Color color, float outline = 2f)
    {
        profile = Ccw(profile);
        var faces = new List<Face>();
        for (var i = 0; i < profile.Length; i++)
        {
            var a = profile[i];
            var b = profile[(i + 1) % profile.Length];
            var edge = b - a;
            var n = new Vector3(0, edge.Y, -edge.X).Normalized();
            faces.Add(new Face
            {
                Points = [new(x0, a.X, a.Y), new(x1, a.X, a.Y), new(x1, b.X, b.Y), new(x0, b.X, b.Y)],
                Normal = n, Color = color, Outline = outline,
            });
        }

        faces.Add(new Face { Points = profile.Select(p => new Vector3(x0, p.X, p.Y)).ToArray(), Normal = new Vector3(-1, 0, 0), Color = color, Outline = outline });
        faces.Add(new Face { Points = profile.Select(p => new Vector3(x1, p.X, p.Y)).ToArray(), Normal = new Vector3(1, 0, 0), Color = color, Outline = outline });
        Sort(faces);
        return faces;
    }

    /// <summary>截面（x, z）沿 y 挤出 y0–y1。</summary>
    public static List<Face> ExtrudeY(Vector2[] profile, float y0, float y1, Color color, float outline = 2f)
    {
        profile = Ccw(profile);
        var faces = new List<Face>();
        for (var i = 0; i < profile.Length; i++)
        {
            var a = profile[i];
            var b = profile[(i + 1) % profile.Length];
            var edge = b - a;
            var n = new Vector3(edge.Y, 0, -edge.X).Normalized();
            faces.Add(new Face
            {
                Points = [new(a.X, y0, a.Y), new(b.X, y0, b.Y), new(b.X, y1, b.Y), new(a.X, y1, a.Y)],
                Normal = n, Color = color, Outline = outline,
            });
        }

        faces.Add(new Face { Points = profile.Select(p => new Vector3(p.X, y0, p.Y)).ToArray(), Normal = new Vector3(0, -1, 0), Color = color, Outline = outline });
        faces.Add(new Face { Points = profile.Select(p => new Vector3(p.X, y1, p.Y)).ToArray(), Normal = new Vector3(0, 1, 0), Color = color, Outline = outline });
        Sort(faces);
        return faces;
    }

    /// <summary>远的先画。</summary>
    public static void Sort(List<Face> faces) => faces.Sort((a, b) => TownView.Depth(a.Center).CompareTo(TownView.Depth(b.Center)));

    private static Vector2[] Ccw(Vector2[] p)
    {
        var area = 0f;
        for (var i = 0; i < p.Length; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Length];
            area += a.X * b.Y - b.X * a.Y;
        }

        return area >= 0 ? p : p.Reverse().ToArray();
    }
}

/// <summary>
/// 参与前后排序的物件：地面占地（x, y 的外接矩形）、屏幕外框。
/// 两件在屏幕上重叠时，按占地是否在镜头方向上分离决定前后（斜视下 y 更南或 x 更东的在前），
/// 无法分离时按占地中心远近；Overhead（廊棚屋面）压在其下的行人之上。
/// </summary>
public interface ISortable
{
    Rect2 Foot { get; }
    Rect2 ScreenBox { get; }
    bool Overhead { get; }
    bool Walker { get; }
    CanvasItem Item { get; }
}

public static class DepthSort
{
    /// <summary>a 在 b 之前（更靠近镜头）返回 1，之后返回 -1，无法判定返回 0。</summary>
    public static int Compare(ISortable a, ISortable b)
    {
        if (a.Overhead && b.Walker && !Ahead(b.Foot, a.Foot)) return 1;
        if (b.Overhead && a.Walker && !Ahead(a.Foot, b.Foot)) return -1;
        if (Ahead(a.Foot, b.Foot)) return 1;
        if (Ahead(b.Foot, a.Foot)) return -1;
        return 0;
    }

    /// <summary>占地 a 整个在 b 的镜头一侧。</summary>
    public static bool Ahead(Rect2 a, Rect2 b)
    {
        var dir = TownView.GroundFromScreen(new Vector2(0, 1));
        var eps = 0.05f;
        return (dir.X > eps && a.Position.X >= b.End.X - 0.5f) || (dir.X < -eps && a.End.X <= b.Position.X + 0.5f)
            || (dir.Y > eps && a.Position.Y >= b.End.Y - 0.5f) || (dir.Y < -eps && a.End.Y <= b.Position.Y + 0.5f);
    }

    /// <summary>拓扑排序，把次序写进 ZIndex（远的小）。</summary>
    public static void Apply(IReadOnlyList<ISortable> items, int baseZ = 1)
    {
        var n = items.Count;
        var after = new List<int>[n];
        var incoming = new int[n];
        for (var i = 0; i < n; i++) after[i] = [];
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                if (!items[i].ScreenBox.Intersects(items[j].ScreenBox))
                {
                    continue;
                }

                var order = Compare(items[i], items[j]);
                if (order == 0)
                {
                    order = TownView.Depth(items[i].Foot.GetCenter()).CompareTo(TownView.Depth(items[j].Foot.GetCenter()));
                }

                if (order > 0)
                {
                    after[j].Add(i);
                    incoming[i]++;
                }
                else
                {
                    after[i].Add(j);
                    incoming[j]++;
                }
            }
        }

        var done = new bool[n];
        for (var rank = 0; rank < n; rank++)
        {
            // 可放的里挑最远的；若成环（没有入度为 0 的），也挑剩下最远的打破。
            var pick = -1;
            var pickFree = false;
            for (var i = 0; i < n; i++)
            {
                if (done[i]) continue;
                var free = incoming[i] == 0;
                if (pick < 0 || (free && !pickFree) || (free == pickFree && TownView.Depth(items[i].Foot.GetCenter()) < TownView.Depth(items[pick].Foot.GetCenter())))
                {
                    pick = i;
                    pickFree = free;
                }
            }

            done[pick] = true;
            items[pick].Item.ZIndex = baseZ + rank;
            foreach (var k in after[pick]) incoming[k]--;
        }
    }
}
