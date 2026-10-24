using OpenIGI.Audio;

namespace OpenIGI.Host.Audio;

/// <summary>The weapon wheel's own sounds: a tick for each weapon passed and a click as one is taken.</summary>
/// <remarks>
/// <para>
/// <b>Not retail</b>, as the wheel is not: made here rather than borrowed from either game's banks, where every cue already means
/// something else. Each is a short, quiet piece of filtered noise or a struck resonance -- texture, not a note -- so it sits
/// under the game's own sounds rather than over them.
/// </para>
/// </remarks>
public static class WeaponWheelSounds
{
    /// <summary>The name the tick of passing a weapon is registered under.</summary>
    public const string TickSoundName = "remaster.wheel.tick";

    /// <summary>The name the click of taking a weapon is registered under.</summary>
    public const string ChooseSoundName = "remaster.wheel.choose";

    /// <summary>The sounds' sample rate.</summary>
    public const int SampleRate = ResonantSynthesis.SampleRate;

    private static readonly Lazy<(string Name, byte[] Pcm)[]> Cache = new(() =>
    [
        (TickSoundName, ResonantSynthesis.ToPcm(Tick(), 0.3f)),
        (ChooseSoundName, ResonantSynthesis.ToPcm(Choose(), 0.45f)),
    ]);

    /// <summary>Gets the format the sounds are in: mono sixteen-bit.</summary>
    public static AudioFormat Format { get; } = new(SampleRate, 1, 16);

    /// <summary>Gets every sound, by the name it is registered under.</summary>
    public static IReadOnlyList<(string Name, byte[] Pcm)> All => Cache.Value;

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
