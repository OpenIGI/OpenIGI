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
            speedInertiaUnitsPerSecondPerTick: 30);

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
    public void ALongerSplineSegmentTakesTheCarLongerOverTheSameRun()
    {
        // The original moves its parameter along the segment's spline length (0x43D6E0), not along the straight run, and
        // places the car by its share of it: a run whose cubic is twice as long cruises twice as long. The brake is measured in
        // the same length, so the last of the run takes the same time either way.
        static int Ride(double? length)
        {
            ElevatorState elevator = new(
                startPosition: default,
                floors:
                [
                    new ElevatorFloor(10, default),
                    new ElevatorFloor(20, new Vector3d(0, 0, 100000)) { SegmentLength = length },
                ],
                maxSpeedUnitsPerSecond: 20000,
                speedInertiaUnitsPerSecondPerTick: 1000000);
            elevator.RequestFloor(1);
            int ticks = 0;
            do
            {
                elevator.Tick(canStart: true, floorUp: false, floorDown: false);
                ticks++;
                Assert.InRange(elevator.Position.Z, 0.0, 100000.0);
            }
            while (elevator.IsMoving && ticks < 10000);

            Assert.Equal(new Vector3d(0, 0, 100000), elevator.Position);
            return ticks;
        }

        int straight = Ride(null);
        int bent = Ride(200000);
        Assert.InRange(bent, (straight * 3) / 2, straight * 2);
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
            speedInertiaUnitsPerSecondPerTick: 30,
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
            speedInertiaUnitsPerSecondPerTick: 30);

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
            speedInertiaUnitsPerSecondPerTick: 30);

        elevator.RequestFloor(1);
        elevator.Tick(canStart: false, floorUp: false, floorDown: false);
        Assert.False(elevator.IsMoving);
        Assert.Equal(1, elevator.WantedFloorIndex);
        Assert.True(elevator.HasMovementRequest);

        elevator.Tick(canStart: true, floorUp: false, floorDown: false);
        Assert.True(elevator.IsMoving);
        Assert.Equal(1, elevator.TargetFloorIndex);
        Assert.Equal(1, elevator.WantedFloorIndex);
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
            speedInertiaUnitsPerSecondPerTick: 30);

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
        Assert.True(elevator.SpeedUnitsPerSecond > 0.0, "UpdatePosition does not zero speed on an intermediate node");

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
            speedInertiaUnitsPerSecondPerTick: 30);

        elevator.RequestFloor(1);
        elevator.Tick(canStart: true, floorUp: false, floorDown: false);

        Assert.Equal(new Vector3d(0, 0, 0), elevator.Position);
        Assert.False(elevator.IsMoving);
    }

    [Fact]
    public void ACabinRequestMidRideRedirectsNWantedFloor()
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
            speedInertiaUnitsPerSecondPerTick: 30);

        elevator.RequestFloor(2);
        elevator.Tick(canStart: true, floorUp: false, floorDown: false);
        elevator.Tick(canStart: true, floorUp: false, floorDown: false);
        Vector3d positionDuringTrip = elevator.Position;

        // Elevator_Activate overwrites +0x19C every frame; a later cabin press is a new destination.
        elevator.RequestFloor(1);
        elevator.Tick(canStart: true, floorUp: false, floorDown: false);

        Assert.Equal(1, elevator.TargetFloorIndex);
        Assert.True(elevator.Position != positionDuringTrip);
    }

    [Fact]
    public void RequestingTheCurrentFloorDoesNotStartMovement()
    {
        ElevatorState elevator = new(
            startPosition: new Vector3d(0, 0, 0),
            floors: [new ElevatorFloor(10, new Vector3d(0, 0, 0)), new ElevatorFloor(20, new Vector3d(0, 0, 10))],
            maxSpeedUnitsPerSecond: 30,
            speedInertiaUnitsPerSecondPerTick: 30);

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
            speedInertiaUnitsPerSecondPerTick: 30);
        ElevatorState movingState = new(
            startPosition: new Vector3d(0, 0, 0),
            floors: [
                new ElevatorFloor(20, new Vector3d(0, 0, 0)),
                new ElevatorFloor(30, new Vector3d(0, 0, 100)),
            ],
            maxSpeedUnitsPerSecond: 300,
            speedInertiaUnitsPerSecondPerTick: 300);
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
    public void ExportedFloorPositionLeavesTheDepartureIntegerOnTheFirstMovementTick()
    {
        ElevatorState elevator = new(
            startPosition: new Vector3d(0, 0, 0),
            floors: [new ElevatorFloor(10, new Vector3d(0, 0, 0)), new ElevatorFloor(20, new Vector3d(0, 0, 100))],
            maxSpeedUnitsPerSecond: 30,
            speedInertiaUnitsPerSecondPerTick: 30);

        elevator.RequestFloor(1);
        Assert.Equal(0.0, elevator.FloorPosition);

        elevator.Tick(canStart: false, floorUp: false, floorDown: false);
        Assert.Equal(0.0, elevator.FloorPosition);
        Assert.True(elevator.HasMovementRequest);

        elevator.Tick(canStart: true, floorUp: false, floorDown: false);
        Assert.InRange(elevator.FloorPosition, double.Epsilon, 1.0 - double.Epsilon);
        Assert.Equal(0.0, elevator.LastFloorPosition);
        Assert.Equal(1, elevator.WantedFloorIndex);
        Assert.Equal(0, elevator.CurrentFloorIndex);
        Assert.False(elevator.IsStart);
        Assert.False(elevator.IsStop);
    }

    [Fact]
    public void TravelVoiceLookupKeepsElevatorIdentityWhenSoundStateChanges()
    {
        ElevatorState state = new(
            startPosition: Vector3d.Zero,
            floors: [new ElevatorFloor(10, Vector3d.Zero)],
            maxSpeedUnitsPerSecond: 30,
            speedInertiaUnitsPerSecondPerTick: 30);
        PlacedElevator elevator = new(
            new OpenIGI.Engine.Tasks.LevelTask(),
            state,
            Matrix3f.Identity,
            "lift",
            "",
            "",
            "");
        Dictionary<PlacedElevator, string> voices = new(ReferenceEqualityComparer.Instance)
        {
            [elevator] = "voice",
        };

        // PlacedElevator is a mutable record. The travel sound flips this flag immediately before
        // removing the voice, so the audio dictionary must not use the record's value hash.
        elevator.IsSounding = true;
        elevator.IsSounding = false;

        Assert.True(voices.Remove(elevator));
    }

    [Fact]
    public void IsStartFiresOnlyWhenDepartingTheParkedWantedFloorThisTick()
    {
        ElevatorState elevator = new(
            startPosition: new Vector3d(0, 0, 0),
            floors: [new ElevatorFloor(10, new Vector3d(0, 0, 0)), new ElevatorFloor(20, new Vector3d(0, 0, 100))],
            maxSpeedUnitsPerSecond: 30,
            speedInertiaUnitsPerSecondPerTick: 30);

        elevator.Tick(canStart: true, floorUp: false, floorDown: false, wantedFloor: 1);
        Assert.True(elevator.IsStart);
        Assert.False(elevator.IsStop);
        Assert.Equal(0, elevator.InactiveTicks);

        elevator.Tick(canStart: true, floorUp: false, floorDown: false);
        Assert.False(elevator.IsStart);

        for (int i = 0; i < 400 && elevator.IsMoving; i++)
        {
            elevator.Tick(canStart: true, floorUp: false, floorDown: false);
        }

        Assert.False(elevator.IsMoving);
        Assert.True(elevator.IsStop);
        Assert.Equal(1, elevator.InactiveTicks);

        elevator.Tick(canStart: true, floorUp: false, floorDown: false);
        Assert.False(elevator.IsStop);
        Assert.Equal(2, elevator.InactiveTicks);
    }

    [Fact]
    public void AQueuedRequestDoesNotSetIsStartUntilTheDestChangesWhileParked()
    {
        ElevatorState elevator = new(
            startPosition: new Vector3d(0, 0, 0),
            floors: [new ElevatorFloor(10, new Vector3d(0, 0, 0)), new ElevatorFloor(20, new Vector3d(0, 0, 100))],
            maxSpeedUnitsPerSecond: 30,
            speedInertiaUnitsPerSecondPerTick: 30);

        elevator.RequestFloor(1);
        elevator.Tick(canStart: false, floorUp: false, floorDown: false);
        Assert.False(elevator.IsStart);
        Assert.True(elevator.HasMovementRequest);

        elevator.Tick(canStart: true, floorUp: false, floorDown: false);
        Assert.False(elevator.IsStart);
        Assert.True(elevator.IsMoving);
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
            speedInertiaUnitsPerSecondPerTick: 300);
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

    [Fact]
    public void A_trip_halts_where_it_is_while_can_start_is_false()
    {
        // The update (0x43D280) skips the move (0x43D6E0) whenever the lift's own "Can start" expression is false,
        // mid-trip as much as at a floor; the car stands still and resumes when the expression comes true again.
        ElevatorState elevator = new(
            startPosition: new Vector3d(0, 0, 0),
            floors:
            [
                new ElevatorFloor(10, new Vector3d(0, 0, 0)),
                new ElevatorFloor(20, new Vector3d(0, 0, 100000)),
            ],
            maxSpeedUnitsPerSecond: 3000,
            speedInertiaUnitsPerSecondPerTick: 3000);

        elevator.Tick(canStart: true, floorUp: true, floorDown: false);
        elevator.Tick(canStart: true, floorUp: false, floorDown: false);
        Vector3d underway = elevator.Position;
        Assert.True(underway.Z > 0);

        elevator.Tick(canStart: false, floorUp: false, floorDown: false, enabled: false);
        Assert.Equal(underway, elevator.Position);
        Assert.True(elevator.IsMoving);

        elevator.Tick(canStart: true, floorUp: false, floorDown: false);
        Assert.True(elevator.Position.Z > underway.Z);
    }

    [Fact]
    public void A_motion_projected_on_is_where_the_lift_itself_gets_to()
    {
        // A co-op client takes the server's lift as the snapshot had it, some ticks old, and projects it to its own tick:
        // the projection must land where the server's own lift does over those ticks, accelerating and braking alike.
        ElevatorState server = TallLift();
        server.RequestFloor(2);
        for (int i = 0; i < 5; i++)
        {
            server.Tick(canStart: true, floorUp: false, floorDown: false);
        }

        ElevatorMotion sent = server.Motion;
        ElevatorState client = TallLift();
        foreach (int ahead in new[] { 0, 1, 12, 200, 1000 })
        {
            ElevatorState reference = TallLift();
            reference.Restore(sent);
            for (int i = 0; i < ahead; i++)
            {
                reference.Tick(canStart: true, floorUp: false, floorDown: false);
            }

            Assert.Equal(reference.Motion, client.Project(sent, ahead));
        }

        // Far enough on, the car has arrived and stands.
        Assert.Equal(new ElevatorMotion(new Vector3d(0, 0, 80000), 2, null, 0.0), client.Project(sent, 1000));
    }

    [Fact]
    public void A_restored_lift_carries_on_from_the_motion_it_took()
    {
        // A partner called it on the server; this client's copy never heard the press, and takes the server's mid-trip.
        ElevatorState server = TallLift();
        ElevatorState client = TallLift();
        server.RequestFloor(1);
        for (int i = 0; i < 10; i++)
        {
            server.Tick(canStart: true, floorUp: false, floorDown: false);
        }

        Vector3d was = client.Position;
        client.Restore(server.Motion);
        Assert.Equal(server.Position, client.Position);
        Assert.Equal(was, client.PreviousPosition);
        Assert.True(client.TransformChangedThisTick);

        for (int i = 0; i < 1000; i++)
        {
            server.Tick(canStart: true, floorUp: false, floorDown: false);
            client.Tick(canStart: true, floorUp: false, floorDown: false);
            Assert.Equal(server.Position, client.Position);
        }

        Assert.Equal(1, client.CurrentFloorIndex);
        Assert.False(client.IsMoving);
    }

    private static ElevatorState TallLift() => new(
        startPosition: new Vector3d(0, 0, 0),
        floors:
        [
            new ElevatorFloor(10, new Vector3d(0, 0, 0)),
            new ElevatorFloor(20, new Vector3d(0, 0, 40000)),
            new ElevatorFloor(30, new Vector3d(0, 0, 80000)),
        ],
        maxSpeedUnitsPerSecond: 6000,
        speedInertiaUnitsPerSecondPerTick: 500);
}
