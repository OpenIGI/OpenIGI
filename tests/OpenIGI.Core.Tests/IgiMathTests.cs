using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Core.Tests;

/// <summary>
/// Pins the truncation and alignment behaviour the determinism contract depends on.
/// </summary>
public sealed class IgiMathTests
{
    [Theory]
    [InlineData(0.0, 0L)]
    [InlineData(1.9, 1L)]
    [InlineData(-1.9, -1L)]
    [InlineData(-1.75, -1L)]   // the original asserts exactly this every frame (FPU rounding-mode check)
    [InlineData(0.5, 0L)]
    [InlineData(-0.5, 0L)]
    [InlineData(1.5, 1L)]
    [InlineData(2.5, 2L)]      // truncation, not banker's rounding
    [InlineData(-2.5, -2L)]
    public void Trunc_TruncatesTowardZero(double value, long expected) =>
        Assert.Equal(expected, IgiMath.Trunc(value));

    [Fact]
    public void Trunc_DiffersFromRounding()
    {
        // Math.Round would give -2 for both of these; the original engine gives -1.
        Assert.Equal(-1L, IgiMath.Trunc(-1.5));
        Assert.Equal(-1L, IgiMath.Trunc(-1.9999999));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(1e30)]
    [InlineData(-1e30)]
    public void Trunc_ProducesIntegerIndefiniteInsteadOfSaturating(double value)
    {
        // A plain (long) cast saturates on .NET; x87 ftol yields the integer-indefinite value.
        Assert.Equal(long.MinValue, IgiMath.Trunc(value));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(3e9)]
    [InlineData(-3e9)]
    public void TruncToInt32_ProducesIntegerIndefiniteInsteadOfSaturating(double value) =>
        Assert.Equal(int.MinValue, IgiMath.TruncToInt32(value));

    [Fact]
    public void Trunc_AcceptsTheExtremesOfTheInt64Range()
    {
        Assert.Equal(long.MinValue, IgiMath.Trunc(-9223372036854775808.0));
        Assert.Equal(2147483647L, IgiMath.Trunc(2147483647.9));
    }

    [Fact]
    public void TruncToInt32_AcceptsTheExtremesOfTheInt32Range()
    {
        Assert.Equal(int.MinValue, IgiMath.TruncToInt32(-2147483648.0));
        Assert.Equal(int.MaxValue, IgiMath.TruncToInt32(2147483647.5));
    }

    [Theory]
    [InlineData(0, 4, 0)]
    [InlineData(1, 4, 4)]
    [InlineData(4, 4, 4)]
    [InlineData(5, 4, 8)]
    [InlineData(20, 4, 20)]
    [InlineData(33, 32, 64)]
    [InlineData(7, 1, 7)]
    public void AlignUp_RoundsUpToTheNextMultiple(int value, int alignment, int expected) =>
        Assert.Equal(expected, IgiMath.AlignUp(value, alignment));

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-4)]
    public void AlignUp_RejectsNonPowerOfTwoAlignments(int alignment) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => IgiMath.AlignUp(8, alignment));

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(4, true)]
    [InlineData(1 << 30, true)]
    [InlineData(0, false)]
    [InlineData(3, false)]
    [InlineData(-2, false)]
    public void IsPowerOfTwo_MatchesTheOriginalValidation(int value, bool expected) =>
        Assert.Equal(expected, IgiMath.IsPowerOfTwo(value));
}
