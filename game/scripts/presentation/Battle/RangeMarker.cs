using Godot;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Game.Presentation.Ui;
using Side = WuxiaWorld.Domain.Combat.Side;

namespace WuxiaWorld.Game.Presentation.Battle;

/// <summary>
/// 地面范围标记，画在人物脚下（形象层之下）。每个在范围内的阵位一枚地标：
/// 被命中者脚下一团由中心向外淡去的柔光，外加一圈四段断开的细环；范围内的空位只留一圈很淡的断环。
/// 不画连片地带与折线外框，群体范围由几枚地标并列表达。透明度轻微呼吸。
/// </summary>
public partial class RangeMarker : Control
{
    private const float Rx = 80;
    private const float Ry = 24;

    private static GradientTexture2D? _glow;

    private readonly List<(Vector2 Feet, bool Hit)> _cells = [];
    private Color _tone = UiPalette.Gilt;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Motion.Pulse(this, 0.75f, 1.4f);
    }

    /// <param name="cells">范围内的阵位与是否有人被命中。</param>
    public void Show(Side side, IEnumerable<(Position Cell, bool Hit)> cells, Color tone)
    {
        _cells.Clear();
        foreach (var (cell, hit) in cells)
        {
            _cells.Add((UnitView.Feet(side, cell), hit));
        }

        _tone = tone;
        Visible = _cells.Count > 0;
        QueueRedraw();
    }

    public void Clear()
    {
        _cells.Clear();
        Visible = false;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var glow = Glow;
        foreach (var (feet, hit) in _cells)
        {
            if (hit)
            {
                var size = new Vector2(Rx * 2.3f, Ry * 2.6f);
                DrawTextureRect(glow, new Rect2(feet - size / 2, size), false, _tone with { A = 1f });
                SegmentedRing(feet, Rx, Ry, UiPalette.Abyss with { A = 0.3f }, 3.4f, new Vector2(0, 1.5f));
                SegmentedRing(feet, Rx, Ry, _tone.Lightened(0.25f) with { A = 0.95f }, 2f, Vector2.Zero);
            }
            else
            {
                SegmentedRing(feet, Rx * 0.85f, Ry * 0.85f, _tone with { A = 0.35f }, 1.5f, Vector2.Zero);
            }
        }
    }

    /// <summary>中心实、边缘透明的径向柔光（白色，由绘制时的调色给出颜色）。</summary>
    private static GradientTexture2D Glow => _glow ??= new GradientTexture2D
    {
        Width = 128,
        Height = 128,
        Fill = GradientTexture2D.FillEnum.Radial,
        FillFrom = new Vector2(0.5f, 0.5f),
        FillTo = new Vector2(1f, 0.5f),
        Gradient = new Gradient
        {
            Offsets = [0f, 0.45f, 1f],
            Colors = [new Color(1, 1, 1, 0.85f), new Color(1, 1, 1, 0.4f), new Color(1, 1, 1, 0)],
        },
    };

    /// <summary>四段断开的椭圆细环（每段 60°，段间留 30° 空隙），像地面上的一圈点位标记。</summary>
    private void SegmentedRing(Vector2 c, float rx, float ry, Color color, float width, Vector2 offset)
    {
        const int segments = 4;
        const int steps = 14;
        var span = Mathf.Tau / segments;
        var arc = span * (2f / 3f);
        var points = new Vector2[steps + 1];
        for (var s = 0; s < segments; s++)
        {
            var start = s * span + span / 6 + Mathf.Pi / 12;
            for (var i = 0; i <= steps; i++)
            {
                var a = start + arc * i / steps;
                points[i] = c + offset + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
            }

            DrawPolyline(points, color, width, true);
        }
    }
}
