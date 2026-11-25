using OpenIGI.Game.Multiplayer;
using OpenIGI.Host;
using OpenIGI.Presentation.Rendering;
using Xunit;

namespace OpenIGI.Desktop.Tests.Completion;

/// <summary>
/// What a race's ghost's cloak is told of its runner, read off two of its track's frames: a shot when its count moves, a hurt
/// when its health falls, a fall when it stops being alive. The same for either title, whose runners are carried alike.
/// </summary>
public sealed class RaceGhostEventsTests
{
    private static readonly ReplicatedHuman Standing = new(
        Id: 1, default, default, Yaw: 0, Pitch: 0, State: 0, Action: 0, Weapon: 0, Health: 255, Team: 0, ReplicatedHumanCondition.Alive, Shots: 4);

    [Fact]
    public void Nothing_happens_to_a_runner_standing_still()
    {
        Assert.Equal(GhostEvents.None, GameHost.GhostEventsBetween(null, Standing));
        Assert.Equal(GhostEvents.None, GameHost.GhostEventsBetween(Standing, Standing));
    }

    [Fact]
    public void A_shot_is_its_count_moving_round_the_byte()
    {
        Assert.Equal(GhostEvents.Fired, GameHost.GhostEventsBetween(Standing, Standing with { Shots = 5 }));
        Assert.Equal(GhostEvents.Fired, GameHost.GhostEventsBetween(Standing with { Shots = 255 }, Standing with { Shots = 0 }));
    }

    [Fact]
    public void A_hurt_is_its_health_falling_and_never_its_rising()
    {
        Assert.Equal(GhostEvents.Hurt, GameHost.GhostEventsBetween(Standing, Standing with { Health = 200 }));
        Assert.Equal(GhostEvents.None, GameHost.GhostEventsBetween(Standing with { Health = 200 }, Standing));
    }

    [Fact]
    public void A_fall_is_told_once_with_the_hurt_that_felled_it()
    {
        ReplicatedHuman fallen = Standing with { Health = 0, Flags = ReplicatedHumanCondition.None };

        Assert.Equal(GhostEvents.Hurt | GhostEvents.Died, GameHost.GhostEventsBetween(Standing, fallen));
        Assert.Equal(GhostEvents.None, GameHost.GhostEventsBetween(fallen, fallen));
        Assert.Equal(GhostEvents.Died, GameHost.GhostEventsBetween(null, fallen));
    }
}
