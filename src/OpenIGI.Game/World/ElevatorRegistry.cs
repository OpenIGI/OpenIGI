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
    /// <summary>Horizontal radius (engine units) around the platform centre a player must be within.</summary>
    public const double PlayerInsideHorizontalRadius = 8192.0;

    /// <summary>Vertical tolerance (engine units) above/below the platform surface a player must be within.</summary>
    public const double PlayerInsideVerticalTolerance = 16384.0;

    private readonly List<PlacedElevator> _elevators = [];
    private readonly List<ElevatorCommand> _commands = [];
    private Action<PlacedElevator, ElevatorSoundEvent>? _sounds;
    private Func<Vector3d>? _playerPositionProvider;

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

    /// <summary>Sets the provider that returns the human player's current world position.</summary>
    /// <remarks>
    /// The lift must not move without the player inside the cab. This provider lets the start gate
    /// read the player's position each tick; without it the player-inside check is skipped (tests that
    /// do not wire a player still pass).
    /// </remarks>
    public void SetPlayerPositionProvider(Func<Vector3d>? provider) => _playerPositionProvider = provider;

    public static ElevatorRegistry Build(GameTask root, ScriptVarRegistry variables) =>
        Build(root, variables, doors: null);

    public static ElevatorRegistry Build(
        GameTask root,
        ScriptVarRegistry variables,
        DoorRegistry? doors)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(variables);
        _ = doors;

        ElevatorRegistry registry = new();
        LevelExpressionRuntime expressions = new(variables);
        Collect(root, root, registry, expressions, variables);
        return registry;
    }

    /// <summary>True when every door parented to the lift is fully shut.</summary>
    public static bool AreAllDoorsFullyClosed(GameTask elevator, DoorRegistry doors)
    {
        ArgumentNullException.ThrowIfNull(elevator);
        ArgumentNullException.ThrowIfNull(doors);
        foreach (PlacedDoor door in doors.Doors)
        {
            if (IsDescendantOf(door.Task, elevator) && !door.State.IsFullyClosed)
            {
                return false;
            }
        }

        return true;
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
            if (command.TryRequestFromExteriorSwitch(switchTask))
            {
                return true;
            }

            if (IsDescendantOf(switchTask, command.Elevator.Task)
                && command.RequestFromSwitch())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Evaluates an authored wanted-floor expression for a lift for diagnostics.</summary>
    /// <param name="elevatorTask">The lift task to inspect.</param>
    /// <param name="floorIndex">The wanted floor index.</param>
    /// <returns>The expression result, or <see langword="null"/> when the lift/floor is unknown.</returns>
    public bool? EvaluateWantedFloor(DeclaredTask elevatorTask, int floorIndex)
    {
        foreach (ElevatorCommand command in _commands)
        {
            if (ReferenceEquals(command.Elevator.Task, elevatorTask)
                && floorIndex >= 0
                && floorIndex < command.WantedFloors.Count)
            {
                return command.WantedFloors[floorIndex]();
            }
        }

        return null;
    }

    /// <summary>
    /// Writes this tick's elevator and non-door child collision before the player settles.
    /// </summary>
    public int ApplyCollision(ObjectCollisionIndex solids)
    {
        ArgumentNullException.ThrowIfNull(solids);
        int updated = 0;
        foreach (PlacedElevator elevator in _elevators)
        {
            if (!elevator.State.TransformChangedThisTick)
            {
                continue;
            }

            Vector3d increment = (elevator.State.Position - elevator.State.PreviousPosition)
                / WorldScene.UnitsPerMetre;
            if (solids.UpdateTransform(elevator.Task, elevator.DrawPosition, elevator.Orientation))
            {
                updated++;
            }

            updated += solids.UpdateDescendantTranslation(elevator.Task, increment, IsDoorTask);
        }

        return updated;
    }

    /// <summary>Gets the displacement to apply to a passenger standing on a moving platform.</summary>
    public bool TryGetPassengerDisplacement(Vector3d passenger, out Vector3d displacement) =>
        TryGetPassengerDisplacement(passenger, solids: null, out displacement);

    /// <summary>Gets the displacement for a passenger, using live collision when available.</summary>
    /// <remarks>
    /// Retail attaches from BoneDynCubeObj / ground-hit owner (0x4626E1 / 0x463310), not a 1 m
    /// sphere around the task origin.
    /// </remarks>
    public bool TryGetPassengerDisplacement(
        Vector3d passenger,
        ObjectCollisionIndex? solids,
        out Vector3d displacement)
    {
        foreach (PlacedElevator elevator in _elevators)
        {
            Vector3d delta = elevator.State.Position - elevator.State.PreviousPosition;
            if (delta == Vector3d.Zero || !IsPassengerOn(elevator, passenger, solids))
            {
                continue;
            }

            displacement = delta;
            return true;
        }

        displacement = default;
        return false;
    }

    /// <summary>Checks whether a world position sits inside any elevator's cab volume.</summary>
    /// <remarks>
    /// A player is "inside" when they are within the platform footprint (a horizontal radius of
    /// <see cref="PlayerInsideHorizontalRadius"/> around the platform centre) and within
    /// <see cref="PlayerInsideVerticalTolerance"/> above the platform surface. Retail only boards
    /// the lift once the player is standing in the cab — you cannot call the car from the hallway and
    /// ride it.
    /// </remarks>
    public bool IsPlayerInsideElevator(Vector3d playerPosition, out PlacedElevator? containingElevator)
    {
        foreach (PlacedElevator elevator in _elevators)
        {
            Vector3d platform = elevator.State.Position;
            double dx = playerPosition.X - platform.X;
            double dy = playerPosition.Y - platform.Y;
            double dz = playerPosition.Z - platform.Z;
            double horizontal = Math.Sqrt((dx * dx) + (dy * dy));
            if (horizontal <= PlayerInsideHorizontalRadius && dz >= -PlayerInsideVerticalTolerance
                && dz <= PlayerInsideVerticalTolerance)
            {
                containingElevator = elevator;
                return true;
            }
        }

        containingElevator = null;
        return false;
    }

    /// <summary>Gets the world transform for an elevator-owned task.</summary>
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
                List<string> wantedExpressions = Enumerable.Range(0, 10)
                    .Select(i => ReadString(declared, $"Wanted floor {i}"))
                    .ToList();
                List<Func<bool>> wantedFloors = wantedExpressions
                    .Select(expression => (Func<bool>)(() => expressions.EvaluateCondition(
                        expression, emptyValue: false)))
                    .ToList();
                string canStart = ReadString(declared, "Can start");
                string floorUp = ReadString(declared, "Floor up");
                string floorDown = ReadString(declared, "Floor down");
                List<ExteriorSwitchCall> exteriorCalls = FindExteriorSwitchCalls(
                    root, declared, floors, wantedExpressions);
                registry._commands.Add(new ElevatorCommand(
                    elevator,
                    () => expressions.EvaluateCondition(canStart, emptyValue: true),
                    () => expressions.EvaluateCondition(floorUp, emptyValue: false),
                    () => expressions.EvaluateCondition(floorDown, emptyValue: false),
                    wantedFloors,
                    exteriorCalls));
                // Elevator_Activate writes a double spline coordinate at +0x1A0 and binds %s.vFloor.
                // An integer last-floor export makes landing-door "vFloor == N" true on the first step.
                variables.BindReal(
                    declared,
                    "vFloor",
                    () => state.FloorPosition,
                    value => state.RequestFloor((int)value));
                variables.BindBool(declared, "isMoving", () => state.HasMovementRequest, _ => { });
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
        List<Func<bool>> WantedFloors,
        List<ExteriorSwitchCall> ExteriorCalls)
    {
        public PlacedElevator Elevator { get; } = Elevator;
        public Func<bool> CanStart { get; } = CanStart;
        public Func<bool> FloorUp { get; } = FloorUp;
        public Func<bool> FloorDown { get; } = FloorDown;
        public List<Func<bool>> WantedFloors { get; } = WantedFloors;
        public bool[] LastWantedFloors { get; } = new bool[WantedFloors.Count];
        public List<ExteriorSwitchCall> ExteriorCalls { get; } = ExteriorCalls;

        public bool TryRequestFromExteriorSwitch(DeclaredTask switchTask)
        {
            foreach (ExteriorSwitchCall call in ExteriorCalls)
            {
                if (ReferenceEquals(call.Switch, switchTask))
                {
                    Elevator.State.RequestFloor(call.FloorIndex);
                    return true;
                }
            }

            return false;
        }

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

    private sealed record ExteriorSwitchCall(DeclaredTask Switch, int FloorIndex);

    private static bool IsPassengerOn(
        PlacedElevator elevator,
        Vector3d passenger,
        ObjectCollisionIndex? solids)
    {
        if (solids is not null)
        {
            Vector3d shin = new(passenger.X, passenger.Y, passenger.Z + 2048.0);
            if (solids.IsPointInTaskVolume(passenger, elevator.Task)
                || solids.IsPointInTaskVolume(shin, elevator.Task)
                || (solids.TryFindContainingDynamicOwner(passenger, includeInactiveOwners: false, out GameTask? owner)
                    && owner is not null
                    && IsSelfOrDescendant(owner, elevator.Task))
                || (solids.TryFindContainingDynamicOwner(shin, includeInactiveOwners: false, out GameTask? lifted)
                    && lifted is not null
                    && IsSelfOrDescendant(lifted, elevator.Task)))
            {
                return true;
            }
        }

        Vector3d previous = elevator.State.PreviousPosition;
        double dx = passenger.X - previous.X;
        double dy = passenger.Y - previous.Y;
        double dz = passenger.Z - previous.Z;
        double horizontal = Math.Sqrt((dx * dx) + (dy * dy));
        return horizontal <= PlayerInsideHorizontalRadius
            && dz >= -PlayerInsideVerticalTolerance
            && dz <= PlayerInsideVerticalTolerance;
    }

    private static bool IsDoorTask(GameTask task) =>
        task is DeclaredTask declared
        && string.Equals(declared.Type.Name, "Door", StringComparison.Ordinal);

    private static bool IsSelfOrDescendant(GameTask owner, GameTask ancestor)
    {
        for (GameTask? current = owner; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static List<ExteriorSwitchCall> FindExteriorSwitchCalls(
        GameTask root,
        DeclaredTask elevator,
        List<ElevatorFloor> floors,
        List<string> wantedExpressions)
    {
        string elevatorName = elevator.Name;
        string[] doorExpressions = Descendants(root)
            .OfType<DeclaredTask>()
            .Where(static task => string.Equals(task.Type.Name, "Door", StringComparison.Ordinal))
            .SelectMany(task =>
            {
                string[] expressions =
                [
                    ReadString(task, "Close door expression"),
                    ReadString(task, "Open door expression"),
                ];
                bool owned = IsDescendantOf(task, elevator);
                bool mentionsElevator = expressions.Any(expression =>
                    expression.Contains(elevatorName, StringComparison.Ordinal));
                return owned || mentionsElevator
                    ? expressions.Where(static expression => expression.Length > 0)
                    : [];
            })
            .ToArray();

        List<DeclaredTask> switches = Descendants(root)
            .OfType<DeclaredTask>()
            .Where(static task => string.Equals(task.Type.Name, "Switch", StringComparison.Ordinal))
            .ToList();
        Dictionary<DeclaredTask, int> floorsBySwitch = new(ReferenceEqualityComparer.Instance);
        for (int floor = 0; floor < wantedExpressions.Count; floor++)
        {
            string wanted = wantedExpressions[floor];
            if (wanted.Length == 0)
            {
                continue;
            }

            foreach (DeclaredTask switchTask in switches)
            {
                if (IsDescendantOf(switchTask, elevator)
                    || !wanted.Contains($"{switchTask.Name}.isLastPressed", StringComparison.Ordinal))
                {
                    continue;
                }

                floorsBySwitch[switchTask] = floor;
            }
        }

        List<ExteriorSwitchCall> calls = [];
        foreach (DeclaredTask switchTask in switches)
        {
            if (IsDescendantOf(switchTask, elevator))
            {
                continue;
            }

            if (floorsBySwitch.TryGetValue(switchTask, out int authoredFloor))
            {
                calls.Add(new ExteriorSwitchCall(switchTask, authoredFloor));
                continue;
            }

            if (!doorExpressions.Any(expression => expression.Contains(
                    $"{switchTask.Name}.isLastPressed", StringComparison.Ordinal)))
            {
                continue;
            }

            Vector3d position = ReadPosition(switchTask, "Position", default);
            int floor = 0;
            double distance = double.MaxValue;
            for (int i = 0; i < floors.Count; i++)
            {
                double candidate = Vector3d.Distance(position, floors[i].Position);
                if (candidate < distance)
                {
                    distance = candidate;
                    floor = i;
                }
            }

            calls.Add(new ExteriorSwitchCall(switchTask, floor));
        }

        return calls;
    }

    private static IEnumerable<GameTask> Descendants(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            yield return child;
            foreach (GameTask descendant in Descendants(child))
            {
                yield return descendant;
            }
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
