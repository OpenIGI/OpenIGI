namespace OpenIGI.Game.Player;

/// <summary>Holds every cheat off while a multiplayer session runs, whatever the cheat variables say.</summary>
/// <remarks>
/// Counted rather than a flag, so two games in one process -- a co-op test's host and joiner -- each hold and release
/// their own, and one leaving its session does not lift the other's. <see cref="PlayerCheatOptions.Current"/> reads
/// <see cref="IsHeld"/>; the console's <c>cheats</c> gate and the host's free camera refuse while it is held.
/// </remarks>
public static class PlayerCheatLock
{
    private static int _holds;

    /// <summary>Gets a value indicating whether a session holds cheats off.</summary>
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
