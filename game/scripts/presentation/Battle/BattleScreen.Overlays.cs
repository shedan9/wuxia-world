using Godot;
using WuxiaWorld.Application.Combat;
using WuxiaWorld.Domain.Combat;
using Side = WuxiaWorld.Domain.Combat.Side;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Battle;

// 本命名空间的外层有同名命名空间 Presentation.Ui，别名写在命名空间内才能指到控件工厂类。
using Ui = WuxiaWorld.Game.Presentation.Ui.Ui;

/// <summary>开战配置与结算弹层。</summary>
public sealed partial class BattleScreen
{
    private bool _setupOpen;
    private bool _resultOpen;

    private void CloseOverlay()
    {
        Ui.ClearChildren(_overlay);
        _overlay.MouseFilter = MouseFilterEnum.Ignore;
        _setupOpen = false;
        _resultOpen = false;
    }

    private Control Veil()
    {
        var veil = new ColorRect { Color = UiPalette.Abyss with { A = 0.72f } };
        veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _overlay.AddChild(veil);
        _overlay.MouseFilter = MouseFilterEnum.Stop;
        Motion.FadeIn(veil, Motion.Quick);
        return veil;
    }

    // ── 开战配置 ─────────────────────────────────────────

    private void ShowSetup()
    {
        CloseOverlay();
        _setupOpen = true;
        Veil();

        var encounters = Ui.Row(UiPalette.SpaceM);
        var encounterGroup = new ButtonGroup();
        for (var i = 0; i < EncounterIds.Length; i++)
        {
            var index = i;
            var id = EncounterIds[i];
            var locked = _engine.Content.Encounter(id).Locked;
            var note = locked ? "首领机制战：水门开闸、蓄力预兆、半血增援；三人同行" : "普通战：押运打手两名、飞钩手一名；可撤退";
            var button = Ui.Toggle($"{_bundle.Name(id)}", UiTheme.ChoiceButton, encounterGroup, () => { _encounter = index; }, i == _encounter);
            button.TooltipText = note;
            button.CustomMinimumSize = new Vector2(300, 56);
            encounters.AddChild(button);
        }

        var builds = Ui.Row(UiPalette.SpaceM);
        var buildGroup = new ButtonGroup();
        var buildNote = Ui.Text(Builds[_build].Note, UiTheme.DarkMutedLabel, 18, wrap: true);
        for (var i = 0; i < Builds.Length; i++)
        {
            var index = i;
            var button = Ui.Toggle(Builds[i].Label, UiTheme.ChoiceButton, buildGroup, () =>
            {
                _build = index;
                buildNote.Text = Builds[index].Note;
            }, i == _build);
            button.CustomMinimumSize = new Vector2(200, 56);
            builds.AddChild(button);
        }

        var start = Ui.Button("开战", UiTheme.PrimaryButton, () => StartBattle(_encounter, _build, _seed));
        start.CustomMinimumSize = new Vector2(220, 56);

        var panel = new PanelContainer { ThemeTypeVariation = UiTheme.DarkPanel };
        panel.AddChild(Ui.Column(UiPalette.SpaceL,
            Ui.Row(UiPalette.SpaceL, Ui.Seal("试剑"), Ui.Column(4,
                Ui.Text("战斗原型（M1）", UiTheme.DarkTitleLabel, 40),
                Ui.Text("规则、招式与敌人来自内容包；主角三套流派的成长预算相同（5 级、12 点潜能、同等装备）。", UiTheme.DarkMutedLabel, 18))),
            Ui.Rule(dark: true),
            Ui.Section("遭遇（← / →）", dark: true), encounters,
            Ui.Section("主角流派（Q / E）", dark: true), builds, buildNote,
            Ui.Text("同行：陆青禾（长篙）；旧渡水门另有同行者（占位，经典人物援手在人物锚点核验后接入）。", UiTheme.DarkMutedLabel, 17, wrap: true),
            Ui.Rule(dark: true),
            Ui.Row(UiPalette.SpaceL, Ui.Text($"随机种子 {_seed}", UiTheme.DarkMutedLabel, 16), Ui.Spacer(),
                Ui.KeyHints(true, ("Enter", "开战"), ("Esc", "返回标题")), start)));
        _overlay.AddChild(Ui.Place(panel, 0.5f, 0.5f, -720, -330, 720, 330));
        Motion.Enter(panel, 0.05f, Motion.Normal, rise: 24);
        start.CallDeferred(Control.MethodName.GrabFocus);
    }

    private bool SetupKey(Key key)
    {
        switch (key)
        {
            case Key.Left:
                _encounter = (_encounter + EncounterIds.Length - 1) % EncounterIds.Length;
                ShowSetup();
                return true;
            case Key.Right:
                _encounter = (_encounter + 1) % EncounterIds.Length;
                ShowSetup();
                return true;
            case Key.Q:
                _build = (_build + Builds.Length - 1) % Builds.Length;
                ShowSetup();
                return true;
            case Key.E:
                _build = (_build + 1) % Builds.Length;
                ShowSetup();
                return true;
            case Key.Enter or Key.KpEnter:
                StartBattle(_encounter, _build, _seed);
                return true;
            default:
                return false;
        }
    }

    // ── 结算 ─────────────────────────────────────────────

    private void ShowResult()
    {
        var session = _session!;
        _resultOpen = true;
        HideTargeting();
        Veil();

        var state = session.State;
        var (title, sub) = state.Outcome switch
        {
            BattleOutcome.Victory => (session.Record.Setup.EncounterId.EndsWith("sluice", StringComparison.Ordinal) ? "旧渡解围" : "击退押运队", "战斗胜利"),
            BattleOutcome.Defeat => ("力战不支", "我方全员失去战斗能力（原型不写存档，可同种子重试）"),
            _ => ("全身而退", "撤退成功"),
        };
        var heading = Ui.Text(title, UiTheme.DisplayLabel, 88);
        heading.AddThemeColorOverride("font_color", UiPalette.TextOnDark);
        heading.AddThemeColorOverride("font_outline_color", (state.Outcome == BattleOutcome.Victory ? UiPalette.Accent : UiPalette.Warm) with { A = 0.6f });
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        var subtitle = Ui.Text($"{sub}　·　第 {state.Round} 轮　·　{session.Record.Entries.Count} 条命令", UiTheme.GiltLabel, 24);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;

        var party = Ui.Column(UiPalette.SpaceS, Ui.Section("我方", dark: true));
        foreach (var unit in state.Units.Where(u => u.Side == Side.Ally))
        {
            party.AddChild(Line(DisplayName(unit.Id), unit.IsDown ? "失去战斗能力" : $"气血 {unit.Hp} / {unit.Stats.MaxHp}", $"内力 {unit.Inner} / {unit.Stats.MaxInner}"));
        }

        var encounter = _engine.Content.Encounter(state.EncounterId);
        var replay = BattleSession.Replay(_engine, session.Record);
        var record = Ui.Column(UiPalette.SpaceS, Ui.Section("战斗记录", dark: true),
            Line("随机种子", $"{session.Record.Setup.Seed}", ""),
            Line("内容版本", session.Record.ContentVersion, $"规则版本 {session.Record.RulesetVersion}"),
            Line("终局哈希", session.Record.FinalHash, ""),
            Line("重放校验", replay is null ? "一致" : $"第 {replay} 条命令不一致", "同一输入重算一遍比对逐条哈希"),
            Line("所得", state.Outcome == BattleOutcome.Victory ? $"经验 {encounter.Experience}" : "—", "奖励与成长在 M2 的应用事务中一次性提交"));

        var panel = new PanelContainer { ThemeTypeVariation = UiTheme.DarkPanel };
        panel.AddChild(Ui.Column(UiPalette.SpaceL, heading, subtitle, Ui.Rule(dark: true),
            Ui.Row(UiPalette.SpaceXxl, Ui.Expand(party), Ui.Expand(record)),
            Ui.Rule(dark: true),
            Ui.Row(UiPalette.SpaceM, Ui.Spacer(), Ui.KeyHints(true, ("R", "同种子重来"), ("N", "换种子再战"), ("B", "重新配置"), ("Esc", "返回标题")))));
        _overlay.AddChild(Ui.Place(panel, 0.5f, 0.5f, -800, -330, 800, 330));
        Motion.Enter(panel, 0.1f, Motion.Slow, rise: 30);
    }

    private static Control Line(string label, string value, string note) =>
        Ui.Row(UiPalette.SpaceL, Ui.MinSize(Ui.Text(label, UiTheme.DarkMutedLabel, 19), 96), Ui.MinSize(Ui.Text(value, UiTheme.DarkLabel, 20), 250),
            Ui.Text(note, UiTheme.DarkMutedLabel, 16));

    private bool ResultKey(Key key)
    {
        switch (key)
        {
            case Key.R:
                Restart(newSeed: false);
                return true;
            case Key.N:
                Restart(newSeed: true);
                return true;
            case Key.B:
                ShowSetup();
                return true;
            default:
                return false;
        }
    }
}
