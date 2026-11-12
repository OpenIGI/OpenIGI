using OpenIGI.Game.Level;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Ai;

/// <summary>A security camera: Project I.G.I.'s <c>SCamera</c>.</summary>
/// <remarks>
/// Its run handler (<c>0x442610</c>) runs its children -- the lens, which sweeps and scans (<c>0x441770</c>) -- and then
/// evaluates its power. <see cref="SecurityCameraRegistry"/> keeps the level's cameras; the task runs its own where the
/// level's walk reaches it.
/// </remarks>
public sealed class SecurityCameraTask : DeclaredLevelTask, ISecurityCameraDeclaration
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "SCamera";

    /// <summary>Gets or sets the level's cameras, which step this one; none until they are built.</summary>
    internal SecurityCameraRegistry? Cameras { get; set; }

    /// <summary>Gets or sets the camera this task is, or <see langword="null"/> until a registry keeps it.</summary>
    internal SecurityCamera? Camera { get; set; }

    /// <summary>Gets where the holder stands ("Holder Position").</summary>
    public OpenIGI.Core.Numerics.Vector3d HolderPosition => DeclaredTaskValues.Vector(this, "Holder Position");

    /// <summary>Gets the holder's turn about Z ("Holder Gamma").</summary>
    public float HolderGamma => DeclaredTaskValues.RealFromFloatOrDouble(this, "Holder Gamma");

    /// <summary>Gets the head's authored pitch ("Camera Alpha").</summary>
    public float CameraAlpha => DeclaredTaskValues.RealFromFloatOrDouble(this, "Camera Alpha");

    /// <summary>Gets the head's authored turn ("Camera Gamma").</summary>
    public float CameraGamma => DeclaredTaskValues.RealFromFloatOrDouble(this, "Camera Gamma");

    /// <summary>Gets the head's model ("Camera Model"), empty where the level gives none.</summary>
    public string CameraModel => DeclaredTaskValues.TextTrimmed(this, "Camera Model");

    /// <summary>Gets how far right the head sweeps, in degrees ("Rotate Gamma Right (d)").</summary>
    public int RotateGammaRight => DeclaredTaskValues.IntWidening(this, "Rotate Gamma Right (d)", 45);

    /// <summary>Gets how far left the head sweeps, in degrees ("Rotate Gamma Left (d)").</summary>
    public int RotateGammaLeft => DeclaredTaskValues.IntWidening(this, "Rotate Gamma Left (d)", 45);

    /// <summary>Gets how fast the head sweeps, in degrees a second ("Rotate Gamma Speed (d/s)").</summary>
    public int RotateGammaSpeed => DeclaredTaskValues.IntWidening(this, "Rotate Gamma Speed (d/s)", 45);

    /// <summary>Gets how long the head rests at each end of its sweep, in seconds ("Gamma Delay (s)").</summary>
    public float GammaDelay => DeclaredTaskValues.RealFromFloatOrDouble(this, "Gamma Delay (s)", 1f);

    /// <summary>Gets the view cone's height, in degrees ("Viewcone Alpha (d)").</summary>
    public int ViewconeAlpha => DeclaredTaskValues.IntWidening(this, "Viewcone Alpha (d)", 90);

    /// <summary>Gets the view cone's width, in degrees ("Viewcone Gamma (d)").</summary>
    public int ViewconeGamma => DeclaredTaskValues.IntWidening(this, "Viewcone Gamma (d)", 90);

    /// <summary>Gets the view cone's length, in metres ("Viewcone length (m)").</summary>
    public float ViewconeLength => DeclaredTaskValues.RealFromFloatOrDouble(this, "Viewcone length (m)", 10f);

    /// <summary>Gets the expression that powers the camera ("On Expression").</summary>
    public string OnExpression => DeclaredTaskValues.TextTrimmed(this, "On Expression");

    /// <inheritdoc/>
    /// <remarks>With nothing to step it, it is walked as a plain container is.</remarks>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (Camera is not { } camera || Cameras?.RunFromWalk(camera, this, level) != true)
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
public sealed class SecurityCameraControlTask : TerminalTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public new const string TypeName = "SCameraControl";

    /// <summary>Gets or sets the level's cameras, which hold this control's detection; none until they are built.</summary>
    internal SecurityCameraRegistry? Cameras { get; set; }

    /// <summary>Gets or sets what the registry keeps of this control, or <see langword="null"/> until one does.</summary>
    internal SecurityCameraRegistry.ControlState? Control { get; set; }

    /// <summary>Gets the expression that raises the control's alarm ("Detection Expression").</summary>
    public string DetectionExpression => DeclaredTaskValues.TextTrimmed(this, "Detection Expression");

    /// <inheritdoc/>
    public override void Run(LevelRun level)
    {
        base.Run(level);
        if (Control is { } control)
        {
            Cameras?.UpdateDetectionFromWalk(control);
        }
    }
}
