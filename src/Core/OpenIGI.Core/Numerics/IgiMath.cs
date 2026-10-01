namespace OpenIGI.Core.Numerics;

/// <summary>
/// Scalar helpers whose behaviour is part of the engine's determinism contract.
/// </summary>
/// <remarks>
/// <para>
/// The original game is an MSVC 6 / x87 binary. Its float-to-integer conversions go through the CRT
/// helper <c>_ftol</c>, which truncates toward zero and — for NaN or out-of-range inputs — yields the
/// x87 "integer indefinite" value (the most negative integer of the target width). .NET, by contrast,
/// <em>saturates</em> such conversions since .NET Core 3.0. The conversion helpers here reproduce the
/// original behaviour, so game logic must use them rather than a plain cast.
/// </para>
/// <para>
/// The main loop of the original asserts the FPU rounding mode every frame by checking that
/// <c>(long)(-1.75) == -1</c>; <see cref="Trunc(double)"/> preserves exactly that.
/// </para>
/// </remarks>
public static class IgiMath
{
    /// <summary>
    /// Converts <paramref name="value"/> to a 64-bit integer, truncating toward zero.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>
    /// The truncated value, or <see cref="long.MinValue"/> (the x87 integer-indefinite result) when
    /// <paramref name="value"/> is NaN or lies outside the range of <see cref="long"/>.
    /// </returns>
    public static long Trunc(double value)
    {
        // The comparison order matters: NaN fails both bounds checks and falls through to indefinite.
        if (value >= -9223372036854775808.0 && value < 9223372036854775808.0)
        {
            return (long)value;
        }

        return long.MinValue;
    }

    /// <summary>
    /// Converts <paramref name="value"/> to a 32-bit integer, truncating toward zero.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>
    /// The truncated value, or <see cref="int.MinValue"/> (the x87 integer-indefinite result) when
    /// <paramref name="value"/> is NaN or lies outside the range of <see cref="int"/>.
    /// </returns>
    public static int TruncToInt32(double value)
    {
        if (value >= -2147483648.0 && value < 2147483648.0)
        {
            return (int)value;
        }

        return int.MinValue;
    }

    /// <summary>
    /// Converts <paramref name="value"/> to a 32-bit integer, truncating toward zero.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>
    /// The truncated value, or <see cref="int.MinValue"/> when <paramref name="value"/> is NaN or lies
    /// outside the range of <see cref="int"/>.
    /// </returns>
    public static int TruncToInt32(float value) => TruncToInt32((double)value);

    /// <summary>
    /// Rounds <paramref name="value"/> up to the next multiple of <paramref name="alignment"/>.
    /// </summary>
    /// <param name="value">The value to align. Must not be negative.</param>
    /// <param name="alignment">The alignment, which must be a positive power of two.</param>
    /// <returns>The smallest multiple of <paramref name="alignment"/> that is at least <paramref name="value"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="value"/> is negative, or <paramref name="alignment"/> is not a positive power of two.
    /// </exception>
    /// <remarks>
    /// This is the alignment rule used throughout the ILFF container format, where chunk payloads are
    /// aligned relative to their parent container's base address.
    /// </remarks>
    public static int AlignUp(int value, int alignment)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        if (!IsPowerOfTwo(alignment))
        {
            throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "Alignment must be a positive power of two.");
        }

        return (value + (alignment - 1)) & ~(alignment - 1);
    }

    /// <summary>
    /// Determines whether <paramref name="value"/> is a positive power of two.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a positive power of two; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// Mirrors the original ILFF validation <c>(a &amp; ~(a - 1)) == a</c>, with the additional guard that
    /// zero and negative values are rejected.
    /// </remarks>
    public static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
}
