using Godot;
using WuxiaWorld.Application.Persistence;
using WuxiaWorld.Application.World;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Scenery;
using WuxiaWorld.Game.Presentation.Ui;
using WuxiaWorld.Game.Preview.Pages;
using WuxiaWorld.Game.Preview.Samples;
using TravelMode = WuxiaWorld.Domain.World.TravelMode;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 游戏内探索页（架构文档 3 的 Exploration 状态，开发计划 M2-01 / M2-02 / M2-03 的界面接入）：
/// 按当前地图选用已验收的 M0 布景（<see cref="MapStaging"/>），由 <see cref="GameSession"/> 决定落点、交互点、出口、路线、
/// 事件、同行者与站位人物。交互、对话、换图与战斗都经会话事务：对话在副本上跑完才提交；换图先开票据，
/// 重新载入本页后才提交（加载失败则中止，不扣费、不推进时辰）；剧情请求的战斗转到战斗页，结算后回来。
/// 进图先处理途中事件与本图过场事件。Esc 菜单（保存、读取、札记、回标题），J 札记，M 江湖大地图（码头的乘船点也打开它），
/// F5 快速存档，F9 快速读档。
/// </summary>
public partial class ExplorationScreen : Control, IExploreDriver
{
    private const float MarkerItem = 150;
    private const float MarkerEvent = 230;
    private const float MarkerExit = 200;

    private PlaySession _play = null!;
    private MapStage _staging = null!;
    private ExploreStage _view = null!;
    private Control _hudLayer = null!;
    private Control? _hud;
    private Control _overlay = null!;
    private DialogueOverlay? _dialogue;
    private Control? _modal;

    /// <summary>HUD 建立时的世界修订号；暂停菜单里分配潜能、换装备、买卖后回到探索页时据此刷新 HUD。</summary>
    private long _hudRevision = -1;

    /// <summary>自动走查已结束（停在要截图的画面上）。</summary>
    private bool _autoplayFinished;
    private bool _leaving;
    private WorldMapOverlay? _worldMap;
    private string _partyKey = "";
    private (string Key, Vector2 Ground, float Height, string Label)? _goal;
    private double _autoWait = 0.5;

    private GameSession Game => _play.Game;
    private WorldState World => _play.Game.World;
    private MapDefinition Map => Game.Rules.Content.Maps[World.MapId];

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        if (AppHost.Instance.Play is not { } play)
        {
            Fatal("没有进行中的游戏。请从标题页开始新的旅程或读取存档。");
            return;
        }

        _play = play;
        var arrivalNotes = new List<(string, string)>();
        if (play.Arriving is { } t)
        {
            // 目的地已载入：提交切换（架构文档 6.3），随后自动存档。
            play.Arriving = null;
            var r = Game.CommitTransition(t);
            if (!r.Ok)
            {
                arrivalNotes.Add(("提示", $"切换未生效：{r.Error}"));
            }
            else
            {
                arrivalNotes.AddRange(Describe(r));
                AutoSave(arrivalNotes);
            }
        }

        if (Game.RepairSpawn() is { } repaired)
        {
            GD.PushWarning(repaired);
            arrivalNotes.Add(("提示", repaired));
        }

        if (!MapStaging.Maps.TryGetValue(World.MapId, out var staging))
        {
            Fatal($"地图 {World.MapId} 还没有布景摆放（MapStaging）。");
            return;
        }

        _staging = staging;
        AppHost.Instance.Sound.PlayMusic(staging.Music, 2.5f);
        AppHost.Instance.Sound.PlayAmbience(staging.Ambience);
        _partyKey = string.Join(",", World.Party);
        _goal = ComputeGoal();
        _view = staging.Layout switch
        {
            StageLayout.Town => new ExploreTownPreview { Driver = this },
            StageLayout.Inn => new ExploreInnPreview { Driver = this },
            _ => new ExploreWildPreview { Driver = this, Variant = staging.Wild },
        };
        _view.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_view);

        _hudLayer = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _hudLayer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_hudLayer);
        RebuildHud();

        _overlay = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_overlay);

        Toasts(play.PendingToasts.Concat(arrivalNotes).ToList());

        play.PendingToasts.Clear();
        if (DevCapture.CheckStaging)
        {
            CallDeferred(MethodName.CheckStaging);
            return;
        }

        CallDeferred(MethodName.Resume);
    }

    /// <summary>
    /// 摆放核对（<c>--check-staging</c>，开发用）：核对本图每个落点能站人、每个交互点走得到，
    /// 再经剧情换图票据去下一张未核对的地图；全部核对完打印结果退出。只改本次会话，不存档。
    /// </summary>
    private void CheckStaging()
    {
        foreach (var (id, spawn) in _staging.Spawns)
        {
            if (!_view.CanStand(spawn.At))
            {
                DevCapture.StagingProblems++;
                GD.Print($"[staging] {World.MapId} 落点 {id} 站不住人");
            }
        }

        foreach (var (key, at) in _staging.Points)
        {
            if (!_view.CanReach(at))
            {
                DevCapture.StagingProblems++;
                GD.Print($"[staging] {World.MapId} {key} 交互距离内无处可站");
            }
        }

        var content = Game.Rules.Content;
        foreach (var x in Map.Exits.Select(x => "exit:" + x.Id)
                     .Concat(Map.Interactables.Select(i => "interact:" + i.Id))
                     .Concat(content.Routes.Values.Where(r => r.From == World.MapId).Select(r => "route:" + r.Id))
                     .Concat(content.Events.Values.Where(e => e.Map == World.MapId && e.Anchor is not null).Select(e => "anchor:" + e.Anchor))
                     .Where(k => !_staging.Points.ContainsKey(k)))
        {
            DevCapture.StagingProblems++;
            GD.Print($"[staging] {World.MapId} 摆放表缺少 {x}");
        }

        foreach (var spawn in Map.Spawns.Where(s => !_staging.Spawns.ContainsKey(s)))
        {
            DevCapture.StagingProblems++;
            GD.Print($"[staging] {World.MapId} 摆放表缺少落点 {spawn}");
        }

        GD.Print($"[staging] {World.MapId} 已核对（{_staging.Layout}，落点 {_staging.Spawns.Count}、位置 {_staging.Points.Count}）");
        Checked.Add(World.MapId);
        var next = content.Maps.Keys.Order(StringComparer.Ordinal).FirstOrDefault(m => !Checked.Contains(m));
        if (next is null)
        {
            GD.Print($"[staging] 全部 {Checked.Count} 张地图核对完毕，问题 {DevCapture.StagingProblems} 处");
            GetTree().Quit(DevCapture.StagingProblems == 0 ? 0 : 3);
            return;
        }

        Go(Game.BeginStoryTravel(new TravelRequest(next, null)));
    }

    private static readonly HashSet<string> Checked = new(StringComparer.Ordinal);

    /// <summary>进图或提交后：有待开战斗先去打；否则开始途中事件或本图过场事件。</summary>
    private void Resume()
    {
        if (_leaving || _dialogue is not null)
        {
            return;
        }

        if (World.Battle is not null)
        {
            ToBattle();
            return;
        }

        if (Game.AutoEvent is { } auto)
        {
            Open(() => Game.StartEvent(auto.Id));
        }
    }

    // ── 自动走查（--autoplay，开发用）────────────────────

    private int _thumbnailFrames;

    public override void _Process(double delta)
    {
        if (_play is not null && _hud is not null && _modal is null && _dialogue is null && !_leaving && _hudRevision != World.Revision)
        {
            RebuildHud();
        }

        FlushThumbnails();

        if (DevCapture.Autoplay <= 0 || _play is null || _staging is null || _autoplayFinished)
        {
            return;
        }

        var main = Game.Rules.Content.Quests.Values.First(q => q.Kind == QuestKind.Main);
        var done = World.QuestStatusOf(main.Id) == QuestStatus.Completed;
        if (DevCapture.Walk && !_leaving && _dialogue is null && (StepTravelPanel(delta) || (!InputLocked && StepWalk(delta))))
        {
            return;
        }

        // 主线完成后停在章终回顾上截图；其余弹层、对话与换图期间不动。
        if (InputLocked && !(done && _modal is not null && _dialogue is null && !_leaving))
        {
            return;
        }

        _autoWait -= delta;
        if (_autoWait > 0)
        {
            return;
        }

        _autoWait = 0.25;

        // 像玩家一样：升级后去人物页按流派推荐把潜能分完（走查也借此经过分配潜能的事务）。
        // 截人物页时留着未分配的潜能，好看到加点界面。
        if (Game.Growth is { } growth && growth.Unspent(World, growth.Hero) > 0 && Game.CanManage && DevCapture.Hold != "character")
        {
            var add = growth.Recommend(World, growth.Hero);
            var allocated = Game.Allocate(growth.Hero, add);
            GD.Print($"[autoplay] 分配潜能 {string.Join("/", GrowthText.Values(add))}：{(allocated.Ok ? "成功" : allocated.Error)}，{growth.Level(World)} 级");
        }

        var holdInScene = DevCapture.Hold is "journal" or "menu" or "saves" or "travel" or "worldmap" or "character" or "martial" or "equipment" or "inventory" or "party" or "shop";
        var next = AutoTarget();
        if (done || DevCapture.AutoplaySteps >= DevCapture.Autoplay + (DevCapture.Hold is null || holdInScene ? 0 : 3) || next is null)
        {
            switch (DevCapture.Hold)
            {
                case "journal":
                    OpenJournal();
                    break;
                case "menu":
                    AppHost.Instance.Menu.Open();
                    break;
                case "saves":
                    _play.Save(SaveSlot.Manual(1));
                    AppHost.Instance.Menu.Open(slots: true);
                    break;
                case "travel" when Game.Routes is [var route, ..]:
                    OpenWorldMap(Game.Rules.Content.NodeOf(route.To)?.Id);
                    break;
                case "worldmap":
                    OpenWorldMap(null);
                    break;
                case "character":
                    OpenCharacter(0);
                    break;
                case "martial":
                    OpenCharacter(1);
                    break;
                case "equipment":
                    OpenCharacter(2);
                    break;
                case "inventory":
                    OpenInventory();
                    break;
                case "party":
                    OpenParty();
                    break;
                case "shop" when Game.Rules.Content.Shops.Keys.FirstOrDefault() is { } shopId:
                    OpenShop(shopId);
                    break;
            }

            // 走查到此为止：不再自动推进，但页面照常响应按键与点击（截图前注入的 --keys / --click 走真实输入路径）。
            _autoplayFinished = true;
            var side = Game.Rules.Content.Quests.Values.Where(q => q.Kind == QuestKind.Side).Select(q => $"{q.Id} {World.QuestStatusOf(q.Id)}");
            var facts = World.Facts.Where(f => f.Key.Contains("side01") || f.Key.Contains("ferryman") || f.Key.Contains("custody") || f.Key.Contains("helper"));
            GD.Print($"[autoplay] 结束：{(done ? "主线完成" : next is null ? "没有可指向的目标" : "步数用完")}；支线 {string.Join("、", side)}；"
                + $"事实 {string.Join("、", facts.Select(f => $"{f.Key}={f.Value}"))}；"
                + $"地图 {World.MapId}，主线 {World.QuestStatusOf(main.Id)}/{World.Quests[main.Id].Stage}，队伍 {string.Join("、", World.Party.Select(_play.Name))}，"
                + $"银 {World.Silver}，经验 {World.Experience}，线索 {World.Clues.Count}，时辰 {World.Clock}，修订 {World.Revision}");
            if (DevCapture.Walk)
            {
                GD.Print($"[walk] 结束：问题 {WalkProblems} 处");
            }

            DevCapture.FinishAutoplay(GetTree(), !(done || next is not null) ? 2 : DevCapture.Walk && WalkProblems > 0 ? 4 : 0);
            return;
        }

        var (key, ground, _, label) = next.Value;
        DevCapture.AutoplaySteps++;
        GD.Print($"[autoplay] 第 {DevCapture.AutoplaySteps} 步：{World.MapId} → {key}（{label}）");
        if (DevCapture.Walk && BeginWalk(key, ground))
        {
            return;
        }

        _view.PlaceHero(ground + new Vector2(60, 60));
        if (key.StartsWith("route:", StringComparison.Ordinal))
        {
            var route = Game.Rules.Content.Routes[key["route:".Length..]];
            var mode = route.Modes.First(m => Game.Rules.Check(m.When, World) && World.Silver >= m.Silver);
            Depart(route, mode.Mode);
            return;
        }

        Act(key);
    }

    // ── IExploreDriver ────────────────────────────────────

    public (Vector2 Hero, Vector2 Back, float Zoom) Start
    {
        get
        {
            var spawn = _staging.Spawns.TryGetValue(World.SpawnId, out var s) ? s : _staging.Spawns.Values.First();
            return (spawn.At, spawn.Back, 1f);
        }
    }

    public string Region => _staging.Region;

    public IReadOnlyList<FollowerLook> Followers => [.. World.Party.Skip(1).Select(Looks.Of)];

    public IReadOnlyList<ExploreActor> Actors
    {
        get
        {
            var actors = new List<ExploreActor>();
            foreach (var e in Game.Events)
            {
                if (e.Anchor is not { } anchor || Point("anchor:" + anchor) is not { } at)
                {
                    continue;
                }

                var i = 0;
                foreach (var who in e.Participants)
                {
                    if (World.Party.Contains(who) || _staging.Residents.Contains(who) || actors.Any(a => a.Look == Looks.Of(who)))
                    {
                        continue;
                    }

                    if (_staging.Stand.TryGetValue(who, out var stand))
                    {
                        actors.Add(new ExploreActor(Looks.Of(who), stand.At, stand.Facing));
                        continue;
                    }

                    var offset = MapStaging.AroundAnchor[i++ % MapStaging.AroundAnchor.Length];
                    actors.Add(new ExploreActor(Looks.Of(who), at + offset, offset.X > 0 ? -1 : 1));
                }

                if (_staging.Extras.TryGetValue(e.Id, out var extras))
                {
                    actors.AddRange(extras.Select(x => new ExploreActor(x.Look, x.At, x.Facing)));
                }
            }

            return actors;
        }
    }

    public IReadOnlyList<TownInteraction> Interactions
    {
        get
        {
            var list = new List<TownInteraction>();
            foreach (var item in Game.Interactables)
            {
                var verb = item.Kind switch
                {
                    InteractableKind.Pickup => "拾取", InteractableKind.Talk => "交谈", InteractableKind.Shop => "买卖", _ => "查看",
                };
                Add(list, "interact:" + item.Id, MarkerItem, verb, _play.Text($"{Map.Id}.{item.Id}.name") ?? item.Id);
            }

            foreach (var e in Game.Events.Where(e => !e.Auto && e.Anchor is not null))
            {
                Add(list, "event:" + e.Id, MarkerEvent, _play.Text(e.Id + ".verb") ?? "交谈", _play.Text(e.Id + ".name") ?? e.Id, "anchor:" + e.Anchor);
            }

            foreach (var x in Map.Exits)
            {
                Add(list, "exit:" + x.Id, MarkerExit, "前往", _play.Name(x.To));
            }

            foreach (var r in Game.Rules.Content.Routes.Values.Where(r => r.From == World.MapId))
            {
                var verb = r.Modes.Any(m => m.Mode == TravelMode.Ferry) ? "乘船" : "启程";
                Add(list, "route:" + r.Id, MarkerItem, verb, "去" + _play.Name(r.To));
            }

            return list;
        }
    }

    private void Add(List<TownInteraction> list, string id, float height, string verb, string target, string? pointKey = null)
    {
        if (Point(pointKey ?? id) is { } at)
        {
            list.Add(new TownInteraction(id, at, height, verb, target, "", "", ""));
        }
    }

    /// <summary>摆放表里的位置；内容里有而摆放表缺的，记一条警告并放到主角落点旁，保证仍可交互。</summary>
    private Vector2? Point(string key)
    {
        if (_staging.Points.TryGetValue(key, out var at))
        {
            return at;
        }

        GD.PushWarning($"{World.MapId}：摆放表缺少 {key} 的位置，暂放在落点旁");
        return Start.Hero + new Vector2(120, 0);
    }

    public (Vector2 Ground, float Height, string Label)? Goal => _goal is { } g ? (g.Ground, g.Height, g.Label) : null;

    /// <summary>借景说明只在开发信息打开时显示（F12 / --dev），玩家看不到。</summary>
    public string Caption => AppHost.DevInfo ? _staging.Caption : "";

    public bool InputLocked => _dialogue is not null || _modal is not null || _leaving;

    public void NearChanged(TownInteraction? item)
    {
    }

    public void Interact(TownInteraction item) => Act(item.Id);

    /// <summary>执行一个交互键：<c>interact:</c> 交互物、<c>event:</c> 事件、<c>exit:</c> 出口、<c>route:</c> 路线面板。</summary>
    private void Act(string key)
    {
        var (kind, id) = key.Split(':', 2) switch { [var k, var v] => (k, v), _ => ("", key) };
        try
        {
            switch (kind)
            {
                case "interact" when Game.Interactables.FirstOrDefault(i => i.Id == id) is { Kind: InteractableKind.Shop, Shop: { } shop }:
                    OpenShop(shop);
                    break;
                case "interact":
                    AppHost.Instance.Sound.Play("interact", -6, 0.05f);
                    var dialogue = Game.Interact(id, out var result);
                    if (dialogue is not null)
                    {
                        ShowDialogue(dialogue);
                    }
                    else
                    {
                        Handle(result);
                    }

                    break;
                case "event":
                    Open(() => Game.StartEvent(id));
                    break;
                case "exit":
                    var exit = Map.Exits.First(x => x.Id == id);
                    if (!Game.Rules.Check(exit.When, World))
                    {
                        _view.Toast("提示", exit.LockedHint is { } h ? _play.Text(h) ?? h : "此路暂时不通", "");
                        break;
                    }

                    Go(Game.BeginExit(id));
                    break;
                case "route":
                    var route = Game.Rules.Content.Routes[id];
                    if (!Game.Rules.Check(route.When, World))
                    {
                        _view.Toast("提示", route.LockedHint is { } hint ? _play.Text(hint) ?? hint : "此路暂时不通", "");
                        break;
                    }

                    OpenWorldMap(Game.Rules.Content.NodeOf(route.To)?.Id);
                    break;
            }
        }
        catch (InvalidOperationException ex)
        {
            _view.Toast("提示", ex.Message, "");
        }
    }

    // ── 对话 ─────────────────────────────────────────────

    private void Open(Func<DialogueSession> start)
    {
        try
        {
            ShowDialogue(start());
        }
        catch (InvalidOperationException ex)
        {
            _view.Toast("提示", ex.Message, "");
        }
    }

    private void ShowDialogue(DialogueSession session)
    {
        _dialogue = new DialogueOverlay(_play, session, Finish);
        _hudLayer.Visible = false;
        _view.HudVisible = false;
        _view.ToastsPaused = true;
        _overlay.AddChild(_dialogue);
    }

    private void Finish(DialogueSession session)
    {
        var r = Game.FinishDialogue(session);
        _dialogue?.QueueFree();
        _dialogue = null;
        _hudLayer.Visible = true;
        _view.HudVisible = true;
        _view.ToastsPaused = false;
        Handle(r);
    }

    /// <summary>处理一次提交：显示提示；有战斗请求去战斗页，有剧情换图开票据，否则刷新布景与 HUD，再看有没有接着自动开始的事件。</summary>
    private void Handle(CommitResult r)
    {
        if (!r.Ok)
        {
            GD.PushWarning($"事务未提交：{r.Error}");
            _view.Toast("提示", $"未能完成：{r.Error}", "");
            Refresh();
            return;
        }

        var notes = Describe(r).ToList();
        if (World.Battle is not null)
        {
            _play.PendingToasts.AddRange(notes);
            ToBattle();
            return;
        }

        if (r.Travel is { } travel)
        {
            _play.PendingToasts.AddRange(notes);
            Go(Game.BeginStoryTravel(travel));
            return;
        }

        Toasts(notes);
        Refresh();
        var main = Game.Rules.Content.Quests.Values.FirstOrDefault(q => q.Kind == QuestKind.Main);
        if (main is not null && r.Notices.Any(n => n.Kind == "quest_completed" && n.Id == main.Id))
        {
            // 主线完成：章终回顾。
            ShowModal(ChapterEnd.Build(_play, CloseModal), 1240, 820);
            return;
        }

        CallDeferred(MethodName.Resume);
    }

    /// <summary>推一批通知，并按其中最要紧的一类响一声（任务 > 武学 > 物品 > 线索）。</summary>
    private void Toasts(IReadOnlyCollection<(string Kind, string Text)> notes)
    {
        foreach (var (kind, text) in notes)
        {
            _view.Toast(kind, text, "");
        }

        if (notes.Count == 0)
        {
            return;
        }

        var sound = notes.Select(n => n.Kind switch
        {
            "任务" => (3, "notify.quest"),
            "武学" or "成长" => (2, "notify.skill"),
            "物品" or "银两" => (1, "notify.item"),
            "线索" or "目标" => (0, "notify.clue"),
            _ => (-1, ""),
        }).MaxBy(x => x.Item1);
        if (sound.Item1 >= 0)
        {
            AppHost.Instance.Sound.Play(sound.Item2, -3);
        }
    }

    private IEnumerable<(string Kind, string Text)> Describe(CommitResult r)
    {
        foreach (var n in r.Notices)
        {
            if (n.Kind == "quest_updated")
            {
                if (World.Quests.TryGetValue(n.Id, out var q) && q.Status == QuestStatus.Active && q.Stage is { } stage)
                {
                    yield return ("目标", _play.Text($"{n.Id}.stage.{stage}") ?? stage);
                }

                continue;
            }

            if (_play.Describe(n) is { } d)
            {
                yield return d;
            }
        }
    }

    private void Refresh()
    {
        _goal = ComputeGoal();
        _view.RefreshInteractions();
        _view.RefreshActors();
        var party = string.Join(",", World.Party);
        if (party != _partyKey)
        {
            _partyKey = party;
            _view.RefreshFollowers();
        }

        RebuildHud();
    }

    private void RebuildHud()
    {
        _hud?.QueueFree();
        _hud = PlayHud.Build(_play);
        _hudLayer.AddChild(_hud);
        _hudRevision = World.Revision;
    }

    // ── 换图与战斗 ───────────────────────────────────────

    /// <summary>开了切换票据：重新载入探索页，载入后提交；场景缺失则中止，世界不变。</summary>
    private void Go(Transition t)
    {
        if (!SceneRouter.CanGoTo(ScenePaths.Exploration))
        {
            Game.AbortTransition(t);
            _view.Toast("提示", "目的地场景缺失，已取消出发", "");
            return;
        }

        _leaving = true;
        _play.Arriving = t;
        AppHost.Instance.Router.GoTo(ScenePaths.Exploration);
    }

    private void ToBattle()
    {
        _leaving = true;
        AppHost.Instance.Router.GoTo(ScenePaths.BattlePrototype);
    }

    /// <summary>
    /// 江湖大地图（M 键，或码头乘船点选中目的地打开）：全屏盖在探索页上，选地标、方式后启程；
    /// 行进动画走完交回切换票据，按换图流程载入目的地。场景缺失时票据中止、地图关闭，世界不变。
    /// </summary>
    private void OpenWorldMap(string? target)
    {
        CloseModal();
        _worldMap = new WorldMapOverlay(_play, target, CloseModal, t =>
        {
            Go(t);
            if (!_leaving)
            {
                CloseModal();
            }
        });
        _overlay.AddChild(_worldMap);
        _modal = _worldMap;
        AppHost.Instance.Sound.Play("ui.page", -4);
    }

    /// <summary>自动走查（非真实行走）直接按路线的第一种可用方式启程，不经大地图。</summary>
    private void Depart(RouteDefinition route, TravelMode mode)
    {
        AppHost.Instance.Sound.Play(mode == TravelMode.Ferry ? "travel.oar" : "travel.whoosh", -2);
        try
        {
            Go(Game.BeginRoute(route.Id, mode));
        }
        catch (InvalidOperationException ex)
        {
            _view.Toast("提示", ex.Message, "");
        }
    }

    // ── 菜单、札记、存读档 ───────────────────────────────

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_play is null || _leaving || @event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        if (_modal is not null)
        {
            if (key.Keycode == Key.Escape)
            {
                CloseModal();
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (_dialogue is not null)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.J:
                OpenJournal();
                break;
            case Key.C:
                OpenCharacter(0);
                break;
            case Key.I:
                OpenInventory();
                break;
            case Key.P:
                OpenParty();
                break;
            case Key.M:
                OpenWorldMap(null);
                break;
            case Key.F5:
                var r = _play.Save(SaveSlot.Quick, SaveThumbnail.Grab(GetViewport()));
                _view.Toast("存档", r.Ok ? "已快速存档" : $"存档失败：{r.Error}", "");
                break;
            case Key.F9:
                GameMenu.LoadInto(SaveSlot.Quick, ShowModalMessage);
                break;
            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

    private void OpenJournal()
    {
        ShowModal(Journal.Build(_play), 1500, 860);
        AppHost.Instance.Sound.Play("ui.page", -4);
    }

    /// <summary>人物页（C）：在探索中可分配潜能、换装备、调整装配与修炼。</summary>
    private void OpenCharacter(int tab)
    {
        ShowModal(CharacterPage.Build(_play, tab), PageWidth, PageHeight);
        AppHost.Instance.Sound.Play("ui.page", -4);
    }

    private void OpenInventory()
    {
        ShowModal(InventoryPage.Build(_play), PageWidth, PageHeight);
        AppHost.Instance.Sound.Play("ui.page", -4);
    }

    /// <summary>队伍页（P）：调换阵位，查看同行人物。</summary>
    private void OpenParty()
    {
        ShowModal(PartyPage.Build(_play), PageWidth, PageHeight);
        AppHost.Instance.Sound.Play("ui.page", -4);
    }

    private void OpenShop(string shopId)
    {
        ShowModal(ShopPanel.Build(_play, shopId), PageWidth, PageHeight);
    }

    /// <summary>人物、行囊、队伍与店铺页的面板尺寸（1920×1080 画布）。</summary>
    public const float PageWidth = 1720;

    public const float PageHeight = 960;

    private void ShowModalMessage(string text) => _view.Toast("提示", text, "");

    private void ShowModal(Control content, float width, float height)
    {
        CloseModal();
        var layer = new Control();
        layer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var veil = new ColorRect { Color = UiPalette.Abyss with { A = 0.66f } };
        veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        veil.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
            {
                CloseModal();
            }
        };
        layer.AddChild(veil);
        var panel = new PanelContainer { ThemeTypeVariation = UiTheme.DarkPanel };
        panel.AddChild(content);
        layer.AddChild(Ui.Place(panel, 0.5f, 0.5f, -width / 2, -height / 2, width / 2, height / 2));
        Motion.Enter(panel, 0, Motion.Normal, rise: 20);
        _overlay.AddChild(layer);
        _modal = layer;
        AppHost.Instance.Sound.Play("ui.open", -6);
    }

    private void CloseModal()
    {
        _worldMap = null;
        if (_modal is { } layer)
        {
            AppHost.Instance.Sound.Play("ui.close", -8);
            _modal = null;
            layer.QueueFree();
            RebuildHud();
        }
    }

    /// <summary>
    /// 换图与战斗后的自动存档在进图时写下，那时新画面还没画出、色幕还盖着：等色幕收起、进图过场对话与弹层都结束、
    /// 再画两帧，抓当前探索画面补作缩略图。中途离开（读档、回标题）则不补，卡片退回程序化山水。
    /// </summary>
    private void FlushThumbnails()
    {
        if (_play is not { AwaitingThumbnail: true } || _leaving || _dialogue is not null || _modal is not null || AppHost.Instance.Router.Busy)
        {
            _thumbnailFrames = 0;
            return;
        }

        if (++_thumbnailFrames >= 3 && SaveThumbnail.Grab(GetViewport()) is { } jpeg)
        {
            _play.FlushThumbnails(jpeg);
        }
    }

    private void AutoSave(List<(string, string)> notes)
    {
        if (_play.AutoSave() is { Ok: false } r)
        {
            notes.Add(("存档", $"自动存档失败：{r.Error}"));
        }
    }

    private void Fatal(string message)
    {
        AddChild(Backdrop.Veiled());
        var panel = Ui.Panel(UiTheme.DarkPanel, Ui.Column(UiPalette.SpaceM,
            Ui.Text("无法进入探索", UiTheme.DarkTitleLabel, 34),
            Ui.Text(message, UiTheme.DarkLabel, 20, wrap: true),
            Ui.KeyHints(true, ("Esc", "返回标题"))));
        AddChild(Ui.Place(panel, 0.5f, 0.5f, -560, -200, 560, 200));
    }

    // ── 目标指向 ─────────────────────────────────────────

    /// <summary>
    /// 主线目标在本图的指向：本图有可开始的主线事件就指向它；目标在别处就指向通往那里的出口或路线（按当前可走的出口与路线找最短路）；
    /// 目标就在本图则指向摆放表里登记的目标物。
    /// </summary>
    private (string Key, Vector2 Ground, float Height, string Label)? ComputeGoal()
    {
        var content = Game.Rules.Content;
        var main = content.Quests.Values.FirstOrDefault(q => q.Kind == QuestKind.Main && World.QuestStatusOf(q.Id) == QuestStatus.Active);
        if (main is null)
        {
            return null;
        }

        var ev = Game.Events.FirstOrDefault(e => !e.Auto && e.Priority == EventPriority.MainUrgent && e.Anchor is not null);
        if (ev is not null && _staging.Points.TryGetValue("anchor:" + ev.Anchor, out var anchor))
        {
            return ("event:" + ev.Id, anchor, MarkerEvent, _play.Text(ev.Id + ".name") ?? ev.Id);
        }

        return QuestTarget(main);
    }

    /// <summary>一条进行中任务当前阶段第一个未完成目标的指向；目标物尚不可交互或路不通时为 null。</summary>
    private (string Key, Vector2 Ground, float Height, string Label)? QuestTarget(QuestDefinition quest)
    {
        var progress = World.Quests[quest.Id];
        if (progress.Stage is not { } stageId || quest.StageById(stageId) is not { } stage)
        {
            return null;
        }

        var objective = stage.Objectives.FirstOrDefault(o => !o.Optional && !progress.Objectives.Contains(o.Id));
        if (objective?.Map is not { } target)
        {
            return null;
        }

        if (target != World.MapId)
        {
            return NextHop(target) is { } hop && _staging.Points.TryGetValue(hop.Key, out var at) ? (hop.Key, at, MarkerExit, hop.Label) : null;
        }

        if (!MapStaging.ObjectiveTargets.TryGetValue(objective.Id, out var key) || !_staging.Points.TryGetValue(key, out var p))
        {
            return null;
        }

        var item = key["interact:".Length..];
        return Game.Interactables.Any(i => i.Id == item) ? (key, p, MarkerItem, _play.Text($"{World.MapId}.{item}.name") ?? objective.Id) : null;
    }

    /// <summary>
    /// 自动走查的下一步：默认跟主线指向；带 <c>--side</c> 时先接本图可开始的地区事件（支线委托），
    /// 再做进行中支线的当前目标，支线暂时走不通时回到主线。
    /// </summary>
    private (string Key, Vector2 Ground, float Height, string Label)? AutoTarget()
    {
        if (!DevCapture.Side)
        {
            return _goal;
        }

        var content = Game.Rules.Content;
        var offer = Game.Events.FirstOrDefault(e => !e.Auto && e.Priority == EventPriority.Regional && e.Anchor is not null
            && content.Quests.Values.Any(q => q.Kind == QuestKind.Side && World.QuestStatusOf(q.Id) == QuestStatus.Available));
        if (offer is not null && _staging.Points.TryGetValue("anchor:" + offer.Anchor, out var at))
        {
            return ("event:" + offer.Id, at, MarkerEvent, _play.Text(offer.Id + ".name") ?? offer.Id);
        }

        foreach (var side in content.Quests.Values.Where(q => q.Kind == QuestKind.Side && World.QuestStatusOf(q.Id) == QuestStatus.Active))
        {
            if (QuestTarget(side) is { } t)
            {
                return t;
            }
        }

        return _goal;
    }

    /// <summary>从当前地图经出口与路线走到目标地图的第一步（广度优先）。</summary>
    private (string Key, string Label)? NextHop(string target)
    {
        var content = Game.Rules.Content;
        IEnumerable<(string Key, string To)> Edges(string map) =>
            content.Maps[map].Exits.Where(x => Game.Rules.Check(x.When, World)).Select(x => ("exit:" + x.Id, x.To))
                .Concat(content.Routes.Values.Where(r => r.From == map && Game.Rules.Check(r.When, World)).Select(r => ("route:" + r.Id, r.To)));

        var first = new Dictionary<string, (string Key, string To)> { [World.MapId] = ("", World.MapId) };
        var queue = new Queue<string>([World.MapId]);
        while (queue.Count > 0)
        {
            var map = queue.Dequeue();
            foreach (var (key, to) in Edges(map))
            {
                if (first.ContainsKey(to) || !content.Maps.ContainsKey(to))
                {
                    continue;
                }

                first[to] = map == World.MapId ? (key, to) : first[map];
                if (to == target)
                {
                    var hop = first[to];
                    return (hop.Key, (hop.Key.StartsWith("route:", StringComparison.Ordinal) ? "乘船去" : "前往") + _play.Name(hop.To));
                }

                queue.Enqueue(to);
            }
        }

        return null;
    }
}
