using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Collision;

/// <summary>
/// The level's tasks a human stands on, as a save game names them: <c>Human_s.ptGroundQTaskRef</c>, written by
/// <c>SerializeQTaskRef_ParameterPrintHandler</c> as the task's hierarchy and read back to the task by
/// <c>SerializeQTaskRef_ParameterParseHandler</c>. Here the ground is the collider the body's footing struck, so this maps each
/// collider to the task it is and back.
/// </summary>
/// <param name="root">A task of the level.</param>
/// <param name="world">The level's collision.</param>
/// <param name="graphs">The level's AI graphs, once its AI is made: a soldier with a mind stands on his graph.</param>
/// <remarks>
/// A placement's own solid is its task; an attachment's has no task with an id in the original either (a model's attach objects are
/// made under a container of their own), so it is written as <c>-1</c> and read back as no ground, which the footing takes hold of
/// again on the body's next move.
/// </remarks>
public sealed class Igi2LevelGrounds(GameTask root, Igi2CollisionWorld world, Func<Ai.Igi2AiGraphs?> graphs)
{
    /// <summary>The task a ground is, for a save to name it.</summary>
    /// <param name="ground">The collider the body stands on, or <see langword="null"/>.</param>
    /// <returns>The task, or <see langword="null"/> for none the save can name.</returns>
    public GameTask? TaskOf(IWorldCollider? ground) => ground switch
    {
        null => null,
        Igi2RigidObject { Owner: Igi2CollisionOwner { Depth: 0 } owner } => ById(owner.LevelId) is { } task && task.Type.Name == owner.TypeName ? task : null,
        Ai.Igi2AiGraphGround graph => ById(graph.Graph.Id) as Ai.Igi2AiGraphTask,
        _ when ReferenceEquals(ground, world.Terrain) => root.Tree.Root.Descendants().OfType<World.TerrainTask>().FirstOrDefault(),
        _ => null,
    };

    /// <summary>The ground a task a save names is, for a body read back to stand on.</summary>
    /// <param name="task">The task, or <see langword="null"/>.</param>
    /// <returns>Its collider, or <see langword="null"/> when it has none.</returns>
    public IWorldCollider? GroundOf(GameTask? task) => task switch
    {
        null => null,
        Ai.Igi2AiGraphTask => graphs()?.Graph(task.Id)?.Ground,
        World.TerrainTask => world.Terrain,
        _ => world.SolidsOf(task.Type.Name, task.Id).FirstOrDefault(static solid => solid.Owner is Igi2CollisionOwner { Depth: 0 }),
    };

    private GameTask? ById(int id) => id == GameTask.NoId ? null : root.Tree.FromId(id);
}
