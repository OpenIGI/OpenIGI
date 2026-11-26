namespace OpenIGI.Game.Multiplayer;

/// <summary>
/// A soldier's health as a co-op server's snapshots carry it (<see cref="ReplicatedHuman.Health"/>): what is left of it, as a
/// share of the soldier's own health before a squad's toughness scaled it, in 64ths. Each game knows a soldier's own health
/// from the level's data, so a client reads the server's soldier without knowing how many players toughen it.
/// <see cref="Unforeseeable"/> marks one whose health tells nothing of when it falls: harm kept off it, or the head's one shot.
/// </summary>
public static class SoldierVitality
{
    /// <summary>The byte for a soldier whose death is not to be foreseen.</summary>
    public const byte Unforeseeable = byte.MaxValue;

    /// <summary>How much of a soldier's own health one step of the byte is.</summary>
    public const float Step = 1f / 64f;

    /// <summary>Writes what is left of a soldier.</summary>
    /// <param name="remaining">The health it has left, in its game's units; nought once dead.</param>
    /// <param name="ownMaximum">Its own health, unscaled, in the same units.</param>
    /// <param name="foreseeable">Whether a client may foresee its death.</param>
    /// <returns>The byte.</returns>
    public static byte Encode(float remaining, float ownMaximum, bool foreseeable)
    {
        if (!foreseeable || !(ownMaximum > 0f) || !float.IsFinite(remaining))
        {
            return Unforeseeable;
        }

        // Rounded up: a client never takes a soldier for weaker than it is.
        double steps = Math.Ceiling((Math.Max(0f, remaining) / ownMaximum / Step) - 1e-3);
        return steps >= Unforeseeable ? Unforeseeable : (byte)steps;
    }

    /// <summary>Reads what is left of a soldier.</summary>
    /// <param name="value">The byte.</param>
    /// <param name="ownMaximum">Its own health, unscaled, in its game's units, as this game has it.</param>
    /// <returns>The health left, or <see langword="null"/> when its death is not to be foreseen.</returns>
    public static float? Decode(byte value, float ownMaximum) =>
        value == Unforeseeable || !(ownMaximum > 0f) ? null : value * Step * ownMaximum;
}
