using OpenIGI.Game.Igi2.Multiplayer;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Multiplayer;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>A shipped multiplayer map started, though it places no player of its own.</summary>
public sealed class RetailIgi2MultiplayerLevelTests
{
    [RequiresTitleDataFact("igi2")]
    public void Sandstorm_starts_with_its_player_at_a_spawn_point_of_the_first_team()
    {
        Igi2TitlePlayer level = RetailIgi2NetworkHumanTests.StandLevel("multiplayer", "sandstorm", 5)
            ?? throw new Xunit.Sdk.XunitException("the multiplayer map's level did not start");
        Igi2SpawnAreaTask[] areas = [.. level.LevelTasks().OfType<Igi2SpawnAreaTask>()];
        Assert.NotEmpty(areas);
        Assert.Contains(areas, a => a.Team == TeamRoster.Good
            && a.Points().Any(p => Math.Abs(p.Position.X - level.Human.Position.X) + Math.Abs(p.Position.Y - level.Human.Position.Y) < 4096));
    }
}