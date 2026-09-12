using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Time;
using OpenIGI.Game.World;

namespace OpenIGI.Desktop;

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
    LampManifest Lamps,
    DistanceFieldCache DistanceFields,
    bool ComputeSupported);
