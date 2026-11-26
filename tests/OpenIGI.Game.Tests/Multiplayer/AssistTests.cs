using OpenIGI.Game.Multiplayer;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>Assists: an enemy that hurt a player badly in the life another player's kill ended helped kill it.</summary>
public sealed class AssistTests
{
    private const int Victim = 0;
    private const int Killer = 1;
    private const int Helper = 2;

    private static Match Teams()
    {
        Match match = new(new MultiplayerRules());
        match.Join(Victim, "Priboi", TeamRoster.Bad);
        match.Join(Killer, "Jones", TeamRoster.Good);
        match.Join(Helper, "Anya", TeamRoster.Good);
        return match;
    }

    [Fact]
    public void An_enemy_that_dealt_enough_damage_gets_an_assist_and_the_killer_does_not()
    {
        Match match = Teams();
        match.Damaged(Victim, Helper, 0.5f);
        match.Damaged(Victim, Killer, 0.5f);
        match.Kill(Victim, Killer, 1.0);

        Assert.Equal(1, match.Players[Helper].Assists);
        Assert.Equal(0, match.Players[Killer].Assists);
        Assert.Equal(1, match.Players[Killer].Kills);
    }

    [Fact]
    public void Damage_under_the_share_is_no_assist()
    {
        Match match = Teams();
        match.Damaged(Victim, Helper, Match.AssistShare - 0.05f);
        match.Kill(Victim, Killer, 1.0);

        Assert.Equal(0, match.Players[Helper].Assists);
    }

    [Fact]
    public void Several_hits_add_up_within_a_life()
    {
        Match match = Teams();
        match.Damaged(Victim, Helper, 0.2f);
        match.Damaged(Victim, Helper, 0.25f);
        match.Kill(Victim, Killer, 1.0);

        Assert.Equal(1, match.Players[Helper].Assists);
    }

    [Fact]
    public void Damage_from_an_earlier_life_does_not_count()
    {
        Match match = Teams();
        match.Damaged(Victim, Helper, 0.6f);
        match.Kill(Victim, -1, 1.0);
        Assert.Equal(1, match.Players[Helper].Assists);

        Assert.True(match.TrySpawn(Victim, 100.0));
        match.Kill(Victim, Killer, 101.0);
        Assert.Equal(1, match.Players[Helper].Assists);
    }

    [Fact]
    public void A_team_mates_damage_is_no_assist()
    {
        Match match = Teams();
        match.Join(3, "Ekk", TeamRoster.Bad);
        match.Damaged(Victim, 3, 0.9f);
        match.Kill(Victim, Killer, 1.0);

        Assert.Equal(0, match.Players[3].Assists);
    }

    [Fact]
    public void A_new_round_forgets_the_wounds_of_the_last()
    {
        Match match = Teams();
        match.Damaged(Victim, Helper, 0.9f);
        match.RoundOver(TeamRoster.Good);
        match.Kill(Victim, Killer, 1.0);

        Assert.Equal(0, match.Players[Helper].Assists);
    }

    [Fact]
    public void A_new_map_clears_assists()
    {
        Match match = Teams();
        match.Damaged(Victim, Helper, 0.9f);
        match.Kill(Victim, Killer, 1.0);
        match.NewMap();

        Assert.Equal(0, match.Players[Helper].Assists);
    }
}
