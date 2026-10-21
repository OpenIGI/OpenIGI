using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>
/// What every part of a level's cutscene runtime shares: the level, its expressions, the live transforms of the tasks
/// it moves, the world it queries and the tick count.
/// </summary>
/// <remarks>
/// <see cref="CutsceneRuntime"/> builds one and hands it to each collaborator it splits its work across, so that all of
/// them read and write the same transforms in the order the level walk runs them. Main thread only.
/// </remarks>
internal sealed class CutsceneContext(
    GameTask root,
    LevelExpressionRuntime expressions,
    VehiclePhysicsRegistry? vehiclePhysics,
    TerrainData? terrain,
    IOpenIgiLogger logger)
{
    private readonly List<string> _diagnostics = [];

    /// <summary>Gets the level root.</summary>
    public GameTask Root { get; } = root;

    /// <summary>Gets the level's expression runtime, which every Run, condition and script goes through.</summary>
    public LevelExpressionRuntime Expressions { get; } = expressions;

    /// <summary>Gets the vehicle physics records, when the level was given them.</summary>
    public VehiclePhysicsRegistry? VehiclePhysics { get; } = vehiclePhysics;

    /// <summary>Gets the level's terrain, when it has one.</summary>
    public TerrainData? Terrain { get; } = terrain;

    /// <summary>Gets the logger the airframe notes go to.</summary>
    public IOpenIgiLogger Logger { get; } = logger;

    /// <summary>Gets each driven task's transform, by task id.</summary>
    public Dictionary<int, CutsceneTransformState> Transforms { get; } = [];

    /// <summary>Gets each driven task's transform, by task, including the tasks that carry no id.</summary>
    public Dictionary<GameTask, CutsceneTransformState> TaskTransforms { get; } = [];

    /// <summary>Gets each task's place in the level's depth-first order.</summary>
    public Dictionary<GameTask, int> TaskOrder { get; } = [];

    /// <summary>Gets or sets the number of runtime ticks applied since construction.</summary>
    public int SimulationTick { get; set; }

    /// <summary>Gets or sets the solid-object index a vehicle finds its supporting surface in.</summary>
    public ObjectCollisionIndex? CollisionIndex { get; set; }

    /// <summary>Gets or sets the destruction state whose vehicle origins follow live CarAI transforms.</summary>
    public DestructibleObjectRegistry? Destructibles { get; set; }

    /// <summary>Gets the ride / rest / attach notes, capped.</summary>
    public IReadOnlyList<string> Diagnostics => _diagnostics;

    /// <summary>Records a task's transform under the task and, when it has one, under its id.</summary>
    public void Remember(GameTask task, CutsceneTransformState state)
    {
        TaskTransforms[task] = state;
        if (task.Id != GameTask.NoId)
        {
            Transforms[task.Id] = state;
        }
    }

    /// <summary>Adds one note, stamped with the tick, until the cap is reached.</summary>
    public void Note(string message)
    {
        if (_diagnostics.Count >= 256)
        {
            return;
        }

        _diagnostics.Add($"tick={SimulationTick} {message}");
    }
}
