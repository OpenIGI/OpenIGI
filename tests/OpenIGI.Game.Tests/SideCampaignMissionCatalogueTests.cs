using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>A mod's campaign keeps its missions out of the game's progression and lists them in its own order.</summary>
public sealed class SideCampaignMissionCatalogueTests
{
    [Fact]
    public void A_side_campaigns_missions_are_kept_out_of_the_games_progression_and_listed_in_their_own_order()
    {
        Catalogue inner = new([Mission(1, next: 2), Mission(2), Mission(901), Mission(902)]);
        TitleCampaign nightfall = new("Nightfall", [902, 901, 903]);

        SideCampaignMissionCatalogue catalogue = new(inner, [nightfall]);

        Assert.Equal([1, 2], catalogue.InProgressionOrder().Select(static m => m.Id));
        Assert.Equal([1, 2], catalogue.InCampaignOrder().Select(static m => m.Id));
        Assert.Equal([902, 901], catalogue.InSideCampaignOrder(nightfall).Select(static m => m.Id));
        Assert.True(catalogue.TryGet(901, out _));
        Assert.Equal(4, catalogue.Count);
    }

    private static MissionDescriptor Mission(int id, int next = MissionDescriptor.NextMissionUndefined) =>
        new(id, $"Mission {id}", string.Empty, string.Empty, next, $"missions/location0/level{id}", "common", "level", "common", "level", string.Empty);

    private sealed class Catalogue(IReadOnlyList<MissionDescriptor> missions) : IMissionCatalogue<MissionDescriptor>
    {
        public IReadOnlyList<MissionDescriptor> Missions => missions;

        public int Count => missions.Count;

        public void Register(NativeRegistry natives)
        {
        }

        public int Scan(VirtualFileSystem fileSystem, ResourceRegistry resources, NativeRegistry natives) => Count;

        public IEnumerable<MissionDescriptor> InCampaignOrder() => missions;

        public IEnumerable<MissionDescriptor> InProgressionOrder() => missions;

        public bool TryGet(int id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MissionDescriptor? descriptor)
        {
            descriptor = missions.FirstOrDefault(m => m.Id == id);
            return descriptor is not null;
        }
    }
}
