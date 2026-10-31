using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>
/// A multiplayer map's mission, I.G.I. 2's <c>Network_Mission</c> (netmission.c): where the camera looks from while a player
/// waits to spawn, and what ends the mission early.
/// </summary>
public sealed partial class Igi2NetworkMissionTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Network_Mission";

    /// <summary>Gets or sets where the waiting camera stands.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Start Camera Position")]
    public Vector3d StartCameraPosition { get; set; }

    /// <summary>Gets or sets the waiting camera's orientation.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Start Camera Orientation")]
    public Vector3f StartCameraOrientation { get; set; }

    /// <summary>Gets or sets the expression that, holding, ends the mission as violated.</summary>
    [ScriptParam(ScriptParamType.VarString, "Mission Violated Expression")]
    public string MissionViolatedExpression { get; set; } = string.Empty;
}

/// <summary>
/// One objective of a multiplayer map, I.G.I. 2's <c>Network_Objective</c> (netmission.c): taken in order, for one team to win
/// against the clock, perhaps needing the C4 bomb.
/// </summary>
public sealed partial class Igi2NetworkObjectiveTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Network_Objective";

    /// <summary>Gets or sets the expression that, holding, completes the objective.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective Expression")]
    public string ObjectiveExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that, holding, fails it.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective Failed Expression")]
    public string FailedExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets its place in the order the objectives are taken.</summary>
    [ScriptParam(ScriptParamType.Int32, "Order number for objective")]
    public int Order { get; set; }

    /// <summary>Gets or sets the team whose objective it is: the attackers.</summary>
    [ScriptParam(ScriptParamType.Int32, "Team this objective is for")]
    public int Team { get; set; }

    /// <summary>Gets or sets the time it may take.</summary>
    [ScriptParam(ScriptParamType.Int32, "Time for objective")]
    public int Time { get; set; }

    /// <summary>Gets or sets a value indicating whether the C4 bomb is needed for it.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Is the c4 bomb required for this objective")]
    public bool NeedsC4 { get; set; }
}

/// <summary>
/// Where a team's players spawn, I.G.I. 2's <c>SpawnArea</c> (spawnarea.c): a box holding its spawn points, and the camera a
/// player choosing it looks from.
/// </summary>
public sealed partial class Igi2SpawnAreaTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "SpawnArea";

    private Igi2OrientedBox _box;

    /// <summary>Gets or sets the box's centre.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the box's orientation.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets the box's size; retail's parameter is spelled so.</summary>
    [ScriptParam(ScriptParamType.Real32x3, "Dimentions")]
    public Vector3f Dimensions { get; set; }

    /// <summary>Gets or sets where the camera stands while a player chooses this area.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Camera Position")]
    public Vector3d CameraPosition { get; set; }

    /// <summary>Gets or sets that camera's orientation.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Camera Orientation")]
    public Vector3f CameraOrientation { get; set; }

    /// <summary>Gets or sets the team that spawns here.</summary>
    [ScriptParam(ScriptParamType.Int32, "Team")]
    public int Team { get; set; }

    /// <summary>Whether a point lies in the area, <c>SpawnArea_IsInsideArea</c>.</summary>
    /// <param name="point">The point.</param>
    /// <returns><see langword="true"/> inside the box, edges included.</returns>
    public bool IsInside(Vector3d point) => _box.IsInside(point, Position);

    /// <summary>Gets the area's spawn points: its children of that type, as the original keeps them.</summary>
    /// <returns>The points, in the tree's order.</returns>
    public IEnumerable<Igi2SpawnPointTask> Points() => Children.OfType<Igi2SpawnPointTask>();

    /// <inheritdoc/>
    protected override void OnParsed() => _box = Igi2OrientedBox.From(Position, Orientation, Dimensions);
}

/// <summary>Which spawn point of an area a player is put at: the one used least lately, <c>SpawnPoint_GetSpawnPoint</c>.</summary>
/// <remarks>
/// <para>
/// <c>SpawnPoint_Use</c> stamps a point with a count that rises by one each use, and <c>SpawnPoint_GetSpawnPoint</c> takes the
/// area's point with the lowest stamp, the earliest on a tie. One count serves the whole map, so a team spreads over its
/// points before any is used twice.
/// </para>
/// <para>
/// Retail never offers an area's first point: <c>igi2 0x45D340</c> takes the first child of the type and steps at once to the
/// next before it starts comparing. The same here. An area with a single point would then give none, where retail's caller
/// is left without one; this falls back to that point instead.
/// </para>
/// </remarks>
public sealed class Igi2SpawnSelector
{
    private readonly Dictionary<Igi2SpawnPointTask, int> _stamps = [];
    private int _uses;

    /// <summary>Takes the area's least lately used point and stamps it used.</summary>
    /// <param name="area">The area.</param>
    /// <returns>The point, or <see langword="null"/> when the area has none.</returns>
    public Igi2SpawnPointTask? Pick(Igi2SpawnAreaTask area)
    {
        ArgumentNullException.ThrowIfNull(area);
        Igi2SpawnPointTask? best = null;
        int bestStamp = int.MaxValue;
        foreach (Igi2SpawnPointTask point in area.Points().Skip(1))
        {
            int stamp = _stamps.GetValueOrDefault(point);
            if (best is null || stamp < bestStamp)
            {
                best = point;
                bestStamp = stamp;
            }
        }

        best ??= area.Points().FirstOrDefault();
        if (best is not null)
        {
            _stamps[best] = ++_uses;
        }

        return best;
    }
}

/// <summary>A place a player stands when spawning, I.G.I. 2's <c>SpawnPoint</c> (spawnpoint.c), in whichever spawn area holds it.</summary>
public sealed partial class Igi2SpawnPointTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "SpawnPoint";

    /// <summary>Gets or sets where the player's feet go.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the heading the player faces.</summary>
    [ScriptParam(ScriptParamType.Angle, "Gamma")]
    public float Gamma { get; set; }
}

/// <summary>Where the C4 bomb may be planted, I.G.I. 2's <c>C4BombArea</c> (c4bomb.c), while its expression holds.</summary>
public sealed partial class Igi2C4BombAreaTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "C4BombArea";

    private Igi2OrientedBox _box;

    /// <summary>Gets or sets the box's centre.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the box's orientation.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets the box's size.</summary>
    [ScriptParam(ScriptParamType.Real32x3, "Dimensions")]
    public Vector3f Dimensions { get; set; }

    /// <summary>Gets or sets the expression under which the bomb may be planted here.</summary>
    [ScriptParam(ScriptParamType.VarString, "Active Expression")]
    public string ActiveExpression { get; set; } = string.Empty;

    /// <summary>Whether a point lies in the area.</summary>
    /// <param name="point">The point.</param>
    /// <returns><see langword="true"/> inside the box, edges included.</returns>
    public bool IsInside(Vector3d point) => _box.IsInside(point, Position);

    /// <inheritdoc/>
    protected override void OnParsed() => _box = Igi2OrientedBox.From(Position, Orientation, Dimensions);
}
