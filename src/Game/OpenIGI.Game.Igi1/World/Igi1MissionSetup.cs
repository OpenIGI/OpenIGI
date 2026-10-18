using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.Menus;
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

    /// <summary>Stops every cutscene sound.</summary>
    void StopCutsceneSounds();

    /// <summary>Reads a key, for the <c>Key0..9.isPressed</c> globals.</summary>
    /// <param name="key">The key.</param>
    /// <returns>Whether it is held.</returns>
    bool IsKeyDown(KeyCode key);
}

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
}
