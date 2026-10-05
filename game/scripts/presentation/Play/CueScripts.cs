using Godot;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Presentation.Play;

/// <summary>
/// 第一章演出提示的画面调度（M3，架构文档 9.4.18）：键为“对话 ID/节点 ID”，对应对白文件里 <c>stage</c> 节点的制作说明
/// （<c>content/dialogue/arc01/chapter01.json</c> 的 <c>direction</c>）。属表现层数据，同 <see cref="MapStaging"/> 的摆位一样只给画面，
/// 不改世界状态。位置多用画面坐标（A 向右、D 向下，见 <see cref="RiverSamples.W(float, float)"/>）表达“身侧”“往前”。
/// 标题卡与带文字的特写由对话层照旧显示纸卡，这里只负责镜头。新增演出节点时在此补上；没有脚本的节点照旧拉黑边停一拍。
/// </summary>
public static class CueScripts
{
    private static readonly Dictionary<string, string> Mentors = new()
    {
        ["linghu"] = "char.linghu_chong",
        ["huang"] = "char.huang_rong",
        ["xiao"] = "char.xiao_feng",
    };

    private static Vector2 W(float a, float d) => RiverSamples.W(a, d);

    /// <summary>某处往画面右 da、往下（近镜头）dd 的位置。</summary>
    private static Vector2 Off(Vector2 at, float da, float dd = 0) => at + W(da, dd);

    /// <summary>从 from 看 to 在画面左还是右（1 右、-1 左）。</summary>
    private static int Toward(Vector2 from, Vector2 to) => WildSamples.Frame(to - from).X >= 0 ? 1 : -1;

    private static string? Mentor(CueContext c) => c.Fact("fact.ch01.mentor") is { } m && Mentors.TryGetValue(m, out var id) ? id : null;

    public static readonly IReadOnlyDictionary<string, Action<CueContext, Cue>> All = new Dictionary<string, Action<CueContext, Cue>>
    {
        // ── 芦湾河滩：开场 ───────────────────────────────
        // 雨后河滩，主角伏在泥滩上，黑场淡出时镜头从他身上摇向河面——照片里那座桥的位置空着，只有渡船；摇回来时他撑起身。
        ["dlg.ch01.opening_luwan/cue.001"] = (c, q) => q
            .At(0).Pose("hero", "down").Face("hero", -1)
            .At(1.2f).Pan(W(1300, -150), rate: 1.1f, height: 30, zoom: 0.92f, seconds: 2.4f)
            .At(3.8f).Pan(c.Hero, rate: 1.6f, height: 90, zoom: 1f)
            .At(4.4f).Pose("hero", null).Sound("step.dirt.3", -8)
            .Hold(5.4f),

        // 岸边青石特写：镜头推向刻痕，主角转头看着它。
        ["dlg.ch01.opening_luwan/cue.002"] = (c, q) => q
            .At(0).Face("hero", 1)
            .Pan(c.Point("interact:stone_marks"), rate: 2.2f, height: 40, zoom: 1.3f, seconds: 1.8f)
            .Hold(2.4f),

        ["dlg.ch01.inspect_stone_marks/cue.001"] = (c, q) => q
            .At(0).Face("hero", Toward(c.Hero, c.Point("interact:stone_marks")))
            .Pan(c.Point("interact:stone_marks"), rate: 2.4f, height: 30, zoom: 1.35f, seconds: 1.6f)
            .Hold(2.2f),

        // 芦苇根下的拖痕：先看泥痕，再顺着它一路拉向下游旧渡。
        ["dlg.ch01.side_tide_line/cue.001"] = (c, q) => q
            .At(0).Pan(c.Point("interact:tide_line"), rate: 2.4f, height: 20, zoom: 1.25f, seconds: 1.2f)
            .At(1.4f).Pan(W(3420, 60), rate: 1.2f, height: 40, zoom: 1.05f, seconds: 2f)
            .Hold(3.6f),

        // ── 芦湾街 ──────────────────────────────────────
        ["dlg.ch01.inspect_ferry_notice/cue.001"] = (c, q) => q
            .At(0).Face("hero", Toward(c.Hero, c.Point("interact:ferry_notice")))
            .Pan(c.Point("interact:ferry_notice"), rate: 2.6f, height: 150, zoom: 1.25f),

        ["dlg.ch01.side_ferry_tags/cue.001"] = (c, q) => q
            .At(0).Face("hero", Toward(c.Hero, c.Point("interact:ferry_tags")))
            .Pan(c.Point("interact:ferry_tags"), rate: 2.6f, height: 130, zoom: 1.3f),

        // ── 江南客栈 ────────────────────────────────────
        // 进门：镜头先落到柜台后拨算盘的乔红绡，她把信拍在桌上；主角与陆青禾走进大堂。
        ["dlg.ch01.inn_council/cue.001"] = (c, q) => q
            .At(0).Walk("hero", new Vector2(720, 610), face: 1)
            .Pan(new Vector2(420, 170), rate: 1.8f, height: 110, zoom: 1.05f, seconds: 1.6f)
            .At(1.5f).Sound("cue.table_slap", -4)
            .Hold(2.8f),

        // 镜头扫过三人：靠窗的令狐冲 → 灯下的黄蓉 → 门边的萧峰。
        ["dlg.ch01.inn_council/cue.002"] = (c, q) => q
            .At(0).Pan(c.Stand("char.linghu_chong"), rate: 2.6f, height: 110, zoom: 1.2f, seconds: 1.3f)
            .At(1.4f).Pan(c.Stand("char.huang_rong"), rate: 2.6f, height: 110, zoom: 1.2f, seconds: 1.3f)
            .At(2.8f).Pan(c.Stand("char.xiao_feng"), rate: 2.6f, height: 120, zoom: 1.2f, seconds: 1.3f)
            .Hold(4.2f),

        // 停顿一拍：镜头退开，四人同在画面里，一时无话。
        ["dlg.ch01.inn_council/cue.003"] = (c, q) => q
            .At(0).Face("hero", 1)
            .Pan((c.Hero + c.Stand("char.huang_rong") + c.Stand("char.xiao_feng")) / 3, rate: 1.4f, height: 100, zoom: 1f, seconds: 2f)
            .Hold(2.2f),

        // 讨教：镜头慢慢扫过各占一角的三人，等主角自己走过去。
        ["dlg.ch01.mentor_choice/cue.001"] = (c, q) => q
            .At(0).Pan(c.Stand("char.linghu_chong"), rate: 1.6f, height: 110, zoom: 1.08f, seconds: 1.2f)
            .At(1.2f).Pan(c.Stand("char.xiao_feng"), rate: 1.1f, height: 110, zoom: 1f, seconds: 1.8f)
            .Hold(3f),

        // 时间流逝：黑场里过了半夜，亮回来时主角收势站在原处。
        ["dlg.ch01.mentor_choice/cue.002"] = (c, q) => q
            .At(0).Black(fadeIn: 0.8f, hold: 1.2f, fadeOut: 1f)
            .Hold(3.2f),

        // 切磋：讨教那位侠客见主角走近，转过身来。
        ["dlg.ch01.mentor_spar/cue.001"] = (c, q) =>
        {
            if (Mentor(c) is not { } m)
            {
                return;
            }

            q.At(0).Face(m, Toward(c.At(m), c.Hero)).Face("hero", Toward(c.Hero, c.At(m)))
                .Pan((c.Hero + c.At(m)) / 2, rate: 2f, height: 110, zoom: 1.15f, seconds: 1.4f)
                .Hold(1.8f);
        },

        // 收势：两人各退一步。
        ["dlg.ch01.mentor_spar_after/cue.001"] = (c, q) =>
        {
            if (Mentor(c) is not { } m)
            {
                return;
            }

            var hero = c.Hero;
            var mentor = c.At(m);
            var away = (hero - mentor).Normalized();
            q.At(0).Face(m, Toward(mentor, hero)).Face("hero", Toward(hero, mentor))
                .Pan((hero + mentor) / 2, rate: 2.4f, height: 110, zoom: 1.15f, seconds: 1f)
                .At(0.3f).Walk("hero", hero + away * 50, speed: 140, face: Toward(hero, mentor))
                .Walk(m, mentor - away * 40, speed: 140, face: Toward(mentor, hero))
                .Hold(1.8f);
        },

        ["dlg.ch01.mentor_spar_after/cue.002"] = (c, q) => q
            .At(0).Black(fadeIn: 0.8f, hold: 1.1f, fadeOut: 1f)
            .Hold(3f),

        // ── 芦湾旧渡 ────────────────────────────────────
        // 旧渡全景：押运打扮的汉子守在锁船旁；镜头自栈桥摇过锁船，看一眼远处半开的水门，再落回锁船。
        ["dlg.ch01.old_ferry_arrival/cue.001"] = (c, q) => q
            .At(0).Appear("escort.1", W(1360, 210), 1, fade: 0).Appear("escort.2", W(1560, 150), -1, fade: 0)
            .Appear("escort.3", W(1640, 250), -1, fade: 0)
            .Pan(W(1480, 40), rate: 1.4f, height: 60, zoom: 0.92f, seconds: 1.8f)
            .At(1.9f).Pan(W(2700, 60), rate: 1.3f, height: 120, zoom: 0.92f, seconds: 1.8f)
            .At(3.8f).Pan(W(1300, 150), rate: 1.6f, height: 80, zoom: 1f, seconds: 1.4f)
            .Hold(5.2f),

        // 萧峰先行：他从锁船后面下水，片刻后扶着湿透的杜三篙从船边走回岸上。
        ["dlg.ch01.old_ferry_arrival/cue.002"] = (c, q) => q
            .At(0).Pan(W(1450, 80), rate: 2f, height: 90, zoom: 1.05f, seconds: 1.2f)
            .Appear("char.xiao_feng", W(1440, 40), -1, fade: 0.3f).Appear("char.du_sangao", W(1490, 50), -1, fade: 0.3f)
            .Splash(W(1440, 40), 0.9f).Splash(W(1490, 50), 0.7f).Sound("cue.splash", -6)
            .At(0.5f).Walk("char.xiao_feng", Off(c.Hero, 150, 110), speed: 190, face: -1)
            .Walk("char.du_sangao", Off(c.Hero, 210, 160), speed: 190, face: -1)
            .At(0.8f).Walk("char.lu_qinghe", Off(c.Hero, 110, 190), run: true, face: 1)
            .At(1.4f).Pan(Off(c.Hero, 200, 120), rate: 1.4f, height: 90, zoom: 1.05f, seconds: 1.6f)
            .Hold(3.6f),

        // 守船的汉子发现众人，拔刀围上来，转入战斗。
        ["dlg.ch01.old_ferry_arrival/cue.003"] = (c, q) => q
            .At(0).Sound("battle.draw", -4)
            .Walk("escort.1", Off(c.Hero, 440, 90), run: true, face: -1)
            .Walk("escort.2", Off(c.Hero, 500, 0), run: true, face: -1)
            .Walk("escort.3", Off(c.Hero, 500, 200), run: true, face: -1)
            .Pan(Off(c.Hero, 260, 80), rate: 2.4f, height: 90, zoom: 1.05f, seconds: 1.2f)
            .Hold(1.9f),

        // 押运队仍守在锁船旁，见众人折返，重新拔刀。
        ["dlg.ch01.escort_regroup/cue.001"] = (c, q) => q
            .At(0).Sound("battle.draw", -4)
            .Pan((c.Hero + W(1500, 200)) / 2, rate: 2.2f, height: 90, zoom: 1f, seconds: 1.2f)
            .At(0.3f).Walk("escort.1", Off(c.Hero, 300, 40), run: true, face: -1)
            .Walk("escort.2", Off(c.Hero, 370, -50), run: true, face: -1)
            .Walk("escort.3", Off(c.Hero, 360, 130), run: true, face: -1)
            .Hold(1.8f),

        // 锁船：船头铁链特写（锁眼塞满了泥），舱板底下传来咳嗽。
        ["dlg.ch01.side_locked_boat/cue.001"] = (c, q) => q
            .At(0).Face("hero", 1)
            .Pan(W(1440, 40), rate: 2.4f, height: 40, zoom: 1.35f, seconds: 1.4f)
            .At(1.3f).Sound("cue.cough", -6)
            .Hold(2.6f),

        // 主角撬开舱板，扶出浑身湿透的老渡工。
        ["dlg.ch01.side_locked_boat/cue.002"] = (c, q) => q
            .At(0).Sound("cue.wood_creak", -6)
            .Pan((c.Hero + W(1470, 60)) / 2, rate: 2f, height: 90, zoom: 1.15f, seconds: 1.2f)
            .At(0.6f).Appear("char.du_sangao", W(1480, 50), -1, fade: 0.4f)
            .At(1f).Walk("char.du_sangao", Off(c.Hero, 110, -10), speed: 120, face: -1)
            .Hold(2.6f),

        // 水门对峙：络腮胡的唐守亭提着带钩长刀，从闸桥上沿堤走到石阶，一步步走下来。
        ["dlg.ch01.sluice_confrontation/cue.001"] = (c, q) => q
            .At(0).Pan(W(2700, 60), rate: 2.2f, height: 160, zoom: 1f, seconds: 1f)
            .At(0.6f).Pan(W(2450, 180), rate: 1.1f, height: 140, zoom: 1f, seconds: 1.6f)
            .At(0.4f).Walk("char.tang_shouting", W(2330, 110), speed: 230)
            .After().Walk("char.tang_shouting", W(2330, 390), speed: 200, face: Toward(W(2330, 390), c.Hero))
            .Hold(1),

        // 令狐冲从芦苇里跃出，落在主角身侧，剑尖斜指水门。
        ["dlg.ch01.sluice_confrontation/cue.002"] = (c, q) => q
            .At(0).Pan(Off(c.Hero, -120, -40), rate: 2.6f, height: 120, zoom: 1.05f, seconds: 1f)
            .Sound("cue.reeds_rustle", -6)
            .At(0.2f).Leap("char.linghu_chong", Off(c.Hero, -110, -20), seconds: 0.8f, height: 170, from: W(1990, 60), face: 1)
            .At(1.05f).Pose("char.linghu_chong", "guard")
            .Hold(1.9f),

        // 黄蓉不知何时已坐在水门的石栏上，晃着脚朝主角眨眼（坐姿帧未做，暂以站在闸桥上淡入代替）。
        ["dlg.ch01.sluice_confrontation/cue.003"] = (c, q) => q
            .At(0).Pan(W(2620, 0), rate: 2.2f, height: 180, zoom: 1.05f, seconds: 1.2f)
            .At(0.5f).Appear("char.huang_rong", W(2610, -20), -1, fade: 0.6f)
            .Hold(2.2f),

        // 萧峰大步踏过浅滩，水花溅起老高。
        ["dlg.ch01.sluice_confrontation/cue.004"] = (c, q) => q
            .At(0).Pan(W(2120, 120), rate: 2f, height: 100, zoom: 1f, seconds: 1f)
            .Appear("char.xiao_feng", W(1980, 10), 1, fade: 0.2f)
            .Walk("char.xiao_feng", Off(c.Hero, -110, -10), speed: 260, face: 1)
            .Splash(W(1980, 10), 1.3f).At(0.35f).Splash(W(2060, 50), 1.4f).At(0.7f).Splash(W(2140, 90), 1.2f)
            .At(0.2f).Sound("cue.splash", -3)
            .At(1.2f).Pan(Off(c.Hero, -60, -10), rate: 2f, height: 110, zoom: 1f, seconds: 1f)
            .Hold(2.6f),

        // 战后：水门闸板落下，唐守亭被按在泥地里，长刀滚到一边。杜三篙此时还在舱里（提前救出时已在场）。
        ["dlg.ch01.copy_custody/cue.001"] = (c, q) =>
        {
            q.At(0).Pan(W(2760, 40), rate: 3f, height: 130, zoom: 1f, seconds: 0.8f)
                .At(0.2f).Sound("cue.gate_drop", -2)
                .At(1.1f).Pan(c.Stand("char.tang_shouting", "event.ch01.copy_custody"), rate: 2f, height: 70, zoom: 1.12f, seconds: 1.2f)
                .Pose("char.tang_shouting", "down")
                .Hold(2.6f);
            if (c.Fact("fact.ch01.ferryman_freed_early") != "true")
            {
                q.At(0).Vanish("char.du_sangao", fade: 0);
            }
        },

        // 众人从船舱里拖出半昏的老渡工：陆青禾跑上去扶住他。
        ["dlg.ch01.copy_custody/cue.002"] = (c, q) =>
        {
            var seat = c.Stand("char.du_sangao", "event.ch01.copy_custody");
            q.At(0).Pan(Off(seat, -60, 0), rate: 2f, height: 90, zoom: 1.1f, seconds: 1.2f)
                .Appear("char.du_sangao", Off(seat, -240, 30), 1, fade: 0.4f)
                .At(0.3f).Walk("char.du_sangao", seat, speed: 110, face: 1)
                .At(0.6f).Walk("char.lu_qinghe", Off(seat, -90, -40), run: true, face: 1)
                .Hold(2.6f);
        },

        // 唐守亭从怀里摸出转信副页：镜头推近。
        // 唐守亭从怀里摸出转信副页：镜头推近，副页特写，求援书的转信章滑过来叠在签押上——笔势相同。
        ["dlg.ch01.copy_custody/cue.003"] = (c, q) => q
            .At(0).Pan(c.Stand("char.tang_shouting", "event.ch01.copy_custody"), rate: 2.6f, height: 50, zoom: 1.35f, seconds: 1.2f)
            .At(0.6f).Sound("ui.open", -6).Document(DocumentKind.RelayCopy, seconds: 3.4f)
            .Hold(4f),

        // ── 章末：客栈门口的红纸名单 ─────────────────────
        ["dlg.ch01.epilogue/cue.001"] = (c, q) => q
            .At(0).Pan(c.Point("anchor:door"), rate: 2f, height: 140, zoom: 1.25f, seconds: 1.4f)
            .At(0.7f).Document(DocumentKind.RedList, seconds: 2.8f)
            .Hold(3.6f),
    };
}
