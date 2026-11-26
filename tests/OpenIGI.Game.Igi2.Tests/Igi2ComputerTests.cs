using OpenIGI.Game.Igi2.Computer;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Scoring;
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

    /// <summary>The level's fifteen parameters are the three ranks' requirements, Bodyguard first, which the shared rules weigh.</summary>
    [Fact]
    public void The_score_settings_parameters_are_the_ranks_requirements()
    {
        Igi2MissionScoreSettingsTask settings = new()
        {
            TimeBodyguard = 900f, SpottedBodyguard = 5f, LoadsBodyguard = 3f, HealthBodyguard = 20f, AccuracyBodyguard = 10f,
            TimeSecretAgent = 600f, SpottedSecretAgent = 2f, LoadsSecretAgent = 1f, HealthSecretAgent = 50f, AccuracySecretAgent = 30f,
            TimeOperative = 300f, SpottedOperative = 0f, LoadsOperative = 0f, HealthOperative = 80f, AccuracyOperative = 60f,
        };

        MissionRankRequirements requirements = settings.Requirements;
        Assert.Equal(900f, requirements[0, MissionRankItem.Time]);
        Assert.Equal(1f, requirements[1, MissionRankItem.Loads]);
        Assert.Equal(60f, requirements[2, MissionRankItem.Accuracy]);
        Assert.Equal(2, requirements.GetRank(new MissionScore(30 * 250, 0, 90, 100, 70)));
        Assert.Equal(1, requirements.GetRank(new MissionScore(30 * 250, 1, 90, 100, 70)));
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

    /// <summary>
    /// The rocket base's objectives done in more than one place have each place as a target: the two fuel pipes (one through
    /// its counter), the gantry's three levels and the bunker's doors; one done in one place has none, and is marked at its link.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_rocket_bases_objectives_have_every_place_they_are_done_at()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(location: "location3", levelName: "LEVEL6");
        m.Step();
        Igi2DefineComputerObjectiveTask objectives = Assert.Single(m.Tree.OfType<Igi2DefineComputerObjectiveTask>());
        int[] Open(int row) => [.. objectives.Objectives[row].OpenTargets.Select(task => task.Id).Order()];

        Assert.Equal([2615, 2616], Open(0));
        Assert.Empty(Open(1));
        Assert.False(objectives.Objectives[1].HasTargets);
        Assert.Equal([1400, 1401, 1403], Open(2));
        Assert.Contains(4062, Open(3));
    }
}
