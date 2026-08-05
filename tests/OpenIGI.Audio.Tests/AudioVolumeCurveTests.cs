using OpenIGI.Audio;
using Xunit;

namespace OpenIGI.Audio.Tests;

public sealed class AudioVolumeCurveTests
{
    [Fact]
    public void FullScaleIsZeroAndSilenceIsTheFloor()
    {
        Assert.Equal(0, AudioVolumeCurve.ToMillibels(1f));
        Assert.Equal(AudioVolumeCurve.MinimumMillibels, AudioVolumeCurve.ToMillibels(0f));
        Assert.Equal(AudioVolumeCurve.MinimumMillibels, AudioVolumeCurve.ToMillibels(-1f));
        Assert.Equal(AudioVolumeCurve.MinimumMillibels, AudioVolumeCurve.ToMillibels(float.NaN));
    }

    [Fact]
    public void EachHalvingCostsOneThousand_ButLandsOneShort()
    {
        // The constant at 0x4952F0 scales a NATURAL logarithm, which makes the unit 1000 per octave --
        // not hundredths of a decibel, even though that is how DirectSound then reads it. Getting that
        // wrong makes every fade in the game 1.66x too steep or too shallow.
        //
        // The values are 999 and not 1000 on purpose, and it is not rounding sloppiness in the port.
        // The literal the original stores, 1442.695040888849, is a hair BELOW 1000/ln(2), so a halving
        // computes to -999.99999999992 -- and the cast truncates toward zero. Every exact power of two
        // therefore lands one unit short of the round number, in the original as much as here. Writing
        // the round numbers into this test is what would make the port diverge.
        Assert.Equal(-999, AudioVolumeCurve.ToMillibels(0.5f));
        Assert.Equal(-1999, AudioVolumeCurve.ToMillibels(0.25f));
        Assert.Equal(-2999, AudioVolumeCurve.ToMillibels(0.125f));
    }

    [Fact]
    public void TheCurveIsSteeperThanDecibels()
    {
        // True amplitude decibels would put a halving at -602. If a future change quietly "corrects"
        // the curve to real dB, this is the test that says no.
        int halving = AudioVolumeCurve.ToMillibels(0.5f);
        Assert.True(halving < -900, $"A halving read {halving}; the original's curve gives -1000.");
    }

    [Fact]
    public void TheFloorIsReachedAtTenOctavesDown()
    {
        // -10000 is exactly ten halvings, so 2^-10 is the quietest gain that is not simply silence.
        Assert.Equal(AudioVolumeCurve.MinimumMillibels, AudioVolumeCurve.ToMillibels(1f / 1024f));
        Assert.True(AudioVolumeCurve.ToMillibels(1f / 512f) > AudioVolumeCurve.MinimumMillibels);
    }

    [Fact]
    public void TheCurveIsMonotonic()
    {
        int previous = AudioVolumeCurve.MinimumMillibels - 1;
        for (int i = 0; i <= 10000; i++)
        {
            int current = AudioVolumeCurve.ToMillibels(i / 10000f);
            Assert.True(current >= previous, $"Entry {i} went backwards: {current} after {previous}.");
            previous = current;
        }

        Assert.Equal(0, previous);
    }

    [Fact]
    public void GainsAreQuantisedToTheTable()
    {
        // The original looks up a truncated index, so gains inside one step are indistinguishable.
        // Reproducing that is what makes the vanilla profile match rather than merely resemble.
        Assert.Equal(AudioVolumeCurve.ToMillibels(0.50000f), AudioVolumeCurve.ToMillibels(0.500009f));
    }

    [Fact]
    public void RoundTrippingAGainStaysClose()
    {
        foreach (float gain in (float[])[1f, 0.75f, 0.5f, 0.25f, 0.1f, 0.01f])
        {
            float quantised = AudioVolumeCurve.Quantise(gain);
            Assert.InRange(quantised, gain * 0.98f, gain * 1.02f);
        }

        Assert.Equal(0f, AudioVolumeCurve.Quantise(0f));
    }

    [Fact]
    public void PanningSignsTheAttenuationOfTheOppositeChannel()
    {
        // Centre attenuates nothing on either side; hard over attenuates the far channel to the floor.
        Assert.Equal(0, AudioVolumeCurve.PanToMillibels(0f));
        Assert.Equal(AudioVolumeCurve.MinimumMillibels, AudioVolumeCurve.PanToMillibels(1f));
        Assert.Equal(-AudioVolumeCurve.MinimumMillibels, AudioVolumeCurve.PanToMillibels(-1f));
    }

    [Fact]
    public void ReverseStereoFlipsTheSideWithoutChangingTheAmount()
    {
        int normal = AudioVolumeCurve.PanToMillibels(0.5f);
        int reversed = AudioVolumeCurve.PanToMillibels(0.5f, reverseStereo: true);

        Assert.Equal(-normal, reversed);
        Assert.NotEqual(0, normal);
    }
}
