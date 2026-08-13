using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

#pragma warning disable CS1591 // Public members are documented by the elevator registry contract.

namespace OpenIGI.Game.World;

/// <summary>One placed <c>Elevator</c> task and its authored spline floors.</summary>
public sealed record PlacedElevator(
    GameTask Task,
    ElevatorState State,
    Matrix3f Orientation,
    string Model,
    string StartSound,
    string StopSound,
    string MoveSound)
{
    /// <summary>Gets or sets whether the travelling sound is active.</summary>
    public bool IsSounding { get; set; }
    /// <summary>Gets the current platform position in renderer metres.</summary>
    public Vector3d DrawPosition => State.Position / WorldScene.UnitsPerMetre;

    /// <summary>Gets a presentation position between the previous and current simulation ticks.</summary>
    public Vector3d DrawPositionAt(float tickFraction, bool smooth)
    {
        double alpha = smooth ? Math.Clamp(tickFraction, 0f, 1f) : 1.0;
        return (State.PreviousPosition + ((State.Position - State.PreviousPosition) * alpha))
            / WorldScene.UnitsPerMetre;
    }
}

/// <summary>Collects, advances and presents the level's <c>Elevator</c> tasks.</summary>
public sealed class ElevatorRegistry
{
    private readonly List<PlacedElevator> _elevators = [];
    private readonly List<ElevatorCommand> _commands = [];
    private Action<PlacedElevator, ElevatorSoundEvent>? _sounds;

    public ElevatorRegistry()
    {
    }

    public ElevatorRegistry(IEnumerable<PlacedElevator> elevators)
    {
        ArgumentNullException.ThrowIfNull(elevators);
        _elevators.AddRange(elevators);
    }

    public IReadOnlyList<PlacedElevator> Elevators => _elevators;

    /// <summary>Sets the callback for elevator start/stop sounds.</summary>
    public void SetSoundSink(Action<PlacedElevator, ElevatorSoundEvent>? sink) => _sounds = sink;

    public static ElevatorRegistry Build(GameTask root, ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(variables);

        ElevatorRegistry registry = new();
        LevelExpressionRuntime expressions = new(variables);
        Collect(root, root, registry, expressions, variables);
        return registry;
    }

    public void Tick()
    {
        foreach (ElevatorCommand command in _commands)
        {
            bool wasMoving = command.Elevator.State.IsMoving;
            for (int i = 0; i < command.WantedFloors.Count; i++)
            {
                bool wanted = command.WantedFloors[i]();
                if (wanted && !command.LastWantedFloors[i])
                {
                    command.Elevator.State.RequestFloor(i);
                }

                command.LastWantedFloors[i] = wanted;
            }

            command.Elevator.State.Tick(
                command.CanStart(),
                command.FloorUp(),
                command.FloorDown());

            if (_sounds is not null && wasMoving != command.Elevator.State.IsMoving)
            {
                command.Elevator.IsSounding = command.Elevator.State.IsMoving;
                _sounds(command.Elevator, command.Elevator.IsSounding
                    ? ElevatorSoundEvent.StartedMoving
                    : ElevatorSoundEvent.StoppedMoving);
            }
        }
    }

    /// <summary>Converts an elevator-owned switch pulse into its authored floor request.</summary>
    /// <remarks>
    /// The switch task remains a normal <c>Switch</c>: it publishes its three-tick script pulse, but
    /// the lift must also receive the request immediately. Door children are deliberately not involved
    /// in this route; their own interaction dispatch remains responsible for opening a door.
    /// </remarks>
    public bool RequestFromSwitch(DeclaredTask switchTask)
    {
        ArgumentNullException.ThrowIfNull(switchTask);

        foreach (ElevatorCommand command in _commands)
        {
            if (IsDescendantOf(switchTask, command.Elevator.Task)
                && command.RequestFromSwitch())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets the displacement to apply to a passenger standing on a moving platform.</summary>
    /// <param name="passenger">The passenger feet position in engine units.</param>
    /// <param name="displacement">The platform displacement in engine units.</param>
    /// <returns><see langword="true"/> when the passenger was on a platform during this tick.</returns>
    public bool TryGetPassengerDisplacement(Vector3d passenger, out Vector3d displacement)
    {
        foreach (PlacedElevator elevator in _elevators)
        {
            Vector3d previous = elevator.State.PreviousPosition;
            Vector3d current = elevator.State.Position;
            Vector3d horizontal = new(passenger.X - previous.X, passenger.Y - previous.Y, 0.0);
            if (Math.Abs(passenger.Z - previous.Z) <= 1024.0 && horizontal.Length <= 4096.0)
            {
                displacement = current - previous;
                if (displacement != Vector3d.Zero)
                {
                    return true;
                }
            }
        }

        displacement = default;
        return false;
    }

    public bool TryGetDrawTransform(
        GameTask task,
        Vector3d authoredPosition,
        Matrix3f authoredOrientation,
        float tickFraction,
        bool smooth,
        out Vector3d position,
        out Matrix3f orientation)
    {
        foreach (PlacedElevator elevator in _elevators)
        {
            if (ReferenceEquals(elevator.Task, task))
            {
                position = elevator.DrawPositionAt(tickFraction, smooth);
                orientation = elevator.Orientation;
                return true;
            }

            Vector3d displacement = GetDisplacement(elevator, tickFraction, smooth);
            if (IsDescendantOf(task, elevator.Task) && displacement != Vector3d.Zero)
            {
                position = authoredPosition + displacement;
                orientation = authoredOrientation;
                return true;
            }
        }

        position = default;
        orientation = default;
        return false;
    }

    /// <summary>Gets the interpolated translation of an elevator-owned child task.</summary>
    public bool TryGetDisplacement(
        GameTask task,
        float tickFraction,
        bool smooth,
        out Vector3d displacement)
    {
        foreach (PlacedElevator elevator in _elevators)
        {
            if (IsDescendantOf(task, elevator.Task))
            {
                displacement = GetDisplacement(elevator, tickFraction, smooth);
                return displacement != Vector3d.Zero;
            }
        }

        displacement = default;
        return false;
    }

    /// <summary>Gets the current world translation for an elevator-owned task in render metres.</summary>
    public Vector3d GetCurrentDisplacement(GameTask task)
    {
        foreach (PlacedElevator elevator in _elevators)
        {
            if (IsDescendantOf(task, elevator.Task))
            {
                return GetDisplacement(elevator, 1f, smooth: false);
            }
        }

        return default;
    }

    /// <summary>Gets the current world translation for an elevator-owned task in engine units.</summary>
    /// <remarks>
    /// World interactions and player activation queries use engine units. This is deliberately
    /// separate from <see cref="GetCurrentDisplacement"/>, which is used by render-space callers.
    /// </remarks>
    public Vector3d GetCurrentDisplacementInUnits(GameTask task)
    {
        foreach (PlacedElevator elevator in _elevators)
        {
            if (IsDescendantOf(task, elevator.Task))
            {
                return elevator.State.Position - elevator.State.StartPosition;
            }
        }

        return default;
    }

    private static Vector3d GetDisplacement(PlacedElevator elevator, float tickFraction, bool smooth)
    {
        double alpha = smooth ? Math.Clamp(tickFraction, 0f, 1f) : 1.0;
        Vector3d position = elevator.State.PreviousPosition
            + ((elevator.State.Position - elevator.State.PreviousPosition) * alpha);
        return (position - elevator.State.StartPosition) / WorldScene.UnitsPerMetre;
    }

    private static bool IsDescendantOf(GameTask task, GameTask ancestor)
    {
        for (GameTask? current = task.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static void Collect(
        GameTask root,
        GameTask task,
        ElevatorRegistry registry,
        LevelExpressionRuntime expressions,
        ScriptVarRegistry variables)
    {
        if (task is DeclaredTask declared
            && string.Equals(declared.Type.Name, "Elevator", StringComparison.Ordinal))
        {
            int? splineId = ReadInt(declared, "SplineObj taskid");
            GameTask? spline = splineId is int id ? FindById(root, id) : null;
            List<ElevatorFloor> floors = spline?.Children
                .OfType<DeclaredTask>()
                .Where(static child => string.Equals(child.Type.Name, "SplineObjWaypoint", StringComparison.Ordinal))
                .Select(child => new ElevatorFloor(
                    child.Id,
                    ReadPosition(child, "Position", default)))
                .ToList() ?? [];

            if (floors.Count > 0)
            {
                Vector3d start = ReadPosition(declared, "Position", floors[0].Position);
                ElevatorState state = new(
                    start,
                    floors,
                    ReadReal(declared, "Max speed", 1f) * WorldScene.UnitsPerMetre,
                    ReadReal(declared, "Speed inertia", 1f) * WorldScene.UnitsPerMetre,
                    ReadInt(declared, "Start floor task ID"));
                PlacedElevator elevator = new(
                    declared,
                    state,
                    ReadOrientation(declared, "Orientation"),
                    ReadString(declared, "Model"),
                    ReadString(declared, "Start sound"),
                    ReadString(declared, "Stop sound"),
                    ReadString(declared, "Move sound"));
                registry._elevators.Add(elevator);
                List<Func<bool>> wantedFloors = Enumerable.Range(0, 10)
                    .Select(i => ReadString(declared, $"Wanted floor {i}"))
                    .Select(expression => (Func<bool>)(() => expressions.EvaluateCondition(
                        expression, emptyValue: false)))
                    .ToList();
                string canStart = ReadString(declared, "Can start");
                string floorUp = ReadString(declared, "Floor up");
                string floorDown = ReadString(declared, "Floor down");
                registry._commands.Add(new ElevatorCommand(
                    elevator,
                    () => expressions.EvaluateCondition(canStart, emptyValue: true),
                    () => expressions.EvaluateCondition(floorUp, emptyValue: false),
                    () => expressions.EvaluateCondition(floorDown, emptyValue: false),
                    wantedFloors));
                variables.BindInt(declared, "vFloor", () => state.CurrentFloorIndex, value => state.RequestFloor(value));
                variables.BindBool(declared, "isMoving", () => state.IsMoving, _ => { });
            }
        }

        foreach (GameTask child in task.Children)
        {
            Collect(root, child, registry, expressions, variables);
        }
    }

    private sealed class ElevatorCommand(
        PlacedElevator Elevator,
        Func<bool> CanStart,
        Func<bool> FloorUp,
        Func<bool> FloorDown,
        List<Func<bool>> WantedFloors)
    {
        public PlacedElevator Elevator { get; } = Elevator;
        public Func<bool> CanStart { get; } = CanStart;
        public Func<bool> FloorUp { get; } = FloorUp;
        public Func<bool> FloorDown { get; } = FloorDown;
        public List<Func<bool>> WantedFloors { get; } = WantedFloors;
        public bool[] LastWantedFloors { get; } = new bool[WantedFloors.Count];

        public bool RequestFromSwitch()
        {
            for (int i = 0; i < WantedFloors.Count; i++)
            {
                if (WantedFloors[i]())
                {
                    Elevator.State.RequestFloor(i);
                    return true;
                }
            }

            return false;
        }
    }

    private static GameTask? FindById(GameTask task, int id)
    {
        if (task.Id == id)
        {
            return task;
        }

        foreach (GameTask child in task.Children)
        {
            GameTask? match = FindById(child, id);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static Vector3d ReadPosition(DeclaredTask task, string name, Vector3d fallback) =>
        task.TryGetValue(name, out object? value) && value is Vector3d position ? position : fallback;

    private static int? ReadInt(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is int integer ? integer : null;

    private static double ReadReal(DeclaredTask task, string name, double fallback) =>
        task.TryGetValue(name, out object? value) ? value switch
        {
            float single => single,
            double real => real,
            int integer => integer,
            _ => fallback,
        } : fallback;

    private static string ReadString(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is string text ? text : string.Empty;

    private static Matrix3f ReadOrientation(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is Vector3f angles
            ? WorldScene.ToOrientation(angles)
            : Matrix3f.Identity;
}
