using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;

namespace WuxiaWorld.Game.Presentation.Audio;

/// <summary>一句台词的配音状况。</summary>
public enum VoiceState
{
    /// <summary>正在播放。</summary>
    Playing,

    /// <summary>清单里没有这句（未生成或未安装），只显示字幕。</summary>
    Missing,

    /// <summary>音频生成时的文字与当前台词不同，不播放，等增量重生成。</summary>
    Stale,

    /// <summary>音频文件读不出来。</summary>
    Broken,
}

/// <summary>
/// 台词配音播放（开发计划 M2-09，架构文档 10.5）：按 <c>line_id</c> 查 <c>res://assets/audio/voice/voice_manifest.json</c>
/// （由 tools/VoiceBuilder/install.py 写出），核对生成时的文字哈希与当前台词一致才播放，否则降级为只显示字幕。
/// 挂在 <c>AppHost</c> 下，走独立的 Voice 总线；同一时刻只播一句，换句、跳过或关闭对话时停止。
/// </summary>
public partial class VoicePlayer : Node
{
    public const string Bus = "Voice";
    private const string Dir = "res://assets/audio/voice/";

    private readonly AudioStreamPlayer _player = new();
    private Dictionary<string, Entry> _lines = new(StringComparer.Ordinal);
    private string? _current;

    /// <summary>清单状态：<c>trial</c> 为试听版（台词未锁稿），<c>final</c> 为锁稿审核后的正式配音；无清单为空串。</summary>
    public string Status { get; private set; } = "";

    public override void _Ready()
    {
        if (AudioServer.GetBusIndex(Bus) < 0)
        {
            AudioServer.AddBus();
            var index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, Bus);
            AudioServer.SetBusSend(index, "Master");
        }

        _player.Bus = Bus;
        AddChild(_player);
        LoadManifest();
    }

    private void LoadManifest()
    {
        var path = Dir + "voice_manifest.json";
        if (!Godot.FileAccess.FileExists(path))
        {
            GD.PushWarning($"找不到配音清单 {path}：对话只显示字幕");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(path));
            Status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
            _lines = doc.RootElement.GetProperty("lines").EnumerateObject().ToDictionary(
                p => p.Name,
                p => new Entry(p.Value.GetProperty("file").GetString()!, p.Value.GetProperty("text_sha256").GetString()!),
                StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            GD.PushError($"配音清单格式错误：{ex.Message}");
        }
    }

    /// <summary>播放一句台词的配音；不可播放时返回原因，调用方照常显示字幕。</summary>
    public VoiceState Play(string lineId, string text)
    {
        Stop();
        if (!_lines.TryGetValue(lineId, out var entry))
        {
            return VoiceState.Missing;
        }

        var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        if (sha != entry.TextSha256)
        {
            GD.PushWarning($"配音过期：{lineId} 的文字已改，需重生成");
            return VoiceState.Stale;
        }

        var path = Dir + entry.File;
        if (!ResourceLoader.Exists(path) || GD.Load<AudioStream>(path) is not { } stream)
        {
            GD.PushWarning($"配音文件读不出：{path}");
            return VoiceState.Broken;
        }

        _player.Stream = stream;
        _player.Play();
        _current = lineId;
        return VoiceState.Playing;
    }

    /// <summary>重播上一句（对话里的 R 键）。只重放声音，不再执行任何对话效果。</summary>
    public void Replay()
    {
        if (_current is not null && _player.Stream is not null)
        {
            _player.Play();
        }
    }

    /// <summary>退出时停止并释放当前音频，免得播放中的流在引擎关闭时仍被引用。</summary>
    public override void _ExitTree()
    {
        Stop();
        _player.Stream = null;
    }

    public void Stop()
    {
        _player.Stop();
        _current = null;
    }

    public bool Playing => _player.Playing;

    private sealed record Entry(string File, string TextSha256);
}
