using OpenIGI.Game.Hosting;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Level;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Host.Lighting;
using OpenIGI.Host.Menu;
using OpenIGI.Host.Services;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Host.Audio;
using OpenIGI.Host.Configuration;
using System.Diagnostics;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // The mission being played: which, its level tree, and how far it has got to being decided.
    private readonly MissionSession _session;

    /// <summary>Test-only outro forcing writes re-applied on every logic tick.</summary>
    private List<(string Name, string Value)> _testOutroTickWrites = [];
    private void StartMission(int id)
    {
        if (!_missions.TryGet(id, out MissionDescriptor? chosen))
        {
            _logger.Warning("Mission", $"Asked to start mission {id}, which is not registered.");
            return;
        }

        // The hold starts before anything blocks, and so does the panel. Everything below this line
        // runs on the render thread without presenting, so without a frame pushed out here the window
        // simply freezes on whatever it was last showing for the half second it takes -- and the warm-up
        // panel cannot cover that, being drawn by the loop this is blocking. One frame now, holding the
        // picture the player was looking at, and the panel is already up when the load blocks.
        BeginMissionWarmUp(chosen);
        PresentLoadingFrame();

        ReleasePlayedMission();

        _logger.Information("Mission", $"Starting {chosen}.");
        LevelTask levelRoot = (LevelTask)_loop.Tasks.New(_loop.Tasks.Root, _loop.Types.Get(EngineTaskTypes.Level));
        _session.Begin(chosen, levelRoot);

        // The menu's lite profile ends here, before anything below reads the variables it overrides.
        UpdateBackdropProfile();
        _computerOpen = false;
        _computerFlight.Reset();

        // The marker belongs to a task in the level being torn down; carrying its fade into the next
        // one would project a dead anchor for a frame.
        _interactionPrompt.Reset();
        LevelLoader levelLoader = new(levelRoot, _loop.TaskCommands, _loop.Natives, _resources, _logger);
        TerrainLoader terrainLoader = new(_resources, _loop.Natives, _logger);

        // The bank is rebuilt per mission, so the mission's own sounds mount into one holding only the
        // menu's. MISSION: has already been re-pointed by the loader below, in the same step order the
        // original uses.
        ResetSoundsForMission();

        // The resource registry may have arrived from a promoted preview, but audio and other session
        // services retain the file system created at startup. Both aliases must move before the loader
        // reads through either side.
        if (!ReferenceEquals(_resourceFileSystem, _fileSystem))
        {
            _fileSystem.SetAlias(VirtualFileSystem.MissionAlias, chosen.MissionAliasTarget);
        }
        // The title's format profile, without which every pack is read as the first game's and a title
        // shipping a later mesh version has every model in it refused on the way in -- the pack still
        // opens, still reports a name, and holds nothing.
        MissionLoader missionLoader = new(
            _missions, _resourceFileSystem, _resources, levelLoader, _logger, terrainLoader, _audioOut.Bank,
            _titleRuntime.Formats);
        LoadedMission loaded = missionLoader.Load(id);

        // Evaluate the level's ConditionalContainer conditions against the initial playable state, so the
        // scene, spawn and soldier walks below skip the subtrees the original leaves unregistered (the
        // intro/outro cut-scene props and actors). Runs after the tree is built and its variables exported,
        // and before anything walks it.
        new ConditionalContainerGate(_loop.Variables, _logger).Prepare(levelRoot);

        BringMissionUp(loaded, levelRoot, id, promoted: false);
    }

    /// <summary>Applies what a title reads off a level once and the renderer keeps for the whole of it.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <remarks>
    /// <para>
    /// Two title seams, both constants of the level rather than of the frame. The AIR: I.G.I. 2 reads
    /// a fog colour, a fog density and a flat ambient off its <c>GlobalLight</c> and fogs every surface
    /// against them; Project I.G.I. authors none of this and answers nothing, which leaves its picture
    /// exactly as it was (<see cref="IGraphicsDevice.SetFog"/>). The OBJECT SHADING: the sequel's
    /// bakes are <c>MODULATE2X</c> and its unlightmapped objects are lit by a fixed-function sun read off
    /// the level; the other title keeps <see cref="ObjectShading.Default"/>, the compositing it always
    /// had. The gain and the sun's direction are the world renderer's, per draw; the sun's colours are
    /// the device's, baked in beside the fog.
    /// </para>
    /// <para>
    /// Called from BOTH ways a level comes up: <see cref="AdoptPreview"/>, where the menu builds it as a
    /// backdrop, and the cold path of <see cref="BringMissionUp"/>. It used to live in the latter's
    /// not-promoted block alone, so a mission entered through the menu -- the way the game is played --
    /// had no haze, white tanks and unlit objects, while one launched straight to had all three.
    /// </para>
    /// </remarks>
    private void ApplyLevelConstants(GameTask levelRoot)
    {
        SceneAtmosphere? air = _presentation.ReadAtmosphere(levelRoot);
        _atmosphere = air;
        // Through the SAME SceneFog every world draw already wears, rather than a mechanism of its own.
        // Two were built independently -- this game's air as pipeline specialization constants, and
        // Project I.G.I.'s retail fog as device state with a per-vertex factor. The second is strictly
        // the more capable: it changes per draw without a pipeline variant, and SceneFog.ColourFor
        // already knows an additive pass fogs to black.
        _device.SetFog(air is { } authored
            ? new SceneFog(authored.FogColour, authored.FogDensity)
            : SceneFog.None);
        if (air is { } reported)
        {
            _logger.Information(
                "Mission",
                $"The level's air: fog {reported.FogColour} at {reported.FogDensity:G4} per metre, "
                    + $"ambient {reported.Ambient}.");
        }

        // How the level drives the dynamic renderer, for a title whose sun is a lighting direction
        // rather than a position in the sky, or for a level whose night only its sky dome says. Project
        // I.G.I. answers nothing for its daylit levels, which keep the sweep as it was.
        _deferredLighting = _presentation.ReadDeferredLighting(levelRoot);
        if (_deferredLighting is { } lit)
        {
            _logger.Information(
                "Mission",
                $"The level's own light: ambient {lit.Ambient}, nightfall {lit.Nightfall:F2} "
                    + "(its sun is authored, so the day/night sweep is off).");
        }

        ObjectShading shading = _presentation.ReadObjectShading(levelRoot) ?? ObjectShading.Default;
        if (_world is { } shadedWorld)
        {
            shadedWorld.Shading = shading;
        }

        _device.SetSceneSun(shading.Sun?.Ambient ?? Vector3f.Zero, shading.Sun?.Colour ?? Vector3f.Zero);
        if (shading.Sun is { } fixedFunctionSun)
        {
            _logger.Information(
                "Light",
                $"Objects without a bake are lit by the level's sun: ambient {fixedFunctionSun.Ambient}, "
                    + $"colour {fixedFunctionSun.Colour}, towards {fixedFunctionSun.Direction}; "
                    + FormattableString.Invariant($"bakes are multiplied by {shading.LightmapGain:G2}."));
        }
    }

    /// <summary>Builds everything a loaded level needs in order to be played, and stands the player in it.</summary>
    /// <param name="loaded">The mission's descriptor, packs and terrain.</param>
    /// <param name="levelRoot">The level's task tree, already loaded and gated.</param>
    /// <param name="id">The mission id, for the colour grade and the log.</param>
    /// <param name="promoted">
    /// <see langword="true"/> when the level arrived as the menu's backdrop and is being promoted rather
    /// than loaded. The backdrop is already drawn — its scene, collision index, terrain, sky, sun, grade
    /// and camera were all built by <see cref="AdoptPreview"/> off the render thread — so the steps that
    /// would rebuild them are skipped and the rest runs exactly as it does for a cold start. The order of
    /// everything else is deliberately untouched between the two paths: the AI's ray cast closes over
    /// <c>_playerTerrain</c>, the interactions have to exist before the brains are animated, and the
    /// weapon rosters have to load before <c>humanplayer.qvm</c> runs (weapons.md section 8).
    /// </param>
    private void BringMissionUp(LoadedMission loaded, LevelTask levelRoot, int id, bool promoted)
    {
        BeginStage();
        LevelUpdateWalker walker = new(levelRoot);
        MissionBuild build = new(walker, _title.Services, loaded, levelRoot, id, promoted);
        LevelSystems<MissionLevelBuild> systems = MissionSystems();
        if (_title.Services.TryGet(out LevelSystemEdits? edits))
        {
            // A mod's systems join the host's by their names (MissionSystemNames), for this session's missions.
            edits.Apply(systems);
        }

        systems.BuildAll(build);
    }

    /// <summary>Makes the host's mission systems, in the order a mission is brought up.</summary>
    /// <returns>A fresh list, which the session's level-system edits may change.</returns>
    private LevelSystems<MissionLevelBuild> MissionSystems() => new LevelSystems<MissionLevelBuild>()
        .Add(HostSystem(MissionSystemNames.Sounds, MissionSounds))
        .Add(HostSystem(MissionSystemNames.Cutscenes, MissionCutscenes))
        .Add(HostSystem(MissionSystemNames.Objectives, MissionObjectives))
        .Add(HostSystem(MissionSystemNames.Computer, MissionComputer))
        .Add(HostSystem(MissionSystemNames.Scene, MissionScene))
        .Add(HostSystem(MissionSystemNames.Soldiers, MissionSoldiers))
        .Add(HostSystem(MissionSystemNames.Interactions, MissionInteractions))
        .Add(HostSystem(MissionSystemNames.Cameras, MissionCameras))
        .Add(HostSystem(MissionSystemNames.NavGraphs, MissionNavGraphs))
        .Add(HostSystem(MissionSystemNames.LightBakes, MissionLightBakes))
        .Add(HostSystem(MissionSystemNames.NavigationAi, MissionNavigationAi))
        .Add(HostSystem(MissionSystemNames.LevelWalk, MissionLevelWalk))
        .Add(HostSystem(MissionSystemNames.Picture, MissionPicture))
        .Add(HostSystem(MissionSystemNames.Player, MissionPlayer))
        .Add(HostSystem(MissionSystemNames.Adornments, MissionAdornments));

    // One of the host's own systems, which reads the host's build; a mod's reads the public one.
    private static LevelSystem<MissionLevelBuild> HostSystem(string name, Action<MissionBuild> build) =>
        new(name, b => build((MissionBuild)b));

    // A system ran before the one it depends on.
    private static InvalidOperationException MissingSystem(string name) =>
        new($"The mission system '{name}' has not run.");

    /// <summary>The mission's terrain, its sounds and its score.</summary>
    private void MissionSounds(MissionBuild b)
    {
        LoadedMission loaded = b.Loaded;
        bool promoted = b.Promoted;

        // The mission's terrain, first thing, because three separate passes below close over it and it
        // used to be assigned a thousand lines after the first of them. A cold start therefore built the
        // security cameras' sight trace and the lightmap bake's occlusion cast against whatever the
        // FIELD still held -- null, since the last mission's unload cleared it -- so a camera saw
        // through a hillside and a bake let the sun through one. Nothing between here and there reads
        // it, and the value depends on nothing but the argument.
        //
        // Terrain stays optional: Mission 14 is entirely inside placed building and tunnel models, whose
        // collision floors are enough to walk on even though its terrain files are not live data.
        _playerTerrain = loaded.Terrain is { IsUsable: true } standable ? standable : null;

        // The mission's sounds and its score. Both routes into a mission come through here -- a cold
        // start and a backdrop being promoted -- and only the cold one passes the mission loader, so
        // the mount has to happen on this side or a promoted mission plays nothing at all.
        //
        // A cold start emptied the bank in StartMission, before the loader mounted into it. A PROMOTED
        // backdrop never went near StartMission, so without this the bank still holds the mission the
        // player last played -- and since the bank keeps the FIRST mount of a name, the mount below
        // cannot replace it. Every level ships its own sounds/game_music.wav, so what leaked through was
        // the previous level's score playing over the new one.
        //
        // Tearing the bank down costs a re-read of the menu archive and the common directory: 15 ms
        // warm, 23 ms cold for all 375 sounds, which is the same bill the cold path already pays and is
        // spent here under the going-in card rather than during play.
        if (promoted)
        {
            ResetSoundsForMission();
        }

        MountMissionSounds(loaded);
        _audioOut.Music?.Play(MusicPlayer.GameMusic);
    }

    /// <summary>The mission's text and the cutscene runtime, with the natives the level's cutscenes call.</summary>
    private void MissionCutscenes(MissionBuild b)
    {
        b.MissionText = _hosted.Setup.BuildCutscenes(b.Loaded, b.LevelRoot, b.Walker, () => b.Objectives);
    }

    /// <summary>The objective runtime, the intro skipped when cinematics are off, and the test captures' script writes.</summary>
    private void MissionObjectives(MissionBuild b)
    {
        _hosted.Setup.BuildObjectives(b.LevelRoot, b.MissionText!, _hosted.Tick.AreaPlayerNow, made => b.Objectives = made);

        // Test-only capture switch: exercise an authored outro through its real QVM gate rather than
        // injecting a scene. OPENIGI_TEST_OUTRO_WRITES is a comma list of script writes, e.g.
        // "EditVariable_199.nValue=1,AreaActivate_5.nActive=1"; the same writes are re-applied on
        // every logic tick while the run lasts, so gates the level scripts reset stay satisfied.
        string? testOutroWrites = Environment.GetEnvironmentVariable("OPENIGI_TEST_OUTRO_WRITES");
        _testOutroTickWrites = TestOutroWrites(testOutroWrites);
        if (_testOutroTickWrites.Count > 0)
        {
            foreach ((string assignmentName, string assignmentValue) in _testOutroTickWrites)
            {
                double parsed = double.Parse(assignmentValue, System.Globalization.CultureInfo.InvariantCulture);
                if (parsed != Math.Floor(parsed))
                {
                    _logger.Warning(
                        "Mission",
                        $"Test capture write {assignmentName}={assignmentValue} is not an integer; "
                            + $"script variables carry ints, so it truncates to {(int)parsed}.");
                }

                _logger.Information("Mission", $"Test capture write: {assignmentName}={assignmentValue}.");
                _loop.Variables.TryWrite(assignmentName, QvmValue.FromInt32((int)parsed));
            }

            CutsceneRuntime outroCutscenes = _mission.Cutscenes!;
            outroCutscenes.RequestBreak();
            outroCutscenes.Tick();
            MissionObjectiveRuntime outroObjectives = _mission.Objectives!;
            outroObjectives.TickLevelState();
            outroObjectives.TickAfterLevelState();
            outroCutscenes.FinishExpressionTick();
            _logger.Information("Mission", "Test capture outro forcing active.");
        }
    }

    /// <summary>The map computer, its highlights, the tactical computer and the comms cards.</summary>
    private void MissionComputer(MissionBuild b)
    {
        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;
        MissionStringResolver missionText = b.MissionText!;

        _computerHighlights = _presentation.Presenters.Create<ComputerHighlightRequest, IComputerHighlightCatalogue>(new ComputerHighlightRequest(levelRoot, _loop.Variables));
        _mapComputer = _presentation.Presenters.Create<MapComputerPresentationRequest, IMapComputerPresenter>(
            new MapComputerPresentationRequest(_device, _resources, _logger, missionText.Get));
        if (_mapComputer is not null && !_mapComputer.Load(_fileSystem))
        {
            _mapComputer.Dispose();
            _mapComputer = null;
        }

        _mapComputerDevice = new MapComputerDeviceRenderer(_device, _resources, _logger);
        if (!_mapComputerDevice.Load(loaded))
        {
            _mapComputerDevice.Dispose();
            _mapComputerDevice = null;
        }

        // Both presentations are built, and r_computer_tactical decides which is drawn — see
        // GameHost.TacticalComputer. The retail pair above needs the installation's own archives; this
        // one needs only the shipped faces, so a mission whose computer.res is missing still has a map.
        _tacticalComputer = _presentation.Presenters.Create<MapComputerPresentationRequest, ITacticalComputerPresenter>(
            new MapComputerPresentationRequest(_device, _resources, _logger, missionText.Get));
        if (_tacticalComputer is not null && !_tacticalComputer.Load())
        {
            _tacticalComputer.Dispose();
            _tacticalComputer = null;
        }

        // The comms cards. Built here rather than beside the HUD below, because the messages belong to the
        // mission rather than to the loadout: a level whose player carries nothing still sends them.
        _messages?.Dispose();
        _messages = new StatusMessageRenderer(_device, _logger);
        if (!_messages.Load())
        {
            _messages.Dispose();
            _messages = null;
        }

        // The walls. Built here because two consumers want it: the player's collision, and the
        // soldiers' ground probe - a guard's declared Position is only where it starts, and without
        // the settle the retail integrator performs, floor-authored spawns bury the body to the waist.
        //
        // A promoted backdrop arrives with all three already built on a worker: the scene walk, the
        // world renderer and the collision index are exactly what AdoptPreview put up to draw the menu
        // over, and they are the same level.
        EndStage("objectives, highlights and the map computer");
    }

    /// <summary>The scene walk, the world renderer and the collision index; the vehicles on it, what can be destroyed, and the weather.</summary>
    private void MissionScene(MissionBuild b)
    {
        if (_mission.Cutscenes is null)
        {
            throw MissingSystem(MissionSystemNames.Cutscenes);
        }

        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;
        int id = b.Id;
        bool promoted = b.Promoted;

        ObjectCollisionIndex solids;
        IWorldPresenter world;
        WorldScene scene;
        if (promoted)
        {
            solids = _mission.Solids ?? throw new InvalidOperationException(
                "A promoted mission must arrive with the backdrop's collision index already built.");
            world = _world ?? throw new InvalidOperationException(
                "A promoted mission must arrive with the backdrop's world renderer already built.");
            scene = _scene ?? throw new InvalidOperationException(
                "A promoted mission must arrive with the backdrop's scene already walked.");
        }
        else
        {
            WorldBuildResult built = _worldBuilder.Build(
                new WorldBuildRequest(loaded, levelRoot, _resources, _mission.MagicObjects, _logger, Services: _title.Services));
            scene = built.Scene;
            _scene = scene;
            _ground = built.Ground;
            _mission.Spins = built.Spins;

            if (built.UnresolvedCount > 0 || built.LightmappedCount > 0)
            {
                _logger.Information(
                    "World",
                    $"{scene.Placements.Count} placement(s), {built.LightmappedCount} lightmapped, "
                        + $"{built.UnresolvedCount} unresolved.");
            }

            // Unlike the thirteen optional passes, a mission cannot load without a world to put in it:
            // the collision index, the destructibles and the cut-scene bindings below all attach to it.
            // A title that has not written one yet can still show a menu, and says so here rather than
            // by a null reference three hundred lines later.
            world = _presentation.Presenters.Create<WorldPresentationRequest, IWorldPresenter>(new WorldPresentationRequest(_device, _resources, _logger))
                ?? throw new InvalidOperationException(
                    "The title supplies no world presenter, so it cannot load a mission yet.");

            world.Materials = _materials;
            world.DetailMaps = _detailMaps;
            world.Lamps = _gi.Lamps;

            // The parts of the level's objects that never stop turning. Set here rather than beside the
            // other registries below because a promoted backdrop skips that block entirely, and it
            // arrives with its own already walked.
            world.Spins = _mission.Spins;
            _world = world;
            solids = ObjectCollisionIndex.Build(
                scene.Placements,
                _titleRuntime.Formats.Collision,
                name => loaded.TryGetModel(name, out MefModel? model) ? model : null);
            _mission.Solids = solids;
            EndStage("the scene walk, the world renderer and the collision index");
        }
        _hosted.Setup.BuildVehicles(loaded, levelRoot, id, scene, solids);
        world.Destructibles = _mission.Destructibles;
        world.Cutscenes = _mission.Cutscenes;

        // Rain is a gameplay effect, not part of the backdrop picture: it is configured fresh for every
        // mission — cold start or promoted — straight from the level's own objects.qvm (a RainEffect task
        // enabled by its Is Rain and Is Active flags), and whatever rain the previous mission left is
        // dropped first, exactly as the reference editor resets its rain at every level load. The same scene's
        // geometry also provides the roofs the rain is suppressed under.
        _rain?.Dispose();
        _rain = new RainRenderer(_device, _logger);
        _rain.ReadParams(levelRoot);
        _snow?.Dispose();
        _snow = new SnowRenderer(_device, _logger);
        _snow.ReadParams(levelRoot);
        _storm?.Dispose();
        _storm = new StormRenderer(_device, _logger);
        BuildShelterField(scene, loaded);
        StartTerrainRaster();

        // The storm falls where the level says rain falls -- it is the same weather, drawn better -- so
        // it inherits the vanilla renderer's reading of the RainEffect task rather than parsing it a
        // second time, and shelters under the same roofs.
        _storm.SetShelter(_shelterField);
        ApplyWeatherSettings();

        b.Scene = scene;
        b.Solids = solids;
        b.World = world;
    }

    /// <summary>The soldiers' meshes and textures.</summary>
    private void MissionSoldiers(MissionBuild b)
    {
        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;
        bool promoted = b.Promoted;
        ObjectCollisionIndex solids = b.Solids!;

        // The soldiers the level places. Each names its own model, skeleton and stand animation
        // (player.md section 15); the sets they index were loaded when the packs opened. They are not
        // WorldScene placements - a human task is a typed task, and the original draws it through the
        // bone path, not the static one.
        // A promoted backdrop has already built these, behind the menu and against the very same level
        // and collision index, so it hands them over rather than uploading a second copy of every
        // soldier's mesh. Taking the reference is also what stops the backdrop disposing them.
        if (promoted && _menuBackdrop.TakeSoldiers() is { } armedSoldiers)
        {
            _soldiers?.Dispose();
            _soldiers = armedSoldiers;
            _mission.AdoptSoldiers(armedSoldiers);
        }
        else
        {
            _soldiers?.Dispose();
            _soldiers = _presentation.Presenters.Create<SoldierPresentationRequest, ISoldierBodies>(
                new SoldierPresentationRequest(_device, _resources, _logger, _mission.MagicObjects, _detailMaps));
            _mission.AdoptSoldiers(_soldiers);
            _soldiers?.Load(
                loaded,
                levelRoot,
                // The walk's ground when the title keeps one in the level, and the mission's terrain
                // otherwise. _ground itself is not settled until the player is stood up below.
                _ground ?? _playerTerrain,
                solids.Caster());
        }

        EndStage("the soldiers' meshes and textures");
    }

    /// <summary>The doors, elevators, pickups and world interactions, and the soldiers' sounds.</summary>
    private void MissionInteractions(MissionBuild b)
    {
        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;
        ObjectCollisionIndex solids = b.Solids!;
        IWorldPresenter world = b.World!;

        _hosted.Audio.ForgetAmbience();
        _hosted.Setup.BuildInteractions(loaded, levelRoot, solids);

        // Never handed over after the move to the host, which left the renderer's interaction-driven
        // transforms dead: a gun's stand did not turn with the gun it holds.
        world.GenericPickups = _mission.GenericPickups;
        world.Interactions = _mission.Interactions;
        world.Doors = _mission.Doors;
        world.Elevators = _mission.Elevators;

        _hosted.Impacts.ConnectSoldiers();

        EndStage("the doors, elevators and world interactions");
    }

    /// <summary>The security cameras, their sight, and the rotating objects.</summary>
    private void MissionCameras(MissionBuild b)
    {
        WorldScene scene = b.Scene!;
        IWorldPresenter world = b.World!;

        _hosted.Audio.ForgetCameras();
        _cutsceneSounds.StopAll();
        b.WorldTrace = _hosted.Setup.BuildCameras(b.LevelRoot, scene, b.Solids!, _playerTerrain);
        world.SecurityCameras = _mission.SecurityCameras;
        _cameraStatusLights?.Dispose();
        _cameraStatusLights = _presentation.Presenters.Create<EffectsPresentationRequest, ICameraStatusLightPresenter>(EffectsRequest());
        if (_cameraStatusLights is not null && !_cameraStatusLights.Load(_fileSystem))
        {
            _cameraStatusLights.Dispose();
            _cameraStatusLights = null;
        }

        world.RotatingObjects = _mission.RotatingObjects;
    }

    /// <summary>The nav graphs and the soldiers' brains, with what their rounds, blasts and alarms do.</summary>
    /// <summary>The level's nav graphs, which the soldiers and the vehicles drive over.</summary>
    private void MissionNavGraphs(MissionBuild b)
    {
        b.Graphs = _hosted.Setup.ReadNavGraphs(b.Loaded, b.LevelRoot, _gamePath);

        EndStage("reading the nav graphs");
    }

    /// <summary>The level's baked lighting: the probe field, the path-traced lightmaps and the distance fields, started.</summary>
    private void MissionLightBakes(MissionBuild b)
    {
        if (_scene is null)
        {
            throw MissingSystem(MissionSystemNames.Scene);
        }

        LevelTask levelRoot = b.LevelRoot;
        int id = b.Id;
        bool promoted = b.Promoted;
        ObjectCollisionIndex solids = b.Solids!;

        // The level's sun has to be known BEFORE the bakes below, not after: the Dirlight was read
        // some hundreds of lines further down (the `if (!promoted)` block), so every probe field and
        // path-traced lightmap was baked with `_sun` still null -- a zero sun direction and a fallback
        // sky -- which is exactly why the baked GI came out dim. Read it here, unless a promoted
        // backdrop already carried one in. The later read stays and simply re-confirms the same value.
        if (_sun is null && DirectionalLightSettings.TryRead(levelRoot, out DirectionalLightSettings bakeSun))
        {
            _sun = bakeSun;
        }

        // The probe field, traced against the level's collision on a WORKER. Inline it blocks for
        // about three seconds in Release and half a minute in Debug, and by this point the window
        // and its GL context are live -- so the process stops answering, the GPU is handed no
        // work for the whole of it, and Windows' watchdog takes the context away. Loading mission
        // 1 from the menu died that way, with exit 0xCFFFFFFF. The main thread carries on loading
        // and presenting; IrradianceProbeBake hands the volume over when it is ready.
        //
        // It traces with every door SHUT, which is two things at once. A door standing open would
        // otherwise be baked as a hole in the wall it belongs to, and the field is static, so the
        // hole would outlast the door -- that is why this used to have to run before the doors
        // did. And the shut cast is the only one that reads no state the game mutates, which is
        // what makes running it alongside a live level safe rather than merely early.
        //
        // A level with no Dirlight has no sun to trace towards, so the field is sky and bounce
        // alone -- which is what such a level looks like anyway.
        // The shared bake inputs -- sun and a glass-passing cast -- are built when EITHER the probe
        // field or the path-traced lightmaps are wanted; each bake below is then guarded on its own
        // cvar, so r_lightmap works at a preset (ultra) that runs no probe field.
        if (RenderCVars.GlobalIllumination.Value || RenderCVars.BakedLightmaps.Value > 0)
        {
            Vector3f bakeSunDirection = _sun is { } declared && !declared.IsBelowHorizon
                ? declared.Direction
                : default;

            WorldRayCast shutObjectCast = solids.CasterWithDoorsShut();

            // A bake ray passes through glass and chain-link the way a line of sight does
            // (GameMaterial.IsSightTransparent), so daylight reaches a room through its windows
            // instead of stopping at the pane -- the very thing that left window rooms as dark as
            // sealed ones. It steps a hair past each see-through face and casts on until it meets
            // something solid or leaves the level. Portal polygons (doorway/window openings) are
            // already skipped by the cast itself. Terrain always blocks -- it reports material Air.
            WorldRayCast seeThroughObjectCast = (a, b, out blockedAt) =>
            {
                Vector3d origin = a;
                for (int pass = 0; pass < 8; pass++)
                {
                    if (!shutObjectCast(origin, b, out WorldRayHit hit))
                    {
                        blockedAt = default;
                        return false;
                    }

                    if (!GameMaterial.IsSightTransparent(hit.Material))
                    {
                        blockedAt = hit;
                        return true;
                    }

                    Vector3d toEnd = new(b.X - hit.Position.X, b.Y - hit.Position.Y, b.Z - hit.Position.Z);
                    double length = Math.Sqrt((toEnd.X * toEnd.X) + (toEnd.Y * toEnd.Y) + (toEnd.Z * toEnd.Z));
                    if (length <= 1e-6)
                    {
                        blockedAt = default;
                        return false;
                    }

                    // A few units past the pane (4/4096 m ~= 1 mm); thicker or layered glass is
                    // cleared by the loop, one see-through face at a time.
                    double scale = 4.0 / length;
                    origin = new Vector3d(
                        hit.Position.X + (toEnd.X * scale),
                        hit.Position.Y + (toEnd.Y * scale),
                        hit.Position.Z + (toEnd.Z * scale));
                }

                blockedAt = default;
                return false;
            };

            WorldRayCast bakeCast = (a, b, out blockedAt) =>
            {
                bool hitObject = seeThroughObjectCast(a, b, out WorldRayHit onObject);
                if (_playerTerrain is { } bakeGround
                    && TerrainSightBlock.TryBlock(bakeGround, a, b, out WorldRayHit onGround)
                    && (!hitObject || onGround.Distance < onObject.Distance))
                {
                    blockedAt = onGround;
                    return true;
                }

                blockedAt = onObject;
                return hitObject;
            };

            // Dev verification for the path-traced lightmapper (phases 1-2): when IGI_BAKELM_DIR is
            // set, enumerate the lightmapped texels, bake direct sun + sky, and dump the largest few
            // pages' normals and radiance so the texel map and the direct pass can be checked by eye.
            // Off unless the env var is set.
            if (Environment.GetEnvironmentVariable("IGI_BAKELM_DIR") is { Length: > 0 } lightmapDir
                && _scene is { } lightmapScene)
            {
                DumpLightmapVerification(lightmapScene, bakeCast, bakeSunDirection, lightmapDir);
            }

            // Path-traced object lightmaps (r_lightmap): re-bake the level's static object lighting
            // -- direct sun, sky and multi-bounce indirect -- and swap the pages in where the shipped
            // .olm ones would be sampled. Cached to disk like the probe field, so only a mission
            // without a bake pays to trace it, and traced on a worker so the load does not stall.
            // The sky is the daytime skylight (r_sky_light), not the dim raw ambient, so sky-lit
            // surfaces read closer to what the artists painted. r_lightmap 2 discards the shipped
            // lightmaps entirely; 1 keeps them where the bake produced nothing.
            if (_scene is { } bakeScene)
            {
                _baked.Start(id, bakeScene, _world, _resources, _sun, bakeCast);
            }
        }

        // The distance fields, on a worker beside the probe bake. Tracing every distinct
        // model is arithmetic and moves off the render thread freely; only the upload cannot,
        // because a GL texture belongs to the context that made it.
        //
        // Started here rather than built here: _gi.EnsureDistanceFields is the same call the render
        // loop makes every frame, so a level begins its bake at load and a player who switches the
        // lighting model on later gets one started for them. See GameHost.RealtimeGi.cs.
        // The outgoing level's cards go first, before the incoming field is adopted. Adopting a
        // field is one of the two moments that can complete the pair and converge the cache, and
        // with the old cards still standing it would converge THOSE against the new level's field
        // -- then latch, so the real convergence a few hundred lines below was skipped and the
        // level opened on the ramp this is all meant to remove. It failed silently, because a
        // converged-looking cache and a stale one differ only in the picture.
        // Not on a promotion: the backdrop already carries this very mission's cards and field
        // -- built or baking behind the menu, which is what lets the menu show the lit level --
        // and discarding them here would throw the field away and bake it again in front of
        // the player.
        if (!promoted)
        {
            _gi.DiscardSurfaceCache();
            _gi.DiscardDistanceFields();
        }

        _gi.EnsureDistanceFields();

        // A capture must not race the worker, for the reason the probe bake records: a
        // reference image has to either contain the field or not, never whichever frame
        // the worker happened to reach.
        if (_options.Lockstep)
        {
            _gi.WaitForFieldBake();
        }

        // Every distinct model's surface is captured into the card atlas, but deliberately NOT
        // here -- even though this is where the geometry first exists. Lighting the cache needs
        // the level's own sun, which is not read until further down this load, and lighting the
        // cards before that gave every page a zero sun direction and a picture of pure sky, which
        // is exactly what the first run produced. _gi.EnsureSurfaceCache captures and lights in one
        // go, so it is called once the sun is known; the outgoing cards were dropped above.

        _baked.Show();
        ApplyColourLookup(_options.StartMission);
        EndStage("starting the light bakes");
    }

    /// <summary>The soldiers' brains, the alarm speakers and the guards' rounds.</summary>
    private void MissionNavigationAi(MissionBuild b)
    {
        if (_scene is null)
        {
            throw MissingSystem(MissionSystemNames.Scene);
        }

        if (!_hosted.Setup.HasSoldiers)
        {
            // A title without IGI1's soldiers has no brains to animate here.
            return;
        }

        Dictionary<int, GraphFile> graphs = b.Graphs ?? throw MissingSystem(MissionSystemNames.NavGraphs);

        // The difficulty every guard reads its combat row at. Faithfully the ACTIVE PLAYER PROFILE's
        // (sub_406F70 takes it from sub_406220()'s first field), so the difficulty the player picked
        // in the menu actually reaches the AI -- which it did not while this was a hardcoded default,
        // leaving every guard on GD_2/Normal whatever the profile said. -difficulty still overrides,
        // for testing a difficulty without editing a profile.
        AiDifficulty effectiveDifficulty = EffectiveDifficulty();
        string difficultySource = _options.Difficulty is null
            ? $"from profile '{_configuration.Active?.Name ?? "(none)"}'."
            : "from -difficulty override.";

        _hosted.Audio.ForgetSirens();
        _hosted.Setup.AnimateSoldiers(b.LevelRoot, b.Scene!, b.WorldTrace!, graphs, effectiveDifficulty, difficultySource);
        b.World!.AlarmSpeakers = _mission.AlarmSpeakers;
    }

    /// <summary>The difficulty every guard reads its combat row at.</summary>
    /// <returns>The <c>-difficulty</c> override, or else the active profile's.</returns>
    private AiDifficulty EffectiveDifficulty() => _options.Difficulty
        ?? (AiDifficulty)Math.Clamp(
            _configuration.Active?.Difficulty ?? (int)AiDifficulty.Normal,
            (int)AiDifficulty.Easy,
            (int)AiDifficulty.Hard);

    /// <summary>The soldiers, the doors, the alarm controls and the player join the level's walk.</summary>
    private void MissionLevelWalk(MissionBuild b)
    {
        LevelTask levelRoot = b.LevelRoot;

        // The soldiers, the doors, the alarm controls and the player update inside the level's walk.
        _hosted.Tick.AttachToLevelWalk(levelRoot);
    }

    /// <summary>The terrain, sky, sun, grade and camera; the lamps and the surface cache; what impact effects trace against; the player's state reset.</summary>
    private void MissionPicture(MissionBuild b)
    {
        if (_scene is null)
        {
            throw MissingSystem(MissionSystemNames.Scene);
        }

        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;
        int id = b.Id;
        bool promoted = b.Promoted;
        WorldScene scene = b.Scene!;

        // Everything from here to the free camera is what makes the level a PICTURE, and a promoted
        // backdrop is already that picture -- AdoptPreview built the terrain (with its ground detail
        // derived on the worker, which is most of the cost), the sky, the shadow pass, the clear colour,
        // the grade and the camera, and the menu has been drawing them for as long as the player has been
        // reading the briefing. Rebuilding them here would throw away the one thing this whole path
        // exists to reuse, and would do it on the render thread.
        if (!promoted)
        {
            // A level whose terrain files are not live data — level 14 is exactly that — draws its objects
            // over nothing rather than following offsets into whatever the geometry blob happens to hold.
            // The surface comes from the level's own TextureModifier tasks, so it needs the loaded tree.
            // A title whose ground it reads itself answers CreateGround; one whose ground the host
            // already built as a TerrainSurface answers CreateTerrain. Asking in that order is what lets
            // the two coexist without either pretending to the other's shape.
            _terrain?.Dispose();
            _terrain = _presentation.Presenters.Create<GroundPresentationRequest, ITerrainPresenter>(
                new GroundPresentationRequest(_device, levelRoot, _resources, _logger));

            _terrain ??= loaded.Terrain is { IsUsable: true } usable
                ? _presentation.Presenters.Create<TerrainPresentationRequest, ITerrainPresenter>(new TerrainPresentationRequest(
                    _device, TerrainSurface.Build(usable, levelRoot, _logger), _logger, _detailMaps, null))
                : null;

            // The sky is script data on the level's task tree; its fog colour is also what the frame
            // clears to, so the horizon meets sky-coloured backing rather than black.
            _sky?.Dispose();
            _sky = _presentation.Presenters.Create<SkyPresentationRequest, ISkyPresenter>(
                new SkyPresentationRequest(_device, levelRoot, _fileSystem, _resources, FarPlane, _logger));
            _shadowPass = new ShadowCasterPass(_device);

            ApplyLevelConstants(levelRoot);

            if (DirectionalLightSettings.TryRead(levelRoot, out DirectionalLightSettings readSun))
            {
                _sun = readSun;
                _cycleSun = DayNightBinding.Bind(readSun, _deferredLighting);

                // At the hour the level's authors placed the sun for, not a fixed one: the cycle lands
                // back on the authored direction only there.
                _dayNight.ResetForLevel(_cycleSun, RenderCVars.TimeOfDay.Value);

                // After the reset, which clears it: a level that authors its own night hands the cycle
                // that night to cross to, instead of the faint one the cycle invents for a level that
                // does not have one. See DayNightCycle.SetNightLighting.
                if (_deferredLighting is { Night: { } boundNight } authoredNight)
                {
                    _dayNight.SetNightLighting(
                        boundNight.SunIsMoon ? readSun.Colour : null,
                        authoredNight.Ambient);
                }
                _logger.Information(
                    "Light",
                    FormattableString.Invariant(
                        $"Sun at {MathF.Asin(Math.Clamp(readSun.Direction.Z, -1f, 1f)) * 180f / MathF.PI:F1} degrees ")
                    + FormattableString.Invariant(
                        $"elevation, time {readSun.TimeOfDay:F0}, colour ({readSun.Colour.X:F2}, {readSun.Colour.Y:F2}, {readSun.Colour.Z:F2}), ")
                    + FormattableString.Invariant(
                        $"ambient ({readSun.Ambient.X:F2}, {readSun.Ambient.Y:F2}, {readSun.Ambient.Z:F2})."));

                if (readSun.IsBelowHorizon)
                {
                    _logger.Warning("Light", "This level's authored sun is below the horizon; see rendering.md section 12.");
                }
            }
            else
            {
                _cycleSun = null;
                _dayNight.ResetForLevel(null, RenderCVars.TimeOfDay.Value);
                _logger.Information("Light", "The level declares no Dirlight; the modern path will light it with a default sun.");
            }

            // A title may decline a sky entirely -- the factory returns null when the level declares
            // none -- so the clear colour keeps whatever it had rather than reading off nothing.
            if (_sky is { HasSky: true } sky)
            {
                _scheduler.ClearColour = new Bgra32(
                    (byte)Math.Clamp((int)(sky.FogColour.Z * 255f), 0, 255),
                    (byte)Math.Clamp((int)(sky.FogColour.Y * 255f), 0, 255),
                    (byte)Math.Clamp((int)(sky.FogColour.X * 255f), 0, 255),
                    255);
            }

            // The mission's own grade. It used to be applied with `_options.StartMission` from inside the
            // nav-graph block, which was wrong twice over: -mission happens to set that option so the grade
            // looked right under the flag that was being tested with, but a mission started from the shell
            // leaves it 0 and got mission 0's grade, and a level shipping no nav graph got no grade at all.
            // The backdrop always applied the right one (AdoptPreview passes the preview's id), so the two
            // disagreed and the picture changed colour the instant Play was pressed.
            ApplyColourLookup(id);

            // Open on the middle of what was placed, backed off and looking down slightly, so the first
            // frame shows the level rather than the inside of whichever object happens to be at the origin.
            // -campos overrides that with an exact spot, which is how a bug at a known position is looked at.
            _camera = _options.CameraPosition is { } at
                ? new FreeCamera(new Vector3d(at.X, at.Y, at.Z), _options.CameraYaw, _options.CameraPitch)
                : FreeCameraFactory.Create(scene, _options.CameraYaw, _options.CameraPitch, _options.CameraHeight);
        }

        // Now that the sun is known. The pages hold unlit albedo by design, so this is the only thing
        // that puts light into the cache -- and it runs once, because this game's sun is a fixed
        // direction per mission, which is the bargain the whole surface cache is built on.
        // The lamps this level implies but never declares, worked out once and used in three places: as
        // EMISSION on the fixture geometry itself, so a lamp looks like the thing doing the lighting; as
        // real lights in the deferred pass, so a windowless room has a source at all; and as emitters the
        // surface cache bounces off, where there is one. All three matter together -- a room lit from a
        // fixture that does not glow reads as lit by nothing, and a fixture that glows without lighting
        // reads as a decal.
        //
        // Above the surface cache and not inside it. The cache is an ultra-only structure and none of
        // this depends on it: the glow is a material and the lights are punctual, so binding the whole
        // conclusion to whether the cards were captured would have switched a level's lamps off for
        // anyone running ultra with r_surface_cache 0.
        //
        // A promoted backdrop surveyed its lamps on the preview worker and has been showing them for as
        // long as the player was reading the briefing; surveying again here was the largest single cost
        // of pressing Play, and it would have produced the same answer.
        // A cold start has a NEW world renderer that has to be handed them either way.
        if (_gi.LampsMissionId != id || !promoted)
        {
            _gi.ApplyLamps(
                RenderCVars.GlobalIlluminationLamps.Value != 0
                    ? EmissiveLightProposer.Propose(
                        _scene, _resources, _lampManifest, id, ActiveTitle.Id, _logger)
                    : EmissiveLightProposer.Proposal.None,
                id);
        }

        // The surfaces, now that the sun and the fixtures they are lit by are both known. Captures the
        // atlas and lights it in one step -- see GameHost.RealtimeGi.cs, which is also what the render
        // loop calls when the lighting model is switched on mid-level.
        _gi.EnsureSurfaceCache();

        // The walk has already spoken for a title that keeps its ground in the level; for the one that
        // keeps it in the mission, the terrain IS the ground.
        _ground ??= _playerTerrain;
        _mission.Trace = _mission.Solids is { } liveSolids
            ? _playerTerrain is { } liveTerrain
                ? WorldTrace.Nearest(liveSolids.Tracer(), TerrainRayTrace.Caster(liveTerrain))
                : liveSolids.Tracer()
            : null;

        // What the impact effects trace against, built here rather than beside the security cameras
        // because the terrain is only known now: a blood droplet lands on the ground at least as often
        // as it hits a wall, and the splash behind a body needs whatever is three metres past it. The
        // player's movement cast will not do: it is a coarser collision whose rows name no mark, so every
        // splash traced against it was refused.
        //
        // The terrain leaf reports no material row, so a hit on open ground comes back as Air and marks
        // nothing -- which would leave the ground clean of both bullet holes and blood, where retail's
        // Ground row names hole_concrete1.tex. The level's own terrain row is substituted, which is the
        // same row the footsteps and the player's material lookup read.
        _mission.EffectWalls = _mission.Trace is { } effectTrace
            ? (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
            {
                if (!effectTrace(
                        new WorldTraceRequest(origin, end, WorldTraceMask.World), out WorldTraceHit rich))
                {
                    hit = default;
                    return false;
                }

                hit = rich.RayHit;
                if (rich.Kind == WorldTraceHitKind.Terrain && hit.Material == 0 && _effectTerrainRow is int ground)
                {
                    hit = hit with { Material = ground };
                }

                return true;
            }
            : null;
        _mission.ForgetPlayer();
        _titleFeatures = null;
        _titleHud?.Dispose();
        _titleHud = null;
        _playerMode = false;
        _hosted.Blasts.ClearPending();

        // A fresh mission is a fresh life: the last one's death, its red wash and its death camera do not
        // carry over.
        _mission.StartFreshLife();
        Viewpoint?.ResetDeathView();
        _flashlight.Reset();

        EndStage("the terrain, sky, sun, grade and camera");
    }

    /// <summary>The player: its rig, its animations, its loadout and HUD, stood up at the spawn; or the title's own player.</summary>
    private void MissionPlayer(MissionBuild b)
    {
        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;
        int id = b.Id;
        MissionStringResolver missionText = b.MissionText!;
        WorldScene scene = b.Scene!;
        ObjectCollisionIndex solids = b.Solids!;

        {
            var playerSetup = _hosted.NewPlayerSetup();
            playerSetup.Begin(loaded, levelRoot, solids, missionText);
            Formats.Animation.BoneAnimationSet? playerBodySet = _playerBodyAnimations;

            // The guards' weapons, which have to wait for the roster: a soldier's gun is an authored
            // Gun child task naming a WEAPON_ID_*, and only the table knows which model that draws.
            _soldiers?.Arm(loaded, playerSetup.Weapons);
            playerSetup.ArmVehicles(loaded, levelRoot);
            EndStage("the guards' weapon models");

            playerSetup.Configure();

            // The first-person rig. Every Human builds one in its constructor and gives it skeleton 5
            // and the model 005_01_1 (0x45EB30, 0x464660); it is what the held weapon hangs off, so
            // without it there is nothing to draw a gun on.
            if (_viewRigAnimations is { } viewRigSet)
            {
                playerSetup.UseViewRig(viewRigSet);

                // The arms are the other half of what 0x4644A0 draws, and the half that needs CPU
                // skinning: 005_01_1 is mesh format 1.
                _arms?.Dispose();
                _arms = _presentation.Presenters.Create<ViewModelPresentationRequest, IViewModelPresenter>(ViewModelRequest());
                if (_arms is not null && !_arms.Load(loaded))
                {
                    _arms.Dispose();
                    _arms = null;
                }
            }

            EndStage("the first-person rig and the arms");

            // What the mission arms you with is declared, not computed: weapon tasks hang off the
            // level's own HumanPlayer, and every WEAPON_ID_* resolves to a roster entry naming a
            // Gun Model. With the rig in place that model can finally be put where the game puts it.
            _gunPickupRenderer?.Dispose();
            _gunPickupRenderer = null;
            _heldWeapon?.Dispose();
            _hud?.Dispose();
            _marks?.Dispose();
            _tracers?.Dispose();
            _tracers = null;
            _effects.Release();
            _heldWeapon = null;

            // Set whether or not the level declares a loadout: a weapon the all-weapons cheat grants
            // later is loaded from this mission's packs like any other.
            _loadedMissionForWeapon = loaded;

            if (playerSetup.CarryDeclaredLoadout(levelRoot, out bool carried))
            {
                _heldWeapon = carried ? CreateHeldWeapon() : null;

                // The player's rounds are offered to the soldiers, which own their own health.
                playerSetup.ConnectSoldiers(_hosted.Sounds.PlayRicochet);

                // The in-game HUD owns its own archive, exactly as StatusScreen's constructor does.
                _marks?.Dispose();
                _marks = _presentation.Presenters.Create<EffectsPresentationRequest, IBulletMarkPresenter>(EffectsRequest());

                // The ballistic streaks, beside the marks they end in. Cheap to have around: with
                // g_ballistics off nothing is ever spawned and Draw returns at the count check.
                _tracers?.Dispose();
                _tracers = new TracerTrailRenderer(_device);

                // The world's effects: the bursts where rounds land, the original's own dust, chips and blood,
                // the fireball every blast shares, and the smoke rockets and blasts leave.
                _effects.Load(loaded, _presentation.Presenters, _resources, _fileSystem);

                _hud?.Dispose();
                _hud = _presentation.Presenters.Create<HudPresentationRequest, IHudPresenter>(new HudPresentationRequest(_device, _logger));
                if (_hud is not null && !_hud.Load(_fileSystem))
                {
                    _hud.Dispose();
                    _hud = null;
                }

                _remasteredHud?.Dispose();
                _remasteredHud = _presentation.Presenters.Create<HudPresentationRequest, IRemasteredHudPresenter>(new HudPresentationRequest(_device, _logger));
                if (_remasteredHud is not null && !_remasteredHud.Load())
                {
                    _remasteredHud.Dispose();
                    _remasteredHud = null;
                }

                _debugOverlay?.Dispose();
                _debugOverlay = new DebugOverlayRenderer(_device, _logger);
                if (!_debugOverlay.Load())
                {
                    _debugOverlay.Dispose();
                    _debugOverlay = null;
                }
            }

            if (_mission.GunPickups is { } pickups)
            {
                _gunPickupRenderer = _presentation.Presenters.Create<ViewModelPresentationRequest, IGunPickupPresenter>(ViewModelRequest());
                if (_gunPickupRenderer is not null)
                {
                    _gunPickupRenderer.Materials = _materials;
                    _gunPickupRenderer.Load(loaded, pickups);
                }
            }

            // The grenades in the air, drawn with their own retail models. Loaded beside the
            // pickups because it is the same model machinery at a transform the simulation owns.
            _grenadeRenderer?.Dispose();
            _grenadeRenderer = _presentation.Presenters.Create<ViewModelPresentationRequest, IGrenadePresenter>(ViewModelRequest());
            _grenadeRenderer?.Load(loaded);
            _missileRenderer?.Dispose();
            _missileRenderer = _presentation.Presenters.Create<ViewModelPresentationRequest, IMissilePresenter>(ViewModelRequest());
            _missileRenderer?.Load(loaded);

            EndStage("the loadout, the bullet marks and the HUD");

            // A level with neither a declared spawn nor an opening camera is simply not placed, which the
            // warning below already covers.
            if (playerSetup.Stand(levelRoot, _ground, solids, _camera?.Position, _options.CameraYaw, out PlayerSpawnPoint spawn))
            {
                IHostedPlayer candidate = playerSetup.Player;

                // The player's own third-person body -- the rig the death camera reveals. Built from the
                // model the level's HumanPlayer declares, on skeleton 0's set (000.iff), the same one the
                // player walks on. It stays invisible until the player dies. Without the spawn's model or
                // the set there is simply no body to show, and death falls back to the frozen view.
                if (_soldiers is { } bodies
                    && playerBodySet is { } bodySet
                    && !string.IsNullOrEmpty(spawn.Model))
                {
                    bodies.LoadPlayerBody(loaded, spawn.Model, bodySet, spawn.Position, spawn.Gamma);
                }

                EndStage("standing the player up and building its third-person body");

                // Walking is what starting a mission means. The free camera is the debugging tool, so
                // it is the thing you switch to rather than the thing you start in.
                _playerMode = !_options.StartFlying;

                // After a switch of renderer, back where the view was before it.
                ResumePose();
                _logger.Information(
                    "Player",
                    _playerMode
                        ? $"Standing at {candidate.Position}; F1 for the free camera."
                        : $"Free camera; F1 to stand at {candidate.Position}.");

                // The eye, spelled the way the capture flags take it. Walking and flying are supposed to
                // draw the same picture from the same place, and the only way to check that is to put the
                // free camera exactly where the walking eye is -- which needs the eye, not the feet the
                // line above reports, and needs it before a capture run can be scripted around it.
                Camera3D spawnEye = candidate.Camera;
                _logger.Information(
                    "Player",
                    FormattableString.Invariant(
                        $"Eye at -campos {spawnEye.Position.X:F3},{spawnEye.Position.Y:F3},{spawnEye.Position.Z:F3} ")
                        + FormattableString.Invariant(
                            $"-yaw {candidate.Body.Yaw:F4} -pitch {candidate.Body.Pitch:F4}"));
            }
            else
            {
                _logger.Warning(
                    "Player",
                    "Nothing to stand on at the level's spawn, so this mission opens in the free camera.");
            }
        }

        // A title that brings its own player stands it here, in place of the rig above.
        StandTitlePlayer(loaded, levelRoot, scene);
    }

    /// <summary>The forest and the water, the menu's renderer and backdrop dropped, and the mission reported built.</summary>
    private void MissionAdornments(MissionBuild b)
    {
        bool promoted = b.Promoted;
        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;

        // The things standing in the world rather than being it: planted foliage, and water. Both are
        // handed the mission's model lookup, which is the one thing the ground and the sky do not need --
        // a forest plants instances of a model, and the model lives in a pack rather than in the level's
        // own files.
        //
        // Outside the block above, which exists to keep what the backdrop already built. The backdrop
        // builds the ground and the sky and stops there, so a promoted mission that skipped these would
        // be a level with no trees and no water in it -- which is what pressing Play produced while this
        // sat with the rest.
        ScenePresentationRequest adornments = new(
            _device,
            levelRoot,
            _resources,
            name => loaded.TryGetModel(name, out MefModel? model) ? model : null,
            (name, slot) => loaded.TryGetTextureName(name, slot, out string texture) ? texture : null,
            _logger);

        _forest?.Dispose();
        _forest = _presentation.Presenters.Create<ScenePresentationRequest, IForestPresenter>(adornments);

        _water?.Dispose();
        _water = _presentation.Presenters.Create<ScenePresentationRequest, IWaterPresenter>(adornments);
        MakeTaskPresenters(levelRoot);

        // The menus are done; without closing their screens the last one would keep drawing over the world.
        _retailMenus?.CloseScreens();

        // The menu's backdrop is not needed once a real mission is up. On a cold start that releases the
        // previewed level's packs outright. On a promotion it releases nothing: the preview IS this
        // mission now, and its registry and file system are reachable through `_resources` and through
        // the renderers that were built against them — dropping this reference only says the level has
        // stopped being scenery.
        _menuBackdrop.Clear();
        _arming = null;

        // Built. What the hold is waiting on from here is the lighting, not the level.
        NotifyMissionBuilt();
        _logger.Information("Mission", $"{(promoted ? "Promoted" : "Loaded")} {loaded}.");
    }

    /// <summary>Turns the menu's backdrop into the mission being played, without loading anything.</summary>
    /// <param name="preview">The backdrop, which must be the mission the player asked for.</param>
    /// <returns><see langword="true"/> when the mission is up and the player is standing in it.</returns>
    /// <remarks>
    /// <para>
    /// The level under the menu is not a picture of the mission — it <em>is</em> the mission, built by
    /// <see cref="MissionPreviewLoader"/> on a worker thread while the player was reading the briefing.
    /// The only reason it was not playable is that it was built into its own private
    /// <see cref="GameLoop"/>, which is exactly what made building it off the render thread safe: its
    /// task tree, its script namespace and its variables are unreachable from the running game, so
    /// nothing in it could ever be ticked. Promotion is the game changing which loop it advances.
    /// </para>
    /// <para>
    /// What that buys is the whole of the load: the packs are parsed, the scene walked, the collision
    /// index built, the terrain surface built and — the single largest cost — every ground tile's detail
    /// map derived, all of it already done and none of it on this thread. What is left is the part that
    /// genuinely cannot leave the render thread, which is the GL uploads, and those are the subject of
    /// the arming that runs while the menu is idle.
    /// </para>
    /// <para>
    /// The outgoing loop and its resource registry are dropped here. That is not housekeeping: a
    /// resource registry holds a level's worth of packs, and <see cref="AdoptPreview"/>
    /// already carries the scar of a leak in the same shape — a world renderer dropped without being
    /// disposed leaked a level's textures per row of the mission list.
    /// </para>
    /// </remarks>
    private bool PromoteBackdrop(MissionPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        if (!_missions.TryGet(preview.MissionId, out MissionDescriptor? chosen))
        {
            _logger.Warning("Mission", $"Asked to promote mission {preview.MissionId}, which is not registered.");
            return false;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();

        // The level is about to be built; it is not yet worth looking at. Everything temporal -- the
        // probe gather, the radiance cache, the resolve's history, the eye -- needs frames rather than
        // load time, so those frames are drawn with the picture withheld, under the entry shot's hold.
        // See GameHost.MissionWarmUp.cs.
        BeginMissionWarmUp(chosen);

        // A mission with an authored intro is promoted straight from the menu with no shot to cover it,
        // so it goes in behind the sequence's card, over the picture the menu held.
        if (!_entry.IsRunning)
        {
            PresentLoadingFrame();
        }

        // The schedule moves across before the incoming loop runs a single frame. A fresh GameLoop builds
        // its own GameClock starting at tick zero, and -lockstep derives its synthetic milliseconds FROM
        // the frame count, so letting the counters restart would make wall-clock time jump backwards.
        GameLoop outgoing = _loop;
        preview.Loop.Clock.AdoptScheduleFrom(outgoing.Clock);

        _loop = preview.Loop;
        _resourceFileSystem = preview.FileSystem;
        _resources = preview.Resources;
        _session.Begin(chosen, preview.LevelRoot);

        // The backdrop stops being drawn lite before BringMissionUp reads the lighting variables: the
        // real-time structures it skipped are built from here, under the warm-up's hold.
        UpdateBackdropProfile();
        _computerOpen = false;
        _computerFlight.Reset();

        // The outgoing loop's level tasks, if it had any. Coming from the front end it has none — the
        // shell is drawn over a backdrop, not over a played mission — but a promotion reached from
        // anywhere else must not leave the previous level's tree alive in a loop nobody advances.
        if (outgoing.Tasks.Root is { } outgoingRoot)
        {
            foreach (GameTask child in outgoingRoot.Children.ToArray())
            {
                if (child is LevelTask level && !level.IsDeleted)
                {
                    outgoing.Tasks.Delete(level);
                }
            }
        }

        BringMissionUp(preview.Mission, preview.LevelRoot, preview.MissionId, promoted: true);

        _logger.Information(
            "Mission",
            $"Promoted the backdrop for mission {preview.MissionId} in {clock.Elapsed.TotalMilliseconds:F1} ms "
                + "on the render thread.");
        return true;
    }

    /// <summary>Builds the level's top-down map of what the solid world stands over, and binds it.</summary>
    /// <param name="scene">The mission's placed objects.</param>
    /// <param name="loaded">The mission whose models bend its spline segments, as the collision index bends them.</param>
    /// <remarks>
    /// <para>
    /// The one answer to "can the sky reach this point", for the fog, the falling rain, its splashes and
    /// the standing water alike. It is a raster of the collision geometry seen from above rather than a
    /// list of building bounding boxes, because the boxes were wrong both ways round: on mission one a
    /// quarter of the ground inside them has open sky over it — a building at an angle to the world axes
    /// fills barely a third of its own box — while two fifths of the ground genuinely under a roof lay
    /// outside every box, its shelter never having been labelled a building.
    /// </para>
    /// <para>
    /// The CPU keeps the raster for its own questions and the GPU is handed a packed copy, so the puddle
    /// a pixel draws and the drop the storm culls are decided by the same numbers.
    /// </para>
    /// </remarks>
    private void BuildShelterField(WorldScene scene, LoadedMission loaded)
    {
        ReleaseShelterField();

        long started = _loop.TimeSource.Milliseconds;
        _shelterField = ShelterHeightField.Build(
            scene.Placements,
            collision: _titleRuntime.Formats.Collision,
            models: name => loaded.TryGetModel(name, out MefModel? model) ? model : null);
        if (_shelterField.IsEmpty)
        {
            _logger.Information("Shelter", "the level places nothing solid; nothing is sheltered");
            return;
        }

        Bgra32[] texels = _shelterField.Encode(out float scale);
        _shelterMap = _device.CreateTexture(
            new TextureDescription(
                _shelterField.Width,
                _shelterField.Depth,
                MipLevels: 1,
                TextureFilter.Point,
                TextureAddressMode.Clamp) { HoldsData = true },
            texels);

        _giRoofTransform = new ShelterMapTransform(
                (float)_shelterField.OriginX,
                (float)_shelterField.OriginY,
                (float)_shelterField.CellMetres,
                _shelterField.Width,
                _shelterField.Depth,
                _shelterField.LowestZ,
                scale,
                (float)ShelterHeightField.SurfaceHeadroomMetres,
                (float)ShelterHeightField.MaxOverheadMetres);
        _terrainHeightsGivenTo = null;
        _device.SetShelterMap(
            _shelterMap,
            new ShelterMapTransform(
                (float)_shelterField.OriginX,
                (float)_shelterField.OriginY,
                (float)_shelterField.CellMetres,
                _shelterField.Width,
                _shelterField.Depth,
                _shelterField.LowestZ,
                scale,
                (float)ShelterHeightField.SurfaceHeadroomMetres,
                (float)ShelterHeightField.MaxOverheadMetres));

        _logger.Information(
            "Shelter",
            FormattableString.Invariant(
                $"{_shelterField.Width}x{_shelterField.Depth} at {_shelterField.CellMetres:0.00} m, z {_shelterField.LowestZ:0.0}..{_shelterField.HighestZ:0.0}, built in {_loop.TimeSource.Milliseconds - started} ms"));
    }

    /// <summary>Drops the level's shelter map, and tells the device there is none.</summary>
    private void ReleaseShelterField()
    {
        _giRoofTransform = ShelterMapTransform.None;
        ReleaseTerrainRaster();
        _shelterField = ShelterHeightField.Empty;
        _device.SetShelterMap(TextureHandle.None, ShelterMapTransform.None);
        if (_shelterMap.IsValid)
        {
            _device.DestroyTexture(_shelterMap);
            _shelterMap = TextureHandle.None;
        }
    }

    private void ReleasePlayedMission()
    {
        ReleaseTaskPresenters();

        // First, because both are worker threads tracing the level that is going and they hold the whole
        // of what they trace: the lightmap bake's texel maps, triangles and BVH run to a few hundred
        // megabytes, and nothing here released them. A bake was only ever cancelled by the NEXT one
        // starting, so leaving a level left every core path-tracing a level nobody was in -- which is
        // what the new level then had to load against -- and a session that entered a handful of missions
        // carried a handful of those at once. The heap stood at one and a half gigabytes of lightmap
        // texels alone by the sixth.
        _baked.Abandon();

        _computerOpen = false;
        _computerFlight.Reset();
        _session.ResetOutcome();
        _mapComputer?.Dispose();
        _mapComputer = null;
        _mapComputerDevice?.Dispose();
        _mapComputerDevice = null;
        _tacticalComputer?.Dispose();
        _tacticalComputer = null;
        _messages?.Dispose();
        _messages = null;
        _computerHighlights = null;
        _mission.ReleaseRuntimes();
        _hosted.Audio.ForgetCameras();
        _cutsceneSounds.StopAll();
        _session.Mission = null;

        // The level's own sun goes with the level. It is read on load only when it is not already set --
        // which is how a promoted backdrop hands its own in -- so a sun left standing here was taken for
        // one that had been handed in, and the next mission was lit and BAKED by the one before it. The
        // backdrop the menu puts up next sets its own in AdoptPreview, so nothing that needs one is left
        // without.
        _sun = null;
        _effectiveSun = null;

        // And so does what the level said about its own light. Left standing, the next title's mission
        // would be lit by the last one's answer -- and for Project I.G.I. that means its sky model would
        // stay switched off after a visit to the sequel.
        _deferredLighting = null;
        _cycleSun = null;

        // Before a single mesh is destroyed. The backend has assembled its own copy of the level from
        // the frames it drew -- the traceable world, and the material records a hit is looked up in --
        // and destroying the meshes one by one tells it each has gone without telling it the level has.
        // Left to accumulate, the records ran up to their ceiling over half a dozen mission entries and
        // the traced world went on holding structures nothing would place again.
        _device.ReleaseWorld();

        // And the residency walk, which holds the world renderer being disposed below and is stepped by
        // the idle loop for as long as a backdrop is up. Left standing it keeps calling MakeResident on
        // a disposed renderer -- reliably, on the first frame after a title switch, because that path
        // releases the level while the menu is still arming the next backdrop.
        _arming = null;

        _sky?.Dispose();
        _terrain?.Dispose();
        _world?.Dispose();
        _soldiers?.Dispose();
        _menuBackdrop.ReleaseSoldiers();
        _heldWeapon?.Dispose();
        _gunPickupRenderer?.Dispose();
        _hud?.Dispose();
        _remasteredHud?.Dispose();
        _debugOverlay?.Dispose();
        _marks?.Dispose();
        _tracers?.Dispose();
        _effects.Release();
        _cameraStatusLights?.Dispose();
        _grenadeRenderer?.Dispose();
        _missileRenderer?.Dispose();
        _arms?.Dispose();
        _titleArms?.Dispose();
        _sky = null;
        _rain?.Dispose();
        _rain = null;
        _storm?.Dispose();
        _storm = null;
        _snow?.Dispose();
        _snow = null;
        ReleaseShelterField();
        _terrain = null;
        _world = null;
        _soldiers = null;
        _heldWeapon = null;
        _gunPickupRenderer = null;
        _hud = null;
        _remasteredHud = null;
        _debugOverlay = null;
        _marks = null;
        _tracers = null;
        _cameraStatusLights = null;
        _grenadeRenderer = null;
        _missileRenderer = null;
        _arms = null;
        _titleFeatures = null;
        _titleArms = null;
        _playerMode = false;
        _scene = null;
        _playerTerrain = null;
        _ground = null;
        _mission.ReleaseWorld();
        _loadedMissionForWeapon = null;

        // A vehicle's engines and guns must not keep sounding into the next run.
        _vehicleSounds?.StopAll();
        _vehicleSounds = null;

        if (_session.LevelRoot is not null && !_session.LevelRoot.IsDeleted)
        {
            // Deleting a task does not unexport its names (TaskTree.Delete), and a restart reuses this loop's
            // variables. Left bound, AreaActivate_175.nActive, Car_361.isExploded and the rest answered for the
            // run being left until the new level's runtimes rebound them -- after the conditional containers had
            // already been decided. Mission 7's real T80 (ConditionalContainer_700, which latches itself through
            // this.isRun) came up live at tick 0 whenever the player restarted from inside its trigger area.
            // Unbound, the new level starts from the same zeros a first load does.
            _loop.Variables.UnexportTree(_session.LevelRoot);
            _loop.Tasks.Delete(_session.LevelRoot);
        }
        _session.LevelRoot = null;

        // Back to the front end, so back to the menu theme. Every voice the level started goes with it:
        // an ambience bed outliving its level would follow the player into the menus.
        _audioOut.Sounds?.StopAll();
        _audioOut.Music?.Play(MusicPlayer.MenuMusic);
    }

    // The path-traced lightmapper's phase 1-2 verification: enumerate the lightmapped texels, bake the
    // direct sun + sky, report coverage, and dump the largest few pages as normal and radiance images. A
    // correct normal page reads as a UV unwrap of the object; a correct radiance page shows the sun and its
    // shadows. Dev-only, driven by IGI_BAKELM_DIR, so it never runs in a normal load.
    private void DumpLightmapVerification(
        WorldScene scene, WorldRayCast cast, Vector3f sunDirection, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        IReadOnlyList<LightmapBaker.LightmapTarget> targets = LightmapBaker.EnumerateTargets(scene);

        long covered = 0;
        long total = 0;
        foreach (LightmapBaker.LightmapTarget target in targets)
        {
            total += target.Texels.Length;
            foreach (LightmapBaker.LightmapTexel texel in target.Texels)
            {
                if (texel.IsCovered)
                {
                    covered++;
                }
            }
        }

        _logger.Information(
            "Lightmap", $"Enumerated {targets.Count} target(s); {covered}/{total} texels covered.");

        Vector3f sunColour = _sun?.Colour ?? new Vector3f(1f, 1f, 1f);
        Vector3f skyColour = _sun?.Ambient ?? new Vector3f(0.3f, 0.3f, 0.3f);
        Stepping.Drain(LightmapBaker.BakeDirectAndSky(targets, cast, sunDirection, sunColour, skyColour, 64, _work));

        // Phase 3: fold in the multi-bounce indirect. Build the lightmapped triangles and a BVH over them,
        // then gather several bounces reading the last pass's light. Grey albedo for now (colour is 3b),
        // so this verifies the bounce fills and brightens rather than the colour it carries.
        LightmapBaker.LightmapTriangle[] triangles = LightmapBaker.BuildTriangles(scene, targets, _resources);
        int coloured = triangles.Count(t => MathF.Abs(t.Albedo.X - 0.5f) > 0.02f || MathF.Abs(t.Albedo.Y - 0.5f) > 0.02f || MathF.Abs(t.Albedo.Z - 0.5f) > 0.02f);
        _logger.Information(
            "Lightmap",
            FormattableString.Invariant(
                $"Albedo: {coloured}/{triangles.Length} triangles got a resolved colour; samples ({triangles[0].Albedo.X:F2},{triangles[0].Albedo.Y:F2},{triangles[0].Albedo.Z:F2}) ({triangles[triangles.Length / 2].Albedo.X:F2},{triangles[triangles.Length / 2].Albedo.Y:F2},{triangles[triangles.Length / 2].Albedo.Z:F2})."));
        LightmapBaker.LightmapBvh bvh = LightmapBaker.LightmapBvh.Build(triangles);
        _logger.Information("Lightmap", $"Built a BVH over {triangles.Length} triangle(s); gathering bounces.");
        Stepping.Drain(LightmapBaker.BakeBounces(targets, triangles, bvh, skyColour, 4, 64, _work));

        // A numerical spot-check on the largest page: how many texels it covered, and a few of them with
        // their world position (should sit near the placement) and baked radiance (sunlit near the sun
        // colour, shadowed/skylit near the ambient). This settles what an image cannot show clearly.
        if (targets.OrderByDescending(t => t.Texels.Length).FirstOrDefault() is { } sample)
        {
            int sampleCovered = 0;
            foreach (LightmapBaker.LightmapTexel t in sample.Texels)
            {
                if (t.IsCovered)
                {
                    sampleCovered++;
                }
            }

            Vector3d origin = scene.Placements[sample.PlacementIndex].Position;
            _logger.Information(
                "Lightmap",
                FormattableString.Invariant(
                    $"Largest page p{sample.PlacementIndex} {sample.Width}x{sample.Height}: {sampleCovered} covered, placement at ({origin.X:F1},{origin.Y:F1},{origin.Z:F1})."));

            int shown = 0;
            for (int i = 0; i < sample.Texels.Length && shown < 4; i++)
            {
                if (!sample.Texels[i].IsCovered)
                {
                    continue;
                }

                LightmapBaker.LightmapTexel t = sample.Texels[i];
                Vector3f rad = sample.Radiance[i];
                _logger.Information(
                    "Lightmap",
                    FormattableString.Invariant(
                        $"  texel {i}: pos ({t.WorldPosition.X:F1},{t.WorldPosition.Y:F1},{t.WorldPosition.Z:F1}) n ({t.Normal.X:F2},{t.Normal.Y:F2},{t.Normal.Z:F2}) rad ({rad.X:F2},{rad.Y:F2},{rad.Z:F2})"));
                shown++;
            }
        }

        int dumped = 0;
        foreach (LightmapBaker.LightmapTarget target in targets.OrderByDescending(t => t.Texels.Length).Take(6))
        {
            string stem = FormattableString.Invariant(
                $"lm_p{target.PlacementIndex}_r{target.RecordIndex}_{target.Width}x{target.Height}");

            Bgra32[] normals = new Bgra32[target.Texels.Length];
            LightmapBaker.DumpTargetNormals(target, normals);
            BitmapWriter.Write(
                Path.Combine(outputDirectory, stem + "_normal.bmp"), target.Width, target.Height, normals);

            Bgra32[] radiance = new Bgra32[target.Texels.Length];
            LightmapBaker.DumpTargetRadiance(target, radiance, exposure: 1f);
            BitmapWriter.Write(
                Path.Combine(outputDirectory, stem + "_radiance.bmp"), target.Width, target.Height, radiance);

            dumped++;
        }

        _logger.Information("Lightmap", $"Dumped {dumped} page(s) to {outputDirectory}.");
    }

    // The presenter for the player's hand, which follows whatever weapon ends up in it (Igi1PlayerSetup.Carry hands
    // the player everything the mission declares).
    private IHeldWeaponPresenter? CreateHeldWeapon()
    {
        IHeldWeaponPresenter? presenter = _presentation.Presenters.Create<ViewModelPresentationRequest, IHeldWeaponPresenter>(ViewModelRequest());
        if (presenter is not null)
        {
            presenter.Materials = _materials;
        }

        return presenter;
    }

    // The all-weapons cheat put weapons in the player's hands (Igi1MissionTick tops the inventory up).
    private void ShowCheatWeapons()
    {
        // A level that declared no loadout never built a presenter for the hand, so the first granted
        // weapon would be carried but never drawn.
        if (_heldWeapon is null && HostedPlayer?.View is not null
            && _presentation.Presenters.Create<ViewModelPresentationRequest, IHeldWeaponPresenter>(ViewModelRequest()) is { } presenter)
        {
            presenter.Materials = _materials;
            _heldWeapon = presenter;
        }
    }

    /// <summary>The three things the small world-effect presenters are built from.</summary>
    private EffectsPresentationRequest EffectsRequest() => new(_device, _resources, _logger);

    /// <summary>The four things every first-person and projectile presenter is built from.</summary>
    /// <remarks>
    /// A helper because five call sites want the same value and the magic-object table is not readable
    /// until a mission has loaded, so it cannot be cached beside the provider.
    /// </remarks>
    private ViewModelPresentationRequest ViewModelRequest() =>
        new(_device, _resources, _mission.MagicObjects, _logger, _work);

    /// <summary>Parses the test-only outro forcing list: "Var=Value,Var=Value".</summary>
    private static List<(string Name, string Value)> TestOutroWrites(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return [];
        }

        List<(string, string)> writes = [];
        foreach (string part in spec.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = part.IndexOf('=');
            if (equals > 0)
            {
                writes.Add((part[..equals].Trim(), part[(equals + 1)..].Trim()));
            }
        }
        return writes;
    }

    /// <summary>The mission's side of one pass of the loop: the world, the player and the level's state, ticked.</summary>
    /// <param name="state">The frame loop's state.</param>
    /// <param name="outcome">What the loop's frame did; only a tick advances anything.</param>
    private void TickMission(FrameLoopState state, FrameOutcome outcome)
    {
        _missionHost.State = state;
        _hosted.Tick.Run(outcome == FrameOutcome.Tick);
    }

    /// <summary>The host's side of <see cref="IHostedMission"/>: the picture, the sound, the files, the menus and the log of a title's mission.</summary>
    /// <param name="host">The host.</param>
    private sealed class MissionHostServices(GameHost host) : IMissionHostServices
    {

        public void SpawnImpactBurst(in WorldRayHit worldHit)
        {
            if (host._effects.Particles is { } bursts
                && RenderCVars.ImpactParticles.Value is > 0f and var intensity)
            {
                string material = host._mission.Materials is { } names
                    && names.TryGet(worldHit.Material, out GameMaterial struck)
                    ? struck.Name
                    : string.Empty;
                bursts.SpawnBurst(
                    worldHit.Position,
                    worldHit.Normal,
                    ImpactParticleRenderer.KindOf(material),
                    intensity);
            }
        }

        /// <summary>Gets or sets the frame loop's state for the pass being run.</summary>
        public FrameLoopState State { get; set; } = null!;

        public bool PlayerMode => host._playerMode;

        public Vector3d? FreeCameraPosition => host._camera?.Position;

        public WorldScene? Scene => host._scene;


        // Its spawn column follows the eye, so it is handed the position the last frame drew from -- one frame stale
        // is nothing to a column forty metres across, and the tick has no camera of its own.
        public void StepWeather() => host._storm?.Tick(host._stormEyeMetres);


        public void SoundVehicles(CutsceneRuntime cutscenes)
        {
            if (host._audioOut.Sounds is not { } vehicleSoundSystem)
            {
                return;
            }

            host._vehicleSounds ??= new VehicleSoundPlayer(vehicleSoundSystem, new Random(0x49474931));
            host._vehicleSounds.Tick(
                cutscenes.VehicleSoundSources(),
                cutscenes.VehicleGunSounds(),
                host._audioOut.Device.Listener.Position * OpenIGI.Engine.Audio.SoundSystem.UnitsPerMetre);
        }

        public IOpenIgiLogger Logger => host._logger;

        public OpenIGI.Input.InputState ViewInput => host.ViewInput;

        public bool FailedMenu => host._session.FailedMenu;

        public bool ComputerOpen => host._computerOpen;

        public bool TickTitlePlayer() => host.TickTitlePlayer();

        public void CheatWeaponsGranted() => host.ShowCheatWeapons();

        public void PlayMineTrigger(Vector3d origin) =>
            host._audioOut.Sounds?.PlayDefinition("proxy_arm", origin, priority: 180);




        public void PresentPlayerImpacts() => host.PresentPlayerImpacts();




        public void StepEffectsAfterDeath() => host.StepEffectsAfterDeath();

        public void StepScreen() => host._hud?.Tick();

        // Test-only outro forcing: re-apply the env-specified gate writes every tick so gates whose variables the level
        // scripts reset stay satisfied while the scene plays.
        public void ApplyTestWrites()
        {
            foreach ((string writeName, string writeValue) in host._testOutroTickWrites)
            {
                host._loop.Variables.TryWrite(
                    writeName,
                    QvmValue.FromInt32((int)double.Parse(writeValue, System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        public void FollowCorpse(Vector3d centre) => host.Viewpoint?.FollowDeathView(centre);

        public OpenIGI.Input.InputState Input => host._window.Input;

        public OpenIGI.Game.Config.PlayerProfile? InputProfile => host._configuration.Active;

        public IGroundHeightField? Ground => host._ground;

        public bool IsInteractionViewActive => host.Viewpoint?.IsInteractionViewActive == true;

        public void BeginInteractionView(Vector3d feet, float facing, WorldRayCast? walls) =>
            host.Viewpoint?.BeginInteractionView(feet, facing, walls);

        public void FollowInteractionView(Vector3d feet) => host.Viewpoint?.FollowInteractionView(feet);

        public void TickInteractionView() => host.Viewpoint?.TickInteractionView();

        public void ResetInteractionView() => host.Viewpoint?.ResetInteractionView();

        public bool BodyFollowAllowed =>
            host._playerMode
            && !host._session.FailedMenu
            && !host._computerOpen
            && !host._entry.IsCameraActive
            && !host._menuFlow.OverShowcase;

        public void CloseTick() => host.SettleMissionOutcome(State);

        public GameLoop Game => host._loop;

        public VirtualFileSystem FileSystem => host._fileSystem;

        public OpenIGI.Engine.Resources.ResourceRegistry Resources => host._resources;

        public TerrainData? Terrain => host._playerTerrain;

        public Formats.Animation.BoneAnimationSet? ReadSharedAnimations()
        {
            host.ReadSharedAnimations();
            return host._playerBodyAnimations;
        }

        public void EndStage(string what) => host.EndStage(what);



        public void WeaponDropped(LoadedMission loaded, GunPickup pickup) => host._gunPickupRenderer?.Add(loaded, pickup);

        public void TerrainRowRead(int? row) => host._effectTerrainRow = row;

        public SoundSystem? SoundOutput => host._audioOut.Sounds;


        public void PlayDefinition(string name, Vector3d? positionInUnits = null, int priority = 0) =>
            host._audioOut.Sounds?.PlayDefinition(name, positionInUnits, priority: priority);



        public void BeginDeathCamera(Vector3d position, float facing, WorldRayCast walls) =>
            host.Viewpoint?.BeginDeathView(position, facing, walls);

        public int ScriptDifficulty => (int?)host._options.Difficulty ?? host._configuration.Active?.Difficulty ?? 1;

        public bool PlayCutscenes => host._options.PlayCutscenes;

        public QvmValue? HostNative(string name, OpenIGI.Scripting.Expressions.ScriptArguments arguments)
        {
        if (name == GameMusicNatives.EnableNative)
        {
            if (host._audioOut.Music is not null)
            {
                host._audioOut.Music.Enabled = true;
            }
            return QvmValue.FromInt32(1);
        }
        if (name == GameMusicNatives.DisableNative)
        {
            if (host._audioOut.Music is not null)
            {
                host._audioOut.Music.Enabled = false;
            }
            return QvmValue.FromInt32(1);
        }
            return null;
        }

        public void CutsceneSound(CutsceneSoundEvent sound) => host._cutsceneSounds.Set(sound);

        public void StopCutsceneSounds() => host._cutsceneSounds.StopAll();

        public bool IsRaining => host._rain?.IsWeatherEnabled == true;

        public bool ImpactEffectsWanted => RenderCVars.ImpactEffects.Value;

        public bool BloodWanted => host._configuration.Active?.BloodEnabled ?? true;

        public bool IsKeyDown(OpenIGI.Input.KeyCode key) => host._window.Input.Keyboard.IsDown(key);

        public void PlayDefinition(string name, Vector3d positionInUnits, int priority) =>
            host._audioOut.Sounds?.PlayDefinition(name, positionInUnits, priority: priority);

        public void FlashbangSmoke(Vector3d positionInUnits) => host._effects.SpawnFlashbangSmoke(positionInUnits);

        public void Fireball(Vector3d positionInUnits) =>
            host._effects.SpawnBlast(positionInUnits, Math.Max(1f, RenderCVars.ImpactParticles.Value));


        public void Explosion(Vector3d positionInUnits, float fullRangeUnits, int fireballs, int fragments) =>
            host._effects.Particles?.SpawnRetailExplosion(positionInUnits, new RetailExplosionShape(fullRangeUnits, fireballs, fragments));

        public void SpeakerExplosion(Vector3d positionInUnits, bool isCameraLens) =>
            host._effects.Particles?.SpawnRetailExplosion(
                positionInUnits,
                isCameraLens ? RetailExplosionShape.CameraLens : RetailExplosionShape.AlarmLight);

        public void GlassShatter(WorldPlacement placement, Vector3d atUnits) =>
            host._effects.Particles?.SpawnGlassShatter(placement, atUnits);
    }

    /// <summary>What the player's rounds struck, heard and seen: the impact sounds, the sparks and dust, and the rockets' trails.</summary>
    private void PresentPlayerImpacts()
    {
        IHostedPlayer player = HostedPlayer!;

        // What this tick's ballistic rounds landed on -- dirt, steel or flesh, out of the
        // material table, where the round arrived rather than where it was fired.
        _hosted.Sounds.PlayPlayerImpacts();
        _hosted.Sounds.PlayWeaponDrops();

        // The same impacts, seen: sparks and dust composed from the struck material.
        // Ticked before spawning so a new burst draws its birth frame unmoved.
        _effects.Tick();

        // The original's own effects, on the same rhythm: the dust and chips step, and the
        // blood droplets in the air are traced against the world, marking whatever they
        // land on. Ticked before this tick's impacts spawn, so a new puff is drawn where
        // it was born.
        _hosted.Impacts.Tick();

        SpawnBallisticImpactParticles();
        _hosted.Impacts.SpawnPlayerImpacts();

        // The launcher rockets in the air lay their smoke trail, one column of puffs per
        // segment they crossed this tick.
        _effects.SpawnMissileTrails(player.Missiles, _mission.VehicleMissiles);
    }

    /// <summary>The world's effects stepping on under the death camera, after the player's update has stopped.</summary>
    /// <remarks>
    /// A blast that killed the player burns out, and its debris and smoke settle, under the death camera as they do in the
    /// original. Stepping them only from the player's update froze them on screen.
    /// </remarks>
    private void StepEffectsAfterDeath()
    {
        _effects.Tick();
        _hosted.Impacts.Tick();
    }

    /// <summary>Holds the objectives' messages, and opens the failure menu or moves on once the mission is decided.</summary>
    private void SettleMissionOutcome(FrameLoopState state)
    {
        if (_mission.Objectives is { } objectiveTick)
        {
            // The remaster holds a message three seconds longer than the original does, because
            // the authored durations are short and the hold only starts once the last character
            // has been typed. The retail HUD always retains the original timing and message block,
            // irrespective of the independently archived tactical-message preference.
            objectiveTick.ExtraMessageHoldSeconds =
                RemasteredHudPresentation.UsesTacticalMessages(
                    RenderCVars.RemasteredHud.Value,
                    RenderCVars.TacticalMessages.Value)
                && _mission.Cutscenes?.IsPlaying != true
                    ? RemasteredMessageHoldSeconds
                    : 0f;
        }

        _mission.Objectives?.TickAfterLevelState();
        if (_mission.Objectives is { IsFailed: true } || TitlePlayer is { Outcome: TitleMissionOutcome.Failed })
        {
            // The retail grace period first; see MissionSession.CountFailure.
            if (_session.CountFailure() && state.Shell is not null)
            {
                _session.FailedMenu = true;

                // Said out loud because the level decided this, not the player: a LevelFlow
                // 'Failed' expression is authored out of message and area state, and when it
                // turns for a reason nobody in the room understands this line is where the
                // reading starts.
                _logger.Information(
                    "Mission",
                    $"The level's LevelFlow has failed the mission; opening the failed page.");
                _computerOpen = false;
                _computerFlight.Reset();
                HostedPlayer?.SetMapComputerOpen(false);

                // The result is a shell page, not a drawn card, so its two choices can be clicked
                // and navigated like every other page and the shell's own cursor is the one on
                // screen. Menu mode hands it the frame's input and draw; the pause flag holds the
                // showcase off, because the level this is drawn over is still loaded -- the same
                // pair the pause menu sets.
                _menuFlow.MenuMode = true;
                _menuFlow.PauseMenu = true;
                state.Shell.OpenMissionFailed();

                // The platform cursor stays hidden: the shell draws its own, so revealing this one
                // would put a second, unrelated pointer beside it.
                _window.CursorVisible = false;
                HostedPlayer?.HoldStill();
                _window.CursorLocked = false;
            }
        }
        else
        {
            _session.ClearFailure();
        }
        if (!_session.ResultHandled
            && (_mission.Objectives is { IsComplete: true } || TitlePlayer is { Outcome: TitleMissionOutcome.Completed })
            && _session.Mission is { } completedMission)
        {
            _session.ResultHandled = true;
            // A successor the catalogue does not offer ends the campaign the way the last mission
            // does -- in a build cut to the opening missions, the last one offered is its finale.
            int nextMissionId = completedMission.NextMissionId;
            if (nextMissionId != MissionDescriptor.NextMissionUndefined && _missions.TryGet(nextMissionId, out _))
            {
                if (_configuration.Active is { } activeProfile)
                {
                    MissionProgression.UnlockNext(activeProfile, _missions, nextMissionId);
                    // Retail writes LOCAL:config.qsc after every non-final completion, even when
                    // replaying an earlier mission leaves the frontier unchanged.
                    HostConfiguration.Save(_configuration, _logger);
                }

                _logger.Information("Mission", $"Completed mission {completedMission.Id}; loading {nextMissionId}.");
                TitlePlayer?.MissionCompleted(nextMissionId);
                StartMission(nextMissionId);
            }
            else
            {
                _logger.Information("Mission", $"Completed final mission {completedMission.Id}; campaign complete.");
                ReleasePlayedMission();
                if (state.Shell is not null)
                {
                    // Event 7 is the retail outro-screen path. The authored final cutscene has
                    // completed before LevelFlow reaches here, so the shell now continues to its
                    // campaign-complete destination.
                    state.Shell.OpenOn("extras");
                    _menuFlow.MenuMode = true;
                    _menuFlow.ShowcasePending = true;
                }
            }
        }
    }

    // The presenters the title registered for particular task types, one for each such task of the level.
    private readonly List<ITaskPresenter> _taskPresenters = [];

    /// <summary>Makes a presenter for every task of the level whose type has one registered.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    private void MakeTaskPresenters(LevelTask levelRoot)
    {
        ReleaseTaskPresenters();
        PresenterRegistry presenters = _presentation.Presenters;
        if (presenters.TaskTypeCount == 0)
        {
            return;
        }

        TaskPresenterRequest request = new(_device, _resources, levelRoot, _logger);
        foreach (GameTask task in levelRoot.Descendants())
        {
            if (presenters.CreateForTask(task, request) is { } presenter)
            {
                _taskPresenters.Add(presenter);
            }
        }

        _logger.Information("Mission", $"{_taskPresenters.Count} task presenter(s) made.");
    }

    // Frees the presenters made for the level's tasks.
    private void ReleaseTaskPresenters()
    {
        foreach (ITaskPresenter presenter in _taskPresenters)
        {
            presenter.Dispose();
        }

        _taskPresenters.Clear();
    }
}
