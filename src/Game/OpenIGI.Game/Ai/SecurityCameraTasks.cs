using OpenIGI.Game.Level;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Ai;

/// <summary>A security camera: Project I.G.I.'s <c>SCamera</c>.</summary>
/// <remarks>
/// Its run handler (<c>0x442610</c>) runs its children -- the lens, which sweeps and scans (<c>0x441770</c>) -- and then
/// evaluates its power. <see cref="SecurityCameraRegistry"/> keeps the level's cameras; the task runs its own where the
/// level's walk reaches it.
/// </remarks>
public sealed class SecurityCameraTask : DeclaredLevelTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "SCamera";

    /// <summary>Gets or sets the level's cameras, which step this one; none until they are built.</summary>
    internal SecurityCameraRegistry? Cameras { get; set; }

    /// <inheritdoc/>
    /// <remarks>With nothing to step it, it is walked as a plain container is.</remarks>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (Cameras?.RunFromWalk(this, level) != true)
        {
            level.RunChildren(this);
        }
    }
}

/// <summary>The terminal that switches a set of cameras: Project I.G.I.'s <c>SCameraControl</c>, a kind of <c>Terminal</c>.</summary>
/// <remarks>
/// Its run handler (<c>0x440D70</c>) is the terminal's, then its Detection Expression, so a control reads this tick's
/// <c>isDetection</c> from every camera ahead of it in the tree and last tick's from those after.
/// </remarks>
public sealed class SecurityCameraControlTask : WorldInteractionTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "SCameraControl";

    /// <summary>Gets or sets the level's cameras, which hold this control's detection; none until they are built.</summary>
    internal SecurityCameraRegistry? Cameras { get; set; }

    /// <inheritdoc/>
    public override void Run(LevelRun level)
    {
        base.Run(level);
        Cameras?.UpdateDetectionFromWalk(this);
    }
}
