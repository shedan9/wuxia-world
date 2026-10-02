using Godot;
using WuxiaWorld.Game.Presentation.Audio;

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
/// 玩家设置（音量、显示、文字），存 <c>user://settings.cfg</c>，启动时读取并应用。
/// 音量按 0–100 记，换算到各总线的分贝；配乐与环境声另叠加台词播放时的压低量（<see cref="Duck"/>）。
/// </summary>
public static class GameSettings
{
    private const string Path = "user://settings.cfg";

    public static int Master { get; set; } = 80;
    public static int Music { get; set; } = 70;
    public static int Ambience { get; set; } = 80;
    public static int Voice { get; set; } = 90;
    public static int Sfx { get; set; } = 75;
    public static bool Fullscreen { get; set; } = true;
    public static TextSpeed TextSpeed { get; set; } = TextSpeed.Normal;

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
        }

        ApplyVolumes();
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
