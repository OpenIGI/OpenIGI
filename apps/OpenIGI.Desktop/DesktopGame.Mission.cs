using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Audio;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.World;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
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
    private void StartMission(int id)
    {
        if (!_missions.TryGet(id, out MissionDescriptor? chosen))
        {
            _logger.Warning("Mission", $"Asked to start mission {id}, which is not registered.");
            return;
        }

        ReleasePlayedMission();

        _logger.Information("Mission", $"Starting {chosen}.");
        LevelTask levelRoot = (LevelTask)_loop.Tasks.New(_loop.Tasks.Root, _loop.Types.Get(EngineTaskTypes.Level));
        _activeLevelRoot = levelRoot;
        _activeMission = chosen;
        _missionResultHandled = false;
        _computerOpen = false;
        _computerFlight.Reset();
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

    /// <summary>Gets the along-track extent retail uses to space cars in a Train consist.</summary>
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
                return name is "MenuManager_SetEnabled" or "Game_CutsceneDelete"
                    ? QvmValue.FromInt32(1)
                    : null;
            },
            SetCutsceneSound,
            modelLength: name => CutsceneModelLength(loaded, name),
            modelPivot: name => CutsceneModelPivot(loaded, name),
            vehiclePhysics: _vehiclePhysics);
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
            world = new WorldRenderer(_device, _resources, _logger) { Materials = _materials, DetailMaps = _detailMaps };
            _world = world;
            solids = ObjectCollisionIndex.Build(scene.Placements);
            _worldSolids = solids;
            EndStage("the scene walk, the world renderer and the collision index");
        }
        world.Cutscenes = _cutscenes;

        // Rain is a gameplay effect, not part of the backdrop picture: it is configured fresh for every
        // mission — cold start or promoted — straight from the level's own objects.qvm (a RainEffect task
        // enabled by its Is Rain and Is Active flags), and whatever rain the previous mission left is
        // dropped first, exactly as the reference resets its rain at every level load. The same scene's
        // building placements also provide the shells the rain is suppressed inside.
        _rain?.Dispose();
        _rain = new RainRenderer(_device, _logger);
        _rain.ReadParams(levelRoot);
        _snow?.Dispose();
        _snow = new SnowRenderer(_device, _logger);
        _snow.ReadParams(levelRoot);
        ApplyWeatherSettings();
        _rainBuildingVolumes = RainRenderer.BuildBuildingVolumes(scene.Placements);

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
        _cutscenes?.SetTargetPoseResolver(_soldiers.CutscenePose);

        // Interactions are level systems, not AI systems: build them even when a mission has no usable
        // navigation graph. The same door objects are shared by player use, AI door hops and collision.
        _doors = DoorRegistry.Build(levelRoot);
        _interactions = WorldInteractionRegistry.Build(levelRoot, loaded, _doors, _loop.Variables);
        _soldiers.SetAiTaskActivator(id => _interactions?.ActivateFromAi(id));
        _soldiers.UseInteractions(_interactions);
        _soldiers.SetDoors(_doors, solids);

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
        world.Doors = _doors;
        if (_doors.Doors.Count > 0)
        {
            _logger.Information("World", $"{_doors.Doors.Count} door(s) available to players and AI.");
        }

        EndStage("the doors and the world interactions");

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
            string graphDirectory = Path.Combine(
                _gamePath, loaded.Descriptor.MissionDirectory, "graphs");
            Dictionary<int, GraphFile> graphs = [];
            if (Directory.Exists(graphDirectory))
            {
                foreach (string file in Directory.GetFiles(graphDirectory, "graph*.dat"))
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
                WorldRayCast levelCast = WorldTrace.AsRayCast(worldTrace);

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
                _soldiers.SetRoundHandler((from, at, probability, weapon) =>
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

                    // Resolve the guard's round against the player's OWN skinned body (limb hits do less,
                    // and the hitbox is the real body, not a fat cylinder). Only if the body is not loaded
                    // does it fall back to the cylinder-and-trunk stand-in.
                    HumanHit hit = default;
                    bool met = _soldiers is not null
                        && _soldiers.TryHitPlayerBody(from, to, feet, _player.Body.Yaw, out hit);
                    if (!met)
                    {
                        Vector3f facing = new(-MathF.Sin(_player.Body.Yaw), MathF.Cos(_player.Body.Yaw), 0f);
                        met = HumanHitTest.TryCast(from, to, feet, facing, pose: null, out hit);
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
                        }

                        // A guard's stray round pops a speaker exactly as the player's does -- the
                        // retail damage message never asked who fired.
                        DamageAlarmSpeakers(worldHit.Position, owner: blocker.Owner);

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
                        // Geometric miss -- aim lag against a moving target, or a running patrol's
                        // spray. The round flies on and lands where the world says.
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
                        MarkTheImpact();
                        return;
                    }

                    if (_playerHealth.IsDead)
                    {
                        // Already down. Nothing kills you twice, and without this the log filled with
                        // hundreds of "DEAD" lines while the guards kept shooting the corpse.
                        return;
                    }

                    _playerHealth.Apply(hit.Zone, weapon.DamageFactor);
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

        // The player stands where the camera looks from, dropped onto whatever is under it. A level
        // whose terrain is not live data has nothing to stand on, so the player stays unavailable and
        // F1 does nothing.
        _playerTerrain = loaded.Terrain is { IsUsable: true } standable ? standable : null;
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
        _deathCamera.Reset();

        EndStage("the terrain, sky, sun, grade and camera");

        if (_playerTerrain is not null)
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
                _logger.Information("Player", $"{interactions.Items.Count} direct world interaction(s).");
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
            candidate.Body.UseHealth(_playerHealth);
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
                    candidate.GroundImpact = impact => guards.NotifyGroundImpact(impact, candidate.Body.Position);

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
                _impactParticles = new ImpactParticleRenderer(_device);

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

            EndStage("the loadout, the bullet marks and the HUD");

            // The level places its own player: every mission's objects.qvm declares a HumanPlayer task
            // with a Position and a Gamma. Standing there is standing where the game starts you.
            // Without a declared spawn the player is stood where the opening camera looks from, so that
            // fallback needs a camera to read; a level with neither is simply not placed, which the
            // warning below already covers.
            bool placed = PlayerSpawn.TryFindGameplay(levelRoot, out PlayerSpawnPoint spawn)
                ? candidate.PlaceAt(_playerTerrain, spawn)
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

    private void ReleasePlayedMission()
    {
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

        _sky?.Dispose();
        _terrain?.Dispose();
        _world?.Dispose();
        _soldiers?.Dispose();
        _backdropSoldiers?.Dispose();
        _heldWeapon?.Dispose();
        _gunPickupRenderer?.Dispose();
        _hud?.Dispose();
        _remasteredHud?.Dispose();
        _marks?.Dispose();
        _tracers?.Dispose();
        _impactParticles?.Dispose();
        _cameraStatusLights?.Dispose();
        _grenadeRenderer?.Dispose();
        _arms?.Dispose();
        _sky = null;
        _rain?.Dispose();
        _rain = null;
        _snow?.Dispose();
        _snow = null;
        _rainBuildingVolumes = [];
        _terrain = null;
        _world = null;
        _soldiers = null;
        _backdropSoldiers = null;
        _heldWeapon = null;
        _gunPickupRenderer = null;
        _hud = null;
        _remasteredHud = null;
        _marks = null;
        _tracers = null;
        _impactParticles = null;
        _cameraStatusLights = null;
        _grenadeRenderer = null;
        _arms = null;
        _player = null;
        _playerMode = false;
        _scene = null;
        _playerTerrain = null;
        _worldSolids = null;
        _worldMaterials = null;
        _doors = null;
        _interactions = null;
        _gunPickups = null;
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
        _damageFlash.Hit();
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
}
