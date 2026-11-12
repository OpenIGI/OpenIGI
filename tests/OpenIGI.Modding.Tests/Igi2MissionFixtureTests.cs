using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Systems;
using OpenIGI.Game.Player;
using OpenIGI.Modding.Testing;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Modding.Tests;

/// <summary>An I.G.I. 2 mission loaded headless brings its level up as the game does, a mod's systems among the game's.</summary>
public sealed class Igi2MissionFixtureTests
{
    [RequiresTitleDataFact("igi2")]
    public void Mission_one_stands_its_player_with_its_level_and_soldiers_brought_up()
    {
        Igi2Mission mission = MissionFixture.Igi2(11);

        Igi2TitlePlayer player = Assert.IsType<Igi2TitlePlayer>(mission.Stand());

        Assert.NotNull(player.Level);
        Assert.NotNull(player.Ai);
        Assert.NotEmpty(player.Soldiers!.Bodies);
    }

    [RequiresTitleDataFact("igi2")]
    public void A_mods_level_system_runs_among_the_games_as_the_mission_is_stood()
    {
        MarkingMod mod = new();
        Igi2Mission mission = MissionFixture.Igi2(11, ModHarness.Of(mod, titles: ["igi2"]));

        mission.Stand();

        Assert.True(mod.Ran);
    }

    // A mod that notes its system ran, just before the level's AI.
    private sealed class MarkingMod : IOpenIgiMod
    {
        public bool Ran { get; private set; }

        public void Configure(IModBuilder builder) =>
            builder.LevelSystems<Igi2LevelBuild>().InsertBefore(Igi2LevelSystems.Ai, "Test.Mark", _ => Ran = true);
    }
}
