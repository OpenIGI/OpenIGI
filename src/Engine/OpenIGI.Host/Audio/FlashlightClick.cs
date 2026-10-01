using OpenIGI.Audio;
using OpenIGI.Game.Player;

namespace OpenIGI.Host.Audio;

/// <summary>The flashlight's switch: a short mechanical click, made here rather than taken from the game.</summary>
/// <remarks>
/// <para>
/// <b>Not retail.</b> The original has no flashlight and so no sound for one; borrowing a menu or weapon cue
/// would sound like the thing it was borrowed from. A tail-cap switch is a few events a few milliseconds apart
/// -- the thumb landing on the boot, the plunger bottoming out, the latch snapping over -- and what is
/// synthesised is those events and nothing else.
/// </para>
/// <para>
/// Each event is a sub-millisecond burst of noise struck through a small bank of resonances, not a tone. The
/// first version rang each event as a clean sine with one overtone, and a click with a pitch sounds like a
/// toy: a real switch's parts are small, stiff and damped by the hand, so their modes are inharmonic, broad
/// and gone in a few milliseconds, and what the ear hears is the edge and the colour, never a note. Under the
/// snap sits a soft, low knock of the thumb on the rubber boot and the body of the torch.
/// </para>
/// <para>
/// On and off are separate sounds, as a clicky switch really is two different mechanisms: switching on is
/// press, bottom out and latch; switching off is a softer press and the latch releasing on the way back up.
/// Deterministic: fixed-seed generators, so every run clicks the same and nothing depends on the clock.
/// </para>
/// </remarks>
public static class FlashlightClick
{
    /// <summary>The name the switching-on click is registered under with the sound system.</summary>
    public const string OnSoundName = "remaster.flashlight.on";

    /// <summary>The name the switching-off click is registered under with the sound system.</summary>
    public const string OffSoundName = "remaster.flashlight.off";

    /// <summary>The clicks' sample rate.</summary>
    public const int SampleRate = 44100;

    /// <summary>The switch's resonances, in hertz: the latch spring, the plunger and the tail cap's metal.</summary>
    private static readonly float[] ModeHertz = [1420f, 2330f, 3790f, 5470f, 8150f];

    /// <summary>
    /// How sharp each resonance is: enough to ring for two to four milliseconds, which is colour, and never the
    /// tens of milliseconds that would make it a note.
    /// </summary>
    private static readonly float[] ModeQ = [14f, 16f, 16f, 12f, 9f];

    /// <summary>How much of each resonance a strike excites.</summary>
    private static readonly float[] ModeGain = [0.55f, 0.8f, 0.7f, 0.45f, 0.3f];

    /// <summary>Gets the format the clicks are in: mono sixteen-bit.</summary>
    public static AudioFormat Format { get; } = new(SampleRate, 1, 16);

    /// <summary>Gets the switching-on click as little-endian sixteen-bit PCM.</summary>
    public static byte[] OnPcm { get; } = Synthesise(on: true);

    /// <summary>Gets the switching-off click as little-endian sixteen-bit PCM.</summary>
    public static byte[] OffPcm { get; } = Synthesise(on: false);

    private static byte[] Synthesise(bool on)
    {
        const float Seconds = 0.09f;
        int count = (int)(SampleRate * Seconds);
        float[] samples = new float[count];

        if (on)
        {
            // The thumb on the boot, the plunger bottoming out, then the latch snapping over: the loud one, at
            // the moment the rig brings the light on.
            Knock(samples, start: 0f, gain: 0.35f, hertz: 190f, seed: 0x7A11Bu);
            Strike(samples, start: 0.004f, gain: 0.45f, brightness: 0.55f, detune: 0.97f, seed: 0x5EED1u);
            Strike(samples, start: FlashlightRig.OnLatchSeconds, gain: 1f, brightness: 1f, detune: 1f, seed: 0xC11C4u);
        }
        else
        {
            // A lighter press, the latch releasing as the rig puts the light out, and the plunger coming back
            // up against the cap.
            Knock(samples, start: 0f, gain: 0.28f, hertz: 175f, seed: 0x0FF1Cu);
            Strike(samples, start: FlashlightRig.OffLatchSeconds, gain: 0.85f, brightness: 0.8f, detune: 0.93f, seed: 0xB0A7Du);
            Strike(samples, start: 0.034f, gain: 0.3f, brightness: 0.5f, detune: 0.9f, seed: 0x3E1EAu);
        }

        // The brightest fizz off, as the air and the hand take it off a real click a hand's length from the ear.
        float smoothed = 0f;
        float keep = MathF.Exp(-MathF.Tau * 9000f / SampleRate);
        for (int i = 0; i < count; i++)
        {
            smoothed = (smoothed * keep) + (samples[i] * (1f - keep));
            samples[i] = smoothed;
        }

        // Faded out over the last few milliseconds, so nothing is cut off with a tick of its own.
        int fade = SampleRate / 200;
        for (int i = 0; i < fade; i++)
        {
            samples[count - 1 - i] *= i / (float)fade;
        }

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

    // One mechanical strike: a burst of noise a fraction of a millisecond long, heard directly for its edge and
    // struck through the switch's resonances for its colour. brightness tilts it between the dull bottoming out
    // and the bright snap; detune moves the modes, since each part that strikes rings a little differently.
    private static void Strike(float[] samples, float start, float gain, float brightness, float detune, uint seed)
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
            float envelope = (1f - MathF.Exp(-t / 0.00008f)) * MathF.Exp(-t / 0.0005f);
            float noise = Noise(ref state);

            // Differenced, so the burst is a tick and not a thump.
            excitation[i] = (noise - previous) * envelope;
            previous = noise;
        }

        for (int i = 0; i < length; i++)
        {
            samples[first + i] += gain * 0.2f * (0.4f + (0.6f * brightness)) * excitation[i];
        }

        for (int m = 0; m < ModeHertz.Length; m++)
        {
            // The top modes are what makes a snap bright; a dull strike barely reaches them.
            float tilt = MathF.Pow(brightness, m * 0.6f);
            float hertz = ModeHertz[m] * detune * (1f + (0.02f * Noise(ref state)));
            Resonate(samples, first, excitation, hertz, ModeQ[m], gain * ModeGain[m] * tilt);
        }
    }

    // The thumb landing on the boot and the torch's body taking it: a soft, low, heavily damped knock.
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
            excitation[i] = Noise(ref state) * (1f - MathF.Exp(-t / 0.0006f)) * MathF.Exp(-t / 0.002f);
        }

        Resonate(samples, first, excitation, hertz, 1.5f, gain);
        Resonate(samples, first, excitation, hertz * 2.8f, 2.5f, gain * 0.4f);
    }

    // A two-pole band-pass at unit peak gain (the audio EQ cookbook's), run over the excitation and added in.
    private static void Resonate(float[] samples, int first, float[] excitation, float hertz, float q, float gain)
    {
        float w = MathF.Tau * hertz / SampleRate;
        float alpha = MathF.Sin(w) / (2f * q);
        float a0 = 1f + alpha;
        float b0 = alpha / a0;
        float b2 = -alpha / a0;
        float a1 = -2f * MathF.Cos(w) / a0;
        float a2 = (1f - alpha) / a0;

        float x1 = 0f, x2 = 0f, y1 = 0f, y2 = 0f;
        for (int i = 0; i < excitation.Length; i++)
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

    private static float Noise(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return ((state & 0xFFFF) / 32767.5f) - 1f;
    }
}
