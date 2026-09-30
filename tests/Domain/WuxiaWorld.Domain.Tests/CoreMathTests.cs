using WuxiaWorld.Domain.Combat;
using WuxiaWorld.Domain.Common;

namespace WuxiaWorld.Domain.Tests;

public class Pcg32Tests
{
    [Fact]
    public void Matches_reference_vector_for_seed_42_sequence_54()
    {
        // pcg32-demo（pcg-random.org 参考实现）pcg32_srandom(42, 54) 的前 6 个输出。
        var rng = new Pcg32(42, 54);
        uint[] expected = [0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e];
        foreach (var value in expected)
        {
            Assert.Equal(value, rng.NextUInt());
        }
    }

    [Fact]
    public void Restore_continues_the_same_sequence()
    {
        var a = new Pcg32(7, 11);
        a.NextUInt();
        var b = Pcg32.Restore(a.State, a.Increment);
        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(a.NextUInt(), b.NextUInt());
        }
    }

    [Fact]
    public void Certain_and_impossible_rolls_do_not_consume_randomness()
    {
        var rng = new Pcg32(1, 2);
        var before = rng;
        Assert.True(rng.RollBp(10_000));
        Assert.False(rng.RollBp(0));
        Assert.Equal(before, rng);
    }

    [Fact]
    public void Bounded_values_stay_in_range()
    {
        var rng = new Pcg32(3, 4);
        for (var i = 0; i < 2000; i++)
        {
            var v = rng.NextInclusive(9500, 10_500);
            Assert.InRange(v, 9500, 10_500);
        }
    }
}

public class DamageMathTests
{
    [Fact]
    public void Architecture_example_gives_70_then_84_with_counter()
    {
        // 架构文档 7.4：攻击 100、固定值 20、倍率 1.2、防御 100 → 基础 70；克制 1.2 → 84。
        var plain = DamageMath.Compute(new DamageMath.Inputs(100, 100, 20, 12_000, Bp.One, false, 0, 0, Bp.One));
        var countered = DamageMath.Compute(new DamageMath.Inputs(100, 100, 20, 12_000, 12_000, false, 0, 0, Bp.One));
        Assert.Equal(70, plain);
        Assert.Equal(84, countered);
    }

    [Fact]
    public void Damage_is_at_least_one()
    {
        Assert.Equal(1, DamageMath.Compute(new DamageMath.Inputs(1, 5000, 0, 100, 8000, false, -5000, -8000, 9500)));
    }

    [Fact]
    public void Counter_and_status_layers_are_clamped()
    {
        var huge = DamageMath.Compute(new DamageMath.Inputs(100, 0, 0, Bp.One, 99_999, false, 99_999, 99_999, Bp.One));
        // 克制封顶 1.25，攻方增伤封顶 +100%，受方增伤封顶 +100%：100 × 1.25 × 2 × 2 = 500。
        Assert.Equal(500, huge);
    }

    [Fact]
    public void Layers_floor_in_fixed_order()
    {
        // 基础 (0 + 33 × 1.0) × 100 / 110 = 30.0000；暴击 45；攻方 +10% 49.5；受方 −50% 24.75；浮动 0.95 → 23.5125 → 23。
        var v = DamageMath.Compute(new DamageMath.Inputs(33, 10, 0, Bp.One, Bp.One, true, 1000, -5000, 9500));
        Assert.Equal(23, v);
    }

    [Theory]
    [InlineData(100, 100, 9000)]
    [InlineData(100, 400, 6500)]
    [InlineData(400, 100, 9800)]
    [InlineData(150, 100, 9500)]
    public void Hit_chance_follows_formula_and_clamp(int accuracy, int evasion, int expected) =>
        Assert.Equal(expected, DamageMath.HitChanceBp(accuracy, evasion, 0));

    [Fact]
    public void Crit_chance_is_capped_at_35_percent() => Assert.Equal(3500, DamageMath.CritChanceBp(9000));
}
