using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Textures;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>
/// Builds the menu's backdrop mission on a worker thread and hands the result to the render thread when
/// it is ready, so browsing the campaign never stalls the frame.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a level load could not simply be moved onto a thread.</b> Loading a mission rebinds the file
/// system's <c>MISSION:</c> alias, registers the level's packs into a resource registry, and builds a task
/// tree by running scripts against a namespace of natives and variables. In the game those three are one
/// each, shared, and the render thread is reading all of them — ticking the live level's tasks, resolving
/// its models — every frame. A worker doing the same work would be rebinding an alias and mutating a tree
/// out from under it. That is the reason <see cref="MissionWarmer"/> only ever pulled the files into the
/// operating system's cache: reading a file races nothing.
/// </para>
/// <para>
/// <b>What makes it safe here.</b> Each preview load gets its <em>own</em> file system, resource registry
/// and task tree — a private <see cref="GameLoop"/> whose only purpose is to own a
/// <see cref="TaskTree"/> and a namespace, and which is never advanced. Nothing the worker writes is
/// visible to the running game, so there is nothing to race. What it does share is read-only and
/// finished being written at start-up: the mission table and the magic-object table are dictionaries
/// nothing mutates after the initial scan, and the probe cache is read, not written. A backdrop needs no
/// ticking anyway — it is static geometry under a slow orbit — so the private tree is simply never run.
/// </para>
/// <para>
/// <b>What is left on the render thread.</b> Only the uploads: a GL texture belongs to the context that
/// made it, so the terrain's tiles, the sky and the world renderer are built in
/// <c>DesktopGame</c> when the result is collected.
/// </para>
/// <para>
/// A request supersedes whatever was in flight: the worker's result carries the mission it was for, and a
/// result nobody is waiting for any more is dropped rather than swapped in. There is no way to interrupt a
/// load part-way, so a superseded one is left to finish and discarded — it costs a worker thread for a
/// moment and never touches the game.
/// </para>
/// </remarks>
/// <param name="gamePath">The installation to read from.</param>
/// <param name="missions">The mission table, complete and read-only after the start-up scan.</param>
/// <param name="magic">The magic-object table, likewise read-only after start-up.</param>
/// <param name="cameras">The authored orbit styles each mission's showcase points are based on.</param>
/// <param name="logger">The logger.</param>
public sealed class MissionPreviewLoader(
    string gamePath,
    MissionRegistry missions,
    MagicObjectRegistry magic,
    MenuCameraLibrary cameras,
    IIgiLogger logger) : IDisposable
{
    private const string LogCategory = "Preview";

    /// <summary>How many places in a mission the backdrop cycles between.</summary>
    private const int ShowcasePointCount = 5;

    private readonly Lock _gate = new();
    private MissionPreview? _ready;
    private int _wanted = -1;
    private int _building = -1;
    private bool _disposed;

    /// <summary>Gets a value indicating whether a built preview is waiting to be collected.</summary>
    /// <remarks>
    /// What the cross-fade watches. The picture only goes down once there is something ready to replace it,
    /// so the fade is never left holding on a load that has not finished — which is the difference between
    /// a cut and a stall.
    /// </remarks>
    public bool HasResult
    {
        get
        {
            lock (_gate)
            {
                return _ready is not null;
            }
        }
    }

    /// <summary>Asks for a mission to be built, superseding any request that has not been collected.</summary>
    /// <param name="missionId">The mission to build.</param>
    public void Request(int missionId)
    {
        lock (_gate)
        {
            if (_disposed || _wanted == missionId)
            {
                return;
            }

            _wanted = missionId;

            // A result for a mission nobody wants any more is not worth handing over.
            if (_ready is not null && _ready.MissionId != missionId)
            {
                _ready = null;
            }

            if (_building >= 0)
            {
                return; // the worker is busy; it will pick the newest request up when it finishes
            }

            _building = missionId;
        }

        Start(missionId);
    }

    /// <summary>Builds a mission on the calling thread and returns it, for tooling rather than for play.</summary>
    /// <param name="missionId">The mission to build.</param>
    /// <returns>The built mission.</returns>
    /// <remarks>
    /// The same work <see cref="Request"/> does on a worker, run to completion in the caller's own time.
    /// It exists for <c>-poidump</c>, which walks every mission and writes out the places it found; a
    /// backdrop must never use it, because the point of this class is that the render thread does not wait.
    /// </remarks>
    public MissionPreview BuildForTooling(int missionId) => Build(missionId, forceDerivedPoints: true);

    /// <summary>Takes a finished preview, if one is waiting. Call from the render thread.</summary>
    /// <param name="preview">Receives the preview, or <see langword="null"/> when none is ready.</param>
    /// <returns><see langword="true"/> when a preview was collected.</returns>
    public bool TryCollect(out MissionPreview? preview)
    {
        lock (_gate)
        {
            preview = _ready;
            if (preview is null)
            {
                return false;
            }

            _ready = null;
            return true;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _ready = null;
            _wanted = -1;
        }
    }

    private void Start(int missionId) => Task.Run(() =>
    {
        MissionPreview? built = null;
        try
        {
            built = Build(missionId);
        }
        catch (Exception ex) when (ex is EngineFatalException or IOException or InvalidDataException)
        {
            // A backdrop that cannot be built is not a reason to take the menu down with it; the shell
            // keeps showing whatever level is already up.
            logger.Warning(LogCategory, $"Mission {missionId} could not be previewed: {ex.Message}");
        }

        int next;
        lock (_gate)
        {
            _building = -1;
            if (_disposed)
            {
                return;
            }

            // Only publish what is still wanted; if the selection moved on while this was building, the
            // work is dropped and the newest request is started instead.
            if (built is not null && _wanted == missionId)
            {
                _ready = built;
            }
            else if (built is not null)
            {
                logger.Debug(LogCategory, $"Mission {missionId}'s backdrop finished after the selection moved on; dropped.");
            }

            next = _wanted != missionId && _wanted >= 0 ? _wanted : -1;
            if (next >= 0)
            {
                _building = next;
            }
        }

        if (next >= 0)
        {
            Start(next);
        }
    });

    /// <param name="missionId">The mission to build.</param>
    /// <param name="forceDerivedPoints">
    /// Ignore any hand-authored showcase points and derive them from the level. What <c>-poidump</c> needs:
    /// without it the tool reads back the file it wrote last time and republishes it unchanged, so a change
    /// to how the places are found would never show up in its output.
    /// </param>
    private MissionPreview Build(int missionId, bool forceDerivedPoints = false)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();

        // The private context. Its own alias table, its own resource registry, its own task tree: none of
        // it is reachable from the running game, which is the whole reason this can run off the render
        // thread. The loop is a container for the tree and the namespace and is never advanced.
        var fileSystem = new VirtualFileSystem(logger);
        fileSystem.RegisterDevice(new LocalFileDevice(gamePath));
        var resources = new ResourceRegistry(fileSystem, logger);
        var loop = new GameLoop(time: new ManualTimeSource(), logger: logger);

        // The same task types the composition root registers, so the tree comes out shaped the way the
        // scene walk expects rather than as untyped declared tasks.
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        HumanAiConfigRegistry.RegisterTypes(loop.Types);
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        magic.Register(loop.Natives);

        var levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        var levelLoader = new LevelLoader(levelRoot, loop.TaskCommands, loop.Natives, resources, logger);
        var terrainLoader = new TerrainLoader(resources, loop.Natives, logger);
        var missionLoader = new MissionLoader(missions, fileSystem, resources, levelLoader, logger, terrainLoader);

        LoadedMission loaded = missionLoader.Load(missionId);

        // The same gate the played mission runs, so a preview does not show the intro and outro actors the
        // original leaves unregistered.
        new ConditionalContainerGate(loop.Variables, logger).Prepare(levelRoot);

        // The ground tiles' derived detail maps, started here and collected at the very end. They need only
        // the terrain, which the load has just produced, so they run alongside everything below rather than
        // after it — and they are per-tile independent, so they also run alongside each OTHER. That matters
        // more than the overlap does: derivation is around three quarters of a backdrop's whole build, and
        // it is the one part of it that divides cleanly.
        Task<Bgra32[][]> groundDetail = DeriveGroundDetailAsync(loaded.Terrain);

        WorldScene scene = WorldScene.Build(loaded, levelRoot, magic, logger);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);

        TerrainSurface? surface = loaded.Terrain is { IsUsable: true } usable
            ? TerrainSurface.Build(usable, levelRoot, logger)
            : null;

        // The places the backdrop will circle. Derived from the level rather than authored, so every mission
        // has them and a mod's levels get them too, and derived on the worker because searching a level's
        // placements is arithmetic the render thread has no business doing. A mission with a hand-authored
        // set in menu-cameras.json uses that instead.
        IReadOnlyList<ShowcasePoint> pointsOfInterest =
            !forceDerivedPoints && cameras.PointsFor(missionId) is { Count: > 0 } authored
                ? authored
                : MissionShowcasePoints.Extract(scene, levelRoot, ShowcasePointCount, cameras.For(missionId), logger);

        DirectionalLightSettings? sun =
            DirectionalLightSettings.TryRead(levelRoot, out DirectionalLightSettings read) ? read : null;

        Bgra32[][] detail = groundDetail.GetAwaiter().GetResult();

        logger.Information(
            LogCategory,
            $"Built the backdrop for mission {missionId} in {clock.ElapsedMilliseconds} ms off the render thread.");

        return new MissionPreview(
            missionId,
            loaded,
            levelRoot,
            scene,
            solids,
            surface,
            detail,
            pointsOfInterest,
            sun,
            resources,
            fileSystem);
    }

    /// <summary>Derives every ground tile's detail map, the tiles in parallel with one another.</summary>
    /// <param name="terrain">The level's terrain, or <see langword="null"/> when it has none to speak of.</param>
    /// <returns>A packed detail map per tile in tile order, or an empty set when there are no tiles.</returns>
    /// <remarks>
    /// <para>
    /// A ground tile has no name to look a shipped map up by, so one is derived from its pixels — and that
    /// derivation is the better part of a second across a level's tiles, far and away the largest single
    /// cost in building a backdrop. Each tile is derived from nothing but its own pixels into its own
    /// array, so there is no shared state at all and the work divides perfectly.
    /// </para>
    /// <para>
    /// One core is deliberately left free. The whole point of this class is that a backdrop is built
    /// without the game stuttering, and taking every core to do it would cost the render thread exactly
    /// what moving the work off it was meant to save.
    /// </para>
    /// </remarks>
    private static Task<Bgra32[][]> DeriveGroundDetailAsync(TerrainData? terrain)
    {
        if (terrain is not { IsUsable: true } usable || usable.Tiles is not { } atlas || atlas.Tiles.Count == 0)
        {
            return Task.FromResult<Bgra32[][]>([]);
        }

        return Task.Run(() =>
        {
            var derived = new Bgra32[atlas.Tiles.Count][];
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
            };

            Parallel.For(0, derived.Length, options, i =>
            {
                TextureImage tile = atlas.Tiles[i];
                derived[i] = MaterialDetailPack.Derive(tile.GetLevel(0), tile.Width, tile.Height);
            });

            return derived;
        });
    }
}
