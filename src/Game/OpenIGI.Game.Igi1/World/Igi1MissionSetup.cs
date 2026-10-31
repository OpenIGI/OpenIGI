using OpenIGI.Game.Hosting;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Menus;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi1.World;

/// <summary>Brings up Project I.G.I.'s cutscene runtime and objective runtime for a loaded level.</summary>
/// <param name="mission">The mission's world, which keeps them.</param>
/// <param name="harm">What a guard's round does to the player.</param>
/// <param name="blasts">What a guard's round does to a speaker or a destructible.</param>
/// <param name="sounds">The soldiers' cries and the sound of a guard's round landing.</param>
/// <param name="audio">Where the doors, lifts and soldiers send their sounds.</param>
/// <param name="impacts">The material's own effects where a soldier's marched round lands.</param>
/// <param name="host">What plays and shows them.</param>
public sealed class Igi1MissionSetup(
    MissionWorld mission,
    Igi1PlayerHarm harm,
    Igi1Blasts blasts,
    Igi1HumanSounds sounds,
    Igi1MissionAudio audio,
    Igi1ImpactEffects impacts,
    IMissionSetupHost host) : IMissionSetup
{
    /// <inheritdoc/>
    public bool HasSoldiers => mission.Soldiers is not null;

    /// <summary>Makes the mission's text and its cutscene runtime, with the natives the level's cutscenes call.</summary>
    /// <param name="loaded">The mission's descriptor, packs and terrain.</param>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="walker">The level's walk.</param>
    /// <param name="objectives">
    /// The objective runtime being built, which is made after this one; the clear-messages native reads it so that a clear
    /// during construction cannot reach the previous mission's messages.
    /// </param>
    /// <returns>The mission's text.</returns>
    public MissionStringResolver BuildCutscenes(
        LoadedMission loaded,
        LevelTask levelRoot,
        LevelUpdateWalker walker,
        Func<MissionObjectiveRuntime?> objectives)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(walker);
        ArgumentNullException.ThrowIfNull(objectives);
        MenuStringTable missionStrings = new(host.Resources);

        // Mission text is addressed by its own key, not by the menu table's LOCAL:<language>/<key> —
        // see MissionStringResolver. Everything that resolves mission data goes through this one.
        MissionStringResolver missionText = new(host.Resources, missionStrings);

        // The objective runtime owns the message slots but is built after the cutscene runtime, which
        // needs it for StatusMessageScreen_ClearMessages. The native reads the build's rather than the
        // field so a clear during construction cannot reach the previous mission's messages.
        mission.Cutscenes = new CutsceneRuntime(
            levelRoot,
            host.Game.Variables,
            (name, arguments) =>
            {
                if (host.HostNative(name, arguments) is { } answered)
                {
                    return answered;
                }

                if (name == "MenuManager_SetEnabled")
                {
                    // Cutscene start/stop expressions own this native in retail. The menu manager
                    // lives in the shared loop task tree, so update its live script context rather
                    // than returning success while leaving input enabled during cinematics.
                    if (arguments.Count >= 1)
                    {
                        mission.MenusEnabled = arguments.GetBoolean(0);
                        if (host.Game.Tasks.Root.Descendants().OfType<MenuManagerTask>().FirstOrDefault() is { ScriptContext: { } context })
                        {
                            context.IsEnabled = mission.MenusEnabled;
                        }
                    }

                    return QvmValue.FromInt32(1);
                }

                if (name == "StatusMessageScreen_ClearMessages")
                {
                    // 0x485AD0: clears every slot and returns true.
                    objectives()?.ClearMessages();
                    return QvmValue.FromInt32(1);
                }

                if (AreaOccupancy.TryHandleCutsceneCleanupNative(name, mission.ClearTransientProjectiles) is { } cleanup)
                {
                    return cleanup;
                }

                return null;
            },
            host.CutsceneSound,
            modelLength: name => CutsceneModelLength(loaded, name),
            modelPivot: name => CutsceneModelPivot(loaded, name),
            vehiclePhysics: mission.VehiclePhysics,
            terrain: loaded.Terrain,
            logger: host.Logger,
            vehicleObstacleHit: (task, origin, end) =>
            {
                if (mission.Solids?.TryCastIgnoring(
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
                mission.Doors?.Doors.FirstOrDefault(door => ReferenceEquals(door.Task, obstacle))?.State.IsFullyOpen == true,
            walker: walker);
        bool collectDiagnostics = host.Logger.IsEnabled(LogLevel.Information);
        if (collectDiagnostics && levelRoot.Tree.FromId(1608) is DeclaredTask levelTimer)
        {
            string parameters = string.Join(
                ", ",
                levelTimer.Type.Parameters.Select(
                    (parameter, index) => $"{parameter.Name}={levelTimer.GetValue(index) ?? "<null>"}"));
            host.Logger.Information("Mission.Timer", $"LevelTimer_1608 {parameters}");
        }
        if (collectDiagnostics && levelRoot.Tree.FromId(161) is DeclaredTask activation)
        {
            string parameters = string.Join(
                ", ",
                activation.Type.Parameters.Select(
                    (parameter, index) => $"{parameter.Name}={activation.GetValue(index) ?? "<null>"}"));
            host.Logger.Information("Mission.Timer", $"AreaActivate_161 {parameters}");
        }

        return missionText;
    }

    /// <summary>Makes the objective runtime, and breaks the intro when cinematics are off.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="missionText">The mission's text.</param>
    /// <param name="areaPlayer">The player as an area counts it, when the area updates.</param>
    /// <param name="made">Told of the runtime as soon as it exists, before the intro is broken, so that a native the break runs finds it.</param>
    /// <returns>The objective runtime.</returns>
    public MissionObjectiveRuntime BuildObjectives(
        LevelTask levelRoot,
        MissionStringResolver missionText,
        Func<AreaPlayer> areaPlayer,
        Action<MissionObjectiveRuntime> made)
    {
        ArgumentNullException.ThrowIfNull(made);
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(missionText);
        ArgumentNullException.ThrowIfNull(areaPlayer);
        if (mission.Cutscenes is null)
        {
            throw new InvalidOperationException("The cutscene runtime is built before the objectives.");
        }

        // LevelFlow exports eDifficulty straight from the active profile (0x488ED0), unclamped, and the
        // number-row keys as Key0..9.isPressed. -difficulty overrides the profile here as it does for the AI.
        MissionObjectiveRuntime objectives;
        objectives = new MissionObjectiveRuntime(
            levelRoot,
            host.Game.Variables,
            missionText.Get,
            mission.Cutscenes.TryInvokeNative,
            difficulty: host.ScriptDifficulty,
            isKeyDown: host.IsKeyDown);
        mission.Objectives = objectives;
        made(objectives);

        // A transmission sounds as it goes up: 0x486650 plays the message's Sound ("message", "fail") at the listener,
        // like the pickup cue.
        objectives.MessageSound = audio.PlayTransmissionSound;

        // The areas and edit variables update inside the level's one walk, where the tree places them.
        objectives.AttachTo(
            mission.Cutscenes.Walker,
            mission.Cutscenes.VehicleOccupants,
            () => mission.Soldiers?.LiveOccupants() ?? [],
            areaPlayer);

        // Cinematics are opt-in while their retail parity work is in progress. Do not tear the runtime
        // out: the level expressions still use its native surface, and mission outros may activate it
        // later. Instead take the exact path Escape takes in retail: the break key is down when the level's
        // first tick walks, and that walk -- tick 0, not a walk of its own before it -- is where the objective
        // flow reads it and closes the intro. A pre-walk here had set the skip variable a tick early: mission 4's
        // LevelTimer_760 (Run "CutScene_1000.isFinished || EditVariable_4000.nValue") counted from tick 0 where
        // retail's, which reads the variable before the walk sets it, counts from tick 1 -- and the squad that
        // timer releases, and everything it met, ran a tick ahead. The intro still runs through the ticks that
        // close it, as retail's does (two on mission 4); what it would show and sound in them is held back
        // (MissionState.CutsceneFrameShown) until it stops.
        // OPENIGI_BREAK_CUTSCENE=1 takes the same edge, for a lockstep run that should skip without a key.
        if (mission.Cutscenes.IsPlaying
            && (!host.PlayCutscenes
                || string.Equals(
                    Environment.GetEnvironmentVariable("OPENIGI_BREAK_CUTSCENE"),
                    "1",
                    StringComparison.Ordinal)))
        {
            mission.Cutscenes.RequestBreak();
            host.StopCutsceneSounds();
            mission.IntroSkipPending = true;
        }

        return objectives;
    }

    /// <summary>Gets a wheel model's rolling radius in world units, for the wheel animation.</summary>
    /// <param name="mission">The mission whose packs hold the model.</param>
    /// <param name="name">The model.</param>
    /// <returns>The radius, or nought when the mission has no such model.</returns>
    public static float CutsceneWheelRadius(LoadedMission mission, string name)
    {
        return mission.TryGetModel(name, out MefModel? model) && model is not null
            ? WorldScene.WheelRadius(model)
            : 0f;
    }

    /// <summary>Gets the along-track extent retail uses to space cars in a Train consist.</summary>
    /// <param name="mission">The mission whose packs hold the model.</param>
    /// <param name="name">The model.</param>
    /// <returns>The extent, or nought when the mission has no such model.</returns>
    public static float CutsceneModelLength(LoadedMission mission, string name)
    {
        if (!mission.TryGetModel(name, out MefModel? model) || model is null)
        {
            return 0f;
        }

        return WorldScene.TrainCouplingDistance(model);
    }

    /// <summary>Gets the retail origin fraction between a Train model's two coupling vertices.</summary>
    /// <param name="mission">The mission whose packs hold the model.</param>
    /// <param name="name">The model.</param>
    /// <returns>The fraction, or nought when the mission has no such model.</returns>
    public static float CutsceneModelPivot(LoadedMission mission, string name)
    {
        if (!mission.TryGetModel(name, out MefModel? model) || model is null)
        {
            return 0f;
        }

        return WorldScene.TrainCouplingPivotFraction(model);
    }

    /// <summary>Builds the doors, elevators, pickups, world interactions and ambience, and hands the soldiers what they use of them.</summary>
    /// <param name="loaded">The mission's descriptor, packs and terrain.</param>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="solids">The collision index over the placed objects.</param>
    /// <param name="placements">The level's placements, whose models size each pickup's reach.</param>
    /// <remarks>
    /// Interactions are level systems, not AI systems: they are built even when a mission has no usable navigation graph,
    /// and the same door objects serve the player, the AI's door hops and the collision.
    /// </remarks>
    public void BuildInteractions(
        LoadedMission loaded, LevelTask levelRoot, ObjectCollisionIndex solids, IReadOnlyList<WorldPlacement>? placements = null)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(solids);

        // Interactions are level systems, not AI systems: build them even when a mission has no usable
        // navigation graph. The same door objects are shared by player use, AI door hops and collision.
        mission.Doors = DoorRegistry.Build(levelRoot);
        mission.Elevators = ElevatorRegistry.Build(levelRoot, host.Game.Variables);
        mission.Interactions = WorldInteractionRegistry.Build(
            levelRoot, loaded, mission.Doors, host.Game.Variables, mission.Elevators, random: mission.SoundRandom);
        mission.Interactions.Clock = () => mission.Objectives?.LevelTick;
        mission.Interactions.Sounds = audio.PlayInteractionSound;
        mission.GenericPickups = GenericPickupRegistry.Build(levelRoot, host.Game.Variables, placements);

        solids.GenericPickups = mission.GenericPickups;

        // The level's authored ambience -- the AmbientArea boxes that bed a sound in around the player.
        // Each area draws its first gap at load (0x44C740) and updates where the tree places it, against the camera
        // as the player's update last left it -- nowhere on the first tick, so every area starts silent and draws.
        mission.Ambience = AmbientAreaRegistry.Build(levelRoot, mission.SoundRandom);
        mission.AmbienceListener = null;
        if (mission.Cutscenes is { } walked)
        {
            mission.Ambience.AttachTo(
                walked.Walker,
                () => mission.AmbienceListener,
                audio.IsPlayableSound,
                audio.SoundSeconds);
        }
        if (mission.Ambience.Areas.Count > 0)
        {
            host.Logger.Information("Audio", $"{mission.Ambience.Areas.Count} ambient area(s).");
        }

        // Doors: door_open_1 / door_close_1 / door_slide_1 in mission 1, all definition names.
        mission.Doors.SetSoundSink(audio.PlayDoorSound);
        mission.Doors.SetDoorwayBlockedQuery(DoorwayIsBlockedByHuman);
        mission.Elevators.SetSoundSink(audio.PlayElevatorSound);
        if (mission.Doors.Doors.Count > 0)
        {
            host.Logger.Information("World", $"{mission.Doors.Doors.Count} door(s) available to players and AI.");
        }

        // The level's own soldiers, where the title has them to draw (a title without IGI1's has none).
        if (mission.Soldiers is { } soldiers)
        {
            soldiers.UseRandom(mission.SoundRandom);
            soldiers.Cutscenes = mission.Cutscenes;
            mission.Cutscenes?.SetTargetPoseResolver(soldiers.CutscenePose);

            soldiers.SetAiTaskActivator(id => mission.Interactions?.ActivateFromAi(id));
            soldiers.UseInteractions(mission.Interactions);
            soldiers.SetDoors(mission.Doors, solids);
            soldiers.SetElevators(mission.Elevators);

            // The first genuinely world-positioned sound in the port. Its distances are the definition's
            // own -- ai_death_1 is authored 0 to 50 metres -- so a guard dying across the map is silent
            // without anything here choosing a range.
            soldiers.SetDeathSound(position => sounds.PlayDeath(HumanVariantSound.AiDeathPrefix, HumanVariantSound.AiDeathVariants, position));

            // The grunt a soldier makes at a non-fatal hit, rate-limited per human.
            soldiers.SetHitSound((prefix, variants, position) => sounds.PlayHit(prefix, variants, position));
            soldiers.SetHitCooldown(() => HumanVariantSound.HitCooldownTicks(mission.SoundRandom));
            soldiers.SetMeleeStrikeSink((struck, attacker, damage) => harm.StrikePlayer(struck ?? harm.Own, attacker, damage));
            soldiers.SetPlayerDeltaTranslationScale(() => mission.PlayerDeltaTranslationScale);

            // AIAction_PlaySound (0x48DF80) either attaches the definition to the human or starts it at
            // HumanAI's current position. Both begin at the same live position; the backend owns any voice
            // following after that point.
            soldiers.SetScriptSound(audio.PlayScriptSound);

            // Each AI weapon owns its automatic loop and updates its world position every tick. The round
            // callback handles ballistics only; without this parallel path AI shots were completely silent.
            soldiers.SetWeaponSound(audio.TickSoldierWeaponSound);

            // Footsteps: animation event 1010, picked from the six walk sounds of whatever is underfoot.
            soldiers.SetFootstepSound(step => audio.PlayFootstep(step));
        }
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
        if (HumanStandsInSegment(mission.Player?.Body.Position, from, to))
        {
            return true;
        }

        if (mission.Soldiers is { } soldiers)
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
        Vector3d closest = Vector3d.ClosestOnSegment(from, to, point);
        return Vector3d.Distance(closest, point) <= 0.5;
    }

    /// <summary>Builds the security cameras, their sight, and the rotating objects.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="scene">The level's built scene, whose placements the cameras and rotating objects turn.</param>
    /// <param name="solids">The collision index over the placed objects.</param>
    /// <param name="terrain">The terrain the sight also tests, or <see langword="null"/>.</param>
    /// <returns>The trace the cameras see through, which the soldiers see through too.</returns>
    public WorldTraceCast BuildCameras(LevelTask levelRoot, WorldScene scene, ObjectCollisionIndex solids, TerrainData? terrain)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(solids);

        // Camera sight uses the same placed-object + terrain trace as HumanAI. Cameras are level
        // objects, not navigation actors, so construct them independently of whether this mission
        // happens to ship any AI graphs.
        WorldTraceCast worldTrace = terrain is { } ground
            ? WorldTrace.Nearest(solids.Tracer(), TerrainRayTrace.Caster(ground))
            : solids.Tracer();
        mission.SecurityCameras = SecurityCameraRegistry.Build(levelRoot, host.Game.Variables, worldTrace);
        mission.SecurityCameras.BindScene(scene.Placements);
        host.Logger.Information(
            "Ai",
            $"{mission.SecurityCameras.Cameras.Count} security camera(s), "
                + $"{mission.SecurityCameras.ControlCount} camera control(s) placed.");

        // Radar dishes and the authored Rotating tasks. Built here and handed to the presenter, which is
        // what turns them: the walk gives a placement, this gives it an angle per tick.
        mission.RotatingObjects = RotatingObjectRegistry.Build(levelRoot, scene.Placements, host.Game.Variables, mission.MagicObjects);
        return worldTrace;
    }

    /// <summary>Animates the soldiers' brains, and builds the alarm speakers and the guards' rounds.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="scene">The level's built scene, whose placements the speakers sound from.</param>
    /// <param name="worldTrace">What the soldiers' sight and rounds trace: the placed objects and the terrain.</param>
    /// <param name="graphs">The level's nav graphs, by their task's id.</param>
    /// <param name="difficulty">The difficulty every guard reads its combat row at.</param>
    /// <param name="difficultySource">Where the difficulty came from, for the log.</param>
    public void AnimateSoldiers(
        LevelTask levelRoot,
        WorldScene scene,
        WorldTraceCast worldTrace,
        Dictionary<int, GraphFile> graphs,
        AiDifficulty difficulty,
        string difficultySource)
    {
        host.Logger.Information("Ai", $"Difficulty {HumanAiConfigRegistry.NameOf(difficulty)} ({difficulty}) {difficultySource}");
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(worldTrace);
        ArgumentNullException.ThrowIfNull(graphs);
        SoldierSimulation soldiers = mission.Soldiers
            ?? throw new InvalidOperationException("The soldiers are made before their brains are animated.");

        // The sight and bullet rays test placed objects AND the terrain, as the original's
        // 0x4CDB20 does (both of the sight ray's passes send the -1 mask whose sign bit turns
        // the terrain arm on). Objects alone let guards see and shoot through
        // hills.
        // The level's doors, before the brains: a walker asks the registry for the nearest door
        // on a door hop, and an open door is switched out of the collision index so the
        // doorway is walkable. Both have to exist before Animate hands each brain its query.
        soldiers.Animate(
            graphs, worldTrace, host.Resources, host.Game.Natives, levelRoot, mission.AiConfig, difficulty,
            host.Game.Variables);
        host.EndStage("animating the soldiers' brains");

        // The minefields, which blast at their target while their condition holds.
        mission.MineFields = Igi1MineFields.Build(levelRoot);
        mission.MineFieldExpressions = new LevelExpressionRuntime(host.Game.Variables);

        // The Smoke tasks' plumes, in the impact effects' particle module so the one particle drawer shows them.
        mission.SmokePlumes = Igi1SmokePlumes.Build(levelRoot, host.Game.Variables);
        mission.SmokePlumes.Expressions = mission.MineFieldExpressions;
        mission.SmokePlumes.Bind(impacts.Runtime.Particles);

        // A smoke a level hangs off a train or a vehicle rides along with it (0x437AD0).
        mission.SmokePlumes.CarrierPose = task => mission.Cutscenes?.TryGetDrawTransform(task, out Vector3d at, out Matrix3f turn) == true
            ? (at * WorldScene.UnitsPerMetre, turn)
            : null;

        // The level's alarm speakers, for the sirens: each follows its own expression over
        // the same live variables the controls export through.
        mission.AlarmSpeakers = AlarmSpeakerRegistry.Build(
            levelRoot,
            host.Game.Variables,
            () => soldiers.AnyAlarmOn);
        mission.AlarmSpeakers.BindScene(scene.Placements);
        host.Logger.Information("Ai", $"{mission.AlarmSpeakers.Speakers.Count} alarm speaker(s) placed.");

        // A guard's round: it raised the trigger, the weapon's cadence let one out, and GuardRounds makes it a cast.
        mission.GuardRounds = new GuardRounds(soldiers, worldTrace, mission, harm, blasts, sounds, impacts, host, host.Logger);
        soldiers.SetRoundHandler(mission.GuardRounds.Resolve);

        // The soldiers' guns march their rounds through the same world the soldiers see.
        mission.GuardRounds.AttachGunShotWorld();
    }

    /// <summary>Sets the level's vehicles going: their collision, wheels, rigid bodies and what of them can be destroyed.</summary>
    /// <param name="loaded">The mission's descriptor, packs and terrain.</param>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="id">The mission id, for the two missions whose vehicles are configured by name.</param>
    /// <param name="scene">The level's built scene.</param>
    /// <param name="solids">The collision index over the placed objects.</param>
    public void BuildVehicles(LoadedMission loaded, LevelTask levelRoot, int id, WorldScene scene, ObjectCollisionIndex solids)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(solids);
        CutsceneRuntime cutscenes = mission.Cutscenes
            ?? throw new InvalidOperationException("The cutscene runtime is built before the vehicles.");

        // Mission 4's authored getaway Bell is a one-shot terminal landing. Configure that
        // behavior explicitly here; CutsceneRuntime remains generic and does not infer it from ids.
        if (id == 4 && levelRoot.Tree.FromId(800) is { } getawayBell)
        {
            cutscenes.EnableTerminalTerrainLanding(getawayBell);
        }

        // Every mission: a car's suspension reads this for the surface under each wheel, and much
        // of what a vehicle stands on is placed geometry -- Mission 1's outro truck starts on a
        // garage floor above the dirt. Re-seating the helicopters on it stays Mission 4's.
        cutscenes.CollisionIndex = solids;
        if (id == 4)
        {
            cutscenes.RestHelicoptersOnSupport();
        }

        // Bind each car's drawn TASKTYPE_WHEEL attachments to its physics record so the drive force
        // and the tyre torque act at the real axle offsets, and the wheels roll and steer.
        cutscenes.BindVehicleWheels(scene, name => Igi1MissionSetup.CutsceneWheelRadius(loaded, name));

        // Every car runs on the original's rigid body, its wheel rays answered by the exact terrain query.
        if (loaded.Terrain is { } carTerrain)
        {
            RetailTerrainRay carRays = new(
                carTerrain.Octree,
                carTerrain.Commands,
                TerrainDiscardBinding.For(carTerrain).Regions,
                TerrainHeightBindings.Bind(carTerrain, levelRoot));
            Func<string, MefModel?> carModels = name => loaded.TryGetModel(name, out MefModel? model) ? model : null;
            mission.RetailWorld = new RetailCarWorld(
                carRays,
                RetailWorldObjects.Build(
                    levelRoot,
                    carModels,
                    name => mission.MagicObjects?.TryGet(name, out MagicObjectDefinition attached) == true ? attached : null,
                    definition => mission.MagicObjects?.TaskTypeName(definition.TaskType) == "TASKTYPE_GLASS"),
                cutscenes.RetailVehicleModels,
                static owner => owner is RetailGlassPane pane
                    ? !pane.IsBroken && ConditionalContainerGate.IsTaskActive(pane.Owner)
                    : owner is not GameTask task || ConditionalContainerGate.IsTaskActive(task),
                (pane, from) => mission.TouchGlass?.Invoke(pane, from) == true);
            cutscenes.ConfigureRetailCars(
                mission.RetailWorld,
                carModels,
                name => mission.MagicObjects?.TryGet(name, out MagicObjectDefinition magic) == true ? magic : null);
        }

        // The level's one global stream, seeded as LevelFlow's start seeds it (0x416D90), before the first draw:
        // the glass panes' thresholds below, then the ambience, then everything a tick draws, in walk order.
        mission.SoundRandom.SeedForLevel();

        // The Hind's rocket pods and door, which its flights run; each pod draws its first gap as it is made. The pods and
        // the vehicles' guns draw from the level's stream.
        cutscenes.ConfigureVehicleParts(
            scene,
            name => mission.MagicObjects?.TryGet(name, out MagicObjectDefinition part) == true ? part : null,
            definition => mission.MagicObjects?.TaskTypeName(definition.TaskType) ?? string.Empty);
        cutscenes.VehicleRandom = mission.SoundRandom;

        // Every Car exports isExploded and blows up from its own CAR record's explosion points; the
        // CarAI stops, and follows its body, through the same registry.
        mission.Destructibles = DestructibleObjectRegistry.Build(
            levelRoot,
            scene.Placements,
            host.Game.Variables,
            mission.VehiclePhysics,
            random: mission.SoundRandom,
            made: cutscenes.VehiclePartMade);
        solids.Destructibles = mission.Destructibles;
        cutscenes.Destructibles = mission.Destructibles;
    }

    /// <summary>Reads the level's nav graphs, which the soldiers and the vehicles drive over.</summary>
    /// <param name="loaded">The mission, whose directory holds them.</param>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="gamePath">The install's root.</param>
    /// <param name="placements">The level's placements, which place a graph on a train where its car stands.</param>
    /// <returns>The graphs by their task's id, in world space; empty when the level ships none.</returns>
    public Dictionary<int, GraphFile> ReadNavGraphs(LoadedMission loaded, LevelTask levelRoot, string gamePath, IReadOnlyList<WorldPlacement>? placements = null)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(gamePath);

        string graphDirectory = ResolveDirectoryCaseInsensitive(
                gamePath, Path.Combine(loaded.Descriptor.MissionDirectory, "graphs"))
            ?? Path.Combine(gamePath, loaded.Descriptor.MissionDirectory, "graphs");

        Dictionary<int, GraphFile> graphs;
        try
        {
            graphs = NavGraphs.Read(graphDirectory, levelRoot, host.Logger, placements);
        }
        catch (IOException ex)
        {
            host.Logger.Warning("Ai", $"The nav graph could not be read: {ex.Message}");
            graphs = [];
        }

        if (graphs.Count > 0)
        {
            host.Logger.Information(
                "Ai",
                $"Nav graphs: {graphs.Count} — " +
                string.Join(", ", graphs.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value.Nodes.Count}n@{p.Value.CoverMidOffsetMetres:0.##}m")));

            // CarAI is a separate retail task family from HumanAI. Give the runtime the same graph
            // corpus the soldiers receive so every graph-driven vehicle -- Mission 7's checkpoint APC
            // and T80, Mission 6's alarm APC, the objective-gated limousines -- follows its authored
            // PatrolPath route. d843661b dropped this call and left every one of them parked.
            mission.Cutscenes?.ConfigureCarAi(graphs);
        }
        else
        {
            // A missing patrol graph only removes movement: retail still registers every HumanAI as a live human, which
            // can stand, be seen and be selected by the binocular detector.
            host.Logger.Warning("Ai", $"No nav graph under '{graphDirectory}', so no soldier will patrol.");
        }

        return graphs;
    }

    /// <summary>Resolves a retail directory without assuming the install's casing matches the script.</summary>
    /// <param name="root">The directory it is under.</param>
    /// <param name="relativePath">The path below it, as the script spells it.</param>
    /// <returns>The directory as the install spells it, or <see langword="null"/> when there is none.</returns>
    public static string? ResolveDirectoryCaseInsensitive(string root, string relativePath)
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
}
