using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>What a title needs in order to turn a loaded level into a drawable scene.</summary>
/// <param name="Mission">The mission, which carries the packs and locates the lightmap archive.</param>
/// <param name="LevelRoot">The level's task tree, which is what places everything.</param>
/// <param name="Resources">The registry the level's own files are read from.</param>
/// <param name="Magic">The magic-object table, for a title that resolves placements through one.</param>
/// <param name="Logger">Where load diagnostics go.</param>
/// <param name="VehiclePhysics">
/// The registry a walk reads a vehicle's mass and contact points from, or null for a title with none.
/// </param>
public readonly record struct WorldBuildRequest(
    LoadedMission Mission,
    GameTask LevelRoot,
    ResourceRegistry Resources,
    MagicObjectRegistry? Magic,
    IIgiLogger Logger,

    // The registry a walk reads a vehicle's mass and contact points from. Nullable because a title
    // without driveable vehicles has none to read, and the walk treats absent as "no vehicle here".
    VehiclePhysicsRegistry? VehiclePhysics = null);

/// <summary>What a title's scene walk produced.</summary>
/// <param name="Scene">The placements, which the world presenter draws and the collision index is built from.</param>
/// <param name="UnresolvedCount">How many placements named something the packs do not hold.</param>
/// <param name="LightmappedCount">How many carry a baked object lightmap.</param>
/// <param name="Ground">
/// The ground this level's own data describes, or <see langword="null"/> for a title whose ground the
/// host already has. Project I.G.I.'s arrives with the mission, as part of what the mission loader
/// opened; the sequel's is inside the level, so the walk that reads the level is what produces it.
/// </param>
/// <param name="Spins">
/// The placements this title turns on the spot, or <see langword="null"/> for a title that turns none.
/// Project I.G.I. has no such thing; I.G.I. 2's <c>RotateAttachment</c> is one.
/// </param>
/// <remarks>
/// The two counts are diagnostics rather than data: a scene walk that silently placed nothing would
/// produce an empty world and no complaint, and these are what let the host say how much of the level it
/// actually found.
/// </remarks>
public readonly record struct WorldBuildResult(
    WorldScene Scene,
    int UnresolvedCount,
    int LightmappedCount,
    IGroundHeightField? Ground = null,
    IPlacementSpin? Spins = null);

/// <summary>Turns a title's loaded level into the shared scene everything downstream consumes.</summary>
/// <remarks>
/// <para>
/// <b>Behind a seam because the walk is per title and nothing downstream is.</b>
/// <see cref="WorldScene"/> — a position, an orientation, an instance, a pack, a mesh and a lightmap
/// name — is a shape both games have, and the world renderer, the collision index and the soldiers'
/// ground probe all take it unchanged. How a level <em>becomes</em> that is not shared at all:
/// <see cref="WorldScene.Build"/> knows Project I.G.I.'s task names, its parameter names and even its
/// security-camera model ids, and opens the two packs that game has; the sequel declares different
/// types, resolves models through a table over <b>four</b> packs whose order decides which name wins,
/// and binds its lightmaps by task id.
/// </para>
/// <para>
/// The host called the first game's walk directly, so a second title loaded a mission and got the first
/// game's reading of it — which for I.G.I. 2 means almost nothing placed, quietly. That is the same
/// shape of failure the sky had: everything loads, nothing says a word, and the world is empty.
/// </para>
/// <para>
/// A third lookup in the composition root rather than a member of <c>IGameTitleRuntime</c>, for the
/// reason the presentation provider is a second one: the reference graph forbids joining them, because
/// <c>OpenIGI.Game</c> references <c>OpenIGI.Engine</c> and so <c>Engine</c> cannot name a
/// <see cref="WorldScene"/>. What matters is that all three are resolved in the one place allowed to
/// name either game.
/// </para>
/// </remarks>
public interface ITitleWorldBuilder
{
    /// <summary>Walks a loaded level and places what it declares.</summary>
    /// <param name="request">The mission, the level and what they are read through.</param>
    /// <returns>The scene, and what it could not place.</returns>
    WorldBuildResult Build(in WorldBuildRequest request);
}
