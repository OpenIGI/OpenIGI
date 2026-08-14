using OpenIGI.Core.Numerics;

#pragma warning disable CS1591 // Public members are documented by the elevator state contract.

namespace OpenIGI.Game.World;

/// <summary>A floor destination authored by an elevator spline waypoint.</summary>
public sealed record ElevatorFloor(int TaskId, Vector3d Position);

/// <summary>
/// The deterministic movement state for one retail <c>Elevator</c> task.
/// </summary>
/// <remarks>
/// The native elevator updates its platform and children together through
/// <c>Elevator_UpdatePosition</c> and <c>Elevator_UpdateChildPosition</c>. The port keeps the same
/// single position source and exposes the previous sample for render interpolation and collision.
/// Floor expressions are commands: a held expression does not skip through every floor.
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
    private double _speed;

    public ElevatorState(
        Vector3d startPosition,
        IReadOnlyList<ElevatorFloor> floors,
        double maxSpeedUnitsPerSecond,
        double speedInertiaUnitsPerSecondSquared,
        int? startFloorTaskId = null)
    {
        ArgumentNullException.ThrowIfNull(floors);
        if (floors.Count == 0)
        {
            throw new ArgumentException("A lift must have at least one floor.", nameof(floors));
        }

        _floors = floors;
        _maxSpeed = Math.Max(0.0, maxSpeedUnitsPerSecond);
        _inertia = Math.Max(0.0, speedInertiaUnitsPerSecondSquared);
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
    public int? TargetFloorIndex { get; private set; }
    public bool IsMoving => TargetFloorIndex is not null;

    /// <summary>Previous tick's <see cref="FloorPosition"/>, exported as <c>vLastFloor</c> (+0x1A8).</summary>
    public double LastFloorPosition { get; private set; }

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

    /// <summary>Continuous spline coordinate exported as <c>vFloor</c> (Activate at 0x43C440).</summary>
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

    public void Tick(bool canStart, bool floorUp, bool floorDown)
    {
        LastFloorPosition = FloorPosition;
        PreviousPosition = Position;

        bool upPulse = floorUp && !_lastUp;
        bool downPulse = floorDown && !_lastDown;
        _lastUp = floorUp;
        _lastDown = floorDown;

        // Elevator_Activate (0x43C440): Floor-up / Floor-down pulses rewrite nWantedFloor first;
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
            return;
        }

        if (_maxSpeed == 0.0)
        {
            _speed = 0.0;
            TargetFloorIndex = null;
            return;
        }

        // Elevator_UpdatePosition (0x43C8A0) walks the spline parameter toward nWantedFloor
        // without stopping at intermediate waypoints. Speed only zeros on the final node.
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
            _speed = Math.Min(_maxSpeed, _speed + (_inertia * TickSeconds));
        }

        AdvanceToward(targetIndex, _speed * TickSeconds);
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
            if (distance == 0.0 || stepUnits >= distance)
            {
                stepUnits = distance == 0.0 ? stepUnits : stepUnits - distance;
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

            Position += (delta / distance) * stepUnits;
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
        double total = Vector3d.Distance(Position, _floors[fromIndex + step].Position);
        for (int index = fromIndex + step; index != toIndex; index += step)
        {
            total += Vector3d.Distance(_floors[index].Position, _floors[index + step].Position);
        }

        return total;
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
