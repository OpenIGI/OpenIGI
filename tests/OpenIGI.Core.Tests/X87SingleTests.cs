using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Core.Tests;

/// <summary>The 24-bit truncating register model I.G.I. 2's client computes with.</summary>
public sealed class X87SingleTests
{
    /// <summary>A sum keeps a 24-bit significand, truncated, not the nearest one.</summary>
    [Fact]
    public void A_sum_truncates_to_24_bits()
    {
        // 1 + 2^-24 + 2^-25 lies between two 24-bit values; nearest would round up, truncation does not.
        X87Single sum = (X87Single)1.0 + (Math.Pow(2, -24) + Math.Pow(2, -25));

        Assert.Equal(1.0, sum.Value);
        Assert.Equal(1f, sum.F);
    }

    /// <summary>Truncation is toward zero on either side of it.</summary>
    [Fact]
    public void Truncation_is_toward_zero_for_negatives()
    {
        X87Single sum = (X87Single)(-1.0) - (Math.Pow(2, -24) + Math.Pow(2, -25));

        Assert.Equal(-1.0, sum.Value);
    }

    /// <summary>A loaded double keeps its full precision until an operation rounds the result.</summary>
    [Fact]
    public void A_loaded_double_is_exact_until_used()
    {
        double wide = 1.0 + Math.Pow(2, -40);
        X87Single loaded = wide;

        Assert.Equal(wide, loaded.Value);
        Assert.Equal(1.0, (loaded * 1f).Value);
    }

    /// <summary>A product of two floats is truncated, where .NET float arithmetic rounds to nearest.</summary>
    [Fact]
    public void A_float_product_truncates_where_dotnet_rounds()
    {
        const float a = 1.1f;
        const float b = 1.3f;

        float truncated = ((X87Single)a * b).F;

        Assert.True(truncated <= a * b);
        Assert.Equal(X87.ToFloat((double)a * b), truncated);
    }

    /// <summary>A store of an in-range value is exact: the register already holds a float's significand.</summary>
    [Fact]
    public void A_store_of_a_rounded_value_is_exact()
    {
        X87Single third = (X87Single)1f / 3f;

        Assert.Equal(third.Value, (double)third.F);
    }

    /// <summary><c>fistp</c> truncates.</summary>
    [Theory]
    [InlineData(7.9, 7)]
    [InlineData(-7.9, -7)]
    public void Integer_conversion_truncates(double value, int expected) =>
        Assert.Equal(expected, X87Single.ToInt32(value));
}
