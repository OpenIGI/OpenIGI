using System.Diagnostics.CodeAnalysis;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Config;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using Xunit;

namespace OpenIGI.Game.Tests.World;

/// <summary>
/// Pins what a build cut to the opening of the campaign can reach: the first missions along the chain,
/// and nothing past them by any route that looks a mission up.
/// </summary>
public sealed class CampaignLimitedMissionCatalogueTests
{
    [Fact]
    public void OffersTheFirstMissionsAlongTheChainNotByIdOrScanOrder()
    {
        // Scanned alphabetically, as the directory walk finds them, with the campaign running 1, 3, 2, 4.
        ChainCatalogue inner = new((1, 3), (10, 11), (2, 4), (3, 2), (4, 10), (11, ChainCatalogue.End));

        CampaignLimitedMissionCatalogue limited = new(inner, 3);

        Assert.Equal([1, 3, 2], limited.InProgressionOrder().Select(m => m.Id));
        Assert.Equal([1, 3, 2], limited.InCampaignOrder().Select(m => m.Id));
        Assert.Equal([1, 3, 2], limited.Missions.Select(m => m.Id));
        Assert.Equal(3, limited.Count);
    }

    [Fact]
    public void TryGetRefusesAMissionPastTheLimit()
    {
        ChainCatalogue inner = ChainCatalogue.Linear(8);
        CampaignLimitedMissionCatalogue limited = new(inner, 6);

        Assert.True(limited.TryGet(6, out MissionDescriptor? last));
        Assert.Equal(6, last.Id);

        // Mission six still names seven as its successor; the catalogue is what says seven is not there,
        // which is how the host knows that finishing six completes the campaign.
        Assert.Equal(7, last.NextMissionId);
        Assert.False(limited.TryGet(7, out MissionDescriptor? refused));
        Assert.Null(refused);
    }

    [Fact]
    public void AMissionTheCampaignNeverReachesIsNotOffered()
    {
        // 99 is registered but nothing chains to it: a mod's extra level.
        ChainCatalogue inner = new((1, 2), (2, ChainCatalogue.End), (99, ChainCatalogue.End));

        CampaignLimitedMissionCatalogue limited = new(inner, 6);

        Assert.Equal([1, 2], limited.InCampaignOrder().Select(m => m.Id));
        Assert.False(limited.TryGet(99, out _));
    }

    [Fact]
    public void ScanRefreshesWhatIsOffered()
    {
        ChainCatalogue inner = new();
        CampaignLimitedMissionCatalogue limited = new(inner, 2);
        Assert.Equal(0, limited.Count);

        inner.Load((1, 2), (2, 3), (3, ChainCatalogue.End));

        Assert.Equal(2, limited.Scan(null!, null!, null!));
        Assert.Equal([1, 2], limited.Missions.Select(m => m.Id));
    }

    [Fact]
    public void ClampToOfferedBringsAProfileFromTheFullGameBackInside()
    {
        ChainCatalogue inner = new((1, 2), (2, 3), (3, 4), (4, ChainCatalogue.End), (99, ChainCatalogue.End));
        CampaignLimitedMissionCatalogue limited = new(inner, 2);

        Assert.Equal(1, limited.ClampToOffered(1));
        Assert.Equal(2, limited.ClampToOffered(2));
        Assert.Equal(2, limited.ClampToOffered(4));

        // Not on the campaign at all, so not "further along" it either: left for the screens to judge.
        Assert.Equal(99, limited.ClampToOffered(99));
    }

    [Fact]
    public void ProgressionNeverUnlocksPastTheLimit()
    {
        ChainCatalogue inner = ChainCatalogue.Linear(8);
        CampaignLimitedMissionCatalogue limited = new(inner, 6);
        PlayerProfile profile = new() { ActiveMission = 6 };

        Assert.False(MissionProgression.UnlockNext(profile, limited, 7));
        Assert.Equal(6, profile.ActiveMission);
        Assert.Equal(MissionCampaignStatus.Locked, MissionProgression.Status(limited, 6, 7));
    }

    [Fact]
    public void RejectsALimitBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CampaignLimitedMissionCatalogue(new ChainCatalogue(), 0));
    }

    /// <summary>A catalogue whose missions and chain are given directly, in the order a scan found them.</summary>
    private sealed class ChainCatalogue : IMissionCatalogue<MissionDescriptor>
    {
        public const int End = MissionDescriptor.NextMissionUndefined;

        private readonly List<MissionDescriptor> _missions = [];

        public ChainCatalogue(params (int Id, int Next)[] missions) => Load(missions);

        public IReadOnlyList<MissionDescriptor> Missions => _missions;

        public int Count => _missions.Count;

        public static ChainCatalogue Linear(int count) =>
            new([.. Enumerable.Range(1, count).Select(id => (id, id == count ? End : id + 1))]);

        public void Load(params (int Id, int Next)[] missions)
        {
            _missions.Clear();
            foreach ((int id, int next) in missions)
            {
                _missions.Add(new MissionDescriptor(id, $"Mission {id}", string.Empty, string.Empty, next,
                    string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty));
            }
        }

        public void Register(NativeRegistry natives)
        {
        }

        public int Scan(VirtualFileSystem fileSystem, ResourceRegistry resources, NativeRegistry natives) => Count;

        public IEnumerable<MissionDescriptor> InCampaignOrder() => InProgressionOrder();

        public IEnumerable<MissionDescriptor> InProgressionOrder()
        {
            HashSet<int> seen = [];
            int id = MissionRegistry.FirstMissionId;
            while (id != End && seen.Add(id) && TryGet(id, out MissionDescriptor? mission))
            {
                yield return mission;
                id = mission.NextMissionId;
            }
        }

        public bool TryGet(int id, [NotNullWhen(true)] out MissionDescriptor? descriptor)
        {
            descriptor = _missions.Find(m => m.Id == id);
            return descriptor is not null;
        }
    }
}
