using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

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
public sealed class DoorRegistry : ILevelReset, Saves.ISavedStateSource
{
    private readonly List<PlacedDoor> _doors = [];
    private bool _inWalk;
    private Action<PlacedDoor>? _ticked;

    /// <summary>The doors by their task, for the per-placement draw query.</summary>
    /// <remarks>
    /// The renderer asks every placement in the level whether it is a door, every frame; a linear scan
    /// there was a placements-times-doors product that showed in the profile.
    /// </remarks>
    private readonly Dictionary<GameTask, PlacedDoor> _doorsByTask = new(ReferenceEqualityComparer.Instance);

    private Action<PlacedDoor, DoorSoundEvent>? _sounds;
    private Func<Vector3d, Vector3d, bool>? _doorwayBlocked;

    /// <summary>Gets the level's doors.</summary>
    public IReadOnlyList<PlacedDoor> Doors => _doors;

    /// <inheritdoc/>
    public IEnumerable<KeyValuePair<GameTask, Saves.ISavedState>> SavedStates(ObjectCollisionIndex? solids) =>
        _doors.Select(static door => new KeyValuePair<GameTask, Saves.ISavedState>(door.Task, door.State));

    /// <summary>Collects every <c>Door</c> task in a level.</summary>
    /// <param name="root">The level's task tree.</param>
    /// <returns>The registry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public static DoorRegistry Build(GameTask root)
    {
        ArgumentNullException.ThrowIfNull(root);

        DoorRegistry registry = new();
        Collect(root, registry, nearestObject: null);
        return registry;
    }

    /// <summary>Walks the tree in the post-load's order -- a door is placed before the doors inside it.</summary>
    /// <param name="task">The task.</param>
    /// <param name="registry">The registry.</param>
    /// <param name="nearestObject">The nearest ancestor that is a world object (one with a model), which
    /// <c>0x4474E0</c> takes a door's frame from; only a door moves the doors inside it.</param>
    private static void Collect(GameTask task, DoorRegistry registry, GameTask? nearestObject)
    {
        GameTask? objectHere = nearestObject;
        if (task is DoorTask declared)
        {
            (PlacedDoor door, float[] baseOrientation) = Create(declared);
            PlacedDoor? parent = nearestObject is not null && registry._doorsByTask.TryGetValue(nearestObject, out PlacedDoor? outer)
                ? outer
                : null;
            door.Initialise(baseOrientation, parent);
            registry._doors.Add(door);
            declared.PlacementsChanged();
            registry._doorsByTask[declared] = door;
        }

        if (task is DeclaredTask placed && placed.TryGetValue(WorldScene.ModelParameter, out object? model) && model is string { Length: > 0 })
        {
            objectHere = task;
        }

        foreach (GameTask child in task.Children)
        {
            Collect(child, registry, objectHere);
        }
    }

    /// <summary>Reads a <c>Door</c> task's parameters into a door and its base orientation (<c>0x4B38E0</c>, exact).</summary>
    private static (PlacedDoor Door, float[] Base) Create(DoorTask declared)
    {
        float stopX = declared.PositionStopX;
        float stopY = declared.PositionStopY;
        float slider = declared.PositionSlider;
        float openTime = declared.OpenTime;

        // 0x446D10 slides only with a stop; 0x447830 keeps the slide's length for the step.
        bool slides = stopX != 0f || stopY != 0f;
        float length = X87.Sqrt((X87)stopX * stopX + (X87)stopY * stopY).F;
        float[] baseOrientation = declared.BaseOrientation;

        // The three sounds are declared String16 and hold a definition name, not a bank key.
        PlacedDoor door = new(
            declared,
            declared.PositionStart,
            RetailOrientation.ToMatrix(baseOrientation),
            stopX,
            stopY,
            slider,
            new DoorState(declared.MaxAngle, openTime <= 0f ? 1f : openTime, slides, slider, length),
            declared.OpenSound,
            declared.CloseSound,
            declared.MoveSound);
        return (door, baseOrientation);
    }

    /// <summary>Sets where a door's sounds are reported, or <see langword="null"/> to make them silent.</summary>
    /// <param name="sink">Called during <see cref="Tick(PlacedDoor)"/> for each moment a door makes a noise.</param>
    /// <remarks>
    /// A callback because <c>OpenIGI.Game</c> may not reach an audio backend. Whoever takes it owns the
    /// travelling voice: <see cref="DoorSoundEvent.StartedMoving"/> and
    /// <see cref="DoorSoundEvent.StoppedMoving"/> come in pairs, one pair per journey.
    /// </remarks>
    public void SetSoundSink(Action<PlacedDoor, DoorSoundEvent>? sink) => _sounds = sink;

    /// <summary>
    /// Sets the mid-swing doorway ray: a callback answering whether a human currently stands inside the
    /// door's opening, or <see langword="null"/> to disable the check.
    /// </summary>
    /// <param name="query">
    /// Called each tick the door moves, with the two world-space doorway endpoints; returns
    /// <see langword="true"/> when a human blocks the opening.
    /// </param>
    /// <remarks>
    /// This is the retail handler's doorway ray (0x446D10 at 0x447302): a moving door that finds a
    /// human standing in its opening is forced back open, so it never seals while somebody is in the
    /// doorway. The elevator's authored <c>Can start</c> (typically <c>Door.isClosed</c>) therefore
    /// stays false while the door is blocked, which is how the original keeps a lift from running while
    /// a human is inside it or blocks its door. The caller owns finding humans — typically the player
    /// body and the AI soldiers' bodies.
    /// </remarks>
    public void SetDoorwayBlockedQuery(Func<Vector3d, Vector3d, bool>? query) => _doorwayBlocked = query;

    /// <summary>Advances every door one tick, each after the doors inside it, as the level walk does.</summary>
    public void Tick()
    {
        for (int i = _doors.Count - 1; i >= 0; i--)
        {
            Tick(_doors[i]);
        }
    }

    /// <summary>Puts every door into the level's update walk, each where the tree places it.</summary>
    /// <param name="walker">The level walk.</param>
    /// <param name="ticked">Runs after a door has advanced, for the owner's collision and sight updates.</param>
    /// <remarks>The door's update (<c>0x446D10</c>) walks its children first, then runs its own state machine.</remarks>
    public void AttachTo(LevelUpdateWalker walker, Action<PlacedDoor>? ticked = null)
    {
        ArgumentNullException.ThrowIfNull(walker);
        foreach (PlacedDoor door in _doors)
        {
            if (Level.LevelTaskClass.TryAs(door.Task, walker, out DoorTask? task))
            {
                task.Doors = this;
                task.Door = door;
            }
        }

        _inWalk = true;
        _ticked = ticked;
    }

    /// <summary>A door's own update, where the level's walk reaches its task, after its children (<c>0x446D10</c>).</summary>
    /// <param name="door">The door.</param>
    internal void RunFromWalk(PlacedDoor door)
    {
        if (_inWalk)
        {
            Tick(door);
            _ticked?.Invoke(door);
        }
    }

    /// <inheritdoc/>
    public void MarkLevelStart()
    {
        foreach (PlacedDoor door in _doors)
        {
            door.State.MarkLevelStart();
        }
    }

    /// <inheritdoc/>
    /// <remarks>Each door as the match began, re-placed by its next tick; a sound it was making stops.</remarks>
    public void ResetToLevelStart(ObjectCollisionIndex? solids)
    {
        foreach (PlacedDoor door in _doors)
        {
            door.State.ResetToLevelStart();
            door.IsSounding = false;
        }
    }

    /// <summary>Advances one door one tick.</summary>
    /// <param name="door">The door.</param>
    public void Tick(PlacedDoor door)
    {
        ArgumentNullException.ThrowIfNull(door);

        // 0x446D10: its expressions and lock, as it stood after its last update, then its motion.
        if (door.Conditions is { } conditions)
        {
            conditions();
        }
        else
        {
            door.State.RunConditions(null, null);
        }

        // A door moved by its parent this tick was already stepped; its blend starts from where its own tick left it.
        door.KeepPrevious();
        door.State.Tick();

        // 0x446D10: a swing rebuilds the orientation, a slide re-places the door; either way the door's frame in its
        // parent is taken again and the doors inside it follow.
        if (door.State.SwungThisTick)
        {
            door.Swing();
        }

        if (door.State.SlidThisTick)
        {
            door.Place();
        }

        door.Sync();

        if (door.State.TransformChangedThisTick)
        {
            door.Capture();
        }

        // The doorway ray: a door that moved this tick and finds a human in its opening is sent back fully open, and is
        // neither open nor closed for the tick -- the retail re-open at 0x447323, which keeps a blocked elevator door from
        // ever reaching isClosed.
        if (_doorwayBlocked is not null && (door.State.SwungThisTick || door.State.SlidThisTick))
        {
            (Vector3d a, Vector3d b) = door.DoorwaySegment;
            if (_doorwayBlocked(a, b))
            {
                door.State.Reopen();
            }
        }

        if (door.State.TransformChangedThisTick)
        {
            door.MoveChildren();
        }

        if (_sounds is null)
        {
            return;
        }

        DoorState state = door.State;

        // The two rising edges the original tests, in its order: fully open first, then fully
        // closed (0x446D10 at 0x4473F5 and 0x447422).
        if (state.IsFullyOpen && !state.WasFullyOpen)
        {
            _sounds(door, DoorSoundEvent.Opened);
        }

        if (state.IsFullyClosed && !state.WasFullyClosed)
        {
            _sounds(door, DoorSoundEvent.Closed);
        }

        // And the travelling sound, which is a level rather than an edge: the original starts it
        // whenever the door is at neither end and its handle slot is empty.
        bool moving = !state.IsFullyOpen && !state.IsFullyClosed;
        if (moving == door.IsSounding)
        {
            return;
        }

        door.IsSounding = moving;
        _sounds(door, moving ? DoorSoundEvent.StartedMoving : DoorSoundEvent.StoppedMoving);
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

    /// <summary>Gets the live draw transform for a door placement.</summary>
    public bool TryGetDrawTransform(GameTask task, out Vector3d position, out Matrix3f orientation)
        => TryGetDrawTransform(task, 1f, smooth: false, out position, out orientation);

    /// <summary>Whether a task is a door, whose placement this registry draws (see <see cref="OpenIGI.Engine.Tasks.TaskTree.PlacementEpoch"/>).</summary>
    /// <param name="task">The placement's owner.</param>
    /// <returns><see langword="true"/> for a door's task.</returns>
    public bool Claims(GameTask task) => _doorsByTask.ContainsKey(task);

    /// <summary>Gets a door transform presented between its last two simulation states.</summary>
    public bool TryGetDrawTransform(
        GameTask task,
        float tickFraction,
        bool smooth,
        out Vector3d position,
        out Matrix3f orientation)
    {
        if (_doorsByTask.TryGetValue(task, out PlacedDoor? door))
        {
            (position, orientation) = door.PresentedPose(tickFraction, smooth);
            return true;
        }

        position = default;
        orientation = default;
        return false;
    }
}
