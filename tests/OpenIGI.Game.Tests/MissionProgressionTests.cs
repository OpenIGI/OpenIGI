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
}
