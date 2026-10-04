using Godot;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Play;

/// <summary>
/// 菜单层（挂在 <see cref="AppHost"/> 下，跨场景常驻）：一局进行中任意时刻——探索、对话、剧情战、结算页——
/// 按 Esc 打开暂停页（<see cref="GameMenu"/>）；探索中按人物 / 队伍 / 行囊 / 札记键直接打开分区菜单（<see cref="MenuFrame"/>）。
/// 打开期间整棵场景树暂停（行走、逐字、战斗演出、配音、游戏时长一并停住，配乐与环境声照常），全部关闭即恢复。
/// Esc 逐层返回：确认框 → 存档卡 → 暂停页竖栏 → 关闭；从暂停页进入的分区菜单 Esc 回暂停页，由快捷键直接打开的 Esc 关闭。
/// 各页先处理自己要取消的东西（关乘船面板、取消选目标、关对话记录），没有可取消的才落到这里。
/// 场景切换的色幕走动期间不打开，免得把幕布停在半截。
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    private Control? _layer;
    private GameMenu? _pause;
    private MenuFrame? _frame;
    private Texture2D? _scene;
    private long _revision;

    public PauseMenu()
    {
        // 在场景之上、切换色幕（100）之下；暂停时仍要收输入、跑动效。
        Layer = 90;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        AppHost.Instance.ThemeRebuilt += () =>
        {
            if (_layer is not null)
            {
                _layer.Theme = GetTree().Root.Theme;
            }
        };
        KeyBindings.Changed += () => _frame?.RefreshHeader();
    }

    public bool IsOpen => _layer is not null;

    /// <summary>分区菜单开着（快捷键打开或从暂停页进入）。</summary>
    public bool FrameOpen => _frame is not null;

    /// <summary>当前分区菜单（焦点检查用）。</summary>
    public MenuFrame? Frame => _frame;

    /// <summary>有一局在进行、色幕没在走动时才能打开。</summary>
    public bool CanOpen => AppHost.Instance.Play is not null && !AppHost.Instance.Router.Busy && !IsOpen;

    /// <summary>打开暂停页。</summary>
    /// <param name="slots">直接打开读取槽位（截图用）。</param>
    public void Open(bool slots = false)
    {
        if (!Begin())
        {
            return;
        }

        ShowPause(slots);
        AppHost.Instance.Sound.Play("ui.open", -6);
    }

    /// <summary>直接打开分区菜单（探索中的人物 / 队伍 / 行囊 / 札记键）；Esc 或再按同一键关闭。</summary>
    /// <param name="tab">分区内的子页签（人物页 0 属性、1 武学、2 装备）；null 为默认。</param>
    public void OpenSection(MenuSection section, int? tab = null)
    {
        if (!Begin())
        {
            return;
        }

        ShowFrame(section, fromPause: false, tab);
        AppHost.Instance.Sound.Play("ui.page", -4);
    }

    private bool Begin()
    {
        if (!CanOpen)
        {
            return false;
        }

        // 视口里还是上一帧的游戏画面（菜单尚未画出）：抓下来，菜单里手动保存时作缩略图，并缩成虚化底。
        AppHost.Instance.Play!.MenuFrame = SaveThumbnail.Grab(GetViewport());
        _scene = MenuFrame.Blurred(GetViewport());

        // CanvasLayer 截断了主题的向上查找，菜单层须自己挂上全局主题（暗色面板、菜单项样式）。
        var layer = new Control { Theme = GetTree().Root.Theme };
        layer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
            {
                Back();
            }
        };
        AddChild(layer);
        _layer = layer;
        _revision = AppHost.Instance.Play.Game.World.Revision;
        GetTree().Paused = true;
        return true;
    }

    private void ShowPause(bool slots = false)
    {
        Clear();
        _pause = GameMenu.Build(AppHost.Instance.Play!, _scene, Close, s => ShowFrame(s, fromPause: true), slots);
        _layer!.AddChild(_pause.Root);
    }

    private void ShowFrame(MenuSection section, bool fromPause, int? tab = null)
    {
        var pause = fromPause ? _pause : null;
        if (pause is not null)
        {
            // 暂停页只隐藏、不离开场景树（离树再进会让全局挂钩重复连接按钮信号），从分区菜单回来时原样显示，焦点回到进入前那一项。
            pause.Root.Visible = false;
        }
        else
        {
            Clear();
        }

        _frame = MenuFrame.Build(AppHost.Instance.Play, section, _scene, fromPause ? "返回菜单" : "返回游戏", tab);
        _frame.Back = fromPause ? BackToPause : Close;
        _layer!.AddChild(_frame.Root);
    }

    private void BackToPause()
    {
        if (_frame is { } frame)
        {
            _layer!.RemoveChild(frame.Root);
            frame.Root.QueueFree();
            _frame = null;
        }

        if (_pause is { } pause)
        {
            pause.Root.Visible = true;
            pause.Refocus();
        }
        else
        {
            ShowPause();
        }

        AppHost.Instance.Sound.Play("ui.close", -8);
    }

    private void Clear()
    {
        _frame = null;
        _pause = null;
        if (_layer is { } layer)
        {
            Ui.Ui.ClearChildren(layer);
        }
    }

    public void Close()
    {
        if (_layer is not { } layer)
        {
            return;
        }

        Clear();
        _layer = null;
        _scene = null;
        layer.QueueFree();
        GetTree().Paused = false;
        AppHost.Instance.Sound.Play("ui.close", -8);
    }

    /// <summary>逐层返回：分区菜单交给它自己的返回动作；暂停页先收确认框与存档卡，没有可收的就关闭菜单。</summary>
    private void Back()
    {
        if (_frame is { Back: { } frameBack })
        {
            frameBack();
        }
        else if (_pause?.Back is { } back)
        {
            back();
        }
        else
        {
            Close();
        }
    }

    public override void _Process(double delta)
    {
        // 分区菜单里的养成改动（洗点花银、换装）提交后，顶栏的银两跟着刷新。
        if (_frame is { } frame && AppHost.Instance.Play is { } play && play.Game.World.Revision != _revision)
        {
            _revision = play.Game.World.Revision;
            frame.RefreshHeader();
        }
    }

    /// <summary>分区菜单里点右键返回（底栏的“Esc 返回”键帽已删，给鼠标留一个不占画面的返回方式）。</summary>
    public override void _Input(InputEvent @event)
    {
        if (IsOpen && _frame is { Back: { } back } && @event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            GetViewport().SetInputAsHandled();
            back();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen)
        {
            return;
        }

        if (_frame is { } frame)
        {
            if (frame.HandleKey(@event))
            {
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (@event.IsActionPressed("ui_cancel"))
        {
            Back();
            GetViewport().SetInputAsHandled();
        }
    }
}
