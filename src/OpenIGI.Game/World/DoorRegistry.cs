using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>One placed door — its state machine, where it stands and which placement it draws through.</summary>
/// <param name="Task">The exact <c>Door</c> task instance.</param>
/// <param name="Position">Where it is, in world units.</param>
/// <param name="Orientation">Its closed orientation.</param>
/// <param name="StopX">Authored local X translation at fully open, in metres.</param>
/// <param name="StopY">Authored local Y translation at fully open, in metres.</param>
/// <param name="StopZ">Authored slider translation at fully open, in metres.</param>
/// <param name="State">Its state machine.</param>
/// <param name="OpenSound">The definition played when the door reaches fully open.</param>
/// <param name="CloseSound">The definition played when it reaches fully closed.</param>
/// <param name="MoveSound">The definition played while it travels.</param>
public sealed record PlacedDoor(
    GameTask Task,
    Vector3d Position,
    Matrix3f Orientation,
    float StopX,
    float StopY,
    float StopZ,
    DoorState State,
    string OpenSound = "",
    string CloseSound = "",
    string MoveSound = "")
{
    /// <summary>Gets or sets whether the door's travelling sound is running.</summary>
    /// <remarks>
    /// The original keeps the travelling sound's task handle in the door itself, at <c>+736</c>, which
    /// is what tells it the sound is already going. This is that slot, reduced to the one bit the port
    /// needs — the voice itself belongs to whoever took <see cref="DoorRegistry.SetSoundSink"/>.
    /// </remarks>
    public bool IsSounding { get; set; }

    /// <summary>Gets the door's closed position in metres — the centre of its opening.</summary>
    public Vector3d ClosedPosition => new(
        Position.X / WorldScene.UnitsPerMetre,
        Position.Y / WorldScene.UnitsPerMetre,
        Position.Z / WorldScene.UnitsPerMetre);

    /// <summary>Gets the two world-space endpoints of the doorway, spanning the opening.</summary>
    /// <remarks>
    /// The doorway spans across the door's local X axis (the width axis) at the closed position. A
    /// human standing anywhere on this segment is inside the opening and keeps a closing door from
    /// sealing — the mid-swing blocking ray the retail handler casts between its two child-task
    /// positions (0x446D10 at 0x447302).
    /// </remarks>
    public (Vector3d A, Vector3d B) DoorwaySegment
    {
        get
        {
            Vector3d across = new(Orientation.Column1.X, Orientation.Column1.Y, Orientation.Column1.Z);
            double length = across.Length;
            if (length > 0.0)
            {
                across /= length;
            }

            Vector3d centre = ClosedPosition;
            Vector3d half = across * DoorwayHalfWidth;
            return (centre - half, centre + half);
        }
    }

    /// <summary>Gets or sets the doorway's half width, in metres, spanning the opening.</summary>
    /// <remarks>Defaults to a one-metre half width — a body standing in the opening is within it.</remarks>
    public double DoorwayHalfWidth { get; set; } = 1.0;

    /// <summary>Gets the door's current drawable position in metres.</summary>
    public Vector3d DrawPosition
        => DrawPositionAt(State.SlideFraction);

    /// <summary>Gets the door position at a supplied slide fraction.</summary>
    public Vector3d DrawPositionAt(float slideFraction)
    {
        Vector3f local = new(StopX * slideFraction, StopY * slideFraction, StopZ * slideFraction);
        // The renderer applies placements as column vectors (M·v); Matrix3f.Transform is v·M,
        // so transpose once to move a sliding door along its authored local axes.
        Vector3f offset = Orientation.Transposed().Transform(local);
        return new Vector3d(
            (Position.X / WorldScene.UnitsPerMetre) + offset.X,
            (Position.Y / WorldScene.UnitsPerMetre) + offset.Y,
            (Position.Z / WorldScene.UnitsPerMetre) + offset.Z);
    }

    /// <summary>Gets the door's pose as this frame presents it, between its last two ticks.</summary>
    /// <param name="tickFraction">How far the frame stands between the two simulation states.</param>
    /// <param name="smooth">Whether to interpolate at all; <see langword="false"/> takes the newer state.</param>
    /// <returns>The position in metres and the swing orientation.</returns>
    /// <remarks>
    /// The door moves on the thirty-hertz tick and is drawn far more often than that, so everything
    /// that has to sit on the door — its mesh, and any mark placed on it — has to ask for the same
    /// blended pose. Reading the tick state directly is what makes a thing on a moving door judder
    /// while the door itself glides.
    /// </remarks>
    public (Vector3d Position, Matrix3f Orientation) PresentedPose(float tickFraction, bool smooth)
    {
        float alpha = smooth ? Math.Clamp(tickFraction, 0f, 1f) : 1f;
        float slide = State.PreviousSlideFraction
            + ((State.SlideFraction - State.PreviousSlideFraction) * alpha);
        float angle = State.PreviousAngleDegrees
            + ((State.AngleDegrees - State.PreviousAngleDegrees) * alpha);
        return (DrawPositionAt(slide), DrawOrientationAt(angle));
    }

    /// <summary>Gets the door's current drawable swing orientation.</summary>
    public Matrix3f DrawOrientation =>
        DrawOrientationAt(State.AngleDegrees);

    /// <summary>Gets the door orientation at a supplied swing angle.</summary>
    public Matrix3f DrawOrientationAt(float angleDegrees) =>
        Orientation * Matrix3f.RotationZ(angleDegrees * (MathF.PI / 180f));
}

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
            float stopX = declared.TryGetValue("Position stop X", out object? rawX) && rawX is float xValue ? xValue : 0f;
            float stopY = declared.TryGetValue("Position stop Y", out object? rawY) && rawY is float yValue ? yValue : 0f;
            float stopZ = declared.TryGetValue("Position slider", out object? rawZ) && rawZ is float zValue ? zValue : 0f;
            bool slides = stopX != 0f || stopY != 0f || stopZ != 0f;
            Matrix3f orientation = Matrix3f.Identity;
            if (declared.TryGetValue("Orientation", out object? rawOrientation))
            {
                orientation = rawOrientation switch
                {
                    Vector3f angles => WorldScene.ToOrientation(angles),
                    float gamma => Matrix3f.RotationZ(gamma),
                    _ => Matrix3f.Identity,
                };
            }

            PlacedDoor door = new(
                declared,
                position,
                orientation,
                stopX,
                stopY,
                stopZ,
                new DoorState(maxAngle, openTime <= 0f ? 1f : openTime, slides),
                ReadSound(declared, "Open sound"),
                ReadSound(declared, "Close sound"),
                ReadSound(declared, "Move sound"));
            registry._doors.Add(door);
            registry._doorsByTask[declared] = door;
        }

        foreach (GameTask child in task.Children)
        {
            Collect(child, registry);
        }
    }

    /// <summary>Reads one of the door's three sound parameters.</summary>
    /// <remarks>
    /// All three are declared <c>String16</c> — the 16-byte parameter type the registrar's last three
    /// declarations use — and they hold a <b>definition</b> name, not a bank key.
    /// </remarks>
    private static string ReadSound(DeclaredTask declared, string parameter) =>
        declared.TryGetValue(parameter, out object? value) && value is string name ? name : string.Empty;

    /// <summary>Sets where a door's sounds are reported, or <see langword="null"/> to make them silent.</summary>
    /// <param name="sink">Called during <see cref="Tick"/> for each moment a door makes a noise.</param>
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
    /// Called for a closing door each tick, with the two world-space doorway endpoints; returns
    /// <see langword="true"/> when a human blocks the opening.
    /// </param>
    /// <remarks>
    /// This is the retail handler's doorway ray (0x446D10 at 0x447302): a closing door that finds a
    /// human standing in its opening is forced back open, so it never seals while somebody is in the
    /// doorway. The elevator's authored <c>Can start</c> (typically <c>Door.isClosed</c>) therefore
    /// stays false while the door is blocked, which is how the original keeps a lift from running while
    /// a human is inside it or blocks its door. The caller owns finding humans — typically the player
    /// body and the AI soldiers' bodies.
    /// </remarks>
    public void SetDoorwayBlockedQuery(Func<Vector3d, Vector3d, bool>? query) => _doorwayBlocked = query;

    /// <summary>Advances every door one tick.</summary>
    public void Tick()
    {
        foreach (PlacedDoor door in _doors)
        {
            door.State.Tick();

            // The mid-swing doorway ray: a door that is closing (state 3, moving with the latch clear)
            // and finds a human in its opening is forced back open instead of sealing — the retail
            // re-open at 0x447323. This keeps a blocked elevator door from ever reaching isClosed.
            if (_doorwayBlocked is not null && door.State.UseState == 3)
            {
                (Vector3d a, Vector3d b) = door.DoorwaySegment;
                if (_doorwayBlocked(a, b))
                {
                    door.State.CommandOpen();
                }
            }

            if (_sounds is null)
            {
                continue;
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
                continue;
            }

            door.IsSounding = moving;
            _sounds(door, moving ? DoorSoundEvent.StartedMoving : DoorSoundEvent.StoppedMoving);
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

    /// <summary>Gets the live draw transform for a door placement.</summary>
    public bool TryGetDrawTransform(GameTask task, out Vector3d position, out Matrix3f orientation)
        => TryGetDrawTransform(task, 1f, smooth: false, out position, out orientation);

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
