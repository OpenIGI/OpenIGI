using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// Which of the level's placed objects are in the world: everything under a conditional container is in it only while the
/// container runs.
/// </summary>
/// <remarks>
/// <para>
/// <c>ConditionalContainer_RunHandler</c> registers its children in the world when its condition comes to hold
/// (<c>ConditionalContainer_RegisterChildren</c>) and removes them when it stops (<c>ConditionalContainer_RemoveChildren</c>),
/// and a container inside one that is out is out with it. An object taken out is neither drawn nor met by a line: a level
/// keeps the props of each of its cutscenes in a container of their own, and they are gone when it ends.
/// </para>
/// <para>
/// Read from the containers as they stand rather than kept beside them, so a level put back from a save is right without being
/// told. The solids are set once a tick, after the level has run.
/// </para>
/// </remarks>
public sealed class Igi2WorldPresence : IPlacementPresence
{
    private readonly Dictionary<int, GameTask> _gated = [];
    private readonly List<(GameTask Task, Igi2RigidObject Solid)> _solids = [];

    private Igi2WorldPresence()
    {
    }

    /// <summary>Reads a level's containers: the tasks under one, and the solids the collision world built of them.</summary>
    /// <param name="root">The level's task tree.</param>
    /// <param name="world">The level's collision world, or <see langword="null"/> for none.</param>
    /// <returns>The level's presence, or <see langword="null"/> for a level with nothing under a conditional container.</returns>
    public static Igi2WorldPresence? Build(GameTask root, Igi2CollisionWorld? world)
    {
        ArgumentNullException.ThrowIfNull(root);
        Igi2WorldPresence presence = new();
        Collect(root, isGated: false, presence._gated);
        if (presence._gated.Count == 0)
        {
            return null;
        }

        foreach (Igi2RigidObject solid in world?.Objects ?? [])
        {
            if (solid.Owner is Igi2CollisionOwner owner && presence._gated.TryGetValue(owner.LevelId, out GameTask? task))
            {
                presence._solids.Add((task, solid));
            }
        }

        return presence;
    }

    /// <inheritdoc/>
    public bool IsInWorld(WorldPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return IsInWorld(placement.LevelId);
    }

    /// <summary>Gets a value indicating whether the object a task placed is in the world.</summary>
    /// <param name="levelId">The placing task's level id.</param>
    /// <returns><see langword="false"/> while a container above the task is not running.</returns>
    public bool IsInWorld(int levelId) =>
        !_gated.TryGetValue(levelId, out GameTask? task) || ConditionalContainerGate.IsTaskActive(task);

    /// <summary>Puts every solid under a container in the world or takes it out, as its containers now stand.</summary>
    public void Sync()
    {
        foreach ((GameTask task, Igi2RigidObject solid) in _solids)
        {
            solid.IsInWorld = ConditionalContainerGate.IsTaskActive(task);
        }
    }

    // Every task with a level id under a conditional container, by that id.
    private static void Collect(GameTask task, bool isGated, Dictionary<int, GameTask> into)
    {
        foreach (GameTask child in task.Children)
        {
            if (isGated && child.Id >= 0)
            {
                into.TryAdd(child.Id, child);
            }

            Collect(child, isGated || child is ConditionalContainerTask, into);
        }
    }
}
