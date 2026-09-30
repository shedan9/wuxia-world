using System.Text;

namespace WuxiaWorld.Domain.Common;

/// <summary>
/// 状态哈希：64 位 FNV-1a，按调用方给定的固定次序写入字段。只用于重放比对与诊断，
/// 不是加密哈希；调用方负责稳定排序（禁止依赖字典枚举顺序，架构文档 7.5）。
/// </summary>
public sealed class StateHasher
{
    private const ulong Offset = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    public ulong Value { get; private set; } = Offset;

    public StateHasher Add(long value)
    {
        var v = unchecked((ulong)value);
        for (var i = 0; i < 8; i++)
        {
            AddByte((byte)(v >> (i * 8)));
        }

        return this;
    }

    public StateHasher Add(bool value) => Add(value ? 1L : 0L);

    public StateHasher Add(string? value)
    {
        if (value is null)
        {
            return Add(-1L);
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        Add(bytes.Length);
        foreach (var b in bytes)
        {
            AddByte(b);
        }

        return this;
    }

    private void AddByte(byte b) => Value = unchecked((Value ^ b) * Prime);

    public string Hex => Value.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
}
