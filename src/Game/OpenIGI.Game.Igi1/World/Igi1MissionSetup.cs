using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.Menus;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi1.World;

/// <summary>What bringing up Project I.G.I.'s cutscenes and objectives asks of whatever plays and shows them.</summary>
public interface IIgi1MissionSetupHost
{
    /// <summary>Gets the log.</summary>
    IOpenIgiLogger Logger { get; }

    /// <summary>Gets the game's loop, whose variables the runtimes read and write.</summary>
    GameLoop Game { get; }

    /// <summary>Gets the resources the mission's text is read from.</summary>
    ResourceRegistry Resources { get; }

    /// <summary>Gets the difficulty <c>LevelFlow</c> exports as <c>eDifficulty</c>, unclamped.</summary>
    int ScriptDifficulty { get; }

    /// <summary>Gets a value indicating whether the level's cinematics play, or are broken as Escape breaks them.</summary>
    bool PlayCutscenes { get; }

    /// <summary>Answers a native the level's cutscene scripts call that the host owns: the music, the menus.</summary>
    /// <param name="name">The native.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>Its answer, or <see langword="null"/> when the host has none.</returns>
    QvmValue? HostNative(string name, ScriptArguments arguments);

    /// <summary>Plays or stops a cutscene's sound.</summary>
    /// <param name="sound">What changed.</param>
    void CutsceneSound(CutsceneSoundEvent sound);

    /// <summary>Gets where the level's doors, lifts and soldiers send their sounds, and their blows on the player.</summary>
    Igi1SoundSinks Sounds { get; }

    /// <summary>Stops every cutscene sound.</summary>
    void StopCutsceneSounds();

    /// <summary>Reads a key, for the <c>Key0..9.isPressed</c> globals.</summary>
    /// <param name="key">The key.</param>
    /// <returns>Whether it is held.</returns>
    bool IsKeyDown(KeyCode key);
}

/// <summary>Where the level's doors, lifts and soldiers send their sounds, and a soldier's blow on the player.</summary>
/// <param name="Door">A door opening, closing or sliding.</param>
/// <param name="Elevator">A lift starting, moving or stopping.</param>
/// <param name="SoldierDeath">A soldier's death cry, where it died.</param>
/// <param name="SoldierHit">A soldier's grunt at a hit: the definitions' prefix, how many, and where.</param>
/// <param name="Script">A sound a soldier's script plays (<c>AIAction_PlaySound</c>): the definition, where, and whether it follows the soldier.</param>
/// <param name="Weapon">A soldier's weapon loop: its runtime id, whether it fired, the weapon and where.</param>
/// <param name="Footstep">A soldier's footstep.</param>
/// <param name="PlayerStruck">A soldier's close-combat blow on the player: from where, and its damage.</param>
public sealed record Igi1SoundSinks(
    Action<PlacedDoor, DoorSoundEvent> Door,
    Action<PlacedElevator, ElevatorSoundEvent> Elevator,
    Action<Vector3d> SoldierDeath,
    Action<string, int, Vector3d> SoldierHit,
    Action<string, Vector3d, bool> Script,
    Action<int, bool, WeaponType?, Vector3d> Weapon,
    Action<Footstep> Footstep,
    Action<Vector3d, float> PlayerStruck);

/// <summary>Brings up Project I.G.I.'s cutscene runtime and objective runtime for a loaded level.</summary>
/// <param name="mission">The mission's world, which keeps them.</param>
/// <param name="host">What plays and shows them.</param>
public sealed class Igi1MissionSetup(MissionWorld mission, IIgi1MissionSetupHost host)
{
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

        // The areas and edit variables update inside the level's one walk, where the tree places them.
        objectives.AttachTo(
            mission.Cutscenes.Walker,
            mission.Cutscenes.VehicleOccupants,
            () => mission.Soldiers?.LiveOccupants() ?? [],
            areaPlayer);

        // Cinematics are opt-in while their retail parity work is in progress. Do not tear the runtime
        // out: the level expressions still use its native surface, and mission outros may activate it
        // later. Instead take the exact path Escape takes in retail. The objective flow observes the
        // break key, closes the intro container, and the cutscene tick applies that transition before
        // the first gameplay frame, so neither its camera nor its ConditionalSounds leak through.
        if (!host.PlayCutscenes && mission.Cutscenes.IsPlaying)
        {
            mission.Cutscenes.RequestBreak();
            objectives.TickLevelState();
            mission.Cutscenes.Tick();
            mission.Cutscenes.FinishExpressionTick();
            host.StopCutsceneSounds();
        }

        // Same native edge as Escape (LevelFlow_GetBreakCutSceneKey). Used by the
        // agent playtest so lockstep/-frames can skip the intro without a HITL key.
        if (string.Equals(
                Environment.GetEnvironmentVariable("OPENIGI_BREAK_CUTSCENE"),
                "1",
                StringComparison.Ordinal)
            && mission.Cutscenes.IsPlaying)
        {
            mission.Cutscenes.RequestBreak();
            objectives.TickLevelState();
            mission.Cutscenes.Tick();
            mission.Cutscenes.FinishExpressionTick();
            host.StopCutsceneSounds();
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
    /// <remarks>
    /// Interactions are level systems, not AI systems: they are built even when a mission has no usable navigation graph,
    /// and the same door objects serve the player, the AI's door hops and the collision.
    /// </remarks>
    public void BuildInteractions(LoadedMission loaded, LevelTask levelRoot, ObjectCollisionIndex solids)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(solids);

        // Interactions are level systems, not AI systems: build them even when a mission has no usable
        // navigation graph. The same door objects are shared by player use, AI door hops and collision.
        mission.Doors = DoorRegistry.Build(levelRoot);
        mission.Elevators = ElevatorRegistry.Build(levelRoot, host.Game.Variables);
        mission.Interactions = WorldInteractionRegistry.Build(levelRoot, loaded, mission.Doors, host.Game.Variables, mission.Elevators);
        mission.GenericPickups = GenericPickupRegistry.Build(levelRoot, host.Game.Variables);

        solids.GenericPickups = mission.GenericPickups;

        // The level's authored ambience -- the AmbientArea boxes that bed a sound in around the player.
        mission.Ambience = AmbientAreaRegistry.Build(levelRoot, mission.SoundRandom);
        if (mission.Ambience.Areas.Count > 0)
        {
            host.Logger.Information("Audio", $"{mission.Ambience.Areas.Count} ambient area(s).");
        }

        // Doors: door_open_1 / door_close_1 / door_slide_1 in mission 1, all definition names.
        mission.Doors.SetSoundSink(host.Sounds.Door);
        mission.Doors.SetDoorwayBlockedQuery(DoorwayIsBlockedByHuman);
        mission.Elevators.SetSoundSink(host.Sounds.Elevator);
        if (mission.Doors.Doors.Count > 0)
        {
            host.Logger.Information("World", $"{mission.Doors.Doors.Count} door(s) available to players and AI.");
        }

        // The level's own soldiers, where the title has them to draw (a title without IGI1's has none).
        if (mission.Soldiers is { } soldiers)
        {
            soldiers.Cutscenes = mission.Cutscenes;
            mission.Cutscenes?.SetTargetPoseResolver(soldiers.CutscenePose);

            soldiers.SetAiTaskActivator(id => mission.Interactions?.ActivateFromAi(id));
            soldiers.UseInteractions(mission.Interactions);
            soldiers.SetDoors(mission.Doors, solids);
            soldiers.SetElevators(mission.Elevators);

            // The first genuinely world-positioned sound in the port. Its distances are the definition's
            // own -- ai_death_1 is authored 0 to 50 metres -- so a guard dying across the map is silent
            // without anything here choosing a range.
            soldiers.SetDeathSound(host.Sounds.SoldierDeath);

            // The grunt a soldier makes at a non-fatal hit, rate-limited per human.
            soldiers.SetHitSound(host.Sounds.SoldierHit);
            soldiers.SetHitCooldown(() => HumanVariantSound.HitCooldownTicks(mission.SoundRandom));
            soldiers.SetMeleeStrikeSink(host.Sounds.PlayerStruck);
            soldiers.SetPlayerDeltaTranslationScale(() => mission.PlayerDeltaTranslationScale);

            // AIAction_PlaySound (0x48DF80) either attaches the definition to the human or starts it at
            // HumanAI's current position. Both begin at the same live position; the backend owns any voice
            // following after that point.
            soldiers.SetScriptSound(host.Sounds.Script);

            // Each AI weapon owns its automatic loop and updates its world position every tick. The round
            // callback handles ballistics only; without this parallel path AI shots were completely silent.
            soldiers.SetWeaponSound(host.Sounds.Weapon);

            // Footsteps: animation event 1010, picked from the six walk sounds of whatever is underfoot.
            soldiers.SetFootstepSound(host.Sounds.Footstep);
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
}
