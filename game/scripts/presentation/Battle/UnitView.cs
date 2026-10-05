using Godot;
using WuxiaWorld.Domain.Combat;
using Side = WuxiaWorld.Domain.Combat.Side;
using WuxiaWorld.Domain.Combat.Definitions;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Battle;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>头顶标记：一枚圆印，字为标记类别（蓄 / 危 / 闸），可带角标数字。</summary>
public sealed record IntentMark(string Glyph, string? Badge, Color Tone, bool Pulse);

/// <summary>
/// 场上一名单位的画面：战斗形象、头顶一条血条（下接一道细架势线）与其上的标记圆印。
/// 名称、数值、内力、状态与意图的文字说明不常驻，悬停时由战斗页右侧的信息小窗给出。
/// 数值由事件逐条推进（动画播到哪里，界面就显示到哪里）；一批事件播完后再按内核状态校正一次。
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "控件由场景树持有并随之释放。")]
public sealed class UnitView
{
    /// <summary>最远槽位的脚底高度；同排每近一槽下移 SlotDy、向外 SlotDx（设计分辨率 1920×1080，人物群在顶栏与指令区之间居中偏下）。</summary>
    public const float GroundY = 580;
    private const float SlotDx = 100;
    private const float SlotDy = 88;
    private const float MarkSize = 46;

    private const float BarWidth = 112;

    private readonly ProgressBar _hp;
    private readonly ProgressBar? _stance;
    private readonly Control _mark;
    private readonly Panel _markBg;
    private readonly Label _markGlyph;
    private readonly Label _markBadge;
    private readonly List<(string Id, int Stacks)> _statusList = [];
    private Tween? _markPulse;

    /// <summary>已从场上撤下（倒下后淡出完毕或直接隐去）。</summary>
    private bool _gone;

    public UnitView(BattleUnit unit, string name, string? armed = null)
    {
        Id = unit.Id;
        Side = unit.Side;
        Name = name;
        MaxHp = unit.Stats.MaxHp;
        MaxInner = unit.Stats.MaxInner;
        MaxStance = unit.Stats.MaxStance;
        Mechanism = unit.Template.HasTag("mechanism");
        Boss = unit.Template.HasTag("boss");
        Position = unit.Position;

        var scale = 0.86f + unit.Position.Slot * 0.07f;
        var height = (Boss ? 330 : 290) * scale * (Mechanism ? 0.85f : 1);
        var tone = unit.Side == Side.Ally
            ? (unit.Id == "char.hero" ? UiPalette.Accent.Lightened(0.1f) : UiPalette.Trim)
            : (Mechanism ? UiPalette.TextMuted : UiPalette.PanelDark.Lightened(0.15f));
        Standee = new BattleStandee
        {
            Tone = tone, FacingLeft = unit.Side == Side.Enemy, Mechanism = Mechanism, Height = height, Armed = armed,
            // 占位模板没有形象时，剧情人物（char.*）用探索里同一张全身形象（figure.<人物>），没有才画剪影。
            ArtId = unit.Template.ArtId ?? (unit.Id.StartsWith("char.", StringComparison.Ordinal)
                && Art.FigureArt.Find("figure." + unit.Id["char.".Length..]) is not null ? "figure." + unit.Id["char.".Length..] : null),
        };

        // 头顶血条：薄黛底托一条气血，下接一道细架势线（架势是破招、打断蓄力的依据，保留为最细的一条）。
        _hp = Ui.Bar(UiTheme.HealthBar, unit.Hp, MaxHp, BarWidth);
        _hp.CustomMinimumSize = new Vector2(BarWidth, 8);
        var column = Ui.Column(2, _hp);
        if (MaxStance > 0)
        {
            _stance = Ui.Bar(UiTheme.ExpBar, unit.Stance, MaxStance, BarWidth);
            _stance.CustomMinimumSize = new Vector2(BarWidth, 3);
            column.AddChild(_stance);
        }

        Hud = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Pass, MouseDefaultCursorShape = Control.CursorShape.Help };
        Hud.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = UiPalette.Abyss with { A = 0.62f },
            ContentMarginLeft = 2, ContentMarginRight = 2, ContentMarginTop = 2, ContentMarginBottom = 2,
            CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
        });
        Hud.AddChild(Ui.IgnoreMouse(column));

        // 头顶圆印：字居中，右下角标数字（水位等）。
        // 不用容器：容器会把角标也拉满整块，角标需手动放在右下角。
        _mark = new Control
        {
            Visible = false, MouseFilter = Control.MouseFilterEnum.Pass, MouseDefaultCursorShape = Control.CursorShape.Help,
            Size = new Vector2(MarkSize, MarkSize), PivotOffset = new Vector2(MarkSize / 2, MarkSize / 2),
        };
        _markBg = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        _markBg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _mark.AddChild(_markBg);
        _markGlyph = Ui.Text("", UiTheme.DisplayLabel, 26);
        _markGlyph.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _markGlyph.HorizontalAlignment = HorizontalAlignment.Center;
        _markGlyph.VerticalAlignment = VerticalAlignment.Center;
        _markGlyph.AddThemeColorOverride("font_color", UiPalette.TextOnDark);
        _markGlyph.MouseFilter = Control.MouseFilterEnum.Ignore;
        _mark.AddChild(_markGlyph);
        _markBadge = Ui.Text("", UiTheme.GiltLabel, 15);
        _markBadge.AddThemeColorOverride("font_outline_color", UiPalette.Abyss);
        _markBadge.AddThemeConstantOverride("outline_size", 5);
        _markBadge.MouseFilter = Control.MouseFilterEnum.Ignore;
        _markBadge.HorizontalAlignment = HorizontalAlignment.Right;
        Ui.Place(_markBadge, 1, 1, -30, -20, 6, 4);
        _mark.AddChild(_markBadge);
        Intent = _mark;

        Hp = unit.Hp;
        Inner = unit.Inner;
        Stance = unit.Stance;
        foreach (var s in unit.Statuses)
        {
            _statusList.Add((s.StatusId, s.Stacks));
        }

        Refresh();
    }

    public string Id { get; }
    public Side Side { get; }
    public string Name { get; }
    public bool Mechanism { get; }
    public bool Boss { get; }
    public int MaxHp { get; private set; }
    public int MaxInner { get; }
    public int MaxStance { get; }
    public int Hp { get; set; }
    public int Inner { get; set; }
    public int Stance { get; set; }
    public bool Down => Hp <= 0;
    public Position Position { get; set; }

    public BattleStandee Standee { get; }
    public PanelContainer Hud { get; }

    /// <summary>头顶标记圆印。</summary>
    public Control Intent { get; }

    public static Vector2 Feet(Side side, Position p)
    {
        // 侧视斜列：同排槽位越靠前（Slot 越大）越靠下、越向外，形成纵深。M0 展示页脚下有名牌，仍用旧的较紧站位。
        var x = side == Side.Ally ? (p.Row == 0 ? 820 : 480) : (p.Row == 0 ? 1100 : 1440);
        return new Vector2(x + p.Slot * (side == Side.Ally ? -SlotDx : SlotDx), GroundY + p.Slot * SlotDy);
    }

    public Vector2 FeetNow => Feet(Side, Position);

    /// <summary>头顶最上方一件界面（有标记圆印时为圆印上沿，否则为血条上沿）的中点，供目标预估挂在人物头顶。</summary>
    public Vector2 HeadAnchor
    {
        get
        {
            var feet = FeetNow;
            var barY = feet.Y - Standee.Height - (Mechanism ? 2 : 10);
            return new Vector2(feet.X, _mark.Visible ? barY - MarkSize - 4 : barY);
        }
    }

    /// <summary>形象画框。</summary>
    public Rect2 Frame => new(Standee.Position, Standee.Size);

    /// <summary>
    /// 站在本位（不含出手前冲、受击后退等补间位移）时人物实际所占的范围：画框并上当前姿势贴图的外框（刀、篙、甩开的衣袖会伸出画框）。
    /// 悬停小窗按它排开，不会因为小窗恰在人物前冲时弹出而盖住人物本身。
    /// </summary>
    public Rect2 HomeFrame
    {
        get
        {
            var feet = FeetNow;
            var h = Standee.Height;
            var home = new Vector2(feet.X - h * 0.275f, feet.Y - h);
            var frame = new Rect2(home, Standee.Size);
            return Standee.ArtRect is { } art ? frame.Merge(new Rect2(home + art.Position, art.Size)) : frame;
        }
    }

    public void Layout(bool animate, float duration = 0.3f)
    {
        var feet = FeetNow;
        var h = Standee.Height;
        var standee = new Vector2(feet.X - h * 0.275f, feet.Y - h);
        // 血条在头顶上方，标记圆印再在血条之上。
        var barY = feet.Y - h - (Mechanism ? 2 : 10);
        var hud = new Vector2(feet.X - BarWidth / 2 - 2, barY);
        var mark = new Vector2(feet.X - MarkSize / 2, barY - MarkSize - 4);
        if (animate && Motion.Enabled)
        {
            var t = Standee.CreateTween().SetParallel();
            t.TweenProperty(Standee, "position", standee, duration).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
            t.TweenProperty(Hud, "position", hud, duration);
            t.TweenProperty(Intent, "position", mark, duration);
        }
        else
        {
            Standee.Position = standee;
            Hud.Position = hud;
            Intent.Position = mark;
        }
    }

    public void SetStatus(string id, int stacks)
    {
        var i = _statusList.FindIndex(s => s.Id == id);
        if (i >= 0)
        {
            _statusList[i] = (id, stacks);
        }
        else
        {
            _statusList.Add((id, stacks));
        }
    }

    public void RemoveStatus(string id) => _statusList.RemoveAll(s => s.Id == id);

    public bool HasStatus(string id) => _statusList.Exists(s => s.Id == id);

    /// <summary>设置头顶标记；null 表示不显示。危险标记（蓄力、被盯上）呼吸放大。</summary>
    public void ShowMark(IntentMark? mark)
    {
        _markPulse?.Kill();
        _markPulse = null;
        _mark.Scale = Vector2.One;
        if (mark is null || Down)
        {
            _mark.Visible = false;
            return;
        }

        _mark.Visible = true;
        _markGlyph.Text = mark.Glyph;
        _markBadge.Text = mark.Badge ?? "";
        _markBg.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = mark.Tone.Darkened(0.35f) with { A = 0.95f },
            BorderColor = mark.Tone.Lightened(0.35f), BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 23, CornerRadiusTopRight = 23, CornerRadiusBottomLeft = 23, CornerRadiusBottomRight = 23,
            ShadowColor = UiPalette.Abyss with { A = 0.5f }, ShadowSize = 4, AntiAliasing = true,
        });
        if (mark.Pulse && Motion.Enabled)
        {
            _markPulse = _mark.CreateTween().SetLoops();
            _markPulse.TweenProperty(_mark, "scale", new Vector2(1.12f, 1.12f), 0.6f).SetTrans(Tween.TransitionType.Sine);
            _markPulse.TweenProperty(_mark, "scale", Vector2.One, 0.6f).SetTrans(Tween.TransitionType.Sine);
        }
    }

    /// <summary>按内核状态校正（一批事件播完后调用）。</summary>
    public void Sync(BattleUnit unit)
    {
        Hp = unit.Hp;
        Inner = unit.Inner;
        Stance = unit.Stance;
        Position = unit.Position;
        _statusList.Clear();
        foreach (var s in unit.Statuses)
        {
            _statusList.Add((s.StatusId, s.Stacks));
        }
    }

    /// <param name="animate">倒下时是否淡出（事件播放中为真；批次校正与跳过时直接隐去）。</param>
    public void Refresh(bool animate = false)
    {
        _hp.Value = Hp;
        if (_stance is not null)
        {
            // 破绽时架势线整条转朱红，提示此刻打它伤害 +50%。
            var broken = HasStatus(CoreIds.Broken);
            _stance.Value = broken ? MaxStance : Stance;
            _stance.Modulate = broken ? new Color(1.6f, 0.45f, 0.35f) : Colors.White;
        }

        if (Down && !_gone)
        {
            // 倒下者不留在场上：淡出后隐去（形象、状态条、头顶标记一起）。
            _gone = true;
            ShowMark(null);
            if (animate && Motion.Enabled)
            {
                var fade = Standee.CreateTween().SetParallel();
                fade.TweenProperty(Standee, "self_modulate:a", 0f, 0.45f).SetDelay(0.15f);
                fade.TweenProperty(Hud, "modulate:a", 0f, 0.35f).SetDelay(0.15f);
                fade.Chain().TweenCallback(Callable.From(() =>
                {
                    Standee.Visible = false;
                    Hud.Visible = false;
                }));
            }
            else
            {
                Standee.Visible = false;
                Hud.Visible = false;
            }
        }
        else if (!Down && _gone)
        {
            _gone = false;
            Standee.SetPose(null);
            Standee.Visible = Hud.Visible = true;
            Standee.SelfModulate = Colors.White;
            Hud.Modulate = Colors.White;
        }
    }
}
