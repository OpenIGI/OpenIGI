using OpenIGI.Audio;

namespace OpenIGI.Host.Audio;

/// <summary>The flashlight's switch: a short mechanical click, made here rather than taken from the game.</summary>
/// <remarks>
/// <para>
/// <b>Not retail.</b> The original has no flashlight and so no sound for one; borrowing a menu or weapon cue
/// would sound like the thing it was borrowed from. A tail-cap switch is two events a few milliseconds apart
/// -- the plunger bottoming out, then the contact snapping over -- each a burst of broadband noise and a
/// ringing of the small metal parts, gone in a few tens of milliseconds. That is what is synthesised.
/// </para>
/// <para>
/// Deterministic: a fixed-seed generator, so every run clicks the same and nothing here depends on the clock.
/// The same sound serves both ways; the host pitches the off click a little lower, as a real switch sounds
/// slightly different releasing than engaging.
/// </para>
/// </remarks>
public static class FlashlightClick
{
    /// <summary>The name the click is registered under with the sound system.</summary>
    public const string SoundName = "remaster.flashlight";

    /// <summary>The click's sample rate.</summary>
    public const int SampleRate = 44100;

    /// <summary>Gets the format the click is in: mono sixteen-bit.</summary>
    public static AudioFormat Format { get; } = new(SampleRate, 1, 16);

    /// <summary>Gets the click as little-endian sixteen-bit PCM.</summary>
    public static byte[] Pcm { get; } = Synthesise();

    private static byte[] Synthesise()
    {
        const float Seconds = 0.07f;
        int count = (int)(SampleRate * Seconds);
        float[] samples = new float[count];

        // The plunger bottoming out, then the contact snapping over eighteen milliseconds later, softer.
        Event(samples, start: 0f, gain: 1f, ring: 3100f, decay: 0.006f, noiseDecay: 0.0015f, seed: 0x5EED1u);
        Event(samples, start: 0.018f, gain: 0.55f, ring: 2050f, decay: 0.008f, noiseDecay: 0.002f, seed: 0xC11C4u);

        // Peak at half of full scale: a click is heard for its edge, not its level.
        float peak = 1e-6f;
        foreach (float sample in samples)
        {
            peak = MathF.Max(peak, MathF.Abs(sample));
        }

        byte[] pcm = new byte[count * 2];
        float scale = 0.5f / peak;
        for (int i = 0; i < count; i++)
        {
            short value = (short)Math.Clamp(MathF.Round(samples[i] * scale * short.MaxValue), short.MinValue, short.MaxValue);
            pcm[i * 2] = (byte)(value & 0xFF);
            pcm[(i * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        return pcm;
    }

    // One mechanical event: a noise burst that dies in a millisecond or two, high-passed by differencing so it
    // is a tick and not a thump, and a damped ring of the metal parts under it.
    private static void Event(float[] samples, float start, float gain, float ring, float decay, float noiseDecay, uint seed)
    {
        int first = (int)(start * SampleRate);
        uint state = seed;
        float previousNoise = 0f;

        for (int i = first; i < samples.Length; i++)
        {
            float t = (i - first) / (float)SampleRate;

            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            float noise = ((state & 0xFFFF) / 32767.5f) - 1f;
            float tick = (noise - previousNoise) * MathF.Exp(-t / noiseDecay);
            previousNoise = noise;

            float tone = MathF.Sin(MathF.Tau * ring * t) * MathF.Exp(-t / decay);
            float overtone = 0.35f * MathF.Sin(MathF.Tau * ring * 2.37f * t) * MathF.Exp(-t / (decay * 0.5f));

            samples[i] += gain * ((0.8f * tick) + (0.5f * tone) + overtone);
        }
    }
}
