using OpenIGI.Audio;
using Xunit;

namespace OpenIGI.Audio.Tests;

/// <summary>
/// Holds the weather bed's one tap to what it promises: the recording, unaltered when nothing is in
/// the way, and progressively duller as something is.
/// </summary>
/// <remarks>
/// The measurements here are made on signals whose answer is known in advance — a pure tone, a step,
/// a counting ramp — rather than on rain, because "does this sound right" is not a test. What is
/// being pinned is that an open tap is bit-for-bit transparent, that a closed one attenuates by the
/// amount a two-pole filter should, that the loop never runs out, and that a cutoff change is a glide
/// and not a step.
/// </remarks>
public sealed class FilteredLoopSourceTests
{
    private const int Rate = 48000;

    [Fact]
    public void AnOpenTapPassesTheRecordingThroughUntouched()
    {
        short[] pcm = Tone(1000f, 4096);
        FilteredLoopSource tap = new(pcm, Rate, startFrame: 0, cutoffHz: FilteredLoopSource.OpenCutoffHz);

        short[] output = new short[pcm.Length];
        Assert.Equal(output.Length, tap.Read(output));

        // Exactly equal, not nearly. At and above Nyquist the coefficient is forced to zero and both
        // poles become y = x, so an outdoor emitter is the recording rather than the recording through
        // a filter that merely happens to be wide.
        Assert.Equal(pcm, output);
    }

    [Fact]
    public void TheLoopWrapsRatherThanRunningOut()
    {
        short[] pcm = Ramp(1000);
        FilteredLoopSource tap = new(pcm, Rate, startFrame: 0);

        // Two and a half times round. The device's pump treats a short read as the end of a stream, so
        // a tap that ever returned less than it was asked for would be retired mid-storm.
        short[] output = new short[2500];
        Assert.Equal(output.Length, tap.Read(output));

        for (int i = 0; i < output.Length; i++)
        {
            Assert.Equal(pcm[i % pcm.Length], output[i]);
        }
    }

    [Fact]
    public void TapsStartedAtDifferentFramesReadDifferentMaterial()
    {
        short[] pcm = Ramp(600);
        FilteredLoopSource first = new(pcm, Rate, startFrame: 0);
        FilteredLoopSource second = new(pcm, Rate, startFrame: 300);

        short[] a = new short[64];
        short[] b = new short[64];
        first.Read(a);
        second.Read(b);

        // Six emitters reading the same stretch would sum coherently and collapse the field to a
        // point between the speakers; different stretches are what keep it spread out.
        Assert.NotEqual(a, b);
        Assert.Equal(pcm[300], b[0]);
    }

    [Fact]
    public void AStartFrameOutsideTheLoopIsWrappedIntoIt()
    {
        short[] pcm = Ramp(100);

        Assert.Equal(pcm[10], First(new FilteredLoopSource(pcm, Rate, startFrame: 110)));
        Assert.Equal(pcm[90], First(new FilteredLoopSource(pcm, Rate, startFrame: -10)));

        static short First(FilteredLoopSource tap)
        {
            short[] one = new short[1];
            tap.Read(one);
            return one[0];
        }
    }

    [Theory]
    [InlineData(200f, 1.5f)]
    [InlineData(2000f, 22f)]
    [InlineData(8000f, 50f)]
    public void AClosedTapAttenuatesByTheAmountTwoPolesShould(float toneHz, float leastDecibels)
    {
        // A wall's cutoff. A tone below it should come through nearly whole; one well above it should
        // be most of the way gone, and by twelve decibels an octave rather than six.
        const float Cutoff = 420f;

        short[] pcm = Tone(toneHz, Rate);
        FilteredLoopSource tap = new(pcm, Rate, startFrame: 0, cutoffHz: Cutoff);

        short[] output = new short[Rate];
        tap.Read(output);

        // The second half only: the filter's own state has settled by then, so what is measured is the
        // steady-state response and not the transient at the start.
        double loss = 20 * Math.Log10((Rms(pcm.AsSpan(Rate / 2)) / Math.Max(Rms(output.AsSpan(Rate / 2)), 1e-9)) + 1e-12);
        Assert.True(loss >= leastDecibels, $"{toneHz} Hz lost only {loss:0.0} dB through a {Cutoff} Hz filter");
    }

    [Fact]
    public void ACutoffChangeGlidesRatherThanStepping()
    {
        short[] pcm = Tone(8000f, Rate);
        FilteredLoopSource tap = new(pcm, Rate, startFrame: 0, cutoffHz: FilteredLoopSource.OpenCutoffHz);

        // Slam the cutoff shut and watch half a second of it arrive.
        tap.CutoffHz = 200f;
        short[] output = new short[Rate / 2];
        tap.Read(output);

        double unfiltered = Rms(pcm.AsSpan(0, 256));
        double atStart = Rms(output.AsSpan(0, 256));
        double afterAMoment = Rms(output.AsSpan(4096, 256));
        double atEnd = Rms(output.AsSpan(output.Length - 256, 256));

        // The step this exists to rule out would have the whole buffer already filtered. Instead the
        // first few milliseconds are still all but untouched...
        Assert.True(atStart > unfiltered * 0.9, $"the cutoff stepped: {atStart:0} against {unfiltered:0} dry");

        // ...the far end has arrived at the new setting, twelve decibels an octave below the tone...
        Assert.True(atEnd < atStart * 0.1, $"the glide never finished: {atStart:0} then {atEnd:0}");

        // ...and the middle is on its way between the two rather than at either end of the journey.
        Assert.True(
            afterAMoment < atStart && afterAMoment > atEnd,
            $"the glide was not monotone: {atStart:0}, {afterAMoment:0}, {atEnd:0}");
    }

    [Fact]
    public void SnappingTheCutoffSkipsTheGlide()
    {
        short[] pcm = Tone(8000f, Rate);
        FilteredLoopSource tap = new(pcm, Rate, startFrame: 0, cutoffHz: FilteredLoopSource.OpenCutoffHz);
        tap.SnapCutoff(200f);

        short[] output = new short[4096];
        tap.Read(output);

        // A tap created to replace a stolen voice must not be heard opening up over its first fifth of
        // a second, so the first samples have to be as filtered as the last.
        double atStart = Rms(output.AsSpan(512, 256));
        double atEnd = Rms(output.AsSpan(output.Length - 256, 256));
        Assert.True(atStart < atEnd * 2.0, $"snapping still glided: {atStart:0} then {atEnd:0}");
    }

    [Fact]
    public void TheCutoffIsClampedIntoTheRangeThatMeansSomething()
    {
        short[] pcm = Ramp(64);
        FilteredLoopSource tap = new(pcm, Rate);

        tap.CutoffHz = 1_000_000f;
        Assert.Equal(FilteredLoopSource.OpenCutoffHz, tap.CutoffHz);

        tap.CutoffHz = -5f;
        Assert.Equal(FilteredLoopSource.MinimumCutoffHz, tap.CutoffHz, 1);
    }

    [Fact]
    public void TheCoefficientRoundTripsThroughTheCutoffItStandsFor()
    {
        foreach (float cutoff in (float[])[100f, 420f, 3200f, 12000f])
        {
            float coefficient = FilteredLoopSource.CoefficientFor(cutoff, Rate);
            Assert.Equal(cutoff, FilteredLoopSource.CutoffFor(coefficient, Rate), 0);
        }

        // "Open" is the one value that is not a frequency but a state, and it must survive the trip.
        Assert.Equal(0f, FilteredLoopSource.CoefficientFor(FilteredLoopSource.OpenCutoffHz, Rate));
        Assert.Equal(FilteredLoopSource.OpenCutoffHz, FilteredLoopSource.CutoffFor(0f, Rate));
    }

    [Fact]
    public void TheFormatIsTheMonoOneAPositionedVoiceNeeds()
    {
        FilteredLoopSource tap = new(Ramp(64), 44100);

        Assert.Equal(new AudioFormat(44100, 1, 16), tap.Format);
        Assert.True(tap.CanRestart);
    }

    [Fact]
    public void AnEmptyRecordingIsRefused() =>
        Assert.Throws<ArgumentException>(() => new FilteredLoopSource(Array.Empty<short>(), Rate));

    private static short[] Tone(float hertz, int frames)
    {
        short[] pcm = new short[frames];
        for (int i = 0; i < frames; i++)
        {
            pcm[i] = (short)(12000.0 * Math.Sin(2 * Math.PI * hertz * i / Rate));
        }

        return pcm;
    }

    private static short[] Ramp(int frames)
    {
        short[] pcm = new short[frames];
        for (int i = 0; i < frames; i++)
        {
            pcm[i] = (short)(i - (frames / 2));
        }

        return pcm;
    }

    private static double Rms(ReadOnlySpan<short> samples)
    {
        double sum = 0;
        foreach (short sample in samples)
        {
            sum += (double)sample * sample;
        }

        return Math.Sqrt(sum / samples.Length);
    }
}
