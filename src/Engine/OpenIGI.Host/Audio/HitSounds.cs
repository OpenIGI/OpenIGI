using OpenIGI.Audio;

namespace OpenIGI.Host.Audio;

/// <summary>The remastered crosshair's sounds: a short bright tick for a hit and a lower, fuller one for a kill.</summary>
/// <remarks>
/// <b>Not retail</b>, as the crosshair's marks are not: made here, as the weapon wheel's are, a struck resonance a few hundredths of
/// a second long that sits under the gunfire rather than over it.
/// </remarks>
public static class HitSounds
{
    /// <summary>The name a hit's tick is registered under.</summary>
    public const string HitSoundName = "remaster.hit";

    /// <summary>The name a kill's tick is registered under.</summary>
    public const string KillSoundName = "remaster.kill";

    private static readonly Lazy<(string Name, byte[] Pcm)[]> Cache = new(() =>
    [
        (HitSoundName, ResonantSynthesis.ToPcm(Hit(), 0.32f)),
        (KillSoundName, ResonantSynthesis.ToPcm(Kill(), 0.42f)),
    ]);

    /// <summary>Gets the format the sounds are in: mono sixteen-bit.</summary>
    public static AudioFormat Format { get; } = new(ResonantSynthesis.SampleRate, 1, 16);

    /// <summary>Gets every sound, by the name it is registered under.</summary>
    public static IReadOnlyList<(string Name, byte[] Pcm)> All => Cache.Value;

    // A hit: one dry, bright tick.
    private static float[] Hit()
    {
        float[] samples = new float[(int)(ResonantSynthesis.SampleRate * 0.045f)];
        ResonantSynthesis.Knock(samples, start: 0f, gain: 1f, hertz: 3400f, seed: 0x4B17u);
        return samples;
    }

    // A kill: a low knock under a bright tick, and a second tick a breath after, so it reads as a confirmation.
    private static float[] Kill()
    {
        float[] samples = new float[(int)(ResonantSynthesis.SampleRate * 0.14f)];
        ResonantSynthesis.Knock(samples, start: 0f, gain: 0.9f, hertz: 520f, seed: 0x9D02u);
        ResonantSynthesis.Knock(samples, start: 0f, gain: 0.7f, hertz: 2600f, seed: 0x31F5u);
        ResonantSynthesis.Knock(samples, start: 0.055f, gain: 0.55f, hertz: 3100f, seed: 0x6CA9u);
        return samples;
    }
}
