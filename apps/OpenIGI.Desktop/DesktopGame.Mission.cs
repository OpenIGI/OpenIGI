using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.World;
using OpenIGI.Game;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Menus;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Desktop;

internal sealed partial class DesktopGame
{
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
        _activeLevelRoot = levelRoot;
        _activeMission = chosen;
        _missionResultHandled = false;
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
        MissionLoader missionLoader = new(
            _missions, _resourceFileSystem, _resources, levelLoader, _logger, terrainLoader, _soundBank);
        LoadedMission loaded = missionLoader.Load(id);

        // Evaluate the level's ConditionalContainer conditions against the initial playable state, so the
        // scene, spawn and soldier walks below skip the subtrees the original leaves unregistered (the
        // intro/outro cut-scene props and actors). Runs after the tree is built and its variables exported,
        // and before anything walks it.
        new ConditionalContainerGate(_loop.Variables, _logger).Prepare(levelRoot);

        BringMissionUp(loaded, levelRoot, id, promoted: false);
    }

    /// <summary>Resolves a retail directory without assuming the install's casing matches the script.</summary>
    private static string? ResolveDirectoryCaseInsensitive(string root, string relativePath)
    {
        string current = Path.GetFullPath(root);
        foreach (string part in relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            string exact = Path.Combine(current, part);
            if (Directory.Exists(exact))
            {
                current = exact;
                continue;
            }

            if (!Directory.Exists(current))
            {
                return null;
            }

            string? match = Directory.EnumerateDirectories(current)
                .FirstOrDefault(path => string.Equals(
                    Path.GetFileName(path), part, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                return null;
            }

            current = match;
        }

        return current;
    }

    /// <summary>Gets the along-track extent retail uses to space cars in a Train consist.</summary>
    /// <summary>Gets a wheel model's rolling radius in world units, for the wheel animation.</summary>
    private static float CutsceneWheelRadius(LoadedMission mission, string name)
    {
        return mission.TryGetModel(name, out MefModel? model) && model is not null
            ? WorldScene.WheelRadius(model)
            : 0f;
    }

    private static float CutsceneModelLength(LoadedMission mission, string name)
    {
        if (!mission.TryGetModel(name, out MefModel? model) || model is null)
        {
            return 0f;
        }

        return WorldScene.TrainCouplingDistance(model);
    }

    /// <summary>Gets the retail origin fraction between a Train model's two coupling vertices.</summary>
    private static float CutsceneModelPivot(LoadedMission mission, string name)
    {
        if (!mission.TryGetModel(name, out MefModel? model) || model is null)
        {
            return 0f;
        }

        return WorldScene.TrainCouplingPivotFraction(model);
    }

    /// <summary>Chooses the engine-unit occupant used by retail AreaActivate volumes.</summary>
    /// <param name="playerUnits">The player's body position in engine units.</param>
    /// <param name="cutsceneViewMetres">
    /// The authored cutscene camera in renderer metres when it owns the frame; otherwise null.
    /// </param>
    /// <param name="freeViewMetres">
    /// The diagnostic free camera in renderer metres. It is only a fallback when no player body exists;
    /// moving a debug camera must never satisfy gameplay gates such as Mission 4's extraction volume.
    /// </param>
    internal static Vector3d? AreaActivationOccupant(
        Vector3d? playerUnits,
        Vector3d? cutsceneViewMetres,
        Vector3d? freeViewMetres = null)
    {
        if (cutsceneViewMetres is { } cutscene)
        {
            return cutscene * WorldScene.UnitsPerMetre;
        }

        if (playerUnits is { } player)
        {
            return player;
        }

        return freeViewMetres is { } free
            ? free * WorldScene.UnitsPerMetre
            : null;
    }

    /// <summary>
    /// Gets the player-body projection that preserves occupancy while a cutscene camera drives activation.
    /// </summary>
    /// <param name="playerUnits">The player's body position in engine units.</param>
    /// <param name="cutsceneViewMetres">
    /// The authored cutscene camera in renderer metres when it owns the frame; otherwise null.
    /// </param>
    /// <remarks>
    /// Retail queries humans rather than cameras, so the stationary body never stops occupying its
    /// volume when a cinematic starts. Without this, Mission 4's latch-less outro gate
    /// (<c>AreaActivate_156.nActive</c>) drops as soon as the <c>CutScene_2222</c> camera leaves the
    /// extraction volume, aborting the cutscene and refiring it every other tick. The camera remains
    /// the primary projection (Mission 14 streams tunnel sections off it), while the body is an
    /// alternate projection of that same logical player rather than another occupant.
    /// </remarks>
    internal static Vector3d? AreaActivationExtraOccupant(
        Vector3d? playerUnits,
        Vector3d? cutsceneViewMetres) =>
        cutsceneViewMetres is not null && playerUnits is { } player ? player : null;

    /// <summary>Feeds the desktop host's player projections and liveness into authored areas.</summary>
    internal static void TickAreaActivations(
        MissionObjectiveRuntime? objectives,
        Vector3d? playerUnits,
        int playerTeam,
        Vector3d? cutsceneViewMetres,
        Vector3d? freeViewMetres,
        bool playerAlive) =>
        objectives?.TickLevelState(
            AreaActivationOccupant(playerUnits, cutsceneViewMetres, freeViewMetres),
            playerTeam,
            AreaActivationExtraOccupant(playerUnits, cutsceneViewMetres),
            playerAlive);

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
        _music?.Play(MusicPlayer.GameMusic);

        MenuStringTable missionStrings = new(_resources);

        // Mission text is addressed by its own key, not by the menu table's LOCAL:<language>/<key> —
        // see MissionStringResolver. Everything that resolves mission data goes through this one.
        MissionStringResolver missionText = new(_resources, missionStrings);
        _cutscenes = new CutsceneRuntime(
            levelRoot,
            _loop.Variables,
            (name, arguments) =>
            {
                if (name == GameMusicNatives.EnableNative)
                {
                    if (_music is not null)
                    {
                        _music.Enabled = true;
                    }
                    return QvmValue.FromInt32(1);
                }
                if (name == GameMusicNatives.DisableNative)
                {
                    if (_music is not null)
                    {
                        _music.Enabled = false;
                    }
                    return QvmValue.FromInt32(1);
                }
                if (name == "MenuManager_SetEnabled")
                {
                    // Cutscene start/stop expressions own this native in retail.  The menu manager
                    // lives in the shared loop task tree, so update its live script context rather
                    // than returning success while leaving input enabled during cinematics.
                    MenuManagerTask? manager = FindMenuManager();
                    if (manager?.ScriptContext is { } context && arguments.Count >= 1)
                    {
                        context.IsEnabled = arguments.GetBoolean(0);
                    }

                    return QvmValue.FromInt32(1);
                }

                if (name == "Game_CutsceneDelete")
                {
                    _cutscenes?.DeleteCutscenes();
                    return QvmValue.FromInt32(1);
                }

                return null;
            },
            SetCutsceneSound,
            modelLength: name => CutsceneModelLength(loaded, name),
            modelPivot: name => CutsceneModelPivot(loaded, name),
            vehiclePhysics: _vehiclePhysics,
            terrain: loaded.Terrain,
            logger: _logger,
            vehicleObstacleHit: (task, origin, end) =>
            {
                if (_worldSolids?.TryCastIgnoring(
                        task,
                        origin,
                        end,
                        out WorldRayHit hit,
                        out GameTask? obstacle,
                        out string obstacleLabel) == true
                    && obstacle is not null)
                {
                    return (obstacle, hit, obstacleLabel);
                }

                return null;
            },
            vehicleObstacleReleased: obstacle =>
                _doors?.Doors.FirstOrDefault(door => ReferenceEquals(door.Task, obstacle))?.State.IsFullyOpen == true);
        bool collectDiagnostics = _logger.IsEnabled(LogLevel.Information);
        if (collectDiagnostics && levelRoot.Tree.FromId(1608) is DeclaredTask levelTimer)
        {
            string parameters = string.Join(
                ", ",
                levelTimer.Type.Parameters.Select(
                    (parameter, index) => $"{parameter.Name}={levelTimer.GetValue(index) ?? "<null>"}"));
            _logger.Information("Mission", $"[timer] LevelTimer_1608 {parameters}");
        }
        if (collectDiagnostics && levelRoot.Tree.FromId(161) is DeclaredTask activation)
        {
            string parameters = string.Join(
                ", ",
                activation.Type.Parameters.Select(
                    (parameter, index) => $"{parameter.Name}={activation.GetValue(index) ?? "<null>"}"));
            _logger.Information("Mission", $"[timer] AreaActivate_161 {parameters}");
        }
        _missionObjectives = new MissionObjectiveRuntime(
            levelRoot,
            _loop.Variables,
            missionText.Get,
            _cutscenes.TryInvokeNative);

        // Cinematics are opt-in while their retail parity work is in progress. Do not tear the runtime
        // out: the level expressions still use its native surface, and mission outros may activate it
        // later. Instead take the exact path Escape takes in retail. The objective flow observes the
        // break key, closes the intro container, and the cutscene tick applies that transition before
        // the first gameplay frame, so neither its camera nor its ConditionalSounds leak through.
        if (!_options.PlayCutscenes && _cutscenes.IsPlaying)
        {
            _cutscenes.RequestBreak();
            _missionObjectives.TickLevelState();
            _cutscenes.Tick();
            _cutscenes.FinishExpressionTick();
            StopCutsceneSounds();
        }

        // Same native edge as Escape (LevelFlow_GetBreakCutSceneKey). Used by the
        // agent playtest so lockstep/-frames can skip the intro without a HITL key.
        if (string.Equals(
                Environment.GetEnvironmentVariable("OPENIGI_BREAK_CUTSCENE"),
                "1",
                StringComparison.Ordinal)
            && _cutscenes.IsPlaying)
        {
            _cutscenes.RequestBreak();
            _missionObjectives.TickLevelState();
            _cutscenes.Tick();
            _cutscenes.FinishExpressionTick();
            StopCutsceneSounds();
        }

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

            _cutscenes.RequestBreak();
            _cutscenes.Tick();
            _missionObjectives.TickLevelState();
            _missionObjectives.TickAfterLevelState();
            _cutscenes.FinishExpressionTick();
            _logger.Information("Mission", "Test capture outro forcing active.");
        }
        _computerHighlights = new ComputerHighlightCatalog(levelRoot, _loop.Variables);
        _mapComputer = new MapComputerRenderer(_device, _resources, _logger, missionText.Get);
        if (!_mapComputer.Load(_fileSystem))
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
        // DesktopGame.TacticalComputer. The retail pair above needs the installation's own archives; this
        // one needs only the shipped faces, so a mission whose computer.res is missing still has a map.
        _tacticalComputer = new TacticalComputerRenderer(_device, _resources, _logger, missionText.Get);
        if (!_tacticalComputer.Load())
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

        ObjectCollisionIndex solids;
        WorldRenderer world;
        WorldScene scene;
        if (promoted)
        {
            solids = _worldSolids ?? throw new InvalidOperationException(
                "A promoted mission must arrive with the backdrop's collision index already built.");
            world = _world ?? throw new InvalidOperationException(
                "A promoted mission must arrive with the backdrop's world renderer already built.");
            scene = _scene ?? throw new InvalidOperationException(
                "A promoted mission must arrive with the backdrop's scene already walked.");
        }
        else
        {
            scene = WorldScene.Build(loaded, levelRoot, _magic, _logger);
            _scene = scene;
            world = new WorldRenderer(_device, _resources, _logger)
            {
                Materials = _materials,
                DetailMaps = _detailMaps,
                Lamps = _lamps,
            };
            _world = world;
            solids = ObjectCollisionIndex.Build(scene.Placements);
            _worldSolids = solids;
            EndStage("the scene walk, the world renderer and the collision index");
        }
        // Mission 4's authored getaway Bell is a one-shot terminal landing. Configure that
        // behavior explicitly here; CutsceneRuntime remains generic and does not infer it from ids.
        if (id == 4 && levelRoot.Tree.FromId(800) is { } getawayBell)
        {
            _cutscenes.EnableTerminalTerrainLanding(getawayBell);
        }

        // Every mission: a car's suspension reads this for the surface under each wheel, and much
        // of what a vehicle stands on is placed geometry -- Mission 1's outro truck starts on a
        // garage floor above the dirt. Re-seating the helicopters on it stays Mission 4's.
        _cutscenes.CollisionIndex = solids;
        if (id == 4)
        {
            _cutscenes.RestHelicoptersOnSupport();
        }

        // Bind each car's drawn TASKTYPE_WHEEL attachments to its physics record so the drive force
        // and the tyre torque act at the real axle offsets, and the wheels roll and steer.
        _cutscenes.BindVehicleWheels(scene, name => CutsceneWheelRadius(loaded, name));

        _destructibles = DestructibleObjectRegistry.Build(
            levelRoot,
            scene.Placements,
            _loop.Variables);
        world.Destructibles = _destructibles;
        solids.Destructibles = _destructibles;
        world.Cutscenes = _cutscenes;

        // Rain is a gameplay effect, not part of the backdrop picture: it is configured fresh for every
        // mission — cold start or promoted — straight from the level's own objects.qvm (a RainEffect task
        // enabled by its Is Rain and Is Active flags), and whatever rain the previous mission left is
        // dropped first, exactly as the reference resets its rain at every level load. The same scene's
        // geometry also provides the roofs the rain is suppressed under.
        _rain?.Dispose();
        _rain = new RainRenderer(_device, _logger);
        _rain.ReadParams(levelRoot);
        _snow?.Dispose();
        _snow = new SnowRenderer(_device, _logger);
        _snow.ReadParams(levelRoot);
        _storm?.Dispose();
        _storm = new StormRenderer(_device, _logger);
        BuildShelterField(scene);

        // The storm falls where the level says rain falls -- it is the same weather, drawn better -- so
        // it inherits the vanilla renderer's reading of the RainEffect task rather than parsing it a
        // second time, and shelters under the same roofs.
        _storm.SetShelter(_shelterField);
        ApplyWeatherSettings();

        // The soldiers the level places. Each names its own model, skeleton and stand animation
        // (player.md section 15); the sets they index were loaded when the packs opened. They are not
        // WorldScene placements - a human task is a typed task, and the original draws it through the
        // bone path, not the static one.
        // A promoted backdrop has already built these, behind the menu and against the very same level
        // and collision index, so it hands them over rather than uploading a second copy of every
        // soldier's mesh. Taking the reference is also what stops the backdrop disposing them.
        if (promoted && _backdropSoldiers is { } armedSoldiers)
        {
            _soldiers?.Dispose();
            _soldiers = armedSoldiers;
            _backdropSoldiers = null;
        }
        else
        {
            _soldiers?.Dispose();
            _soldiers = new SoldierRenderer(_device, _resources, _logger, _magic) { DetailMaps = _detailMaps };
            _soldiers.Load(
                loaded,
                levelRoot,
                loaded.Terrain is { IsUsable: true } standing ? standing : null,
                solids.Caster());
        }

        EndStage("the soldiers' meshes and textures");
        _soldiers.Cutscenes = _cutscenes;

        // A round that meets a body bleeds before it does anything else (0x45F94A), whoever fired it.
        _soldiers.BloodSpray = (owner, at, direction) => SpawnRetailBlood(owner, at, direction);
        _cutscenes?.SetTargetPoseResolver(_soldiers.CutscenePose);

        // Interactions are level systems, not AI systems: build them even when a mission has no usable
        // navigation graph. The same door objects are shared by player use, AI door hops and collision.
        _doors = DoorRegistry.Build(levelRoot);
        _elevators = ElevatorRegistry.Build(levelRoot, _loop.Variables);
        _interactions = WorldInteractionRegistry.Build(levelRoot, loaded, _doors, _loop.Variables, _elevators);
        _genericPickups = GenericPickupRegistry.Build(levelRoot, _loop.Variables);
        world.GenericPickups = _genericPickups;
        solids.GenericPickups = _genericPickups;
        _soldiers.SetAiTaskActivator(id => _interactions?.ActivateFromAi(id));
        _soldiers.UseInteractions(_interactions);
        _soldiers.SetDoors(_doors, solids);
        _soldiers.SetElevators(_elevators);

        // The first genuinely world-positioned sound in the port. Its distances are the definition's
        // own -- ai_death_1 is authored 0 to 50 metres -- so a guard dying across the map is silent
        // without anything here choosing a range.
        _soldiers.SetDeathSound(position => PlayDeathSound(
            HumanVariantSound.AiDeathPrefix, HumanVariantSound.AiDeathVariants, position));

        // The level's authored ambience -- the AmbientArea boxes that bed a sound in around the player.
        _ambience = AmbientAreaRegistry.Build(levelRoot, _soundRandom);
        _ambientVoices.Clear();
        if (_ambience.Areas.Count > 0)
        {
            _logger.Information("Audio", $"{_ambience.Areas.Count} ambient area(s).");
        }

        // The grunt a soldier makes at a non-fatal hit, rate-limited per human.
        _soldiers.SetHitSound(position => PlayHitSound(
            HumanVariantSound.AiHitPrefix, HumanVariantSound.AiHitVariants, position));
        _soldiers.SetHitCooldown(() => HumanVariantSound.HitCooldownTicks(_soundRandom));

        // AIAction_PlaySound (0x48DF80) either attaches the definition to the human or starts it at
        // HumanAI's current position. Both begin at the same live position; the backend owns any voice
        // following after that point.
        _soldiers.SetScriptSound((name, position, attached) =>
            _sounds?.PlayDefinition(name, position, priority: 200));

        // Each AI weapon owns its automatic loop and updates its world position every tick. The round
        // callback handles ballistics only; without this parallel path AI shots were completely silent.
        _soldiers.SetWeaponSound(TickSoldierWeaponSound);

        // Footsteps: animation event 1010, picked from the six walk sounds of whatever is underfoot.
        _soldiers.SetFootstepSound(step => PlayFootstep(step));

        // Doors: door_open_1 / door_close_1 / door_slide_1 in mission 1, all definition names.
        _doors.SetSoundSink(PlayDoorSound);
        _doors.SetDoorwayBlockedQuery(DoorwayIsBlockedByHuman);
        _elevators.SetSoundSink(PlayElevatorSound);
        world.Doors = _doors;
        world.Elevators = _elevators;
        if (_doors.Doors.Count > 0)
        {
            _logger.Information("World", $"{_doors.Doors.Count} door(s) available to players and AI.");
        }

        EndStage("the doors, elevators and world interactions");

        // Camera sight uses the same placed-object + terrain trace as HumanAI. Cameras are level
        // objects, not navigation actors, so construct them independently of whether this mission
        // happens to ship any AI graphs.
        WorldTraceCast worldTrace = _playerTerrain is { } ground
            ? WorldTrace.Nearest(solids.Tracer(), TerrainRayTrace.Caster(ground))
            : solids.Tracer();
        _cameraBeepVoices.Clear();
        _cameraMoveVoices.Clear();
        StopCutsceneSounds();
        _securityCameras = SecurityCameraRegistry.Build(levelRoot, _loop.Variables, worldTrace);
        _securityCameras.BindScene(scene.Placements);
        world.SecurityCameras = _securityCameras;
        _cameraStatusLights?.Dispose();
        _cameraStatusLights = new CameraStatusLightRenderer(_device, _logger);
        if (!_cameraStatusLights.Load(_fileSystem))
        {
            _cameraStatusLights.Dispose();
            _cameraStatusLights = null;
        }
        _logger.Information(
            "Ai",
            $"{_securityCameras.Cameras.Count} security camera(s), "
                + $"{_securityCameras.ControlCount} camera control(s) placed.");

        // The level's nav graphs. A level ships ONE PER AIGraph TASK, named by that task's id (ai.md 1.1),
        // and every HumanAI names the one it walks through its own "Graph ID" parameter (ai.md 7.6).
        //
        // Level 1 ships eleven of them. Picking a single graph -- the one with the most nodes -- and giving
        // it to every soldier routed each one over node ids belonging to a graph that was not its own,
        // which is why guards walked to places no patrol goes and off the edge of the level.
        try
        {
            string graphDirectory = ResolveDirectoryCaseInsensitive(
                    _gamePath, Path.Combine(loaded.Descriptor.MissionDirectory, "graphs"))
                ?? Path.Combine(_gamePath, loaded.Descriptor.MissionDirectory, "graphs");
            Dictionary<int, GraphFile> graphs = [];
            if (Directory.Exists(graphDirectory))
            {
                // Retail installs commonly preserve uppercase pack names (GRAPHS/GRAPH9.DAT). The
                // VFS accepts those names, but this direct filesystem walk must do so explicitly on a
                // case-sensitive volume as well.
                foreach (string file in Directory.EnumerateFiles(graphDirectory)
                    .Where(file => Path.GetFileNameWithoutExtension(file)
                        .StartsWith("graph", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(Path.GetExtension(file), ".dat", StringComparison.OrdinalIgnoreCase)))
                {
                    string stem = Path.GetFileNameWithoutExtension(file);
                    if (!int.TryParse(stem.AsSpan("graph".Length), out int graphId))
                    {
                        continue;
                    }

                    try
                    {
                        GraphFile graph = GraphFile.Read(File.ReadAllBytes(file));

                        // The graph file stores node positions in the graph's own frame. The world
                        // position is node + graphTask.Position — what 0x4F9770 adds every time.
                        Vector3d graphPos = TaskGeometry.FindPosition(levelRoot, graphId);
                        graph.ApplyWorldOffset(graphPos);

                        graphs[graphId] = graph;
                    }
                    catch (GraphFormatException ex)
                    {
                        _logger.Warning("Ai", $"Nav graph '{stem}' did not parse: {ex.Message}");
                    }
                }
            }

            if (graphs.Count > 0)
            {
                _logger.Information(
                    "Ai",
                    $"Nav graphs: {graphs.Count} — " +
                    string.Join(", ", graphs.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value.Nodes.Count}n")));

                // The difficulty every guard reads its combat row at. Faithfully the ACTIVE PLAYER PROFILE's
                // (sub_406F70 takes it from sub_406220()'s first field), so the difficulty the player picked
                // in the menu actually reaches the AI -- which it did not while this was a hardcoded default,
                // leaving every guard on GD_2/Normal whatever the profile said. -difficulty still overrides,
                // for testing a difficulty without editing a profile.
                AiDifficulty effectiveDifficulty = _options.Difficulty
                    ?? (AiDifficulty)Math.Clamp(
                        _configuration.Active?.Difficulty ?? (int)AiDifficulty.Normal,
                        (int)AiDifficulty.Easy,
                        (int)AiDifficulty.Hard);
                _logger.Information(
                    "Ai",
                    $"Difficulty {HumanAiConfigRegistry.NameOf(effectiveDifficulty)} ({effectiveDifficulty}) "
                        + (_options.Difficulty is null
                            ? $"from profile '{_configuration.Active?.Name ?? "(none)"}'."
                            : "from -difficulty override."));
                // The sight and bullet rays test placed objects AND the terrain, as the original's
                // 0x4CDB20 does (both of the sight ray's passes send the -1 mask whose sign bit turns
                // the terrain arm on -- player.md 7.6). Objects alone let guards see and shoot through
                // hills. Nearest contact wins.
                // Movement still skips portal faces (walk through a doorway). Sight must not:
                // those faces are the room/window openings, and skipping them lets a guard see
                // through a closed door and into the room on the other side.
                WorldRayCast levelCast = WorldTrace.AsRayCast(worldTrace, tracePortals: true);

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
                    bool discardRetailLightmaps = RenderCVars.BakedLightmaps.Value >= 2;
                    if (RenderCVars.BakedLightmaps.Value > 0 && _scene is { } bakeScene && _world is not null)
                    {
                        Vector3f lmSun = _sun?.Colour ?? new Vector3f(1f, 1f, 1f);

                        // The LEVEL's ambient, not SkyFillColour's. That reads the effective sun -- the
                        // authored one after the day/night sweep and the daylight re-point have had it,
                        // which moves every frame and belongs to the frame before this load. The two
                        // inputs beside it come from the level's own Dirlight, so mixing the two gave a
                        // disk-cache key that could not be reproduced: entering the same mission twice in
                        // a session missed its own bake and traced the level again.
                        Vector3f lmSky = (_sun?.Ambient ?? new Vector3f(0.27f, 0.29f, 0.33f))
                            * RenderCVars.SkyLight.Value;
                        _lightmapBakeMission = id;
                        _lightmapBakeSun = bakeSunDirection;
                        _lightmapBakeSunColour = lmSun;
                        _lightmapBakeSky = lmSky;

                        if (_lightmapCache.TryLoad(id, bakeSunDirection, lmSun, lmSky) is { } cachedLightmaps)
                        {
                            _world.SetBakedLightmaps(cachedLightmaps, discardRetailLightmaps);
                        }
                        else
                        {
                            // Set the mode straight away so the worker's seconds are not spent showing the
                            // shipped lightmaps under an exclusive (r_lightmap 2) request: with no bake yet,
                            // lightmapped surfaces read black until the bake lands. Mode 1 keeps the shipped
                            // pages meanwhile, which is what its fallback means.
                            _world.SetBakedLightmaps(null, discardRetailLightmaps);

                            // Resolving each surface's albedo reads the resource registry, so the texel
                            // maps, textured triangles and their BVH are built here on the main thread; the
                            // worker then does only the tracing, which touches immutable data and the cast.
                            IReadOnlyList<LightmapBaker.LightmapTarget> lmTargets = LightmapBaker.EnumerateTargets(bakeScene);
                            LightmapBaker.LightmapTriangle[] lmTris = LightmapBaker.BuildTriangles(bakeScene, lmTargets, _resources);
                            LightmapBaker.LightmapBvh lmBvh = LightmapBaker.LightmapBvh.Build(lmTris);

                            _lightmapBake?.Cancel();
                            _lightmapBake = LightmapBake.Start(
                                bakeScene, lmTargets, lmTris, lmBvh, bakeCast, bakeSunDirection, lmSun, lmSky,
                                bounces: 4, hemisphereRays: 128, _logger);

                            if (_options.Lockstep && _lightmapBake.WaitForResult() is { } readyLightmaps)
                            {
                                _world.SetBakedLightmaps(readyLightmaps, discardRetailLightmaps);
                                _lightmapCache.Save(id, bakeSunDirection, lmSun, lmSky, readyLightmaps);
                                _lightmapBake = null;
                            }
                        }
                    }
                    else
                    {
                        // The setting is off, or off since the last load: make sure no stale bake lingers.
                        _world?.SetBakedLightmaps(null);
                    }

                    // The probe field, guarded on its own cvar so it does not trace under a lightmap-only
                    // request (r_gi off, r_lightmap on -- the ultra preset is exactly that).
                    if (RenderCVars.GlobalIllumination.Value)
                    {
                    // Whatever the console says, but never so fine that a big level spends a minute of
                    // load tracing rays nobody asked for.
                    float giSpacing = RenderCVars.GlobalIlluminationSpacing.Value;

                    // Remembered so the live re-bake (F11) can trace the field again from exactly the
                    // inputs this load used -- the same cast, sun and spacing -- whether the field arrived
                    // from the cache or the tracer below.
                    _bakeCast = bakeCast;
                    _probeBakeMission = id;
                    _probeBakeSpacing = giSpacing;
                    _probeBakeSun = bakeSunDirection;

                    // A shipped (or previously-written) bake loads instantly; only a mission without one
                    // pays to trace it here, and then writes the result so no load pays for it again.
                    if (_giCache.TryLoad(id, giSpacing, bakeSunDirection) is { } cachedField)
                    {
                        _probeField = cachedField;
                    }
                    else
                    {
                        _probeBake = IrradianceProbeBake.Start(
                            _scene,
                            bakeCast,
                            bakeSunDirection,
                            giSpacing,

                            // One albedo for the whole level. The alternative is reading each hit's own
                            // texture, which needs the ray to report a material rather than a triangle --
                            // and IGI's palette is concrete, snow and dirt, so a warm grey is not far off
                            // any of them.
                            ProbeBakeAlbedo,
                            _logger);

                        if (_options.Lockstep)
                        {
                            // A capture is not allowed to race the worker: a reference image has to either
                            // contain the field or not, never "whatever frame 30 caught". So a lockstep run
                            // blocks for the finished field and writes the cache inline.
                            _probeField = _probeBake.WaitForResult();
                            _probeBake = null;

                            if (_probeField is not null)
                            {
                                _giCache.Save(id, giSpacing, bakeSunDirection, _probeField);
                            }
                        }
                        else
                        {
                            // Interactive play enters the level on flat ambient and watches the light
                            // stream in: the render loop polls TryCollectLatest and eases each snapshot in
                            // as it lands, so a room fills with light over the first seconds rather than the
                            // load stalling until it is lit. The cache is written once the bake completes,
                            // under the key already remembered above.
                            _probeField = null;
                        }
                    }
                    }
                }

                // The distance fields, on a worker beside the probe bake. Tracing every distinct
                // model is arithmetic and moves off the render thread freely; only the upload cannot,
                // because a GL texture belongs to the context that made it.
                //
                // Started here rather than built here: EnsureDistanceFields is the same call the render
                // loop makes every frame, so a level begins its bake at load and a player who switches the
                // lighting model on later gets one started for them. See DesktopGame.RealtimeGi.cs.
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
                    DiscardSurfaceCache();
                    DiscardDistanceFields();
                }

                EnsureDistanceFields();

                // A capture must not race the worker, for the reason the probe bake records: a
                // reference image has to either contain the field or not, never whichever frame
                // the worker happened to reach.
                if (_options.Lockstep && _sdfBake is { } pendingField
                    && pendingField.WaitForResult() is { } readyField)
                {
                    _sdfBake = null;
                    AdoptDistanceField(readyField.Scene, readyField.Atlas, fromCache: false);
                }

                // Every distinct model's surface is captured into the card atlas, but deliberately NOT
                // here -- even though this is where the geometry first exists. Lighting the cache needs
                // the level's own sun, which is not read until further down this load, and lighting the
                // cards before that gave every page a zero sun direction and a picture of pure sky, which
                // is exactly what the first run produced. EnsureSurfaceCache captures and lights in one
                // go, so it is called once the sun is known; the outgoing cards were dropped above.

                _device.SetIrradianceVolume(_probeField);
                ApplyColourLookup(_options.StartMission);

                // The level's doors, before the brains: a walker asks the registry for the nearest door
                // on a door hop, and an open door is switched out of the collision index so the
                // doorway is walkable. Both have to exist before Animate hands each brain its query.

                EndStage("reading the nav graphs");

                _soldiers.Animate(
                    graphs, levelCast, _resources, _loop.Natives, levelRoot, _aiConfig, effectiveDifficulty,
                    _loop.Variables);
                EndStage("animating the soldiers' brains");

                // The level's alarm speakers, for the sirens: each follows its own expression over
                // the same live variables the controls export through.
                _sirenVoices.Clear();
                _reportedSirenFailures.Clear();
                _alarmSpeakers = AlarmSpeakerRegistry.Build(
                    levelRoot,
                    _loop.Variables,
                    () => _soldiers.AnyAlarmOn);
                _alarmSpeakers.BindScene(scene.Placements);
                world.AlarmSpeakers = _alarmSpeakers;
                _logger.Information("Ai", $"{_alarmSpeakers.Speakers.Count} alarm speaker(s) placed.");

                // A guard's round: it raised the trigger, the weapon's cadence let one out, and this is
                // where it becomes a cast. The AI's hit probability decides whether the round is on
                // target -- the original hands that same figure to the gun with every shot (ai.md 11.6),
                // so a miss is a round that went somewhere else rather than a round that never existed.
                _soldiers.SetRoundHandler((from, at, probability, weapon, shooter) =>
                {
                    _guardRounds++;
                    if (_player is null)
                    {
                        return;
                    }

                    // The ray runs where the GUN points -- the tracked aim (or a patrol's own facing) --
                    // extended past the target, exactly as the retail round is a cast from the muzzle to
                    // the end of its remaining range (GunShot per-tick 0x47B750). A moving player walks
                    // out of the lagging aim and the round geometrically misses; that is retail's miss.
                    Vector3d to = new(
                        from.X + ((at.X - from.X) * 4.0),
                        from.Y + ((at.Y - from.Y) * 4.0),
                        from.Z + ((at.Z - from.Z) * 4.0));

                    Vector3d feet = _player.Body.Position;

                    // Which side fired decides who the round may hurt. A guard's round (opposing the
                    // player's team) may strike the player; a friendly's may not -- it is aimed at an
                    // opposing soldier and a friendly's own gun never endangers the player. Null is
                    // defensive: the player's own shots do not flow through this handler, so no real
                    // round is hostile-neutral; treating it as hostile keeps a round from vanishing.
                    bool shooterHostileToPlayer = shooter is null
                        || shooter.Soldier.Team != _soldiers.PlayerTeam;

                    // The player's living body is a physical obstacle to every round, whatever the
                    // shooter's team -- retail's cast collides with a same-team human too (it just deals
                    // no damage). A DEAD player is not: the detection scan unregisters the corpse, so a
                    // round passes through where the body fell instead of hanging in the air on it.
                    bool playerInTheRay = !_playerHealth.IsDead;

                    // Resolve the guard's round against the player's OWN skinned body (limb hits do less,
                    // and the hitbox is the real body, not a fat cylinder). Only if the body is not loaded
                    // does it fall back to the cylinder-and-trunk stand-in. Skipped entirely for a corpse.
                    HumanHit hit = default;
                    bool met = false;
                    if (playerInTheRay)
                    {
                        met = _soldiers is not null
                            && _soldiers.TryHitPlayerBody(from, to, feet, _player.Body.Yaw, out hit);
                        if (!met)
                        {
                            Vector3f facing = new(-MathF.Sin(_player.Body.Yaw), MathF.Cos(_player.Body.Yaw), 0f);
                            met = HumanHitTest.TryCast(from, to, feet, facing, pose: null, out hit);
                        }
                    }

                    // Where the world stops this ray, over its whole length -- one cast serves cover,
                    // fly-pasts and outright misses alike, and EVERY round that meets a surface leaves
                    // the same mark a player's round does. In retail a miss is visible: the round lands
                    // on the wall or the dirt near you (10.4's decals) -- rounds that silently vanished
                    // are why the guards seemed to hit with every bullet.
                    bool worldBlocked = worldTrace(
                        new WorldTraceRequest(from, to, WorldTraceMask.World),
                        out WorldTraceHit blocker);
                    WorldRayHit worldHit = worldBlocked ? blocker.RayHit : default;

                    // A round that meets no hostile body falls through to the soldiers behind the
                    // player -- an opposing soldier for a guard, a guard for a friendly. This is the
                    // NPC-vs-NPC half of the gate fight: the round is offered to every soldier on the
                    // ray, nearest first, and the first one it meets (per its own probability roll)
                    // takes it. True when a soldier consumed the round; false when it flew past them all.
                    bool SoldiersTakeTheRound()
                        => _soldiers is not null
                            && _soldiers.TryShootAiRound(
                                from, to, probability, weapon, shooter, worldBlocked ? worldHit : null, out _);

                    // The streak, before the outcome: visual only, along the very ray this handler is
                    // resolving, dying where the world stops it. Whether the round connects is decided
                    // below exactly as ever -- from inside the head, a streak that reaches the eye and
                    // a round that hits look the same.
                    if (GameplayCVars.Ballistics.Value && GameplayCVars.BallisticsAiTracers.Value)
                    {
                        Vector3d streakEnd = worldBlocked ? worldHit.Position : to;
                        Vector3d line = streakEnd - from;
                        double lineLength = line.Length;
                        if (lineLength > 1e-6)
                        {
                            AmmoType? streakAmmo = _ammoTypes is { } table
                                && table.TryGetByCalibre(weapon.CalibreId, out AmmoType found)
                                ? found
                                : null;
                            _player.Ballistics.SpawnVisual(
                                from,
                                new Vector3f(
                                    (float)(line.X / lineLength),
                                    (float)(line.Y / lineLength),
                                    (float)(line.Z / lineLength)),
                                streakEnd,
                                weapon,
                                streakAmmo);
                        }
                    }

                    void MarkTheImpact()
                    {
                        if (!worldBlocked)
                        {
                            return;
                        }

                        if (_worldMaterials is { } table)
                        {
                            _player.Marks.TryAdd(worldHit, table);

                            // A guard's stray round runs the struck material's own GunShotEffect just
                            // as the player's does -- 0x4083C0 never asked who fired.
                            if (RenderCVars.ImpactEffects.Value
                                && table.TryGet(worldHit.Material, out GameMaterial? struckRow)
                                && struckRow is not null
                                && struckRow.RecordsImpacts)
                            {
                                Vector3d shotLine = to - from;
                                double shotLength = shotLine.Length;
                                _impactEffects.SpawnSurfaceImpact(
                                    worldHit.Position,
                                    worldHit.Normal,
                                    shotLength > 1e-6
                                        ? new Vector3f(
                                            (float)(shotLine.X / shotLength),
                                            (float)(shotLine.Y / shotLength),
                                            (float)(shotLine.Z / shotLength))
                                        : default,
                                    struckRow.Entry,
                                    struckHuman: false);
                            }
                        }

                        PlayBulletImpactSound(
                            worldHit.Position,
                            struckHuman: false,
                            surfaceMaterial: worldHit.Material);

                        // A guard's stray round pops a speaker exactly as the player's does -- the
                        // retail damage message never asked who fired.
                        DamageAlarmSpeakers(
                            worldHit.Position,
                            owner: blocker.Owner,
                            placement: blocker.Placement);

                        // A guard's round kicks the same burst a player's does -- misses landing
                        // around you are half of what sells incoming fire.
                        if (_impactParticles is { } bursts
                            && RenderCVars.ImpactParticles.Value is > 0f and var intensity)
                        {
                            string material = _worldMaterials is { } names
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

                    if (!met)
                    {
                        // Geometric miss -- aim lag against a moving target, a running patrol's spray, or
                        // a dead player whose corpse stops nothing. The round flies on: offer it to the
                        // soldiers, and only then does it land where the world says.
                        if (SoldiersTakeTheRound())
                        {
                            return;
                        }
                        MarkTheImpact();
                        return;
                    }

                    // The world is in the way: the retail cast meets the nearest surface first, so a wall
                    // or a crate between the muzzle and the body simply takes the round (0x47B750 -- the
                    // round penetrates or stops per the surface, but it does not reach the player). The
                    // small margin keeps the player's own contact from re-testing the surface it stands on.
                    if (worldBlocked && worldHit.Distance < hit.Distance - 40.0)
                    {
                        MarkTheImpact();
                        _logger.Debug(
                            "Weapon",
                            $"Guard round blocked by {blocker.Kind} '{blocker.Label}' at "
                                + $"{blocker.Fraction:P1} before the player body.");
                        return;
                    }

                    // Only now the probability gate: retail rolls `random > probability -> no hit` inside
                    // the cast's evaluate when the trace actually MEETS a human (the context carries
                    // Human+1236, loaded at spawn by sub_474360). A failed roll is a round that flew past
                    // -- through to whatever stands behind the player, where it marks.
                    if (_shotRandom.NextDouble() > probability)
                    {
                        // The roll failed against the player -- the round flies past to whoever stands
                        // behind them: an opposing soldier if the shooter is hostile, a guard if the
                        // shooter is a friendly.
                        if (SoldiersTakeTheRound())
                        {
                            return;
                        }
                        MarkTheImpact();
                        return;
                    }

                    // The roll connected with the player's body. A friendly's round that meets the
                    // player is consumed there with no damage -- retail's cast collides with a same-team
                    // human but deals none, and it never flies on to a soldier behind the player. Only a
                    // hostile round may hurt the player.
                    if (!shooterHostileToPlayer)
                    {
                        return;
                    }

                    // 0x45F94A throws the blood before it looks at the damage at all, so the player
                    // bleeds on contact -- the mist in front of the eye and the splash on whatever
                    // stood behind. The systems belong to the player's own body.
                    Vector3d wound = to - from;
                    double woundLength = wound.Length;
                    SpawnRetailBlood(
                        _playerHealth,
                        hit.Position,
                        woundLength > 1e-6
                            ? new Vector3f(
                                (float)(wound.X / woundLength),
                                (float)(wound.Y / woundLength),
                                (float)(wound.Z / woundLength))
                            : default);

                    _playerHealth.Apply(hit.Zone, weapon.DamageFactor);
                    PlayBulletImpactSound(hit.Position, struckHuman: true, surfaceMaterial: 0);
                    _guardHits++;

                    // The retail handler transforms the round into camera space before writing the four
                    // corner alphas. It picks 0.4–0.8 rather than one opaque wash and replaces the prior
                    // hit, so automatic fire remains a directional cue instead of a full-screen strobe.
                    Vector3f incoming = new(
                        (float)(to.X - from.X),
                        (float)(to.Y - from.Y),
                        (float)(to.Z - from.Z));
                    Vector3f viewIncoming = _player.Camera.Orientation.Transposed().Transform(incoming);
                    _damageFlash.Hit(viewIncoming, _soundRandom.NextSingle());

                    // And the grunt. 0x412440 plays it on the same branch and rate-limits it through
                    // 0x4637C0, so sustained fire staggers the player without stuttering the sound.
                    // Flat, not positional: the sound belongs to the player, who is the listener.
                    if (!_playerHealth.IsDead && _loop.Clock.TickCount >= _playerHitQuietUntil)
                    {
                        _playerHitQuietUntil = _loop.Clock.TickCount + HumanVariantSound.HitCooldownTicks(_soundRandom);
                        PlayHitSound(
                            HumanVariantSound.PlayerHitPrefix, HumanVariantSound.PlayerHitVariants, null);
                    }

                    // Every connecting round SHOVES the player 5 km/h along its travel -- the hit
                    // handler (0x412440) normalises the round's direction and adds x/y x 189.62962
                    // into the slide accumulator, whose 0.9-a-tick damping then bleeds it off. This is
                    // what makes sustained fire stagger the player in retail.
                    if (!_playerHealth.IsDead)
                    {
                        double shoveX = to.X - from.X;
                        double shoveY = to.Y - from.Y;
                        double shoveZ = to.Z - from.Z;
                        double length = Math.Sqrt((shoveX * shoveX) + (shoveY * shoveY) + (shoveZ * shoveZ));
                        if (length > 1e-6)
                        {
                            _player.Body.AddHitShove(
                                (float)(shoveX / length * PlayerBody.HitShoveUnitsPerTick),
                                (float)(shoveY / length * PlayerBody.HitShoveUnitsPerTick));
                        }
                    }

                    _logger.Information(
                        "Weapon",
                        $"A guard's {weapon.ScriptId} hit the player in the {HumanDamage.ZoneName(hit.Zone)} "
                            + $"({_guardHits} of {_guardRounds} round(s); "
                            + $"{_playerHealth.RemainingFraction:P0} left).");

                    if (_playerHealth.IsDead)
                    {
                        BeginPlayerDeath(from, hit.Zone, hit.Bone, weapon.Category);
                        _logger.Information(
                            "Weapon",
                            $"The player is DEAD after {_guardHits} hit(s) from {_guardRounds} round(s), "
                                + $"struck in zone {hit.Zone}; the death camera has pulled back.");
                    }
                });
            }
            else
            {
                _logger.Warning("Ai", $"No nav graph under '{graphDirectory}', so no soldier will patrol.");

                // A missing patrol graph only removes movement.  Retail still registers every
                // HumanAI as a live human: it can stand, be seen and be selected by the binocular
                // detector.  Leaving Animate inside the graphs-present branch rendered the guards
                // but left _brains empty, so BinocularTargets had no contacts to project at all.
                AiDifficulty effectiveDifficulty = _options.Difficulty
                    ?? (AiDifficulty)Math.Clamp(
                        _configuration.Active?.Difficulty ?? (int)AiDifficulty.Normal,
                        (int)AiDifficulty.Easy,
                        (int)AiDifficulty.Hard);
                WorldRayCast levelCast = WorldTrace.AsRayCast(worldTrace, tracePortals: true);
                _soldiers.Animate(
                    graphs, levelCast, _resources, _loop.Natives, levelRoot, _aiConfig, effectiveDifficulty,
                    _loop.Variables);
                EndStage("registering stationary soldiers' brains");
            }
        }
        catch (Exception ex) when (ex is IOException or GraphFormatException)
        {
            _logger.Warning("Ai", $"The nav graph could not be read: {ex.Message}");
        }

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
            _terrain?.Dispose();
            _terrain = loaded.Terrain is { IsUsable: true } usable
                ? new TerrainRenderer(_device, TerrainSurface.Build(usable, levelRoot, _logger), _logger, _detailMaps)
                : null;

            // The sky is script data on the level's task tree; its fog colour is also what the frame
            // clears to, so the horizon meets sky-coloured backing rather than black.
            _sky?.Dispose();
            _sky = new SkyRenderer(_device, _fileSystem, levelRoot, FarPlane, _logger);
            _shadowPass = new ShadowCasterPass(_device);

            if (DirectionalLightSettings.TryRead(levelRoot, out DirectionalLightSettings readSun))
            {
                _sun = readSun;

                // At the hour the level's authors placed the sun for, not a fixed one: the cycle lands
                // back on the authored direction only there.
                _dayNight.ResetForLevel(readSun, RenderCVars.TimeOfDay.Value);
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
                _dayNight.ResetForLevel(null, RenderCVars.TimeOfDay.Value);
                _logger.Information("Light", "The level declares no Dirlight; the modern path will light it with a default sun.");
            }

            if (_sky.HasSky)
            {
                _scheduler.ClearColour = new Bgra32(
                    (byte)Math.Clamp((int)(_sky.FogColour.Z * 255f), 0, 255),
                    (byte)Math.Clamp((int)(_sky.FogColour.Y * 255f), 0, 255),
                    (byte)Math.Clamp((int)(_sky.FogColour.X * 255f), 0, 255),
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
        if (_lampsMissionId != id || !promoted)
        {
            ApplyLamps(
                RenderCVars.GlobalIlluminationLamps.Value != 0
                    ? EmissiveLightProposer.Propose(_scene, _resources, _lampManifest, id, _logger)
                    : EmissiveLightProposer.Proposal.None,
                id);
        }

        // The surfaces, now that the sun and the fixtures they are lit by are both known. Captures the
        // atlas and lights it in one step -- see DesktopGame.RealtimeGi.cs, which is also what the render
        // loop calls when the lighting model is switched on mid-level.
        EnsureSurfaceCache();

        // The player stands where the camera looks from, dropped onto whatever is under it. Terrain is
        // optional: Mission 14 is entirely inside placed building/tunnel models, whose collision floors
        // are enough for placement and movement even though its terrain files are not live data.
        _playerTerrain = loaded.Terrain is { IsUsable: true } standable ? standable : null;
        _worldTrace = _worldSolids is { } liveSolids
            ? _playerTerrain is { } liveTerrain
                ? WorldTrace.Nearest(liveSolids.Tracer(), TerrainRayTrace.Caster(liveTerrain))
                : liveSolids.Tracer()
            : null;

        // What the impact effects trace against, built here rather than beside the security cameras
        // because the terrain is only known now: a blood droplet lands on the ground at least as often
        // as it hits a wall, and the splash behind a body needs whatever is three metres past it.
        //
        // The terrain leaf reports no material row, so a hit on open ground comes back as Air and marks
        // nothing -- which would leave the ground clean of both bullet holes and blood, where retail's
        // Ground row names hole_concrete1.tex. The level's own terrain row is substituted, which is the
        // same row the footsteps and the player's material lookup read.
        _effectWalls = _worldTrace is { } effectTrace
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
        _player = null;
        _playerMode = false;

        // A fresh mission is a fresh life: the last one's death, its red wash and its death camera do not
        // carry over.
        _playerHealth = new(HumanHitZoneTable.Player, _playerMaximumHealth)
        {
            DamageScale = HumanHealth.DifficultyDamageScale(_playerDifficulty),
        };
        _playerDead = false;
        _damageFlash = new();
        _flashbangScreenFlash = new();
        _deathCamera.Reset();

        EndStage("the terrain, sky, sun, grade and camera");

        {
            PlayerCamera candidate = new(_logger);

            // 000.iff, kept so the player's third-person body can be built from the same set it walks on
            // (HumanPlayerBody: the body is skeleton 0, and this is skeleton 0's set). Walking is the
            // animation's own root motion, so without it the player can stand and fall but not walk.
            //
            // Read once for the session rather than per mission: it lives under common/, so it is the
            // same 1.5 MB of IFF for all fourteen, and parsing it again on the render thread every time
            // a mission started was most of that step's cost.
            ReadSharedAnimations();
            Formats.Animation.BoneAnimationSet? playerBodySet = _playerBodyAnimations;
            if (playerBodySet is not null)
            {
                candidate.UseAnimations(playerBodySet);
            }

            // The walls. Terrain alone gives the player ground to stand on but nothing to stop it
            // walking through crates and buildings; the solidity is in each model's own HSMC mesh.
            // The index is the one built above for the soldiers' ground probe.
            candidate.UseWalls(solids);
            _logger.Information(
                "Player",
                $"{solids.Count} placed object(s) are solid, {solids.TriangleCount} collision triangle(s).");

            // The grenade's own cast INCLUDES the terrain: the body meets the ground through its
            // ground query, but a grenade has no such query and would fall through the world.
            candidate.GrenadeWalls = _playerTerrain is { } grenadeGround
                ? WorldTrace.AsRayCast(WorldTrace.Nearest(solids.Tracer(), TerrainRayTrace.Caster(grenadeGround)))
                : solids.Caster();
            candidate.GrenadeTrace = _playerTerrain is { } grenadeTraceGround
                ? WorldTrace.Nearest(solids.Tracer(), TerrainRayTrace.Caster(grenadeTraceGround))
                : solids.Tracer();

            // The ladders the level attaches to its buildings, so the player can climb them (E to mount,
            // W/S up and down, E held to slide).
            if (_scene is not null)
            {
                candidate.UseLadders(_scene.Ladders);
                _logger.Information("Player", $"{_scene.Ladders.Count} ladder(s) to climb.");
            }
            if (_interactions is { } interactions)
            {
                candidate.UseInteractions(interactions);
                candidate.TaskActivated += id => _soldiers?.ActivateTask(id);
                candidate.ElectrifiedFenceTouched += fence => ResolvePlayerElectricFence(candidate, fence);
                _logger.Information("Player", $"{interactions.Items.Count} direct world interaction(s).");
            }
            if (_genericPickups is { } genericPickups)
            {
                candidate.UseGenericPickups(genericPickups);
                candidate.GenericPickedUp += PlayGenericPickupSound;
                _logger.Information("Player", $"{genericPickups.Items.Count} generic pickup(s) available.");
            }

            // ORDER MATTERS, and it is the original's. WEAPON_ID_* and AMMO_ID_* are pattern-registered
            // symbols that get their values as the two tables are read, so the rosters have to load
            // before humanplayer.qvm runs -- otherwise every id in its weapon cycle is a placeholder
            // and the cycle names weapons nobody carries. section 8.
            WeaponTypeRegistry weaponRoster = new(_logger);
            AmmoTypeRegistry ammoTable = new(_logger);
            GameMaterialRegistry materialTable = new(_logger);
            try
            {
                weaponRoster.Register(_loop.Natives);
                weaponRoster.Load(_fileSystem, _resources, _loop.Natives);
                WeaponConfigRegistry.RegisterTypes(_loop.Types);
                WeaponConfigRegistry weaponConfig = new(_logger);
                weaponConfig.Load(_resources, _loop.Natives, _loop.Tasks);
                weaponConfig.AttachTo(weaponRoster);
                ammoTable.Register(_loop.Natives);
                ammoTable.Load(_fileSystem, _resources, _loop.Natives);

                // Shared with the guard-round handler: the ammunition record is what colours a
                // ballistic streak, exactly as it colours the retail tracer.
                _ammoTypes = ammoTable;

                // What every surface in the world is made of, and therefore what a round leaves on it.
                materialTable.Register(_loop.Natives);
                if (_fileSystem.Exists(GameMaterialRegistry.ScriptResource))
                {
                    _resources.Register(
                        GameMaterialRegistry.ScriptResource,
                        _fileSystem.ReadAllBytes(GameMaterialRegistry.ScriptResource));
                }
                else
                {
                    _logger.Warning("Material", $"No material table at {GameMaterialRegistry.ScriptResource}.");
                }

                int materialCount = materialTable.Load(_resources, _loop.Natives);
                candidate.UseMaterials(materialTable);
                _soldiers?.UseMaterials(materialTable);
                _worldMaterials = materialTable;
                _logger.Information("Material", $"Read {materialCount} game material(s).");

                // What the open ground is made of. The terrain declares it the same way a mesh face
                // does -- see FootstepSounds.TerrainGameMaterialIndex -- and it is Snow in the Arctic
                // missions rather than Ground, which is the whole difference in how they sound.
                int? terrainRow = loaded.Terrain is { } terrainData
                    ? FootstepSounds.TerrainGameMaterialIndex(terrainData.Materials)
                    : null;
                candidate.TerrainGameMaterialIndex = terrainRow;
                _effectTerrainRow = terrainRow;
                if (_soldiers is not null)
                {
                    _soldiers.TerrainGameMaterialIndex = terrainRow;
                }

                if (terrainRow is int row && materialTable.TryGet(row, out GameMaterial groundRow))
                {
                    _logger.Information("Material", $"Terrain walks on '{groundRow.Name}' (row {row}).");
                }

                _gunPickups = GunPickupRegistry.Build(levelRoot, weaponRoster, ammoTable);
                GunPickupRegistry gunPickups = _gunPickups;
                candidate.UseGunPickups(gunPickups);
                candidate.GunPickedUp += pickup =>
                {
                    PlayGunPickupSound(pickup);
                    string key = $"PICKEDUPWEAPON{pickup.Weapon.DisplayName}";
                    _missionObjectives?.ShowTransientMessage(missionText.Get(key));
                };
                _soldiers?.UseDroppedWeaponPickups(gunPickups);
                _soldiers?.SetWeaponDropSink((weapon, position, orientation) =>
                {
                    GunPickup pickup = gunPickups.Spawn(position, orientation, weapon);
                    _gunPickupRenderer?.Add(loaded, pickup);
                });
                if (_gunPickups.Items.Count > 0)
                {
                    _logger.Information("Weapon", $"{_gunPickups.Items.Count} gun pickup(s) available.");
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException)
            {
                _logger.Warning("Weapon", $"Could not read the weapon or ammunition tables: {ex.Message}");
            }

            EndStage("the player's animation sets, walls, ladders and the weapon tables");

            // The guards' weapons, which have to wait for the roster: a soldier's gun is an authored
            // Gun child task naming a WEAPON_ID_*, and only the table knows which model that draws.
            _soldiers?.Arm(loaded, weaponRoster);
            EndStage("the guards' weapon models");

            // The player's tuning is not in the executable: humanplayer.qvm is the whole of it, and it
            // is what says how high a jump goes and how far a step travels.
            try
            {
                HumanPlayerConfig config = new();
                config.Register(_loop.Natives);
                config.Load(_resources, _loop.Natives);

                if (config.PlaceholderIds.Count > 0)
                {
                    _logger.Warning(
                        "Player",
                        $"humanplayer.qvm left {config.PlaceholderIds.Count} id(s) unresolved; the weapon "
                            + "cycle may name weapons that do not exist.");
                }

                if (config.Parameters is { } tuning)
                {
                    candidate.UseParameters(tuning);

                    // Argument 7 is the player's maximum health (Human+600, 0x410370's overwrite) --
                    // 3.0 in retail, and the single number that sets the player's time-to-kill.
                    _playerMaximumHealth = tuning.MaximumHealth;

                    // Argument 8 is the dose one powered fence contact adds (0x412F80's fmul against
                    // the human's damage scale) -- 0.5 in retail. Read it for the same reason argument
                    // 7 is read: a retuned humanplayer.qvm owns its own shock, not the shipped number.
                    _electricFenceDose = tuning.ElectricFenceDose;
                    _playerHealth = new(HumanHitZoneTable.Player, _playerMaximumHealth)
                    {
                        DamageScale = HumanHealth.DifficultyDamageScale(_playerDifficulty),
                    };
                }
                else
                {
                    _logger.Warning("Player", "humanplayer.qvm published no parameters; using the retail defaults.");
                }

                // The same script says which weapon starts in hand, and it is not a guess any more:
                // 0x40F760's cycle index begins at -1, so the first update lands on entry 0 of
                // DefineHumanPlayerWeaponCycle and steps on until it finds one you carry. The shipped
                // list starts with the knife. section 16.7.
                candidate.UseWeaponCycle(config.WeaponCycle);

                // And the nine categories the number keys select — 0x411000 reads exactly this table.
                candidate.Weapons?.UseCategories(config.Categories);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException)
            {
                _logger.Warning("Player", $"Could not run humanplayer.qvm: {ex.Message}");
            }

            EndStage("humanplayer.qvm");

            // PlayerBody resolves the landing on the tick after contact, just as retail does. Its
            // default health exists for standalone physics use; the desktop must give it the exact
            // accumulator the HUD, guard hits and death state already share. The sink crosses the
            // presentation boundary for sound, the red wash, AI hearing and lethal-fall death.
            candidate.UseHealth(_playerHealth);
            candidate.Body.SetFallImpactSink(impact => ResolvePlayerFallImpact(candidate, impact));

            // The first-person rig. Every Human builds one in its constructor and gives it skeleton 5
            // and the model 005_01_1 (0x45EB30, 0x464660); it is what the held weapon hangs off, so
            // without it there is nothing to draw a gun on.
            if (_viewRigAnimations is { } viewRigSet)
            {
                candidate.UseViewRig(viewRigSet);

                // The arms are the other half of what 0x4644A0 draws, and the half that needs CPU
                // skinning: 005_01_1 is mesh format 1.
                _arms?.Dispose();
                _arms = new ViewModelRenderer(_device, _resources, _logger);
                if (!_arms.Load(loaded))
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
            _impactParticles?.Dispose();
            _impactParticles = null;
            _retailImpacts?.Dispose();
            _retailImpacts = null;
            _impactEffects.Clear();
            _explosions?.Dispose();
            _explosions = null;
            _missileSmoke?.Dispose();
            _missileSmoke = null;
            _heldWeapon = null;

            if (PlayerLoadout.TryRead(levelRoot, out PlayerLoadout? loadout) && loadout is not null)
            {
                _logger.Information("Player", $"Carrying {loadout}.");
                _heldWeapon = CarryLoadout(candidate, loadout, loaded, weaponRoster, ammoTable);
                _loadedMissionForWeapon = loaded;

                // The player's rounds are offered to the soldiers, which own their own health.
                if (_soldiers is { } guards)
                {
                    candidate.Humans = guards.Shooter();
                    candidate.HumanProbes = guards.Probe();
                    HumanProximityCast soldierProximity = guards.ProximityProbe();
                    candidate.HumanProximityTargets = (Vector3d center, float radiusUnits, out HumanProximityTarget target) =>
                    {
                        bool found = soldierProximity(center, radiusUnits, out target);
                        if (!_playerHealth.IsDead)
                        {
                            double distance = Vector3d.Distance(center, candidate.Body.Position);
                            if (distance <= radiusUnits && (!found || distance < target.Distance))
                            {
                                target = new HumanProximityTarget(candidate.Body.Position, distance);
                                found = true;
                            }
                        }

                        return found;
                    };
                    candidate.GroundImpact = impact => guards.NotifyGroundImpact(impact, candidate.Body.Position);

                    // A glancing round whizzes where it turned, on the ricochet sound's own limit --
                    // 0x47BD7B sounds it there directly rather than through the material's window.
                    candidate.Deflected = PlayRicochetSound;

                    // One grenade pool for both throwers: the player's own and every guard's lob fly,
                    // bounce and detonate through the same simulation and the same drain.
                    guards.SetGrenades(candidate.Grenades);
                }

                // The in-game HUD owns its own archive, exactly as StatusScreen's constructor does.
                _marks?.Dispose();
                _marks = new BulletMarkRenderer(_device, _resources, _logger);

                // The ballistic streaks, beside the marks they end in. Cheap to have around: with
                // g_ballistics off nothing is ever spawned and Draw returns at the count check.
                _tracers?.Dispose();
                _tracers = new TracerTrailRenderer(_device);

                // And the bursts where rounds land. Also cheap when off: r_impact_particles 0
                // spawns nothing, and an empty pool returns at the count check.
                _impactParticles?.Dispose();
                _impactParticles = new ImpactParticleRenderer(_device, _resources, _fileSystem);
                if (!_impactParticles.Load(loaded))
                {
                    _logger.Warning("Impact", "The retail IGI1 explosion model '1009_01_1' was not bound; using the sprite fallback.");
                }

                // The original's own impact effects: concrete1.spr dust, the material's own chip model,
                // and blood1/blood2 for a body. The runtime keeps the motion; this only draws it.
                _retailImpacts?.Dispose();
                _retailImpacts = new RetailImpactRenderer(_device, _resources, _fileSystem);
                int impactModels = _retailImpacts.Load(loaded);
                _impactEffects.Clear();
                _impactEffects.SetMistFrameCount(_retailImpacts.MistFrameCount);
                if (!_retailImpacts.SpritesAvailable)
                {
                    _logger.Warning(
                        "Impact",
                        "concrete1.spr, blood1.spr or blood2.spr did not resolve; the original's "
                            + "impact dust and blood will not draw.");
                }
                else
                {
                    _logger.Debug(
                        "Impact",
                        $"Retail impact effects bound: {impactModels} chip model(s), "
                            + $"{_retailImpacts.MistFrameCount} blood frame(s).");
                }

                // The retail 1009_01_1 fireball, shared by every blast in the level. Loads its real
                // texture lazily on first blast; falls back to the procedural burst without it.
                _explosions?.Dispose();
                _explosions = new ExplosionEffectRenderer(_device, _fileSystem, _logger);

                // The retail smoke2.spr trail behind rockets and the grey column a blast leaves.
                _missileSmoke?.Dispose();
                _missileSmoke = new SmokeSpriteRenderer(_device, _fileSystem, _logger);

                _hud?.Dispose();
                _hud = new StatusScreenRenderer(_device, _logger);
                if (!_hud.Load(_fileSystem))
                {
                    _hud.Dispose();
                    _hud = null;
                }

                _remasteredHud?.Dispose();
                _remasteredHud = new RemasteredHudRenderer(_device, _logger);
                if (!_remasteredHud.Load())
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

            if (_gunPickups is { } pickups)
            {
                _gunPickupRenderer = new GunPickupRenderer(_device, _resources, _magic, _logger)
                {
                    Materials = _materials,
                };
                _gunPickupRenderer.Load(loaded, pickups);
            }

            // The grenades in the air, drawn with their own retail models. Loaded beside the
            // pickups because it is the same model machinery at a transform the simulation owns.
            _grenadeRenderer?.Dispose();
            _grenadeRenderer = new GrenadeRenderer(_device, _resources, _magic, _logger);
            _grenadeRenderer.Load(loaded);
            _missileRenderer?.Dispose();
            _missileRenderer = new MissileRenderer(_device, _resources, _magic, _logger);
            _missileRenderer.Load(loaded);

            EndStage("the loadout, the bullet marks and the HUD");

            // The level places its own player: every mission's objects.qvm declares a HumanPlayer task
            // with a Position and a Gamma. Standing there is standing where the game starts you.
            // Without a declared spawn the player is stood where the opening camera looks from, so that
            // fallback needs a camera to read; a level with neither is simply not placed, which the
            // warning below already covers.
            bool placed = PlayerSpawn.TryFindGameplay(levelRoot, out PlayerSpawnPoint spawn)
                ? candidate.PlaceAt(_playerTerrain, spawn, solids.PlayerRegistrationCaster())
                : _camera is { } opening && candidate.Place(_playerTerrain, opening.Position, _options.CameraYaw);

            if (placed)
            {
                _player = candidate;

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

        // The menus are done; without dropping the renderer the last screen would keep drawing over
        // the world.
        _renderer = null;

        // The menu's backdrop is not needed once a real mission is up. On a cold start that releases the
        // previewed level's packs outright. On a promotion it releases nothing: the preview IS this
        // mission now, and its registry and file system are reachable through `_resources` and through
        // the renderers that were built against them — dropping this reference only says the level has
        // stopped being scenery.
        _backdrop = null;
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
        // See DesktopGame.MissionWarmUp.cs.
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
        _activeLevelRoot = preview.LevelRoot;
        _activeMission = chosen;
        _missionResultHandled = false;
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

    /// <summary>
    /// The mid-swing doorway ray: whether a human currently stands in the opening between the two
    /// doorway endpoints.
    /// </summary>
    /// <param name="from">One end of the doorway segment, in metres.</param>
    /// <param name="to">The other end of the doorway segment, in metres.</param>
    /// <remarks>
    /// This is the player body and every living AI soldier, tested against the doorway segment the
    /// door spans. The retail ray (0x446D10 at 0x447302) casts across the doorway at roughly body
    /// height; we test each human's feet against the segment with a generous half-metre radius, which
    /// finds a body standing in the opening whether it is at waist, chest or head.
    /// </remarks>
    private bool DoorwayIsBlockedByHuman(Vector3d from, Vector3d to)
    {
        if (HumanStandsInSegment(_player?.Body.Position, from, to))
        {
            return true;
        }

        if (_soldiers is { } soldiers)
        {
            foreach (Vector3d feet in soldiers.MapContacts())
            {
                if (HumanStandsInSegment(feet, from, to))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether a human's feet (world units) stand within a half metre of the doorway segment (metres).</summary>
    private static bool HumanStandsInSegment(Vector3d? feetWorld, Vector3d from, Vector3d to)
    {
        if (feetWorld is not { } feet)
        {
            return false;
        }

        Vector3d point = new(
            feet.X / WorldScene.UnitsPerMetre,
            feet.Y / WorldScene.UnitsPerMetre,
            feet.Z / WorldScene.UnitsPerMetre);
        Vector3d closest = ClosestPointOnSegment(from, to, point);
        return Vector3d.Distance(closest, point) <= 0.5;
    }

    private static Vector3d ClosestPointOnSegment(Vector3d start, Vector3d end, Vector3d point)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double dz = end.Z - start.Z;
        double lengthSquared = (dx * dx) + (dy * dy) + (dz * dz);
        if (lengthSquared <= 0.0)
        {
            return start;
        }

        double t = (((point.X - start.X) * dx)
            + ((point.Y - start.Y) * dy)
            + ((point.Z - start.Z) * dz)) / lengthSquared;
        t = Math.Clamp(t, 0.0, 1.0);
        return new Vector3d(start.X + (dx * t), start.Y + (dy * t), start.Z + (dz * t));
    }

    /// <summary>Builds the level's top-down map of what the solid world stands over, and binds it.</summary>
    /// <param name="scene">The mission's placed objects.</param>
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
    private void BuildShelterField(WorldScene scene)
    {
        ReleaseShelterField();

        long started = _loop.TimeSource.Milliseconds;
        _shelterField = ShelterHeightField.Build(scene.Placements);
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
                TextureAddressMode.Clamp),
            texels);

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
        // First, because both are worker threads tracing the level that is going and they hold the whole
        // of what they trace: the lightmap bake's texel maps, triangles and BVH run to a few hundred
        // megabytes, and nothing here released them. A bake was only ever cancelled by the NEXT one
        // starting, so leaving a level left every core path-tracing a level nobody was in -- which is
        // what the new level then had to load against -- and a session that entered a handful of missions
        // carried a handful of those at once. The heap stood at one and a half gigabytes of lightmap
        // texels alone by the sixth.
        _probeBake?.Cancel();
        _probeBake = null;
        _lightmapBake?.Cancel();
        _lightmapBake = null;

        _computerOpen = false;
        _computerFlight.Reset();
        _missionFailureTicks = 0;
        _missionFailedMenu = false;
        _missionResultHandled = false;
        _mapComputer?.Dispose();
        _mapComputer = null;
        _mapComputerDevice?.Dispose();
        _mapComputerDevice = null;
        _tacticalComputer?.Dispose();
        _tacticalComputer = null;
        _messages?.Dispose();
        _messages = null;
        _computerHighlights = null;
        _missionObjectives = null;
        _cutscenes = null;
        _securityCameras = null;
        _cameraBeepVoices.Clear();
        _cameraMoveVoices.Clear();
        StopCutsceneSounds();
        _activeMission = null;

        // The level's own sun goes with the level. It is read on load only when it is not already set --
        // which is how a promoted backdrop hands its own in -- so a sun left standing here was taken for
        // one that had been handed in, and the next mission was lit and BAKED by the one before it. The
        // backdrop the menu puts up next sets its own in AdoptPreview, so nothing that needs one is left
        // without.
        _sun = null;
        _effectiveSun = null;

        // Before a single mesh is destroyed. The backend has assembled its own copy of the level from
        // the frames it drew -- the traceable world, and the material records a hit is looked up in --
        // and destroying the meshes one by one tells it each has gone without telling it the level has.
        // Left to accumulate, the records ran up to their ceiling over half a dozen mission entries and
        // the traced world went on holding structures nothing would place again.
        _device.ReleaseWorld();

        _sky?.Dispose();
        _terrain?.Dispose();
        _world?.Dispose();
        _soldiers?.Dispose();
        _backdropSoldiers?.Dispose();
        _heldWeapon?.Dispose();
        _gunPickupRenderer?.Dispose();
        _hud?.Dispose();
        _remasteredHud?.Dispose();
        _debugOverlay?.Dispose();
        _marks?.Dispose();
        _tracers?.Dispose();
        _impactParticles?.Dispose();
        _retailImpacts?.Dispose();
        _explosions?.Dispose();
        _missileSmoke?.Dispose();
        _cameraStatusLights?.Dispose();
        _grenadeRenderer?.Dispose();
        _missileRenderer?.Dispose();
        _arms?.Dispose();
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
        _backdropSoldiers = null;
        _heldWeapon = null;
        _gunPickupRenderer = null;
        _hud = null;
        _remasteredHud = null;
        _debugOverlay = null;
        _marks = null;
        _tracers = null;
        _impactParticles = null;
        _retailImpacts = null;
        _impactEffects.Clear();
        _explosions = null;
        _missileSmoke = null;
        _cameraStatusLights = null;
        _grenadeRenderer = null;
        _missileRenderer = null;
        _arms = null;
        _player = null;
        _playerMode = false;
        _scene = null;
        _playerTerrain = null;
        _effectWalls = null;
        _worldSolids = null;
        _destructibles = null;
        _worldMaterials = null;
        _doors = null;
        _elevators = null;
        _interactions = null;
        _gunPickups = null;
        _genericPickups = null;
        _loadedMissionForWeapon = null;

        if (_activeLevelRoot is not null && !_activeLevelRoot.IsDeleted)
        {
            _loop.Tasks.Delete(_activeLevelRoot);
        }
        _activeLevelRoot = null;

        // Back to the front end, so back to the menu theme. Every voice the level started goes with it:
        // an ambience bed outliving its level would follow the player into the menus.
        _sounds?.StopAll();
        _music?.Play(MusicPlayer.MenuMusic);
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
        LightmapBaker.BakeDirectAndSky(targets, cast, sunDirection, sunColour, skyColour, hemisphereRays: 64);

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
        LightmapBaker.BakeBounces(targets, triangles, bvh, skyColour, passes: 4, hemisphereRays: 64);

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

    // Puts a weapon in the player's hands and loads the model its roster entry names.
    //
    // Hands the player everything the mission declares, with the ammunition each weapon draws from.
    // WHICH of them ends up in hand is no longer decided here: the weapon cycler's index starts at -1
    // and the first tick lands on entry 0 of DefineHumanPlayerWeaponCycle, stepping on until it finds
    // one that is carried. That retires the divergence this function used to carry -- see
    // the original. The renderer's model follows whatever ends up in hand.
    private HeldWeaponRenderer? CarryLoadout(
        PlayerCamera candidate,
        PlayerLoadout loadout,
        LoadedMission loadedMission,
        WeaponTypeRegistry roster,
        AmmoTypeRegistry ammunition)
    {
        if (candidate.View is null || loadout.Weapons.Count == 0)
        {
            return null;
        }

        foreach (LoadoutWeapon declared in loadout.Weapons)
        {
            if (!roster.TryGetByScriptId(declared.WeaponId, out WeaponType weapon))
            {
                _logger.Warning("Weapon", $"{declared.WeaponId} is not in the roster; it is not carried.");
                continue;
            }

            // A weapon finds its ammunition by naming a Calibre ID, and -1 -- the knife's -- names
            // none at all, which is exactly why a knife never runs out.
            bool hasAmmo = ammunition.TryGetByCalibre(weapon.CalibreId, out AmmoType ammo);
            candidate.Carry(weapon, hasAmmo ? ammo : null);
        }

        foreach (LoadoutAmmo declared in loadout.Ammunition)
        {
            AmmoType? pool = ammunition.Types.FirstOrDefault(a =>
                string.Equals(a.ScriptId, declared.AmmoId, StringComparison.Ordinal));

            if (pool is not null)
            {
                candidate.Weapons?.Ammunition.Add(pool.AmmoId, declared.Count);
                if (string.Equals(declared.AmmoId, "AMMO_ID_M203", StringComparison.Ordinal))
                {
                    candidate.Weapons?.SetM203Ammunition(pool);
                }
            }
        }

        return new HeldWeaponRenderer(_device, _resources, _magic, _logger) { Materials = _materials };
    }

    /// <summary>Puts the player through the whole death sequence, whatever killed them.</summary>
    /// <param name="from">Where the killing thing came from — the shooter's muzzle, or the blast.</param>
    /// <param name="zone">The zone the killing damage landed on.</param>
    /// <param name="bone">The bone the round went through, or <c>-1</c> when nothing so precise.</param>
    /// <param name="category">The killing weapon's category, which picks the fall's family.</param>
    /// <remarks>
    /// Extracted from the guard-round handler so that EVERYTHING lethal runs it — a grenade that
    /// zeroes the health must kill exactly the way a bullet does, or the player stands at zero
    /// health forever, which is precisely the bug this fixed.
    /// </remarks>
    private void BeginPlayerDeath(Vector3d from, int zone, int bone, WeaponCategory category)
    {
        if (_player is null || _playerDead)
        {
            return;
        }

        // Locomotion state 47 (0x40E560): the clip comes from the LAST ZONE STRUCK's row in the
        // player's own hit-zone table, one of its two reactions by rand(2). The player stops taking
        // input from here on -- it is a state, not a message.
        _playerDead = true;
        _player.ResetOptics();
        _player.StopWeaponsForDeath();
        StopBinocularTransitionSound();

        // The player receives no further simulation ticks after death, so the weapon sound player
        // cannot observe a quiet tick and retire an automatic loop on its own.
        _weaponSounds?.Reset();
        int fall = HumanPlayerDeath.Choose(zone, HumanHitZoneTable.Player, _shotRandom.Next(2));

        // The lethal hit has already written the retail corner flash. Do not replace it with a uniform
        // full-screen wash here: 0x412440 writes the flash as part of damage receipt, before it changes
        // the locomotion state to death.

        // Which way the body is thrown: the facing whose backward axis points away from the killer,
        // exactly as the soldier death faces the incoming round (SoldierDeath.TurnToTheShot).
        double throwX = _player.Body.Position.X - from.X;
        double throwY = _player.Body.Position.Y - from.Y;
        double throwLength = Math.Sqrt((throwX * throwX) + (throwY * throwY));
        float throwFacing = throwLength > 1e-6
            ? (float)Math.Atan2(throwX / throwLength, -(throwY / throwLength))
            : _player.Body.Yaw;

        _soldiers?.KillPlayerBody(
            fall,
            _player.Body.Position,
            throwFacing,
            bone,
            new Vector3d(
                _player.Body.Position.X - from.X,
                _player.Body.Position.Y - from.Y,
                _player.Body.Position.Z - from.Z),
            category,
            new Vector3d(
                _player.Body.Velocity.X,
                _player.Body.Velocity.Y,
                _player.Body.Velocity.Z));

        if (_worldSolids is { } deathSolids)
        {
            WorldRayCast deathCaster = deathSolids.Caster();
            _deathCamera.Begin(
                _player.Body.Position,
                throwFacing,
                (f, t, out h) =>
                {
                    bool blocked = deathCaster(f, t, out WorldRayHit wall);
                    h = blocked ? wall.Position : default;
                    return blocked;
                });
        }

        // And the death sound -- state 47's own sub_4638A0(human, "player_death_", 3).
        PlayDeathSound(HumanPlayerDeath.SoundPrefix, HumanPlayerDeath.SoundVariants, null);
    }

    /// <summary>Routes a resolved body landing into the desktop-owned presentation and death state.</summary>
    private void ResolvePlayerFallImpact(PlayerCamera player, HumanFallImpact impact)
    {
        // 0x408460 arms the camera's dip on every ground contact, before it has decided whether the
        // landing hurt: a step off a crate drops the view a little, a long drop drops it the full
        // quarter-metre. 0x4659B0 is two stores, so this is unconditional and cannot fail.
        player.InstallLandingViewKick(impact.ViewKickUnits);

        // The ground's own landing sound, which is what every harmless drop sounds like. Both airborne
        // states play it on contact from the struck material, independently of the fall-damage cue
        // below -- a hurting fall is heard twice, once as the surface and once as the player.
        if (player.TakeLandingSound() is { } landing)
        {
            PlayLandingSound(landing);
        }

        // State 35 names one of these three definitions only when damage is non-zero. Like the player's
        // own hit and death sounds it belongs to the listener, so it is flat rather than world-panned.
        if (!string.IsNullOrEmpty(impact.SoundName))
        {
            _sounds?.PlayDefinition(impact.SoundName, priority: 250);
        }

        // Every ground contact posts type 11. A safe landing carries five metres; a damaging one ten.
        _soldiers?.NotifyGroundImpact(
            player.Body.Position,
            player.Body.Position,
            impact.HearingRadiusUnits);

        if (impact.Damage <= 0f)
        {
            return;
        }

        // PlayerBody has already added the direct, difficulty-independent dose to the shared health.
        // Raise the desktop wash that the render pass actually draws rather than the body's standalone one.
        _damageFlash.Hit(Vector3f.Zero, _soundRandom.NextSingle());
        _logger.Information(
            "Player",
            $"Fall impact {impact.SpeedMetresPerSecond:F2} m/s dealt {impact.Damage:F3} "
                + $"({_playerHealth.RemainingFraction:P0} left), sound '{impact.SoundName}'.");

        if (_playerHealth.IsDead)
        {
            // Zone 13 is the retail fall bucket. With no external attacker the source is the feet,
            // leaving the death fall aligned to the player's current facing.
            BeginPlayerDeath(
                player.Body.Position,
                HumanDamage.FrontZone,
                bone: -1,
                WeaponCategory.Gun);
        }
    }

    /// <summary>Applies one native electric-fence shock after the player's 45-tick guard permits it.</summary>
    private void ResolvePlayerElectricFence(PlayerCamera player, WorldInteraction fence)
    {
        if (_playerHealth.IsDead)
        {
            return;
        }

        float healthBefore = _playerHealth.RemainingFraction;
        _logger.Information(
            "Player",
            $"[fence] contact task={fence.Task.Id} powered={(fence.IsElectrified ? "true" : "false")} "
                + $"healthBefore={healthBefore:P0} position=({player.Body.Position.X:F0},"
                + $"{player.Body.Position.Y:F0},{player.Body.Position.Z:F0})");

        ElectricFenceShock shock = ElectricFenceContact.Shock(_playerHealth, _electricFenceDose);
        float dealt = shock.Damage;
        if (dealt <= 0f)
        {
            _logger.Warning(
                "Player",
                $"[fence] contact produced no damage task={fence.Task.Id} "
                    + $"health={_playerHealth.RemainingFraction:P0} hits={_playerHealth.Hits}.");
            return;
        }

        _damageFlash.Hit(Vector3f.Zero, _soundRandom.NextSingle());
        if (!shock.IsLethal)
        {
            // 0x412F80 selects this fence-specific cue after it has added the direct zone-13 dose.
            _sounds?.PlayDefinition("elfence_spark1", priority: 250);
        }

        _logger.Information(
            "Player",
            $"Electric fence shock dealt {dealt:F3} ({_playerHealth.RemainingFraction:P0} left).");

        if (shock.IsLethal)
        {
            BeginPlayerDeath(fence.Anchor, ElectricFenceContact.Zone, bone: -1, WeaponCategory.Gun);
        }
    }

    // Keeps the drawn model on whatever the cycle has put in hand. Called every frame; it only does
    // anything on the tick the weapon actually changed.
    private void FollowHeldWeapon(PlayerCamera candidate, HeldWeaponRenderer? held, LoadedMission loadedMission)
    {
        if (held is null || !candidate.WeaponChanged)
        {
            return;
        }

        candidate.AcknowledgeWeaponChange();

        if (candidate.HeldWeaponType is not { } weapon)
        {
            return;
        }

        if (held.Load(loadedMission, weapon.GunModel))
        {
            _logger.Information("Weapon", $"Holding {weapon.ScriptId} ('{weapon.GunModel}') on view bone "
                + $"{HumanAttachPoints.FirstPersonWeapon}.");
        }
    }

    private MenuManagerTask? FindMenuManager()
    {
        foreach (GameTask task in WalkTasks(_loop.Tasks.Root))
        {
            if (task is MenuManagerTask manager)
            {
                return manager;
            }
        }

        return null;
    }

    private static IEnumerable<GameTask> WalkTasks(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            yield return child;
            foreach (GameTask nested in WalkTasks(child))
            {
                yield return nested;
            }
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
}
