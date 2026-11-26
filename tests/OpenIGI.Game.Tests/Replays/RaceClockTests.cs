using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Replays;
using Xunit;

namespace OpenIGI.Game.Tests.Replays;

/// <summary>
/// A race tells how much sooner or later than the ghost the player got where they stand -- behind, ahead, nothing off its route,
/// found again on coming back to it -- and pairs the objectives both did, and the finish, as splits.
/// </summary>
public sealed class RaceClockTests
{
    // The ghost walks a straight line half a metre a tick, so no two of its ticks are as near as each other to a point.
    private const int Step = 2048;

    [Fact]
    public void A_player_a_second_behind_reads_a_second_behind()
    {
        RaceClock clock = new(Line(1800));
        for (int tick = 0; tick < 600; tick++)
        {
            clock.Ran(tick, At(Math.Max(0, tick - 30) * Step));
        }

        Assert.Equal(1.0, clock.DeltaSeconds);
    }

    [Fact]
    public void A_player_half_a_second_ahead_reads_ahead()
    {
        RaceClock clock = new(Line(1800));
        for (int tick = 0; tick < 600; tick++)
        {
            clock.Ran(tick, At((tick + 15) * Step));
        }

        Assert.Equal(-0.5, clock.DeltaSeconds);
    }

    [Fact]
    public void Standing_where_the_ghost_stood_reads_level_and_waiting_after_it_left_falls_behind()
    {
        // The ghost stands at the start for two seconds, then walks.
        GhostTrack ghost = new();
        for (int tick = 0; tick < 600; tick++)
        {
            ghost.Set(tick, At(Math.Max(0, tick - 60) * Step));
        }

        RaceClock clock = new(ghost);
        for (int tick = 0; tick < 60; tick++)
        {
            clock.Ran(tick, At(0));
        }

        Assert.Equal(0.0, clock.DeltaSeconds);

        // Still standing a second after it set off: as far behind as it has been since the ghost was last there, at tick 60.
        for (int tick = 60; tick < 90; tick++)
        {
            clock.Ran(tick, At(0));
        }

        Assert.Equal((89 - 60) / 30.0, clock.DeltaSeconds);
    }

    [Fact]
    public void Off_the_route_there_is_no_gap_and_back_on_it_there_is_again()
    {
        RaceClock clock = new(Line(1800));
        for (int tick = 0; tick < 300; tick++)
        {
            clock.Ran(tick, At(tick * Step));
        }

        // Off to the side, far past fifteen metres, while the ghost goes on.
        for (int tick = 300; tick < 600; tick++)
        {
            clock.Ran(tick, new ReplicatedHuman(0, new Int3(300 * Step, 200_000, 0), default, 0, 0, 0, 0, 0, 255, 0, ReplicatedHumanCondition.Alive));
        }

        Assert.Null(clock.DeltaSeconds);

        // Back on it much further along, where the ghost was at tick 1200: found again in a second at most.
        for (int tick = 600; tick < 660; tick++)
        {
            clock.Ran(tick, At(1200 * Step));
        }

        Assert.Equal((659 - 1200) / 30.0, clock.DeltaSeconds);
    }

    [Fact]
    public void The_objectives_both_did_and_the_finish_are_the_splits()
    {
        GhostTrack ghost = Line(1800);
        ghost.Event(400, GhostEventKind.Objective, 0);
        ghost.Event(900, GhostEventKind.Objective, 1);
        ghost.Event(1799, GhostEventKind.End, 1);

        GhostTrack player = Line(1700);
        player.Event(350, GhostEventKind.Objective, 0);
        player.Event(500, GhostEventKind.Objective, 3);
        player.Event(1000, GhostEventKind.Objective, 1);
        player.Event(1699, GhostEventKind.End, 1);

        Assert.Equal([(0, 350, 400), (1, 1000, 900), (-1, 1699, 1799)], RaceClock.Splits(player, ghost));
    }

    [Fact]
    public void A_failed_run_has_no_finish_split()
    {
        GhostTrack ghost = Line(100);
        ghost.Event(99, GhostEventKind.End, 1);
        GhostTrack player = Line(100);
        player.Event(80, GhostEventKind.End, 0);

        Assert.Empty(RaceClock.Splits(player, ghost));
    }

    private static GhostTrack Line(int ticks)
    {
        GhostTrack track = new();
        for (int tick = 0; tick < ticks; tick++)
        {
            track.Set(tick, At(tick * Step));
        }

        return track;
    }

    private static ReplicatedHuman At(int x) =>
        new(0, new Int3(x, 0, 0), default, 0, 0, 0, 0, 0, 255, 0, ReplicatedHumanCondition.Alive);
}
