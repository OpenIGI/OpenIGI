using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>
/// Each face group says how the original draws it, and the subsets built from a model carry that on: a lightmapped fence's sorted
/// groups (the word at offset 24, <c>0x49F1C0</c>) blend, and the FireBall's group adds (<c>0x49EC70</c>).
/// </summary>
public sealed class FaceGroupBlendTests
{
    [RequiresGameDataFact]
    public void A_lightmapped_fences_sorted_groups_blend_and_the_fireball_adds()
    {
        LoadedMission mission = LoadRetailMission();

        Assert.True(mission.TryGetModel("300_02_1", out MefModel? fence));
        Assert.Equal(MefMeshFormat.Lightmapped, fence!.Format);
        Assert.Equal(20, fence.DrawRecords.Count(static r => r.IsTransparent));
        Assert.DoesNotContain(fence.DrawRecords, static r => r.IsAdditive);
        MeshGeometry built = MefMeshBuilder.Build(fence, 1f)!;
        Assert.Equal(20, built.Subsets.Count(static s => s.Blend == SubsetBlend.Translucent));

        Assert.True(mission.TryGetModel("1006_01_1", out MefModel? fireball));
        Assert.Equal(SubsetBlend.Additive, MefMeshBuilder.Build(fireball!, 1f)!.Subsets.Single().Blend);
    }

    private static LoadedMission LoadRetailMission()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LevelLoader level = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        return new MissionLoader(missions, fileSystem, resources, level).Load(1);
    }
}
