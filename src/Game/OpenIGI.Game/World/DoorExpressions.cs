namespace OpenIGI.Game.World;

/// <summary>
/// A door's expressions as its update runs them, between its children and its motion: Project I.G.I.'s door update
/// (<c>0x446D10</c>, <c>0x446DF0</c> to <c>0x446E82</c>) and I.G.I. 2's <c>Door_RunHandler</c>, which are the one function.
/// </summary>
/// <remarks>
/// What the two games' doors do with the command differs -- Project I.G.I.'s swings and slides on two channels, I.G.I. 2's
/// moves one slider and can be held or opened away from its opener -- so each keeps its own motion and calls this for the part
/// that is the same.
/// </remarks>
public static class DoorExpressions
{
    /// <summary>
    /// The Open and Close expressions: a door that stood fully closed is commanded open when its Open expression holds; one that
    /// stood fully open counts the tick, then is commanded shut when its Close expression holds; any other's count is nought.
    /// </summary>
    /// <param name="wasClosed">Whether the door stood fully closed after its last update.</param>
    /// <param name="wasOpen">Whether it stood fully open after its last update.</param>
    /// <param name="open">The open command, which the expressions set and clear.</param>
    /// <param name="ticksOpen">The ticks it has stood open, <c>nDoorOpenTicks</c>.</param>
    /// <param name="opens">The Open expression, true when its value is not nought, or <see langword="null"/> for none.</param>
    /// <param name="closes">The Close expression, likewise.</param>
    /// <returns>Whether the command changed.</returns>
    /// <remarks>
    /// Open is asked only while closed and Close only while open, so a Close expression read as the door starts to open does not
    /// cancel it; the count is taken before the Close expression reads it.
    /// </remarks>
    public static bool Command(bool wasClosed, bool wasOpen, ref bool open, ref int ticksOpen, Func<bool>? opens, Func<bool>? closes)
    {
        bool before = open;
        if (wasClosed && opens?.Invoke() == true)
        {
            open = true;
        }

        if (wasOpen)
        {
            ticksOpen++;
            if (closes?.Invoke() == true)
            {
                open = false;
            }
        }
        else
        {
            ticksOpen = 0;
        }

        return open != before;
    }

    /// <summary>
    /// The Locked expression: its value's low byte when the door has one that compiled, else the lock as it was, so a script's
    /// write to it stands; a picked lock is open whatever the expression says.
    /// </summary>
    /// <param name="locked">The lock, <c>isLocked</c>.</param>
    /// <param name="picked">Whether its lock has been picked, <c>isPicked</c>.</param>
    /// <param name="value">The Locked expression's value, or <see langword="null"/> when it has none.</param>
    public static void Lock(ref byte locked, bool picked, Func<double>? value)
    {
        if (value is not null)
        {
            locked = unchecked((byte)(long)value());
        }

        if (picked)
        {
            locked = 0;
        }
    }
}
