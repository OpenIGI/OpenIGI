namespace OpenIGI.Rendering;

/// <summary>Why the frames before this one should be forgotten.</summary>
/// <remarks>
/// Everything temporal — the upscaler's history, the shadow and fog accumulation, the occlusion, an
/// NVIDIA reconstruction's own — assumes this frame continues the last. When it does not, carrying the
/// history over smears the old view across the new one for as long as the history lasts. The reasons are
/// kept rather than collapsed to a flag because a log line naming the cut is what makes a smear that was
/// <em>not</em> caught diagnosable.
/// </remarks>
[Flags]
public enum HistoryResetReason
{
    /// <summary>No reset.</summary>
    None = 0,

    /// <summary>The camera jumped: a cutscene cut, a death camera, a respawn.</summary>
    CameraCut = 1 << 0,

    /// <summary>A level was loaded, or a menu or loading screen gave way to play.</summary>
    LevelLoad = 1 << 1,

    /// <summary>The render or output size changed.</summary>
    Resize = 1 << 2,

    /// <summary>The reconstruction or its quality mode changed.</summary>
    ReconstructionChanged = 1 << 3,

    /// <summary>The player was moved, not walked: a teleport, a ladder, a vehicle seat.</summary>
    Teleport = 1 << 4,

    /// <summary>The projection jumped: a scope or binoculars raised or lowered.</summary>
    ProjectionChanged = 1 << 5,

    /// <summary>The game resumed after a pause or a renderer switch.</summary>
    Resume = 1 << 6,

    /// <summary>The camera moved further in one frame than any continuous motion does.</summary>
    Discontinuity = 1 << 7,
}
