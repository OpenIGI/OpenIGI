using OpenIGI.Game.Ai;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>What a level's AI is built from: its tree, the session's script namespace and tables, and the level's humans.</summary>
/// <param name="Root">The level's task tree.</param>
/// <param name="Resources">Where its scripts and graphs are read from.</param>
/// <param name="Natives">The session's script namespace.</param>
/// <param name="Functions">The natives line scripts may call.</param>
/// <param name="Player">The player, whichever game's: this game's own, or another's whose level this game's soldiers stand in.</param>
/// <param name="Humans">Every human, in <c>Human_ptQTaskList</c>'s order.</param>
/// <param name="Random">The session's run stream.</param>
/// <param name="LevelTick">The level's tick.</param>
public sealed record Igi2LevelAiSetup(
    GameTask Root,
    ResourceRegistry Resources,
    NativeRegistry Natives,
    Igi2ScriptFunctions Functions,
    IAiPlayer Player,
    IReadOnlyList<Igi2Human> Humans,
    IgiRandom Random,
    Func<int> LevelTick)
{
    /// <summary>Gets what detectors see, or <see langword="null"/> when the session has no materials to trace sight with.</summary>
    public Detection.Igi2DetectionWorld? Detection { get; init; }

    /// <summary>Gets the fire task types, which tell a bullet from a rocket.</summary>
    public Weapons.Igi2FireTaskTypes? FireTaskTypes { get; init; }

    /// <summary>Gets the AI's tuning, or <see langword="null"/>.</summary>
    public Igi2HumanAiConfig? Config { get; init; }

    /// <summary>Gets the session's difficulty, nought to two.</summary>
    public int Difficulty { get; init; } = 1;

    /// <summary>Gets where load diagnostics go.</summary>
    public IOpenIgiLogger? Logger { get; init; }

    /// <summary>
    /// Gets the session's AI module, whose natives are already bound, or <see langword="null"/> to make one for this level and bind
    /// them now.
    /// </summary>
    public Igi2AiWorld? World { get; init; }

    /// <summary>Gets the ambient noise at a point, which muffles what soldiers hear, or <see langword="null"/> for none.</summary>
    public Func<Vector3d, float>? NoiseAt { get; init; }
}

/// <summary>
/// Gives a level's soldiers their minds, as the original loads them: the session's module (opened with its natives and default
/// scripts, <c>HumanAI_Open</c> and <c>AISquad_Open</c>) emptied of the last level; every squad and every soldier's mind made and its script read in
/// the tree's order (<c>AISquad_UpdateInternalDataHandler</c>, <c>HumanAI_UpdateInternalDataHandler</c>); then, once the level is
/// loaded, each squad's team built from its soldiers before they ready themselves (<c>AISquad_BuildTeamFromChildren</c>,
/// <c>HumanAI_UpdateInternalDataAfterLoadHandler</c>, a parent before its children).
/// </summary>
public static class Igi2LevelAi
{
    /// <summary>Builds the level's AI.</summary>
    /// <param name="setup">What it is built from.</param>
    /// <returns>The AI module.</returns>
    public static Igi2AiWorld Build(Igi2LevelAiSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        Dictionary<int, object> byId = TasksById(setup.Root, setup.Player);
        Igi2AiWorld world = setup.World ?? new Igi2AiWorld();
        // humanai.c HumanAI_UpdateInternalDataHandler: scripts run in the loaded level's namespace.
        // The desktop may have promoted a preview loop since this shared module was opened.
        world.Natives = setup.Natives;
        // humanai.c HumanAI_Open / HumanAI_RegisterConstants; aisquad.c AISquad_Open:
        // the playing title owns these bindings. The shared desktop soldier renderer can have
        // installed IGI1 handlers and event values since module registration, even in this namespace.
        Igi2AiConstants.Register(setup.Natives);
        new Igi2HumanAiNatives(world).Register(setup.Natives);
        new Igi2AiSquadNatives(world).Register(setup.Natives);

        if (world.Default is null)
        {
            world.LoadScripts(setup.Resources);
        }

        world.BeginLevel();
        world.Resources = setup.Resources;
        world.Random = setup.Random;
        world.Humans = setup.Humans;
        world.LevelTick = setup.LevelTick;
        world.Difficulty = setup.Difficulty;
        world.Detection = setup.Detection;
        world.Player = setup.Player;
        world.Graphs = Igi2AiGraphs.Build(setup.Root, setup.Resources, setup.Natives, setup.Functions, setup.Logger);
        world.TaskById = id => byId.GetValueOrDefault(id);
        world.TaskPosition = id => PositionOf(byId.GetValueOrDefault(id));
        world.PatrolPath = id => byId.GetValueOrDefault(id) as Igi2PatrolPathTask;
        world.EvaluateCommand = command => Igi2LineScript.Evaluate(command.Expression, setup.Natives, setup.Functions) != 0;
        world.Doors = [.. setup.Root.Descendants().OfType<Interaction.Igi2DoorTask>()];
        world.NoiseAt = setup.NoiseAt ?? (static _ => 0f);
        if (setup.FireTaskTypes is { } fireTypes)
        {
            world.FireTaskTypes = fireTypes;
        }

        foreach (Igi2AiSquadTask task in setup.Root.Descendants().OfType<Igi2AiSquadTask>())
        {
            Igi2AiSquad squad = new(world, task) { Index = world.Squads.Count };
            task.Squad = squad;
            world.Squads.Add(squad);
            squad.UpdateInternalData();
        }

        List<Igi2HumanAi> minds = [];
        foreach (Igi2HumanSoldierTask task in setup.Root.Descendants().OfType<Igi2HumanSoldierTask>())
        {
            if (task.Body is { } body && task.Ai is { } aiTask)
            {
                Igi2HumanAi mind = new(world, aiTask, body);
                mind.UpdateInternalData();
                minds.Add(mind);
            }
        }

        foreach (Igi2AiSquad squad in world.Squads)
        {
            squad.BuildTeam(squad.Task.Soldiers.Select(static soldier => soldier.Body?.Mind).OfType<Igi2HumanAi>());
        }

        foreach (Igi2HumanAi mind in minds)
        {
            mind.AfterLoad(setup.Config?.Tweaks(mind.Task.AiType, setup.Difficulty));
        }

        world.Current = null;
        return world;
    }

    /// <summary>
    /// The level's tasks by id, as <c>Level_atLevelQTaskID</c> holds them: a soldier's task stands for its body, the player's for
    /// the player.
    /// </summary>
    private static Dictionary<int, object> TasksById(GameTask root, IAiPlayer player)
    {
        Dictionary<int, object> byId = [];
        foreach (GameTask task in root.Descendants())
        {
            if (task.Id == GameTask.NoId)
            {
                continue;
            }

            object thing = task switch
            {
                Igi2HumanSoldierTask { Body: { } body } => body,
                Player.Igi2HumanPlayerTask => player,
                _ => task,
            };
            byId.TryAdd(task.Id, thing);
        }

        return byId;
    }

    /// <summary>Where a task is: a human's position, or the position a placed task was given.</summary>
    private static Vector3d? PositionOf(object? thing) => thing switch
    {
        IAiHuman human => human.Position,
        IIgi2PlacedTask placed when placed.TryGetValue("Position", out object? value) && value is Vector3d position => position,
        _ => null,
    };
}
