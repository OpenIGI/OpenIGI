using OpenIGI.Core.Numerics;

namespace OpenIGI.Host.Audio;

/// <summary>The remaster's synthesised sounds' common ingredients: noise struck through resonances.</summary>
internal static class ResonantSynthesis
{
    /// <summary>The rate every synthesised sound is made at.</summary>
    public const int SampleRate = 44100;

    /// <summary>A soft, low, heavily damped knock: a hand on a plate, a thumb on a boot, a core settling.</summary>
    /// <param name="samples">The sound, added into.</param>
    /// <param name="start">When the knock lands, in seconds.</param>
    /// <param name="gain">How loud it is.</param>
    /// <param name="hertz">Its body's pitch.</param>
    /// <param name="seed">The noise's seed.</param>
    public static void Knock(float[] samples, float start, float gain, float hertz, uint seed)
    {
        int first = (int)(start * SampleRate);
        int length = samples.Length - first;
        if (length <= 0)
        {
            return;
        }

        float[] excitation = new float[length];
        uint state = seed;
        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleRate;
            excitation[i] = XorShift32.NextSigned(ref state) * (1f - MathF.Exp(-t / 0.0006f)) * MathF.Exp(-t / 0.002f);
        }

        Resonate(samples, first, excitation, hertz, 1.5f, gain);
        Resonate(samples, first, excitation, hertz * 2.8f, 2.5f, gain * 0.4f);
    }

    /// <summary>A band of noise that swells or strikes, holds and dies away: a hiss, a click, a wet splatter.</summary>
    /// <param name="samples">The sound, added into.</param>
    /// <param name="start">When it starts, in seconds.</param>
    /// <param name="gain">How loud it is at its height.</param>
    /// <param name="band">The band it is filtered to, in hertz: a two-pole high-pass then a two-pole low-pass.</param>
    /// <param name="shape">Its envelope, in seconds: the attack, how long it holds, and its time constant after.</param>
    /// <param name="seed">The noise's seed.</param>
    /// <param name="grain">How grainy it is, from nought (smooth) to one (crackling bursts).</param>
    /// <param name="swell">True for an attack that rises exponentially, gathering to its height, rather than linearly.</param>
    public static void Noise(float[] samples, float start, float gain, (float Low, float High) band, (float Attack, float Hold, float Decay) shape, uint seed, float grain = 0f, bool swell = false)
    {
        int first = (int)(start * SampleRate);
        int length = samples.Length - first;
        if (length <= 0)
        {
            return;
        }

        float[] noise = new float[length];
        uint state = seed;
        float burst = 1f;
        int burstLeft = 0;
        for (int i = 0; i < length; i++)
        {
            if (grain > 0f && --burstLeft <= 0)
            {
                // A crackle: bursts of a few milliseconds, each at its own level.
                burstLeft = 40 + (int)(XorShift32.NextUnit(ref state) * 200f);
                burst = 1f - (grain * XorShift32.NextUnit(ref state) * XorShift32.NextUnit(ref state) * 1.6f);
            }

            noise[i] = XorShift32.NextSigned(ref state) * Math.Max(0f, burst);
        }

        Filter(noise, band.Low, highPass: true);
        Filter(noise, band.High, highPass: false);
        Filter(noise, band.Low, highPass: true);
        Filter(noise, band.High, highPass: false);
        for (int i = 0; i < length; i++)
        {
            samples[first + i] += gain * Envelope(i / (float)SampleRate, shape, swell) * noise[i];
        }
    }

    /// <summary>A cluster of steady tones that swells or strikes, holds and dies away: a metallic shimmer, a ring.</summary>
    /// <param name="samples">The sound, added into.</param>
    /// <param name="start">When it starts, in seconds.</param>
    /// <param name="gain">How loud each tone is at its height.</param>
    /// <param name="hertz">The tones.</param>
    /// <param name="shape">Its envelope, in seconds: the attack, how long it holds, and its time constant after.</param>
    /// <param name="seed">The seed of the tones' starting phases.</param>
    /// <param name="swell">True for an attack that rises exponentially, gathering to its height, rather than linearly.</param>
    public static void Tones(float[] samples, float start, float gain, ReadOnlySpan<float> hertz, (float Attack, float Hold, float Decay) shape, uint seed, bool swell = false)
    {
        int first = (int)(start * SampleRate);
        uint state = seed;
        foreach (float tone in hertz)
        {
            float phase = XorShift32.NextUnit(ref state) * MathF.Tau;
            float step = MathF.Tau * Math.Min(tone, SampleRate * 0.45f) / SampleRate;
            for (int i = first; i < samples.Length; i++)
            {
                samples[i] += gain * Envelope((i - first) / (float)SampleRate, shape, swell) * MathF.Sin(phase + (step * (i - first)));
            }
        }
    }

    /// <summary>A tone whose pitch and level move between points, joined in straight lines (its level in decibels): a sub drop that holds and steps down.</summary>
    /// <param name="samples">The sound, added into.</param>
    /// <param name="start">When it starts, in seconds.</param>
    /// <param name="gain">How loud it is at nought decibels.</param>
    /// <param name="points">Each point's time from the start in seconds, its pitch, and its level in decibels; it falls silent after the last.</param>
    public static void Sweep(float[] samples, float start, float gain, ReadOnlySpan<(float Time, float Hertz, float Level)> points)
    {
        int first = (int)(start * SampleRate);
        double phase = 0.0;
        int segment = 0;
        for (int i = first; i < samples.Length && points.Length > 1; i++)
        {
            float t = (i - first) / (float)SampleRate;
            while (segment < points.Length - 2 && t > points[segment + 1].Time)
            {
                segment++;
            }

            (float t0, float f0, float l0) = points[segment];
            (float t1, float f1, float l1) = points[segment + 1];
            if (t > t1)
            {
                return;
            }

            float k = Math.Clamp((t - t0) / MathF.Max(t1 - t0, 1e-5f), 0f, 1f);
            phase += Math.Tau * (f0 + ((f1 - f0) * k)) / SampleRate;
            samples[i] += gain * MathF.Pow(10f, (l0 + ((l1 - l0) * k)) / 20f) * (float)Math.Sin(phase);
        }
    }

    // An attack, a hold at full and an exponential fall; a swelling attack rises thirty-two decibels over its length.
    private static float Envelope(float t, (float Attack, float Hold, float Decay) shape, bool swell) =>
        t < shape.Attack
            ? (swell ? MathF.Exp((t - shape.Attack) / MathF.Max(shape.Attack / 7.4f, 1e-4f)) : t / MathF.Max(shape.Attack, 1e-4f))
            : t < shape.Attack + shape.Hold ? 1f : MathF.Exp(-(t - shape.Attack - shape.Hold) / MathF.Max(shape.Decay, 1e-4f));

    // A two-pole Butterworth high- or low-pass (the audio EQ cookbook's), run in place.
    private static void Filter(float[] samples, float hertz, bool highPass)
    {
        float w = MathF.Tau * Math.Clamp(hertz, 10f, SampleRate * 0.45f) / SampleRate;
        float alpha = MathF.Sin(w) / (2f * 0.70710678f);
        float cos = MathF.Cos(w);
        float a0 = 1f + alpha;
        float b1 = (highPass ? -(1f + cos) : 1f - cos) / a0;
        float b0 = (highPass ? (1f + cos) * 0.5f : (1f - cos) * 0.5f) / a0;
        float a1 = -2f * cos / a0;
        float a2 = (1f - alpha) / a0;
        float x1 = 0f, x2 = 0f, y1 = 0f, y2 = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float x = samples[i];
            float y = (b0 * x) + (b1 * x1) + (b0 * x2) - (a1 * y1) - (a2 * y2);
            x2 = x1;
            x1 = x;
            y2 = y1;
            y1 = y;
            samples[i] = y;
        }
    }

    /// <summary>A tone that glides from one pitch to another and dies away: a swell before a hit, or a low drop under it.</summary>
    /// <param name="samples">The sound, added into.</param>
    /// <param name="start">When it starts, in seconds.</param>
    /// <param name="gain">How loud it is at its height.</param>
    /// <param name="from">Its pitch as it starts.</param>
    /// <param name="to">Its pitch as it ends.</param>
    /// <param name="glide">How long the glide takes, in seconds.</param>
    /// <param name="rise">How long it takes to swell to its height, in seconds.</param>
    /// <param name="decay">Its time constant once at its height, in seconds.</param>
    public static void Glide(float[] samples, float start, float gain, float from, float to, float glide, float rise, float decay)
    {
        int first = (int)(start * SampleRate);
        double phase = 0.0;
        for (int i = first; i < samples.Length; i++)
        {
            float t = (i - first) / (float)SampleRate;
            float hertz = from + ((to - from) * Math.Clamp(t / MathF.Max(glide, 1e-4f), 0f, 1f));
            phase += Math.Tau * hertz / SampleRate;
            float envelope = t < rise ? t / MathF.Max(rise, 1e-4f) : MathF.Exp(-(t - rise) / decay);
            samples[i] += gain * envelope * (float)Math.Sin(phase);
        }
    }

    /// <summary>A two-pole band-pass at unit peak gain (the audio EQ cookbook's), run over an excitation and added in.</summary>
    /// <param name="samples">The sound, added into.</param>
    /// <param name="first">The sample the excitation starts at.</param>
    /// <param name="excitation">What strikes the resonance.</param>
    /// <param name="hertz">The resonance, clamped under Nyquist.</param>
    /// <param name="q">Its sharpness.</param>
    /// <param name="gain">How loud it rings.</param>
    public static void Resonate(float[] samples, int first, float[] excitation, float hertz, float q, float gain)
    {
        float w = MathF.Tau * Math.Min(hertz, SampleRate * 0.45f) / SampleRate;
        float alpha = MathF.Sin(w) / (2f * q);
        float a0 = 1f + alpha;
        float b0 = alpha / a0;
        float b2 = -alpha / a0;
        float a1 = -2f * MathF.Cos(w) / a0;
        float a2 = (1f - alpha) / a0;

        int count = Math.Min(excitation.Length, samples.Length - first);
        float x1 = 0f, x2 = 0f, y1 = 0f, y2 = 0f;
        for (int i = 0; i < count; i++)
        {
            float x = excitation[i];
            float y = (b0 * x) + (b2 * x2) - (a1 * y1) - (a2 * y2);
            x2 = x1;
            x1 = x;
            y2 = y1;
            y1 = y;
            samples[first + i] += gain * y;
        }
    }

    /// <summary>Normalises samples to a peak and writes them out as little-endian sixteen-bit PCM.</summary>
    /// <param name="samples">The samples.</param>
    /// <param name="peakLevel">The peak, as a fraction of full scale: how loud each sound is against another is the player's gain.</param>
    /// <returns>The PCM.</returns>
    public static byte[] ToPcm(float[] samples, float peakLevel)
    {
        float peak = 1e-6f;
        foreach (float sample in samples)
        {
            peak = MathF.Max(peak, MathF.Abs(sample));
        }

        byte[] pcm = new byte[samples.Length * 2];
        float scale = peakLevel / peak;
        for (int i = 0; i < samples.Length; i++)
        {
            short value = (short)Math.Clamp(MathF.Round(samples[i] * scale * short.MaxValue), short.MinValue, short.MaxValue);
            pcm[i * 2] = (byte)(value & 0xFF);
            pcm[(i * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        return pcm;
    }
}
