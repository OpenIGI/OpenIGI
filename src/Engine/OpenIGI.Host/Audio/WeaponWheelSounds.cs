using OpenIGI.Audio;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Host.Audio;

/// <summary>The weapon wheel's sounds: a breath of air as it opens and time slows, a tick for each weapon passed, a click as one is taken.</summary>
/// <remarks>
/// <para>
/// <b>Not retail</b>, as the wheel is not: made here rather than borrowed from either game's banks, where every cue already means
/// something else. Each is a short, quiet piece of filtered noise or a struck resonance -- texture, not a note -- so it sits
/// under the game's own sounds rather than over them.
/// </para>
/// <para>
/// Opening is noise swept down from bright to dull under a low thump, which reads as the world dropping away into slow time;
/// closing is the sweep the other way, shorter and fainter, the world coming back. Deterministic: fixed-seed generators, so
/// every run sounds the same.
/// </para>
/// </remarks>
public static class WeaponWheelSounds
{
    /// <summary>The name the opening sound is registered under.</summary>
    public const string OpenSoundName = "remaster.wheel.open";

    /// <summary>The name the closing sound is registered under.</summary>
    public const string CloseSoundName = "remaster.wheel.close";

    /// <summary>The name the tick of passing a weapon is registered under.</summary>
    public const string TickSoundName = "remaster.wheel.tick";

    /// <summary>The name the click of taking a weapon is registered under.</summary>
    public const string ChooseSoundName = "remaster.wheel.choose";

    /// <summary>The sounds' sample rate.</summary>
    public const int SampleRate = ResonantSynthesis.SampleRate;

    private static readonly Lazy<(string Name, byte[] Pcm)[]> Cache = new(() =>
    [
        (OpenSoundName, ResonantSynthesis.ToPcm(Sweep(0.34f, fromHertz: 5200f, toHertz: 260f, thump: 0.7f, seed: 0x51E7u), 0.45f)),
        (CloseSoundName, ResonantSynthesis.ToPcm(Sweep(0.2f, fromHertz: 380f, toHertz: 3600f, thump: 0f, seed: 0xC105Eu), 0.3f)),
        (TickSoundName, ResonantSynthesis.ToPcm(Tick(), 0.3f)),
        (ChooseSoundName, ResonantSynthesis.ToPcm(Choose(), 0.45f)),
    ]);

    /// <summary>Gets the format the sounds are in: mono sixteen-bit.</summary>
    public static AudioFormat Format { get; } = new(SampleRate, 1, 16);

    /// <summary>Gets every sound, by the name it is registered under.</summary>
    public static IReadOnlyList<(string Name, byte[] Pcm)> All => Cache.Value;

    // Noise through a low-pass whose corner glides from one frequency to another, swelling in and dying away, with an optional
    // low thump at its start.
    private static float[] Sweep(float seconds, float fromHertz, float toHertz, float thump, uint seed)
    {
        int count = (int)(SampleRate * seconds);
        float[] samples = new float[count];
        uint state = seed;
        float low1 = 0f, low2 = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)count;
            float corner = fromHertz * MathF.Pow(toHertz / fromHertz, t);
            float keep = MathF.Exp(-MathF.Tau * corner / SampleRate);
            float noise = XorShift32.NextSigned(ref state);
            low1 = (low1 * keep) + (noise * (1f - keep));
            low2 = (low2 * keep) + (low1 * (1f - keep));
            float envelope = MathF.Sin(MathF.PI * MathF.Pow(t, 0.6f)) * (1f - (0.4f * t));
            samples[i] = low2 * envelope;
        }

        if (thump > 0f)
        {
            ResonantSynthesis.Knock(samples, start: 0f, gain: thump * 0.05f, hertz: 70f, seed: seed ^ 0x9E37u);
        }

        return samples;
    }

    // A short, dry tick: a fraction of a millisecond of noise struck through a pair of bright resonances.
    private static float[] Tick()
    {
        float[] samples = new float[(int)(SampleRate * 0.035f)];
        ResonantSynthesis.Knock(samples, start: 0f, gain: 1f, hertz: 2900f, seed: 0x71C4u);
        return samples;
    }

    // Taking a weapon: a soft knock and, a breath later, a brighter tick over it.
    private static float[] Choose()
    {
        float[] samples = new float[(int)(SampleRate * 0.09f)];
        ResonantSynthesis.Knock(samples, start: 0f, gain: 0.8f, hertz: 420f, seed: 0xC40Eu);
        ResonantSynthesis.Knock(samples, start: 0.018f, gain: 0.7f, hertz: 2300f, seed: 0x5E1Au);
        return samples;
    }
}
