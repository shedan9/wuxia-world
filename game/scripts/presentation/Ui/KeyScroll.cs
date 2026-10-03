using Godot;

namespace WuxiaWorld.Game.Presentation.Ui;

/// <summary>
/// 可用键盘滚动的滚动区（M3-05）：里面只有文字、没有可聚焦控件时（对话记录、见闻、任务详情），
/// 本身取得焦点，↑↓ 每次滚 64 像素、PgUp / PgDn 不在此处理（留给换子页签）。滚到头再按同方向键不吃掉按键，
/// 焦点照常移到相邻控件，免得困在滚动区里（焦点死路）。取得焦点时四角描泥金折角，与其他控件的焦点框一致。
/// </summary>
public partial class KeyScroll : ScrollContainer
{
    private const float Step = 64;

    public KeyScroll()
    {
        HorizontalScrollMode = ScrollMode.Disabled;
        FocusMode = FocusModeEnum.All;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        FocusEntered += QueueRedraw;
        FocusExited += QueueRedraw;
        // 与按钮一致：鼠标停上即取得焦点，滚轮与方向键作用于同一处。
        MouseEntered += GrabFocus;
    }

    public static KeyScroll Of(Control content)
    {
        var scroll = new KeyScroll();
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(content);
        return scroll;
    }

    /// <summary>滚到最底（对话记录打开时看最新的一句）。</summary>
    public void ScrollToEnd() => Callable.From(() => ScrollVertical = (int)GetVScrollBar().MaxValue).CallDeferred();

    public override void _GuiInput(InputEvent @event)
    {
        var bar = GetVScrollBar();
        var max = bar.MaxValue - bar.Page;
        if (@event.IsActionPressed("ui_down", allowEcho: true) && ScrollVertical < max - 1)
        {
            ScrollVertical = (int)Math.Min(max, ScrollVertical + Step);
            AcceptEvent();
        }
        else if (@event.IsActionPressed("ui_up", allowEcho: true) && ScrollVertical > 0)
        {
            ScrollVertical = (int)Math.Max(0, ScrollVertical - Step);
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        if (!HasFocus())
        {
            return;
        }

        var r = new Rect2(Vector2.Zero, Size).Grow(-2);
        const float arm = 18;
        var c = UiPalette.Gilt;
        foreach (var (corner, dx, dy) in new[]
                 {
                     (r.Position, 1f, 1f), (new Vector2(r.End.X, r.Position.Y), -1f, 1f),
                     (new Vector2(r.Position.X, r.End.Y), 1f, -1f), (r.End, -1f, -1f),
                 })
        {
            DrawLine(corner, corner + new Vector2(arm * dx, 0), c, 2);
            DrawLine(corner, corner + new Vector2(0, arm * dy), c, 2);
        }
    }
}
