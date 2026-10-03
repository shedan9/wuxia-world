using System.Text.Json;
using Godot;
using WuxiaWorld.Game.Presentation.App;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Audio;

/// <summary>
/// 配乐、环境声与音效（架构文档 10.5）：挂在 <c>AppHost</c> 下，跨场景持续，换场景时按需交叉淡化，同一首不重开。
/// 总线：Music、Ambience、Sfx（台词走 <see cref="VoicePlayer.Bus"/>），音量由 <see cref="GameSettings"/> 设定。
/// 配乐只配主要场景、剧情中只在转折处进入（用户 2026-10-01 定的法则）；台词播放时配乐压低 7 dB、环境声压低 4 dB。
/// 素材：<c>res://assets/audio/music</c>（tools/AudioBuild/bgm_runtime.py，循环起点见 music_manifest.json）、
/// <c>res://assets/audio/amb</c> 与 <c>res://assets/audio/sfx</c>（tools/AudioBuild/sfx_build.py 按配方剪辑 Freesound CC0 实录，来源见 sfx_manifest.json）。
/// 音效 ID 有编号变体（<c>step.stone.1</c>…）时随机挑一个并微调音高。
/// </summary>
public partial class SoundDirector : Node
{
    public const string MusicBus = "Music";
    public const string AmbienceBus = "Ambience";
    public const string SfxBus = "Sfx";

    private const string MusicDir = "res://assets/audio/music/";
    private const string AmbDir = "res://assets/audio/amb/";
    private const string SfxDir = "res://assets/audio/sfx/";
    private const float Silent = -60;

    private readonly Dictionary<string, float> _loopOffsets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AudioStream?> _sfxCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _variants = new(StringComparer.Ordinal);
    private readonly List<AudioStreamPlayer> _pool = [];
    private readonly Dictionary<string, AudioStreamPlayer> _ambience = new(StringComparer.Ordinal);
    private readonly RandomNumberGenerator _rng = new();
    private AudioStreamPlayer? _music;
    private string? _musicId;
    private float _duck;
    private ulong _lastUiSound;

    public string? MusicId => _musicId;

    public override void _Ready()
    {
        foreach (var bus in new[] { MusicBus, AmbienceBus, SfxBus, VoicePlayer.Bus })
        {
            EnsureBus(bus);
        }

        for (var i = 0; i < 12; i++)
        {
            var p = new AudioStreamPlayer { Bus = SfxBus };
            AddChild(p);
            _pool.Add(p);
        }

        LoadManifest();
        GetTree().NodeAdded += OnNodeAdded;
    }

    public static void EnsureBus(string name)
    {
        if (AudioServer.GetBusIndex(name) >= 0)
        {
            return;
        }

        AudioServer.AddBus();
        var index = AudioServer.BusCount - 1;
        AudioServer.SetBusName(index, name);
        AudioServer.SetBusSend(index, "Master");
    }

    private void LoadManifest()
    {
        var path = MusicDir + "music_manifest.json";
        if (!Godot.FileAccess.FileExists(path))
        {
            GD.PushWarning($"找不到配乐清单 {path}");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(path));
            foreach (var t in doc.RootElement.GetProperty("tracks").EnumerateObject())
            {
                _loopOffsets[t.Name] = t.Value.GetProperty("loop_offset").GetSingle();
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            GD.PushError($"配乐清单格式错误：{ex.Message}");
        }
    }

    // ── 配乐 ─────────────────────────────────────────────

    /// <summary>换配乐（null 为停）。同一首正在放时不重开；新旧交叉淡化。</summary>
    public void PlayMusic(string? id, float fade = 1.5f)
    {
        if (id == _musicId)
        {
            return;
        }

        if (_music is { } old)
        {
            FadeOutAndFree(old, fade);
            _music = null;
        }

        _musicId = id;
        if (id is null || Loop(MusicDir + id + ".ogg", _loopOffsets.GetValueOrDefault(id)) is not { } stream)
        {
            return;
        }

        _music = new AudioStreamPlayer { Stream = stream, Bus = MusicBus, VolumeDb = Silent };
        AddChild(_music);
        _music.Play();
        FadeTo(_music, 0, fade);
    }

    /// <summary>换环境声（可同时几层，例如河水加风吹芦苇）；不在新集合里的淡出，已在放的不重开。</summary>
    public void PlayAmbience(params string[] ids)
    {
        foreach (var (id, player) in _ambience.Where(a => !ids.Contains(a.Key)).ToList())
        {
            FadeOutAndFree(player, 1.5f);
            _ambience.Remove(id);
        }

        foreach (var id in ids.Where(id => !_ambience.ContainsKey(id)))
        {
            if (Loop(AmbDir + id + ".ogg", 0) is not { } stream)
            {
                continue;
            }

            var p = new AudioStreamPlayer { Stream = stream, Bus = AmbienceBus, VolumeDb = Silent };
            AddChild(p);
            // 从随机位置起播，免得每次进图都从同一处开始。
            p.Play((float)_rng.RandfRange(0, (float)stream.GetLength()));
            FadeTo(p, 0, 2f);
            _ambience[id] = p;
        }
    }

    private static AudioStream? Loop(string path, float offset)
    {
        if (!ResourceLoader.Exists(path))
        {
            GD.PushWarning($"音频不存在：{path}");
            return null;
        }

        var stream = GD.Load<AudioStream>(path);
        if (stream is AudioStreamOggVorbis ogg)
        {
            ogg.Loop = true;
            ogg.LoopOffset = offset;
        }

        return stream;
    }

    private static void FadeTo(AudioStreamPlayer p, float db, float seconds)
    {
        if (seconds <= 0 || !Motion.Enabled)
        {
            p.VolumeDb = db;
            return;
        }

        p.CreateTween().TweenProperty(p, "volume_db", db, seconds).SetTrans(Tween.TransitionType.Sine);
    }

    private static void FadeOutAndFree(AudioStreamPlayer p, float seconds)
    {
        if (seconds <= 0 || !Motion.Enabled)
        {
            p.QueueFree();
            return;
        }

        var tween = p.CreateTween();
        tween.TweenProperty(p, "volume_db", Silent, seconds).SetTrans(Tween.TransitionType.Sine);
        tween.TweenCallback(Callable.From(p.QueueFree));
    }

    public override void _Process(double delta)
    {
        // 台词播放时压低配乐与环境声，说完回升。
        var target = AppHost.Instance.Voice.Playing ? 1f : 0f;
        _duck = Mathf.MoveToward(_duck, target, (float)delta * (target > _duck ? 4f : 1.2f));
        GameSettings.Duck = _duck;
        GameSettings.ApplyVolumes();
        if (DevCapture.AudioMeter)
        {
            Meter(delta);
        }
    }

    private double _meterClock;
    private readonly Dictionary<string, float> _meterPeak = new(StringComparer.Ordinal);

    /// <summary>开发用（<c>--audio-meter</c>）：每 3 秒打印各总线这段时间的峰值电平，核对配乐、环境声、音效与台词确实在响。</summary>
    private void Meter(double delta)
    {
        foreach (var bus in new[] { MusicBus, AmbienceBus, SfxBus, VoicePlayer.Bus })
        {
            var i = AudioServer.GetBusIndex(bus);
            var db = Mathf.Max(AudioServer.GetBusPeakVolumeLeftDb(i, 0), AudioServer.GetBusPeakVolumeRightDb(i, 0));
            _meterPeak[bus] = Mathf.Max(_meterPeak.GetValueOrDefault(bus, -200), db);
        }

        _meterClock += delta;
        if (_meterClock < 3)
        {
            return;
        }

        _meterClock = 0;
        GD.Print("[audio] " + string.Join("　", _meterPeak.Select(p => $"{p.Key} {(p.Value < -100 ? "静" : $"{p.Value:0} dB")}")) + $"　配乐 {_musicId ?? "无"}　环境 {string.Join("+", _ambience.Keys)}");
        _meterPeak.Clear();
    }

    // ── 音效 ─────────────────────────────────────────────

    /// <summary>
    /// 播一个音效。ID 不存在而有编号变体（<c>id.1</c>、<c>id.2</c>…）时随机挑一个；<paramref name="pitchJitter"/> 为音高随机幅度。
    /// </summary>
    public void Play(string id, float volumeDb = 0, float pitchJitter = 0)
    {
        var stream = Resolve(id);
        if (stream is null)
        {
            return;
        }

        var player = _pool.FirstOrDefault(p => !p.Playing) ?? _pool.MinBy(p => p.GetPlaybackPosition() == 0 ? float.MaxValue : -p.GetPlaybackPosition())!;
        player.Stream = stream;
        player.VolumeDb = volumeDb;
        player.PitchScale = pitchJitter > 0 ? 1 + _rng.RandfRange(-pitchJitter, pitchJitter) : 1;
        player.Play();
    }

    private AudioStream? Resolve(string id)
    {
        if (!_variants.TryGetValue(id, out var count))
        {
            count = 0;
            while (ResourceLoader.Exists($"{SfxDir}{id}.{count + 1}.ogg"))
            {
                count++;
            }

            _variants[id] = count;
        }

        var key = count > 0 ? $"{id}.{_rng.RandiRange(1, count)}" : id;
        if (!_sfxCache.TryGetValue(key, out var stream))
        {
            var path = SfxDir + key + ".ogg";
            stream = ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;
            if (stream is null)
            {
                GD.PushWarning($"音效不存在：{path}");
            }

            _sfxCache[key] = stream;
        }

        return stream;
    }

    /// <summary>界面音效：所有按钮取得焦点时轻响一下、按下时确认声（全局挂钩，不必逐页接线）。</summary>
    private const string SoundMeta = "ui_sound_hooked";

    private void OnNodeAdded(Node node)
    {
        // 同一按钮离开场景树再进来时会再收到一次，只挂一次。
        if (node is not BaseButton button || button.HasMeta(SoundMeta))
        {
            return;
        }

        button.SetMeta(SoundMeta, true);
        button.FocusEntered += () => UiSound("ui.move", -6);
        button.Pressed += () => UiSound("ui.confirm", -4);
    }

    private void UiSound(string id, float db)
    {
        // 同一帧里焦点连跳（打开面板时默认聚焦）只响一次。
        var now = Time.GetTicksMsec();
        if (now - _lastUiSound < 60)
        {
            return;
        }

        _lastUiSound = now;
        Play(id, db);
    }

    public override void _ExitTree()
    {
        foreach (var p in _pool)
        {
            p.Stop();
            p.Stream = null;
        }

        foreach (var p in _ambience.Values)
        {
            p.Stop();
            p.Stream = null;
        }

        _music?.Stop();
    }
}
