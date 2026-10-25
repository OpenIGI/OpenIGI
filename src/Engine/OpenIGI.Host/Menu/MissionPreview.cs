using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation;
using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Host.Lighting;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Menu;

/// <summary>
/// A mission built for the menu backdrop, everything in it produced away from the graphics device and
/// away from the running game, ready for the render thread to turn into renderers.
/// </summary>
/// <remarks>
/// <para>
/// The point of this type is what it does <b>not</b> hold: no <see cref="TextureHandle"/>, no renderer,
/// nothing owned by the GL context. Every field is plain data or a task tree, so all of it can be built on
/// a worker; the handover on the render thread is then only the uploads, which is the part that genuinely
/// has to happen there.
/// </para>
/// <para>
/// It carries its own <see cref="Resources"/> and <see cref="FileSystem"/> because that is what made the
/// load safe to run in the background at all — see <see cref="MissionPreviewLoader"/> — and because a
/// <see cref="IWorldPresenter"/> reads models and textures through the registry it was given for as long as
/// it lives. Dropping the preview drops the whole context with it.
/// </para>
/// </remarks>
/// <param name="MissionId">The mission this was built for.</param>
/// <param name="Mission">The loaded mission: its descriptor, packs and terrain.</param>
/// <param name="Loop">
/// The private loop the level was built into: its task tree, its script namespace, its variables. Held
/// because a preview is not only looked at — pressing Play <em>promotes</em> the backdrop into the played
/// mission, and promotion is this loop becoming the game's own rather than the level being loaded a second
/// time. A preview that is never promoted simply never has it advanced, which is what it was before.
/// </param>
/// <param name="LevelRoot">The level's task tree, in the preview's own private tree.</param>
/// <param name="Scene">The placements the world renderer draws.</param>
/// <param name="Spins">
/// The placements this title turns on the spot, or null when none do. Carried through the backdrop
/// because a promoted mission IS the backdrop, and a fan that only turns when the mission was entered
/// from the command line is the kind of difference nobody sees until they play from the menu.
/// </param>
/// <param name="Solids">The collision index built from those placements.</param>
/// <param name="TerrainSurface">The terrain's material surface, or <see langword="null"/> for a level with no usable terrain.</param>
/// <param name="GroundDetail">
/// A packed detail map per ground tile, in tile order, derived on the worker. The single most expensive
/// thing a backdrop swap used to do on the render thread; here it is already done and only wants uploading.
/// </param>
/// <param name="PointsOfInterest">
/// The places in the level worth circling behind the menu, the player's spawn first. Derived on the worker
/// from where the level put its geometry — see <see cref="MissionShowcasePoints"/>.
/// </param>
/// <param name="Sun">The level's directional light, or <see langword="null"/> when it declares none.</param>
/// <param name="ProbeField">A shipped or cached probe field, or <see langword="null"/> when the mission has no bake.</param>
/// <param name="Resources">The registry the preview's packs were opened into; the world renderer reads through it.</param>
/// <param name="FileSystem">The file system the preview's <c>MISSION:</c> alias is bound in.</param>
/// <param name="Lamps">
/// The level's proposed fixtures and glowing geometry, surveyed on the worker. What used to be the single
/// largest cost of promoting a backdrop (around 140 ms of texture decoding on the render thread), and
/// also what a backdrop needs in order to be lit the way the mission is: a lamplit room previewed with its
/// lamps off is a different picture.
/// </param>
/// <param name="DistanceField">
/// The level's cached distance field, read on the worker, or <see langword="null"/> when the cache missed
/// or the field is not wanted. Uploading it is the render thread's; reading it is not.
/// </param>
/// <param name="PendingDistanceField">
/// A bake started on the worker when the cache missed, or <see langword="null"/>. The render loop collects
/// it exactly as it collects a bake started by a played mission, so a backdrop that was browsed long enough
/// hands the mission a finished field, and one that was not hands it the bake in flight.
/// </param>
/// <param name="DistanceFieldResolution">The voxel resolution the field was read or is being baked at.</param>
/// <param name="Ground">
/// The ground the level's own data describes, when the title's walk produced one. Project I.G.I.'s comes
/// with the mission instead, so this is null there and the host falls back to it.
/// </param>
/// <param name="Visibility">
/// The title's view culling over the scene, when its walk produced one. Carried for the same reason as
/// <paramref name="Spins"/>: a promoted mission keeps the backdrop's renderer.
/// </param>
/// <param name="Decoded">
/// The level's textures, decoded on the thread pool while the rest of this was being built. The render
/// thread then only uploads them — see <see cref="TextureDecodeCache"/>.
/// </param>
public sealed record MissionPreview(
    int MissionId,
    LoadedMission Mission,
    GameLoop Loop,
    LevelTask LevelRoot,
    WorldScene Scene,
    ObjectCollisionIndex Solids,
    TerrainSurface? TerrainSurface,
    IReadOnlyList<Bgra32[]> GroundDetail,
    IReadOnlyList<ShowcasePoint> PointsOfInterest,
    DirectionalLightSettings? Sun,
    IrradianceVolume? ProbeField,
    ResourceRegistry Resources,
    VirtualFileSystem FileSystem,
    EmissiveLightProposer.Proposal Lamps,
    SceneDistanceField? DistanceField,
    DistanceFieldBake? PendingDistanceField,
    int DistanceFieldResolution,
    TextureDecodeCache? Decoded,
    IGroundHeightField? Ground = null,
    IPlacementSpin? Spins = null,
    IPlacementVisibility? Visibility = null);
