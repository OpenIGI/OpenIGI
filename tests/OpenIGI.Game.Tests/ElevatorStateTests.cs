using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class ElevatorStateTests
{
    [Fact]
    public void UpPulseMovesExactlyOneFloorAndStopsAtTheAuthoredPosition()
    {
        ElevatorState elevator = new(
            startPosition: new Vector3d(0, 0, 0),
            floors:
            [
                new ElevatorFloor(10, new Vector3d(0, 0, 0)),
                new ElevatorFloor(20, new Vector3d(0, 0, 10)),
                new ElevatorFloor(30, new Vector3d(0, 0, 20)),
            ],
            maxSpeedUnitsPerSecond: 30,
            speedInertiaUnitsPerSecondSquared: 30);

        elevator.Tick(canStart: true, floorUp: true, floorDown: false);
        Assert.True(elevator.IsMoving);
        Assert.Equal(1, elevator.TargetFloorIndex);

        for (int i = 0; i < 30; i++)
        {
            elevator.Tick(canStart: true, floorUp: true, floorDown: false);
        }

        Assert.False(elevator.IsMoving);
        Assert.Equal(1, elevator.CurrentFloorIndex);
        Assert.Equal(new Vector3d(0, 0, 10), elevator.Position);

        // A held expression is one command, not an accidental ride through every floor.
        Assert.Equal(1, elevator.CurrentFloorIndex);
    }

    [Fact]
    public void DownPulseIsRememberedUntilCanStartBecomesTrue()
    {
        ElevatorState elevator = new(
            startPosition: new Vector3d(0, 0, 10),
            floors:
            [
                new ElevatorFloor(10, new Vector3d(0, 0, 0)),
                new ElevatorFloor(20, new Vector3d(0, 0, 10)),
            ],
            maxSpeedUnitsPerSecond: 30,
            speedInertiaUnitsPerSecondSquared: 30,
            startFloorTaskId: 20);

        elevator.Tick(canStart: false, floorUp: false, floorDown: true);
        Assert.False(elevator.IsMoving);

        elevator.Tick(canStart: true, floorUp: false, floorDown: true);
        Assert.True(elevator.IsMoving);
        Assert.Equal(0, elevator.TargetFloorIndex);
    }

    [Fact]
    public void PresentationRetainsThePreviousPosition()
    {
        ElevatorState elevator = new(
            startPosition: new Vector3d(0, 0, 0),
            floors: [new ElevatorFloor(10, new Vector3d(0, 0, 0)), new ElevatorFloor(20, new Vector3d(0, 0, 10))],
            maxSpeedUnitsPerSecond: 30,
            speedInertiaUnitsPerSecondSquared: 30);

        elevator.Tick(canStart: true, floorUp: true, floorDown: false);
        elevator.Tick(canStart: true, floorUp: true, floorDown: false);

        Assert.NotEqual(elevator.PreviousPosition, elevator.Position);
        Assert.True(elevator.TransformChangedThisTick);
    }

    [Fact]
    public void RequestedFloorWaitsForTheStartGate()
    {
        ElevatorState elevator = new(
            startPosition: new Vector3d(0, 0, 0),
            floors: [new ElevatorFloor(10, new Vector3d(0, 0, 0)), new ElevatorFloor(20, new Vector3d(0, 0, 10))],
            maxSpeedUnitsPerSecond: 30,
            speedInertiaUnitsPerSecondSquared: 30);

        elevator.RequestFloor(1);
        elevator.Tick(canStart: false, floorUp: false, floorDown: false);
        Assert.False(elevator.IsMoving);

        elevator.Tick(canStart: true, floorUp: false, floorDown: false);
        Assert.True(elevator.IsMoving);
        Assert.Equal(1, elevator.TargetFloorIndex);
    }

    [Fact]
    public void RequestedFarFloorTraversesEveryAuthoredWaypoint()
    {
        ElevatorState elevator = new(
            startPosition: new Vector3d(0, 0, 0),
            floors:
            [
                new ElevatorFloor(10, new Vector3d(0, 0, 0)),
                new ElevatorFloor(20, new Vector3d(10, 0, 0)),
                new ElevatorFloor(30, new Vector3d(20, 0, 0)),
            ],
            maxSpeedUnitsPerSecond: 30,
            speedInertiaUnitsPerSecondSquared: 30);

        elevator.RequestFloor(2);
        elevator.Tick(canStart: true, floorUp: false, floorDown: false);
        Assert.Equal(0, elevator.CurrentFloorIndex);
        Assert.Equal(2, elevator.TargetFloorIndex);

        for (int i = 0; i < 30; i++)
        {
            elevator.Tick(canStart: true, floorUp: false, floorDown: false);
            if (elevator.CurrentFloorIndex == 1)
            {
                break;
            }
        }

        Assert.Equal(1, elevator.CurrentFloorIndex);
        Assert.Equal(2, elevator.TargetFloorIndex);

        for (int i = 0; i < 30; i++)
        {
            elevator.Tick(canStart: true, floorUp: false, floorDown: false);
        }

        Assert.Equal(2, elevator.CurrentFloorIndex);
        Assert.False(elevator.IsMoving);
        Assert.Equal(new Vector3d(20, 0, 0), elevator.Position);
    }

    [Fact]
    public void ZeroSpeedElevatorHoldsItsPosition()
    {
        ElevatorState elevator = new(
            new Vector3d(0, 0, 0),
            [new ElevatorFloor(10, new Vector3d(0, 0, 0)), new ElevatorFloor(20, new Vector3d(0, 0, 10))],
            maxSpeedUnitsPerSecond: 0,
            speedInertiaUnitsPerSecondSquared: 30);

        elevator.RequestFloor(1);
        elevator.Tick(canStart: true, floorUp: false, floorDown: false);

        Assert.Equal(new Vector3d(0, 0, 0), elevator.Position);
        Assert.False(elevator.IsMoving);
    }

    [Fact]
    public void DuplicateRequestWhileMovingDoesNotCancelTheCurrentTrip()
    {
        ElevatorState elevator = new(
            startPosition: new Vector3d(0, 0, 0),
            floors:
            [
                new ElevatorFloor(10, new Vector3d(0, 0, 0)),
                new ElevatorFloor(20, new Vector3d(0, 0, 10)),
                new ElevatorFloor(30, new Vector3d(0, 0, 20)),
            ],
            maxSpeedUnitsPerSecond: 30,
            speedInertiaUnitsPerSecondSquared: 30);

        elevator.RequestFloor(2);
        elevator.Tick(canStart: true, floorUp: false, floorDown: false);
        elevator.Tick(canStart: true, floorUp: false, floorDown: false);
        Vector3d positionDuringTrip = elevator.Position;

        elevator.RequestFloor(1);
        elevator.Tick(canStart: true, floorUp: false, floorDown: false);

        Assert.Equal(2, elevator.TargetFloorIndex);
        Assert.True(elevator.Position != positionDuringTrip);
    }

    [Fact]
    public void RequestingTheCurrentFloorDoesNotStartMovement()
    {
        ElevatorState elevator = new(
            startPosition: new Vector3d(0, 0, 0),
            floors: [new ElevatorFloor(10, new Vector3d(0, 0, 0)), new ElevatorFloor(20, new Vector3d(0, 0, 10))],
            maxSpeedUnitsPerSecond: 30,
            speedInertiaUnitsPerSecondSquared: 30);

        elevator.RequestFloor(0);
        elevator.Tick(canStart: true, floorUp: false, floorDown: false);

        Assert.False(elevator.IsMoving);
        Assert.Equal(0, elevator.CurrentFloorIndex);
        Assert.Equal(new Vector3d(0, 0, 0), elevator.Position);
    }

    [Fact]
    public void PassengerDisplacementSkipsAnOverlappingStationaryElevator()
    {
        ElevatorState stationaryState = new(
            startPosition: new Vector3d(0, 0, 0),
            floors: [new ElevatorFloor(10, new Vector3d(0, 0, 0))],
            maxSpeedUnitsPerSecond: 30,
            speedInertiaUnitsPerSecondSquared: 30);
        ElevatorState movingState = new(
            startPosition: new Vector3d(0, 0, 0),
            floors: [
                new ElevatorFloor(20, new Vector3d(0, 0, 0)),
                new ElevatorFloor(30, new Vector3d(0, 0, 100)),
            ],
            maxSpeedUnitsPerSecond: 300,
            speedInertiaUnitsPerSecondSquared: 300);
        movingState.RequestFloor(1);
        movingState.Tick(canStart: true, floorUp: false, floorDown: false);

        ElevatorRegistry registry = new([
            new PlacedElevator(new OpenIGI.Engine.Tasks.LevelTask(), stationaryState,
                Matrix3f.Identity, "stationary", "", "", ""),
            new PlacedElevator(new OpenIGI.Engine.Tasks.LevelTask(), movingState,
                Matrix3f.Identity, "moving", "", "", ""),
        ]);

        Assert.True(registry.TryGetPassengerDisplacement(
            new Vector3d(0, 0, 0), out Vector3d displacement));
        Assert.Equal(movingState.Position - movingState.PreviousPosition, displacement);
        Assert.NotEqual(Vector3d.Zero, displacement);
    }

    [Fact]
    public void PassengerDisplacementCarriesFeetOffTheTaskOrigin()
    {
        ElevatorState movingState = new(
            startPosition: new Vector3d(0, 0, 0),
            floors: [
                new ElevatorFloor(20, new Vector3d(0, 0, 0)),
                new ElevatorFloor(30, new Vector3d(0, 0, 100)),
            ],
            maxSpeedUnitsPerSecond: 300,
            speedInertiaUnitsPerSecondSquared: 300);
        movingState.RequestFloor(1);
        movingState.Tick(canStart: true, floorUp: false, floorDown: false);

        ElevatorRegistry registry = new([
            new PlacedElevator(new OpenIGI.Engine.Tasks.LevelTask(), movingState,
                Matrix3f.Identity, "moving", "", "", ""),
        ]);

        Vector3d offCentre = new(6000.0, -4000.0, 2000.0);
        Assert.True(registry.TryGetPassengerDisplacement(offCentre, out Vector3d displacement));
        Assert.Equal(movingState.Position - movingState.PreviousPosition, displacement);
    }
}
