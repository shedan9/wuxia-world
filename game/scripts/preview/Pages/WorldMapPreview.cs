using Godot;
using WuxiaWorld.Game.Presentation;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 江湖大地图展示页，版式见 docs/art/UI_DESIGN.md 第 5.6 节：全屏绢本青绿舆图（程序化占位），
/// 圆章图标地标（已到访实色、未到访淡色、未开放灰淡并写明开放条件，有事件挂泥金菱形，悬停才显示地名印），
/// 右侧黛本交通面板（目的地、路线、方式、同行者、到达后可见事件、启程；点地图空白处或 Esc 关闭），左上所在与时辰，左下图例。
/// 滚轮缩放（最小可见全图）、拖动平移，Tab 在地标间跳选，1–3 换方式，Enter 启程播放一段行进预览。
/// 舆图在进入页面时一次性烘焙成带多级纹理的贴图，缩放平移只移动这张贴图，不再逐帧重算地形与矢量山峦。
/// 行进预览只在本页推进时辰与铜钱，不写存档、不是架构文档 6.3 的旅行事务。
/// 截图参数 <c>--tab</c>：0 选江南粮仓（骑马）、1 选未开放的北岭驿、2 自芦湾旧渡乘渡船去武当山驿、
/// 3 放大到 1.15 看芦湾一带、4 骑马去山门路并自动启程（配合 <c>--motion --settle=帧数</c> 截取行进途中）、
/// 5 缩到最小看全图并关闭面板，江南粮仓显示地名印。
/// </summary>
public partial class WorldMapPreview : Control
{
    private const float MaxZoom = 1.15f;
    private const float PanelWidth = 540;

    /// <summary>舆图底图的基准缩放：缩放 1.00 时一屏约见江南到中原；地标印不随缩放变大变小。</summary>
    private const float BaseScale = 0.8f;

    private readonly Dictionary<string, MapLandmark> _marks = [];
    private readonly HashSet<string> _visited = [];

    private Control _view = null!;
    private Control _map = null!;
    private Control _pins = null!;
    private RouteLayer _route = null!;
    private VBoxContainer _panel = null!;
    private Control _panelHost = null!;
    private Label _where = null!;
    private Label _money = null!;
    private Label _zoomLabel = null!;
    private VBoxContainer _toasts = null!;

    private string _here = WorldMapSamples.Start;
    private string _target = "sample.granary";
    private TravelMode _mode = TravelMode.Horse;
    private int _hour = WorldMapSamples.StartHour;
    private int _day;
    private int _coins = InventorySamples.Money;
    private float _zoom = 1;
    private bool _dragging;
    private float _dragDistance;
    private bool _travelling;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var node in WorldMapSamples.Nodes.Where(n => n.State == NodeState.Visited))
        {
            _visited.Add(node.Id);
        }

        if (DevCapture.Tab == 2)
        {
            _here = "sample.luwan_ferry";
        }

        (_target, _mode, _zoom) = DevCapture.Tab switch
        {
            1 => ("sample.beiling", TravelMode.Horse, 1f),
            2 => ("map.faction.wudang", TravelMode.Ferry, 1f),
            3 => ("sample.river_wharf", TravelMode.Carriage, MaxZoom),
            4 => ("sample.mountain_pass", TravelMode.Horse, 1f),
            _ => ("sample.granary", TravelMode.Horse, 1f),
        };

        BuildMap();
        AddChild(BuildPlace());
        AddChild(BuildLegend());
        AddChild(BuildHints());
        _panelHost = BuildPanel();
        AddChild(_panelHost);

        _toasts = Ui.Column(UiPalette.SpaceS);
        AddChild(Ui.Place(_toasts, 0, 0, 560, 36, 1320, 200));
        Ui.IgnoreMouse(_toasts);

        // 等视口尺寸确定后再定初始镜头，把当前所在放在未被面板遮住的区域中间。
        CallDeferred(MethodName.InitialView);
    }

    private void InitialView()
    {
        // 镜头略向西北偏，一屏里同时有所在的平原、江河与山门路一带的山。
        var focus = DevCapture.Tab == 3
            ? WorldMapSamples.Node("sample.luwan_ferry").Pos
            : WorldMapSamples.Node(_here).Pos.Lerp(WorldMapSamples.Node("sample.mountain_pass").Pos, 0.45f);
        var open = new Vector2(_view.Size.X - PanelWidth - 40, _view.Size.Y);
        _map.Scale = Vector2.One * MapScale;
        Pan(open / 2 - focus * MapScale);
        RefreshZoom();
        Select(_target);
        if (DevCapture.Tab == 4)
        {
            Depart();
        }
        else if (DevCapture.Tab == 5)
        {
            // 缩到最小看全图，面板关闭，江南粮仓显示地名印（模拟悬停）。
            ZoomAt(_view.Size / 2, MinZoom);
            ClosePanel();
            _marks["sample.granary"].Tagged = true;
        }
    }

    // ── 输入 ─────────────────────────────────────────────

    public override void _Input(InputEvent @event)
    {
        // Tab 与 Enter 先于焦点导航处理：Tab 在地标间跳选，Enter 无论焦点在哪都是启程。
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || _travelling)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.Tab:
                var ids = WorldMapSamples.Nodes.Select(n => n.Id).ToList();
                var step = key.ShiftPressed ? ids.Count - 1 : 1;
                Select(ids[(ids.IndexOf(_target) + step) % ids.Count]);
                foreach (var (id, mark) in _marks)
                {
                    mark.Tagged = id == _target;
                }

                break;
            case Key.Enter or Key.KpEnter when _panelHost.Visible:
                Depart();
                break;
            case Key.Key1 or Key.Key2 or Key.Key3 when _panelHost.Visible:
                var mode = (TravelMode)(key.Keycode - Key.Key1);
                if (Plan(mode) is not null)
                {
                    _mode = mode;
                    Refresh();
                }

                break;
            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Esc 先关交通面板，面板已关时才交给外层回标题。
        if (@event.IsActionPressed("ui_cancel") && _panelHost.Visible && !_travelling)
        {
            ClosePanel();
            GetViewport().SetInputAsHandled();
        }
    }

    private void OnViewInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                var factor = wheel.ButtonIndex == MouseButton.WheelUp ? 1.1f : 1 / 1.1f;
                ZoomAt(wheel.Position, Mathf.Clamp(_zoom * factor, MinZoom, MaxZoom));
                _view.AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Middle or MouseButton.Right } button:
                _dragging = button.Pressed;
                if (button.Pressed)
                {
                    _dragDistance = 0;
                }
                else if (button.ButtonIndex == MouseButton.Left && _dragDistance < 6 && !_travelling)
                {
                    // 在空白处点一下（不是拖动）关闭交通面板。
                    ClosePanel();
                }

                break;
            case InputEventMouseMotion motion when _dragging:
                _dragDistance += motion.Relative.Length();
                Pan(_map.Position + motion.Relative);
                _view.AcceptEvent();
                break;
        }
    }

    private void ZoomAt(Vector2 anchor, float zoom, bool force = false)
    {
        if (!force && Mathf.IsEqualApprox(zoom, _zoom))
        {
            return;
        }

        var local = (anchor - _map.Position) / MapScale;
        _zoom = zoom;
        _map.Scale = Vector2.One * MapScale;
        Pan(anchor - local * MapScale);
        RefreshZoom();
    }

    private float MapScale => BaseScale * _zoom;

    /// <summary>最小缩放：整张舆图恰好放进视口，可一眼看到全局。</summary>
    private float MinZoom => Mathf.Min(MaxZoom,
        Mathf.Min(_view.Size.X / WorldMapSamples.Size.X, _view.Size.Y / WorldMapSamples.Size.Y) / BaseScale);

    /// <summary>
    /// 移动底图，再把地标摆到对应位置。舆图比视口大的方向上平移限制在图内、不露出图外空白；
    /// 缩到比视口小的方向上居中。
    /// </summary>
    private void Pan(Vector2 pos)
    {
        var min = _view.Size - WorldMapSamples.Size * MapScale;
        float Fit(float p, float lo) => lo >= 0 ? lo / 2 : Mathf.Clamp(p, lo, 0);
        _map.Position = new Vector2(Fit(pos.X, min.X), Fit(pos.Y, min.Y)).Round();
        foreach (var mark in _marks.Values)
        {
            mark.Place(_map.Position + mark.Node.Pos * MapScale);
        }
    }

    private void RefreshZoom() => _zoomLabel.Text = $"缩放 {_zoom:0.00}";

    // ── 地图与地标 ───────────────────────────────────────

    private void BuildMap()
    {
        // 缩到全图时视口边上露出的底色：与远海同色。
        var sea = new ColorRect { Color = new Color(0.13f, 0.3f, 0.42f), MouseFilter = MouseFilterEnum.Ignore };
        sea.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(sea);

        _view = new Control { ClipContents = true, MouseFilter = MouseFilterEnum.Stop };
        _view.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _view.GuiInput += OnViewInput;
        AddChild(_view);

        _map = new Control { MouseFilter = MouseFilterEnum.Ignore, Size = WorldMapSamples.Size };
        _view.AddChild(_map);
        var baked = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore, Size = WorldMapSamples.Size, StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, TextureFilter = TextureFilterEnum.LinearWithMipmaps,
        };
        _map.AddChild(baked);
        BakeMap(baked);
        _route = new RouteLayer();
        _map.AddChild(_route);
        _pins = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _pins.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _view.AddChild(_pins);

        foreach (var node in WorldMapSamples.Nodes)
        {
            var mark = new MapLandmark(node);
            mark.Toggled += on =>
            {
                if (on && !_travelling)
                {
                    foreach (var other in _marks.Values)
                    {
                        other.Tagged = false;
                    }

                    Select(node.Id);
                }
                else if (!on)
                {
                    // 已选中的地标再点一下保持选中（行进途中也不取消）。
                    mark.SetSelected(node.Id == _target && _panelHost.Visible);
                }
            };
            _marks[node.Id] = mark;
            _pins.AddChild(mark);
        }

        RefreshMarks();
    }

    /// <summary>关闭交通面板：取消选中、收起所选路线；再点地标或按 Tab 重新打开。</summary>
    private void ClosePanel()
    {
        if (!_panelHost.Visible)
        {
            return;
        }

        _panelHost.Visible = false;
        _route.Show(null, false);
        foreach (var mark in _marks.Values)
        {
            mark.SetSelected(false);
            mark.Tagged = false;
        }
    }

    private void RefreshMarks()
    {
        foreach (var (id, mark) in _marks)
        {
            mark.SetState(_visited.Contains(id), id == _here);
        }
    }

    /// <summary>
    /// 舆图烘焙：地形着色器与上万个矢量三角形只在离屏视口里画一次，读回后生成多级纹理，
    /// 之后缩放平移只是移动一张贴图（缩小时靠多级纹理保持清晰、不闪烁）。
    /// </summary>
    private async void BakeMap(TextureRect target)
    {
        var baker = new SubViewport
        {
            Size = (Vector2I)WorldMapSamples.Size, Disable3D = true, TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        baker.AddChild(new WorldMapCanvas());
        AddChild(baker);

        // 多等一帧，确保着色器与矢量绘制都已提交。
        for (var i = 0; i < 2; i++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }

        if (!IsInstanceValid(this))
        {
            return;
        }

        var image = baker.GetTexture().GetImage();
        baker.QueueFree();
        image.GenerateMipmaps();
        target.Texture = ImageTexture.CreateFromImage(image);
    }

    private void Select(string id)
    {
        _target = id;
        foreach (var (other, mark) in _marks)
        {
            mark.SetSelected(other == id);
        }

        _panelHost.Visible = true;

        if (id != _here && Plan(_mode) is null)
        {
            _mode = Enum.GetValues<TravelMode>().FirstOrDefault(m => Plan(m) is not null, _mode);
        }

        Refresh();
    }

    // ── 路线估算 ─────────────────────────────────────────

    private sealed record Leg(string To, float Length, Vector2[] Path);

    private sealed record Route(List<Leg> Legs, int Hours, int Coins)
    {
        public Vector2[] Path => Legs.SelectMany((l, i) => i == 0 ? l.Path : l.Path[1..]).ToArray();
    }

    /// <summary>按方式在路网上求最短路：渡船只走水路，骑马与马车只走陆路。到不了返回 null。</summary>
    private Route? Plan(TravelMode mode)
    {
        if (_target == _here || WorldMapSamples.Node(_target).State == NodeState.Locked)
        {
            return null;
        }

        var water = mode == TravelMode.Ferry;
        var dist = new Dictionary<string, float> { [_here] = 0 };
        var back = new Dictionary<string, (string From, Leg Leg)>();
        var open = new PriorityQueue<string, float>();
        open.Enqueue(_here, 0);
        while (open.TryDequeue(out var at, out var d))
        {
            if (at == _target)
            {
                break;
            }

            foreach (var edge in WorldMapSamples.Edges.Where(e => e.Water == water && (e.From == at || e.To == at)))
            {
                var path = WorldMapCanvas.RoutePath(edge);
                if (edge.To == at)
                {
                    path = path.Reverse().ToArray();
                }

                var next = edge.From == at ? edge.To : edge.From;
                if (WorldMapSamples.Node(next).State == NodeState.Locked && next != _target)
                {
                    continue;
                }

                var length = 0f;
                for (var i = 1; i < path.Length; i++)
                {
                    length += path[i - 1].DistanceTo(path[i]);
                }

                if (dist.TryGetValue(next, out var known) && known <= d + length)
                {
                    continue;
                }

                dist[next] = d + length;
                back[next] = (at, new Leg(next, length, path));
                open.Enqueue(next, d + length);
            }
        }

        if (!back.ContainsKey(_target))
        {
            return null;
        }

        var legs = new List<Leg>();
        for (var at = _target; at != _here; at = back[at].From)
        {
            legs.Insert(0, back[at].Leg);
        }

        var (pace, rate) = WorldMapSamples.Rate(mode);
        var hours = Mathf.Max(1, Mathf.CeilToInt(dist[_target] / pace));
        return new Route(legs, hours, hours * rate);
    }

    // ── 右侧：交通面板 ───────────────────────────────────

    private Control BuildPanel()
    {
        _panel = Ui.Column(14);
        var panel = Ui.Panel(UiTheme.DarkPanel, _panel);
        Motion.Enter(panel, 0.1f, Motion.Normal, fromX: 40, rise: 0);
        return Ui.Place(panel, 1, 0, -PanelWidth - 40, 32, -40, 1048);
    }

    private void Refresh()
    {
        Ui.ClearChildren(_panel);
        var node = WorldMapSamples.Node(_target);
        var here = _target == _here;
        var route = Plan(_mode);
        _route.Show(route?.Path, _mode == TravelMode.Ferry);

        // 目的地
        var state = here ? "所在" : node.State switch
        {
            NodeState.Locked => "未开放",
            _ when _visited.Contains(node.Id) => "已到访",
            _ => "未到访",
        };
        var title = Ui.Column(2, Ui.Text(here ? "所在之处" : "目的地", UiTheme.GiltLabel, 18),
            Ui.Text(node.Name, UiTheme.DarkTitleLabel, 40), Ui.Text(node.Region, UiTheme.DarkMutedLabel, 18));
        var stateLabel = Ui.Text(state, node.State == NodeState.Locked ? UiTheme.DarkMutedLabel : UiTheme.GiltLabel, 20);
        stateLabel.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        _panel.AddChild(Ui.Row(UiPalette.SpaceM, Ui.Expand(title), stateLabel));
        _panel.AddChild(Ui.Rule(dark: true));

        // 路线
        _panel.AddChild(Ui.Section("路线", dark: true));
        if (node.State == NodeState.Locked)
        {
            _panel.AddChild(Ui.Text($"⚠　尚未开放：{node.LockReason}", UiTheme.DarkLabel, 20, wrap: true));
            _panel.AddChild(Ui.Text("地标先在图上可见，开放条件写明，不藏在隐形数值里。", UiTheme.DarkMutedLabel, 17, wrap: true));
        }
        else if (here)
        {
            _panel.AddChild(Ui.Text("◎　你在此处。选择别的地标查看路线。", UiTheme.DarkMutedLabel, 20, wrap: true));
        }
        else if (route is null)
        {
            _panel.AddChild(Ui.Text($"⚠　{WorldMapSamples.ModeName(_mode)}无路可达", UiTheme.DarkLabel, 20));
        }
        else
        {
            var list = Ui.Column(4, RouteRow("●", WorldMapSamples.Node(_here).Name, "出发", UiTheme.DarkMutedLabel));
            var (pace, _) = WorldMapSamples.Rate(_mode);
            foreach (var leg in route.Legs)
            {
                var last = leg.To == _target;
                var part = $"{(_mode == TravelMode.Ferry ? "水路" : "陆路")}　约 {Mathf.Max(0.5f, Mathf.Round(leg.Length / pace * 2) / 2):0.#} 时辰";
                list.AddChild(RouteRow(last ? "◆" : "○", WorldMapSamples.Node(leg.To).Name, part, last ? UiTheme.GiltLabel : UiTheme.DarkLabel));
            }

            _panel.AddChild(list);
        }

        // 方式
        _panel.AddChild(Ui.Section("方式", dark: true));
        var modes = Ui.Row(UiPalette.SpaceS);
        var group = new ButtonGroup();
        foreach (var mode in Enum.GetValues<TravelMode>())
        {
            modes.AddChild(ModeButton(mode, group));
        }

        _panel.AddChild(modes);
        var note = here || node.State == NodeState.Locked ? "" :
            route is null ? $"{WorldMapSamples.ModeName(_mode)}：此地不临水路，改走陆路" : WorldMapSamples.ModeNote(_mode);
        _panel.AddChild(Ui.Text(note, UiTheme.DarkMutedLabel, 17, wrap: true));

        // 同行
        _panel.AddChild(Ui.Section("同行", dark: true));
        var party = Ui.Row(UiPalette.SpaceM);
        foreach (var name in WorldMapSamples.Companions)
        {
            var glyph = Ui.Glyph(name == "主角" ? "主" : name[..1], name == "主角" ? UiPalette.Accent : UiPalette.Trim, 44);
            var label = Ui.Text(name, UiTheme.DarkLabel, 18);
            label.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            party.AddChild(Ui.Row(UiPalette.SpaceS, glyph, label));
        }

        _panel.AddChild(party);

        // 到达后可见
        _panel.AddChild(Ui.Section(here ? "此处可见" : "到达后可见", dark: true));
        if (node.Events.Length == 0)
        {
            _panel.AddChild(Ui.Text("开放后显示", UiTheme.DarkMutedLabel, 18));
        }

        foreach (var text in node.Events)
        {
            var main = text.StartsWith('◆');
            var done = text.StartsWith('✓');
            _panel.AddChild(Ui.Text(text, main ? UiTheme.GiltLabel : done ? UiTheme.DarkMutedLabel : UiTheme.DarkLabel, 19, wrap: true));
        }

        _panel.AddChild(Ui.Spacer(horizontal: false));
        _panel.AddChild(Ui.Rule(dark: true));

        // 结算与启程
        string summary;
        if (route is null)
        {
            summary = here ? "已在此处" : node.State == NodeState.Locked ? "尚未开放，不能启程" : "换一种方式再启程";
        }
        else
        {
            var arrive = _hour + route.Hours;
            var day = arrive >= 12 ? "次日" : "当日";
            summary = $"约 {route.Hours} 时辰，{day}{WorldMapSamples.Hours[arrive % 12]}时抵达　·　铜钱 {_coins} → {_coins - route.Coins} 文";
        }

        _panel.AddChild(Ui.Text(summary, route is null ? UiTheme.DarkMutedLabel : UiTheme.DarkLabel, 19, wrap: true));
        var go = Ui.Button("启　程", UiTheme.PrimaryButton, Depart, disabled: route is null || route.Coins > _coins);
        go.CustomMinimumSize = new Vector2(0, 62);
        go.AddThemeFontSizeOverride("font_size", 28);
        go.FocusMode = FocusModeEnum.None;
        _panel.AddChild(Ui.Row(UiPalette.SpaceM, Ui.KeyHint("Enter", ""), Ui.Expand(go)));
    }

    private static Control RouteRow(string mark, string name, string detail, string variation)
    {
        var row = Ui.Row(UiPalette.SpaceS, Ui.Text(mark, variation, 18), Ui.Text(name, variation, 21), Ui.Spacer(),
            Ui.Text(detail, UiTheme.DarkMutedLabel, 17));
        return row;
    }

    private Button ModeButton(TravelMode mode, ButtonGroup group)
    {
        var plan = Plan(mode);

        var button = new Button
        {
            ThemeTypeVariation = UiTheme.DarkButton, ToggleMode = true, ButtonGroup = group,
            Disabled = plan is null, FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, 92), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = plan is null ? "此路线不可用" : WorldMapSamples.ModeNote(mode),
        };
        button.SetPressedNoSignal(plan is not null && mode == _mode);
        button.Toggled += on =>
        {
            if (on)
            {
                _mode = mode;
                CallDeferred(MethodName.Refresh);
            }
        };

        var glyph = Ui.Glyph(WorldMapSamples.ModeGlyph(mode), mode == TravelMode.Ferry ? UiPalette.Accent : UiPalette.Ochre, 36);
        var name = Ui.Text($"{(int)mode + 1}　{WorldMapSamples.ModeName(mode)}", UiTheme.DarkLabel, 20);
        name.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var cost = Ui.Text(plan is null ? "不可用" : $"{plan.Hours} 时辰　{plan.Coins} 文", UiTheme.DarkMutedLabel, 16);
        var body = Ui.Column(4, Ui.Row(UiPalette.SpaceS, glyph, name), cost);
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddChild(body);
        button.AddChild(Ui.IgnoreMouse(margin));
        if (plan is null)
        {
            margin.Modulate = new Color(1, 1, 1, 0.5f);
        }

        return button;
    }

    // ── 启程：行进预览 ───────────────────────────────────

    private void Depart()
    {
        var route = Plan(_mode);
        if (_travelling || route is null || route.Coins > _coins)
        {
            return;
        }

        var destination = _target;
        var start = _hour + _day * 12;
        void Arrive()
        {
            _travelling = false;
            _route.Progress = -1;
            _here = destination;
            _visited.Add(destination);
            _coins -= route.Coins;
            SetClock(start + route.Hours);
            RefreshMarks();
            Select(destination);
            Toast("抵达", $"{WorldMapSamples.Node(destination).Name}　·　{WorldMapSamples.ModeName(_mode)} {route.Hours} 时辰、{route.Coins} 文",
                "展示预览，不写存档");
        }

        if (!Motion.Enabled)
        {
            Arrive();
            return;
        }

        // 行进期间锁住重复输入，按路程推进时辰（架构文档 6.3 的“锁定重复输入”在此只做表现）。
        _travelling = true;
        var tween = CreateTween();
        tween.TweenMethod(Callable.From<float>(t =>
        {
            _route.Progress = t;
            SetClock(start + Mathf.FloorToInt(route.Hours * t));
        }), 0f, 1f, Mathf.Clamp(route.Hours * 0.7f, 1.2f, 3f)).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.TweenCallback(Callable.From(Arrive));
    }

    private void SetClock(int absoluteHour)
    {
        _day = absoluteHour / 12;
        _hour = absoluteHour % 12;
        var day = _day == 0 ? "" : $"第 {_day + 1} 日　";
        _where.Text = $"所在　{WorldMapSamples.Node(_here).Name}　·　{day}{WorldMapSamples.Hours[_hour]}时　·　晴";
        _money.Text = $"{_coins} 文";
    }

    // ── 左上：所在与时辰 ─────────────────────────────────

    private Control BuildPlace()
    {
        _where = Ui.Text("", UiTheme.DarkMutedLabel, 18);
        _money = Ui.Text("", UiTheme.DarkLabel, 20);
        var money = Ui.Row(UiPalette.SpaceS, Ui.Glyph("钱", UiPalette.Gilt.Darkened(0.2f), 28), _money);
        var column = Ui.Column(4, Ui.Text("江湖大地图", UiTheme.DarkTitleLabel, 32), _where, money);
        column.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var panel = Ui.Panel(UiTheme.GlassPanel, Ui.Row(UiPalette.SpaceM, Ui.Seal("江湖"), column));
        Motion.Enter(panel, 0.05f, Motion.Normal, rise: -10);
        SetClock(_hour);
        return Ui.IgnoreMouse(Ui.Place(panel, 0, 0, 40, 32, 520, 200));
    }

    // ── 左下：图例与按键 ─────────────────────────────────

    private Control BuildLegend()
    {
        var marks = Ui.Row(UiPalette.SpaceL,
            LegendBadge(MapLandmark.Look.Visited, "已到访"), LegendBadge(MapLandmark.Look.Known, "未到访"),
            LegendBadge(MapLandmark.Look.Locked, "未开放"), LegendText("◆", UiPalette.Gilt, "有事件"),
            LegendText("◎", UiPalette.Accent.Lightened(0.35f), "所在"));
        var lines = Ui.Row(UiPalette.SpaceL,
            LegendText("‒ ‒ ‒", UiPalette.Ochre.Lightened(0.35f), "陆路"), LegendText("·  ·  ·", UiPalette.Accent.Lightened(0.4f), "水路"),
            LegendText("━━", UiPalette.Cinnabar.Lightened(0.2f), "所选路线"));
        _zoomLabel = Ui.Text("", UiTheme.GiltLabel, 17);
        lines.AddChild(Ui.Spacer());
        lines.AddChild(_zoomLabel);
        var panel = Ui.Panel(UiTheme.GlassPanel, Ui.Column(UiPalette.SpaceS, marks, lines));
        return Ui.IgnoreMouse(Ui.Place(panel, 0, 1, 40, -200, 700, -112));
    }

    private static Control LegendBadge(MapLandmark.Look look, string text)
    {
        var chip = new Control { CustomMinimumSize = new Vector2(28, 28), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        chip.Draw += () => MapLandmark.DrawBadge(chip, new Vector2(14, 13), 12, look, MapIcon.Inn);
        return Ui.Row(UiPalette.SpaceS, chip, Ui.Text(text, UiTheme.DarkLabel, 17));
    }

    private static Control LegendText(string symbol, Color color, string text)
    {
        var mark = Ui.Text(symbol, size: 18);
        mark.AddThemeColorOverride("font_color", color);
        return Ui.Row(UiPalette.SpaceS, mark, Ui.Text(text, UiTheme.DarkLabel, 17));
    }

    private static Control BuildHints()
    {
        var hints = Ui.KeyHints(true, ("滚轮", "缩放"), ("拖动", "平移"), ("Tab", "地标"), ("1–3", "方式"), ("Enter", "启程"), ("Esc", "关闭 / 返回"));
        var note = Ui.Text("舆图与地标图标为程序化占位（架构文档 10.3）\n地标与路程为固定样例", UiTheme.DarkMutedLabel, 14);
        note.HorizontalAlignment = HorizontalAlignment.Right;
        note.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        hints.AddChild(Ui.Spacer());
        hints.AddChild(note);
        return Ui.IgnoreMouse(Ui.Place(Ui.Panel(UiTheme.GlassPanel, hints), 0, 1, 40, -96, 1320, -40));
    }

    // ── 通知 ─────────────────────────────────────────────

    private void Toast(string kind, string text, string where)
    {
        var toast = new PanelContainer();
        toast.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            FillA = UiPalette.Abyss with { A = 0.9f }, FillB = UiPalette.Abyss with { A = 0.55f }, Horizontal = true,
            Ragged = 1.4f, Seed = 71, Marker = UiPalette.Cinnabar.Lightened(0.1f), MarkerWidth = 4,
        }.Margins(22, 10));
        toast.AddChild(Ui.Row(UiPalette.SpaceM, Ui.Text(kind, UiTheme.GiltLabel, 18), Ui.Text(text, UiTheme.DarkLabel, 21),
            Ui.Spacer(), Ui.Text(where, UiTheme.DarkMutedLabel, 16)));
        _toasts.AddChild(Ui.IgnoreMouse(toast));
        Motion.Enter(toast, 0, Motion.Normal, rise: -12);
        while (_toasts.GetChildCount() > 2)
        {
            var old = _toasts.GetChild(0);
            _toasts.RemoveChild(old);
            old.QueueFree();
        }
    }
}

/// <summary>所选路线：绢色衬底上一笔朱砂，终点一圈；行进预览时沿路线移动的石青行旅标记。</summary>
public partial class RouteLayer : Control
{
    private Vector2[]? _path;
    private bool _water;
    private float _progress = -1;

    public float Progress
    {
        get => _progress;
        set
        {
            _progress = value;
            QueueRedraw();
        }
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = WorldMapSamples.Size;
    }

    public void Show(Vector2[]? path, bool water)
    {
        _path = path;
        _water = water;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_path is not { Length: > 1 } path)
        {
            return;
        }

        var item = GetCanvasItem();
        Brushwork.Stroke(item, path, 13, UiPalette.Surface with { A = 0.6f }, 3, 0, 0.02f, 0.05f);
        Brushwork.Stroke(item, path, 5.5f, UiPalette.Cinnabar with { A = 0.9f }, 5, 0.3f, 0.04f, 0.12f);
        if (_water)
        {
            foreach (var (p, _) in WorldMapCanvas.Walk(path, 24))
            {
                Brushwork.Dot(item, p, 2.2f, UiPalette.Surface);
            }
        }

        var end = path[^1];
        DrawArc(end, 12, 0, Mathf.Tau, 36, UiPalette.Cinnabar, 3, true);

        if (_progress >= 0)
        {
            var at = WorldMapCanvas.AlongPath(path, _progress);
            DrawCircle(at, 15, UiPalette.Abyss with { A = 0.35f });
            DrawCircle(at, 12, UiPalette.Accent);
            DrawArc(at, 12, 0, Mathf.Tau, 32, UiPalette.Surface, 3, true);
        }
    }
}
