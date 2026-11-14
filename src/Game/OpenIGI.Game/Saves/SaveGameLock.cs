namespace OpenIGI.Game.Saves;

/// <summary>
/// Holds every save game shut while a multiplayer session runs -- co-op, a versus match, a dedicated server, a demo played
/// back -- whichever game is played and whatever asks: nothing is saved, nothing is loaded, no slot is listed, and nothing is
/// carried from one mission to the next.
/// </summary>
/// <remarks>
/// <para>
/// A session's level is the server's: a save read onto it would put one machine's level back and leave the others where they
/// were, and a save made of it would keep a level the player's own game never had. I.G.I. 2 refuses its save screen outside a
/// single player's game the same way (<c>HumanPlayer_IsSaveAllowed</c>, <c>HumanPlayer_SetQuickLoadSlot</c>).
/// </para>
/// <para>
/// The save games themselves read this (<see cref="MissionSaves"/>), so no path round a menu or a key reaches a file. Counted
/// rather than a flag, as <see cref="Player.PlayerCheatLock"/> is, so two games in one process each hold and release their own.
/// </para>
/// </remarks>
public static class SaveGameLock
{
    private static int _holds;

    /// <summary>Gets a value indicating whether a session holds the save games shut.</summary>
    public static bool IsHeld => Volatile.Read(ref _holds) > 0;

    /// <summary>Takes one hold, for a session that has begun.</summary>
    public static void Hold() => Interlocked.Increment(ref _holds);

    /// <summary>Gives back one hold, for a session that has ended.</summary>
    public static void Release()
    {
        if (Interlocked.Decrement(ref _holds) < 0)
        {
            Interlocked.Exchange(ref _holds, 0);
        }
    }
}
