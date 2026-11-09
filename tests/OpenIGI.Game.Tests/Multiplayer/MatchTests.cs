using OpenIGI.Game.Multiplayer;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>The match's bookkeeping under the original's rules.</summary>
public sealed class MatchTests
{
    private static Match NewMatch(MultiplayerRules? rules = null)
    {
        Match match = new(rules ?? new MultiplayerRules());
        match.Join(0, "jones", TeamRoster.Good);
        match.Join(1, "anya", TeamRoster.Good);
        match.Join(2, "priboi", TeamRoster.Bad);
        return match;
    }

    [Fact]
    public void PlayersStartWithTheStartMoney() =>
        Assert.All(NewMatch().Players.Values, p => Assert.Equal(1000, p.Money));

    [Fact]
    public void KillingAnEnemyPaysAndATeamKillFines()
    {
        Match match = NewMatch();
        match.Kill(victim: 2, killer: 0, now: 5);
        Assert.Equal(1800, match.Players[0].Money);
        Assert.Equal(1, match.Players[0].Kills);
        Assert.Equal(1, match.Players[2].Deaths);
        Assert.False(match.Players[2].IsAlive);

        // The fine takes it to nothing, not into debt.
        match.Kill(victim: 1, killer: 0, now: 6);
        Assert.Equal(0, match.Players[0].Money);
        Assert.Equal(1, match.Players[0].Kills);
    }

    [Fact]
    public void TheMoneySettingsPayInPlaceOfTheRulesAMatchWasMadeWith()
    {
        try
        {
            MultiplayerRules made = new();
            Assert.Same(made, NetCVars.WithMoney(made));

            NetCVars.MoneyKill.Value = 300;
            NetCVars.MoneyTeamKill.Value = 0;
            NetCVars.MoneyCap.Value = 2000;
            NetCVars.MoneyStart.Value = 1500;
            Match match = NewMatch(NetCVars.WithMoney(made));
            Assert.Equal(1500, match.Players[0].Money);

            match.Kill(victim: 2, killer: 0, now: 1);
            Assert.Equal(1800, match.Players[0].Money);
            match.Kill(victim: 1, killer: 0, now: 2);
            Assert.Equal(1800, match.Players[0].Money);

            // Changed mid-match, and past the cap the pay is cut to it.
            NetCVars.MoneyKill.Value = 5000;
            match.Rules = NetCVars.WithMoney(match.Rules);
            match.Kill(victim: 2, killer: 0, now: 3);
            Assert.Equal(2000, match.Players[0].Money);
        }
        finally
        {
            NetCVars.MoneyKill.Reset();
            NetCVars.MoneyTeamKill.Reset();
            NetCVars.MoneyCap.Reset();
            NetCVars.MoneyStart.Reset();
        }
    }

    [Fact]
    public void MoneyStopsAtTheCap()
    {
        Match match = NewMatch(new MultiplayerRules { MoneyCap = 1500 });
        match.Kill(victim: 2, killer: 0, now: 0);
        Assert.Equal(1500, match.Players[0].Money);
    }

    [Fact]
    public void ARespawnCostsLessTheLongerTheWaitAndTheDefendersPayLess()
    {
        Match match = NewMatch();
        match.Kill(2, 0, now: 100);
        Assert.Equal(1000, match.SpawnCost(2, 100));
        Assert.Equal(500, match.SpawnCost(2, 110));
        Assert.Equal(0, match.SpawnCost(2, 120));

        match.DefendingTeam = TeamRoster.Bad;
        Assert.Equal(350, match.SpawnCost(2, 110));
    }

    [Fact]
    public void APlayerWhoCannotPayStaysDead()
    {
        Match match = NewMatch();
        match.Players[2].Money = 100;
        match.Kill(2, 0, now: 0);
        Assert.False(match.TrySpawn(2, 1));
        Assert.True(match.TrySpawn(2, 20));
        Assert.True(match.Players[2].IsAlive);
        Assert.Equal(100, match.Players[2].Money);
    }

    [Fact]
    public void AnObjectivePaysBothTeamsAndTheResolverAndScores()
    {
        Match match = NewMatch();
        match.ObjectiveWon(TeamRoster.Good, resolver: 1);
        Assert.Equal(1000 + 2250, match.Players[0].Money);
        Assert.Equal(1000 + 2250 + 1000, match.Players[1].Money);
        Assert.Equal(1000 + 1250, match.Players[2].Money);
        Assert.Equal(0, match.ScoreOf(TeamRoster.Good));
    }

    [Fact]
    public void TheMapEndsAtTheTeamScoreLimit()
    {
        Match match = NewMatch();
        for (int i = 0; i < 2; i++)
        {
            match.RoundOver(TeamRoster.Bad);
        }

        Assert.False(match.IsMapOver(1));
        match.RoundOver(TeamRoster.Bad);
        Assert.True(match.IsMapOver(1));

        match.NewMap();
        Assert.False(match.IsMapOver(1));
        Assert.Equal(1000, match.Players[2].Money);
    }
}
