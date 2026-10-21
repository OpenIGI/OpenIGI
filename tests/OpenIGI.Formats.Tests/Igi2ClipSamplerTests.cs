using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>The sequel's clip sampling, pinned to <c>Quat32_SLERP</c> and the controller's interpolation.</summary>
public sealed class Igi2ClipSamplerTests
{
    private static readonly Quaternion32 Identity = new(0f, 0f, 0f, 1f);

    // A quarter turn about Z.
    private static readonly Quaternion32 QuarterZ = new(0f, 0f, MathF.Sqrt(0.5f), MathF.Sqrt(0.5f));

    /// <summary>The slerp returns its ends at its ends.</summary>
    [Fact]
    public void Slerp_returns_its_ends_at_its_ends()
    {
        AssertClose(Identity, Igi2ClipSampler.Slerp(Identity, QuarterZ, 0.0));
        AssertClose(QuarterZ, Igi2ClipSampler.Slerp(Identity, QuarterZ, 1.0));
    }

    /// <summary>Half way is the eighth turn, to the precision of the polynomial arc-cosine.</summary>
    [Fact]
    public void Slerp_half_way_is_half_the_angle()
    {
        Quaternion32 eighth = new(0f, 0f, MathF.Sin(MathF.PI / 8f), MathF.Cos(MathF.PI / 8f));
        AssertClose(eighth, Igi2ClipSampler.Slerp(Identity, QuarterZ, 0.5), 1e-4f);
    }

    /// <summary>A negative dot takes the shorter arc by negating the start, so the result is the flipped path.</summary>
    [Fact]
    public void Slerp_takes_the_shorter_arc()
    {
        Quaternion32 negated = new(-QuarterZ.X, -QuarterZ.Y, -QuarterZ.Z, -QuarterZ.W);
        Quaternion32 viaShort = Igi2ClipSampler.Slerp(Identity, negated, 0.5);
        Quaternion32 viaLong = Igi2ClipSampler.Slerp(Identity, QuarterZ, 0.5);

        // Same rotation, opposite sign.
        AssertClose(new Quaternion32(-viaLong.X, -viaLong.Y, -viaLong.Z, -viaLong.W), viaShort, 1e-4f);
    }

    /// <summary>Nearly equal orientations are lerped without renormalising.</summary>
    [Fact]
    public void Slerp_lerps_near_equal_orientations()
    {
        Quaternion32 nudge = new(0f, 0f, 0.001f, 1f);
        Quaternion32 result = Igi2ClipSampler.Slerp(Identity, nudge, 0.5);
        Assert.Equal(0.0005f, result.Z, 1e-7f);
        Assert.Equal(1f, result.W);
    }

    /// <summary>Root translation is linear between keys and holds after the last.</summary>
    [Fact]
    public void Translation_is_linear_and_holds()
    {
        Igi2TranslationKey[] track =
        [
            new(0, new Vector3f(0f, 0f, 0f)),
            new(100, new Vector3f(10f, 20f, 40f)),
        ];

        Assert.Equal(new Vector3f(2.5f, 5f, 10f), Igi2ClipSampler.SampleTranslation(track, 25));
        Assert.Equal(new Vector3f(10f, 20f, 40f), Igi2ClipSampler.SampleTranslation(track, 500));
    }

    /// <summary>SQUAD and slerp agree at the keys, where the tangent blend weight is zero.</summary>
    [Fact]
    public void Squad_meets_the_keys()
    {
        Igi2RotationKey[] track =
        [
            new(0, Identity, QuarterZ, QuarterZ),
            new(100, QuarterZ, Identity, Identity),
        ];

        AssertClose(Identity, Igi2ClipSampler.SampleRotation(track, 0, squad: true));
        AssertClose(QuarterZ, Igi2ClipSampler.SampleRotation(track, 100, squad: true));
    }

    /// <summary>An empty rotation track is the identity.</summary>
    [Fact]
    public void An_empty_track_is_the_identity()
    {
        Assert.Equal(Identity, Igi2ClipSampler.SampleRotation([], 0, squad: true));
    }

    private static void AssertClose(Quaternion32 expected, Quaternion32 actual, float tolerance = 1e-6f)
    {
        Assert.Equal(expected.X, actual.X, tolerance);
        Assert.Equal(expected.Y, actual.Y, tolerance);
        Assert.Equal(expected.Z, actual.Z, tolerance);
        Assert.Equal(expected.W, actual.W, tolerance);
    }
}
