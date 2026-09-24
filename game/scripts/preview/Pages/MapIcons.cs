using Godot;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 大地图地标图标的程序化占位：每类地点一枚剪影小图（客栈、渡口、粮仓、河埠、山门、驿站、佛塔、道观、山路、丐帮），
/// 画在地标圆章里。坐标以图标中心为原点、半边长为 1 书写。正式版换成对应插画图标，调用方不变。
/// </summary>
public static class MapIcons
{
    /// <summary>在 <paramref name="c"/> 处画一枚半边长 <paramref name="s"/> 的图标：ink 为剪影，accent 为点缀，hole 为门洞等镂空。</summary>
    public static void Draw(CanvasItem ci, MapIcon icon, Vector2 c, float s, Color ink, Color accent, Color hole)
    {
        Vector2 P(float x, float y) => c + new Vector2(x, y) * s;
        void Poly(Color color, params float[] xy)
        {
            var pts = new Vector2[xy.Length / 2];
            for (var i = 0; i < pts.Length; i++)
            {
                pts[i] = P(xy[i * 2], xy[i * 2 + 1]);
            }

            ci.DrawColoredPolygon(pts, color);
        }

        void Box(Color color, float x0, float y0, float x1, float y1) => Poly(color, x0, y0, x1, y0, x1, y1, x0, y1);
        void Line(Color color, float width, params float[] xy)
        {
            var pts = new Vector2[xy.Length / 2];
            for (var i = 0; i < pts.Length; i++)
            {
                pts[i] = P(xy[i * 2], xy[i * 2 + 1]);
            }

            ci.DrawPolyline(pts, color, width * s / 12f, true);
        }

        // 翘角屋顶：两端上挑的梯形。
        void Roof(Color color, float x0, float x1, float y, float rise)
        {
            var w = x1 - x0;
            Poly(color, x0 - w * 0.08f, y - rise * 0.35f, x0 + w * 0.2f, y - rise, x1 - w * 0.2f, y - rise, x1 + w * 0.08f, y - rise * 0.35f,
                x1 - w * 0.05f, y, x0 + w * 0.05f, y);
        }

        void Waves(Color color, float y)
        {
            Line(color, 1.6f, -0.9f, y, -0.6f, y - 0.1f, -0.3f, y, 0f, y - 0.1f, 0.3f, y, 0.6f, y - 0.1f, 0.9f, y);
        }

        switch (icon)
        {
            case MapIcon.Inn:
                // 客栈：屋顶、屋身、门洞，右侧挑一面酒旗。
                Roof(ink, -0.8f, 0.4f, -0.05f, 0.4f);
                Box(ink, -0.7f, -0.05f, 0.3f, 0.75f);
                Box(hole, -0.35f, 0.3f, -0.05f, 0.75f);
                Line(ink, 1.8f, 0.7f, 0.75f, 0.7f, -0.9f);
                Poly(accent, 0.7f, -0.85f, 0.95f, -0.85f, 0.95f, -0.2f, 0.82f, -0.3f, 0.7f, -0.2f);
                break;
            case MapIcon.Ferry:
                // 渡口：乌篷船与水纹。
                Poly(ink, -0.95f, 0.15f, 0.95f, 0.15f, 0.62f, 0.5f, -0.62f, 0.5f);
                var arch = new List<float> { -0.45f, 0.15f };
                for (var i = 0; i <= 10; i++)
                {
                    var a = Mathf.Pi + Mathf.Pi * i / 10;
                    arch.Add(-0.05f + Mathf.Cos(a) * 0.4f);
                    arch.Add(0.15f + Mathf.Sin(a) * 0.42f);
                }

                Poly(ink, arch.ToArray());
                Line(accent, 1.8f, 0.55f, 0.15f, 0.8f, -0.75f);
                Waves(accent, 0.75f);
                break;
            case MapIcon.Granary:
                // 粮仓：圆仓尖顶，仓身两道箍。
                Poly(ink, -0.9f, 0.05f, 0f, -0.8f, 0.9f, 0.05f);
                Box(ink, -0.62f, 0.05f, 0.62f, 0.78f);
                Box(hole, -0.62f, 0.26f, 0.62f, 0.32f);
                Box(hole, -0.15f, 0.45f, 0.15f, 0.78f);
                Line(accent, 1.6f, -0.35f, -0.35f, 0.35f, -0.35f);
                break;
            case MapIcon.Wharf:
                // 河埠：栈桥、桩与货箱，桥下水纹。
                Box(ink, -0.95f, 0.05f, 0.6f, 0.25f);
                foreach (var x in new[] { -0.8f, -0.3f, 0.2f })
                {
                    Box(ink, x - 0.06f, 0.25f, x + 0.06f, 0.6f);
                }

                Box(ink, -0.75f, -0.45f, -0.2f, 0.05f);
                Box(accent, -0.1f, -0.25f, 0.3f, 0.05f);
                Line(ink, 1.8f, 0.75f, 0.25f, 0.75f, -0.6f);
                Line(ink, 1.4f, 0.75f, -0.45f, 0.95f, -0.1f);
                Waves(accent, 0.8f);
                break;
            case MapIcon.Gate:
                // 山门：两柱牌坊，翘角顶与额枋。
                Roof(ink, -0.85f, 0.85f, -0.4f, 0.4f);
                Box(ink, -0.6f, -0.2f, 0.6f, -0.05f);
                Box(accent, -0.25f, -0.05f, 0.25f, 0.15f);
                Box(ink, -0.62f, -0.4f, -0.42f, 0.8f);
                Box(ink, 0.42f, -0.4f, 0.62f, 0.8f);
                break;
            case MapIcon.Relay:
                // 驿站：收分的望楼，顶上一面令旗。
                Poly(ink, -0.5f, 0.8f, 0.5f, 0.8f, 0.3f, -0.2f, -0.3f, -0.2f);
                Box(hole, -0.12f, 0.35f, 0.12f, 0.8f);
                Box(hole, -0.3f, 0.05f, 0.3f, 0.1f);
                Roof(ink, -0.5f, 0.5f, -0.2f, 0.35f);
                Line(ink, 1.6f, 0f, -0.55f, 0f, -0.95f);
                Poly(accent, 0f, -0.95f, 0.45f, -0.85f, 0f, -0.72f);
                break;
            case MapIcon.Pagoda:
                // 佛塔：三层收分，塔刹。
                Box(ink, -0.55f, 0.65f, 0.55f, 0.82f);
                for (var i = 0; i < 3; i++)
                {
                    var y = 0.65f - i * 0.45f;
                    var w = 0.42f - i * 0.1f;
                    Box(ink, -w * 0.7f, y - 0.3f, w * 0.7f, y);
                    Roof(ink, -w - 0.12f, w + 0.12f, y - 0.26f, 0.2f);
                }

                Line(accent, 1.8f, 0f, -0.65f, 0f, -0.98f);
                break;
            case MapIcon.Hall:
                // 道观：台基上的重檐殿，脊上宝珠。
                Box(ink, -0.9f, 0.62f, 0.9f, 0.8f);
                Box(ink, -0.6f, 0.05f, 0.6f, 0.62f);
                Box(hole, -0.18f, 0.25f, 0.18f, 0.62f);
                Roof(ink, -0.85f, 0.85f, 0.05f, 0.3f);
                Box(ink, -0.35f, -0.4f, 0.35f, -0.2f);
                Roof(ink, -0.55f, 0.55f, -0.35f, 0.3f);
                ci.DrawCircle(P(0, -0.78f), 0.13f * s, accent);
                break;
            case MapIcon.Peak:
                // 山路：双峰，峰顶一抹金顶，山间一条之字路。
                Poly(ink, -0.95f, 0.75f, -0.4f, -0.35f, -0.1f, 0.1f, 0.35f, -0.8f, 0.95f, 0.75f);
                Poly(accent, 0.35f, -0.8f, 0.5f, -0.5f, 0.2f, -0.5f);
                Line(hole, 1.6f, -0.2f, 0.75f, 0.2f, 0.5f, -0.1f, 0.3f, 0.25f, 0.05f);
                break;
            case MapIcon.Beggars:
                // 丐帮：斜挂葫芦的竹杖。
                Line(ink, 2.6f, -0.65f, 0.85f, 0.55f, -0.85f);
                ci.DrawCircle(P(0.2f, 0.18f), 0.3f * s, accent);
                ci.DrawCircle(P(0.2f, -0.2f), 0.19f * s, accent);
                Line(ink, 1.4f, 0.08f, -0.05f, 0.32f, -0.05f);
                Poly(ink, -0.75f, 0.45f, -0.15f, 0.45f, -0.25f, 0.75f, -0.65f, 0.75f);
                break;
        }
    }
}
