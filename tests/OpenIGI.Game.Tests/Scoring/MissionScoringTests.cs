using OpenIGI.Game.Scoring;
using Xunit;

namespace OpenIGI.Game.Tests.Scoring;

/// <summary>
/// A mission's rank for either game, as I.G.I. 2's missionscoresettings.c and missionscorescreen.c reckon and tell it: the rank, the
/// card, the best a profile keeps, and which of two bests is kept.
/// </summary>
public sealed class MissionScoringTests
{
    // Bodyguard, Secret Agent and IGI Operative: time (secs), times spotted, loads, health, accuracy.
    private static MissionRankRequirements Requirements(float timeWeighed = 900f) => MissionRankRequirements.FromRows(
        [timeWeighed, 5f, 3f, 20f, 10f],
        [600f, 2f, 1f, 50f, 30f],
        [300f, 0f, 0f, 80f, 60f]);

    /// <summary>
    /// The highest rank whose every requirement is met is earned: within its time, spotted no more than allowed, healthy enough and
    /// accurate enough; loads are not weighed, and a mission that took no time earns none.
    /// </summary>
    [Fact]
    public void The_rank_is_the_highest_whose_requirements_are_met()
    {
        MissionRankRequirements requirements = Requirements();
        Assert.Equal(2, requirements.GetRank(new MissionScore(30 * 250, 0, 90, 100, 70)));
        Assert.Equal(1, requirements.GetRank(new MissionScore(30 * 250, 1, 90, 100, 70)));
        Assert.Equal(0, requirements.GetRank(new MissionScore(30 * 800, 4, 25, 0, 0) { GameLoads = 9 }));
        Assert.Equal(-1, requirements.GetRank(new MissionScore(30 * 950, 0, 100, 0, 0)));
        Assert.Equal(-1, requirements.GetRank(new MissionScore(0, 0, 100, 0, 0)));
    }

    /// <summary>
    /// The achieved figures stand beside the next rank's, each marked met or short of it; the rank earned is named for the
    /// difficulty played; accuracy is hits per hundred rounds.
    /// </summary>
    [Fact]
    public void The_card_weighs_the_play_against_the_next_rank()
    {
        // Within Secret Agent's time and health, spotted once (Operative allows none), 75% accurate.
        var score = new MissionScore(30 * 250, 1, 90, 100, 75) { Difficulty = 1 };
        MissionScoreCard card = MissionScoreCardBuilder.Build(Requirements(), score, MissionScoreWords.InEnglish);

        Assert.Equal("MISSION STATS", card.Title);
        Assert.Equal(1, card.Rank);
        Assert.Equal("Covert Operative", card.RankName);
        Assert.Equal("Target", card.TargetHeading);
        Assert.Equal(
            [
                new MissionScoreLine("Time To Complete Mission:", "0:04:10", true, "0:05:00"),
                new MissionScoreLine("Times Spotted:", "1", false, "0"),
                new MissionScoreLine("Health:", "90%", true, "80%"),
                new MissionScoreLine("Accuracy:", "75%", true, "60%"),
            ],
            card.Lines);
    }

    /// <summary>
    /// The top rank is named for the difficulty it was played at: an unseen, quick run on Normal is a Secret Agent's, not an
    /// Agent's, which is Easy's.
    /// </summary>
    [Theory]
    [InlineData(0, "Agent")]
    [InlineData(1, "Secret Agent")]
    [InlineData(2, "David Jones")]
    public void The_rank_is_named_for_the_difficulty_played(int difficulty, string name)
    {
        var score = new MissionScore(30 * 100, 0, 100, 0, 0) { Difficulty = difficulty };
        Assert.Equal(name, MissionScoreCardBuilder.Build(Requirements(), score, MissionScoreWords.InEnglish).RankName);
    }

    /// <summary>Once the top rank is earned there is no target column; a figure whose first requirement is below nought is not shown.</summary>
    [Fact]
    public void The_top_rank_has_no_target_and_unweighed_figures_are_left_out()
    {
        var score = new MissionScore(30 * 100, 0, 100, 0, 0) { Difficulty = 2 };
        MissionScoreCard card = MissionScoreCardBuilder.Build(Requirements(timeWeighed: -1f), score, MissionScoreWords.InEnglish);

        Assert.Equal(2, card.Rank);
        Assert.Null(card.TargetHeading);
        Assert.Equal(["Times Spotted:", "Health:", "Accuracy:"], card.Lines.Select(static l => l.Label));
        Assert.All(card.Lines, static l => Assert.True(l.Met));
        Assert.All(card.Lines, static l => Assert.Null(l.Target));
        Assert.Equal("N/A", card.Lines[^1].Achieved);
    }

    /// <summary>A mission that earns nothing is Unranked, every figure short of Bodyguard's where it falls short.</summary>
    [Fact]
    public void An_unranked_mission_is_weighed_against_the_first_rank()
    {
        MissionScoreCard card = MissionScoreCardBuilder.Build(Requirements(), new MissionScore(30 * 1000, 9, 10, 10, 0), MissionScoreWords.InEnglish);

        Assert.Equal(-1, card.Rank);
        Assert.Equal("Unranked", card.RankName);
        Assert.Equal([false, false, false, false], card.Lines.Select(static l => l.Met));
        Assert.Equal(["0:15:00", "5", "20%", "10%"], card.Lines.Select(static l => l.Target));
    }

    /// <summary>The language's words are used where it holds them; English where it does not.</summary>
    [Fact]
    public void Words_come_from_the_language_and_fall_back_to_english()
    {
        var words = new MissionScoreWords(key => key == "Rank6" ? "Agente segreto" : null);
        Assert.Equal("Agente segreto", words.RankName(2, 1));
        Assert.Equal("Desk Officer", words.RankName(0, 0));
        Assert.Equal("Unranked", words.RankName(-1, 2));
        Assert.Equal("Times Spotted", words[MissionScoreWords.Spotted]);
    }

    /// <summary>
    /// A score replaces the kept one only at no lower difficulty, with time taken, and at no lower rank; it is kept in the order
    /// GOPlayerMissionStats writes it, with the requirements, and reads back as it was.
    /// </summary>
    [Fact]
    public void The_best_is_kept_as_retail_keeps_it()
    {
        MissionRankRequirements requirements = Requirements();
        var agent = new MissionScore(30 * 250, 1, 90, 100, 75) { Difficulty = 1, MissionId = 11, Kills = 9, GameLoads = 2 };
        var bodyguard = new MissionScore(30 * 800, 4, 25, 0, 0) { Difficulty = 1 };

        Assert.True(requirements.Improves(agent, null));
        Assert.True(requirements.Improves(agent, bodyguard));
        Assert.False(requirements.Improves(bodyguard, agent));
        Assert.False(requirements.Improves(agent with { Difficulty = 0 }, bodyguard));
        Assert.False(requirements.Improves(new MissionScore(0, 0, 100, 0, 0) { Difficulty = 2 }, bodyguard));

        TitleMissionRecord record = MissionScoreRecord.Encode(agent, requirements);
        Assert.Equal([11, 1, 2, 90, 75, 9, 100, 1, 7500, 0], record.Counts);
        Assert.Equal(600f, record.Figures[5]);
        Assert.True(MissionScoreRecord.TryDecode(record, out MissionScore back, out MissionRankRequirements? kept));
        Assert.Equal(agent, back);
        Assert.Equal(300f, kept[2, MissionRankItem.Time]);
        Assert.False(MissionScoreRecord.TryDecode(new TitleMissionRecord([1, 2], []), out _, out _));
    }

    /// <summary>
    /// The score of a completed mission becomes the best only where it improves on it; a mission first played is kept with nothing
    /// played, and its card names it Unranked beside what the first rank asks.
    /// </summary>
    [Fact]
    public void Scoring_keeps_the_better_and_a_fresh_record_is_unranked()
    {
        var sheet = new MissionScoreSheet(new MissionScore(30 * 250, 0, 90, 100, 75) { Difficulty = 1, MissionId = 3 }, Requirements());
        TitleMissionRecord fresh = MissionScoring.FreshRecord(sheet);

        MissionScoreResult first = MissionScoring.Score(sheet, fresh, MissionScoreWords.InEnglish);
        Assert.NotNull(first.Record);
        Assert.Equal("Secret Agent", first.Card.RankName);

        var worse = sheet with { Score = new MissionScore(30 * 800, 4, 25, 0, 0) { Difficulty = 1, MissionId = 3 } };
        Assert.Null(MissionScoring.Score(worse, first.Record, MissionScoreWords.InEnglish).Record);

        MissionScoreCard? card = MissionScoring.CardFor(fresh, MissionScoreWords.InEnglish);
        Assert.NotNull(card);
        Assert.Equal("Unranked", card.RankName);
        Assert.Equal("0:15:00", card.Lines[0].Target);
    }

    /// <summary>Of two bests, the account's is kept unless this computer's improves on it; one that cannot be read gives way.</summary>
    [Fact]
    public void The_better_of_two_bests_is_preferred()
    {
        MissionRankRequirements requirements = Requirements();
        TitleMissionRecord operative = MissionScoreRecord.Encode(new MissionScore(30 * 250, 0, 90, 100, 70) { Difficulty = 1 }, requirements);
        TitleMissionRecord bodyguard = MissionScoreRecord.Encode(new MissionScore(30 * 800, 4, 25, 0, 0) { Difficulty = 1 }, requirements);
        var broken = new TitleMissionRecord([1], [1f]);

        Assert.Same(operative, MissionScoring.Prefer(operative, bodyguard));
        Assert.Same(operative, MissionScoring.Prefer(bodyguard, operative));
        Assert.Same(operative, MissionScoring.Prefer(broken, operative));
        Assert.Same(bodyguard, MissionScoring.Prefer(bodyguard, broken));
    }
}
