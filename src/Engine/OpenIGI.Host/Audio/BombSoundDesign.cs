using OpenIGI.Audio;

namespace OpenIGI.Host.Audio;

/// <summary>
/// The bomb's own two sounds layered out of the kit's explosions: the deep thump heard far off, and the rush of the front
/// coming at a player and striking it.
/// </summary>
/// <remarks>
/// <para>
/// Made once, as the kit is registered, from the game's own recordings: slowed (which lowers them), darkened through a
/// low-pass, reversed and laid one over another. Sixteen-bit only, as every sound in the kit is; anything else is left out
/// and the bomb is heard without them.
/// </para>
/// <para>
/// The rush is built to its cue (<see cref="Game.Multiplayer.Modes.BombWave.CueLeadSeconds"/> before the front arrives): the
/// rumble of the far explosion run backwards swells for that long and the near blast, slowed and dulled, strikes on the
/// moment the front does, with its own tail behind it.
/// </para>
/// </remarks>
public static class BombSoundDesign
{
    /// <summary>Builds the far thump from the far explosion: slowed, darkened and pushed into a soft limit.</summary>
    /// <param name="format">The far explosion's format.</param>
    /// <param name="distant">Its samples.</param>
    /// <returns>The thump, in the same format; empty where the format is not one this reads.</returns>
    public static byte[] FarThump(AudioFormat format, ReadOnlySpan<byte> distant)
    {
        if (format.BitsPerSample != 16 || distant.IsEmpty)
        {
            return [];
        }

        float[] slowed = Resample(ToMono(format, distant), 0.72f);
        LowPass(slowed, format.SampleRate, 520f);
        LowPass(slowed, format.SampleRate, 520f);
        Gain(slowed, 1.8f);
        return FromMono(format, SoftLimit(slowed));
    }

    /// <summary>Builds the front's rush and strike from the far explosion's rumble and the near explosion's blast.</summary>
    /// <param name="format">The two explosions' format: they must share it.</param>
    /// <param name="distant">The far explosion's samples.</param>
    /// <param name="near">The near explosion's samples.</param>
    /// <param name="leadSeconds">How long before the strike the rush starts: the cue's lead.</param>
    /// <returns>The rush, in the same format; empty where the format is not one this reads.</returns>
    public static byte[] FrontRush(AudioFormat format, ReadOnlySpan<byte> distant, ReadOnlySpan<byte> near, float leadSeconds)
    {
        if (format.BitsPerSample != 16 || distant.IsEmpty || near.IsEmpty)
        {
            return [];
        }

        int rate = format.SampleRate;
        int lead = Math.Max(1, (int)(leadSeconds * rate));

        // The swell: the far rumble backwards -- its fading tail become a rising rush -- darkened and eased in over the lead.
        float[] rumble = ToMono(format, distant);
        Array.Reverse(rumble);
        LowPass(rumble, rate, 1400f);
        int swellLength = Math.Min(rumble.Length, lead);
        float[] swell = rumble.AsSpan(rumble.Length - swellLength).ToArray();

        // The strike: the near blast slowed and dulled, and cut to its first second and a half.
        float[] strike = Resample(ToMono(format, near), 0.82f);
        LowPass(strike, rate, 2600f);
        int strikeLength = Math.Min(strike.Length, (int)(1.5f * rate));

        int swellStart = lead - swellLength;
        var mix = new float[lead + strikeLength];
        for (int i = 0; i < swellLength; i++)
        {
            float rise = (float)i / swellLength;
            mix[swellStart + i] += swell[i] * rise * rise * 0.9f;
        }

        for (int i = 0; i < strikeLength; i++)
        {
            float tail = 1f - Smooth(0.6f * rate, strikeLength, i);
            mix[lead + i] += strike[i] * 0.85f * tail;
        }

        return FromMono(format, SoftLimit(mix));
    }

    // The samples as one channel, -1..1.
    private static float[] ToMono(AudioFormat format, ReadOnlySpan<byte> pcm)
    {
        int channels = Math.Max(format.Channels, 1);
        int frames = pcm.Length / (2 * channels);
        var mono = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++)
            {
                int at = ((f * channels) + c) * 2;
                sum += (short)(pcm[at] | (pcm[at + 1] << 8)) / 32768f;
            }

            mono[f] = sum / channels;
        }

        return mono;
    }

    // One channel back into the format's own, every channel the same.
    private static byte[] FromMono(AudioFormat format, float[] mono)
    {
        int channels = Math.Max(format.Channels, 1);
        var pcm = new byte[mono.Length * channels * 2];
        for (int f = 0; f < mono.Length; f++)
        {
            short value = (short)Math.Clamp((int)MathF.Round(mono[f] * 32767f), short.MinValue, short.MaxValue);
            for (int c = 0; c < channels; c++)
            {
                int at = ((f * channels) + c) * 2;
                pcm[at] = (byte)(value & 0xFF);
                pcm[at + 1] = (byte)((value >> 8) & 0xFF);
            }
        }

        return pcm;
    }

    // Played at a share of its speed: slower, longer and lower.
    private static float[] Resample(float[] source, float speed)
    {
        int length = Math.Max(1, (int)(source.Length / speed));
        var result = new float[length];
        for (int i = 0; i < length; i++)
        {
            float at = i * speed;
            int a = Math.Min((int)at, source.Length - 1);
            int b = Math.Min(a + 1, source.Length - 1);
            float t = at - a;
            result[i] = source[a] + ((source[b] - source[a]) * t);
        }

        return result;
    }

    // A one-pole low-pass, in place.
    private static void LowPass(float[] samples, int sampleRate, float cutoffHz)
    {
        float k = 1f - MathF.Exp(-MathF.Tau * cutoffHz / Math.Max(sampleRate, 1));
        float state = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            state += (samples[i] - state) * k;
            samples[i] = state;
        }
    }

    private static void Gain(float[] samples, float gain)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] *= gain;
        }
    }

    // Peaks rounded off rather than clipped, then the whole brought to just under full scale.
    private static float[] SoftLimit(float[] samples)
    {
        float peak = 1e-6f;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = MathF.Tanh(samples[i] * 1.2f);
            peak = MathF.Max(peak, MathF.Abs(samples[i]));
        }

        float scale = 0.92f / peak;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] *= scale;
        }

        return samples;
    }

    private static float Smooth(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / MathF.Max(to - from, 1e-6f), 0f, 1f);
        return t * t * (3f - (2f * t));
    }
}
