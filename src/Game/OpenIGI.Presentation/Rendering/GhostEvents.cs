namespace OpenIGI.Presentation.Rendering;

/// <summary>What happened to a race's ghost's runner since the ghost was last drawn, which its cloak answers to.</summary>
[Flags]
public enum GhostEvents
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>The runner fired: the cloak shudders with the shot.</summary>
    Fired = 1,

    /// <summary>The runner was hurt: the cloak fails hard.</summary>
    Hurt = 2,

    /// <summary>The player and the ghost changed places, one way or the other: the cloak fails once, cleanly.</summary>
    Overtaken = 4,

    /// <summary>The runner fell: after it has lain a moment the ghost dissolves.</summary>
    Died = 8,

    /// <summary>The runner's run is over: the ghost dissolves.</summary>
    Finished = 16,
}
