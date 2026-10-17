using OpenIGI.Audio;
using OpenIGI.Game.World;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Host.Audio;

/// <summary>What the level's lamps sound like coming on and going off, made here rather than taken from the game.</summary>
/// <remarks>
/// <para>
/// <b>Not retail.</b> The original's lamps never change, so it has no sound for one changing. Each sound is
/// one of the things a real fitting does audibly, and is played at the instant <see cref="LampSwitchboard"/>
/// has the light do it, so what is heard is the light's own sequence: a cold tube's starter ticking for each
/// blink it makes, then the ballast taking the load; a sodium lantern's ignitor crackling, sometimes twice,
/// then the arc catching with a soft thump and a hum.
/// </para>
/// <para>
/// Built from two ingredients. Mechanical and electrical events -- a contact opening, a spark -- are bursts
/// of noise a fraction of a millisecond long, struck through a few broad resonances, as the flashlight's
/// click is (see <see cref="FlashlightClick"/>): a click with a pitch sounds like a toy. Ballasts are
/// magnetic, and hum at twice the mains frequency with a ladder of harmonics the steel's magnetostriction
/// adds, which is made by soft-clipping a hundred-hertz tone rather than by adding clean partials -- the
/// clipping is where the buzz comes from.
/// </para>
/// <para>
/// None of them loops. A ballast hums all night, but forty of them humming at once across a yard is a
/// drone over the whole mission; what is heard here is the hum swelling as the load comes on and settling
/// back under the ambience. Deterministic: fixed-seed generators, so every run sounds the same.
/// </para>
/// </remarks>
public static class LampSounds
{
    /// <summary>A wall switch flipped.</summary>
    public const string SwitchName = "remaster.lamp.switch";

    /// <summary>A glow starter opening and the tube trying to strike; the first of two variants.</summary>
    public const string StarterName = "remaster.lamp.starter";

    /// <summary>The second starter, so a run of blinks is not one sample repeated.</summary>
    public const string StarterAltName = "remaster.lamp.starter.alt";

    /// <summary>A tube's ballast taking the load.</summary>
    public const string TubeHumName = "remaster.lamp.hum.tube";

    /// <summary>A discharge lantern's ignitor firing one burst.</summary>
    public const string IgnitorName = "remaster.lamp.ignitor";

    /// <summary>A discharge lantern's arc catching, and its heavier ballast humming up.</summary>
    public const string DischargeHumName = "remaster.lamp.hum.discharge";

    /// <summary>A cold filament taking the current.</summary>
    public const string FilamentName = "remaster.lamp.filament";

    /// <summary>A ballast letting go as the supply is cut.</summary>
    public const string OffName = "remaster.lamp.off";

    /// <summary>A failing tube's burst of restrikes.</summary>
    public const string SputterName = "remaster.lamp.sputter";

    /// <summary>The sounds' sample rate.</summary>
    public const int SampleRate = 44100;

    /// <summary>A magnetic ballast's hum: twice the mains frequency, fifty hertz mains being the world's commonest.</summary>
    private const float HumHertz = 100f;

    /// <summary>Gets the format every lamp sound is in: mono sixteen-bit.</summary>
    public static AudioFormat Format { get; } = new(SampleRate, 1, 16);

    /// <summary>Gets every lamp sound with the name it is registered under.</summary>
    /// <returns>Name and little-endian sixteen-bit PCM, for each sound.</returns>
    /// <remarks>Synthesised on first call; a few tens of milliseconds, once per process.</remarks>
    public static IReadOnlyList<(string Name, byte[] Pcm)> All => Cache.Value;

    private static readonly Lazy<(string Name, byte[] Pcm)[]> Cache = new(() =>
    [
        (SwitchName, ToPcm(WallSwitch(), 0.5f)),
        (StarterName, ToPcm(Starter(0x51A27u, 1f), 0.5f)),
        (StarterAltName, ToPcm(Starter(0x3B0D9u, 0.92f), 0.5f)),
        (TubeHumName, ToPcm(TubeHum(), 0.5f)),
        (IgnitorName, ToPcm(Ignitor(), 0.5f)),
        (DischargeHumName, ToPcm(DischargeHum(), 0.5f)),
        (FilamentName, ToPcm(Filament(), 0.5f)),
        (OffName, ToPcm(BallastOff(), 0.5f)),
        (SputterName, ToPcm(Sputter(), 0.5f)),
    ]);

    // A rocker switch on the wall: the finger landing, the rocker snapping over, and the contacts seating a
    // few milliseconds later.
    private static float[] WallSwitch()
    {
        float[] s = Buffer(0.11f);
        Knock(s, 0f, 0.4f, 150f, 0x5A17u);
        Strike(s, 0.003f, 1f, [1250f, 2050f, 3350f, 4900f, 7200f], [10f, 12f, 12f, 10f, 8f], 0x9C04u);
        Strike(s, 0.0095f, 0.35f, [1180f, 1930f, 3150f], [9f, 10f, 10f], 0x2E61u);
        return Finish(s, 9500f);
    }

    // The starter's bimetal snapping open -- a tiny bright tick, since the contacts are a few millimetres of
    // strip in a glass bead -- and at the same instant the choke's kick across the tube: a short, sharp
    // electrical spit. Then the arc's first flash buzzes for a moment through the fitting.
    private static float[] Starter(uint seed, float detune)
    {
        float[] s = Buffer(0.09f);
        Strike(s, 0f, 0.7f, [2900f * detune, 4600f * detune, 6800f * detune, 9100f * detune], [18f, 18f, 16f, 12f], seed);

        // The kick: band-limited noise, very fast on and off.
        uint state = seed ^ 0xA5A5u;
        float[] spit = new float[(int)(0.012f * SampleRate)];
        for (int i = 0; i < spit.Length; i++)
        {
            float t = i / (float)SampleRate;
            spit[i] = XorShift32.NextSigned(ref state) * (1f - MathF.Exp(-t / 0.0002f)) * MathF.Exp(-t / 0.0025f);
        }

        Resonate(s, (int)(0.001f * SampleRate), spit, 3500f * detune, 1.2f, 0.9f);
        Resonate(s, (int)(0.001f * SampleRate), spit, 7000f * detune, 1.5f, 0.6f);

        // The flash's own buzz, through the fitting's tin, for as long as the flash lasts.
        Buzz(s, 0.002f, 0.05f, 0.18f, drive: 3f, attack: 0.002f, release: 0.012f);
        return Finish(s, 11000f);
    }

    // A magnetic ballast taking a tube's load: a firm onset as the arc catches, the hum settling to a quiet
    // steady level and then easing away under everything else. A faint glassy tink of the tube's ends heating.
    private static float[] TubeHum()
    {
        float[] s = Buffer(1.8f);
        Knock(s, 0f, 0.25f, 110f, 0x7E11u);
        Strike(s, 0f, 0.12f, [3100f, 5300f], [25f, 22f], 0x1F2Du);

        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)SampleRate;

            // On fast, a brief swell as the cold choke saturates, settling, then a long fade.
            float envelope = (1f - MathF.Exp(-t / 0.012f))
                * (0.42f + (0.58f * MathF.Exp(-t / 0.06f)))
                * Fade(t, 0.75f, 1.8f);
            s[i] += 0.55f * envelope * Hum(t, drive: 2.2f, brightness: 1f);
        }

        return Finish(s, 7000f);
    }

    // A discharge lamp's ignitor: a pulse on every half-cycle of the mains, each a few kilovolts jumping the
    // gap -- a sharp spit -- with a few missing and none quite alike, over the ignitor's own quiet buzz.
    private static float[] Ignitor()
    {
        float burst = LampSwitchboard.IgnitorSeconds;
        float[] s = Buffer(burst + 0.04f);
        uint state = 0x1C17u;

        for (float at = 0.002f; at < burst; at += 1f / HumHertz)
        {
            // A missed pulse now and then, and a little timing slop: it is a spark gap, not a clock.
            if (Unit(ref state) < 0.12f)
            {
                continue;
            }

            float jitter = (Unit(ref state) - 0.5f) * 0.0016f;
            float gain = 0.45f + (0.55f * Unit(ref state));
            Spark(s, at + jitter, gain, ref state);
        }

        Buzz(s, 0f, burst, 0.12f, drive: 2.5f, attack: 0.01f, release: 0.02f);
        return Finish(s, 12000f);
    }

    // The arc in a discharge lantern catching: a soft low whump as the gas breaks down for good, the heavier
    // ballast humming up -- more fundamental, less buzz, than a tube's -- and the arc's faint hiss dying away.
    private static float[] DischargeHum()
    {
        float[] s = Buffer(2.6f);
        Knock(s, 0f, 0.8f, 85f, 0x2D44u);

        uint state = 0x6B9Au;
        float low = 0f;
        float keep = MathF.Exp(-MathF.Tau * 400f / SampleRate);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)SampleRate;

            // The whump: low-passed noise, a few tens of milliseconds.
            low = (low * keep) + (XorShift32.NextSigned(ref state) * (1f - keep));
            s[i] += 2.2f * low * (1f - MathF.Exp(-t / 0.005f)) * MathF.Exp(-t / 0.06f);

            float envelope = (1f - MathF.Exp(-t / 0.04f))
                * (0.5f + (0.5f * MathF.Exp(-t / 0.35f)))
                * Fade(t, 1.2f, 2.6f);
            s[i] += 0.5f * envelope * Hum(t, drive: 1.5f, brightness: 0.55f);
        }

        // The arc's hiss: bright, quiet, fading as the tube's pressure rises.
        float[] hiss = new float[s.Length];
        for (int i = 0; i < hiss.Length; i++)
        {
            float t = i / (float)SampleRate;
            hiss[i] = XorShift32.NextSigned(ref state) * MathF.Exp(-t / 0.8f) * Fade(t, 1.6f, 2.6f);
        }

        Resonate(s, 0, hiss, 5200f, 0.8f, 0.05f);
        return Finish(s, 9000f);
    }

    // A filament taking the current cold: the glass and the filament's supports ringing very faintly from the
    // thermal shock, with the briefest sing of the filament itself.
    private static float[] Filament()
    {
        float[] s = Buffer(0.14f);
        Strike(s, 0f, 0.6f, [4300f, 7100f, 10200f], [35f, 30f, 25f], 0x4F11u);
        Buzz(s, 0f, 0.03f, 0.05f, drive: 1.2f, attack: 0.001f, release: 0.02f);
        return Finish(s, 14000f);
    }

    // A ballast letting go: the core relaxing with a dull tick and the hum dying in a few cycles.
    private static float[] BallastOff()
    {
        float[] s = Buffer(0.16f);
        Knock(s, 0f, 0.5f, 120f, 0x0FF5u);
        Strike(s, 0.001f, 0.3f, [1800f, 3200f], [9f, 9f], 0x77A1u);

        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)SampleRate;
            s[i] += 0.4f * MathF.Exp(-t / 0.04f) * Hum(t, drive: 2f, brightness: 0.8f);
        }

        return Finish(s, 8000f);
    }

    // A failing tube's restrikes: the ballast's buzz cutting in and out as the arc forms and fails, with a
    // crackle at every attempt -- irregular, because a fault is not a clock.
    private static float[] Sputter()
    {
        float[] s = Buffer(0.38f);
        uint state = 0x5B77u;
        float at = 0.002f;

        while (at < 0.33f)
        {
            float on = 0.006f + (0.024f * Unit(ref state));
            Spark(s, at, 0.5f + (0.4f * Unit(ref state)), ref state);
            Buzz(s, at, on, 0.5f, drive: 3f, attack: 0.0015f, release: 0.004f);
            at += on + 0.006f + (0.03f * Unit(ref state));
        }

        return Finish(s, 10000f);
    }

    // The ballast's hum at one instant: a hundred-hertz tone soft-clipped into a buzz. Drive is how hard it
    // is clipped -- how much buzz there is over hum -- and brightness how much of the upper ladder is kept.
    private static float Hum(float t, float drive, float brightness)
    {
        float phase = MathF.Tau * HumHertz * t;
        float tone = MathF.Sin(phase)
            + (0.35f * MathF.Sin((2f * phase) + 0.7f))
            + (0.15f * brightness * MathF.Sin((3f * phase) + 1.9f));
        return MathF.Tanh(drive * tone) / MathF.Tanh(drive * 1.3f);
    }

    // A stretch of ballast buzz, on and off with short ramps so its edges do not click on their own.
    private static void Buzz(float[] samples, float start, float length, float gain, float drive, float attack, float release)
    {
        int first = (int)(start * SampleRate);
        int last = Math.Min(samples.Length, (int)((start + length + (release * 5f)) * SampleRate));

        for (int i = first; i < last; i++)
        {
            float u = (i - first) / (float)SampleRate;
            float envelope = (1f - MathF.Exp(-u / attack))
                * (u < length ? 1f : MathF.Exp(-(u - length) / release));
            samples[i] += gain * envelope * Hum(i / (float)SampleRate, drive, 1f);
        }
    }

    // One spark across a gap: a burst of noise well under a millisecond long, heard for its edge and coloured
    // by the fitting's tin.
    private static void Spark(float[] samples, float start, float gain, ref uint state)
    {
        int first = Math.Max(0, (int)(start * SampleRate));
        int length = Math.Min(samples.Length - first, (int)(0.004f * SampleRate));
        if (length <= 0)
        {
            return;
        }

        float[] burst = new float[length];
        float previous = 0f;
        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleRate;
            float noise = XorShift32.NextSigned(ref state);
            burst[i] = (noise - previous) * MathF.Exp(-t / 0.0004f);
            previous = noise;
        }

        for (int i = 0; i < length; i++)
        {
            samples[first + i] += gain * 0.5f * burst[i];
        }

        Resonate(samples, first, burst, 2600f, 6f, gain * 0.5f);
        Resonate(samples, first, burst, 4400f, 6f, gain * 0.45f);
        Resonate(samples, first, burst, 6900f, 5f, gain * 0.3f);
    }

    // One mechanical strike: a sub-millisecond burst of noise heard directly for its edge and struck through
    // a few broad resonances for its colour.
    private static void Strike(float[] samples, float start, float gain, float[] hertz, float[] q, uint seed)
    {
        int first = (int)(start * SampleRate);
        int length = samples.Length - first;
        if (length <= 0)
        {
            return;
        }

        float[] excitation = new float[length];
        uint state = seed;
        float previous = 0f;
        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleRate;
            float noise = XorShift32.NextSigned(ref state);
            excitation[i] = (noise - previous) * (1f - MathF.Exp(-t / 0.00008f)) * MathF.Exp(-t / 0.0005f);
            previous = noise;
        }

        for (int i = 0; i < length; i++)
        {
            samples[first + i] += gain * 0.15f * excitation[i];
        }

        for (int m = 0; m < hertz.Length; m++)
        {
            float tilt = MathF.Pow(0.8f, m);
            Resonate(samples, first, excitation, hertz[m] * (1f + (0.02f * XorShift32.NextSigned(ref state))), q[m], gain * 0.7f * tilt);
        }
    }

    // A soft, low, heavily damped knock: a hand on a plate, a core settling, a gas breaking down.
    private static void Knock(float[] samples, float start, float gain, float hertz, uint seed)
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

    // A two-pole band-pass at unit peak gain (the audio EQ cookbook's), run over the excitation and added in.
    private static void Resonate(float[] samples, int first, float[] excitation, float hertz, float q, float gain)
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

    private static float[] Buffer(float seconds) => new float[(int)(seconds * SampleRate)];

    // One until a point, then a raised-cosine fall to nothing at the end.
    private static float Fade(float t, float from, float to) =>
        t <= from ? 1f
        : t >= to ? 0f
        : 0.5f + (0.5f * MathF.Cos(MathF.PI * (t - from) / (to - from)));

    // The brightest fizz off, as the air between takes it, and the last few milliseconds faded so nothing is
    // cut off with a tick of its own.
    private static float[] Finish(float[] samples, float cutoff)
    {
        float smoothed = 0f;
        float keep = MathF.Exp(-MathF.Tau * cutoff / SampleRate);
        for (int i = 0; i < samples.Length; i++)
        {
            smoothed = (smoothed * keep) + (samples[i] * (1f - keep));
            samples[i] = smoothed;
        }

        int fade = Math.Min(samples.Length, SampleRate / 200);
        for (int i = 0; i < fade; i++)
        {
            samples[samples.Length - 1 - i] *= i / (float)fade;
        }

        return samples;
    }

    // Peak at a fraction of full scale, and out as little-endian sixteen-bit. Every sound is normalised the
    // same way, and how loud each is against the others is the player's gain, not the sample's.
    private static byte[] ToPcm(float[] samples, float peakLevel)
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

    private static float Unit(ref uint state) => (XorShift32.NextSigned(ref state) + 1f) * 0.5f;
}
