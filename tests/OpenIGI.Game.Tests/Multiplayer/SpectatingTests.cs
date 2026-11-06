using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Multiplayer.Modes;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>Whom a dead player watches: the living of its side, in turn.</summary>
public sealed class SpectatingTests
{
    private static readonly Scoreboard Board = new(1, 0, 0,
    [
        new ScoreboardLine(0, "me", 0, 0, 0, 1, false, Entity: 10),
        new ScoreboardLine(1, "mate", 0, 0, 0, 0, true, Entity: 11),
        new ScoreboardLine(2, "foe", 1, 0, 0, 0, true, Entity: 12),
        new ScoreboardLine(3, "fallen mate", 0, 0, 0, 1, false, Entity: 13),
        new ScoreboardLine(4, "mate two", 0, 0, 0, 0, true, Entity: 14),
    ]);

    private static Vector3d? At(int entity) => new Vector3d(entity, 0, 0);

    [Fact]
    public void In_teams_only_the_living_of_ones_own_side_are_watched_each_in_turn()
    {
        Assert.Equal("mate", Spectating.Pick(Board, 10, teams: true, At, 0)?.Name);
        Assert.Equal("mate two", Spectating.Pick(Board, 10, teams: true, At, 1)?.Name);
        Assert.Equal("mate", Spectating.Pick(Board, 10, teams: true, At, 2)?.Name);
        Assert.Equal(11d, Spectating.Pick(Board, 10, teams: true, At, 0)?.Position.X);
    }

    [Fact]
    public void Without_teams_anyone_standing_is_watched_and_with_nobody_one_watches_ones_own_body()
    {
        Assert.Equal(["mate", "foe", "mate two"], Enumerable.Range(0, 3).Select(p => Spectating.Pick(Board, 10, teams: false, At, p)?.Name));
        Assert.Null(Spectating.Pick(Board, 10, teams: true, _ => null, 0));
        Assert.Null(Spectating.Pick(null, 10, teams: true, At, 0));
    }
}
