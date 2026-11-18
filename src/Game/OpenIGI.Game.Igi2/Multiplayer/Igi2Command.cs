using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>Where a player looked as one of its commands began, and whether its look was the unfiltered Smooth Motion one.</summary>
/// <param name="IdealGamma">The heading the command's turn is added to, <c>Human_s.vIdealGamma</c>.</param>
/// <param name="IdealPitch">The view's pitch the command's look is added to, <c>HumanView_s.vIdealPitch</c>.</param>
/// <param name="Unfiltered">Whether the vertical look skipped the original's mouse filters.</param>
public readonly record struct Igi2CommandLook(float IdealGamma, float IdealPitch, bool Unfiltered);

/// <summary>
/// An I.G.I. 2 client's command as it goes to the server: the player's channel frame, where it looked as it began, and the
/// weapon it had chosen once it had run.
/// </summary>
/// <remarks>
/// <para>
/// The channel frame turns the player by the mouse's movement, not to a heading. Run from a heading the server's copy did not
/// share, the same frame turns it somewhere else, and every step after goes another way: a turn the two once disagreed about
/// stays between them for good, and the client was put back again and again. So the command also says where the client's
/// player looked before the frame turned it, as Project I.G.I.'s command carries its yaw and pitch, and the server's copy
/// starts each command from there -- the frame then turns it exactly as it turned the client's.
/// </para>
/// <para>
/// The weapon keys are steps too -- next, previous, a category -- each from what is in hand, and the server changes its copy's
/// weapons on its own tick (a pickup taken, a grenade thrown away) while the client hears of it later. A key between the two
/// lands on another weapon there, and every step after kept them a weapon apart. So the command carries the weapon the client
/// chose as a number, Project I.G.I.'s chosen weapon, and the server's copy picks that one by number in place of the keys.
/// </para>
/// </remarks>
public static class Igi2Command
{
    private const int Gamma = Igi2HumanChannels.Count;
    private const int Pitch = Gamma + 1;
    private const int Flags = Pitch + 1;
    private const int Chosen = Flags + 1;

    // The flags: the look is carried at all (a blank command, all noughts, carries none and turns nobody to north), and it
    // was the unfiltered one. The chosen weapon is carried plus one, so nought is none.
    private const int HasLook = 1;
    private const int UnfilteredLook = 2;

    /// <summary>The command's length: the channel frame, the look and the chosen weapon.</summary>
    public const int ChannelCount = Chosen + 1;

    // The keys that step the weapon from what is in hand, which the chosen weapon stands in for on the server.
    private static readonly Igi2HumanChannel[] WeaponSteps =
    [
        Igi2HumanChannel.NextWeapon,
        Igi2HumanChannel.PrevWeapon,
        Igi2HumanChannel.WeaponCategory1,
        Igi2HumanChannel.WeaponCategory1 + 1,
        Igi2HumanChannel.WeaponCategory1 + 2,
        Igi2HumanChannel.WeaponCategory1 + 3,
        Igi2HumanChannel.WeaponCategory1 + 4,
        Igi2HumanChannel.WeaponCategory1 + 5,
        Igi2HumanChannel.WeaponCategory1 + 6,
        Igi2HumanChannel.WeaponCategory9,
    ];

    /// <summary>Writes a command.</summary>
    /// <param name="frame">The player's channel frame, <see cref="Igi2HumanChannels.Count"/> values.</param>
    /// <param name="look">Where it looked as the frame was run.</param>
    /// <param name="chosen">The weapon it had chosen once the frame had run, by number, or -1 for none.</param>
    /// <param name="command">Where the command goes, <see cref="ChannelCount"/> values.</param>
    public static void Pack(ReadOnlySpan<float> frame, in Igi2CommandLook look, int chosen, Span<float> command)
    {
        frame[..Igi2HumanChannels.Count].CopyTo(command);
        command[Gamma] = look.IdealGamma;
        command[Pitch] = look.IdealPitch;
        command[Flags] = HasLook | (look.Unfiltered ? UnfilteredLook : 0);
        command[Chosen] = chosen < 0 ? 0f : chosen + 1;
    }

    /// <summary>
    /// Reads a command: the channel frame, with the weapon its client chose put in as the pick and its weapon steps taken out.
    /// </summary>
    /// <param name="command">The command, <see cref="ChannelCount"/> values.</param>
    /// <param name="frame">Where the channel frame goes, <see cref="Igi2HumanChannels.Count"/> values.</param>
    /// <returns>
    /// Where the player looked as the frame was run, a pitch out of the view's reach held to it; or <see langword="null"/> for
    /// a command that carries no look.
    /// </returns>
    public static Igi2CommandLook? Unpack(ReadOnlySpan<float> command, Span<float> frame)
    {
        command[..Igi2HumanChannels.Count].CopyTo(frame);
        float chosen = command[Chosen] - 1f;
        if (float.IsFinite(chosen) && chosen >= 0f)
        {
            foreach (Igi2HumanChannel step in WeaponSteps)
            {
                frame[(int)step] = 0f;
            }

            frame[(int)Igi2HumanChannel.SelectWeapon] = MathF.Floor(chosen);
        }

        int flags = float.IsFinite(command[Flags]) ? (int)command[Flags] : 0;
        if ((flags & HasLook) == 0 || !float.IsFinite(command[Gamma]) || !float.IsFinite(command[Pitch]))
        {
            return null;
        }

        return new Igi2CommandLook(
            command[Gamma],
            Math.Clamp(command[Pitch], -Igi2HumanView.PitchLimit, Igi2HumanView.PitchLimit),
            (flags & UnfilteredLook) != 0);
    }
}
