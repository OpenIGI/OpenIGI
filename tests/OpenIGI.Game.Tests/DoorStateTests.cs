using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The <c>Door</c> task's state machine — <c>0x446D10</c>'s two motion channels and the use state
/// <c>sub_447D70</c> reports, which is what the AI walker branches on.
/// </summary>
public sealed class DoorStateTests
{
    /// <summary>A fresh door is closed, and a use opens it over its <c>Open time</c>.</summary>
    /// <remarks>
    /// The swing steps <c>maxAngle / (openTime × 30)</c> degrees a tick, so a 90° door with a 1 s open
    /// time takes exactly 30 ticks and reports state 2 ("opening") throughout — the state the walker
    /// waits on.
    /// </remarks>
    [Fact]
    public void AUseOpensTheDoorOverItsOpenTime()
    {
        DoorState door = new(maxAngleDegrees: 90f, openTimeSeconds: 1f);

        Assert.True(door.IsFullyClosed);
        Assert.Equal(1, door.UseState);

        door.Toggle();

        for (int tick = 1; tick < 30; tick++)
        {
            door.Tick();
            Assert.Equal(2, door.UseState);
            Assert.False(door.IsFullyOpen);
        }

        door.Tick();

        Assert.True(door.IsFullyOpen);
        Assert.Equal(0, door.UseState);
        Assert.Equal(90f, door.AngleDegrees, precision: 3);
    }

    /// <summary>A second use closes it again, and it reports 3 ("closing") on the way.</summary>
    /// <remarks>
    /// The walker opens on <b>1 and 3 alike</b>, which is what makes a guard re-open a door that is
    /// swinging shut in front of it rather than walking into it.
    /// </remarks>
    [Fact]
    public void ASecondUseClosesItAndItReportsClosing()
    {
        DoorState door = new(maxAngleDegrees: 90f, openTimeSeconds: 1f);
        door.Toggle();
        for (int tick = 0; tick < 30; tick++)
        {
            door.Tick();
        }

        door.Toggle();
        door.Tick();

        Assert.Equal(3, door.UseState);
        Assert.False(door.IsFullyClosed);

        for (int tick = 0; tick < 30; tick++)
        {
            door.Tick();
        }

        Assert.True(door.IsFullyClosed);
        Assert.Equal(1, door.UseState);
        Assert.Equal(0f, door.AngleDegrees, precision: 3);
    }

    /// <summary>The open latch holds — nothing closes a door the level's script does not close.</summary>
    /// <remarks>
    /// The update only assigns the latch inside its <c>HasExpression</c> guards, and the shipped doors'
    /// open/close expressions are empty, so a door a guard opened stays open and its ticks-open counter
    /// runs on (what a close expression would read).
    /// </remarks>
    [Fact]
    public void AnOpenedDoorStaysOpenAndCountsTicks()
    {
        DoorState door = new(maxAngleDegrees: 90f, openTimeSeconds: 0.5f);
        door.CommandOpen();

        for (int tick = 0; tick < 100; tick++)
        {
            door.RunConditions(null, null);
            door.Tick();
        }

        // 0x446E38 counts as the update starts, before the door moves: the fifteenth update arrives, and the
        // eighty-five after it each count a tick it stood open.
        Assert.True(door.IsFullyOpen);
        Assert.Equal(85, door.TicksOpen);
    }

    /// <summary>A sliding door with no angle runs its slide channel and nothing else.</summary>
    [Fact]
    public void ASlidingDoorRunsItsSlideChannel()
    {
        DoorState door = new(maxAngleDegrees: 0f, openTimeSeconds: 1f, slides: true);
        door.CommandOpen();

        door.Tick();
        Assert.Equal(2, door.UseState);
        Assert.Equal(0f, door.AngleDegrees);

        // The step is (L / (time × 30)) / L under the original's chop rounding, so the fraction lands a hair short
        // of one after time × 30 steps and takes one more: mission 8's 3.5-second gates move for 106 ticks in the
        // original, not 105.
        for (int tick = 0; tick < 30; tick++)
        {
            door.Tick();
        }

        Assert.True(door.IsFullyOpen);
        Assert.Equal(1f, door.SlideFraction, precision: 3);
    }

    [Fact]
    public void MovingChannelsRetainTheirPreviousTickForPresentation()
    {
        DoorState door = new(maxAngleDegrees: 90f, openTimeSeconds: 1f, slides: true);
        door.CommandOpen();

        door.Tick();
        Assert.Equal(0f, door.PreviousAngleDegrees);
        Assert.Equal(0f, door.PreviousSlideFraction);

        door.Tick();
        Assert.Equal(3f, door.PreviousAngleDegrees, precision: 4);
        Assert.Equal(1f / 30f, door.PreviousSlideFraction, precision: 4);
        Assert.Equal(6f, door.AngleDegrees, precision: 4);
        Assert.Equal(2f / 30f, door.SlideFraction, precision: 4);
        Assert.True(door.TransformChangedThisTick);
    }

    [Fact]
    public void AStationaryDoorDoesNotRequestAnotherCollisionTransform()
    {
        DoorState door = new(maxAngleDegrees: 90f, openTimeSeconds: 1f);

        door.Tick();

        Assert.False(door.TransformChangedThisTick);
    }
}
