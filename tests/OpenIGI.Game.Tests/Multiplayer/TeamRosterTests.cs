using OpenIGI.Game.Multiplayer;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>Teams kept level, as <c>TeamManager_GetFairTeam</c> keeps them.</summary>
public sealed class TeamRosterTests
{
    [Fact]
    public void ANewcomerJoinsTheSmallerTeam()
    {
        TeamRoster roster = new();
        roster.Assign(0, TeamRoster.Good);
        roster.Assign(1, TeamRoster.Good);
        roster.Assign(2, TeamRoster.Bad);

        Assert.Equal(TeamRoster.Bad, roster.FairTeam());
        Assert.Equal(TeamRoster.Bad, roster.FairTeam(TeamRoster.Good));
    }

    [Fact]
    public void LevelTeamsGiveTheTeamAskedFor()
    {
        TeamRoster roster = new();
        roster.Assign(0, TeamRoster.Good);
        roster.Assign(1, TeamRoster.Bad);
        Assert.Equal(TeamRoster.Good, roster.FairTeam(TeamRoster.Good));
    }

    [Fact]
    public void FillingFromEmptyAlternatesTheTeams()
    {
        TeamRoster roster = new(1234);
        for (int slot = 0; slot < 8; slot++)
        {
            roster.Assign(slot, roster.FairTeam());
        }

        Assert.Equal(4, roster.CountOf(TeamRoster.Good));
        Assert.Equal(4, roster.CountOf(TeamRoster.Bad));
    }

    [Fact]
    public void ALevelRandomChoiceIsEitherTeamAndRepeatsForTheSameSeed()
    {
        int[] first = [.. Enumerable.Range(0, 32).Select(_ => 0)];
        TeamRoster a = new(99);
        TeamRoster b = new(99);
        int[] fromA = [.. first.Select(_ => a.FairTeam())];
        int[] fromB = [.. first.Select(_ => b.FairTeam())];

        Assert.Equal(fromA, fromB);
        Assert.Contains(TeamRoster.Good, fromA);
        Assert.Contains(TeamRoster.Bad, fromA);
    }
}
