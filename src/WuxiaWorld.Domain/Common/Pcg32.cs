namespace WuxiaWorld.Domain.Common;

/// <summary>
/// 项目自有的确定性伪随机数生成器：PCG-XSH-RR 32 位输出、64 位状态（O'Neill，pcg-random.org 参考实现）。
/// 不依赖系统时间或 <see cref="System.Random"/>；状态可序列化，同一初值在任何平台产出同一序列（架构文档 7.5）。
/// 固定测试向量见 tests/Domain 的 <c>Pcg32Tests</c>。
/// </summary>
public struct Pcg32 : IEquatable<Pcg32>
{
    private const ulong Multiplier = 6364136223846793005UL;

    public ulong State { get; private set; }

    /// <summary>流选择量（奇数）。不同用途（战斗、旅行）用不同流，互不干扰。</summary>
    public ulong Increment { get; private set; }

    /// <summary>按参考实现 <c>pcg32_srandom(initstate, initseq)</c> 初始化。</summary>
    public Pcg32(ulong seed, ulong sequence)
    {
        State = 0;
        Increment = (sequence << 1) | 1UL;
        NextUInt();
        State += seed;
        NextUInt();
    }

    /// <summary>从已保存的状态恢复（存档、重放用）。</summary>
    public static Pcg32 Restore(ulong state, ulong increment)
    {
        if ((increment & 1UL) == 0)
        {
            throw new ArgumentException("PCG 流选择量必须为奇数。", nameof(increment));
        }

        return new Pcg32 { State = state, Increment = increment };
    }

    public uint NextUInt()
    {
        var old = State;
        State = unchecked(old * Multiplier + Increment);
        var xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        var rot = (int)(old >> 59);
        return (xorShifted >> rot) | (xorShifted << (-rot & 31));
    }

    /// <summary>[0, bound) 内均匀分布的整数，拒绝采样去偏（参考实现 <c>pcg32_boundedrand</c>）。</summary>
    public uint NextBelow(uint bound)
    {
        if (bound == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bound), "上界必须大于 0。");
        }

        var threshold = (uint)(-bound % bound);
        while (true)
        {
            var r = NextUInt();
            if (r >= threshold)
            {
                return r % bound;
            }
        }
    }

    /// <summary>[min, max] 闭区间整数。</summary>
    public int NextInclusive(int min, int max)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, min);
        return min + (int)NextBelow((uint)(max - min + 1));
    }

    /// <summary>以万分比概率判定：<paramref name="chanceBp"/> ≤ 0 恒假、≥ 10000 恒真，且此时不消耗随机数。</summary>
    public bool RollBp(int chanceBp)
    {
        if (chanceBp <= 0)
        {
            return false;
        }

        if (chanceBp >= Bp.One)
        {
            return true;
        }

        return NextBelow(Bp.One) < (uint)chanceBp;
    }

    public readonly bool Equals(Pcg32 other) => State == other.State && Increment == other.Increment;

    public override readonly bool Equals(object? obj) => obj is Pcg32 other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(State, Increment);

    public static bool operator ==(Pcg32 left, Pcg32 right) => left.Equals(right);

    public static bool operator !=(Pcg32 left, Pcg32 right) => !left.Equals(right);
}
