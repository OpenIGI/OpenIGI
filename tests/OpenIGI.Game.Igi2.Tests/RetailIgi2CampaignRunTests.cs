using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Every campaign mission loads and runs its level, its scripts, flow and tasks, without failing.</summary>
public sealed class RetailIgi2CampaignRunTests
{
    /// <summary>The ticks each mission is run for: twenty seconds of the game's thirty a second.</summary>
    private const int Ticks = 600;

    /// <summary>Each of the nineteen missions runs twenty seconds of its level and its status messages.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Every_mission_runs_its_level()
    {
        List<string> failures = [];
        foreach ((int location, int level) in RetailIgi2CampaignCensusTests.Missions)
        {
            int tick = 0;
            try
            {
                RetailIgi2Mission1 mission = RetailIgi2Mission1.Load(location: $"location{location}", levelName: $"level{level}");
                for (; tick < Ticks; tick++)
                {
                    mission.Step();
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failures.Add($"{(location * 10) + level} at tick {tick}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} missions failed:\n  {string.Join("\n  ", failures)}");
    }
}
