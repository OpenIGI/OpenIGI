using OpenIGI.Game.Igi2.Computer;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The map computer's level data and the mission's rank, as computerhilight.c and missionscoresettings.c read them.</summary>
public sealed class Igi2ComputerTests
{
    /// <summary>A highlight's task list reads numbers and ranges; a range adds the ones between.</summary>
    [Fact]
    public void A_task_list_reads_numbers_and_ranges()
    {
        Assert.Equal([12, 15, 16, 17, 18, 40], Igi2ComputerHilightTask.ExtractTaskIds("12 15-18, 40"));
        Assert.Equal([3], Igi2ComputerHilightTask.ExtractTaskIds("-3"));
    }

    /// <summary>
    /// The reading steps on by the digits of the value read: a lone nought is read again until the list holds 64, and a leading
    /// nought makes the rest be read twice.
    /// </summary>
    [Fact]
    public void A_task_list_rereads_noughts_as_retail_does()
    {
        IReadOnlyList<int> noughts = Igi2ComputerHilightTask.ExtractTaskIds("0");
        Assert.Equal(Igi2ComputerHilightTask.MaxTasks, noughts.Count);
        Assert.All(noughts, static id => Assert.Equal(0, id));
        Assert.Equal([7, 7], Igi2ComputerHilightTask.ExtractTaskIds("07"));
    }

    /// <summary>
    /// The highest rank whose every requirement is met is earned: within its time, spotted no more than allowed, healthy enough and
    /// accurate enough; loads are not weighed, and a mission that took no time earns none.
    /// </summary>
    [Fact]
    public void The_rank_is_the_highest_whose_requirements_are_met()
    {
        Igi2MissionScoreSettingsTask settings = new();
        float[][] rows = [[900f, 5f, 3f, 20f, 10f], [600f, 2f, 1f, 50f, 30f], [300f, 0f, 0f, 80f, 60f]];
        for (int rank = 0; rank < 3; rank++)
        {
            for (int item = 0; item < 5; item++)
            {
                settings.Required[rank, item] = rows[rank][item];
            }
        }

        Assert.Equal(2, settings.GetRank(new Igi2MissionScore(30 * 250, 0, 90, 100, 70)));
        Assert.Equal(1, settings.GetRank(new Igi2MissionScore(30 * 250, 1, 90, 100, 70)));
        Assert.Equal(0, settings.GetRank(new Igi2MissionScore(30 * 800, 4, 25, 0, 0)));
        Assert.Equal(-1, settings.GetRank(new Igi2MissionScore(30 * 950, 0, 100, 0, 0)));
        Assert.Equal(-1, settings.GetRank(new Igi2MissionScore(0, 0, 100, 0, 0)));
        Assert.Equal("Rank6", Igi2MissionScoreSettingsTask.RankName(2, 1));
        Assert.Equal("Unranked", Igi2MissionScoreSettingsTask.RankName(-1, 2));
    }

    /// <summary>Mission 1's objectives load into the class, each with its text and its expressions.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission_1s_objectives_load()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2DefineComputerObjectiveTask objectives = Assert.Single(m.Tree.OfType<Igi2DefineComputerObjectiveTask>());
        Assert.NotEqual(string.Empty, objectives.Objectives[0].TextResource);
        Assert.NotEqual(string.Empty, objectives.Objectives[0].CompleteExpression);
        Assert.Single(m.Tree.OfType<Igi2DefineComputerLimitTask>());
        Assert.NotEmpty(m.Tree.OfType<Igi2ComputerHilightTask>());
        Assert.Single(m.Tree.OfType<Igi2MissionScoreSettingsTask>());
    }
}
