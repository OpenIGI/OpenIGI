using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Objects;

/// <summary>What a radio does with its sounds this tick.</summary>
/// <param name="Play">The slot of the sound to start, one of <see cref="RadioChatter.SoundSlots"/>, or <see langword="null"/> to start none.</param>
/// <param name="Drop">Whether the voice it held has ended and is let go of.</param>
/// <param name="Silence">Whether the voice it holds is cut: the radio went off, or is off, with one still held.</param>
/// <param name="Shutdown">Whether its shutdown sound plays: it was on as the tick began and is off now.</param>
public readonly record struct RadioStep(int? Play, bool Drop, bool Silence, bool Shutdown);

/// <summary>
/// A radio's chatter, as both games run it (Project I.G.I. <c>0x43AF90</c>, I.G.I. 2 <c>Radio_RunHandler</c>): after its
/// <c>ExplodeObject</c>'s update it is off once blown up, else as its expression says; while on it waits out the sound it
/// holds, then a drawn gap, then starts one of its four sounds; going off it cuts the sound and plays its shutdown.
/// </summary>
/// <remarks>
/// Both games draw twice from the level's stream as a sound starts, the slot first (the draw's low two bits) and the gap
/// second (the draw modulo 150, plus 150 ticks). The titles differ only in where the stream and the sounds come from.
/// </remarks>
public sealed class RadioChatter
{
    /// <summary>How many sounds a radio is authored with.</summary>
    public const int SoundSlots = 4;

    /// <summary>The least wait between sounds, in ticks; the draw adds up to as much again.</summary>
    public const int SoundGapTicks = 150;

    /// <summary>Gets or sets <c>isOn</c>, which the level's scripts read and may write.</summary>
    public bool IsOn { get; set; }

    /// <summary>Gets or sets the ticks left before the next sound.</summary>
    public int Countdown { get; set; }

    /// <summary>Runs the radio's own part of its update.</summary>
    /// <param name="isOn">
    /// What it is this tick: <see cref="Switched"/> of its explosion and its expression, or what a server told a client.
    /// </param>
    /// <param name="holdsVoice">Whether it holds a voice from an earlier tick.</param>
    /// <param name="voicePlaying">Whether that voice still plays.</param>
    /// <param name="random">The level's stream.</param>
    /// <returns>What to do with its sounds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
    public RadioStep Step(bool isOn, bool holdsVoice, bool voicePlaying, IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        bool wasOn = IsOn;
        IsOn = isOn;
        if (IsOn)
        {
            if (holdsVoice && voicePlaying)
            {
                return default;
            }

            if (Countdown > 0)
            {
                Countdown--;
                return new RadioStep(null, Drop: holdsVoice, Silence: false, Shutdown: false);
            }

            int slot = (int)random.NextBounded(SoundSlots);
            Countdown = (int)random.NextBounded(SoundGapTicks) + SoundGapTicks;
            return new RadioStep(slot, Drop: holdsVoice, Silence: false, Shutdown: false);
        }

        return new RadioStep(null, Drop: holdsVoice, Silence: holdsVoice && voicePlaying, Shutdown: wasOn);
    }

    /// <summary>What a radio is this tick: off once its object has exploded, else as its expression says, else as it was.</summary>
    /// <param name="current">What it is now.</param>
    /// <param name="exploded">Whether its <c>ExplodeObject</c> has exploded.</param>
    /// <param name="expression">Its "On expression" as read this tick, or <see langword="null"/> when it has none.</param>
    /// <returns>Whether it is on.</returns>
    public static bool Switched(bool current, bool exploded, bool? expression) => !exploded && (expression ?? current);
}
