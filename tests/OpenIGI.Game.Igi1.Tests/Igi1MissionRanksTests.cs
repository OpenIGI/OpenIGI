using OpenIGI.Game.Igi1.Flow;
using OpenIGI.Game.Player;
using OpenIGI.Game.Scoring;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>Project I.G.I.'s mission ranks, the remaster's own: every mission ranked, and what the score is counted from.</summary>
public sealed class Igi1MissionRanksTests
{
    /// <summary>
    /// The shipped table ranks each of the fourteen missions, each rank asking more than the one below it; no other mission; and
    /// being spotted is not weighed in the two open fights.
    /// </summary>
    [Fact]
    public void Every_mission_is_ranked_and_each_rank_asks_more()
    {
        Igi1MissionRanks table = Igi1MissionRanks.Shipped;
        Assert.Equal(Enumerable.Range(1, 14), table.RankedMissions.Order());
        Assert.Null(table.For(15));
        Assert.False(table.For(4)!.Weighs(MissionRankItem.Spotted));
        Assert.False(table.For(10)!.Weighs(MissionRankItem.Spotted));
        Assert.Equal(600f * 2.5f, table.For(1)![0, MissionRankItem.Time]);
        foreach (int mission in table.RankedMissions)
        {
            MissionRankRequirements requirements = table.For(mission)!;
            Assert.True(requirements.Weighs(MissionRankItem.Time));
            for (int rank = 1; rank < MissionRankRequirements.RankCount; rank++)
            {
                Assert.True(requirements[rank, MissionRankItem.Time] < requirements[rank - 1, MissionRankItem.Time]);
                Assert.True(requirements[rank, MissionRankItem.Health] > requirements[rank - 1, MissionRankItem.Health]);
                Assert.True(requirements[rank, MissionRankItem.Accuracy] > requirements[rank - 1, MissionRankItem.Accuracy]);
                Assert.True(!requirements.Weighs(MissionRankItem.Spotted)
                    || requirements[rank, MissionRankItem.Spotted] < requirements[rank - 1, MissionRankItem.Spotted]);
            }
        }
    }

    /// <summary>A quiet, quick, accurate run on Normal earns the top rank, named as I.G.I. 2 names Normal's.</summary>
    [Fact]
    public void A_clean_run_earns_the_top_rank()
    {
        MissionRankRequirements requirements = Igi1MissionRanks.Shipped.For(1)!;
        var score = new MissionScore(30 * 300, 0, 100, 20, 12) { Difficulty = 1, MissionId = 1 };
        MissionScoreCard card = MissionScoreCardBuilder.Build(requirements, score, MissionScoreWords.InEnglish);
        Assert.Equal(2, card.Rank);
        Assert.Equal("Secret Agent", card.RankName);
    }

    /// <summary>
    /// A mission's own file takes its entry's place, keeping the table's rows it does not set; a mod's mission is ranked by its
    /// own file and not without one; a file this build cannot read is refused.
    /// </summary>
    [Fact]
    public void A_missions_own_file_takes_its_place_in_the_table()
    {
        Igi1MissionRanks table = Igi1MissionRanks.Shipped;
        MissionRankRequirements own = table.For(1, """
            // A harder trainyard.
            { "par": 300, "ranks": { "health": [50, 70, 95] } }
            """)!;
        Assert.Equal(300f, own[2, MissionRankItem.Time]);
        Assert.Equal(95f, own[2, MissionRankItem.Health]);
        Assert.Equal(40f, own[2, MissionRankItem.Accuracy]);

        Assert.Null(table.For(40, null));
        Assert.Equal(1200f, table.For(40, """{ "par": 1200 }""")![2, MissionRankItem.Time]);

        Assert.Throws<System.Text.Json.JsonException>(() => table.For(1, """{ "par": 300, "ranks": { "spotted": [1, 2] } }"""));
        Assert.Throws<System.Text.Json.JsonException>(() => table.For(1, """{ "format": 9, "par": 300 }"""));
        Assert.Throws<System.Text.Json.JsonException>(() => Igi1MissionRanks.Parse("""{ "format": 1, "missions": {} }"""));
    }

    /// <summary>The tally counts the rounds a gun fires, and a save puts back the rounds, hits and kills of a single player's mission.</summary>
    [Fact]
    public void The_tally_counts_rounds_and_is_put_back_by_a_save()
    {
        var tally = new ShotTally();
        tally.Fired(3);
        tally.Fired(-1);
        tally.Hit(head: false);
        Assert.Equal(3, tally.RoundsFired);

        var loaded = new ShotTally();
        loaded.Restore(40, 12, 5);
        Assert.Equal((40, 12, 5, 12, 5), (loaded.RoundsFired, loaded.SoldierHits, loaded.SoldierKills, loaded.Hits, loaded.Kills));
    }

    /// <summary>
    /// The squad is spotted when a soldier takes the player on while none is fighting it, as I.G.I. 2 counts a squad first
    /// spotting the player: not again for each soldier that joins, but again once all have left it.
    /// </summary>
    [Fact]
    public void A_sighting_is_counted_when_the_first_soldier_engages()
    {
        int spotted = 0;
        var target = new OpenIGI.Game.Igi1.Ai.AiCombatTarget("HumanPlayer") { Engaged = () => spotted++ };
        object first = new(), second = new();

        target.Add(first);
        target.Add(second);
        target.Add(first);
        Assert.Equal(1, spotted);

        target.Remove(first);
        target.Remove(second);
        target.Add(second);
        Assert.Equal(2, spotted);
    }
}
