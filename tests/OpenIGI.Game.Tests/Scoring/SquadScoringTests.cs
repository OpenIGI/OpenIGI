using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Scoring;
using OpenIGI.Net;
using Xunit;

namespace OpenIGI.Game.Tests.Scoring;

/// <summary>
/// A co-op squad's score: each player ranked by their own figures against the squad's time, told to every player in one message,
/// and each player's card with the whole squad's table.
/// </summary>
public sealed class SquadScoringTests
{
    // Bodyguard, Secret Agent and IGI Operative: time (secs), times spotted, loads, health, accuracy.
    private static readonly MissionRankRequirements Requirements = MissionRankRequirements.FromRows(
        [900f, 5f, 3f, 20f, 10f], [600f, 2f, 1f, 50f, 30f], [300f, 0f, 0f, 80f, 60f]);

    // The host's play: the squad's clock (250 s), on Normal, mission 3.
    private static readonly MissionScoreSheet Host = new(new MissionScore(30 * 250, 0, 90, 100, 70) { Difficulty = 1, MissionId = 3 }, Requirements);

    private static SquadScore Squad() => SquadScore.Of(Host,
    [
        new SquadScoreMember(0, "Jones", Host.Score),
        // Joined late: their own clock says 40 s, but the squad's time is theirs.
        new SquadScoreMember(2, "Anya", new MissionScore(30 * 40, 1, 60, 50, 20) { Difficulty = 0 }),
        new SquadScoreMember(5, "Phillips", new MissionScore(30 * 40, 6, 10, 0, 0)),
    ]);

    /// <summary>Every player takes the squad's time, difficulty and mission; their own figures stay theirs.</summary>
    [Fact]
    public void Each_player_is_weighed_against_the_squads_time()
    {
        SquadScore squad = Squad();
        Assert.All(squad.Members, static m => Assert.Equal((30u * 250u, 1, 3), (m.Score.Ticks, m.Score.Difficulty, m.Score.MissionId)));
        Assert.Equal(1, squad.Members[1].Score.TimesSpotted);
        Assert.Equal(SquadScore.AutoAdvanceSeconds, squad.SecondsLeft);
    }

    /// <summary>
    /// The squad is scored as one, on what holds for all of it: every sighting of anyone, the lowest health, the rounds and hits
    /// of all pooled, and the kills together.
    /// </summary>
    [Fact]
    public void The_squad_is_scored_on_what_holds_for_all_of_it()
    {
        MissionScore squad = Squad().Squad;
        Assert.Equal(30u * 250u, squad.Ticks);
        Assert.Equal(7, squad.TimesSpotted);
        Assert.Equal(10, squad.Health);
        Assert.Equal((150u, 90u), (squad.Shots, squad.Hits));
        Assert.Equal(60, squad.Accuracy);
        Assert.Equal((1, 3), (squad.Difficulty, squad.MissionId));

        // A world that counts sightings for the squad as a whole says so, and they are not added up again.
        Assert.Equal(2, SquadScore.Of(Host, Squad().Members, squadSightings: 2).Squad.TimesSpotted);
    }

    /// <summary>The score goes over the wire and comes back as it was sent.</summary>
    [Fact]
    public void The_message_round_trips()
    {
        SquadScore squad = Squad();
        var writer = new BitWriter();
        SquadScoreMessage.Write(writer, squad);
        var reader = new BitReader(writer.AsSpan());
        SquadScore? read = SquadScoreMessage.Read(ref reader);

        Assert.NotNull(read);
        Assert.Equal(squad.SecondsLeft, read.SecondsLeft);
        Assert.Equal(squad.Squad, read.Squad);
        Assert.Equal(squad.Members, read.Members);
        Assert.Equal(300f, read.Requirements[2, MissionRankItem.Time]);
    }

    /// <summary>
    /// Every player gets the squad's one card and rank, with each player's part in the table and their own marked, and keeps the
    /// squad's score as their record; somebody not in the squad (a dedicated server) gets none.
    /// </summary>
    [Fact]
    public void Every_player_gets_the_squads_one_card()
    {
        SquadScore squad = Squad();
        MissionScoreResult? jones = MissionScoring.ForSquadMember(squad, 0, null, MissionScoreWords.InEnglish);
        MissionScoreResult? anya = MissionScoring.ForSquadMember(squad, 2, null, MissionScoreWords.InEnglish);

        Assert.NotNull(jones);
        Assert.NotNull(anya);
        Assert.Equal("Unranked", anya.Card.RankName);
        Assert.Equal(jones.Card.Lines, anya.Card.Lines);
        Assert.Equal(jones.Record!.Counts, anya.Record!.Counts);
        Assert.Equal(["Jones", "Anya", "Phillips"], anya.Card.SquadLines.Select(static l => l.Name));
        Assert.Equal([false, true, false], anya.Card.SquadLines.Select(static l => l.IsOwn));
        Assert.Equal("N/A", anya.Card.SquadLines[2].Accuracy);
        Assert.Null(MissionScoring.ForSquadMember(squad, null, null, MissionScoreWords.InEnglish));
    }
}
