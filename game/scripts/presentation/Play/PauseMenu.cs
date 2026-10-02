using Godot;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Play;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 暂停菜单层（挂在 <see cref="AppHost"/> 下，跨场景常驻）：一局进行中任意时刻——探索、对话、剧情战、结算页——
/// 按 Esc 打开 <see cref="GameMenu"/>，打开期间整棵场景树暂停（行走、逐字、战斗演出、配音一并停住，配乐与环境声照常），
/// 关闭即恢复。各页先处理自己要取消的东西（关札记 / 乘船面板、取消选目标、关对话记录），没有可取消的才落到这里。
/// 场景切换的色幕走动期间不打开，免得把幕布停在半截。
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    private Control? _layer;
    private PanelContainer? _panel;
    private GameMenu? _menu;

    public PauseMenu()
    {
        // 在场景之上、切换色幕（100）之下；暂停时仍要收输入、跑动效。
        Layer = 90;
        ProcessMode = ProcessModeEnum.Always;
    }

    public bool IsOpen => _layer is not null;

    /// <summary>有一局在进行、色幕没在走动时才能打开。</summary>
    public bool CanOpen => AppHost.Instance.Play is not null && !AppHost.Instance.Router.Busy && !IsOpen;

    /// <param name="slots">直接打开读取槽位页（截图用）。</param>
    public void Open(bool slots = false)
    {
        if (!CanOpen)
        {
            return;
        }

        // CanvasLayer 截断了主题的向上查找，菜单层须自己挂上全局主题（暗色面板、菜单项样式）。
        var layer = new Control { Theme = GetTree().Root.Theme };
        layer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var veil = new ColorRect { Color = UiPalette.Abyss with { A = 0.66f } };
        veil.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        veil.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
            {
                Back();
            }
        };
        layer.AddChild(veil);
        _panel = new PanelContainer { ThemeTypeVariation = UiTheme.DarkPanel };
        layer.AddChild(_panel);
        _menu = GameMenu.Build(AppHost.Instance.Play!, Close, Resize, slots);
        _panel.AddChild(_menu.Root);
        Motion.Enter(_panel, 0, Motion.Normal, rise: 20);
        AddChild(layer);
        _layer = layer;
        GetTree().Paused = true;
        AppHost.Instance.Sound.Play("ui.open", -6);
    }

    public void Close()
    {
        if (_layer is not { } layer)
        {
            return;
        }

        _layer = null;
        _panel = null;
        _menu = null;
        layer.QueueFree();
        GetTree().Paused = false;
        AppHost.Instance.Sound.Play("ui.close", -8);
    }

    private void Resize(float width, float height)
    {
        if (_panel is { } panel)
        {
            Ui.Place(panel, 0.5f, 0.5f, -width / 2, -height / 2, width / 2, height / 2);
        }
    }

    /// <summary>子页回主页，主页关闭菜单。</summary>
    private void Back()
    {
        if (_menu?.Back is { } back)
        {
            back();
        }
        else
        {
            Close();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (IsOpen && @event.IsActionPressed("ui_cancel"))
        {
            Back();
            GetViewport().SetInputAsHandled();
        }
    }
}
