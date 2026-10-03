using Godot;
using WuxiaWorld.Game.Presentation.Play;

namespace WuxiaWorld.Game.Presentation.App;

/// <summary>
/// 焦点与版式走查（<c>--focus-audit</c>，开发用；开发计划 6.1 T10“字体放大、键盘操作：无关键文字遮挡或焦点死路”）。
/// 一局停稳后依次打开暂停页与分区菜单的每个分区、每个子页签，逐页检查：
/// ① 打开时有焦点、且落在可见控件上；② 从初始焦点出发，按 ↑↓←→ 与 Tab / Shift+Tab 的引擎焦点导航，
/// 能走到页上每一个可见、可聚焦的控件（走不到的即“焦点孤岛”）；③ 没有出不去的死路（四向与 Tab 都回到自己）；
/// ④ 可见控件不越出画面（滚动区里的除外），也不被外层裁切容器剪掉。配合 <c>--text-size=32</c> 在最大字号下再走一遍。
/// 结果打印到标准输出，有问题时退出码为 4。
/// </summary>
public static class FocusAudit
{
    private static readonly List<string> Problems = [];

    public static async void Run(Node host)
    {
        var tree = host.GetTree();
        async Task Frames(int n)
        {
            for (var i = 0; i < n; i++)
            {
                await host.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
        }

        var menu = AppHost.Instance.Menu;
        GD.Print($"[focus] 开始：正文字号 {GameSettings.TextSize}，缩放 {Ui.FontScale.Factor:0.00}");

        menu.Open();
        await Frames(6);
        Check("暂停页", menu.GetChild<Control>(0), menu.GetChild<Control>(0));
        menu.Close();
        await Frames(3);

        var tabs = new Dictionary<MenuSection, int>
        {
            [MenuSection.Character] = 3,
            [MenuSection.Party] = 1,
            [MenuSection.Inventory] = 1,
            [MenuSection.Journal] = 5,
            [MenuSection.Settings] = 5,
        };
        foreach (var (section, count) in tabs)
        {
            for (var tab = 0; tab < count; tab++)
            {
                menu.OpenSection(section, tab);
                await Frames(6);
                if (menu.Frame?.Page is { } page)
                {
                    Check($"{section}/{tab}", page, menu.Frame.Root);
                }
                else
                {
                    Problems.Add($"{section}/{tab}：分区没有打开");
                }

                menu.Close();
                await Frames(3);
            }
        }

        foreach (var p in Problems)
        {
            GD.Print("[focus] 问题：" + p);
        }

        GD.Print($"[focus] 完成：问题 {Problems.Count} 处");
        tree.Quit(Problems.Count == 0 ? 0 : 4);
    }

    /// <param name="root">检查焦点的页面。</param>
    /// <param name="layout">检查越界的范围（含外框顶栏、底栏）。</param>
    private static void Check(string name, Control root, Control layout)
    {
        var viewport = root.GetViewport();
        var screen = viewport.GetVisibleRect();
        var all = new List<Control>();
        Collect(root, all);
        var shown = new List<Control>();
        Collect(layout, shown);
        var focusable = all.Where(c => c.FocusMode == Control.FocusModeEnum.All && c is not BaseButton { Disabled: true }).ToList();

        var start = viewport.GuiGetFocusOwner();
        if (start is null || !root.IsAncestorOf(start) && start != root)
        {
            Problems.Add($"{name}：打开时没有焦点（或焦点不在本页，当前 {start?.Name ?? "无"}）");
            start = focusable.FirstOrDefault();
        }

        var reached = new HashSet<Control>();
        if (start is not null)
        {
            var queue = new Queue<Control>([start]);
            reached.Add(start);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                var next = new[]
                {
                    c.FindValidFocusNeighbor(Side.Top), c.FindValidFocusNeighbor(Side.Bottom),
                    c.FindValidFocusNeighbor(Side.Left), c.FindValidFocusNeighbor(Side.Right),
                    c.FindNextValidFocus(), c.FindPrevValidFocus(),
                };
                if (next.All(n => n is null || n == c) && focusable.Count > 1)
                {
                    Problems.Add($"{name}：{Describe(c)} 是焦点死路（四向与 Tab 都离不开）");
                }

                foreach (var n in next)
                {
                    if (n is not null && root.IsAncestorOf(n) && reached.Add(n))
                    {
                        queue.Enqueue(n);
                    }
                }
            }
        }

        var missed = focusable.Where(c => !reached.Contains(c)).ToList();
        if (missed.Count > 0)
        {
            Problems.Add($"{name}：键盘走不到 {missed.Count} 个控件：{string.Join("、", missed.Take(5).Select(Describe))}");
        }

        foreach (var c in shown.Where(c => c is Label or Button))
        {
            // 滚动区里的控件可以在竖向滚出视野，只查是否横向越出滚动区（横向不滚动，越出即被裁掉）。
            var r = c.GetGlobalRect();
            var outside = Scroller(c, layout) is { } scroll
                ? r.Position.X < scroll.GetGlobalRect().Position.X - 1 || r.End.X > scroll.GetGlobalRect().End.X + 1
                : !screen.Grow(1).Encloses(r);
            if (r.Size.X > 0 && r.Size.Y > 0 && outside)
            {
                Problems.Add($"{name}：{Describe(c)} 越出画面（{r.Position.X:0},{r.Position.Y:0} {r.Size.X:0}×{r.Size.Y:0}）");
            }

            // 还在画面里、却被外层裁切的容器（如分区外框的内容区）剪掉：字号放大后页面比外框高时就是这样，只看画面边界查不出。
            else if (r.Size.X > 0 && r.Size.Y > 0 && Clipper(c) is { } clip && !clip.GetGlobalRect().Grow(1).Encloses(r))
            {
                Problems.Add($"{name}：{Describe(c)} 被 {clip.Name} 裁掉（{r.Position.X:0},{r.Position.Y:0} {r.Size.X:0}×{r.Size.Y:0}）");
            }
        }

        GD.Print($"[focus] {name}：可聚焦 {focusable.Count}，键盘可达 {reached.Count(focusable.Contains)}，初始焦点 {(start is null ? "无" : Describe(start))}");
    }

    private static void Collect(Node node, List<Control> into)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Control { Visible: true } c)
            {
                into.Add(c);
                Collect(c, into);
            }
        }
    }

    /// <summary>最近的外层滚动区；没有时为 null。</summary>
    private static ScrollContainer? Scroller(Control c, Control root)
    {
        for (var p = c.GetParent(); p is not null && p != root; p = p.GetParent())
        {
            if (p is ScrollContainer s)
            {
                return s;
            }
        }

        return null;
    }

    /// <summary>最近的外层裁切容器（<see cref="CanvasItem.ClipContents"/>，滚动区另算）；没有时为 null。</summary>
    private static Control? Clipper(Control c)
    {
        for (var p = c.GetParent(); p is not null; p = p.GetParent())
        {
            if (p is ScrollContainer)
            {
                return null;
            }

            if (p is Control { ClipContents: true } clip)
            {
                return clip;
            }
        }

        return null;
    }

    private static string Describe(Control c) => c switch
    {
        Button { Text: { Length: > 0 } t } => $"按钮“{t}”",
        Label { Text: { Length: > 0 } t } => $"文字“{(t.Length > 12 ? t[..12] + "…" : t)}”",
        HSlider => "滑杆",
        ScrollContainer => "滚动区",
        _ => $"{c.GetType().Name}",
    };
}
