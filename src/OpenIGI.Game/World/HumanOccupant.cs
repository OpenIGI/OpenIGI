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
/// <param name="Position">The current world position in engine units.</param>
/// <param name="IsDead">Whether the human has died; a dead human is not an area occupant and reports <c>isDead</c>.</param>
public readonly record struct HumanOccupant(int TaskId, int Team, Vector3d Position, bool IsDead);
