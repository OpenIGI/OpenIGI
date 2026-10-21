using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// A living human's per-tick presence for mission queries: the player or a placed soldier.
/// Mission scripts count these inside authored <c>AreaActivate</c> volumes (whose criteria name a team
/// or the player) and gate on a soldier's <c>isDead</c> script variable, so the runtime needs to know
/// where each human stands, which team it belongs to, and whether it is still alive.
/// </summary>
/// <param name="TaskId">The owning task's id (<c>HumanSoldier_406</c> → 406); the player has no task here.</param>
/// <param name="Team">The human's team, as the mission script's criteria names it (0 or 1).</param>
/// <param name="Position">
/// The point an area tests, in engine units: the human's origin (<c>Human+32</c>), about 0.95 m over its feet
/// standing -- what retail's criteria callbacks pass the box test, not where the feet are.
/// </param>
/// <param name="IsDead">Whether the human has died; a dead human is not an area occupant and reports <c>isDead</c>.</param>
public readonly record struct HumanOccupant(int TaskId, int Team, Vector3d Position, bool IsDead);

/// <summary>A live vehicle's per-tick presence for mission area queries.</summary>
/// <param name="TaskId">The owning vehicle task id.</param>
/// <param name="Position">The current world position in engine units.</param>
public readonly record struct VehicleOccupant(int TaskId, Vector3d Position);

/// <summary>The player as an area sees it when the level walk reaches the area.</summary>
/// <param name="Position">The player's primary world projection, when one exists.</param>
/// <param name="Team">The player's team.</param>
/// <param name="ExtraPosition">An alternate projection of the same player, counted at most once with the first.</param>
/// <param name="Alive">Whether the player can occupy areas.</param>
public readonly record struct AreaPlayer(Vector3d? Position, int Team, Vector3d? ExtraPosition, bool Alive);
