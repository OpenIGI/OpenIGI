using OpenIGI.Presentation.Cameras;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Textures;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Host.Lighting;
using OpenIGI.Host;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Menu;

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
/// <c>GameHost</c> when the result is collected. That is the same handover
/// <see cref="IrradianceProbeBake"/> and <c>DistanceFieldBake</c> already use.
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
/// <param name="probeFields">The shipped and per-user probe caches, read for a mission's baked lighting.</param>
/// <param name="cameras">The authored orbit styles each mission's showcase points are based on.</param>
/// <param name="context">What a built preview needs to be promotable into the played mission.</param>
/// <param name="logger">The logger.</param>
/// <param name="work">
/// Where a backdrop is built: beside the menu where the platform can, and inline -- a stall on the frame
/// that asks -- where it cannot.
/// </param>
public sealed class MissionPreviewLoader(
    string gamePath,
    IMissionCatalogue<MissionDescriptor> missions,
    MagicObjectRegistry? magic,
    GiBakeCache probeFields,
    MenuCameraLibrary cameras,
    MissionPreviewContext context,
    IOpenIgiLogger logger,
    IWorkScheduler work) : IDisposable
{
    private const string LogCategory = "Preview";

    /// <summary>How many places in a mission the backdrop cycles between.</summary>
    /// <remarks>
    /// Three of them are the corners of the triangle <see cref="MissionShowcasePoints"/> spreads across the
    /// level; the remaining two are filled from inside it when the level is big enough to have an inside.
    /// </remarks>
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

            // A result for a mission nobody wants any more is not worth handing over -- nor is the field it
            // may still be baking for a level nobody is going to look at.
            if (_ready is not null && _ready.MissionId != missionId)
            {
                _ready.PendingDistanceField?.Cancel();
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
            _ready?.PendingDistanceField?.Cancel();
            _ready = null;
            _wanted = -1;
        }
    }

    private void Start(int missionId) => _ = work.Run(() =>
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
                built.PendingDistanceField?.Cancel();
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
        // thread.
        //
        // The loop is built the way the SESSION's loop is built — same wall clock, same flow host, same
        // render scheduler — rather than with a throwaway ManualTimeSource, because a backdrop is no
        // longer only looked at. Pressing Play promotes this loop into the game's own, and a loop that
        // measured time from its own origin would make every elapsed reading in the frame loop jump at
        // the moment of the swap. None of the three is called here; the loop's constructor only stores
        // them, and a preview that is never promoted is still never advanced.
        var fileSystem = new VirtualFileSystem(logger);
        fileSystem.RegisterDevice(new LocalFileDevice(gamePath));
        var resources = new ResourceRegistry(fileSystem, logger);

        // Before a single Register call, because registering an archive substitutes its entries on the
        // way in and a store attached afterwards would have missed every one of them. This is also the
        // only place it can be attached at all: a preview's registry is private, and promotion hands it
        // to the game wholesale -- so a registry built without it plays the mission from the game's own
        // archives no matter what is in the folder.
        resources.Overrides = context.Overrides;
        var loop = new GameLoop(
            time: context.TimeSource,
            host: context.Host,
            renderer: context.Scheduler,
            logger: logger,
            structuralTypes: context.StructuralTaskTypes);

        // The archives that belong to the session rather than to this level. Registered from bytes the
        // composition root read at start-up: once promoted, this registry is the one the menus, the
        // mission briefings and the handheld computer resolve their strings through.
        foreach (KeyValuePair<string, byte[]> archive in context.SessionArchives)
        {
            resources.Register(archive.Key, archive.Value);
        }

        // The same content the playing session registers, from the same list rather than from a second
        // copy of it maintained by hand -- which is what this was, and it had already drifted by an
        // entry. loadTables is false because a preview does not play the mission: it needs every type
        // registered so the tree comes out properly typed rather than as untyped placeholders, and it
        // reads the tables the playing session already loaded through `inherited`.
        TitleSession.Start(
            context.TitleModules,
            loop,
            fileSystem,
            resources,
            logger,
            loadTables: false,
            inherited: context.TitleServices);

        // The two natives the composition root installs at boot and a look-only preview never needed.
        // DefineMission's table is already scanned and lives on the shared registry, so this is the hook
        // and nothing more; Game_SetMission likewise. Both write only into this loop's own namespace.
        missions.Register(loop.Natives);
        context.MissionState.Register(loop.Natives);

        var levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        var levelLoader = new LevelLoader(levelRoot, loop.TaskCommands, loop.Natives, resources, logger);
        var terrainLoader = new TerrainLoader(resources, loop.Natives, logger);
        var missionLoader = new MissionLoader(
            missions, fileSystem, resources, levelLoader, logger, terrainLoader, soundBank: null, context.Formats);

        LoadedMission loaded = missionLoader.Load(missionId);

        // The same gate the played mission runs, so a preview does not show the intro and outro actors the
        // original leaves unregistered.
        new ConditionalContainerGate(loop.Variables, logger).Prepare(levelRoot);

        // The ground tiles' derived detail maps, started here and collected at the very end. They need only
        // the terrain, which the load has just produced, so they run alongside everything below rather than
        // after it — and they are per-tile independent, so they also run alongside each OTHER. That matters
        // more than the overlap does: derivation is around three quarters of a backdrop's whole build, and
        // it is the one part of it that divides cleanly.
        // Enhanced and vanilla render with material detail disabled. Deriving maps in those presets is
        // not merely unused work: on the affected machine the eighteen ground tiles held this worker for
        // 211-284 seconds and saturated enough CPU to make the live game appear frozen. Cold mission loads
        // share the same policy through MaterialDetailPack.AttachDerived.
        Task<Bgra32[][]> groundDetail = ShouldDeriveGroundDetail(RenderCVars.MaterialDetail.Value)
            ? DeriveGroundDetailAsync(loaded.Terrain)
            : Task.FromResult<Bgra32[][]>([]);

        // The title's own walk, not the first game's. A promoted backdrop IS the mission, scene and
        // all, so these two cannot be allowed to differ.
        WorldBuildResult built = context.WorldBuilder.Build(
            new WorldBuildRequest(loaded, levelRoot, resources, magic, logger));
        WorldScene scene = built.Scene;
        ObjectCollisionIndex solids = built.Solids ?? ObjectCollisionIndex.Build([]);

        TerrainSurface? surface = loaded.Terrain is { IsUsable: true } usable
            ? TerrainSurface.Build(usable, levelRoot, logger)
            : null;

        // The places the backdrop will circle. Derived from the level rather than authored, so every mission
        // has them and a mod's levels get them too, and derived on the worker because searching a level's
        // placements is arithmetic the render thread has no business doing. A mission with a hand-authored
        // set in menu-cameras.json uses that instead.
        IReadOnlyList<ShowcasePoint> pointsOfInterest =
            !forceDerivedPoints && cameras.PointsFor(missionId, context.TitleId) is { Count: > 0 } authored
                ? authored
                : MissionShowcasePoints.Extract(scene, levelRoot, ShowcasePointCount, cameras.For(missionId, context.TitleId), logger);

        DirectionalLightSettings? sun =
            DirectionalLightSettings.TryRead(levelRoot, out DirectionalLightSettings read) ? read : null;

        // A shipped or previously-written bake is a file read and a parse, both of which belong here. A
        // mission with no bake previews unbounced rather than tracing one: the trace is seconds of CPU for
        // a backdrop, and its result could not be cached from this context anyway.
        Vector3f bakeSun = sun is { IsBelowHorizon: false } lit ? lit.Direction : default;
        IrradianceVolume? probeField = RenderCVars.GlobalIllumination.Value
            ? probeFields.TryLoad(missionId, RenderCVars.GlobalIlluminationSpacing.Value, bakeSun)
            : null;

        // The level's lamps, surveyed here for two reasons that happen to coincide. It is the single
        // largest cost promotion used to pay on the render thread -- decoding every emitter texture ran
        // to about 140 ms -- and it is half of what makes a backdrop look like the mission: a lamplit
        // interior previewed with its lamps out is simply a different, darker picture.
        EmissiveLightProposer.Proposal lamps = RenderCVars.GlobalIlluminationLamps.Value != 0
            ? EmissiveLightProposer.Propose(
                scene, resources, context.Lamps, missionId, context.TitleId, logger)
            : EmissiveLightProposer.Proposal.None;

        // The other half: the distance field the real-time lighting traces against. Cached, it is a file
        // read and belongs here; missed, the bake is started here and carried, so a mission that is
        // browsed for a few seconds before Play has its field ready and one that is not hands the game
        // the bake in flight rather than starting one from nothing on the way in. Single-threaded, so it
        // takes one core and leaves the frame alone. The same gate the render loop applies -- and none of
        // it for a lite backdrop, which draws without a field: the read is skipped and a missing field is
        // baked when Play is pressed instead of for every mission the player scrolls past.
        int fieldResolution = RenderCVars.DistanceFieldResolution.Value;
        SceneDistanceField? distanceField = null;
        DistanceFieldBake? pendingField = null;
        if (RenderCVars.DistanceFields.Value && context.ComputeSupported && !RenderCVars.MenuBackdropLite.Value)
        {
            distanceField = context.DistanceFields.TryLoad(missionId, fieldResolution);
            if (distanceField is null)
            {
                pendingField = DistanceFieldBake.Start(scene.Placements, work, fieldResolution, logger);
                logger.Information(
                    LogCategory,
                    $"Mission {missionId} has no cached distance field; baking one at {fieldResolution} voxels behind the menu.");
            }
        }

        Bgra32[][] detail = groundDetail.GetAwaiter().GetResult();

        // The level's textures, decoded on the pool from here on. Started last, after everything that
        // reads the registry on this thread has finished with it: the cache takes the bytes it needs now
        // and touches the registry no further, so nothing races the render thread's own loads later.
        TextureDecodeCache decoded = TextureDecodeCache.StartFor(
            resources,
            TextureDecodeCache.AlphaTestCutoff,
            work,
            context.DetailMaps,
            TextureDecodeCache.TexturesUsedBy(scene),
            context.BlockCompressionSupported);

        // And finished before the backdrop is handed over: the swap's surface-cache capture wants nearly
        // every texture on the render thread, and one the pool has not reached is decoded there, in the
        // frame. Waited here instead, the menu goes on showing the old backdrop for that time. A decode
        // that failed is not a reason to lose the backdrop -- the renderer decodes that one itself.
        try
        {
            decoded.Completion.GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Warning(LogCategory, $"The backdrop's textures did not all decode ahead: {ex.Message}");
        }

        logger.Information(
            LogCategory,
            $"Built the backdrop for mission {missionId} in {clock.ElapsedMilliseconds} ms off the render thread.");

        return new MissionPreview(
            missionId,
            loaded,
            loop,
            levelRoot,
            scene,
            solids,
            surface,
            detail,
            pointsOfInterest,
            sun,
            probeField,
            resources,
            fileSystem,
            lamps,
            distanceField,
            pendingField,
            fieldResolution,
            decoded,
            built.Ground,
            built.Spins);
    }

    /// <summary>Gets whether the selected render preset can consume derived ground detail.</summary>
    /// <param name="materialDetail">The current <c>r_material_detail</c> strength.</param>
    /// <returns><see langword="true"/> only when deriving and uploading the maps can affect the picture.</returns>
    internal static bool ShouldDeriveGroundDetail(float materialDetail) => materialDetail > 0f;

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
    private Task<Bgra32[][]> DeriveGroundDetailAsync(TerrainData? terrain)
    {
        if (terrain is not { IsUsable: true } usable || usable.Tiles is not { } atlas || atlas.Tiles.Count == 0)
        {
            return Task.FromResult<Bgra32[][]>([]);
        }

        return work.Run(() =>
        {
            var derived = new Bgra32[atlas.Tiles.Count][];

            work.ForRange(0, derived.Length, Math.Max(1, Environment.ProcessorCount - 1), i =>
            {
                TextureImage tile = atlas.Tiles[i];
                derived[i] = MaterialDetailPack.Derive(tile.GetLevel(0), tile.Width, tile.Height);
            });

            return derived;
        });
    }
}
