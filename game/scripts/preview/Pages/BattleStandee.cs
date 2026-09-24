using Godot;
using WuxiaWorld.Game.Presentation;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 战斗形象占位：纸影剪影（头、肩、衣摆）或机关方框，带地面投影。
/// 只用于核对站位、比例与界面层级；正式战斗形象见架构文档 10.2，不以立绘平移代替。
/// </summary>
public partial class BattleStandee : Control
{
    public Color Tone { get; init; } = UiPalette.Trim;
    public bool FacingLeft { get; init; }
    public bool Mechanism { get; init; }
    public float Height { get; init; } = 300;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(Height * 0.55f, Height);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Ignore;
        PivotOffset = new Vector2(Size.X / 2, Size.Y);
    }

    public override void _Draw()
    {
        var w = Size.X;
        var h = Size.Y;
        var cx = w / 2;
        var shadow = UiPalette.Abyss with { A = 0.35f };
        DrawSetTransform(new Vector2(cx, h - 4), 0, new Vector2(1, 0.22f));
        DrawCircle(Vector2.Zero, w * 0.52f, shadow);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        var dark = Tone.Darkened(0.45f);
        var light = Tone.Lightened(0.15f);
        if (Mechanism)
        {
            // 水门：两根立柱夹一扇闸板。
            var top = h * 0.35f;
            DrawRect(new Rect2(cx - w * 0.5f, top, w * 0.12f, h - top - 6), dark);
            DrawRect(new Rect2(cx + w * 0.38f, top, w * 0.12f, h - top - 6), dark);
            DrawRect(new Rect2(cx - w * 0.38f, top + h * 0.12f, w * 0.76f, h * 0.42f), Tone);
            for (var i = 1; i < 4; i++)
            {
                var y = top + h * 0.12f + h * 0.42f * i / 4;
                DrawLine(new Vector2(cx - w * 0.38f, y), new Vector2(cx + w * 0.38f, y), dark, 2);
            }

            DrawRect(new Rect2(cx - w * 0.56f, top - 10, w * 1.12f, 14), dark);
            return;
        }

        var dir = FacingLeft ? -1 : 1;
        var headR = h * 0.075f;
        var head = new Vector2(cx + dir * w * 0.04f, h * 0.14f);
        // 衣身：肩宽、腰收、衣摆外张，略向面对方向倾斜。
        Vector2[] robe =
        [
            new(cx - w * 0.30f + dir * 6, h * 0.25f),
            new(cx + w * 0.30f + dir * 6, h * 0.25f),
            new(cx + w * 0.20f, h * 0.55f),
            new(cx + w * 0.40f, h - 8),
            new(cx - w * 0.40f, h - 8),
            new(cx - w * 0.20f, h * 0.55f),
        ];
        DrawColoredPolygon(robe, Tone);
        // 受光面：朝向一侧的半身稍亮，腰带一道暗色。
        Vector2[] lit =
        [
            new(cx + dir * 6, h * 0.25f),
            new(cx + dir * (w * 0.30f + 6), h * 0.25f),
            new(cx + dir * w * 0.20f, h * 0.55f),
            new(cx + dir * w * 0.40f, h - 8),
            new(cx, h - 8),
        ];
        DrawColoredPolygon(lit, light with { A = 0.55f });
        DrawRect(new Rect2(cx - w * 0.21f, h * 0.50f, w * 0.42f, h * 0.04f), dark);
        DrawCircle(head + new Vector2(0, h * 0.015f), headR * 1.25f, dark);
        DrawCircle(head, headR, Tone.Lightened(0.35f));
        // 兵刃：一道斜线。
        var grip = new Vector2(cx + dir * w * 0.28f, h * 0.52f);
        DrawLine(grip, grip + new Vector2(dir * w * 0.45f, -h * 0.30f), UiPalette.Surface with { A = 0.85f }, 3, true);
        // 轮廓线。
        var outline = new Vector2[robe.Length + 1];
        robe.CopyTo(outline, 0);
        outline[^1] = robe[0];
        DrawPolyline(outline, dark, 2, true);
    }
}
