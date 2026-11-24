using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Core.Tests;

/// <summary>
/// The simulation's own transcendental functions: within an ulp or two of the platform's, its special cases kept, and -- the
/// point of them -- the very same bits on every machine, held by a fingerprint of a hundred thousand results.
/// </summary>
public sealed class SimMathTests
{
    private const int Samples = 20000;

    /// <summary>Sine, cosine and tangent stay within an ulp of the platform's, near nought and far round the circle.</summary>
    /// <param name="scale">How far from nought the angles go.</param>
    [Theory]
    [InlineData(1.0)]
    [InlineData(10.0)]
    [InlineData(1000.0)]
    [InlineData(1e6)]
    public void Circular_functions_are_within_an_ulp(double scale)
    {
        foreach (double x in Values(scale, seed: 1))
        {
            AssertClose(Math.Sin(x), SimMath.Sin(x), 1, $"sin({x:R})");
            AssertClose(Math.Cos(x), SimMath.Cos(x), 1, $"cos({x:R})");
            AssertClose(Math.Tan(x), SimMath.Tan(x), 2, $"tan({x:R})");
        }
    }

    /// <summary>The inverse functions stay within an ulp of the platform's over their whole domain.</summary>
    [Fact]
    public void Inverse_functions_are_within_an_ulp()
    {
        foreach (double x in Values(1.0, seed: 2))
        {
            AssertClose(Math.Asin(x), SimMath.Asin(x), 1, $"asin({x:R})");
            AssertClose(Math.Acos(x), SimMath.Acos(x), 1, $"acos({x:R})");
        }

        foreach (double x in Values(1e4, seed: 3))
        {
            AssertClose(Math.Atan(x), SimMath.Atan(x), 1, $"atan({x:R})");
        }

        Random random = new(4);
        for (int i = 0; i < Samples; i++)
        {
            double y = (random.NextDouble() - 0.5) * Math.Pow(10, random.Next(-3, 4));
            double x = (random.NextDouble() - 0.5) * Math.Pow(10, random.Next(-3, 4));
            AssertClose(Math.Atan2(y, x), SimMath.Atan2(y, x), 2, $"atan2({y:R}, {x:R})");
        }
    }

    /// <summary>The exponential, the logarithm and powers stay close to the platform's.</summary>
    [Fact]
    public void Exponentials_are_close()
    {
        Random random = new(5);
        for (int i = 0; i < Samples; i++)
        {
            double x = (random.NextDouble() - 0.5) * 1400;
            AssertClose(Math.Exp(x), SimMath.Exp(x), 1, $"exp({x:R})");
            double positive = Math.Exp((random.NextDouble() - 0.5) * 1400);
            AssertClose(Math.Log(positive), SimMath.Log(positive), 1, $"log({positive:R})");
            double b = random.NextDouble() * 4;
            double e = (random.NextDouble() - 0.5) * 20;
            AssertClose(Math.Pow(b, e), SimMath.Pow(b, e), 2, $"pow({b:R}, {e:R})");
            double whole = Math.Round(e * 3);
            AssertClose(Math.Pow(b, whole), SimMath.Pow(b, whole), 2, $"pow({b:R}, {whole:R})");
        }
    }

    /// <summary>The edges answer as the platform's do: zeroes, infinities, NaN, the domain's ends.</summary>
    [Fact]
    public void Special_values_match_the_platform()
    {
        double[] specials = [0.0, -0.0, 1.0, -1.0, double.PositiveInfinity, double.NegativeInfinity, double.NaN, double.Epsilon, -double.Epsilon, double.MaxValue, 1e-300, 0.5, -0.5, 2.0];
        foreach (double x in specials)
        {
            if (Math.Abs(x) < 1e6 || !double.IsFinite(x))
            {
                AssertSame(Math.Sin(x), SimMath.Sin(x), $"sin({x:R})", ulps: 1);
                AssertSame(Math.Cos(x), SimMath.Cos(x), $"cos({x:R})", ulps: 1);
            }

            AssertSame(Math.Asin(x), SimMath.Asin(x), $"asin({x:R})", ulps: 1);
            AssertSame(Math.Acos(x), SimMath.Acos(x), $"acos({x:R})", ulps: 1);
            AssertSame(Math.Atan(x), SimMath.Atan(x), $"atan({x:R})", ulps: 1);
            AssertSame(Math.Exp(x), SimMath.Exp(x), $"exp({x:R})", ulps: 1);
            AssertSame(Math.Log(x), SimMath.Log(x), $"log({x:R})", ulps: 1);
            foreach (double y in specials)
            {
                AssertSame(Math.Atan2(y, x), SimMath.Atan2(y, x), $"atan2({y:R}, {x:R})", ulps: 1);
                AssertSame(Math.Pow(x, y), SimMath.Pow(x, y), $"pow({x:R}, {y:R})", ulps: 1);
            }
        }
    }

    /// <summary>
    /// Beyond the reduction's reach -- angles the simulation never meets -- sine and cosine are not accurate, but they stay
    /// finite, on the unit circle, and the same on every machine.
    /// </summary>
    [Fact]
    public void Huge_angles_stay_on_the_circle()
    {
        foreach (double x in new[] { 1e7, -3e9, 1e20, double.MaxValue, -double.MaxValue })
        {
            double sin = SimMath.Sin(x);
            double cos = SimMath.Cos(x);
            Assert.InRange(sin, -1.0, 1.0);
            Assert.InRange(cos, -1.0, 1.0);
            Assert.InRange((sin * sin) + (cos * cos), 1 - 1e-15, 1 + 1e-15);
        }
    }

    /// <summary>The float twins are the double results rounded once.</summary>
    [Fact]
    public void Float_twins_round_the_double_results()
    {
        foreach (double value in Values(10.0, seed: 6))
        {
            float x = (float)value;
            Assert.Equal((float)SimMath.Sin(x), SimMathF.Sin(x));
            Assert.Equal((float)SimMath.Atan2(x, 0.75f), SimMathF.Atan2(x, 0.75f));
            AssertCloseF(MathF.Sin(x), SimMathF.Sin(x), $"sinf({x:R})");
            AssertCloseF(MathF.Cos(x), SimMathF.Cos(x), $"cosf({x:R})");
        }
    }

    /// <summary>
    /// The same bits on every machine: a hash of a hundred thousand results, pinned. Should this fail on one platform only, the
    /// functions are not portable there; should it fail everywhere, a function changed, and every recorded replay with it.
    /// </summary>
    [Fact]
    public void Results_are_the_same_bits_everywhere()
    {
        ulong hash = 14695981039346656037UL;
        Random random = new(7);
        for (int i = 0; i < 10000; i++)
        {
            double x = (random.NextDouble() - 0.5) * Math.Pow(10, random.Next(-4, 5));
            double y = (random.NextDouble() - 0.5) * Math.Pow(10, random.Next(-4, 5));
            Fold(SimMath.Sin(x));
            Fold(SimMath.Cos(x));
            Fold(SimMath.Tan(x));
            Fold(SimMath.Atan(x));
            Fold(SimMath.Atan2(y, x));
            Fold(SimMath.Asin(Math.Clamp(x, -1, 1)));
            Fold(SimMath.Acos(Math.Clamp(y, -1, 1)));
            Fold(SimMath.Exp(Math.Clamp(x, -700, 700)));
            Fold(SimMath.Log(Math.Abs(x)));
            Fold(SimMath.Pow(Math.Abs(x), Math.Clamp(y, -30, 30)));
        }

        Assert.Equal(ExpectedFingerprint, hash);

        void Fold(double value)
        {
            hash ^= (ulong)BitConverter.DoubleToInt64Bits(value);
            hash *= 1099511628211UL;
        }
    }

    // The fingerprint of Results_are_the_same_bits_everywhere, first computed on win-x64 (2026-11-24).
    private const ulong ExpectedFingerprint = 7971497311450282632UL;

    private static IEnumerable<double> Values(double scale, int seed)
    {
        Random random = new(seed);
        for (int i = 0; i < Samples; i++)
        {
            yield return ((random.NextDouble() * 2) - 1) * scale;
        }
    }

    private static void AssertClose(double expected, double actual, long ulps, string what)
    {
        if (double.IsNaN(expected) || double.IsInfinity(expected) || expected == 0)
        {
            AssertSame(expected, actual, what, ulps);
            return;
        }

        long distance = Math.Abs(Ordered(expected) - Ordered(actual));
        Assert.True(distance <= ulps, $"{what}: platform {expected:R}, SimMath {actual:R}, {distance} ulps apart");
    }

    private static void AssertSame(double expected, double actual, string what, long ulps)
    {
        if (double.IsNaN(expected))
        {
            Assert.True(double.IsNaN(actual), $"{what}: platform NaN, SimMath {actual:R}");
            return;
        }

        if (double.IsInfinity(expected) || expected == 0 || double.IsInfinity(actual) || actual == 0)
        {
            Assert.True(
                expected.Equals(actual) || Math.Abs(Ordered(expected) - Ordered(actual)) <= ulps,
                $"{what}: platform {expected:R}, SimMath {actual:R}");
            return;
        }

        AssertClose(expected, actual, ulps, what);
    }

    // Within a float ulp of the platform's single-precision routine.
    private static void AssertCloseF(float expected, float actual, string what)
    {
        int a = BitConverter.SingleToInt32Bits(expected);
        int b = BitConverter.SingleToInt32Bits(actual);
        long distance = Math.Abs((long)(a < 0 ? int.MinValue - a : a) - (b < 0 ? int.MinValue - b : b));
        Assert.True(distance <= 1, $"{what}: platform {expected:R}, SimMathF {actual:R}, {distance} float ulps apart");
    }

    // A double's bits as an integer that orders like the value, so neighbours differ by one.
    private static long Ordered(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        return bits < 0 ? long.MinValue - bits : bits;
    }
}
