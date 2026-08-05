using OpenIGI.Audio;
using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Audio.Tests;

public sealed class VanillaSpatialiserTests
{
    private static readonly AudioListenerState Origin = AudioListenerState.Default;

    [Fact]
    public void TheRolloffIsLinearBetweenTheTwoDistances()
    {
        // Not inverse, not inverse-clamped: a straight line from full gain at the minimum to silence at
        // the maximum. An inverse model would read 0.5 at the halfway point rather than 0.5 by
        // construction, and would put every authored min/max pair to a different use than intended.
        Assert.Equal(1f, VanillaSpatialiser.DistanceGain(1f, 5.0, 10f, 50f));
        Assert.Equal(1f, VanillaSpatialiser.DistanceGain(1f, 10.0, 10f, 50f));
        Assert.Equal(0.75f, VanillaSpatialiser.DistanceGain(1f, 20.0, 10f, 50f), 5);
        Assert.Equal(0.5f, VanillaSpatialiser.DistanceGain(1f, 30.0, 10f, 50f), 5);
        Assert.Equal(0f, VanillaSpatialiser.DistanceGain(1f, 50.0, 10f, 50f), 5);
    }

    [Fact]
    public void BeyondTheMaximumTheVoiceIsSilent()
    {
        Assert.Equal(0f, VanillaSpatialiser.DistanceGain(1f, 50.001, 10f, 50f));
        Assert.Equal(0f, VanillaSpatialiser.DistanceGain(1f, 1000.0, 10f, 50f));
    }

    [Fact]
    public void InsideTheMinimumTheGainIsUntouched()
    {
        // The original does not even clamp here -- it skips the arithmetic entirely.
        Assert.Equal(0.3f, VanillaSpatialiser.DistanceGain(0.3f, 0.0, 10f, 50f));
    }

    [Fact]
    public void ADegenerateRangeIsSilentRatherThanInfinite()
    {
        Assert.Equal(0f, VanillaSpatialiser.DistanceGain(1f, 10.0, 10f, 10f));
    }

    [Fact]
    public void PanIsTheLateralOffsetOverTheSumOfTheDistances()
    {
        // This is the formula's genuinely surprising part: distance cancels out. A source ten metres to
        // the right pans identically whether it is ten metres away or forty, because the divisor is the
        // sum of the min and max distances and nothing else.
        AudioListenerState listener = Origin;

        float near = VanillaSpatialiser.Pan(new Vector3d(10, 0, 0), 10.0, 10f, 50f, listener);
        float far = VanillaSpatialiser.Pan(new Vector3d(10, 40, 0), Math.Sqrt((10 * 10) + (40 * 40)), 10f, 50f, listener);

        Assert.Equal(10f / 60f, near, 5);
        Assert.Equal(near, far, 5);
    }

    [Fact]
    public void PanIsClampedAndCentredOnTopOfTheListener()
    {
        Assert.Equal(1f, VanillaSpatialiser.Pan(new Vector3d(1000, 0, 0), 1000.0, 10f, 50f, Origin));
        Assert.Equal(-1f, VanillaSpatialiser.Pan(new Vector3d(-1000, 0, 0), 1000.0, 10f, 50f, Origin));
        Assert.Equal(0f, VanillaSpatialiser.Pan(Vector3d.Zero, 0.0, 10f, 50f, Origin));
    }

    [Fact]
    public void TurningTheListenerMovesTheSourceAcrossTheStereoImage()
    {
        // A source dead ahead is centred; after a quarter turn the same source is off to one side. This
        // is what pins the v * M convention -- the transposed one sends it the other way.
        Matrix3f quarterTurn = new(0f, 1f, 0f, -1f, 0f, 0f, 0f, 0f, 1f);

        Vector3d ahead = new(0, 10, 0);
        Assert.Equal(0f, VanillaSpatialiser.Pan(ahead, 10.0, 1f, 20f, Origin), 5);

        float turned = VanillaSpatialiser.Pan(ahead, 10.0, 1f, 20f, Origin with { Orientation = quarterTurn });
        Assert.True(Math.Abs(turned) > 0.4f, $"A quarter turn moved the pan only to {turned}.");
    }

    [Fact]
    public void ANonPositionalVoiceKeepsItsOwnGainAndPan()
    {
        VoiceParameters flat = VoiceParameters.Default with { Gain = 0.4f, Pan = -0.8f };
        (float gain, float pan) = VanillaSpatialiser.Spatialise(flat, Origin);

        Assert.Equal(0.4f, gain);
        Assert.Equal(-0.8f, pan);
    }

    [Fact]
    public void APositionalVoiceGetsBothDerived()
    {
        VoiceParameters voice = (VoiceParameters.Default with { Gain = 1f })
            .At(new Vector3d(30, 0, 0), 10f, 50f);

        (float gain, float pan) = VanillaSpatialiser.Spatialise(voice, Origin);

        Assert.Equal(0.5f, gain, 5);
        Assert.Equal(0.5f, pan, 5);
    }

    [Fact]
    public void PitchIsClampedToTheOriginalsRange()
    {
        Assert.Equal(0.5f, VanillaSpatialiser.ClampPitch(0.1f));
        Assert.Equal(2f, VanillaSpatialiser.ClampPitch(10f));
        Assert.Equal(1f, VanillaSpatialiser.ClampPitch(1f));
        Assert.Equal(1f, VanillaSpatialiser.ClampPitch(float.NaN));
    }
}
