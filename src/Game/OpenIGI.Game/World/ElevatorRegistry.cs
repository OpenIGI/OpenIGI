using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using static OpenIGI.Game.World.DeclaredTaskValues;

#pragma warning disable CS1591 // Public members are documented by the elevator registry contract.

namespace OpenIGI.Game.World;

/// <summary>One placed <c>Elevator</c> task and its authored spline floors.</summary>
/// <param name="Task">The <c>Elevator</c> task.</param>
/// <param name="State">Where the car is and where it is going.</param>
/// <param name="AuthoredOrientation">The task's own <c>Orientation</c> (<c>+288</c>).</param>
/// <param name="Model">The car's model.</param>
/// <param name="StartSound">The sound it starts with.</param>
/// <param name="StopSound">The sound it stops with.</param>
/// <param name="MoveSound">The sound it travels with.</param>
/// <param name="SplineFrame">
/// The frame its spline turns that orientation by, or <see langword="null"/> for a car that keeps it -- every
/// <c>Linear</c> or <c>Force orientation</c> car.
/// </param>
public sealed record PlacedElevator(
    GameTask Task,
    ElevatorState State,
    Matrix3f AuthoredOrientation,
    string Model,
    string StartSound,
    string StopSound,
    string MoveSound,
    ElevatorSplineFrame? SplineFrame = null)
{
    /// <summary>Gets the orientation the car is drawn and collides with: <c>0x43D960</c>'s authored times frame.</summary>
    public Matrix3f Orientation => SplineFrame is { } frame
        ? AuthoredOrientation * frame.At(State.FloorPosition)
        : AuthoredOrientation;

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
    private readonly HashSet<PlacedElevator> _collisionPlaced = new(ReferenceEqualityComparer.Instance);

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
    /// Cabin requests must not move the lift without the player inside the cab. Authored exterior hall
    /// calls are the exception: those requests are allowed to bring an empty cab to the caller. This
    /// provider lets the start gate read the player's position each tick; without it the player-inside
    /// check is skipped (data-only tests that do not wire a player still pass).
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

    /// <summary>Steps every lift one tick, in the order they were declared.</summary>
    /// <remarks>
    /// For a lift no level walk reaches. A walked level steps each lift where the walk reaches it, with
    /// <see cref="TickElevator"/>, as the original's Elevator update (<c>0x43D280</c>) does.
    /// </remarks>
    public void Tick()
    {
        // NOBODY HAS TO BE ABOARD. 0x43D280 reads "Can start" (true when the level leaves it empty) and, when
        // it holds, moves the car (0x43D6E0) -- there is no occupancy test anywhere in the Elevator's tick.
        // Every lift the levels build gates itself in its own script instead: "Can start" wants its doors
        // closed, and only a switch asks for a floor. A car told to move by nothing but its script -- mission
        // 12's cutscene gondola ("Floor up" is the constant 1), mission 11's (CutScene_114.isRun), mission
        // 14's hacked cage -- used to stand still here until the player climbed in, which in a cutscene is
        // never.
        foreach (ElevatorCommand command in _commands)
        {
            TickCommand(command);
        }
    }

    /// <summary>Steps one lift one tick, where the level walk reaches it.</summary>
    /// <param name="elevator">The lift.</param>
    public void TickElevator(PlacedElevator elevator)
    {
        ArgumentNullException.ThrowIfNull(elevator);
        ElevatorCommand command = _commands.First(c => ReferenceEquals(c.Elevator, elevator));
        TickCommand(command);
    }

    private void TickCommand(ElevatorCommand command)
    {
        bool wasMoving = command.Elevator.State.HasMovementRequest;
        // The update (0x43D280) writes isWantedFloor[0..9] then, if there is no up/down pulse,
        // sets nWantedFloor to the last true index. A held expression is a destination, not a
        // rising-edge command, and a later floor overwrites an earlier one the same tick.
        int? wantedFloor = null;
        for (int i = 0; i < command.WantedFloors.Count; i++)
        {
            if (command.WantedFloors[i]())
            {
                wantedFloor = i;
            }
        }

        // Nobody has to be aboard: "Can start" alone gates the move (see Tick).
        bool enabled = command.CanStart();
        command.Elevator.State.Tick(
            enabled,
            command.FloorUp(),
            command.FloorDown(),
            wantedFloor,
            enabled);
        command.ClearExteriorRequestAfterStart();

        if (_sounds is not null)
        {
            if (command.Elevator.State.IsStart)
            {
                _sounds(command.Elevator, ElevatorSoundEvent.Started);
            }

            if (!wasMoving && command.Elevator.State.HasMovementRequest)
            {
                command.Elevator.IsSounding = true;
                _sounds(command.Elevator, ElevatorSoundEvent.Traveling);
            }

            if (command.Elevator.State.IsStop)
            {
                command.Elevator.IsSounding = false;
                _sounds(command.Elevator, ElevatorSoundEvent.Stopped);
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
            updated += ApplyCollision(solids, elevator);
        }

        return updated;
    }

    /// <summary>Writes one lift's collision, and its non-door children's, after it moved this tick.</summary>
    /// <param name="solids">The collision index.</param>
    /// <param name="elevator">The lift.</param>
    /// <returns>How many entries were moved.</returns>
    /// <remarks>
    /// The first pass places every car's solid at the orientation it is drawn with, which a car its spline turns does not
    /// share with its authored one -- mission 12's gondola stood a quarter turn out until it first moved, and the soldier
    /// standing in it found no floor to be parented to.
    /// </remarks>
    public int ApplyCollision(ObjectCollisionIndex solids, PlacedElevator elevator)
    {
        ArgumentNullException.ThrowIfNull(solids);
        ArgumentNullException.ThrowIfNull(elevator);
        if (!elevator.State.TransformChangedThisTick && !_collisionPlaced.Add(elevator))
        {
            return 0;
        }

        int updated = 0;
        Vector3d increment = (elevator.State.Position - elevator.State.PreviousPosition)
            / WorldScene.UnitsPerMetre;
        if (solids.UpdateTransform(elevator.Task, elevator.DrawPosition, elevator.Orientation))
        {
            updated++;
        }

        updated += solids.UpdateDescendantTranslation(elevator.Task, increment, IsDoorTask);
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
            if (TryGetPassengerDisplacement(elevator, passenger, solids, out displacement))
            {
                return true;
            }
        }

        displacement = default;
        return false;
    }

    /// <summary>Gets the displacement one lift gave a passenger standing on it this tick.</summary>
    /// <param name="elevator">The lift.</param>
    /// <param name="passenger">Where the passenger stands, in world units.</param>
    /// <param name="solids">The live collision, whose ground-hit owner says what the passenger stands on.</param>
    /// <param name="displacement">How far the lift carried it.</param>
    /// <returns>Whether the passenger rides this lift and it moved.</returns>
    public static bool TryGetPassengerDisplacement(
        PlacedElevator elevator,
        Vector3d passenger,
        ObjectCollisionIndex? solids,
        out Vector3d displacement)
    {
        ArgumentNullException.ThrowIfNull(elevator);
        Vector3d delta = elevator.State.Position - elevator.State.PreviousPosition;
        if (delta == Vector3d.Zero || !IsPassengerOn(elevator, passenger, solids))
        {
            displacement = default;
            return false;
        }

        displacement = delta;
        return true;
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
            int? splineId = IntStrictOrNull(declared, "SplineObj taskid");
            GameTask? spline = splineId is int id ? FindById(root, id) : null;
            List<DeclaredTask> waypoints = spline?.Children
                .OfType<DeclaredTask>()
                .Where(static child => string.Equals(child.Type.Name, "SplineObjWaypoint", StringComparison.Ordinal))
                .ToList() ?? [];
            List<ElevatorFloor> floors = [.. waypoints.Select((child, i) => new ElevatorFloor(
                child.Id,
                Vector(child, "Position", default))
            {
                SegmentLength = i > 0 && spline is DeclaredTask splineObj ? SegmentLength(splineObj, waypoints[i - 1], child) : null,
            })];

            if (floors.Count > 0)
            {
                Vector3d start = Vector(declared, "Position", floors[0].Position);
                ElevatorState state = new(
                    start,
                    floors,
                    DoubleFromNumber(declared, "Max speed", 1f) * WorldScene.UnitsPerMetre,
                    DoubleFromNumber(declared, "Speed inertia", 1f) * WorldScene.UnitsPerMetre,
                    IntStrictOrNull(declared, "Start floor task ID"));
                PlacedElevator elevator = new(
                    declared,
                    state,
                    ReadOrientation(declared, "Orientation"),
                    TextOrEmpty(declared, "Model"),
                    TextOrEmpty(declared, "Start sound"),
                    TextOrEmpty(declared, "Stop sound"),
                    TextOrEmpty(declared, "Move sound"),
                    ElevatorSplineFrame.For(declared, spline));
                registry._elevators.Add(elevator);
                List<string> wantedExpressions = Enumerable.Range(0, 10)
                    .Select(i => TextOrEmpty(declared, $"Wanted floor {i}"))
                    .ToList();
                List<Func<bool>> wantedFloors = wantedExpressions
                    .Select(expression => (Func<bool>)(() => expressions.EvaluateCondition(
                        expression, emptyValue: false, declared)))
                    .ToList();
                string canStart = TextOrEmpty(declared, "Can start");
                string floorUp = TextOrEmpty(declared, "Floor up");
                string floorDown = TextOrEmpty(declared, "Floor down");
                List<ExteriorSwitchCall> exteriorCalls = FindExteriorSwitchCalls(
                    root, declared, floors, wantedExpressions);
                registry._commands.Add(new ElevatorCommand(
                    elevator,
                    () => expressions.EvaluateCondition(canStart, emptyValue: true, declared),
                    () => expressions.EvaluateCondition(floorUp, emptyValue: false, declared),
                    () => expressions.EvaluateCondition(floorDown, emptyValue: false, declared),
                    wantedFloors,
                    exteriorCalls));
                // Elevator_BindExports (0x43D0E0): vFloor +0x1A0, vLastFloor +0x1A8,
                // isWantedFloorN +0x1B0, nInactive +0x1BC, nWantedFloor +0x19C,
                // isMoving +0x5D6, isStart +0x5D7, isStop +0x5D8.
                // Activate overwrites +0x1A0 after motion. A script store into vFloor does not
                // change nWantedFloor (+0x19C); that write used to retarget the car back to the
                // departure integer on the first moving tick.
                variables.BindReal(declared, "vFloor", () => state.FloorPosition, _ => { });
                variables.BindReal(declared, "vLastFloor", () => state.LastFloorPosition, _ => { });
                variables.BindInt(declared, "nWantedFloor", () => state.WantedFloorIndex, value =>
                {
                    if ((uint)value < (uint)state.FloorCount)
                    {
                        state.RequestFloor(value);
                    }
                });
                for (int floor = 0; floor < wantedFloors.Count; floor++)
                {
                    int captured = floor;
                    variables.BindBool(
                        declared,
                        $"isWantedFloor{captured}",
                        () => wantedFloors[captured](),
                        _ => { });
                }

                variables.BindBool(declared, "isMoving", () => state.HasMovementRequest, _ => { });
                variables.BindBool(declared, "isStart", () => state.IsStart, _ => { });
                variables.BindBool(declared, "isStop", () => state.IsStop, _ => { });
                variables.BindInt(declared, "nInactive", () => state.InactiveTicks, value => { });
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
        public List<ExteriorSwitchCall> ExteriorCalls { get; } = ExteriorCalls;
        public bool HasExteriorRequest { get; private set; }

        public void ClearExteriorRequestAfterStart()
        {
            if (Elevator.State.IsStart)
            {
                HasExteriorRequest = false;
            }
        }

        public bool TryRequestFromExteriorSwitch(DeclaredTask switchTask)
        {
            foreach (ExteriorSwitchCall call in ExteriorCalls)
            {
                if (ReferenceEquals(call.Switch, switchTask))
                {
                    Elevator.State.RequestFloor(call.FloorIndex);
                    if (call.FloorIndex != Elevator.State.CurrentFloorIndex)
                    {
                        HasExteriorRequest = true;
                    }

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
                    && owner.IsSelfOrDescendantOf(elevator.Task))
                || (solids.TryFindContainingDynamicOwner(shin, includeInactiveOwners: false, out GameTask? lifted)
                    && lifted is not null
                    && lifted.IsSelfOrDescendantOf(elevator.Task)))
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

    private static List<ExteriorSwitchCall> FindExteriorSwitchCalls(
        GameTask root,
        DeclaredTask elevator,
        List<ElevatorFloor> floors,
        List<string> wantedExpressions)
    {
        string elevatorName = elevator.Name;
        string[] doorExpressions = root.Descendants()
            .OfType<DeclaredTask>()
            .Where(static task => string.Equals(task.Type.Name, "Door", StringComparison.Ordinal))
            .SelectMany(task =>
            {
                string[] expressions =
                [
                    TextOrEmpty(task, "Close door expression"),
                    TextOrEmpty(task, "Open door expression"),
                ];
                bool owned = IsDescendantOf(task, elevator);
                bool mentionsElevator = expressions.Any(expression =>
                    expression.Contains(elevatorName, StringComparison.Ordinal));
                return owned || mentionsElevator
                    ? expressions.Where(static expression => expression.Length > 0)
                    : [];
            })
            .ToArray();

        List<DeclaredTask> switches = root.Descendants()
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

            Vector3d position = Vector(switchTask, "Position", default);
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

    // The length the waypoint's segment from the one before adds to the spline (0x500EF0 builds it by 0x4E4530): straight
    // where the spline's Linear Segments or the waypoint's Align (+418) says, else the cubic sampled N = min(the waypoint's
    // NumAreas, the spline's matrices a segment, 20) times. Automatic Orientation frames the waypoints by their neighbours,
    // which no lift that bends uses, so such a segment keeps its straight run.
    private static double? SegmentLength(DeclaredTask spline, DeclaredTask previous, DeclaredTask waypoint)
    {
        bool linear = BoolStrict(spline, "Linear Segments") || BoolStrict(waypoint, "Align");
        if (!linear && (BoolStrict(spline, "Automatic Orientation") || BoolStrict(waypoint, "Automatic Orientation")))
        {
            return null;
        }

        int count = Math.Min(Math.Min(IntStrictOrNull(waypoint, "NumAreas") ?? 20, IntStrictOrNull(spline, "Number of Matrices / Segment") ?? 20), 20);
        if (!linear && count < 1)
        {
            return null;
        }

        Vector3f a0 = previous.TryGetValue("Orientation", out object? r0) && r0 is Vector3f o0 ? o0 : default;
        Vector3f a1 = waypoint.TryGetValue("Orientation", out object? r1) && r1 is Vector3f o1 ? o1 : default;
        return RetailSplineSegment.Length(
            Vector(previous, "Position", default),
            Vector(waypoint, "Position", default),
            RetailSplineSegment.AnglesToMatrix(a0.X, a0.Y, a0.Z),
            RetailSplineSegment.AnglesToMatrix(a1.X, a1.Y, a1.Z),
            count,
            linear);
    }

    private static Matrix3f ReadOrientation(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is Vector3f angles
            ? WorldScene.ToOrientation(angles)
            : Matrix3f.Identity;
}
