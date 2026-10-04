using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.Art;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Battle;

/// <summary>
/// 战斗形象：有 AI 全身形象（<see cref="FigureArt"/>，与探索共用同一张）时贴图，否则画纸影剪影（头、肩、衣摆）；机关有 AI 道具图时贴图，否则画方框。带地面投影。
/// 有战斗关键姿势帧（M3-02，见 <see cref="SetPose"/>）的人物待机画迎敌架势，出手、受击、倒下换对应姿势；没有帧的人物仍是静态站姿。
/// 选中时沿人物轮廓描边（<see cref="Outline"/>，贴图用 figure_outline 着色器，剪影占位直接描线），描边透明度呼吸。
/// </summary>
public partial class BattleStandee : Control
{
    private const float OutlinePx = 3.2f;
    private static readonly Shader OutlineShader = GD.Load<Shader>("res://assets/shaders/figure_outline.gdshader");

    private Color? _outline;
    private ShaderMaterial? _outlineMaterial;
    private double _pulse;

    /// <summary>描边颜色；null 表示不描边。</summary>
    public Color? Outline
    {
        get => _outline;
        set
        {
            if (_outline == value)
            {
                return;
            }

            _outline = value;
            if (value is not null && _outlineMaterial is null)
            {
                _outlineMaterial = new ShaderMaterial { Shader = OutlineShader };
                Material = _outlineMaterial;
            }

            _outlineMaterial?.SetShaderParameter("enabled", value is not null);
            SetProcess(value is not null);
            QueueRedraw();
        }
    }

    public Color Tone { get; init; } = UiPalette.Trim;
    public bool FacingLeft { get; init; }
    public bool Mechanism { get; init; }
    public float Height { get; init; } = 300;

    /// <summary>AI 全身形象 id（与探索共用，见 <see cref="FigureArt"/>）；未入库时画剪影占位。</summary>
    public string? ArtId { get; init; }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(Height * 0.55f, Height);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Ignore;
        PivotOffset = new Vector2(Size.X / 2, Size.Y);
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        SetProcess(_outline is not null);
    }

    public override void _Process(double delta)
    {
        if (_outline is not { } color || _outlineMaterial is null)
        {
            return;
        }

        // 1.2 秒一周期的呼吸，与设计规范“提示呼吸”一致。
        _pulse += delta;
        var a = Motion.Enabled ? 0.6f + 0.4f * (0.5f + 0.5f * Mathf.Sin((float)(_pulse * Math.Tau / 1.2))) : 1f;
        _outlineMaterial.SetShaderParameter("outline_color", color with { A = a });
        if (!HasArt)
        {
            QueueRedraw();
        }
    }

    private bool HasArt => ArtId is not null && FigureArt.Find(ArtId) is not null;

    private string? _pose;
    private int _poseToken;

    /// <summary>
    /// 当前该画的形象（M3-02 战斗关键姿势）：在做的动作（<c>windup</c> 蓄势、<c>strike</c> 出手、<c>hit</c> 受击、<c>down</c> 倒下）
    /// 有对应帧（<c>&lt;形象&gt;.&lt;姿势&gt;</c>）就画它，否则画战斗待机 <c>&lt;形象&gt;.guard</c>，再没有就画全身站姿；缺帧的人物不受影响。
    /// </summary>
    private FigureArt? BodyArt => ArtId is null
        ? null
        : (_pose is { } pose ? FigureArt.Find($"{ArtId}.{pose}") : null) ?? FigureArt.Find($"{ArtId}.guard") ?? FigureArt.Find(ArtId);

    /// <summary>换一个姿势；null 回到待机。</summary>
    public void SetPose(string? pose)
    {
        _poseToken++;
        if (_pose == pose)
        {
            return;
        }

        _pose = pose;
        QueueRedraw();
    }

    /// <summary>做一个姿势并保持 seconds 秒后回待机（期间换了别的姿势则不回）。</summary>
    public void PlayPose(string pose, float seconds)
    {
        SetPose(pose);
        var token = _poseToken;
        GetTree().CreateTimer(seconds).Timeout += () =>
        {
            if (_poseToken == token && IsInstanceValid(this))
            {
                SetPose(null);
            }
        };
    }

    /// <summary>节点局部坐标 point 是否落在人物轮廓内：贴图按不透明像素，剪影按衣身多边形与头部，机关框按矩形。</summary>
    public bool HitTest(Vector2 point)
    {
        var w = Size.X;
        var h = Size.Y;
        var cx = w / 2;
        var facing = FacingLeft ? -1 : 1;
        if (Mechanism && ArtId is not null && FigureArt.Find(ArtId) is { } prop)
        {
            return prop.AlphaAt(point, new Vector2(cx, h - 4), h, facing) > 0.4f;
        }

        if (!Mechanism && BodyArt is { } art)
        {
            return art.AlphaAt(point, new Vector2(cx, h - 6), h * 0.92f, facing) > 0.4f;
        }

        if (Mechanism)
        {
            return new Rect2(cx - w * 0.56f, h * 0.35f - 10, w * 1.12f, h * 0.65f).HasPoint(point);
        }

        var (robe, head, headR) = Silhouette(w, h);
        return Geometry2D.IsPointInPolygon(point, robe) || point.DistanceTo(head) <= headR * 1.25f;
    }

    /// <summary>剪影占位的衣身多边形与头部（绘制与命中判定共用）。</summary>
    private (Vector2[] Robe, Vector2 Head, float HeadR) Silhouette(float w, float h)
    {
        var cx = w / 2;
        var dir = FacingLeft ? -1 : 1;
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
        return (robe, new Vector2(cx + dir * w * 0.04f, h * 0.14f), h * 0.075f);
    }

    /// <summary>描边宽度换算到贴图像素：贴图按 drawnHeight / stature 缩放。</summary>
    private void SetOutlineWidth(FigureArt art, float drawnHeight)
    {
        _outlineMaterial?.SetShaderParameter("width_texels", OutlinePx * art.Stature / drawnHeight);
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

        if (Mechanism && ArtId is not null && FigureArt.Find(ArtId) is { } prop)
        {
            // 机关道具（place.py --prop）：底边落地、整高即立像高度，压暗一阶融进黄昏。
            SetOutlineWidth(prop, h);
            prop.Draw(this, new Vector2(cx, h - 4), h, FacingLeft ? -1 : 1, modulate: new Color(0.8f, 0.8f, 0.86f));
            return;
        }

        if (!Mechanism && BodyArt is { } art)
        {
            // 头顶到脚底为 Height 的 92%：留出发髻、兵刃高出头顶的余量，与剪影占位的头顶位置相当。
            SetOutlineWidth(art, h * 0.92f);
            // 受击帧再绕脚底后仰一点（模型画不出大幅后仰），与闪白、抖动一起读成“被打退”。
            art.Draw(this, new Vector2(cx, h - 6), h * 0.92f, FacingLeft ? -1 : 1, _pose == "hit" && art != FigureArt.Find(ArtId!) ? -0.07f : 0);
            return;
        }

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
            if (_outline is { } picked)
            {
                DrawRect(new Rect2(cx - w * 0.6f, top - 14, w * 1.2f, h - top + 10), picked, false, OutlinePx);
            }

            return;
        }

        var dir = FacingLeft ? -1 : 1;
        var (robe, head, headR) = Silhouette(w, h);
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
        if (_outline is { } selected)
        {
            // 剪影占位没有贴图可供着色器描边，直接沿衣身与头部外扩描线。
            var a = _outlineMaterial?.GetShaderParameter("outline_color").AsColor().A ?? 1f;
            var line = selected with { A = a };
            var grown = new Vector2[outline.Length];
            var center = new Vector2(cx, h * 0.6f);
            for (var i = 0; i < outline.Length; i++)
            {
                grown[i] = outline[i] + (outline[i] - center).Normalized() * OutlinePx;
            }

            DrawPolyline(grown, line, OutlinePx, true);
            DrawArc(head, headR + OutlinePx, 0, Mathf.Tau, 32, line, OutlinePx, true);
        }
    }
}
