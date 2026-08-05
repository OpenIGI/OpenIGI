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

    /// <summary>Gets the door's current drawable position in metres.</summary>
    public Vector3d DrawPosition
    {
        get
        {
            Vector3f local = new(StopX * State.SlideFraction, StopY * State.SlideFraction, StopZ * State.SlideFraction);
            // The renderer applies placements as column vectors (M·v); Matrix3f.Transform is v·M,
            // so transpose once to move a sliding door along its authored local axes.
            Vector3f offset = Orientation.Transposed().Transform(local);
            return new Vector3d(
                (Position.X / WorldScene.UnitsPerMetre) + offset.X,
                (Position.Y / WorldScene.UnitsPerMetre) + offset.Y,
                (Position.Z / WorldScene.UnitsPerMetre) + offset.Z);
        }
    }

    /// <summary>Gets the door's current drawable swing orientation.</summary>
    public Matrix3f DrawOrientation =>
        Orientation * Matrix3f.RotationZ(State.AngleDegrees * (MathF.PI / 180f));
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
    private Action<PlacedDoor, DoorSoundEvent>? _sounds;

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

            registry._doors.Add(new PlacedDoor(
                declared,
                position,
                orientation,
                stopX,
                stopY,
                stopZ,
                new DoorState(maxAngle, openTime <= 0f ? 1f : openTime, slides),
                ReadSound(declared, "Open sound"),
                ReadSound(declared, "Close sound"),
                ReadSound(declared, "Move sound")));
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

    /// <summary>Advances every door one tick.</summary>
    public void Tick()
    {
        foreach (PlacedDoor door in _doors)
        {
            door.State.Tick();

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
    {
        foreach (PlacedDoor door in _doors)
        {
            if (ReferenceEquals(door.Task, task))
            {
                position = door.DrawPosition;
                orientation = door.DrawOrientation;
                return true;
            }
        }
        position = default;
        orientation = default;
        return false;
    }
}
