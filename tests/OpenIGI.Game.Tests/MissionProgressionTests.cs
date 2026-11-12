using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Config;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Checks the retail active-mission frontier against the authored descriptor chain.</summary>
public sealed class MissionProgressionTests
{
    [Fact]
    public void FreshRetailProfileStartsAtMissionOne() =>
        Assert.Equal(MissionRegistry.FirstMissionId, new PlayerProfile().ActiveMission);

    [RequiresGameDataFact]
    public void UnlockNextOnlyMovesTheFrontierForwardAlongTheRetailChain()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        NativeRegistry natives = new();
        MissionRegistry missions = new();
        missions.Register(natives);
        missions.Scan(fileSystem, resources, natives);

        MissionDescriptor first = missions.Require(MissionRegistry.FirstMissionId);
        PlayerProfile profile = new() { ActiveMission = first.Id };
        Assert.True(MissionProgression.UnlockNext(profile, missions, first.NextMissionId));
        Assert.Equal(first.NextMissionId, profile.ActiveMission);

        Assert.False(MissionProgression.UnlockNext(profile, missions, first.NextMissionId));
        Assert.Equal(first.NextMissionId, profile.ActiveMission);
        Assert.False(MissionProgression.UnlockNext(
            profile, missions, MissionDescriptor.NextMissionUndefined));
    }

    /// <summary>The progression the seam reports is the chain, unchanged.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="MissionProgression"/> and the mission-select menu both walked
    /// <c>MissionRegistry.Chain</c> directly; they walk <c>InProgressionOrder</c> on the catalogue
    /// interface now, so that a title whose data states no chain can still answer. This is the assertion
    /// that the answer did not move for the title whose data does.
    /// </para>
    /// <para>
    /// Compared element for element against the chain rather than merely counted: the chain's whole
    /// point is its order, and a reordering would keep every count identical.
    /// </para>
    /// </remarks>
    [RequiresGameDataFact]
    public void TheProgressionIsTheChainUnchanged()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        NativeRegistry natives = new();
        MissionRegistry missions = new();
        missions.Register(natives);
        missions.Scan(fileSystem, resources, natives);

        int[] chain = [.. missions.Chain(MissionRegistry.FirstMissionId).Select(m => m.Id)];
        int[] progression = [.. missions.InProgressionOrder().Select(m => m.Id)];

        Assert.NotEmpty(chain);
        Assert.Equal(chain, progression);

        // And the browser order still starts with the chain and adds whatever it does not reach.
        int[] browser = [.. missions.InCampaignOrder().Select(m => m.Id)];
        Assert.Equal(chain, browser.Take(chain.Length));
        Assert.Equal(missions.Count, browser.Length);
    }
}
