using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Locks down where a marker for an activatable belongs: on the thing, and staying there.
/// </summary>
public sealed class WorldInteractionMarkerTests
{
    /// <summary>A terminal whose task origin is on the floor and whose model sits a metre above it.</summary>
    private static WorldInteraction Terminal() => Build(
        InteractionActivateType.Terminal,
        anchor: new Vector3d(1000.0, 2000.0, 0.0),
        markerPoint: new Vector3d(1000.0, 2000.0, 4096.0));

    [Fact]
    public void TheMarkerIsOnTheThingRatherThanAtItsOrigin()
    {
        WorldInteraction terminal = Terminal();

        // The origin is where the author dropped the task; the marker is the middle of the model that
        // stands on it, which for a terminal is a metre off the floor.
        Assert.Equal(4096.0, terminal.MarkerPoint(new Vector3d(0.0, 0.0, 4096.0)).Z, 3);
    }

    [Fact]
    public void TheMarkerDoesNotMoveWhenThePlayerDoes()
    {
        WorldInteraction terminal = Terminal();

        Vector3d fromLeft = terminal.MarkerPoint(new Vector3d(-3000.0, -2000.0, 5000.0));
        Vector3d fromRight = terminal.MarkerPoint(new Vector3d(6000.0, 1000.0, 2000.0));
        Vector3d crouched = terminal.MarkerPoint(new Vector3d(1000.0, 500.0, 1200.0));

        // The whole complaint about the first cut: it tracked the eye, so the badge slid across the
        // object — and off it — as the player took a step. An object does not move when you do.
        Assert.Equal(fromLeft, fromRight);
        Assert.Equal(fromLeft, crouched);
    }

    [Fact]
    public void ADoorsMarkerRidesTheLeafAsItSwings()
    {
        DoorState state = new(maxAngleDegrees: 90f, openTimeSeconds: 1f);
        PlacedDoor placed = new(
            new DeclaredTask(),
            new Vector3d(0.0, 0.0, 0.0),
            Matrix3f.Identity,
            StopX: 0f,
            StopY: 0f,
            StopZ: 0f,
            state);

        // The leaf's middle, half a metre along the door's own width axis and a metre up.
        WorldInteraction door = Build(
            InteractionActivateType.Door,
            anchor: default,
            markerPoint: new Vector3d(2048.0, 0.0, 4096.0),
            markerLocal: new Vector3d(2048.0, 0.0, 4096.0),
            door: placed);

        Vector3d closed = door.MarkerPoint(default);
        state.Toggle();
        for (int tick = 0; tick < 60; tick++)
        {
            state.Tick();
        }

        Vector3d open = door.MarkerPoint(default);

        Assert.True(state.AngleDegrees > 1f, "the door did not swing");
        Assert.NotEqual(closed.X, open.X, 1);

        // It swings about the hinge, so it keeps its distance from it and its height.
        Assert.Equal(Math.Sqrt((closed.X * closed.X) + (closed.Y * closed.Y)), Math.Sqrt((open.X * open.X) + (open.Y * open.Y)), 1);
        Assert.Equal(closed.Z, open.Z, 3);
    }

    [Fact]
    public void ADoorsMarkerIsPresentedBetweenTicksLikeTheDoorItself()
    {
        DoorState state = new(maxAngleDegrees: 90f, openTimeSeconds: 1f);
        PlacedDoor placed = new(
            new DeclaredTask(),
            new Vector3d(0.0, 0.0, 0.0),
            Matrix3f.Identity,
            StopX: 0f,
            StopY: 0f,
            StopZ: 0f,
            state);
        WorldInteraction door = Build(
            InteractionActivateType.Door,
            anchor: default,
            markerPoint: new Vector3d(2048.0, 0.0, 4096.0),
            markerLocal: new Vector3d(2048.0, 0.0, 4096.0),
            door: placed);

        state.Toggle();
        for (int tick = 0; tick < 10; tick++)
        {
            state.Tick();
        }

        Vector3d previous = door.MarkerPoint(default, tickBlend: 0f);
        Vector3d current = door.MarkerPoint(default, tickBlend: 1f);
        Vector3d halfway = door.MarkerPoint(default, tickBlend: 0.5f);

        // The door swings on the thirty-hertz tick and is drawn many times between two of them. A
        // marker that reads the tick state steps once per tick beside a door that glides -- which is
        // exactly what the judder was. Half a tick in, it stands between the two states.
        Assert.NotEqual(previous.Y, current.Y, 1);
        Assert.InRange(halfway.Y, Math.Min(previous.Y, current.Y), Math.Max(previous.Y, current.Y));
        Assert.NotEqual(previous.Y, halfway.Y, 3);
        Assert.NotEqual(current.Y, halfway.Y, 3);

        // And it stands on the swing's arc rather than across it: what is interpolated is the angle,
        // the same quantity the door's own mesh is drawn from.
        Assert.Equal(Radius(previous), Radius(halfway), 3);
        Assert.Equal(Radius(previous), Radius(current), 3);

        static double Radius(Vector3d point) => Math.Sqrt((point.X * point.X) + (point.Y * point.Y));
    }

    [Fact]
    public void ALongThingIsMarkedWhereItIsBeingUsed()
    {
        // A fence is metres long: the part offered to the player is the part to mark, which is the one
        // case where following them is right rather than wrong.
        WorldInteraction fence = Build(
            InteractionActivateType.Fence,
            anchor: default,
            markerPoint: default,
            localBoundsMinimum: new Vector3d(-8192.0, -100.0, 0.0),
            localBoundsMaximum: new Vector3d(8192.0, 100.0, 8192.0),
            hasLocalBounds: true);

        Vector3d left = fence.MarkerPoint(new Vector3d(-6000.0, 2000.0, 4096.0));
        Vector3d right = fence.MarkerPoint(new Vector3d(6000.0, 2000.0, 4096.0));

        Assert.True(left.X < right.X);
    }

    [Fact]
    public void AMissingModelLeavesTheMarkerOnTheActivationAnchor()
    {
        WorldInteraction switchTask = Build(
            InteractionActivateType.Switch,
            anchor: new Vector3d(500.0, 600.0, 4500.0),
            markerPoint: new Vector3d(500.0, 600.0, 4500.0));

        Assert.Equal(new Vector3d(500.0, 600.0, 4500.0), switchTask.MarkerPoint(new Vector3d(0.0, 0.0, 0.0)));
    }

    private static WorldInteraction Build(
        InteractionActivateType type,
        Vector3d anchor,
        Vector3d markerPoint,
        Vector3d markerLocal = default,
        PlacedDoor? door = null,
        Vector3d localBoundsMinimum = default,
        Vector3d localBoundsMaximum = default,
        bool hasLocalBounds = false) =>
        new(
            new DeclaredTask(),
            type,
            anchor,
            end: anchor,
            orientation: Matrix3f.Identity,
            facing: 0f,
            durationTicks: 0,
            actionSprite: string.Empty,
            door,
            isPickable: false,
            stationaryGun: null,
            localBoundsMinimum,
            localBoundsMaximum,
            hasLocalBounds,
            markerPoint,
            markerLocal);
}
