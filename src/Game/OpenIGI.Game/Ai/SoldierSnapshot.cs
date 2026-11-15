using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Ai;

/// <summary>
/// What one soldier looked like at the moment the developer inspector asked.
/// </summary>
/// <remarks>
/// A copy rather than a reference. The inspector draws on the far side of an interface that must not
/// see gameplay types, and a panel holding a live soldier across a mission change would be holding one
/// the loader had already thrown away.
/// </remarks>
/// <param name="Name">The level's own name for the soldier.</param>
/// <param name="Position">Where it stands, in metres — the unit every panel and command works in.</param>
/// <param name="IsDead">Whether it has been killed.</param>
/// <param name="Animation">The animation clip it is playing.</param>
/// <param name="HealthFraction">How much health remains, 0 to 1, or -1 when it has no health model.</param>
/// <param name="IsCutsceneActor">Whether it was staged for a cutscene rather than spawned for play.</param>
public readonly record struct SoldierSnapshot(
    string Name,
    Vector3d Position,
    bool IsDead,
    int Animation,
    float HealthFraction,
    bool IsCutsceneActor)
{
    /// <summary>Gets whether a mind runs the soldier, so the fields of its mind below mean something.</summary>
    public bool HasMind { get; init; }

    /// <summary>Gets which way it faces, in radians about the up axis: forward is <c>(-sin, cos)</c> across the ground.</summary>
    public float Facing { get; init; }

    /// <summary>Gets whether it is alerted, which widens how far it sees.</summary>
    public bool Alerted { get; init; }

    /// <summary>Gets whether the one it is watching -- the player, mostly -- is in its sight now.</summary>
    public bool SeesTarget { get; init; }

    /// <summary>Gets how far it sees as it stands, in metres.</summary>
    public double SightMetres { get; init; }

    /// <summary>Gets half the width of what it sees, in radians either side of where it faces.</summary>
    public double SightHalfAngle { get; init; }

    /// <summary>Gets what it is doing, in a word or two: its activity and the script action it runs.</summary>
    public string Doing { get; init; } = string.Empty;

    /// <summary>Gets the waypoints it has still to walk, in metres.</summary>
    public IReadOnlyList<Vector3d> RouteMetres { get; init; } = [];
}
