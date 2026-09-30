namespace WuxiaWorld.Domain.Common;

/// <summary>
/// 万分比定点（basis points）：10000 表示 1.0。规则计算一律用 64 位整数与万分比倍率，
/// 在固定位置向下取整；策划表里的小数只是表示法（架构文档 7.4）。
/// </summary>
public static class Bp
{
    public const int One = 10_000;

    /// <summary>value × bp / 10000，向下取整（负数向负无穷取整）。</summary>
    public static long Apply(long value, int bp) => FloorDiv(value * bp, One);

    public static long FloorDiv(long a, long b)
    {
        var q = a / b;
        if ((a % b != 0) && ((a < 0) != (b < 0)))
        {
            q--;
        }

        return q;
    }

    public static int Clamp(int value, int min, int max) => Math.Min(max, Math.Max(min, value));

    public static long Clamp(long value, long min, long max) => Math.Min(max, Math.Max(min, value));

    /// <summary>以百分数显示（向下取整到整数百分比），仅供界面文字。</summary>
    public static int ToPercent(int bp) => bp / 100;
}
