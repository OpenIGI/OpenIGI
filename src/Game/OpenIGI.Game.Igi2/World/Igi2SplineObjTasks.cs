using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// A spline-built object, I.G.I. 2's <c>SplineObj</c> (splineobj.c): its waypoints, its children, are joined by segments each bent
/// along the curve between two, which make roads, rails and fences.
/// </summary>
/// <remarks>
/// It has no run of its own: <c>SplineObj_UpdateInternalDataHandler</c> builds the segments as the level loads, and they are drawn,
/// collided with and ridden as the world scene reads them (<see cref="Igi2SplineObject"/>).
/// </remarks>
public sealed partial class Igi2SplineObjTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = Igi2SplineObject.SplineType;

    /// <summary>Gets or sets a value indicating whether its segments are straight.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Linear Segments")]
    public bool LinearSegments { get; set; }

    /// <summary>Gets or sets a value indicating whether the editor shows its waypoints.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Display waypoints")]
    public bool DisplayWaypoints { get; set; }

    /// <summary>Gets or sets a value indicating whether a segment's length is snapped to its model's.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Snap Length")]
    public bool SnapLength { get; set; }

    /// <summary>Gets or sets a value indicating whether its waypoints face along the curve.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Automatic Orientation")]
    public bool AutomaticOrientation { get; set; }

    /// <summary>Gets or sets how many frames a segment is bent through.</summary>
    [ScriptParam(ScriptParamType.Int32, "Number of Matrices / Segment")]
    public int MatricesPerSegment { get; set; } = Igi2SplineSweep.MaximumAreas;

    /// <summary>Gets or sets the level of detail its collision is built from.</summary>
    [ScriptParam(ScriptParamType.Int32, "Collision LOD")]
    public int CollisionLod { get; set; }

    /// <summary>Gets or sets where the editor shows it.</summary>
    [ScriptParam(ScriptParamType.Real32x3, "Position")]
    public Vector3f Position { get; set; }

    /// <summary>Gets or sets the editor's turn of it.</summary>
    [ScriptParam(ScriptParamType.Angle, "Gamma Orientation")]
    public float GammaOrientation { get; set; }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.RunChildren(this);
    }
}

/// <summary>
/// A waypoint of a spline-built object, I.G.I. 2's <c>SplineObjWaypoint</c> (splineobj.c): where it is and how it faces, the model
/// drawn at it, and the model the segment from it to the next is bent from.
/// </summary>
/// <remarks>
/// Its handlers register it and its segment in the world and answer traces, lines of sight, ground and material queries; the world
/// scene and the collision world build those from it as the level loads.
/// </remarks>
public sealed partial class Igi2SplineObjWaypointTask : Igi2Task, IIgi2PlacedTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = Igi2SplineObject.WaypointType;

    /// <summary>Gets or sets its turn, as three Euler angles.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets where it is.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the model drawn at it.</summary>
    [ScriptParam(ScriptParamType.String16, "Waypoint Model")]
    public string WaypointModel { get; set; } = string.Empty;

    /// <summary>Gets or sets the model the segment from it is bent from.</summary>
    [ScriptParam(ScriptParamType.String16, "Segment Model")]
    public string SegmentModel { get; set; } = string.Empty;

    /// <summary>Gets or sets how many frames its segment is bent through.</summary>
    [ScriptParam(ScriptParamType.Int32, "NumAreas")]
    public int Areas { get; set; }

    /// <summary>Gets or sets a value indicating whether it is aligned.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Align")]
    public bool Align { get; set; }

    /// <summary>Gets or sets a value indicating whether its segment is flipped.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Flip")]
    public bool Flip { get; set; }

    /// <summary>Gets or sets a value indicating whether it faces along the curve.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Automatic Orientation")]
    public bool AutomaticOrientation { get; set; }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.RunChildren(this);
    }
}
