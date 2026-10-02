using Godot;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Play;

/// <summary>
/// 队伍页阵位图的一格：地上一圈淡墨站位印记（笔描椭圆 + 晕染），人物站在印记上，名字写在脚下。
/// 悬停或键盘焦点时印记转石青，选中待换位时转朱砂并加重。按钮本身不画底框，命中区只包住印记与名字
/// （人物上半身伸出格外、不接收点击），免得近处一格挡住远处一格的印记。
/// </summary>
public partial class FormationCell : Button
{
    /// <summary>脚下印记中心相对本格左上角的位置。</summary>
    public static readonly Vector2 Feet = new(98, 100);

    public bool Occupied { get; init; }
    public bool Picked { get; init; }
    public float Seed { get; init; }

    public override void _Ready()
    {
        Flat = true;
        FocusMode = FocusModeEnum.All;
        foreach (var style in new[] { "normal", "hover", "pressed", "focus", "disabled", "hover_pressed" })
        {
            AddThemeStyleboxOverride(style, new StyleBoxEmpty());
        }

        MouseEntered += QueueRedraw;
        MouseExited += QueueRedraw;
        FocusEntered += QueueRedraw;
        FocusExited += QueueRedraw;
    }

    public override void _Draw()
    {
        var item = GetCanvasItem();
        var lit = !Disabled && (IsHovered() || HasFocus());
        var (ring, wash, width) = Picked ? (UiPalette.Cinnabar, UiPalette.Cinnabar with { A = 0.26f }, 3.2f)
            : lit ? (UiPalette.Accent, UiPalette.Accent with { A = 0.2f }, 2.6f)
            : Occupied ? (UiPalette.Ochre with { A = 0.7f }, UiPalette.Ochre with { A = 0.14f }, 2.2f)
            : (UiPalette.Ochre with { A = 0.4f }, UiPalette.Ochre with { A = 0.06f }, 1.6f);
        Brushwork.Blot(item, Feet, 52, wash, Seed);

        // 椭圆分两笔描：上弧自左向右、下弧自右向左，起收笔处留一点缺口，像手描的圈。
        const int n = 24;
        var rx = 74f;
        var ry = 24f;
        Vector2 At(float a) => Feet + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        var upper = new Vector2[n + 1];
        var lower = new Vector2[n + 1];
        for (var i = 0; i <= n; i++)
        {
            upper[i] = At(Mathf.Pi + 0.12f + (Mathf.Pi - 0.3f) * i / n);
            lower[i] = At(0.1f + (Mathf.Pi - 0.28f) * i / n);
        }

        Brushwork.Stroke(item, upper, width * 0.8f, ring with { A = ring.A * 0.8f }, Seed, 0.4f, 0.12f, 0.4f);
        Brushwork.Stroke(item, lower, width, ring, Seed + 3.1f, 0.5f, 0.1f, 0.45f);
        if (Picked)
        {
            Brushwork.Dot(item, Feet + new Vector2(rx + 10, -ry - 4), 5, UiPalette.Cinnabar);
        }
    }
}
