using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>One placed door — its state machine, where it stands and which placement it draws through.</summary>
/// <param name="TaskId">The <c>Door</c> task's own id.</param>
/// <param name="TaskName">The placing task's name, which the collision index keys on.</param>
/// <param name="Position">Where it is, in world units.</param>
/// <param name="State">Its state machine.</param>
public sealed record PlacedDoor(int TaskId, string TaskName, Vector3d Position, DoorState State);

/// <summary>
/// The level's doors — collected from the task tree, ticked together, and answering the AI walker's
/// nearest-door query.
/// </summary>
/// <remarks>
/// The <c>Door</c> task (<c>0x4464C0</c>, 808 bytes) declares <c>Position start</c>,
/// <c>Position stop X/Y</c>, <c>Position slider</c>, <c>Orientation</c>, <c>Model</c>,
/// <c>Max angle</c>, <c>Open time</c>, <c>Pickable</c>, <c>Pick lock time (s)</c>, three condition
/// expressions and three sound names. <see cref="DoorState"/> runs the two motion channels;
/// <see cref="Nearest"/> is the walker's broadcast query (<c>sub_446470</c> keeps the nearest answer
/// and has <b>no radius cap</b>).
/// </remarks>
public sealed class DoorRegistry
{
    private readonly List<PlacedDoor> _doors = [];

    /// <summary>Gets the level's doors.</summary>
    public IReadOnlyList<PlacedDoor> Doors => _doors;

    /// <summary>Collects every <c>Door</c> task in a level.</summary>
    /// <param name="root">The level's task tree.</param>
    /// <returns>The registry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public static DoorRegistry Build(GameTask root)
    {
        ArgumentNullException.ThrowIfNull(root);

        DoorRegistry registry = new();
        Collect(root, registry);
        return registry;
    }

    private static void Collect(GameTask task, DoorRegistry registry)
    {
        if (task is DeclaredTask declared
            && string.Equals(task.Type.Name, "Door", StringComparison.Ordinal))
        {
            Vector3d position = declared.TryGetValue("Position start", out object? start) && start is Vector3d p
                ? p
                : default;

            float maxAngle = declared.TryGetValue("Max angle", out object? angle) && angle is float a ? a : 0f;
            float openTime = declared.TryGetValue("Open time", out object? time) && time is float t ? t : 1f;
            bool slides =
                (declared.TryGetValue("Position stop X", out object? sx) && sx is float x && x != 0f)
                || (declared.TryGetValue("Position stop Y", out object? sy) && sy is float y && y != 0f);

            registry._doors.Add(new PlacedDoor(
                task.Id,
                task.Name,
                position,
                new DoorState(maxAngle, openTime <= 0f ? 1f : openTime, slides)));
        }

        foreach (GameTask child in task.Children)
        {
            Collect(child, registry);
        }
    }

    /// <summary>Advances every door one tick.</summary>
    public void Tick()
    {
        foreach (PlacedDoor door in _doors)
        {
            door.State.Tick();
        }
    }

    /// <summary>The door nearest a point — the walker's query, with no radius cap.</summary>
    /// <param name="near">Where to search from, in world units.</param>
    /// <returns>The nearest door's state, or <see langword="null"/> when the level ships none.</returns>
    public DoorState? Nearest(Vector3d near)
    {
        DoorState? best = null;
        double bestDistance = -1.0;

        foreach (PlacedDoor door in _doors)
        {
            double dx = door.Position.X - near.X;
            double dy = door.Position.Y - near.Y;
            double dz = door.Position.Z - near.Z;
            double distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

            // The sentinel starts negative, so the first answer always wins -- sub_446470's test.
            if (bestDistance < 0.0 || distance < bestDistance)
            {
                bestDistance = distance;
                best = door.State;
            }
        }

        return best;
    }
}
