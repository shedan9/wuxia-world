using Godot;
using WuxiaWorld.Application.Combat;
using WuxiaWorld.Domain.Combat;
using Side = WuxiaWorld.Domain.Combat.Side;
using WuxiaWorld.Domain.World;
using WuxiaWorld.Game.Adapters;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Audio;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Battle;

using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 战斗喊声（开发计划 M3-06“关键战斗语音”）：<see cref="BattleBarkDirector"/> 按播放到的事件挑句，本页负责说出来——
/// 左上战斗名之下一枚薄墨说话签（说话人印鉴、名字与字幕），日志记一行，有配音时播放。
/// 起初挂在说话人头侧，截图自查发现会压住相邻人物的血条、读成别人说的，也会撞上首领蓄力横幅，改为固定位置。
/// 跳过动画时只记账不说；倍速时只说优先级 3（开战、首领预兆、阶段、胜利），人声不跟着加速。
/// 正在说的一句只被更高优先级的打断；没有配音时字幕按字数停留。
/// </summary>
public sealed partial class BattleScreen
{
    private BattleBarkDirector? _barker;
    private PanelContainer? _barkBubble;
    private double _barkUntil;
    private int _barkPriority;

    private BarkMode BarkMode => _skipping ? BarkMode.Silent : _speed > 1 ? BarkMode.Fast : BarkMode.Normal;

    private void StartBarks()
    {
        AppHost.Instance.Voice.StopBark();
        _barkBubble?.QueueFree();
        _barkBubble = null;
        _barkUntil = 0;
        _barkPriority = 0;
        _barker = GeneratedContent.World is { } world ? new BattleBarkDirector(world.Barks, _session!.State) : null;
        Say(_barker?.Start(BarkMode));
    }

    private void ObserveBark(BattleEvent e)
    {
        if (_barker is not null)
        {
            Say(_barker.Observe(e, BarkMode));
        }
    }

    private void Say(BattleBarkDefinition? bark)
    {
        if (bark is null)
        {
            return;
        }

        var now = Time.GetTicksMsec() / 1000.0;
        if (now < _barkUntil && bark.Priority <= _barkPriority)
        {
            return;
        }

        var name = DisplayName(bark.UnitId);
        var state = AppHost.Instance.Voice.PlayBark(bark.LineId, bark.Text, out var seconds);
        var hold = state == VoiceState.Playing ? seconds + 0.35 : Math.Clamp(0.9 + bark.Text.Length * 0.16, 1.4, 3.2);
        _barkUntil = now + hold;
        _barkPriority = bark.Priority;
        AddLog($"{name}：“{bark.Text}”");
        if (DevCapture.Autoplay > 0 || AppHost.DevInfo)
        {
            GD.Print($"[bark] {bark.LineId}（{bark.Trigger}，优先级 {bark.Priority}，{(state == VoiceState.Playing ? $"配音 {seconds:0.0} 秒" : state.ToString())}）{name}：{bark.Text}");
        }

        ShowBubble(bark, name, hold);
        if (DevCapture.BarkShots is { } dir)
        {
            // 字幕淡入落定后截一张，核对位置与遮挡。
            GetTree().CreateTimer(0.3).Timeout += () =>
            {
                System.IO.Directory.CreateDirectory(dir);
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, bark.LineId + ".png"));
            };
        }
    }

    private void ShowBubble(BattleBarkDefinition bark, string name, double hold)
    {
        _barkBubble?.QueueFree();
        _barkBubble = null;

        // 说话签：固定在左上战斗名之下（远空处，不压人物头顶的血条、招式名、首领蓄力横幅与日志）；
        // 印鉴取说话人名首字，我方石绿、敌方杏红，与行动顺序条的印鉴一致。
        var enemy = _session?.State.TryUnit(bark.UnitId)?.Side == Side.Enemy;
        var seal = Ui.Glyph(name[..1], enemy ? UiPalette.Warm : UiPalette.Trim, 48);
        var nameLabel = Ui.Text(name, UiTheme.GiltLabel, FontScale.Of(UiPalette.FontSecondary));
        var text = Ui.Text(bark.Text, UiTheme.DarkLabel, FontScale.Of(UiPalette.FontBody));
        var bubble = Ui.Panel(UiTheme.GlassPanel, Ui.Row(UiPalette.SpaceM, seal, Ui.Column(0, nameLabel, text)));
        bubble.MouseFilter = MouseFilterEnum.Ignore;
        bubble.Modulate = Colors.Transparent;
        bubble.Position = new Vector2(40, 112);
        AddChild(bubble);
        MoveChild(bubble, _overlay.GetIndex());
        _barkBubble = bubble;

        var tween = bubble.CreateTween();
        if (Motion.Enabled)
        {
            // 淡入与右移同时进行，之后依次停留、淡出（不用 SetParallel：那样停留与淡出也会同时开始）。
            bubble.Position += new Vector2(-12, 0);
            tween.TweenProperty(bubble, "modulate", Colors.White, 0.12f);
            tween.Parallel().TweenProperty(bubble, "position:x", 40f, 0.18f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        }
        else
        {
            bubble.Modulate = Colors.White;
        }

        tween.TweenInterval(hold);
        tween.TweenProperty(bubble, "modulate", Colors.Transparent, Motion.Enabled ? 0.25f : 0f);
        tween.TweenCallback(Callable.From(() =>
        {
            if (_barkBubble == bubble)
            {
                _barkBubble = null;
            }

            bubble.QueueFree();
        }));
    }

    public override void _ExitTree() => AppHost.Instance.Voice.StopBark();
}
