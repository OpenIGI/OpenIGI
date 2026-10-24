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
