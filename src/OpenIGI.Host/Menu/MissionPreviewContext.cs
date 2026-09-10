using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.World;
using OpenIGI.Host.Lighting;

namespace OpenIGI.Host.Menu;

/// <summary>
/// The pieces of the running session a menu backdrop needs in order to be <em>promotable</em> — that is,
/// to become the played mission rather than be thrown away when the player presses Play.
/// </summary>
/// <remarks>
/// <para>
/// A preview builds the whole level into its own private <see cref="GameLoop"/>, and that privacy is what
/// makes it safe to build on a worker thread. The consequence is that the level is built but not
/// <em>playable</em>: nothing in that tree would ever tick, because the game advances a different loop.
/// Promotion is the game changing which loop it advances, and for that to be possible the preview's loop
/// has to have been built the way the session's own was.
/// </para>
/// <para>
/// So this carries the three things <see cref="GameLoop"/>'s constructor takes that a look-only preview
/// never needed — the wall clock, the flow host and the render scheduler — plus the archives that belong
/// to the session rather than to any one level. Every one of them is either immutable or merely stored by
/// the loop's constructor, so handing them to a worker makes no call on any of them.
/// </para>
/// </remarks>
/// <param name="TimeSource">
/// The session's one wall clock. Both loops must read milliseconds from the same origin or every elapsed
/// time in the frame loop jumps at the moment of the swap — see <see cref="GameClock.AdoptScheduleFrom"/>.
/// </param>
/// <param name="Host">The flow host the loop reports state transitions to; stored, never called, on a preview.</param>
/// <param name="Scheduler">The render scheduler the loop presents through; likewise only stored until promotion.</param>
/// <param name="MissionState">
/// The shared <c>Game_SetMission</c> state. Its native has to exist in the promoted loop's namespace, or a
/// level script that reaches for it after promotion would fault against a namespace that never had it.
/// </param>
/// <param name="SessionArchives">
/// The string archives read once at start-up — the menu's labels, the mission names and briefings, the
/// handheld computer's three tables — as bytes. A promoted registry has to carry them because it becomes
/// the registry everything resolves through, and reading them out of the session's own registry on a
/// worker would be exactly the shared-state race the private context exists to avoid.
/// </param>
/// <param name="Overrides">
/// The installation's overrides folder — the store itself, which reports an empty index when there is no
/// such folder, rather than a null standing for one. A preview builds
/// the level into its own registry, and that registry becomes the game's own the moment Play is pressed
/// — so a replacement pack that is not attached HERE is a folder that replaces nothing, however
/// correctly it was built. The store is shared rather than copied: it is the session's one index of what
/// is on disk, and a rescan from the console has to reach the registry the played mission resolves
/// through.
/// </param>
/// <param name="Lamps">
/// The lamps a person named by hand, read once at start-up. A backdrop proposes its level's fixtures on
/// the worker so the menu is lit the way the played mission will be, and the manifest is what the
/// proposal is checked against.
/// </param>
/// <param name="DistanceFields">
/// The shipped and per-user distance-field caches. Read on the worker, so a cached field arrives with
/// the backdrop rather than being loaded on the render thread after it; never written from there.
/// </param>
/// <param name="DetailMaps">
/// The session's derived detail-map pack. A backdrop's textures are decoded on a worker, and their maps
/// are read there too rather than on the frame that uploads them; the pack is shared across missions, so
/// this is the session's own and not the preview's.
/// </param>
/// <param name="TitleServices">
/// The tables the playing session's content modules built. A preview registers the same modules into
/// its own loop — the natives are per-loop and must be — but the tables are the session's, so a
/// backdrop reads the magic-object table that is already loaded rather than a second, empty one.
/// </param>
/// <param name="StructuralTaskTypes">
/// The task types this title's editor emits no declaration for, so the loop registers them itself. It is
/// the title's set and not a fixed one: what makes a type structural is that the title's own editor does
/// not declare it, and the two games disagree by exactly one name — I.G.I. 2's editor declares
/// <c>Container</c> with a parameter, and a loop that had registered it with none refuses every level
/// that mentions it.
/// </param>
/// <param name="TitleModules">
/// The running title's content modules. A preview registers the same ones into its own loop, so its
/// tree comes out shaped the way the scene walk expects rather than as untyped placeholders.
/// </param>
/// <param name="ComputeSupported">
/// Whether the device can run compute at all. A distance field is only ever traced against on a device
/// that can, so a backdrop built for one that cannot should not spend a worker baking one.
/// </param>
public sealed record MissionPreviewContext(
    ITimeSource TimeSource,
    IFlowHost Host,
    IRenderScheduler Scheduler,
    GameMissionState MissionState,
    OpenIGI.Rendering.MaterialDetailPack? DetailMaps,
    IReadOnlyDictionary<string, byte[]> SessionArchives,
    ResourceOverrides Overrides,
    LampManifest Lamps,
    DistanceFieldCache DistanceFields,
    IServiceCatalog TitleServices,
    IReadOnlyList<IContentModule> TitleModules,
    IReadOnlyList<string> StructuralTaskTypes,
    bool ComputeSupported);
