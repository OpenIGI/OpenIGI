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
    }

    public Vector3d StartPosition { get; }
    public Vector3d Position { get; private set; }
    public Vector3d PreviousPosition { get; private set; }
    public int CurrentFloorIndex { get; private set; }
    public int FloorCount => _floors.Count;
    public int? TargetFloorIndex { get; private set; }
    public bool IsMoving => TargetFloorIndex is not null;
    public bool TransformChangedThisTick => Position != PreviousPosition;
    public double SpeedUnitsPerSecond => _speed;

    public void Tick(bool canStart, bool floorUp, bool floorDown)
    {
        PreviousPosition = Position;

        bool upPulse = floorUp && !_lastUp;
        bool downPulse = floorDown && !_lastDown;
        _lastUp = floorUp;
        _lastDown = floorDown;

        if (TargetFloorIndex is null && _pendingTargetFloor is null && _pendingDirection == 0)
        {
            if (upPulse)
            {
                _pendingDirection = 1;
            }
            else if (downPulse)
            {
                _pendingDirection = -1;
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

        if (TargetFloorIndex is not int targetIndex)
        {
            _speed = 0.0;
            return;
        }

        int nextIndex = targetIndex > CurrentFloorIndex
            ? CurrentFloorIndex + 1
            : CurrentFloorIndex - 1;
        Vector3d destination = _floors[nextIndex].Position;
        Vector3d delta = destination - Position;
        double distance = delta.Length;
        if (distance == 0.0)
        {
            Arrive(nextIndex, destination);
            return;
        }

        _speed = Math.Min(_maxSpeed, _speed + (_inertia * TickSeconds));
        double step = _speed * TickSeconds;
        if (step >= distance || _maxSpeed == 0.0)
        {
            Arrive(nextIndex, destination);
            return;
        }

        Position += (delta / distance) * step;
    }

    /// <summary>Requests a specific authored floor, subject to the normal start gate.</summary>
    public void RequestFloor(int floorIndex)
    {
        if ((uint)floorIndex < (uint)_floors.Count)
        {
            _pendingDirection = 0;
            _pendingTargetFloor = floorIndex;
        }
    }

    private void Arrive(int reachedIndex, Vector3d destination)
    {
        Position = destination;
        CurrentFloorIndex = reachedIndex;
        if (TargetFloorIndex == reachedIndex)
        {
            TargetFloorIndex = null;
        }
        _speed = 0.0;
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
