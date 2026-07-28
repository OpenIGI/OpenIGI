using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Pins the rotation sampler against <c>0x4D5180</c> — slerp, SQUAD, and the cyclic wrap.
/// </summary>
public sealed class BoneRotationSamplerTests
{
    private static readonly Quaternion32 Identity = new(0f, 0f, 0f, 1f);

    [Fact]
    public void AnEmptyTrackSamplesToIdentity() =>
        Assert.Equal(Identity, BoneRotationSampler.Sample([], 10f, 100f));

    [Fact]
    public void ASingleKeyTrackHoldsItsKey()
    {
        Quaternion32 q = AboutZ(1.0f);
        BoneRotationKey[] keys = [Key(q, 0f)];

        AssertClose(q, BoneRotationSampler.Sample(keys, 0f, 100f));
        AssertClose(q, BoneRotationSampler.Sample(keys, 5000f, 100f));
    }

    [Fact]
    public void SlerpTravelsAtConstantAngularSpeed()
    {
        // The defining property: halfway in parameter is halfway in angle. A nlerp would not be.
        Quaternion32 a = AboutZ(0f);
        Quaternion32 b = AboutZ(1.5f);

        Quaternion32 mid = BoneRotationSampler.Slerp(a, b, 0.5f);
        AssertClose(AboutZ(0.75f), mid);

        Quaternion32 quarter = BoneRotationSampler.Slerp(a, b, 0.25f);
        AssertClose(AboutZ(0.375f), quarter);
    }

    [Fact]
    public void SlerpTakesTheShorterArc()
    {
        // b and -b are the same rotation. Interpolating towards the negated one must not go the long
        // way round: the original encodes this by adding a full turn to the baked angle.
        Quaternion32 a = AboutZ(0f);
        Quaternion32 b = AboutZ(1.0f);
        Quaternion32 negated = new(-b.X, -b.Y, -b.Z, -b.W);

        Quaternion32 viaB = BoneRotationSampler.Slerp(a, b, 0.5f);
        Quaternion32 viaNegated = BoneRotationSampler.Slerp(a, negated, 0.5f);

        AssertClose(viaB, viaNegated);
    }

    [Fact]
    public void SlerpEndpointsAreExact()
    {
        Quaternion32 a = AboutZ(0.2f);
        Quaternion32 b = AboutZ(1.1f);

        AssertClose(a, BoneRotationSampler.Slerp(a, b, 0f));
        AssertClose(b, BoneRotationSampler.Slerp(a, b, 1f));
    }

    [Fact]
    public void AZeroAngleReturnsTheSourceUntouched()
    {
        Quaternion32 a = AboutZ(0.4f);
        Assert.Equal(a, BoneRotationSampler.Slerp(a, a, 0.5f));
    }

    [Fact]
    public void ATinyAngleFallsBackToALerp()
    {
        // Below 0.01 radians the original stops dividing by the sine. The result is still sane; what
        // matters is that it does not blow up.
        Quaternion32 a = AboutZ(0f);
        Quaternion32 b = AboutZ(0.001f);

        Quaternion32 mid = BoneRotationSampler.Slerp(a, b, 0.5f);
        Assert.True(float.IsFinite(mid.X) && float.IsFinite(mid.W));
        AssertClose(AboutZ(0.0005f), mid, 4);
    }

    [Fact]
    public void AnExactKeyHitTakesTheKeyUntouched()
    {
        // The key carries deliberately wild tangents; landing exactly on it must ignore them.
        BoneRotationKey[] keys =
        [
            Key(AboutZ(0f), 0f),
            new BoneRotationKey(AboutZ(1f), 160f, AboutZ(3f), AboutZ(-3f)),
        ];

        AssertClose(AboutZ(1f), BoneRotationSampler.Sample(keys, 160f, 320f));
    }

    [Fact]
    public void PastTheLastKeyANonCyclicClipHolds()
    {
        BoneRotationKey[] keys = [Key(AboutZ(0f), 0f), Key(AboutZ(1f), 160f)];

        AssertClose(AboutZ(1f), BoneRotationSampler.Sample(keys, 300f, 480f, cyclic: false));
    }

    [Fact]
    public void PastTheLastKeyACyclicClipWrapsBackToKeyZero()
    {
        // This is the bit-31 branch. Between the last key at 160 and the duration at 320, the pose
        // travels from the last key back to the first — which is why the two need not be equal.
        BoneRotationKey[] keys = [Key(AboutZ(0f), 0f), Key(AboutZ(1f), 160f)];

        Quaternion32 halfway = BoneRotationSampler.Sample(keys, 240f, 320f, cyclic: true, spline: false);

        AssertClose(AboutZ(0.5f), halfway);

        // ...and it arrives exactly on key 0 at the duration.
        AssertClose(AboutZ(0f), BoneRotationSampler.Sample(keys, 320f, 320f, cyclic: true, spline: false));
    }

    [Fact]
    public void ACyclicClipWithNoRoomToWrapStillHolds()
    {
        // A duration that does not exceed the last key's time leaves nothing to interpolate over; the
        // guard keeps that from dividing by zero.
        BoneRotationKey[] keys = [Key(AboutZ(0f), 0f), Key(AboutZ(1f), 160f)];

        AssertClose(AboutZ(1f), BoneRotationSampler.Sample(keys, 200f, 160f, cyclic: true));
    }

    [Fact]
    public void SquadAgreesWithSlerpWhenTheTangentsMatchTheKeys()
    {
        // With each tangent equal to its own key, the tangent slerp equals the key slerp, so blending
        // them is a no-op whatever the weight. Any error in the SQUAD weighting shows up here.
        Quaternion32 a = AboutZ(0f);
        Quaternion32 b = AboutZ(1.2f);
        BoneRotationKey[] keys =
        [
            new BoneRotationKey(a, 0f, a, a),
            new BoneRotationKey(b, 100f, b, b),
        ];

        for (float t = 10f; t < 100f; t += 10f)
        {
            AssertClose(
                BoneRotationSampler.Sample(keys, t, 200f, spline: false),
                BoneRotationSampler.Sample(keys, t, 200f, spline: true),
                4);
        }
    }

    [Fact]
    public void SquadEndpointsStillLandOnTheKeys()
    {
        // 2s(1-s) is zero at both ends, so the outer blend returns the key slerp untouched there
        // however the tangents are pointed.
        Quaternion32 a = AboutZ(0f);
        Quaternion32 b = AboutZ(1.2f);
        BoneRotationKey[] keys =
        [
            new BoneRotationKey(a, 0f, AboutZ(2.5f), a),
            new BoneRotationKey(b, 100f, b, AboutZ(-2.5f)),
        ];

        AssertClose(a, BoneRotationSampler.Sample(keys, 0.001f, 200f), 3);
        AssertClose(b, BoneRotationSampler.Sample(keys, 100f, 200f));
    }

    [Fact]
    public void TheBlendCutoffsReturnTheirArgumentWhole()
    {
        Quaternion32 a = AboutZ(0f);
        Quaternion32 b = AboutZ(1f);

        Assert.Equal(b, BoneRotationSampler.Blend(a, b, 0f));
        Assert.Equal(b, BoneRotationSampler.Blend(a, b, BoneRotationSampler.BlendLowerCutoff));
        Assert.Equal(a, BoneRotationSampler.Blend(a, b, 1f));
        Assert.Equal(a, BoneRotationSampler.Blend(a, b, BoneRotationSampler.BlendUpperCutoff));
    }

    [Fact]
    public void EverySampleOfARetailTrackStaysUnit()
    {
        // Slerp preserves unit length; SQUAD's outer slerp does too. Sweeping a segment catches a
        // weighting mistake that the endpoint tests would miss.
        BoneRotationKey[] keys =
        [
            new BoneRotationKey(AboutZ(0f), 0f, AboutZ(0.1f), AboutZ(0f)),
            new BoneRotationKey(AboutZ(0.9f), 160f, AboutZ(0.9f), AboutZ(0.8f)),
            new BoneRotationKey(AboutZ(0.3f), 480f, AboutZ(0.3f), AboutZ(0.4f)),
        ];

        for (float t = 0f; t <= 480f; t += 7f)
        {
            Quaternion32 q = BoneRotationSampler.Sample(keys, t, 640f);
            float length = MathF.Sqrt((q.X * q.X) + (q.Y * q.Y) + (q.Z * q.Z) + (q.W * q.W));
            Assert.Equal(1f, length, 3);
        }
    }

    private static Quaternion32 AboutZ(float radians) =>
        new(0f, 0f, MathF.Sin(radians * 0.5f), MathF.Cos(radians * 0.5f));

    private static BoneRotationKey Key(Quaternion32 rotation, float time) =>
        new(rotation, time, rotation, rotation);

    private static void AssertClose(Quaternion32 expected, Quaternion32 actual, int precision = 5)
    {
        // q and -q are the same rotation, so compare against whichever representative is nearer.
        float dot = (expected.X * actual.X) + (expected.Y * actual.Y)
            + (expected.Z * actual.Z) + (expected.W * actual.W);
        if (dot < 0f)
        {
            actual = new Quaternion32(-actual.X, -actual.Y, -actual.Z, -actual.W);
        }

        Assert.Equal(expected.X, actual.X, precision);
        Assert.Equal(expected.Y, actual.Y, precision);
        Assert.Equal(expected.Z, actual.Z, precision);
        Assert.Equal(expected.W, actual.W, precision);
    }
}
