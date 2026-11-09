using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Host.Multiplayer;
using Xunit;

namespace OpenIGI.Desktop.Tests.Multiplayer;

public sealed class NextMapFlowTests
{
    private static readonly IReadOnlyList<(int Id, string Name)> Missions =
        [(1, "Trainyard"), (2, "Rescue the Pilot"), (3, "Destroy the Radar"), (4, "Missile Trainyard"), (5, "Military Airbase"), (6, "Border Crossing")];

    [Fact]
    public void A_listen_host_offers_this_map_again_the_next_and_two_others_under_other_modes()
    {
        IReadOnlyList<MapChoice> choices = NextMapFlow.Around(Missions, 3, GameModes.TeamDeathmatch, new Random(7));

        Assert.Equal(NextMapVote.MostChoices, choices.Count);
        Assert.Equal(new MapChoice(3, GameModes.TeamDeathmatch, "Destroy the Radar", Again: true), choices[0]);
        Assert.Equal(new MapChoice(4, GameModes.TeamDeathmatch, "Missile Trainyard"), choices[1]);
        Assert.Equal(choices.Count, choices.Select(c => c.Mission).Distinct().Count());
        Assert.All(choices.Skip(2), c => Assert.NotEqual(GameModes.TeamDeathmatch, c.Mode));
        Assert.All(choices.Skip(2), c => Assert.True(GameModes.IsVersus(c.Mode)));
        Assert.NotEqual(choices[2].Mode, choices[3].Mode);
    }

    [Fact]
    public void The_last_mission_offers_the_first_as_the_next()
    {
        IReadOnlyList<MapChoice> choices = NextMapFlow.Around(Missions, 6, GameModes.FreeForAll, new Random(1));

        Assert.Equal(1, choices[1].Mission);
    }

    [Fact]
    public void Co_op_offers_no_other_map()
    {
        IReadOnlyList<MapChoice> choices = NextMapFlow.Around(Missions, 2, GameModes.Coop, new Random(1));

        Assert.Single(choices);
        Assert.True(choices[0].Again);
    }
}
