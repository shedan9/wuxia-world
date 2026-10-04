using Godot;
using WuxiaWorld.Game.Presentation.Audio;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.App;

/// <summary>对话文字的显示速度。</summary>
public enum TextSpeed
{
    Slow,
    Normal,
    Fast,
    Instant,
}

/// <summary>
/// 玩家设置（音量、显示、文字、辅助与按键），存 <c>user://settings.cfg</c>，启动时读取并应用。
/// 音量按 0–100 记，换算到各总线的分贝；配乐与环境声另叠加台词播放时的压低量（<see cref="Duck"/>）。
/// </summary>
public static class GameSettings
{
    /// <summary>
    /// 设置文件：<c>user://settings.cfg</c>；开发运行给了 <c>--saves=目录</c> 时改用该目录下的 settings.cfg，
    /// 走查与截图（含改键实测）从默认设置开始，也不改动玩家真实的设置。
    /// </summary>
    private static string Path => DevCapture.SaveDirectory is { } dir ? System.IO.Path.Combine(dir, "settings.cfg") : "user://settings.cfg";

    public static int Master { get; set; } = 80;
    public static int Music { get; set; } = 70;
    public static int Ambience { get; set; } = 80;
    public static int Voice { get; set; } = 90;
    public static int Sfx { get; set; } = 75;
    public static bool Fullscreen { get; set; } = true;
    public static TextSpeed TextSpeed { get; set; } = TextSpeed.Normal;

    /// <summary>正文字号（20–32，默认 24）：阅读用的字按它与 24 之比缩放（<see cref="FontScale"/>）。</summary>
    public static int TextSize { get; set; } = FontScale.BaseBody;

    /// <summary>对话自动推进：一句配音说完（无配音的按字数留出读完的时间）后自动进入下一句；选项与演出不受影响。</summary>
    public static bool AutoAdvance { get; set; }

    public const int TextSizeMin = 20;
    public const int TextSizeMax = 32;

    /// <summary>减少动效：界面入场与提示呼吸直接落定（<see cref="Motion.Reduced"/>）。</summary>
    public static bool ReduceMotion { get; set; }

    /// <summary>战斗受击时人物横向抖动；关闭后只留闪白与音效。</summary>
    public static bool HitShake { get; set; } = true;

    /// <summary>战斗演出的默认倍速（1 或 2）；战斗中仍可随时切换，不影响结果。</summary>
    public static int BattleSpeed { get; set; } = 1;

    /// <summary>台词播放时配乐与环境声的压低程度（0–1，由 <see cref="SoundDirector"/> 平滑更新）。</summary>
    public static float Duck { get; set; }

    /// <summary>逐字显示的每秒字数；瞬间显示为 0。</summary>
    public static float CharsPerSecond => TextSpeed switch
    {
        TextSpeed.Slow => 24,
        TextSpeed.Fast => 70,
        TextSpeed.Instant => 0,
        _ => 40,
    };

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) == Error.Ok)
        {
            Master = (int)cfg.GetValue("audio", "master", Master);
            Music = (int)cfg.GetValue("audio", "music", Music);
            Ambience = (int)cfg.GetValue("audio", "ambience", Ambience);
            Voice = (int)cfg.GetValue("audio", "voice", Voice);
            Sfx = (int)cfg.GetValue("audio", "sfx", Sfx);
            Fullscreen = (bool)cfg.GetValue("display", "fullscreen", Fullscreen);
            TextSpeed = (TextSpeed)(int)cfg.GetValue("text", "speed", (int)TextSpeed);
            TextSize = Math.Clamp((int)cfg.GetValue("text", "size", TextSize), TextSizeMin, TextSizeMax);
            AutoAdvance = (bool)cfg.GetValue("text", "auto_advance", AutoAdvance);
            ReduceMotion = (bool)cfg.GetValue("assist", "reduce_motion", ReduceMotion);
            HitShake = (bool)cfg.GetValue("assist", "hit_shake", HitShake);
            BattleSpeed = (int)cfg.GetValue("assist", "battle_speed", BattleSpeed) >= 2 ? 2 : 1;
            KeyBindings.Load(cfg);
        }

        ApplyVolumes();
        ApplyText();
    }

    /// <summary>套用字号与减少动效；字号变了返回 true（调用方须重建主题与已打开的页面）。</summary>
    public static bool ApplyText()
    {
        var factor = TextSize / (float)FontScale.BaseBody;
        var changed = !Mathf.IsEqualApprox(factor, FontScale.Factor);
        FontScale.Factor = factor;
        Motion.Reduced = ReduceMotion;
        return changed;
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("audio", "master", Master);
        cfg.SetValue("audio", "music", Music);
        cfg.SetValue("audio", "ambience", Ambience);
        cfg.SetValue("audio", "voice", Voice);
        cfg.SetValue("audio", "sfx", Sfx);
        cfg.SetValue("display", "fullscreen", Fullscreen);
        cfg.SetValue("text", "speed", (int)TextSpeed);
        cfg.SetValue("text", "size", TextSize);
        cfg.SetValue("text", "auto_advance", AutoAdvance);
        cfg.SetValue("assist", "reduce_motion", ReduceMotion);
        cfg.SetValue("assist", "hit_shake", HitShake);
        cfg.SetValue("assist", "battle_speed", BattleSpeed);
        KeyBindings.Save(cfg);
        if (DevCapture.SaveDirectory is { } dir)
        {
            System.IO.Directory.CreateDirectory(dir);
        }

        cfg.Save(Path);
    }

    public static void ApplyVolumes()
    {
        Set("Master", Master, 0);
        Set(SoundDirector.MusicBus, Music, -7 * Duck);
        Set(SoundDirector.AmbienceBus, Ambience, -4 * Duck);
        Set(VoicePlayer.Bus, Voice, 0);
        Set(SoundDirector.SfxBus, Sfx, 0);
    }

    private static void Set(string bus, int percent, float extraDb)
    {
        var index = AudioServer.GetBusIndex(bus);
        if (index < 0)
        {
            return;
        }

        AudioServer.SetBusMute(index, percent <= 0);
        // 0–100 按平方律换算，低段更细：50 约 −12 dB。
        var linear = Mathf.Pow(Mathf.Clamp(percent, 0, 100) / 100f, 2);
        AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Mathf.Max(linear, 1e-4f)) + extraDb);
    }
}
