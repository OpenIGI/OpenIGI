using OpenIGI.Core.Numerics;

#pragma warning disable CS1591 // Public members are documented by the elevator state contract.

namespace OpenIGI.Game.World;

/// <summary>A floor destination authored by an elevator spline waypoint.</summary>
public sealed record ElevatorFloor(int TaskId, Vector3d Position)
{
    /// <summary>
    /// Gets the spline length of the segment that arrives at this floor from the one before (<c>+248</c>, summed by
    /// <c>0x4E4530</c>), or <see langword="null"/> for the straight distance between them.
    /// </summary>
    public double? SegmentLength { get; init; }
}

/// <summary>
/// The deterministic movement state for one retail <c>Elevator</c> task.
/// </summary>
/// <remarks>
/// The native elevator updates its platform and children together through
/// <c>Elevator_UpdatePosition</c> and <c>Elevator_UpdateChildPosition</c>. The port keeps the same
/// single position source and exposes the previous sample for render interpolation and collision.
/// Floor expressions are commands: a held expression does not skip through every floor.
/// <para>
/// The original moves a spline parameter (<c>+388</c>) along the spline's segment lengths, its speed and brake in those
/// units (<c>0x43D6E0</c>), and places the car on the straight line between the knots (<c>Linear</c>, <c>0x43D960</c>). The
/// port keeps the car's position, and measures each step and the brake in the segment's own length: a bent segment's length
/// is its sampled cubic's, longer than the straight run, so the car covers the run more slowly. Every shipped lift that is
/// not <c>Linear</c> has straight segments, which the car follows knot to knot either way.
/// </para>
/// </remarks>
public sealed class ElevatorState
{
    private const double TickSeconds = 1.0 / 30.0;
    /// <summary>DAT_0053182c — remaining spline distance that switches UpdatePosition to the brake curve.</summary>
    private const double BrakeDistanceUnits = 16384.0;
    /// <summary>DAT_00531828 — <c>2^-14</c>, the native close-range speed gain.</summary>
    private const double BrakeGain = 1.0 / 16384.0;
    /// <summary>DAT_005313c8 — 0.1 m/s floor on the braking speed.</summary>
    private const double MinBrakeSpeedUnits = 0.1 * WorldScene.UnitsPerMetre;
    private readonly IReadOnlyList<ElevatorFloor> _floors;
    private readonly double _maxSpeed;
    private readonly double _inertia;
    private int _pendingDirection;
    private int? _pendingTargetFloor;
    private bool _lastUp;
    private bool _lastDown;
    private bool _lastCanStart;
    private double _speed;

    public ElevatorState(
        Vector3d startPosition,
        IReadOnlyList<ElevatorFloor> floors,
        double maxSpeedUnitsPerSecond,
        double speedInertiaUnitsPerSecondPerTick,
        int? startFloorTaskId = null)
    {
        ArgumentNullException.ThrowIfNull(floors);
        if (floors.Count == 0)
        {
            throw new ArgumentException("A lift must have at least one floor.", nameof(floors));
        }

        _floors = floors;
        _maxSpeed = Math.Max(0.0, maxSpeedUnitsPerSecond);
        _inertia = Math.Max(0.0, speedInertiaUnitsPerSecondPerTick);
        StartPosition = startPosition;
        Position = startPosition;
        PreviousPosition = startPosition;
        CurrentFloorIndex = FindStartFloor(startPosition, startFloorTaskId);
        LastFloorPosition = CurrentFloorIndex;
    }

    public Vector3d StartPosition { get; }
    public Vector3d Position { get; private set; }
    public Vector3d PreviousPosition { get; private set; }
    public int CurrentFloorIndex { get; private set; }
    public int FloorCount => _floors.Count;

    /// <summary>Gets the floors, in the spline's order.</summary>
    public IReadOnlyList<ElevatorFloor> Floors => _floors;
    public int? TargetFloorIndex { get; private set; }
    public bool IsMoving => TargetFloorIndex is not null;

    /// <summary>Previous tick's <see cref="FloorPosition"/>, exported as <c>vLastFloor</c> (+0x1A8).</summary>
    public double LastFloorPosition { get; private set; }

    /// <summary>Native <c>isStart</c> at +0x5D7 — true for the tick travel begins from a parked dest.</summary>
    public bool IsStart { get; private set; }

    /// <summary>Native <c>isStop</c> at +0x5D8 — true for the tick the spline reaches nWantedFloor.</summary>
    public bool IsStop { get; private set; }

    /// <summary>Native <c>nInactive</c> at +0x1BC — frames spent parked with a stable Can start.</summary>
    public int InactiveTicks { get; private set; }

    /// <summary>
    /// Native <c>nWantedFloor</c> at +0x19C: the destination index, including a request that is
    /// still waiting on Can start.
    /// </summary>
    public int WantedFloorIndex
    {
        get
        {
            if (TargetFloorIndex is int target)
            {
                return target;
            }

            if (_pendingTargetFloor is int pending)
            {
                return pending;
            }

            if (_pendingDirection != 0)
            {
                return Math.Clamp(CurrentFloorIndex + _pendingDirection, 0, _floors.Count - 1);
            }

            return CurrentFloorIndex;
        }
    }

    /// <summary>True while a travel request is queued or the car is on the spline (native +0x5D6).</summary>
    public bool HasMovementRequest => TargetFloorIndex is not null
        || (_pendingTargetFloor is int pending && pending != CurrentFloorIndex)
        || (_pendingDirection > 0 && CurrentFloorIndex + 1 < _floors.Count)
        || (_pendingDirection < 0 && CurrentFloorIndex > 0);

    /// <summary>The car's place between floors, exported as <c>vFloor</c> (<c>+416</c>, bound by <c>0x43DF20</c>).</summary>
    /// <remarks>
    /// Retail's update (<c>0x43D280</c>, at <c>0x43D4B1</c>) recomputes it every tick from the spline parameter <c>+388</c>
    /// against each floor's knot parameter: the first floor past the car, less how far short of it the car is as a share of
    /// the span. Within a segment the car's share of the straight run is its share of the segment's length, so the share
    /// by distance here is the same.
    /// </remarks>
    public double FloorPosition
    {
        get
        {
            if (TargetFloorIndex is not int target || target == CurrentFloorIndex)
            {
                return CurrentFloorIndex;
            }

            int direction = target > CurrentFloorIndex ? 1 : -1;
            int nextIndex = CurrentFloorIndex + direction;
            Vector3d start = _floors[CurrentFloorIndex].Position;
            Vector3d finish = _floors[nextIndex].Position;
            double segmentLength = Vector3d.Distance(start, finish);
            if (segmentLength == 0.0)
            {
                return nextIndex;
            }

            double fraction = Math.Clamp(Vector3d.Distance(start, Position) / segmentLength, 0.0, 1.0);
            return CurrentFloorIndex + (direction * fraction);
        }
    }

    public bool TransformChangedThisTick => Position != PreviousPosition;
    public double SpeedUnitsPerSecond => _speed;

    public void Tick(bool canStart, bool floorUp, bool floorDown, int? wantedFloor = null, bool enabled = true)
    {
        double paramBefore = FloorPosition;
        int wantedBefore = WantedFloorIndex;
        LastFloorPosition = paramBefore;
        PreviousPosition = Position;
        IsStart = false;
        IsStop = false;

        bool upPulse = floorUp && !_lastUp;
        bool downPulse = floorDown && !_lastDown;
        _lastUp = floorUp;
        _lastDown = floorDown;

        // The update (0x43D280): Floor-up / Floor-down pulses rewrite nWantedFloor first;
        // otherwise the last true isWantedFloorN wins. Both apply even while the car is travelling.
        if (upPulse)
        {
            int next = WantedFloorIndex + 1;
            if (next < _floors.Count)
            {
                RequestFloor(next);
            }
        }
        else if (downPulse)
        {
            int next = WantedFloorIndex - 1;
            if (next >= 0)
            {
                RequestFloor(next);
            }
        }
        else if (wantedFloor is int wanted)
        {
            RequestFloor(wanted);
        }

        if (TargetFloorIndex is null && canStart && _pendingTargetFloor is int requestedTarget)
        {
            _pendingTargetFloor = null;
            TargetFloorIndex = requestedTarget == CurrentFloorIndex ? null : requestedTarget;
        }

        if (TargetFloorIndex is null && canStart && _pendingDirection != 0)
        {
            int target = CurrentFloorIndex + _pendingDirection;
            _pendingDirection = 0;
            if (target >= 0 && target < _floors.Count)
            {
                TargetFloorIndex = target;
            }
        }

        if (TargetFloorIndex is not int targetIndex || targetIndex == CurrentFloorIndex)
        {
            if (TargetFloorIndex == CurrentFloorIndex)
            {
                TargetFloorIndex = null;
            }

            _speed = 0.0;
            FinishExports(paramBefore, wantedBefore, canStart);
            return;
        }

        if (_maxSpeed == 0.0)
        {
            _speed = 0.0;
            TargetFloorIndex = null;
            FinishExports(paramBefore, wantedBefore, canStart);
            return;
        }

        // The move (0x43D6E0) runs only while the lift's own "Can start" expression holds: the
        // update (0x43D280) skips it outright otherwise, mid-trip as much as at a floor, so the car
        // stands where it is and keeps its speed until the expression comes true again.
        if (!enabled)
        {
            FinishExports(paramBefore, wantedBefore, canStart);
            return;
        }

        // The move (0x43D6E0) walks the spline parameter toward nWantedFloor without stopping at
        // intermediate waypoints. Speed only zeros on the final node.
        double remaining = DistanceAlongFloors(CurrentFloorIndex, targetIndex);
        if (remaining <= BrakeDistanceUnits)
        {
            // Native writes the brake curve into +0x190 (m/s). Authored max speed is ~1 m/s; clamp
            // so a unit-scale test lift cannot be forced above its own Max speed.
            _speed = Math.Min(
                _maxSpeed,
                Math.Max(MinBrakeSpeedUnits, remaining * _maxSpeed * BrakeGain));
        }
        else
        {
            // 0x43D6E0 adds Speed inertia (+408) to the speed (+400) every tick until it reaches Max speed
            // (+404) -- both in metres a second, so the authored 0.99 gets mission 12's gondola to its 1.5 m/s on
            // its second tick. Read as an acceleration in metres a second squared, it had taken a second and a
            // half, and the gondola trailed the original's by over a metre up the whole first shot.
            _speed = _speed < _maxSpeed
                ? Math.Min(_maxSpeed, _speed + _inertia)
                : Math.Max(_maxSpeed, _speed - _inertia);
        }

        AdvanceToward(targetIndex, _speed * TickSeconds);
        FinishExports(paramBefore, wantedBefore, canStart);
    }

    private void FinishExports(double paramBefore, int wantedBefore, bool canStart)
    {
        // Elevator_Activate: isStart when we were sitting on the pre-tick nWantedFloor param and
        // then left it; isStop when this tick's +0x184 lands on the (possibly rewritten) dest.
        double paramAfter = FloorPosition;
        IsStart = paramBefore == wantedBefore && paramBefore != paramAfter;
        IsStop = paramAfter == WantedFloorIndex && paramBefore != paramAfter;

        if (paramAfter == WantedFloorIndex && !HasMovementRequest && canStart == _lastCanStart)
        {
            InactiveTicks++;
            return;
        }

        InactiveTicks = 0;
        _lastCanStart = canStart;
    }

    /// <summary>Requests a specific authored floor, subject to the normal start gate.</summary>
    /// <remarks>
    /// A request for the floor the lift is already standing at is a no-op and must not occupy the
    /// pending slot: such a request can be left over while the doors stay open (for example from an
    /// idle <c>vFloor</c> binding) and would otherwise silently swallow a later, real destination
    /// request such as a hall-call switch.
    /// </remarks>
    public void RequestFloor(int floorIndex)
    {
        if ((uint)floorIndex >= (uint)_floors.Count)
        {
            return;
        }

        _pendingDirection = 0;
        if (TargetFloorIndex is not null)
        {
            // Native writes +0x19C every Activate, so a cabin press mid-ride redirects the spline.
            bool parkedOnFloor = floorIndex == CurrentFloorIndex
                && Position == _floors[floorIndex].Position;
            TargetFloorIndex = parkedOnFloor ? null : floorIndex;
            if (parkedOnFloor)
            {
                _speed = 0.0;
            }

            return;
        }

        if (_pendingTargetFloor == CurrentFloorIndex)
        {
            _pendingTargetFloor = null;
        }

        if (floorIndex == CurrentFloorIndex)
        {
            return;
        }

        _pendingTargetFloor = floorIndex;
    }

    private void AdvanceToward(int targetIndex, double stepUnits)
    {
        int guard = _floors.Count + 1;
        while (stepUnits > 0.0 && TargetFloorIndex is int dest && dest != CurrentFloorIndex && guard-- > 0)
        {
            int nextIndex = dest > CurrentFloorIndex ? CurrentFloorIndex + 1 : CurrentFloorIndex - 1;
            Vector3d destination = _floors[nextIndex].Position;
            Vector3d delta = destination - Position;
            double distance = delta.Length;
            double scale = SegmentScale(CurrentFloorIndex, nextIndex);
            double along = distance * scale;
            if (distance == 0.0 || stepUnits >= along)
            {
                stepUnits = distance == 0.0 ? stepUnits : stepUnits - along;
                Position = destination;
                CurrentFloorIndex = nextIndex;
                if (nextIndex == dest)
                {
                    TargetFloorIndex = null;
                    _speed = 0.0;
                    return;
                }

                continue;
            }

            Position += (delta / distance) * (stepUnits / scale);
            return;
        }
    }

    private double DistanceAlongFloors(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex)
        {
            return Vector3d.Distance(Position, _floors[toIndex].Position);
        }

        int step = toIndex > fromIndex ? 1 : -1;
        double total = Vector3d.Distance(Position, _floors[fromIndex + step].Position) * SegmentScale(fromIndex, fromIndex + step);
        for (int index = fromIndex + step; index != toIndex; index += step)
        {
            total += Vector3d.Distance(_floors[index].Position, _floors[index + step].Position) * SegmentScale(index, index + step);
        }

        return total;
    }

    // The spline length of the segment between two neighbouring floors for each unit of the straight run between them.
    private double SegmentScale(int a, int b)
    {
        int arriving = Math.Max(a, b);
        if (a == b || _floors[arriving].SegmentLength is not double length)
        {
            return 1.0;
        }

        double chord = Vector3d.Distance(_floors[a].Position, _floors[b].Position);
        return chord > 0.0 && length > 0.0 ? length / chord : 1.0;
    }

    private int FindStartFloor(Vector3d startPosition, int? startFloorTaskId)
    {
        if (startFloorTaskId is int id)
        {
            for (int i = 0; i < _floors.Count; i++)
            {
                if (_floors[i].TaskId == id)
                {
                    return i;
                }
            }
        }

        int nearest = 0;
        double distance = double.MaxValue;
        for (int i = 0; i < _floors.Count; i++)
        {
            double candidate = Vector3d.Distance(startPosition, _floors[i].Position);
            if (candidate < distance)
            {
                distance = candidate;
                nearest = i;
            }
        }
        return nearest;
    }
}
