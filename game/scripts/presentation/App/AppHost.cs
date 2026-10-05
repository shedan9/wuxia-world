using Godot;
using WuxiaWorld.Game.Presentation.Audio;
using WuxiaWorld.Game.Presentation.Play;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.App;

/// <summary>
/// 应用宿主，唯一的全局持久节点（架构文档 5.1）。持有场景路由；
/// 后续的音频管理和启动装配也挂在这里，不为各模块另建单例。
/// </summary>
public partial class AppHost : Node
{
    public static AppHost Instance { get; private set; } = null!;

    public SceneRouter Router { get; private set; } = null!;

    /// <summary>台词配音播放（音频管理挂在宿主下，不另设单例）。</summary>
    public VoicePlayer Voice { get; private set; } = null!;

    /// <summary>配乐、环境声与音效。</summary>
    public SoundDirector Sound { get; private set; } = null!;

    /// <summary>一局进行中按 Esc 打开的暂停菜单（保存、读取、札记、设置、返回标题、退出游戏）。</summary>
    public PauseMenu Menu { get; private set; } = null!;

    /// <summary>进行中的一局游戏（M2）；标题页、场景目录与 M0 展示页时为 null。</summary>
    public PlaySession? Play { get; set; }

    /// <summary>
    /// 显示开发信息（台词编号与锁稿状态、配音状态、借景说明、战斗种子等）。玩家默认看不到；
    /// 启动参数 <c>--dev</c> 打开，游戏中 F12 切换（各页在下次建立时生效）。
    /// </summary>
    public static bool DevInfo { get; set; }

    public override void _EnterTree()
    {
        Instance = this;
        DevCapture.Parse();
        GetTree().Root.Theme = UiTheme.Build();
        Router = new SceneRouter();
        AddChild(Router);
        Voice = new VoicePlayer();
        AddChild(Voice);
        // 暂停菜单打开时配乐、环境声与界面音效照常（配音随场景树暂停）。
        Sound = new SoundDirector { ProcessMode = ProcessModeEnum.Always };
        AddChild(Sound);
        Menu = new PauseMenu();
        AddChild(Menu);
    }

    public override void _Ready()
    {
        // 总线由 SoundDirector / VoicePlayer 建好后再读设置、套音量；截图模式不改窗口。
        GameSettings.Load();
        if (DevCapture.TextSize is { } size)
        {
            GameSettings.TextSize = Math.Clamp(size, GameSettings.TextSizeMin, GameSettings.TextSizeMax);
            GameSettings.ApplyText();
        }

        if (!Mathf.IsEqualApprox(FontScale.Factor, 1))
        {
            // 主题在读设置之前已按默认字号建好：字号改过的，按设置重建一次。
            RebuildTheme();
        }

        GetTree().NodeAdded += FocusOnHover;
        PerfProbe.Begin(this);
        if ((!GameSettings.Fullscreen || DevCapture.Endure > 0) && DevCapture.Output is null && PerfProbe.Output is null)
        {
            EnterWindowed();
        }
    }

    public override void _Process(double delta)
    {
        // 游戏时长：一局进行中才计；暂停菜单打开时场景树暂停，宿主按默认处理模式随之停住，不计入。
        if (Play is { } play)
        {
            play.PlaySeconds += delta;
        }
    }

    /// <summary>主题按当前字号重建后通知（叠在 CanvasLayer 上、自己挂主题的界面层据此换上新主题）。</summary>
    public event Action? ThemeRebuilt;

    /// <summary>按当前设置（字号）重建全局主题；已打开的页面需各自重建才会用上新字号。</summary>
    public void RebuildTheme()
    {
        GetTree().Root.Theme = UiTheme.Build();
        ThemeRebuilt?.Invoke();
    }

    /// <summary>
    /// 鼠标悬停即取得焦点（UI_DESIGN 第 7 节）：键盘与鼠标共用同一个“当前项”，悬停后按 Enter 与点击是同一项。
    /// 只对可聚焦、未禁用的按钮生效；页签之类不取焦点的控件不受影响。
    /// </summary>
    private static void FocusOnHover(Node node)
    {
        if (node is BaseButton button && !button.HasMeta(HoverMeta))
        {
            button.SetMeta(HoverMeta, true);
            button.MouseEntered += () =>
            {
                if (button.FocusMode == Control.FocusModeEnum.All && !button.Disabled && button.IsVisibleInTree() && !button.HasFocus())
                {
                    button.GrabFocus();
                }
            };
        }
    }

    private const string HoverMeta = "focus_on_hover";

    public override void _Input(InputEvent @event)
    {
        // 游戏默认全屏（跟随显示器分辨率）；Alt+Enter 在全屏与窗口之间切换，先于各页的确认键处理。
        if (@event is InputEventKey { Pressed: true, Echo: false, AltPressed: true } key
            && key.Keycode is Key.Enter or Key.KpEnter)
        {
            var fullscreen = DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;
            SetFullscreen(!fullscreen);

            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F12 })
        {
            DevInfo = !DevInfo;
            GD.Print($"开发信息：{(DevInfo ? "显示" : "隐藏")}");
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// 切到窗口：窗口按所在屏幕可用区域（扣除任务栏）的 80% 宽高取尺寸并居中，不固定像素，4K 与 1080p 屏上观感一致；
    /// 逻辑画布按 expand 适配窗口宽高比。模式切换要过几帧才落定，之后再设尺寸，否则会被还原成切换前记下的窗口尺寸。
    /// </summary>
    /// <summary>切换全屏 / 窗口并记入设置。</summary>
    public void SetFullscreen(bool on)
    {
        GameSettings.Fullscreen = on;
        GameSettings.Save();
        if (on)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        }
        else
        {
            EnterWindowed();
        }
    }

    private async void EnterWindowed()
    {
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        for (var i = 0; i < 3; i++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }

        var area = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
        if (DevCapture.Endure > 0)
        {
            // 长时稳定性测试要跑两小时：窗口缩小放在屏幕右下角，少挡开发者的其他窗口。
            var small = new Vector2I(area.Size.X * 2 / 5, area.Size.X * 2 / 5 * 9 / 16);
            DisplayServer.WindowSetSize(small);
            DisplayServer.WindowSetPosition(area.Position + area.Size - small - new Vector2I(24, 24));
            return;
        }

        var size = new Vector2I((int)(area.Size.X * 0.8f), (int)(area.Size.Y * 0.8f));
        DisplayServer.WindowSetSize(size);
        DisplayServer.WindowSetPosition(area.Position + (area.Size - size) / 2);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel"))
        {
            return;
        }

        // 一局进行中：各页没有要取消的东西时，取消键打开暂停菜单（探索、对话、剧情战、结算页都一样）；
        // 不直接回标题，回标题只经菜单确认，免得误按丢掉进度。
        if (Play is not null)
        {
            Menu.Open();
            GetViewport().SetInputAsHandled();
            return;
        }

        // M0 展示包：任意预览页按取消键都回到标题；标题页自己处理取消键（关闭弹层）。
        if (!Router.IsAt(ScenePaths.MainMenu))
        {
            Router.GoTo(ScenePaths.MainMenu);
            GetViewport().SetInputAsHandled();
        }
    }
}
