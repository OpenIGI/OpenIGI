using OpenIGI.Audio;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Host.Audio;

/// <summary>The remastered crosshair's sounds: what a hit and a kill play.</summary>
/// <remarks>
/// <para>
/// <b>Not retail</b>, as the crosshair's marks are not: synthesised here, from nothing of any game's. Each is a pool of variants a
/// sound is drawn from at random, so a burst never repeats itself. A hit plays a tick -- a broad click falling into a low thud
/// near seventy hertz, gone in a fifth of a second -- and a kill a heavier confirm: a high hiss gathering for a tenth of a second
/// into a broad impact, a deep boom under it and a faint high tail. Either plays the round's strike with it: a brighter, shorter
/// thwack into a lower thud, and a quiet wet splatter of grainy noise.
/// </para>
/// <para>
/// <see cref="OverrideVariable"/> names a folder of recordings standing in for these, laid out the same way, for judging them
/// against a reference by ear; nothing of such a folder ships.
/// </para>
/// </remarks>
public static class HitSounds
{
    /// <summary>The variable naming a folder of sounds to use in place of these, for judging the feel against a reference.</summary>
    public const string OverrideVariable = "OPENIGI_HIT_SOUNDS";

    private const int Ticks = 8;
    private const int Kills = 6;
    private const int Thwacks = 10;
    private const int Splatters = 8;

    private static readonly Lazy<HitSoundFolder> Cache = new(Make);

    /// <summary>Gets the sounds in play: the folder <see cref="OverrideVariable"/> names, or the synthesised ones.</summary>
    /// <remarks>
    /// Each subfolder of such a folder is a pool of sixteen-bit PCM WAVs one sound is drawn from: <c>hit</c> for a hit, <c>kill</c>
    /// for a kill, <c>kill_head</c> for a headshot kill (else <c>kill</c>), and every <c>impact*</c> pool played with any of them.
    /// </remarks>
    public static HitSoundFolder Current { get; } = HitSoundFolder.Read(Environment.GetEnvironmentVariable(OverrideVariable)) ?? Synthesised;

    /// <summary>Gets the synthesised sounds.</summary>
    public static HitSoundFolder Synthesised => Cache.Value;

    // Computed rather than stored: Current is initialised first and makes the sounds with it.
    private static AudioFormat Format => new(ResonantSynthesis.SampleRate, 1, 16);

    private static HitSoundFolder Make()
    {
        List<(string Name, AudioFormat Format, byte[] Pcm)> sounds = [];
        uint seed = 0x5EED1u;
        List<string> Pool(string name, int count, Func<uint, (float[] Samples, float Peak)> make)
        {
            List<string> names = [];
            for (int i = 0; i < count; i++)
            {
                (float[] samples, float peak) = make(XorShift32.Next(ref seed) | 1u);
                string id = $"remaster.{name}.{i}";
                sounds.Add((id, Format, ResonantSynthesis.ToPcm(samples, peak)));
                names.Add(id);
            }

            return names;
        }

        List<string> hit = Pool("hit", Ticks, Tick);
        List<string> kill = Pool("kill", Kills, Kill);
        List<string> thwack = Pool("impact.thwack", Thwacks, Thwack);
        List<string> splatter = Pool("impact.splatter", Splatters, Splatter);
        return new HitSoundFolder(sounds, hit, kill, kill, [thwack, splatter]);
    }

    // A value from a range, drawn from a variant's stream.
    private static float Draw(ref uint state, float from, float to) => from + ((to - from) * XorShift32.NextUnit(ref state));

    // A hit's tick: a broad click for the first few milliseconds, falling into a low thud that is down twenty decibels by about a
    // ninth of a second.
    private static (float[] Samples, float Peak) Tick(uint seed)
    {
        uint r = seed;
        float[] samples = new float[(int)(ResonantSynthesis.SampleRate * 0.18f)];
        float thud = Draw(ref r, 64f, 82f);
        ResonantSynthesis.Noise(samples, 0f, 3.2f, (110f, Draw(ref r, 4200f, 6500f)), (0.0004f, 0.004f, Draw(ref r, 0.005f, 0.008f)), XorShift32.Next(ref r));
        ResonantSynthesis.Noise(samples, 0f, 6f, (180f, 900f), (0.0005f, 0.006f, 0.008f), XorShift32.Next(ref r));
        ResonantSynthesis.Glide(samples, 0f, 1.5f, thud * 1.35f, thud, glide: 0.03f, rise: 0.003f, decay: Draw(ref r, 0.045f, 0.06f));
        Release(samples, 0.03f);
        return (samples, Draw(ref r, 0.45f, 0.67f));
    }

    // A kill's confirm: a metallic shimmer -- steady, unrelated high tones in noise -- swelling thirty-two decibels for about an eighth
    // of a second and cut dead by a low punch; under it a sub that drops from about a hundred hertz to forty-seven in fifty
    // milliseconds, holds there at full, then steps fifteen decibels down onto a thirty-five hertz rumble that fades by two-thirds
    // of a second; a soft second thump a little after; and the shimmer's tones ringing faintly on.
    private static (float[] Samples, float Peak) Kill(uint seed)
    {
        uint r = seed;
        float[] samples = new float[(int)(ResonantSynthesis.SampleRate * 0.9f)];
        float hit = Draw(ref r, 0.1f, 0.13f);
        float spread = Draw(ref r, 0.92f, 1.08f);
        float[] shimmer =
        [
            Draw(ref r, 620f, 700f), Draw(ref r, 2450f, 2750f) * spread, Draw(ref r, 4100f, 4500f) * spread, Draw(ref r, 4800f, 5200f) * spread,
            Draw(ref r, 5600f, 6300f) * spread, Draw(ref r, 6900f, 7100f) * spread, Draw(ref r, 11000f, 12000f),
        ];
        ResonantSynthesis.Tones(samples, 0f, 0.55f, shimmer, (hit, 0f, 0.005f), XorShift32.Next(ref r), swell: true);
        ResonantSynthesis.Noise(samples, 0f, 3f, (2000f, 12000f), (hit, 0f, 0.005f), XorShift32.Next(ref r), swell: true);

        // The punch: a short burst of body, mostly under six hundred hertz.
        ResonantSynthesis.Noise(samples, hit, 4f, (70f, 650f), (0.002f, 0.03f, 0.025f), XorShift32.Next(ref r));
        ResonantSynthesis.Noise(samples, hit, 3.5f, (650f, 10000f), (0.001f, 0.02f, 0.02f), XorShift32.Next(ref r));

        float low = Draw(ref r, 44f, 50f);
        float floor = Draw(ref r, 33f, 37f);
        float held = Draw(ref r, 0.09f, 0.11f);
        ResonantSynthesis.Sweep(samples, hit, 1f,
        [
            (0f, Draw(ref r, 100f, 110f), -6f), (0.012f, 95f, 0f), (0.03f, 80f, -2f), (0.05f, low, -6f), (held, low, -7f),
            (held + 0.02f, floor, -16f), (held + 0.1f, floor, -22f), (held + 0.2f, floor + 4f, -30f), (held + 0.3f, floor, -40f),
            (held + 0.45f, floor, -55f), (held + 0.55f, floor, -75f),
        ]);
        ResonantSynthesis.Sweep(samples, hit, 0.35f, [(0f, 210f, -4f), (0.02f, 170f, 0f), (0.05f, 140f, -6f), (0.08f, 120f, -24f), (0.12f, 110f, -60f)]);
        ResonantSynthesis.Knock(samples, hit + held + Draw(ref r, 0.0f, 0.03f), 0.8f, Draw(ref r, 55f, 70f), XorShift32.Next(ref r));

        // The shimmer ringing on, faintly.
        ResonantSynthesis.Tones(samples, hit + 0.15f, 0.012f, shimmer.AsSpan(1, 5), (0.04f, 0.06f, 0.08f), XorShift32.Next(ref r));
        Release(samples, 0.08f);
        return (samples, 0.97f);
    }

    // The round striking flesh: a bright click falling into a short, low thud, down forty decibels by about a tenth of a second.
    private static (float[] Samples, float Peak) Thwack(uint seed)
    {
        uint r = seed;
        float[] samples = new float[(int)(ResonantSynthesis.SampleRate * 0.17f)];
        float thud = Draw(ref r, 48f, 64f);
        ResonantSynthesis.Noise(samples, 0f, 2f, (2500f, 11000f), (0.0003f, 0.008f, Draw(ref r, 0.01f, 0.016f)), XorShift32.Next(ref r));
        ResonantSynthesis.Noise(samples, 0f, 5f, (300f, 1400f), (0.0005f, 0.012f, Draw(ref r, 0.012f, 0.02f)), XorShift32.Next(ref r));
        ResonantSynthesis.Knock(samples, 0f, 2f, Draw(ref r, 700f, 1100f), XorShift32.Next(ref r));
        ResonantSynthesis.Glide(samples, Draw(ref r, 0f, 0.006f), 2f, thud * 1.6f, thud, glide: 0.03f, rise: 0.016f, decay: Draw(ref r, 0.02f, 0.028f));
        Release(samples, 0.03f);
        return (samples, 0.95f);
    }

    // The round's wet splatter: quiet, grainy noise between one and seven kilohertz, sometimes gathering before it dies.
    private static (float[] Samples, float Peak) Splatter(uint seed)
    {
        uint r = seed;
        float length = Draw(ref r, 0.22f, 0.5f);
        float[] samples = new float[(int)(ResonantSynthesis.SampleRate * length)];
        ResonantSynthesis.Noise(samples, 0f, 0.25f, (1500f, 14000f), (0.001f, 0.01f, 0.02f), XorShift32.Next(ref r));
        float attack = Draw(ref r, 0.01f, length * 0.35f);
        (float Attack, float Hold, float Decay) wet = (attack, Draw(ref r, 0.03f, 0.08f), Draw(ref r, 0.06f, 0.11f));
        ResonantSynthesis.Noise(samples, 0f, 1.6f, (Draw(ref r, 600f, 900f), 3000f), wet, XorShift32.Next(ref r), grain: 0.8f);
        ResonantSynthesis.Noise(samples, 0f, 0.55f, (2500f, 12000f), wet, XorShift32.Next(ref r), grain: 0.8f);
        Release(samples, 0.04f);
        return (samples, Draw(ref r, 0.12f, 0.45f));
    }

    // A short fade to silence at the end, so no sound stops on a click.
    private static void Release(float[] samples, float seconds)
    {
        int fade = Math.Min(samples.Length, (int)(seconds * ResonantSynthesis.SampleRate));
        for (int i = 0; i < fade; i++)
        {
            samples[samples.Length - 1 - i] *= i / (float)fade;
        }
    }
}

/// <summary>A folder of hit and kill sounds standing in for the synthesised ones: pools, each a sound drawn from at random.</summary>
/// <param name="Sounds">Every sound, by the name it is registered under, with its format.</param>
/// <param name="Hit">The pool a hit draws from.</param>
/// <param name="Kill">The pool a kill draws from.</param>
/// <param name="HeadshotKill">The pool a headshot kill draws from.</param>
/// <param name="Impacts">The pools every hit and kill draws one each from as well.</param>
public sealed record HitSoundFolder(
    IReadOnlyList<(string Name, AudioFormat Format, byte[] Pcm)> Sounds,
    IReadOnlyList<string> Hit,
    IReadOnlyList<string> Kill,
    IReadOnlyList<string> HeadshotKill,
    IReadOnlyList<IReadOnlyList<string>> Impacts)
{
    /// <summary>Reads a folder, or none.</summary>
    /// <param name="folder">The folder, or null.</param>
    /// <returns>Its sounds, or null where it names no folder with any.</returns>
    public static HitSoundFolder? Read(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        List<(string Name, AudioFormat Format, byte[] Pcm)> sounds = [];
        List<string> Pool(string sub)
        {
            List<string> names = [];
            string path = Path.Combine(folder, sub);
            if (!Directory.Exists(path))
            {
                return names;
            }

            foreach (string file in Directory.GetFiles(path, "*.wav").Order(StringComparer.OrdinalIgnoreCase))
            {
                if (ReadWav(file) is { } wav)
                {
                    string name = $"remaster.ref.{sub}.{names.Count}";
                    sounds.Add((name, wav.Format, wav.Pcm));
                    names.Add(name);
                }
            }

            return names;
        }

        List<string> hit = Pool("hit");
        List<string> kill = Pool("kill");
        List<string> head = Pool("kill_head");
        List<IReadOnlyList<string>> impacts = [];
        foreach (string sub in Directory.GetDirectories(folder, "impact*").Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase))
        {
            if (Pool(sub) is { Count: > 0 } impact)
            {
                impacts.Add(impact);
            }
        }

        return sounds.Count == 0 ? null : new HitSoundFolder(sounds, hit, kill, head.Count > 0 ? head : kill, impacts);
    }

    // A sixteen-bit PCM WAV's format and samples; null for anything else.
    private static (AudioFormat Format, byte[] Pcm)? ReadWav(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < 44 || data[0] != 'R' || data[8] != 'W')
        {
            return null;
        }

        int at = 12;
        AudioFormat? format = null;
        while (at + 8 <= data.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(data, at, 4);
            int size = BitConverter.ToInt32(data, at + 4);
            if (id == "fmt ")
            {
                int channels = BitConverter.ToInt16(data, at + 10);
                int rate = BitConverter.ToInt32(data, at + 12);
                int bits = BitConverter.ToInt16(data, at + 22);
                if (BitConverter.ToInt16(data, at + 8) != 1 || bits != 16)
                {
                    return null;
                }

                format = new AudioFormat(rate, channels, bits);
            }
            else if (id == "data" && format is { } known)
            {
                return (known, data[(at + 8)..Math.Min(data.Length, at + 8 + size)]);
            }

            at += 8 + size + (size & 1);
        }

        return null;
    }
}
