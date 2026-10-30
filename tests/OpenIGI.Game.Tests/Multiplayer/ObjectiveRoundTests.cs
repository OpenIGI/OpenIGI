using OpenIGI.Game.Multiplayer;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>A round's objectives taken in order, as <c>NetMission</c> takes them.</summary>
public sealed class ObjectiveRoundTests
{
    private const int TickRate = 30;

    [Fact]
    public void AllObjectivesWonGivesTheAttackersTheMission()
    {
        (Match match, Objective first, Objective second, ObjectiveRound round) = Setup();
        round.Start(0);
        Assert.Equal(1, round.Current);
        Assert.Equal(TeamRoster.Good, round.AttackingTeam);
        Assert.Equal(TeamRoster.Bad, match.DefendingTeam);

        first.Completed = true;
        Assert.Equal(RoundEvent.ObjectiveResolved, round.Tick(1, resolver: 0));
        Assert.Equal(2, round.Current);
        Assert.Equal(1000 + 2250 + 1000, match.Players[0].Money);

        second.Completed = true;
        Assert.Equal(RoundEvent.MissionOver, round.Tick(2));
        Assert.Equal(TeamRoster.Good, round.LastWinner);
        Assert.Equal(1, match.ScoreOf(TeamRoster.Good));
        Assert.Equal(0, round.Current);
    }

    [Fact]
    public void AFailedObjectiveMovesOnButLosesTheMission()
    {
        (Match match, Objective first, Objective second, ObjectiveRound round) = Setup();
        round.Start(0);
        first.Failed = true;
        Assert.Equal(RoundEvent.ObjectiveResolved, round.Tick(1));
        second.Completed = true;
        Assert.Equal(RoundEvent.MissionOver, round.Tick(2));
        Assert.Equal(TeamRoster.Bad, round.LastWinner);
        Assert.Equal(1, match.ScoreOf(TeamRoster.Bad));
    }

    [Fact]
    public void TheClockGivesTheDefendersTheMissionAndTheRoundRestarts()
    {
        (Match match, _, _, ObjectiveRound round) = Setup();
        round.Start(0);
        long end = match.Rules.ObjectiveMaxMinutes * 60L * TickRate;
        Assert.Equal(RoundEvent.None, round.Tick(end - 1));
        Assert.Equal(RoundEvent.MissionOver, round.Tick(end));
        Assert.Equal(TeamRoster.Bad, round.LastWinner);

        RoundEvent last = RoundEvent.None;
        for (int i = 1; i <= ObjectiveRound.RestartTicks; i++)
        {
            last = round.Tick(end + i);
        }

        Assert.Equal(RoundEvent.Restart, last);
        Assert.Equal(1, round.Current);
        Assert.Equal(1, match.Round);
    }

    private static (Match, Objective, Objective, ObjectiveRound) Setup()
    {
        Match match = new(new MultiplayerRules());
        match.Join(0, "jones", TeamRoster.Good);
        match.Join(1, "priboi", TeamRoster.Bad);
        Objective first = new(1, TeamRoster.Good);
        Objective second = new(2, TeamRoster.Good);
        return (match, first, second, new ObjectiveRound([second, first], match, TickRate));
    }

    private sealed class Objective(int order, int team) : IRoundObjective
    {
        public int Order { get; } = order;

        public int Team { get; } = team;

        public bool Completed { get; set; }

        public bool Failed { get; set; }

        public bool IsCompleted() => Completed;

        public bool IsFailed() => Failed;
    }
}
