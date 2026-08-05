using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

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
/// <see cref="WorldRenderer"/> reads models and textures through the registry it was given for as long as
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
/// <param name="Resources">The registry the preview's packs were opened into; the world renderer reads through it.</param>
/// <param name="FileSystem">The file system the preview's <c>MISSION:</c> alias is bound in.</param>
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
    ResourceRegistry Resources,
    VirtualFileSystem FileSystem);
