using Godot;

namespace WuxiaWorld.Game.Presentation.Battle;

/// <summary>打击特效的形状（按招式标签选，见 <see cref="HitFx.KindOf"/>）。</summary>
public enum HitFxKind
{
    /// <summary>剑、刀、钩：一道斜劈的月牙刃光。</summary>
    Slash,

    /// <summary>刺击（带 pierce 标签）：顺出手方向一道收尖的直光，尖端一圈小环。</summary>
    Thrust,

    /// <summary>棍、篙：一道压扁的宽弧横扫。</summary>
    Sweep,

    /// <summary>拳掌与基本招式：中心一闪、一圈冲击环与放射的劲线。</summary>
    Impact,

    /// <summary>内劲伤害：贴地压扁的几圈气环向外漾开，带回旋的气线。</summary>
    Qi,

    /// <summary>水势（水门放水、首领借水）：水花向上四溅后落下。</summary>
    Splash,
}

/// <summary>
/// 战斗受击处的一次打击特效（M3-02 动作补齐）：赛璐璐式的硬边形状——白色亮芯外包一层半透明色晕，两阶明暗、不做柔光粒子，
/// 与人物的线稿上色一致；色调取界面青绿配色（刃光湖蓝、拳掌泥金、内劲石青、水花湖蓝）。
/// 由 <see cref="Play"/> 按倍速播放约 0.4 秒后自行释放。坐标原点为受击人物的身躯中心。
/// </summary>
public sealed partial class HitFx : Control
{
    private static readonly Color BladeGlow = Color.FromHtml("#7FD0E6");
    private static readonly Color Core = Color.FromHtml("#FBFDF8");
    private static readonly Color BluntGlow = Color.FromHtml("#E8C77E");
    private static readonly Color QiGlow = Color.FromHtml("#3FA3A0");
    private static readonly Color WaterGlow = Color.FromHtml("#5BB6D6");

    private HitFxKind _kind;
    private float _facing = 1;
    private float _size = 1;
    private float _tilt;
    private float[] _jitter = [];
    private float _t;

    /// <summary>按招式标签与伤害类别选特效形状。</summary>
    public static HitFxKind KindOf(IReadOnlyList<string> tags, bool internalDamage)
    {
        bool Has(string tag) => tags.Contains(tag, StringComparer.Ordinal);
        if (Has("water"))
        {
            return HitFxKind.Splash;
        }

        if (internalDamage || Has("inner"))
        {
            return HitFxKind.Qi;
        }

        if (Has("pierce"))
        {
            return HitFxKind.Thrust;
        }

        if (Has("sword") || Has("blade") || Has("hook"))
        {
            return HitFxKind.Slash;
        }

        return Has("staff") ? HitFxKind.Sweep : HitFxKind.Impact;
    }

    /// <summary>
    /// 在 layer 上 center 处播一次特效。facing 为出手方向（+1 往右打、−1 往左打），scale 为人物高度比（暴击另放大）。
    /// 特效放在该层最底下，不压住悬停小窗与飘字。
    /// </summary>
    public static void Play(Control layer, Vector2 center, HitFxKind kind, float facing, float scale, float speed, int seed)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };
        var fx = new HitFx
        {
            _kind = kind, _facing = facing < 0 ? -1 : 1, _size = scale,
            _tilt = rng.RandfRange(-0.25f, 0.25f),
            _jitter = [.. Enumerable.Range(0, 16).Select(_ => rng.Randf())],
            Position = center, MouseFilter = MouseFilterEnum.Ignore,
        };
        layer.AddChild(fx);
        layer.MoveChild(fx, 0);
        var life = kind switch { HitFxKind.Splash => 0.55f, HitFxKind.Qi => 0.5f, _ => 0.36f };
        var tween = fx.CreateTween();
        tween.TweenMethod(Callable.From<float>(t =>
        {
            fx._t = t;
            fx.QueueRedraw();
        }), 0f, 1f, life / speed);
        tween.TweenCallback(Callable.From(fx.QueueFree));
    }

    public override void _Draw()
    {
        switch (_kind)
        {
            case HitFxKind.Slash:
                DrawSlash();
                break;
            case HitFxKind.Thrust:
                DrawThrust();
                break;
            case HitFxKind.Sweep:
                DrawSweep();
                break;
            case HitFxKind.Impact:
                DrawImpact();
                break;
            case HitFxKind.Qi:
                DrawQi();
                break;
            case HitFxKind.Splash:
                DrawSplash();
                break;
        }
    }

    /// <summary>淡出：前 55% 不透明，之后线性消失。</summary>
    private float Fade => _t < 0.55f ? 1 : 1 - (_t - 0.55f) / 0.45f;

    /// <summary>
    /// 沿一段弧画收尖的刃带：头部在前 45% 时间内扫过整段，尾部随后追上（刃光由粗到细地收掉）。
    /// rx / ry 为弧的两个半径（ry &lt; rx 时压扁），width 为最粗处宽度。
    /// </summary>
    private void Crescent(float rx, float ry, float from, float to, float width, Color glow, float rotation)
    {
        var head = Mathf.Clamp(_t / 0.45f, 0, 1);
        var tail = Mathf.Clamp((_t - 0.2f) / 0.8f, 0, 1);
        head = 1 - (1 - head) * (1 - head);
        if (head <= tail)
        {
            return;
        }

        const int Steps = 28;
        var outer = new Vector2[Steps + 1];
        var inner = new Vector2[Steps + 1];
        var xf = Transform2D.Identity.Rotated(rotation).Scaled(new Vector2(_facing, 1));
        for (var i = 0; i <= Steps; i++)
        {
            var u = tail + (head - tail) * i / Steps;
            var a = Mathf.Lerp(from, to, u);
            // 宽度在刃带中段最粗、两头收尖，整体随淡出变细。
            var w = width * Mathf.Sin(Mathf.Pi * i / Steps) * (0.4f + 0.6f * Fade);
            var dir = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
            var normal = dir.Normalized();
            outer[i] = xf * (dir + normal * w * 0.5f);
            inner[i] = xf * (dir - normal * w * 0.5f);
        }

        Band(outer, inner, glow with { A = 0.5f * Fade });
        // 亮芯：贴近外缘的一条更细的带。
        var coreOuter = new Vector2[Steps + 1];
        var coreInner = new Vector2[Steps + 1];
        for (var i = 0; i <= Steps; i++)
        {
            coreOuter[i] = outer[i];
            coreInner[i] = outer[i].Lerp(inner[i], 0.45f);
        }

        Band(coreOuter, coreInner, Core with { A = Fade });
    }

    private void Band(Vector2[] outer, Vector2[] inner, Color color)
    {
        var polygon = new Vector2[outer.Length * 2];
        for (var i = 0; i < outer.Length; i++)
        {
            polygon[i] = outer[i];
            polygon[polygon.Length - 1 - i] = inner[i];
        }

        if (Geometry2D.TriangulatePolygon(polygon).Length > 0)
        {
            DrawColoredPolygon(polygon, color);
        }
    }

    /// <summary>收尖的线段（劲线、气线）：p0 处宽 w0，p1 处收成尖。</summary>
    private void Spike(Vector2 p0, Vector2 p1, float w0, Color color)
    {
        var n = (p1 - p0).Orthogonal().Normalized() * w0 * 0.5f;
        DrawColoredPolygon([p0 + n, p1, p0 - n], color);
    }

    private void DrawSlash()
    {
        var r = 120 * _size;
        // 从对手的上前方斜劈到下后方：弧心偏到出手者一侧，刃光的凸面朝向出手方向。
        Crescent(r, r * 0.8f, -2.1f, 0.5f, 26 * _size, BladeGlow, -0.35f + _tilt);
        // 刃尖迸出的几点碎光。
        if (_t is > 0.15f and < 0.7f)
        {
            var a = Fade;
            for (var i = 0; i < 4; i++)
            {
                var angle = -0.3f + _jitter[i] * 0.9f;
                var from = new Vector2(Mathf.Cos(angle) * _facing, Mathf.Sin(angle)) * r * (0.5f + _t);
                Spike(from, from + from.Normalized() * 34 * _size * (0.6f + _jitter[i + 4]), 5 * _size, Core with { A = a });
            }
        }
    }

    private void DrawThrust()
    {
        var length = 300 * _size;
        var head = Mathf.Clamp(_t / 0.35f, 0, 1);
        var tail = Mathf.Clamp((_t - 0.15f) / 0.85f, 0, 1);
        var back = new Vector2(-_facing * length * 0.65f, 0).Rotated(_tilt * 0.4f);
        var tip = back + new Vector2(_facing * length, 0).Rotated(_tilt * 0.4f);
        var p1 = back.Lerp(tip, head);
        var p0 = back.Lerp(tip, tail);
        if (p0.DistanceTo(p1) > 2)
        {
            var w = 22 * _size * (0.4f + 0.6f * Fade);
            DrawColoredPolygon(Taper(p0, p1, w * 2.2f), BladeGlow with { A = 0.45f * Fade });
            DrawColoredPolygon(Taper(p0, p1, w), Core with { A = Fade });
        }

        if (head >= 1)
        {
            var ring = Mathf.Clamp((_t - 0.35f) / 0.65f, 0, 1);
            DrawArc(tip, (14 + 60 * ring) * _size, 0, Mathf.Tau, 40, BladeGlow with { A = (1 - ring) * 0.9f }, (8 - 6 * ring) * _size, true);
        }
    }

    /// <summary>尾细头粗的梭形：在 p1 处最粗、p0 处收尖，前端略收圆。</summary>
    private static Vector2[] Taper(Vector2 p0, Vector2 p1, float width)
    {
        var d = p1 - p0;
        var n = d.Orthogonal().Normalized() * width * 0.5f;
        var u = d.Normalized();
        return [p0, p0.Lerp(p1, 0.8f) + n, p1 + u * width * 0.35f, p0.Lerp(p1, 0.8f) - n];
    }

    private void DrawSweep()
    {
        var r = 150 * _size;
        Crescent(r, r * 0.42f, -2.6f, 0.9f, 30 * _size, BluntGlow, _tilt * 0.5f + 0.15f);
        // 扫中处一圈短促的冲击环。
        var ring = Mathf.Clamp((_t - 0.2f) / 0.5f, 0, 1);
        if (ring is > 0 and < 1)
        {
            DrawArc(Vector2.Zero, (20 + 70 * ring) * _size, 0, Mathf.Tau, 40, Core with { A = (1 - ring) * 0.9f }, (7 - 5 * ring) * _size, true);
        }
    }

    private void DrawImpact()
    {
        var grow = 1 - (1 - _t) * (1 - _t);
        // 中心一闪：前 25% 时间由大到小的亮斑。
        if (_t < 0.25f)
        {
            var k = 1 - _t / 0.25f;
            DrawCircle(Vector2.Zero, 46 * _size * (0.6f + 0.4f * k), BluntGlow with { A = 0.55f * k });
            DrawCircle(Vector2.Zero, 28 * _size * (0.6f + 0.4f * k), Core with { A = k });
        }

        DrawArc(Vector2.Zero, (24 + 96 * grow) * _size, 0, Mathf.Tau, 48, BluntGlow with { A = 0.7f * Fade }, (16 - 12 * grow) * _size, true);
        DrawArc(Vector2.Zero, (24 + 96 * grow) * _size, 0, Mathf.Tau, 48, Core with { A = Fade }, (6 - 4 * grow) * _size, true);
        // 放射劲线：从内圈向外飞出、整体朝出手方向偏。
        for (var i = 0; i < 9; i++)
        {
            var angle = Mathf.Tau * i / 9 + _jitter[i] * 0.4f;
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.8f);
            dir = (dir + new Vector2(_facing * 0.35f, 0)).Normalized();
            var inner = (30 + 70 * grow) * _size;
            var len = (40 + 50 * _jitter[i + 6]) * _size * (1 - 0.5f * grow);
            Spike(dir * inner, dir * (inner + len), 9 * _size, Core with { A = Fade });
        }
    }

    private void DrawQi()
    {
        // 几圈压扁的气环先后漾开（贴着身躯中心偏下，读作内劲透体）。
        for (var k = 0; k < 3; k++)
        {
            var local = Mathf.Clamp((_t - k * 0.14f) / 0.7f, 0, 1);
            if (local is <= 0 or >= 1)
            {
                continue;
            }

            var r = (30 + 120 * local) * _size;
            var a = (1 - local) * 0.85f;
            DrawSetTransform(new Vector2(0, 30 * _size), 0, new Vector2(1, 0.45f));
            DrawArc(Vector2.Zero, r, 0, Mathf.Tau, 56, QiGlow with { A = a * 0.7f }, 14 * _size * (1 - local) + 2, true);
            DrawArc(Vector2.Zero, r, 0, Mathf.Tau, 56, Core with { A = a }, 4 * _size * (1 - local) + 1, true);
        }

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        // 回旋气线：绕身躯中心旋进的几道弧。
        for (var i = 0; i < 5; i++)
        {
            var start = Mathf.Tau * i / 5 + _t * 4 * _facing + _jitter[i];
            var r = (90 - 50 * _t) * _size;
            DrawArc(Vector2.Zero, r, start, start + 0.9f, 12, QiGlow with { A = 0.9f * Fade }, 6 * _size, true);
            DrawArc(Vector2.Zero, r, start + 0.2f, start + 0.7f, 8, Core with { A = Fade }, 2.5f * _size, true);
        }
    }

    private void DrawSplash()
    {
        // 十二颗水珠先向上、向两侧溅开，再按重力落下；每颗是一个朝运动方向拉长的水滴。
        for (var i = 0; i < 12; i++)
        {
            var spread = (i / 11f - 0.5f) * 2.4f + (_jitter[i] - 0.5f) * 0.3f;
            var v = new Vector2(Mathf.Sin(spread) * 420, -Mathf.Cos(spread) * (520 + 220 * _jitter[(i + 3) % 16])) * _size;
            var time = _t * 0.55f;
            var g = 1800 * _size;
            var p = new Vector2(v.X * time, v.Y * time + 0.5f * g * time * time) + new Vector2(0, 40 * _size);
            var vel = new Vector2(v.X, v.Y + g * time).Normalized();
            var r = (7 + 6 * _jitter[(i + 7) % 16]) * _size * (1 - 0.4f * _t);
            DrawCircle(p, r * 1.6f, WaterGlow with { A = 0.5f * Fade });
            DrawColoredPolygon([p + vel.Orthogonal() * r, p - vel * r * 3, p - vel.Orthogonal() * r, p + vel * r], Core with { A = Fade });
        }

        var ring = Mathf.Clamp(_t / 0.5f, 0, 1);
        if (ring < 1)
        {
            DrawSetTransform(new Vector2(0, 60 * _size), 0, new Vector2(1, 0.35f));
            DrawArc(Vector2.Zero, (40 + 130 * ring) * _size, 0, Mathf.Tau, 48, WaterGlow with { A = (1 - ring) * 0.8f }, (12 - 9 * ring) * _size, true);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }
}
