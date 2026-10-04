using Godot;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Battle;

using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>
/// 指令区上方的说明小签（2026-10-05 起替代指令区里常驻的说明行）：招式全名、消耗与范围、效果说明。
/// 鼠标停在招式格上时说明该格；换招时说明新招，停留数秒后收起；用物品、换位时一直说明当前所选，直到退出该模式。
/// </summary>
public sealed partial class BattleScreen
{
    private const double InfoHoldSeconds = 2.6;

    private PanelContainer _card = null!;
    private Label _cardTitle = null!;
    private Label _cardMeta = null!;
    private Label _cardDesc = null!;

    /// <summary>当前模式（换招、物品、换位）给出的说明；Sticky 为真时不自动收起。</summary>
    private (string Title, string? Meta, string? Desc, string? Skill, bool Sticky)? _modeInfo;

    private double _modeInfoUntil;
    private (Button Slot, string Skill)? _hoverSlot;

    private void BuildInfoCard()
    {
        _card = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _card.AddThemeStyleboxOverride("panel", new OrnateBox
        {
            FillA = UiPalette.Abyss with { A = 0.95f }, FillB = UiPalette.PanelDark with { A = 0.95f }, Ragged = 1.2f, Seed = 83,
            Border = UiPalette.Gilt with { A = 0.5f }, BorderWidth = 1.2f, Brush = true,
        }.Margins(14, 10));
        _cardTitle = Ui.Text("", UiTheme.GiltLabel, 18);
        _cardMeta = Ui.Text("", UiTheme.DarkMutedLabel, 14);
        _cardDesc = Ui.Text("", UiTheme.DarkLabel, 15, wrap: true);
        _cardDesc.CustomMinimumSize = new Vector2(420, 0);
        _card.AddChild(Ui.Column(3, _cardTitle, _cardMeta, _cardDesc));
        AddChild(_card);
    }

    /// <summary>换招 / 物品 / 换位时设定说明；skill 为说明所指的招式格（小签对准它）。</summary>
    private void SetInfo(string title, string? meta, string? desc, string? skill, bool sticky)
    {
        _modeInfo = (title, meta, desc, skill, sticky);
        _modeInfoUntil = Time.GetTicksMsec() / 1000.0 + InfoHoldSeconds;
        UpdateInfoCard();
    }

    private void ClearInfo()
    {
        _modeInfo = null;
        _hoverSlot = null;
        UpdateInfoCard();
    }

    private void HoverSlot(Button? slot, string? skill)
    {
        _hoverSlot = slot is not null && skill is not null ? (slot, skill) : null;
        UpdateInfoCard();
    }

    /// <summary>每帧调用：换招说明到时收起；显示中随招式格排版（新卡面要到下一帧才排好）对准位置。</summary>
    private void StepInfoCard()
    {
        if (_card.Visible)
        {
            UpdateInfoCard();
        }
    }

    private void UpdateInfoCard()
    {
        (string Title, string? Meta, string? Desc, string? Skill)? show = null;
        if (_hoverSlot is { } hover && IsInstanceValid(hover.Slot) && SkillInfo(hover.Skill) is { } info)
        {
            show = (info.Title, info.Meta, info.Desc, hover.Skill);
        }
        else if (_modeInfo is { } mode && (mode.Sticky || Time.GetTicksMsec() / 1000.0 <= _modeInfoUntil))
        {
            show = (mode.Title, mode.Meta, mode.Desc, mode.Skill);
        }

        if (show is not { } s)
        {
            _card.Visible = false;
            return;
        }

        _cardTitle.Text = s.Title;
        _cardMeta.Text = s.Meta ?? "";
        _cardMeta.Visible = !string.IsNullOrEmpty(s.Meta);
        _cardDesc.Text = s.Desc ?? "";
        _cardDesc.Visible = !string.IsNullOrEmpty(s.Desc);
        _card.Visible = true;
        _card.ResetSize();

        // 对准所指的招式格，没有就对准招式栏中间；底边贴在指令区上沿之上。
        var anchor = _slots.GetGlobalRect();
        if (s.Skill is { } skill && _slots.GetChildren().OfType<Button>().FirstOrDefault(b => b.HasMeta("skill") && b.GetMeta("skill").AsString() == skill) is { } slot)
        {
            anchor = slot.GetGlobalRect();
        }

        var toLocal = GetGlobalTransform().AffineInverse();
        var center = toLocal * anchor.GetCenter();
        var top = (toLocal * _dock.GetGlobalRect().Position).Y;
        var size = _card.Size;
        _card.Position = new Vector2(Math.Clamp(center.X - size.X / 2, 16, Size.X - size.X - 16), top - size.Y - 8);
    }

    /// <summary>某招的说明：全名、消耗与范围（不能用时附原因）、效果。</summary>
    private (string Title, string Meta, string? Desc)? SkillInfo(string skill)
    {
        if (_session?.AwaitingPlayer is not { } actor)
        {
            return null;
        }

        var def = _engine.Content.Skill(skill);
        var meta = $"{CostText(def)}　·　{RuleText(def.TargetRule)}";
        if (Usable(actor, skill) is { } reason)
        {
            var cooldown = actor.CooldownOf(skill);
            meta += cooldown > 0 ? $"　·　冷却中（{cooldown}）" : $"　·　{reason.TrimEnd('。')}";
        }

        return (_bundle.Name(skill), meta, _bundle.Describe(skill));
    }
}
