using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Pins the root-motion sampler against the arithmetic in <c>0x4D4840</c>.
/// </summary>
public sealed class BoneTrackSamplerTests
{
    [Fact]
    public void AnEmptyTrackSamplesToZero() =>
        Assert.Equal(default, BoneTrackSampler.Sample([], 5f));

    [Fact]
    public void ASingleKeyTrackHoldsItsKeyEverywhere()
    {
        BoneTranslationKey[] keys = [Key(new Vector3f(1f, 2f, 3f), 100f)];

        Assert.Equal(new Vector3f(1f, 2f, 3f), BoneTrackSampler.Sample(keys, 0f));
        Assert.Equal(new Vector3f(1f, 2f, 3f), BoneTrackSampler.Sample(keys, 100f));
        Assert.Equal(new Vector3f(1f, 2f, 3f), BoneTrackSampler.Sample(keys, 5000f));
    }

    [Fact]
    public void PastTheLastKeyItHolds()
    {
        BoneTranslationKey[] keys = [Key(default, 0f), Key(new Vector3f(10f, 0f, 0f), 160f)];

        Assert.Equal(new Vector3f(10f, 0f, 0f), BoneTrackSampler.Sample(keys, 1000f));
    }

    [Fact]
    public void AnExactHitTakesTheKeyUntouched()
    {
        // The original compares the scan's key time against the sample time and short-circuits, so a
        // key with wild tangents still reports exactly its own position when landed on.
        BoneTranslationKey[] keys =
        [
            Key(default, 0f),
            new BoneTranslationKey(
                new Vector3f(10f, 0f, 0f), 160f, new Vector3f(999f, 999f, 999f), new Vector3f(-999f, 0f, 0f)),
        ];

        Assert.Equal(new Vector3f(10f, 0f, 0f), BoneTrackSampler.Sample(keys, 160f));
    }

    [Fact]
    public void TheLinearArmIsAPlainLerp()
    {
        BoneTranslationKey[] keys =
        [
            Key(default, 0f),
            new BoneTranslationKey(
                new Vector3f(100f, 0f, 0f), 100f, new Vector3f(50f, 0f, 0f), new Vector3f(50f, 0f, 0f)),
        ];

        // Halfway, ignoring the tangents entirely.
        Vector3f mid = BoneTrackSampler.Sample(keys, 50f, spline: false);
        Assert.Equal(50f, mid.X, 4);
    }

    [Fact]
    public void TheHermiteArmMatchesTheBasisAtTheEndpoints()
    {
        // h00/h01/h10/h11 are 1/0/0/0 at s=0 and 0/1/0/0 at s=1, so a segment starts and ends exactly
        // on its keys whatever the tangents are. Anything that mixes the tangents in at the endpoints
        // has the basis wrong.
        BoneTranslationKey[] keys =
        [
            new BoneTranslationKey(default, 0f, new Vector3f(7f, 0f, 0f), default),
            new BoneTranslationKey(
                new Vector3f(100f, 0f, 0f), 100f, default, new Vector3f(-13f, 0f, 0f)),
        ];

        Assert.Equal(0f, BoneTrackSampler.Sample(keys, 0.0001f).X, 2);
        Assert.Equal(100f, BoneTrackSampler.Sample(keys, 100f).X, 4);
    }

    [Fact]
    public void ZeroTangentsMakeHermiteASmoothstep()
    {
        // With both tangents zero the basis collapses to 3s^2 - 2s^3, so the midpoint is exactly half
        // and the curve is symmetric — a cheap closed form to check the cubic against.
        BoneTranslationKey[] keys =
        [
            Key(default, 0f),
            Key(new Vector3f(100f, 0f, 0f), 100f),
        ];

        Assert.Equal(50f, BoneTrackSampler.Sample(keys, 50f).X, 4);
        Assert.Equal(15.625f, BoneTrackSampler.Sample(keys, 25f).X, 3);
        Assert.Equal(84.375f, BoneTrackSampler.Sample(keys, 75f).X, 3);
    }

    [Fact]
    public void TangentsAreScaledByTheSegmentDuration()
    {
        // The original multiplies each tangent by the segment's duration, so the same tangent value
        // bends a long segment more than a short one. Without the scale both would bend identically.
        Vector3f tangent = new(1f, 0f, 0f);
        BoneTranslationKey[] shortSegment =
        [
            new BoneTranslationKey(default, 0f, tangent, default),
            new BoneTranslationKey(new Vector3f(100f, 0f, 0f), 10f, default, tangent),
        ];
        BoneTranslationKey[] longSegment =
        [
            new BoneTranslationKey(default, 0f, tangent, default),
            new BoneTranslationKey(new Vector3f(100f, 0f, 0f), 1000f, default, tangent),
        ];

        // At the midpoint h10 and h11 are both 0.125, so the tangents contribute duration * 0.25 on
        // top of the 50 the positions give: 52.5 over ten units of time and 300 over a thousand.
        Assert.Equal(52.5f, BoneTrackSampler.Sample(shortSegment, 5f).X, 3);
        Assert.Equal(300f, BoneTrackSampler.Sample(longSegment, 500f).X, 3);
    }

    [Fact]
    public void ADegenerateSegmentDoesNotDivideByZero()
    {
        // Two keys at the same time would make the parameter infinite. The retail data has only
        // non-decreasing times, so this is a guard rather than a reproduction of anything.
        BoneTranslationKey[] keys = [Key(default, 50f), Key(new Vector3f(9f, 9f, 9f), 50f)];

        Vector3f sampled = BoneTrackSampler.Sample(keys, 50f);
        Assert.True(float.IsFinite(sampled.X));
    }

    private static BoneTranslationKey Key(Vector3f position, float time) =>
        new(position, time, default, default);
}
