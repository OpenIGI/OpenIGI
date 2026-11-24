namespace OpenIGI.Game.World;

/// <summary>
/// Whether the game is stepping its level or drawing it: what a read of a level's state may do. A frame drawn -- any number of
/// times between two ticks, or none at all on a server -- may look at the level but must change nothing of it, or the level
/// would play by how it was watched, and a replay or a server playing it again would part from the run.
/// </summary>
/// <remarks>
/// Set by the host about its frame: presenting outside the level's tick, stepping within it. Stepping by default, so code run
/// without a host -- a test stepping a player -- steps as the level does.
/// </remarks>
public static class SimulationPhase
{
    [ThreadStatic]
    private static bool t_presenting;

    /// <summary>Gets a value indicating whether the game is drawing its level now, not stepping it.</summary>
    public static bool IsPresenting => t_presenting;

    /// <summary>Marks the frame as drawing the level: what reads it from here changes nothing until <see cref="Step"/>.</summary>
    public static void Present() => t_presenting = true;

    /// <summary>Marks the frame as stepping the level: reads take what the simulation takes, as the original's do.</summary>
    public static void Step() => t_presenting = false;
}
