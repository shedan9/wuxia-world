using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 地标：圆章里一枚地点图标（<see cref="MapIcons"/>，M0 为程序化占位）。已到访为朱砂实章、未到访为绢底朱边、未开放为灰淡；
/// 有未结算事件时章上挂一粒上下浮动的泥金菱形；当前所在以石青双圈标出，选中时四角加石青折角、悬停为赭石折角。
/// 竖排地名印平时隐去，鼠标悬停或键盘跳选时才显示在章的上方。
/// </summary>
public partial class MapLandmark : Button
{
    public enum Look
    {
        Visited,
        Known,
        Locked,
    }

    private const float Radius = 21;
    private const float Hit = 50;
    private const float TagWidth = 42;
    private const float CharHeight = 27;

    /// <summary>地名印底边：章顶上方留一小段竖笔。</summary>
    private const float TagBottom = Hit / 2 - Radius - 12;

    public MapNode Node => _node;

    private readonly MapNode _node;
    private readonly Panel _tag;
    private readonly Label _label;
    private readonly Control _crown;
    private Label? _diamond;
    private Look _look;
    private bool _here;
    private bool _hovered;
    private bool _tagged;

    public MapLandmark(MapNode node)
    {
        _node = node;
        ToggleMode = true;
        FocusMode = FocusModeEnum.None;
        Size = new Vector2(Hit, Hit);
        foreach (var style in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
        {
            AddThemeStyleboxOverride(style, new StyleBoxEmpty());
        }

        var chars = node.Name.EnumerateRunes().Count();
        var tagHeight = chars * CharHeight + 18;
        _tag = new Panel
        {
            MouseFilter = MouseFilterEnum.Ignore, Visible = false, Size = new Vector2(TagWidth, tagHeight),
            Position = new Vector2((Hit - TagWidth) / 2, TagBottom - tagHeight),
        };
        _label = Ui.Text(Ui.Vertical(node.Name), UiTheme.SealLabel, 23);
        _label.AddThemeConstantOverride("line_spacing", -3);
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _label.MouseFilter = MouseFilterEnum.Ignore;
        _tag.AddChild(_label);
        AddChild(_tag);

        _crown = new Control { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_crown);

        MouseEntered += () => SetHovered(true);
        MouseExited += () => SetHovered(false);
        Toggled += _ => QueueRedraw();
        UpdateTag();
    }

    /// <summary>键盘跳选时由页面置为 true，鼠标不在其上时地名印也显示。</summary>
    public bool Tagged
    {
        set
        {
            _tagged = value;
            UpdateTag();
        }
    }

    /// <summary>把地标章的中心对准屏幕上的 <paramref name="center"/>。</summary>
    public void Place(Vector2 center) => Position = (center - new Vector2(Hit, Hit) / 2).Round();

    public void SetSelected(bool selected)
    {
        SetPressedNoSignal(selected);
        QueueRedraw();
    }

    private void SetHovered(bool hovered)
    {
        _hovered = hovered;
        UpdateTag();
    }

    private void UpdateTag()
    {
        var show = _hovered || _tagged;
        _tag.Visible = show;
        // 显示地名印的地标压在其他地标之上，菱形随之挂到印的上方。
        ZIndex = show ? 2 : 0;
        _crown.Position = new Vector2(Hit / 2, show ? _tag.Position.Y - 4 : Hit / 2 - Radius - 2);
        QueueRedraw();
    }

    public void SetState(bool visited, bool here)
    {
        _here = here;
        _look = _node.State == NodeState.Locked ? Look.Locked : visited ? Look.Visited : Look.Known;
        _tag.AddThemeStyleboxOverride("panel", Box(_look));
        _label.AddThemeColorOverride("font_color", _look switch
        {
            Look.Visited => UiPalette.Surface,
            Look.Known => UiPalette.Cinnabar.Darkened(0.1f),
            _ => UiPalette.TextMuted,
        });

        var hasEvent = _node.Events.Any(e => !e.StartsWith('✓'));
        if (hasEvent && _diamond is null)
        {
            _diamond = Ui.Text("◆", size: 22);
            _diamond.AddThemeColorOverride("font_color", UiPalette.Gilt);
            _diamond.AddThemeColorOverride("font_outline_color", UiPalette.Abyss with { A = 0.8f });
            _diamond.AddThemeConstantOverride("outline_size", 6);
            _diamond.MouseFilter = MouseFilterEnum.Ignore;
            _diamond.Position = new Vector2(-11, -30);
            _crown.AddChild(_diamond);
            if (Motion.Enabled)
            {
                var bob = _diamond.CreateTween().SetLoops().SetTrans(Tween.TransitionType.Sine);
                bob.TweenProperty(_diamond, "position:y", -38f, 0.9f);
                bob.TweenProperty(_diamond, "position:y", -30f, 0.9f);
            }
        }

        QueueRedraw();
    }

    /// <summary>地名印的样式。</summary>
    private static OrnateBox Box(Look look)
    {
        var box = look switch
        {
            Look.Visited => new OrnateBox
            {
                FillA = UiPalette.Cinnabar.Lightened(0.04f), FillB = UiPalette.Cinnabar.Darkened(0.14f),
                Grain = UiPalette.Surface with { A = 0.25f }, GrainScale = 0.4f,
                Inner = UiPalette.Surface with { A = 0.6f }, InnerInset = 3.5f, Brush = true,
            },
            Look.Known => new OrnateBox
            {
                FillA = UiPalette.Surface, FillB = UiPalette.SurfaceShade,
                Border = UiPalette.Cinnabar with { A = 0.9f }, BorderWidth = 1.8f, Brush = true, Overshoot = 0.3f,
            },
            _ => new OrnateBox
            {
                FillA = UiPalette.SurfaceShade with { A = 0.92f }, FillB = UiPalette.SurfaceShade with { A = 0.85f },
                Border = UiPalette.TextMuted with { A = 0.6f }, BorderWidth = 1.3f, Brush = true, Overshoot = 0.3f,
            },
        };
        box.Ragged = 1.3f;
        box.Seed = (int)look * 7 + 3;
        box.Shadow = UiPalette.Abyss with { A = 0.25f };
        box.ShadowSize = 3;
        box.ShadowOffset = new Vector2(0, 2);
        return box;
    }

    /// <summary>地标圆章：毛边圆面、章边与图标。图例共用。</summary>
    public static void DrawBadge(CanvasItem ci, Vector2 c, float r, Look look, MapIcon icon, bool hover = false)
    {
        var (fill, rim, ink, accent) = look switch
        {
            Look.Visited => (UiPalette.Cinnabar.Lightened(hover ? 0.12f : 0.02f), UiPalette.Cinnabar.Darkened(0.35f),
                UiPalette.Surface, UiPalette.Gilt),
            Look.Known => (UiPalette.Surface.Lightened(hover ? 0.1f : 0), UiPalette.Cinnabar, UiPalette.Text, UiPalette.Cinnabar),
            _ => (UiPalette.SurfaceShade.Darkened(0.05f), UiPalette.TextMuted with { A = 0.7f },
                UiPalette.TextMuted with { A = 0.8f }, UiPalette.TextMuted with { A = 0.55f }),
        };

        const int n = 30;
        var pts = new Vector2[n + 1];
        for (var i = 0; i < n; i++)
        {
            var a = Mathf.Tau * i / n;
            var wobble = 1 + (Brushwork.Noise(i * 0.9f + (int)icon * 3.1f) - 0.5f) * 0.07f;
            pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * wobble;
        }

        pts[n] = pts[0];
        ci.DrawCircle(c + new Vector2(0, 3), r + 1, UiPalette.Abyss with { A = 0.3f });
        ci.DrawColoredPolygon(pts[..n], fill);
        ci.DrawPolyline(pts, rim, look == Look.Locked ? 1.6f : 2.4f, true);
        if (look == Look.Visited)
        {
            ci.DrawArc(c, r - 3.5f, 0, Mathf.Tau, 36, UiPalette.Surface with { A = 0.55f }, 1.2f, true);
        }

        MapIcons.Draw(ci, icon, c, r * 0.58f, ink, accent, fill);
    }

    public override void _Draw()
    {
        var c = new Vector2(Hit, Hit) / 2;
        if (_here)
        {
            DrawArc(c, Radius + 6, 0, Mathf.Tau, 48, UiPalette.Accent, 3, true);
            DrawArc(c, Radius + 12, 0, Mathf.Tau, 56, UiPalette.Accent with { A = 0.45f }, 2, true);
        }

        if (_tag.Visible)
        {
            DrawLine(new Vector2(c.X, TagBottom), new Vector2(c.X, c.Y - Radius), UiPalette.Text with { A = 0.6f }, 2, true);
        }

        DrawBadge(this, c, Radius, _look, _node.Icon, _hovered);

        // 折角：选中石青、悬停赭石，四角各一笔。
        if (ButtonPressed || _hovered)
        {
            var color = ButtonPressed ? UiPalette.Accent.Darkened(0.15f) : UiPalette.Ochre;
            var e = Radius + (_here ? 16 : 8);
            const float arm = 9;
            foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                var corner = c + new Vector2(sx, sy) * e;
                DrawPolyline([corner - new Vector2(0, sy * arm), corner, corner - new Vector2(sx * arm, 0)], color, 2.6f, true);
            }
        }
    }
}
