using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    /// <summary>Test-only outro forcing writes re-applied on every logic tick.</summary>
    private List<(string Name, string Value)> _testOutroTickWrites = [];

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
        _mission.Terrain = loaded.Terrain is { IsUsable: true } standable ? standable : null;

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

    /// <summary>The test captures' script writes, made over the objectives the game just built.</summary>
    private void MissionTestCaptures(MissionBuild b)
    {
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

    /// <summary>The scene walk, the world renderer and the collision index; the vehicles on it, what can be destroyed, and the weather.</summary>
    private void MissionScene(MissionBuild b)
    {
        if (_mission.Cutscenes is null)
        {
            throw MissingSystem(MissionSystemNames.Cutscenes);
        }

        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;
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
            // The vehicle table is what binds each car's wreck: a CAR record's last value names the model
            // 0x42E5A0 puts in the car's place when it blows up (601_08_1 for the APC, 614_08_1 for the T80).
            // Without it every placement went unbound, and the renderer drops a destroyed placement that has
            // nothing to swap in -- so a LAW'd tank simply vanished where retail leaves its wreck.
            WorldBuildResult built = _worldBuilder.Build(
                new WorldBuildRequest(
                    loaded, levelRoot, _resources, _mission.MagicObjects, _logger, _mission.VehiclePhysics, _title.Services));
            scene = built.Scene;
            _scene = scene;
            _mission.Ground = built.Ground;
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

            // The title's own view culling, when its walk decides it -- Project I.G.I.'s sector cull. The sequel's arrives
            // with its player instead.
            world.Visibility = built.Visibility;
            _world = world;
            solids = built.Solids ?? ObjectCollisionIndex.Build([]);
            _mission.Solids = solids;
            EndStage("the scene walk, the world renderer and the collision index");
        }
        b.Scene = scene;
        b.Solids = solids;
        b.World = world;
    }

    /// <summary>The destructibles and the cutscenes handed to the world renderer, and the weather fresh for the mission.</summary>
    private void MissionSceneView(MissionBuild b)
    {
        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;
        WorldScene scene = b.Scene ?? throw MissingSystem(MissionSystemNames.Scene);
        IWorldPresenter world = b.World ?? throw MissingSystem(MissionSystemNames.Scene);

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
    }

    /// <summary>The soldiers' meshes and textures.</summary>
    private void MissionSoldiers(MissionBuild b)
    {
        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;
        bool promoted = b.Promoted;
        ObjectCollisionIndex solids = b.Solids!;

        // The soldiers the level places. Each names its own model, skeleton and stand animation;
        // the sets they index were loaded when the packs opened. They are not
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
                new SoldierPresentationRequest(
                    _device,
                    _resources,
                    _logger,
                    _mission.MagicObjects,
                    _detailMaps,
                    () => _world?.Shading.Sun,
                    // The original's levels of detail, on its own look only, as the world's models give theirs up.
                    () => GraphicsPresetRegistry.Default.Current == GraphicsPreset.Vanilla ? _modelLod : null));
            _mission.AdoptSoldiers(_soldiers);
            _soldiers?.Load(
                loaded,
                levelRoot,
                // The walk's ground when the title keeps one in the level, and the mission's terrain
                // otherwise. _mission.Ground itself is not settled until the player is stood up below.
                _mission.Ground ?? _mission.Terrain,
                solids.Caster());
        }

        EndStage("the soldiers' meshes and textures");
    }

    /// <summary>The doors, elevators, pickups and world interactions handed to the world renderer.</summary>
    private void MissionInteractionsView(MissionBuild b)
    {
        IWorldPresenter world = b.World!;

        // Never handed over after the move to the host, which left the renderer's interaction-driven
        // transforms dead: a gun's stand did not turn with the gun it holds.
        world.GenericPickups = _mission.GenericPickups;
        world.Interactions = _mission.Interactions;
        world.Variants = _mission.Interactions;
        world.Doors = _mission.Doors;
        world.Elevators = _mission.Elevators;

        EndStage("the doors, elevators and world interactions");
    }

    /// <summary>The security cameras and the rotating objects handed to the world renderer, and the cameras' status lights.</summary>
    private void MissionCamerasView(MissionBuild b)
    {
        IWorldPresenter world = b.World!;

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

    /// <summary>The alarm speakers the game's brains left, handed to the world renderer.</summary>
    private void MissionAlarmsView(MissionBuild b)
    {
        if (_hosted.Setup.HasSoldiers)
        {
            b.World!.AlarmSpeakers = _mission.AlarmSpeakers;
        }
    }
}
