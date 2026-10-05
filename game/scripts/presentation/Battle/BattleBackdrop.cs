using System.Text.Json;
using Godot;
using WuxiaWorld.Game.Presentation.Scenery;
using WuxiaWorld.Game.Presentation.Art;

namespace WuxiaWorld.Game.Presentation.Battle;

/// <summary>
/// 侧视战斗布景（M0-04 旧渡水门；M3 第十五批起按遭遇换布景，如客栈后院夜景）：远景为整张 AI 布景（<c>assets/art/battle/&lt;id&gt;.png</c>，tools/ArtGen/backdrop.py 生成，
/// json 记远岸水线 waterline），按水线对齐到 <see cref="WaterlineY"/>、按宽度铺满；水线以下留一窄条远景水面，
/// 其下 <see cref="EdgeY"/> 起为码头石板近景，由 battle_ground 着色器按透视铺 AI 石板纹理（与城镇探索同一张）。
/// 版式坐标为 1920×1080 设计像素；两军脚底在 540–684，都落在近景地面上。未入库时退回程序化黄昏山水。
/// </summary>
public partial class BattleBackdrop : Control
{
    /// <summary>远景远岸水线在画面上的高度。</summary>
    public const float WaterlineY = 430;

    /// <summary>码头远端压边的顶线：其上是远景水面，其下是近景石板。</summary>
    public const float EdgeY = 505;

    private static readonly Shader GroundShader = GD.Load<Shader>("res://assets/shaders/battle_ground.gdshader");

    public required string ArtId { get; init; }

    private TextureRect? _far;
    private float _waterline;
    private ColorRect? _ground;
    private ShaderMaterial? _groundMaterial;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var basePath = $"res://assets/art/battle/{ArtId}";
        if (!PieceArt.Enabled || !ResourceLoader.Exists($"{basePath}.png") || !Godot.FileAccess.FileExists($"{basePath}.json"))
        {
            AddChild(new Backdrop { Mood = 1, SunX = 0.8f, Defocus = 0.3f, Veil = 0.08f, Leaves = 14 });
            return;
        }

        // 布景 json 可带 ground：近景地面的纹理与光色（缺省为旧渡黄昏的码头石板），见架构文档 9.4.17。
        var groundTexture = "town.ground.flagstone";
        var tints = new Dictionary<string, Color>();
        float? glowX = null;
        var leaves = true;
        using (var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString($"{basePath}.json")))
        {
            _waterline = doc.RootElement.GetProperty("waterline").GetSingle();
            if (doc.RootElement.TryGetProperty("ground", out var g))
            {
                groundTexture = g.TryGetProperty("texture", out var t) ? t.GetString()! : groundTexture;
                foreach (var key in new[] { "far_tint", "near_tint", "sky_glow" })
                {
                    if (g.TryGetProperty(key, out var c))
                    {
                        tints[key] = new Color(c[0].GetSingle(), c[1].GetSingle(), c[2].GetSingle());
                    }
                }

                glowX = g.TryGetProperty("sun_x", out var x) ? x.GetSingle() : null;
                leaves = !g.TryGetProperty("leaves", out var l) || l.GetBoolean();
            }
        }

        if (PieceArt.FindTexture(groundTexture) is not { } flagstone)
        {
            AddChild(new Backdrop { Mood = 1, SunX = 0.8f, Defocus = 0.3f, Veil = 0.08f, Leaves = 14 });
            return;
        }

        // 远景略压暗、收一点饱和，让前景人物立得住。
        _far = new TextureRect
        {
            Texture = GD.Load<Texture2D>($"{basePath}.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
            SelfModulate = new Color(0.9f, 0.9f, 0.94f),
        };
        AddChild(_far);

        _groundMaterial = new ShaderMaterial { Shader = GroundShader };
        _groundMaterial.SetShaderParameter("ground_tex", flagstone.Texture);
        _groundMaterial.SetShaderParameter("tex_world", flagstone.WorldSize);
        foreach (var (key, color) in tints)
        {
            _groundMaterial.SetShaderParameter(key, new Vector3(color.R, color.G, color.B));
        }

        if (glowX is { } sx)
        {
            _groundMaterial.SetShaderParameter("sun_x", sx);
        }

        _ground = new ColorRect { Material = _groundMaterial, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_ground);

        if (leaves)
        {
            AddChild(Backdrop.LeafFall(12, 0.85f, 1));
        }

        Resized += Layout;
        Layout();
    }

    private void Layout()
    {
        if (_far is null || _ground is null || _groundMaterial is null || Size.X <= 0)
        {
            return;
        }

        // 宽屏（21:9）时按宽度放大，水线仍对齐。
        var texSize = _far.Texture.GetSize();
        var k = Mathf.Max(Size.X / texSize.X, 1);
        _far.Position = new Vector2((Size.X - texSize.X * k) / 2, WaterlineY - _waterline * k);
        _far.Size = texSize * k;

        _ground.Position = new Vector2(0, EdgeY);
        _ground.Size = new Vector2(Size.X, Mathf.Max(1, Size.Y - EdgeY));
        _groundMaterial.SetShaderParameter("rect_origin", _ground.Position);
        _groundMaterial.SetShaderParameter("rect_size", _ground.Size);
        _groundMaterial.SetShaderParameter("center_x", Size.X / 2);
    }
}
