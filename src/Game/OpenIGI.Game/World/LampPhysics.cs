using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// The curves a lamp moves along: how it goes out, how it comes on, and how a failing one stutters.
/// </summary>
/// <remarks>
/// Static, and working on one lamp's state at a time, so that <see cref="LampSwitchboard"/> keeps the
/// switches, the supply and the order lamps are stepped in, and this keeps the physics of a fitting. Every
/// draw comes from the lamp's own generator, in the order the board always drew them, and every sound is
/// raised into the board's list at the instant the light does the thing it is the sound of.
/// </remarks>
internal static class LampPhysics
{
    /// <summary>How long a tube's flash holds, in seconds, once the starter opens.</summary>
    private const float FlashSeconds = 0.045f;

    /// <summary>When a tube's flash has dropped back nearly dark, as the arc settles or fails.</summary>
    private const float DipEndSeconds = 0.085f;

    /// <summary>How much of its output a lamp still gives out, this long after the current stopped.</summary>
    /// <remarks>
    /// <para>
    /// Two decays added, because a fitting going out has two things happening in it and they run at very
    /// different rates. Most of the light stops within a couple of mains cycles — an arc extinguishes,
    /// a filament's surface cools — and that is the fast term, around 35 ms. What is left is thermal
    /// mass and phosphor persistence, and it lingers for a fifth of a second as the ember or the
    /// afterglow everybody has seen and nobody would describe.
    /// </para>
    /// <para>
    /// A single exponential gets the timing right and the LOOK wrong: it is either fast enough to be a
    /// snap or slow enough to be a dissolve, and never both. The sum is what reads as a light going out.
    /// </para>
    /// </remarks>
    internal static float Cooling(float seconds) =>
        (0.82f * MathF.Exp(-seconds / 0.035f)) + (0.18f * MathF.Exp(-seconds / 0.16f));

    /// <summary>How far into coming back on a lamp is, from nought to one.</summary>
    /// <remarks>
    /// <para>
    /// A tube does not fade up. It draws a dim glow across the cathodes, strikes — one hard flash, far
    /// brighter than the dim it came from — drops nearly dark again as the arc settles, and only then
    /// climbs to full over a few tenths of a second as the mercury warms. That sequence is the whole
    /// reason a switched-on lamp reads as a lamp; a linear ramp reads as a slider being dragged.
    /// </para>
    /// <para>
    /// Not every fitting does it, so not every lamp here does: a fixture whose generator says otherwise
    /// simply warms, the way a filament does. A corridor whose lamps all strike in the same way at the
    /// same moment is the artefact that says a single effect is being played on all of them.
    /// </para>
    /// <para>
    /// The sounds are raised from here, at the instant the light does the thing they are the sound of.
    /// </para>
    /// </remarks>
    internal static float Warming(ref Lamp lamp, int index, List<LampEvent> events)
    {
        float t = lamp.Elapsed;

        if (lamp.Strikes)
        {
            return lamp.Character == LampCharacter.Discharge
                ? Igniting(ref lamp, index, events, t)
                : Striking(ref lamp, index, events, t);
        }

        // Nothing to ignite: a filament, a tube on an instant-start ballast, or any lamp that was still
        // partly lit and so still has its arc. Heard only when it really is a start from dark.
        if (!lamp.Fired)
        {
            lamp.Fired = true;

            if (lamp.From < 0.15f)
            {
                LampSwitchboard.Raise(
                    events,
                    index,
                    lamp.Character == LampCharacter.Filament ? LampSound.Filament : LampSound.Arc);
            }
        }

        // The mercury warming, or the filament heating: fast at first and asymptotic, which is what both
        // of them actually do. Normalised so the curve reaches one rather than approaching it forever.
        return Math.Clamp(1f - MathF.Exp(-t / (lamp.Character == LampCharacter.Discharge ? 0.3f : 0.19f)), 0f, 1f);
    }

    /// <summary>A tube on a glow starter coming on: preheat, flash, and as many goes as it takes.</summary>
    /// <remarks>
    /// <para>
    /// The starter is a little neon bulb with a bimetal strip in it. It glows, the strip warms and closes,
    /// current heats the tube's cathodes -- the dim orange glow at each end -- and then the strip cools
    /// and springs open, and the choke's kick across the tube is the flash. If the arc holds, that is the
    /// lamp lit; if not, the starter closes again and the whole thing repeats. A warm tube on a good
    /// starter catches first time in a tenth of a second. A cold one at dusk often does not: a second go,
    /// a third, now and then a tube that blinks half a dozen times before it holds, which is the one
    /// everybody notices.
    /// </para>
    /// <para>
    /// And it does not catch at full. Cold mercury is thin vapour, so a tube lit after a day standing dark
    /// comes up at half or two thirds of its brightness and creeps the rest of the way over a few seconds.
    /// </para>
    /// </remarks>
    internal static float Striking(ref Lamp lamp, int index, List<LampEvent> events, float t)
    {
        if (!lamp.Arc)
        {
            float u = t - lamp.AttemptStart;

            if (u < lamp.Preheat)
            {
                // The cathode glow: visible, dim, and not yet the lamp.
                return lamp.Glow * (u / MathF.Max(lamp.Preheat, 0.0001f));
            }

            if (u < lamp.Preheat + FlashSeconds)
            {
                if (!lamp.Fired)
                {
                    lamp.Fired = true;
                    LampSwitchboard.Raise(events, index, LampSound.Starter);
                }

                return lamp.Peak;
            }

            if (u < lamp.Preheat + DipEndSeconds)
            {
                return 0.06f;
            }

            if (lamp.Attempts > 0)
            {
                // The arc did not hold. The starter closes and preheats the cathodes for another go.
                lamp.Attempts--;
                lamp.AttemptStart = t;
                lamp.Fired = false;
                RollStrike(ref lamp);
                return 0f;
            }

            lamp.Arc = true;
            lamp.ArcAt = lamp.AttemptStart + lamp.Preheat + DipEndSeconds;
            LampSwitchboard.Raise(events, index, LampSound.Arc);
        }

        float w = t - lamp.ArcAt;

        return lamp.Cold
            ? Math.Clamp(lamp.ColdStart + ((1f - lamp.ColdStart) * (1f - MathF.Exp(-w / lamp.Ramp))), 0f, 1f)
            : Math.Clamp(1f - MathF.Exp(-w / 0.19f), 0f, 1f);
    }

    /// <summary>A discharge lantern coming on: the ignitor's crackle, a dim arc, and a long climb.</summary>
    /// <remarks>
    /// <para>
    /// A sodium or metal-halide lamp has no cathodes to warm. An ignitor fires pulses of a few kilovolts
    /// across the tube, every half-cycle of the mains, until the gas breaks down -- an audible crackle and
    /// nothing much to see but a faint spitting -- and then the arc holds in the starting gas, dim and the
    /// wrong colour, and climbs as the metal in the tube vaporises. A real one takes minutes. This takes
    /// seconds, because a dusk the player is watching is not minutes long, but keeps the shape: dim at
    /// first, then most of the way quite quickly, then the last of it slowly.
    /// </para>
    /// <para>
    /// A cold tube does not always break down on the first burst, and the ignitor pauses and tries again.
    /// </para>
    /// </remarks>
    internal static float Igniting(ref Lamp lamp, int index, List<LampEvent> events, float t)
    {
        if (!lamp.Arc)
        {
            float u = t - lamp.AttemptStart;

            if (u < LampSwitchboard.IgnitorSeconds)
            {
                if (!lamp.Fired)
                {
                    lamp.Fired = true;
                    LampSwitchboard.Raise(events, index, LampSound.Ignitor);
                }

                // Sparks across the gap rather than light: a faint, unsteady spitting.
                return ((int)(u * 41f) & 3) == 0 ? 0.035f : 0.008f;
            }

            if (lamp.Attempts > 0)
            {
                // Preheat stands for the ignitor's pause between bursts: a discharge lamp has no preheat.
                if (u < LampSwitchboard.IgnitorSeconds + lamp.Preheat)
                {
                    return 0f;
                }

                lamp.Attempts--;
                lamp.AttemptStart = t;
                lamp.Fired = false;
                lamp.Preheat = 0.5f + XorShift32.NextUnit(ref lamp.Rng);
                return 0f;
            }

            lamp.Arc = true;
            lamp.ArcAt = lamp.AttemptStart + LampSwitchboard.IgnitorSeconds;
            LampSwitchboard.Raise(events, index, LampSound.Arc);
        }

        float w = t - lamp.ArcAt;

        return Math.Clamp(0.06f + (0.94f * (1f - MathF.Exp(-w / lamp.Ramp))), 0f, 1f);
    }

    /// <summary>What colour a lamp coming on gives out, before it is giving out its own.</summary>
    internal static Vector3f WarmingTint(ref Lamp lamp)
    {
        switch (lamp.Character)
        {
            case LampCharacter.Tube when lamp.Strikes && !lamp.Arc && lamp.Level < 0.15f:
                // The cathodes heating, with no arc between them yet.
                return new Vector3f(1f, 0.62f, 0.36f);

            case LampCharacter.Discharge when lamp.Strikes:
                if (!lamp.Arc)
                {
                    // The ignitor's sparks: a cold blue-white.
                    return new Vector3f(0.78f, 0.86f, 1f);
                }

                // The starting gas's colour, giving way to the lamp's own as the metal vaporises. Tied to
                // the brightness rather than to time, because both are the same thing happening.
                float own = Math.Clamp((lamp.Level - 0.06f) / 0.94f, 0f, 1f);
                Vector3f start = lamp.Sodium ? new Vector3f(1f, 0.42f, 0.55f) : new Vector3f(0.70f, 0.88f, 1f);
                return start + ((Vector3f.One - start) * own);

            default:
                return Vector3f.One;
        }
    }

    internal static void BeginCooling(ref Lamp lamp)
    {
        lamp.Transition = LampTransition.Cooling;
        lamp.Elapsed = 0f;
        lamp.From = lamp.Level;
    }

    internal static void BeginWarming(ref Lamp lamp, bool cold)
    {
        lamp.Transition = LampTransition.Warming;
        lamp.Elapsed = 0f;
        lamp.From = lamp.Level;
        lamp.Cold = cold;
        lamp.Arc = false;
        lamp.Fired = false;
        lamp.AttemptStart = 0f;

        // Only from dark, because a strike is an arc being established and a lamp that is already half
        // lit has one. Without that condition a lamp brought back up from nine tenths flashes on its way
        // to full, which is not something any fitting does.
        bool fromDark = lamp.From < 0.15f;

        switch (lamp.Character)
        {
            case LampCharacter.Tube:
                // Rolled per switch-on rather than per lamp: the same fitting does not strike identically
                // every time, and a lamp that always flashes at exactly 90 ms is a canned animation. A cold
                // tube always goes through its starter; a warm one on an electronic ballast often does not.
                lamp.Strikes = fromDark && (cold || XorShift32.NextUnit(ref lamp.Rng) < 0.6f);
                lamp.Attempts = !lamp.Strikes ? 0
                    : cold ? ColdStrikeAttempts(ref lamp.Rng)
                    : XorShift32.NextUnit(ref lamp.Rng) < 0.12f ? 1 : 0;
                lamp.Ramp = 0.6f + (XorShift32.NextUnit(ref lamp.Rng) * 0.6f);
                lamp.ColdStart = 0.45f + (XorShift32.NextUnit(ref lamp.Rng) * 0.25f);
                RollStrike(ref lamp);
                break;

            case LampCharacter.Discharge:
                float roll = XorShift32.NextUnit(ref lamp.Rng);
                lamp.Strikes = fromDark;
                lamp.Attempts = cold ? (roll < 0.55f ? 0 : roll < 0.85f ? 1 : 2) : 0;
                lamp.Preheat = 0.5f + XorShift32.NextUnit(ref lamp.Rng);
                lamp.Ramp = cold ? 1.4f + (XorShift32.NextUnit(ref lamp.Rng) * 1.6f) : 0.3f;
                break;

            default:
                lamp.Strikes = false;
                lamp.Attempts = 0;
                break;
        }
    }

    /// <summary>Rolls one go of a tube's starter: how long it preheats, and how bright the flash is.</summary>
    /// <remarks>
    /// A cold starter's bimetal takes far longer to close and open than a warm one's, which is the long
    /// orange-ended pause before a tube at dusk first blinks. A flash that fails is a weaker one -- the
    /// arc barely formed -- and the one that takes is the full flash.
    /// </remarks>
    internal static void RollStrike(ref Lamp lamp)
    {
        lamp.Preheat = lamp.Cold
            ? 0.35f + (XorShift32.NextUnit(ref lamp.Rng) * 0.8f)
            : 0.05f + (XorShift32.NextUnit(ref lamp.Rng) * 0.08f);
        lamp.Peak = lamp.Attempts > 0 ? 0.3f + (XorShift32.NextUnit(ref lamp.Rng) * 0.6f) : 0.95f;
        lamp.Glow = lamp.Cold ? 0.05f + (XorShift32.NextUnit(ref lamp.Rng) * 0.04f) : 0.12f;
    }

    /// <summary>How many times a cold tube's starter fails before it catches.</summary>
    /// <remarks>
    /// Mostly none or one. Now and then a tube is stubborn and blinks several times over a few seconds,
    /// and that rare one is worth more to the scene than any amount of the common case.
    /// </remarks>
    internal static int ColdStrikeAttempts(ref uint rng)
    {
        float roll = XorShift32.NextUnit(ref rng);

        return roll < 0.42f ? 0
            : roll < 0.72f ? 1
            : roll < 0.88f ? 2
            : 3 + (int)(XorShift32.NextUnit(ref rng) * 2f);
    }

    /// <summary>Advances a failing lamp, in the phase it is currently stuck in.</summary>
    /// <remarks>
    /// <para>
    /// A broken fluorescent is not noise. It is a loop of four recognisable states — a dim glow at the
    /// cathodes, a burst of rapid restrikes, a stretch of running unsteadily, and darkness — and what
    /// makes it read as broken rather than as a flicker shader is that the durations are wildly uneven
    /// and the transitions are not a cycle. Random per frame gives white noise, which looks like a fault
    /// in the renderer; this looks like a fault in the lamp.
    /// </para>
    /// <para>
    /// Every phase's brightness is a function of the phase's own clock, never of the frame, so the same
    /// fault plays out identically whatever the frame rate is.
    /// </para>
    /// </remarks>
    /// <returns><see langword="true"/> when the lamp has just begun a burst of restrikes, which is heard.</returns>
    internal static bool StepBroken(ref Lamp lamp, float step)
    {
        lamp.BrokenElapsed += step;
        lamp.BrokenLeft -= step;
        bool struck = false;

        if (lamp.BrokenLeft <= 0f)
        {
            float roll = XorShift32.NextUnit(ref lamp.Rng);

            EnterBroken(ref lamp, lamp.Broken switch
            {
                LampBrokenPhase.Run => roll < 0.5f ? LampBrokenPhase.Dark
                    : roll < 0.8f ? LampBrokenPhase.Glow : LampBrokenPhase.Strike,
                LampBrokenPhase.Dark => roll < 0.75f ? LampBrokenPhase.Strike : LampBrokenPhase.Glow,
                LampBrokenPhase.Strike => roll < 0.55f ? LampBrokenPhase.Run
                    : roll < 0.8f ? LampBrokenPhase.Dark : LampBrokenPhase.Glow,
                _ => roll < 0.6f ? LampBrokenPhase.Strike : LampBrokenPhase.Dark,
            });

            struck = lamp.Broken == LampBrokenPhase.Strike;
        }

        float t = lamp.BrokenElapsed;

        lamp.Level = lamp.Broken switch
        {
            // The arc trying and failing to hold: a hard square wave, because that is what an arc does —
            // it is either struck or it is not, and there is nothing in between to ramp through.
            LampBrokenPhase.Strike => ((int)(t * lamp.BrokenRate) & 1) == 0 ? lamp.BrokenPeak : 0.04f,

            // Lit, but not well. A slow wobble a few per cent deep, at a rate low enough to be seen
            // rather than aliased into a shimmer by whatever the frame rate happens to be.
            LampBrokenPhase.Run => lamp.BrokenPeak
                * (1f - (0.07f * (0.5f + (0.5f * MathF.Sin(t * lamp.BrokenRate))))),

            // The cathodes glowing with no arc across them: dim, orange in a real tube, and unsteady.
            LampBrokenPhase.Glow => 0.06f + (0.05f * (0.5f + (0.5f * MathF.Sin(t * lamp.BrokenRate)))),

            _ => 0f,
        };

        return struck;
    }

    internal static void EnterBroken(ref Lamp lamp, LampBrokenPhase phase)
    {
        lamp.Broken = phase;
        lamp.BrokenElapsed = 0f;

        switch (phase)
        {
            case LampBrokenPhase.Strike:
                // Twelve to thirty-five restrikes a second: fast enough to read as a fault rather than as
                // a signal, slow enough that a 60 Hz frame still resolves individual flashes.
                lamp.BrokenRate = 12f + (XorShift32.NextUnit(ref lamp.Rng) * 23f);
                lamp.BrokenPeak = 0.75f + (XorShift32.NextUnit(ref lamp.Rng) * 0.25f);
                lamp.BrokenLeft = 0.08f + (XorShift32.NextUnit(ref lamp.Rng) * 0.42f);
                break;

            case LampBrokenPhase.Run:
                lamp.BrokenRate = 5f + (XorShift32.NextUnit(ref lamp.Rng) * 9f);
                lamp.BrokenPeak = 0.8f + (XorShift32.NextUnit(ref lamp.Rng) * 0.2f);

                // The long phase, and deliberately the widest range of the four: a tube that fails every
                // second on the second is a metronome. Some of these hold for four seconds and the room
                // looks fine, which is what makes the next dropout land.
                lamp.BrokenLeft = 0.4f + (XorShift32.NextUnit(ref lamp.Rng) * 3.6f);
                break;

            case LampBrokenPhase.Glow:
                lamp.BrokenRate = 3f + (XorShift32.NextUnit(ref lamp.Rng) * 6f);
                lamp.BrokenLeft = 0.12f + (XorShift32.NextUnit(ref lamp.Rng) * 0.78f);
                break;

            default:
                lamp.BrokenLeft = 0.04f + (XorShift32.NextUnit(ref lamp.Rng) * 0.76f);
                break;
        }
    }
}
