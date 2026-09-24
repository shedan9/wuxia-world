using Godot;

namespace WuxiaWorld.Game.Presentation.Scenery;

/// <summary>
/// 青绿山水程序化背景（assets/shaders/landscape.gdshader）加飘落柳叶。
/// <see cref="Clear"/> 用于主菜单；<see cref="Veiled"/> 为菜单与暂停界面的虚化暗底。
/// 鼠标移动带动轻微视差；开启“减少动效”后应把 <see cref="Motion"/> 设为 false（设置页接入后）。
/// </summary>
public partial class Backdrop : Control
{
    private static readonly Shader LandscapeShader = GD.Load<Shader>("res://assets/shaders/landscape.gdshader");

    private readonly ShaderMaterial _material = new() { Shader = LandscapeShader };
    private Vector2 _parallax;

    public float Defocus { get; init; }
    public float Veil { get; init; }
    public float LeftWash { get; init; }
    public int Leaves { get; init; } = 28;
    public bool Motion { get; init; } = true;
    /// <summary>日轮横向位置（0–1），存档缩略图用来区分不同地点。</summary>
    public float SunX { get; init; } = 0.74f;
    /// <summary>山形种子，不同值得到不同山势。</summary>
    public float Seed { get; init; }

    public static Backdrop Clear() => new() { LeftWash = 0.55f };

    public static Backdrop Veiled() => new() { Defocus = 1, Veil = 0.72f, Leaves = 10 };

    /// <summary>静止小图：存档卡缩略图等，不飘叶、不随鼠标。</summary>
    public static Backdrop Still(float sunX, float seed) => new() { Leaves = 0, Motion = false, SunX = sunX, Seed = seed };

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var canvas = new ColorRect { Material = _material, MouseFilter = MouseFilterEnum.Ignore };
        canvas.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(canvas);

        _material.SetShaderParameter("defocus", Defocus);
        _material.SetShaderParameter("veil", Veil);
        _material.SetShaderParameter("left_wash", LeftWash);
        _material.SetShaderParameter("speed", Motion ? 1f : 0f);
        _material.SetShaderParameter("sun_pos", new Vector2(SunX, 0.2f));
        _material.SetShaderParameter("world_seed", Seed);

        if (Leaves > 0 && Motion)
        {
            AddChild(LeafFall(Leaves, Defocus > 0.5f ? 0.35f : 0.85f));
        }

        Resized += UpdateAspect;
        UpdateAspect();
    }

    public override void _Process(double delta)
    {
        if (!Motion || Size.X <= 0)
        {
            return;
        }

        var mouse = GetLocalMousePosition() / Size - new Vector2(0.5f, 0.5f);
        var target = -mouse.Clamp(new Vector2(-0.5f, -0.5f), new Vector2(0.5f, 0.5f)) * 0.03f;
        _parallax = _parallax.Lerp(target, (float)Mathf.Min(1, delta * 2.5));
        _material.SetShaderParameter("parallax", _parallax);
    }

    private void UpdateAspect()
    {
        if (Size.Y > 0)
        {
            _material.SetShaderParameter("aspect", Size.X / Size.Y);
        }
    }

    private static CpuParticles2D LeafFall(int amount, float alpha)
    {
        var particles = new CpuParticles2D
        {
            Amount = amount,
            Lifetime = 16,
            Preprocess = 16,
            Texture = LeafTexture(),
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle,
            EmissionRectExtents = new Vector2(1200, 10),
            Position = new Vector2(900, -30),
            Direction = new Vector2(-0.35f, 1),
            Spread = 18,
            Gravity = new Vector2(0, 6),
            InitialVelocityMin = 38,
            InitialVelocityMax = 70,
            AngularVelocityMin = -40,
            AngularVelocityMax = 40,
            AngleMin = 0,
            AngleMax = 360,
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.25f,
            Color = new Color(0.86f, 0.95f, 0.90f, alpha),
            ColorInitialRamp = new Gradient
            {
                Offsets = [0, 0.5f, 1],
                Colors = [UiPalette.Surface, UiPalette.Trim.Lightened(0.35f), UiPalette.Trim],
            },
        };
        // 摆动：切向加速度正负交替，让叶子左右飘。
        particles.TangentialAccelMin = -12;
        particles.TangentialAccelMax = 12;
        return particles;
    }

    /// <summary>柳叶：两端尖的细长叶形，运行时生成。</summary>
    private static ImageTexture LeafTexture()
    {
        const int w = 28;
        const int h = 10;
        var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var u = (x + 0.5f) / w * 2 - 1;
                var half = (1 - u * u) * (h / 2f - 0.5f);
                var dy = Mathf.Abs(y + 0.5f - h / 2f);
                var a = Mathf.Clamp(half - dy + 0.5f, 0, 1);
                var vein = dy < 0.6f ? 0.85f : 1f;
                image.SetPixel(x, y, new Color(vein, vein, vein, a));
            }
        }

        return ImageTexture.CreateFromImage(image);
    }
}
