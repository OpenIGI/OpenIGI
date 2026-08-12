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
}
