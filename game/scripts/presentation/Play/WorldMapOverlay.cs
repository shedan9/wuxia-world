using Godot;
using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Pages;
using WuxiaWorld.Game.Preview.Samples;
using TravelMode = WuxiaWorld.Domain.World.TravelMode;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 游戏内江湖大地图（开发计划 M2-02，架构文档 6.5，界面规范 5.6）：版式沿用已验收的 M0 大地图页——
/// 全屏舆图（<see cref="WorldMapCanvas"/>，道路与地标取内容数据）、圆章地标（<see cref="MapLandmark"/>）、右侧黛本交通面板、
/// 左上所在与时辰、左下图例。地标、到访、可走路线与开放条件都读 <see cref="GameSession.WorldMapNodes"/>；
/// 方式、费用与时辰读路线定义。启程先开切换票据（<see cref="GameSession.BeginRoute"/>，校验条件与银两、锁住重复启程），
/// 行旅标记沿路线走完后交给探索页载入目的地并提交；载入失败时探索页中止票据，不扣费、不推进时辰。
/// 滚轮缩放、拖动平移、Tab 跳选地标、数字键换方式、Enter 启程；Esc 先关面板再关地图，M 直接关地图。
/// </summary>
public partial class WorldMapOverlay : Control
{
    private const float MaxZoom = 1.15f;
    private const float PanelWidth = 540;
    private const float BaseScale = 0.8f;

    /// <summary>烘焙好的舆图按道路组合缓存：同一局里反复打开不再重画。</summary>
    private static readonly Dictionary<string, ImageTexture> Baked = [];

    private readonly PlaySession _play;
    private readonly Action _close;
    private readonly Action<Transition> _depart;
    private readonly Dictionary<string, MapLandmark> _marks = [];
    private readonly Dictionary<string, WorldNodeStatus> _nodes = [];
    private readonly List<(WorldRoadDefinition Road, Vector2[] Path)> _roads = [];
    private readonly Vector2 _unit;

    private Control _view = null!;
    private Control _map = null!;
    private RouteLayer _route = null!;
    private VBoxContainer _panel = null!;
    private Control _panelHost = null!;
    private Label _where = null!;
    private Label _money = null!;
    private Label _zoomLabel = null!;

    private string _here;
    private string _target;
    private TravelMode? _mode;
    private float _zoom = 1;
    private bool _dragging;
    private float _dragDistance;

    /// <param name="target">打开时选中的地标；为 null 时选中所在之处。</param>
    /// <param name="close">关闭地图（不出发）。</param>
    /// <param name="depart">已开好切换票据、行进动画走完：交给探索页载入目的地。</param>
    public WorldMapOverlay(PlaySession play, string? target, Action close, Action<Transition> depart)
    {
        _play = play;
        _close = close;
        _depart = depart;
        var content = Game.Rules.Content;
        var frame = content.WorldMap.Frame is [var w, var h] ? new Vector2(w, h) : WorldMapSamples.Size / WorldMapSamples.Scale;
        _unit = WorldMapSamples.Size / frame;
        foreach (var n in Game.WorldMapNodes)
        {
            _nodes[n.Node.Id] = n;
        }

        foreach (var road in content.WorldMap.Roads.Where(r => _nodes.ContainsKey(r.From) && _nodes.ContainsKey(r.To)))
        {
            _roads.Add((road, [Pos(content.Nodes[road.From]), .. road.Via.Select(P), Pos(content.Nodes[road.To])]));
        }

        _here = _nodes.Values.FirstOrDefault(n => n.Here)?.Node.Id ?? _nodes.Keys.FirstOrDefault() ?? "";
        _target = target is not null && _nodes.ContainsKey(target) ? target : _here;
    }

    private GameSession Game => _play.Game;

    private WorldState World => Game.World;

    /// <summary>启程后行旅标记行进中：不再响应选点、换方式与关闭。</summary>
    public bool Travelling { get; private set; }

    /// <summary>交通面板开着、选中的是可前往之处：Enter 即启程（自动走查据此注入 Enter）。</summary>
    public bool ReadyToDepart => _panelHost.Visible && !Travelling && Option() is not null;

    private Vector2 P(IReadOnlyList<int> p) => p is [var x, var y] ? new Vector2(x, y) * _unit : Vector2.Zero;

    private Vector2 Pos(WorldNodeDefinition n) => P(n.Pos);

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildMap();
        AddChild(BuildPlace());
        AddChild(BuildLegend());
        _panelHost = BuildPanel();
        AddChild(_panelHost);
        CallDeferred(MethodName.InitialView);
    }

    private void InitialView()
    {
        // 把所在之处与所选地标（或全部可见地标）放进未被面板遮住的区域，缩放在 1 到最大之间取能装下的那一档。
        var focus = _nodes.Values.Select(n => Pos(n.Node)).ToList();
        if (_target != _here)
        {
            focus = [Pos(_nodes[_here].Node), Pos(_nodes[_target].Node)];
        }

        var open = new Vector2(_view.Size.X - PanelWidth - 40, _view.Size.Y);
        var min = focus.Aggregate((a, b) => a.Min(b));
        var max = focus.Aggregate((a, b) => a.Max(b));
        var span = (max - min) * BaseScale + new Vector2(480, 360);
        _zoom = Mathf.Clamp(Mathf.Min(open.X / span.X, open.Y / span.Y), 1, MaxZoom);
        _map.Scale = Vector2.One * MapScale;
        Pan(open / 2 - (min + max) / 2 * MapScale);
        RefreshZoom();
        Select(_target);
    }

    // ── 输入 ─────────────────────────────────────────────

    public override void _Input(InputEvent @event)
    {
        // Tab 与 Enter 先于焦点导航处理：Tab 在地标间跳选，Enter 无论焦点在哪都是启程。
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        if (Travelling)
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        switch (key.Keycode)
        {
            case Key.Tab:
                var ids = _nodes.Keys.ToList();
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
            case >= Key.Key1 and <= Key.Key9 when _panelHost.Visible && Route() is { } route:
                var index = (int)(key.Keycode - Key.Key1);
                if (index < route.Modes.Count && Usable(route.Modes[index]) is null)
                {
                    _mode = route.Modes[index].Mode;
                    Refresh();
                }

                break;
            case Key.Escape when _panelHost.Visible:
                ClosePanel();
                break;
            case Key.Escape or Key.M:
                _close();
                break;
            default:
                return;
        }

        GetViewport().SetInputAsHandled();
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
                else if (button.ButtonIndex == MouseButton.Left && _dragDistance < 6 && !Travelling)
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

    private void ZoomAt(Vector2 anchor, float zoom)
    {
        if (Mathf.IsEqualApprox(zoom, _zoom))
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

    private float MinZoom => Mathf.Min(MaxZoom,
        Mathf.Min(_view.Size.X / WorldMapSamples.Size.X, _view.Size.Y / WorldMapSamples.Size.Y) / BaseScale);

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
        Bake(baked);
        _route = new RouteLayer();
        _map.AddChild(_route);
        var pins = new Control { MouseFilter = MouseFilterEnum.Ignore };
        pins.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _view.AddChild(pins);

        foreach (var status in _nodes.Values)
        {
            var id = status.Node.Id;
            var mark = new MapLandmark(Landmark(status));
            mark.Toggled += on =>
            {
                if (Travelling)
                {
                    mark.SetSelected(id == _target);
                    return;
                }

                if (on)
                {
                    foreach (var other in _marks.Values)
                    {
                        other.Tagged = false;
                    }

                    Select(id);
                }
                else
                {
                    mark.SetSelected(id == _target && _panelHost.Visible);
                }
            };
            _marks[id] = mark;
            pins.AddChild(mark);
            mark.SetState(status.Visited, status.Here);
        }
    }

    /// <summary>把地标状态换成地标章的显示数据：未开放灰淡；到达后可见的事件挂菱形。</summary>
    private MapNode Landmark(WorldNodeStatus s)
    {
        var icon = Enum.TryParse<MapIcon>(s.Node.Icon.ToString(), out var i) ? i : MapIcon.Inn;
        var state = !s.Open ? NodeState.Locked : s.Visited ? NodeState.Visited : NodeState.Known;
        return new MapNode(s.Node.Id, _play.Name(s.Node.Id), _play.Text(s.Node.Id + ".desc") ?? "", Pos(s.Node), icon, state,
            [.. EventLines(s)], s.LockedHint is { } h ? _play.Text(h) ?? h : null);
    }

    /// <summary>到达后可见：◆ 主线、○ 地区事件；只列此刻能开始的，用事件名，没有名字的主线过场写主线任务名。</summary>
    private IEnumerable<string> EventLines(WorldNodeStatus s)
    {
        if (!s.Open)
        {
            yield break;
        }

        var main = Game.Rules.Content.Quests.Values.FirstOrDefault(q => q.Kind == QuestKind.Main && World.QuestStatusOf(q.Id) == QuestStatus.Active);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in Game.EventsIn(s.Node))
        {
            var isMain = e.Priority == EventPriority.MainUrgent;
            var name = _play.Text(e.Id + ".name") ?? (isMain && main is not null ? _play.Name(main.Id) : null);
            if (name is not null && seen.Add(name))
            {
                yield return isMain ? $"◆ 主线　{name}" : $"○ {name}";
            }
        }
    }

    /// <summary>
    /// 舆图烘焙：地形着色器与矢量山峦、江河、道路只在离屏视口里画一次，读回后生成多级纹理；
    /// 同一组道路的结果缓存起来，再打开大地图直接取用。
    /// </summary>
    private async void Bake(TextureRect target)
    {
        var key = string.Join("|", _nodes.Keys) + "#" + string.Join("|", _roads.Select(r => $"{r.Road.From}>{r.Road.To}:{r.Road.Kind}"));
        if (Baked.TryGetValue(key, out var cached))
        {
            target.Texture = cached;
            return;
        }

        var baker = new SubViewport
        {
            Size = (Vector2I)WorldMapSamples.Size, Disable3D = true, TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        baker.AddChild(new WorldMapCanvas(
            [.. _nodes.Values.Select(n => Pos(n.Node))],
            [.. _roads.Select(r => new MapRoad(r.Path, r.Road.Kind == RoadKind.Water))]));
        AddChild(baker);
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
        var texture = ImageTexture.CreateFromImage(image);
        Baked[key] = texture;
        target.Texture = texture;
    }

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

    private void Select(string id)
    {
        _target = id;
        foreach (var (other, mark) in _marks)
        {
            mark.SetSelected(other == id);
        }

        _panelHost.Visible = true;
        if (Route() is { } route && (_mode is not { } m || route.Modes.FirstOrDefault(x => x.Mode == m) is not { } current || Usable(current) is not null))
        {
            _mode = route.Modes.FirstOrDefault(x => Usable(x) is null)?.Mode ?? (route.Modes.Count > 0 ? route.Modes[0].Mode : null);
        }

        Refresh();
    }

    // ── 路线 ─────────────────────────────────────────────

    /// <summary>去所选地标、条件已满足的路线；所在之处或不能前往时为 null。</summary>
    private RouteDefinition? Route() => _nodes.TryGetValue(_target, out var s) ? s.Route : null;

    /// <summary>所选方式（须可用）。</summary>
    private RouteMode? Option() =>
        Route() is { } route && _mode is { } m && route.Modes.FirstOrDefault(x => x.Mode == m) is { } mode && Usable(mode) is null ? mode : null;

    /// <summary>这种方式此刻不能用的原因；能用为 null。</summary>
    private string? Usable(RouteMode m) =>
        !Game.Rules.Check(m.When, World) ? "条件未满足" : World.Silver < m.Silver ? $"银两不足（需 {m.Silver} 两）" : null;

    /// <summary>图上的路线折线：渡船走水路，其余走陆路；没画这类路时退回任一条路，再没有就直线。</summary>
    private Vector2[] PathTo(string to, TravelMode mode)
    {
        var kind = mode == TravelMode.Ferry ? RoadKind.Water : RoadKind.Land;
        var candidates = _roads.Where(r => (r.Road.From == _here && r.Road.To == to) || (r.Road.From == to && r.Road.To == _here)).ToList();
        var pick = candidates.FindIndex(r => r.Road.Kind == kind);
        if (pick < 0 && candidates.Count > 0)
        {
            pick = 0;
        }

        if (pick >= 0)
        {
            var (road, path) = candidates[pick];
            return road.From == _here ? path : [.. Enumerable.Reverse(path)];
        }

        return [Pos(_nodes[_here].Node), Pos(_nodes[to].Node)];
    }

    public static string ModeName(TravelMode mode) => mode switch
    {
        TravelMode.Ferry => "乘渡船",
        TravelMode.Walk => "沿岸步行",
        TravelMode.Horse => "骑马",
        TravelMode.Carriage => "坐车",
        _ => "随行",
    };

    private static string ModeGlyph(TravelMode mode) => mode switch
    {
        TravelMode.Ferry => "船",
        TravelMode.Walk => "步",
        TravelMode.Horse => "骑",
        TravelMode.Carriage => "车",
        _ => "行",
    };

    private static string ModeNote(TravelMode mode) => mode switch
    {
        TravelMode.Ferry => "顺水行船，快而省力，要付船钱",
        TravelMode.Walk => "沿河岸走过去，不花钱，多费些时辰",
        TravelMode.Horse => "最快的陆路",
        TravelMode.Carriage => "较慢，途中可歇息",
        _ => "",
    };

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
        var status = _nodes[_target];
        var node = status.Node;
        var here = status.Here;
        var route = Route();
        var option = Option();
        _route.Show(route is not null && _mode is { } shown ? PathTo(_target, shown) : null, _mode == TravelMode.Ferry);

        // 目的地
        var state = here ? "所在" : !status.Open ? "未开放" : status.Visited ? "已到访" : "未到访";
        var title = Ui.Column(2, Ui.Text(here ? "所在之处" : "目的地", UiTheme.GiltLabel, 18),
            Ui.Text(_play.Name(node.Id), UiTheme.DarkTitleLabel, 40), Ui.Text(_play.Text(node.Id + ".desc") ?? "", UiTheme.DarkMutedLabel, 18));
        var stateLabel = Ui.Text(state, status.Open ? UiTheme.GiltLabel : UiTheme.DarkMutedLabel, 20);
        stateLabel.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        _panel.AddChild(Ui.Row(UiPalette.SpaceM, Ui.Expand(title), stateLabel));
        _panel.AddChild(Ui.Rule(dark: true));

        // 路线
        _panel.AddChild(Ui.Section("路线", dark: true));
        if (here)
        {
            _panel.AddChild(Ui.Text("◎　你在此处：" + string.Join("、", node.Maps.Select(_play.Name)), UiTheme.DarkLabel, 20, wrap: true));
            _panel.AddChild(Ui.Text("选别的地标查看路线", UiTheme.DarkMutedLabel, 18));
        }
        else if (route is null)
        {
            _panel.AddChild(Ui.Text("⚠　尚未开放", UiTheme.DarkLabel, 20));
            _panel.AddChild(Ui.Text(status.LockedHint is { } h ? _play.Text(h) ?? h : "此路暂时不通", UiTheme.DarkMutedLabel, 19, wrap: true));
        }
        else
        {
            var leg = _mode == TravelMode.Ferry ? "水路" : "陆路";
            var ticks = option?.Ticks ?? route.Modes.FirstOrDefault(m => m.Mode == _mode)?.Ticks ?? 0;
            _panel.AddChild(Ui.Column(4,
                RouteRow("●", _play.Name(_here), "出发", UiTheme.DarkMutedLabel),
                RouteRow("◆", _play.Name(node.Id), $"{leg}　约 {ticks} 时辰", UiTheme.GiltLabel)));
        }

        // 方式
        if (route is not null)
        {
            _panel.AddChild(Ui.Section("方式", dark: true));
            var modes = Ui.Row(UiPalette.SpaceS);
            var group = new ButtonGroup();
            for (var i = 0; i < route.Modes.Count; i++)
            {
                modes.AddChild(ModeButton(route.Modes[i], i, group));
            }

            _panel.AddChild(modes);
            if (_mode is { } m)
            {
                _panel.AddChild(Ui.Text(ModeNote(m), UiTheme.DarkMutedLabel, 17, wrap: true));
            }
        }

        // 同行
        _panel.AddChild(Ui.Section("同行", dark: true));
        var party = Ui.Row(UiPalette.SpaceM);
        foreach (var who in World.Party)
        {
            var name = _play.Name(who);
            var label = Ui.Text(name, UiTheme.DarkLabel, 18);
            label.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            party.AddChild(Ui.Row(UiPalette.SpaceS, Ui.Glyph(name[..1], Looks.Of(who).Tone, 44), label));
        }

        _panel.AddChild(party);

        // 到达后可见
        _panel.AddChild(Ui.Section(here ? "此处可见" : "到达后可见", dark: true));
        var events = EventLines(status).ToList();
        if (events.Count == 0)
        {
            _panel.AddChild(Ui.Text(status.Open ? "暂无要事" : "开放后显示", UiTheme.DarkMutedLabel, 18));
        }

        foreach (var text in events)
        {
            _panel.AddChild(Ui.Text(text, text.StartsWith('◆') ? UiTheme.GiltLabel : UiTheme.DarkLabel, 19, wrap: true));
        }

        _panel.AddChild(Ui.Spacer(horizontal: false));
        _panel.AddChild(Ui.Rule(dark: true));

        // 结算与启程
        string summary;
        if (option is { } o)
        {
            summary = $"约 {o.Ticks} 时辰，{PlaySession.ClockText(World.Clock + o.Ticks)}抵达　·　银 {World.Silver} → {World.Silver - o.Silver} 两";
        }
        else
        {
            summary = here ? "已在此处" : route is null ? "尚未开放，不能启程" : "换一种方式再启程";
        }

        _panel.AddChild(Ui.Text(summary, option is null ? UiTheme.DarkMutedLabel : UiTheme.DarkLabel, 19, wrap: true));
        var go = Ui.Button("启　程", UiTheme.PrimaryButton, Depart, disabled: option is null);
        go.CustomMinimumSize = new Vector2(0, 62);
        go.AddThemeFontSizeOverride("font_size", FontScale.Of(28));
        go.FocusMode = FocusModeEnum.None;
        _panel.AddChild(Ui.Row(UiPalette.SpaceM, Ui.KeyHint("Enter", ""), Ui.Expand(go)));
    }

    private static Control RouteRow(string mark, string name, string detail, string variation) =>
        Ui.Row(UiPalette.SpaceS, Ui.Text(mark, variation, 18), Ui.Text(name, variation, 21), Ui.Spacer(),
            Ui.Text(detail, UiTheme.DarkMutedLabel, 17));

    private Button ModeButton(RouteMode m, int index, ButtonGroup group)
    {
        var problem = Usable(m);
        var button = new Button
        {
            ThemeTypeVariation = UiTheme.DarkButton, ToggleMode = true, ButtonGroup = group,
            Disabled = problem is not null, FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, 92), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = problem ?? ModeNote(m.Mode),
        };
        button.SetPressedNoSignal(problem is null && m.Mode == _mode);
        button.Toggled += on =>
        {
            if (on)
            {
                _mode = m.Mode;
                CallDeferred(MethodName.Refresh);
            }
        };

        var glyph = Ui.Glyph(ModeGlyph(m.Mode), m.Mode == TravelMode.Ferry ? UiPalette.Accent : UiPalette.Ochre, 36);
        var name = Ui.Text($"{index + 1}　{ModeName(m.Mode)}", UiTheme.DarkLabel, 20);
        name.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var cost = Ui.Text(problem ?? $"{m.Ticks} 时辰　{(m.Silver > 0 ? $"{m.Silver} 两" : "不花钱")}", UiTheme.DarkMutedLabel, 16);
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddChild(Ui.Column(4, Ui.Row(UiPalette.SpaceS, glyph, name), cost));
        button.AddChild(Ui.IgnoreMouse(margin));
        if (problem is not null)
        {
            margin.Modulate = new Color(1, 1, 1, 0.5f);
        }

        return button;
    }

    // ── 启程 ─────────────────────────────────────────────

    private void Depart()
    {
        if (Travelling || Route() is not { } route || Option() is not { } option)
        {
            return;
        }

        Transition t;
        try
        {
            t = Game.BeginRoute(route.Id, option.Mode);
        }
        catch (InvalidOperationException ex)
        {
            _panel.AddChild(Ui.Text($"⚠　{ex.Message}", UiTheme.DarkLabel, 18, wrap: true));
            return;
        }

        AppHost.Instance.Sound.Play(option.Mode == TravelMode.Ferry ? "travel.oar" : "travel.whoosh", -2);
        Travelling = true;
        var start = World.Clock;
        if (!Motion.Enabled)
        {
            _depart(t);
            return;
        }

        // 行旅标记沿路线行进（每时辰约 0.7 秒，1.2–3 秒），左上时辰随之推进；走完交给探索页载入目的地。
        var tween = CreateTween();
        tween.TweenMethod(Callable.From<float>(p =>
        {
            _route.Progress = p;
            SetClock(start + (long)Math.Floor(option.Ticks * p));
        }), 0f, 1f, Mathf.Clamp(option.Ticks * 0.7f, 1.2f, 3f)).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.TweenCallback(Callable.From(() => _depart(t)));
    }

    private void SetClock(long clock)
    {
        _where.Text = $"所在　{_play.Name(_here)}　·　{PlaySession.ClockText(clock)}";
        _money.Text = $"{World.Silver} 两";
    }

    // ── 左上：所在与时辰 ─────────────────────────────────

    private Control BuildPlace()
    {
        _where = Ui.Text("", UiTheme.DarkMutedLabel, 18);
        _money = Ui.Text("", UiTheme.DarkLabel, 20);
        var money = Ui.Row(UiPalette.SpaceS, Ui.Glyph("银", UiPalette.Gilt.Darkened(0.2f), 28), _money);
        var column = Ui.Column(4, Ui.Text("江湖大地图", UiTheme.DarkTitleLabel, 32), _where, money);
        column.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var panel = Ui.Panel(UiTheme.GlassPanel, Ui.Row(UiPalette.SpaceM, Ui.Seal("江湖"), column));
        Motion.Enter(panel, 0.05f, Motion.Normal, rise: -10);
        SetClock(World.Clock);
        return Ui.IgnoreMouse(Ui.Place(panel, 0, 0, 40, 32, 560, 200));
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
        var hints = Ui.KeyHints(true, ("滚轮", "缩放"), ("拖动", "平移"), ("Tab", "地标"), ("数字", "方式"), ("Enter", "启程"), ("M / Esc", "关闭"));
        var panel = Ui.Panel(UiTheme.GlassPanel, Ui.Column(UiPalette.SpaceS, marks, lines, hints));
        return Ui.IgnoreMouse(Ui.Place(panel, 0, 1, 40, -232, 900, -40));
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
}
