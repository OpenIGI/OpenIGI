using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Drives the mission load sequence against a retail installation: the startup scan that builds the
/// mission table, then loading mission 1 end to end.
/// </summary>
/// <remarks>
/// The mission table is the part worth testing against real data, because it exists nowhere in the
/// executable — every descriptor is published at runtime by a <c>mission.qvm</c> calling
/// <c>DefineMission</c>, and the scan that runs them is a startup step rather than part of loading a
/// mission.
/// </remarks>
public sealed class RetailMissionLoadTests
{
    [RequiresGameDataFact]
    public void TheScanRegistersEveryRetailMission()
    {
        Fixture fixture = Fixture.Create();

        Assert.Equal(14, fixture.Registry.Count);

        for (int id = 1; id <= 14; id++)
        {
            Assert.True(fixture.Registry.TryGet(id, out MissionDescriptor? mission), $"mission {id} is missing");
            Assert.Equal($"missions/location0/level{id}", mission.MissionDirectory);
            Assert.Equal("missions/location0/common", mission.CommonPackDirectory);
            Assert.Equal($"missions/location0/level{id}", mission.LevelPackDirectory);
            Assert.Equal("location0", mission.CommonPackName);
            Assert.Equal($"level{id}", mission.LevelPackName);
        }
    }

    [RequiresGameDataFact]
    public void MissionOneMatchesItsScriptExactly()
    {
        MissionDescriptor mission = Fixture.Create().Registry.Require(1);

        Assert.Equal("Mission 1 Name", mission.Name);
        Assert.Equal("Mission 1 Desc", mission.Description);
        Assert.Equal(string.Empty, mission.Tag);
        Assert.Equal(2, mission.NextMissionId);
        Assert.Equal("mission1.spr", mission.BriefingSprite);
        Assert.Equal("LOCAL:missions/location0/level1/", mission.MissionAliasTarget);
        Assert.Equal("LOCAL:missions/location0/level1/terrain/terrain", mission.TerrainBasePath);
    }

    [RequiresGameDataFact]
    public void TheLastMissionEndsTheChain()
    {
        MissionRegistry registry = Fixture.Create().Registry;

        // Each mission points at the next; whichever one ends the chain says so with the sentinel the
        // original registers as MISSION_NEXT_MISSION_UNDEFINED.
        int ends = registry.Missions.Count(m => m.NextMissionId == MissionDescriptor.NextMissionUndefined);
        Assert.Equal(1, ends);

        foreach (MissionDescriptor mission in registry.Missions)
        {
            if (mission.NextMissionId != MissionDescriptor.NextMissionUndefined)
            {
                Assert.True(registry.TryGet(mission.NextMissionId, out _), $"mission {mission.Id} points at a missing successor");
            }
        }
    }

    [RequiresGameDataFact]
    public void TheCampaignOrderFollowsTheChainRatherThanTheScan()
    {
        MissionRegistry registry = Fixture.Create().Registry;

        // The scan order is a directory walk, so it is alphabetical: missions/location0/level10 sorts
        // between level1 and level2. Every menu that shows the campaign has to follow the successor links
        // instead, which is what the mission list in the shell was not doing.
        List<MissionDescriptor> chained = [.. registry.InCampaignOrder()];
        Assert.Equal(registry.Missions.Count, chained.Count);
        Assert.Equal(MissionRegistry.FirstMissionId, chained[0].Id);

        for (int i = 0; i + 1 < chained.Count; i++)
        {
            if (chained[i].NextMissionId != MissionDescriptor.NextMissionUndefined)
            {
                Assert.Equal(chained[i].NextMissionId, chained[i + 1].Id);
            }
        }
    }

    [RequiresGameDataFact]
    public void LoadingMissionOneBindsBothPacksAndBuildsTheTaskTree()
    {
        Fixture fixture = Fixture.Create();
        LoadedMission mission = fixture.Loader.Load(1);

        // The MISSION: alias has to be live before anything resolves a path through it.
        Assert.True(fixture.FileSystem.Exists("MISSION:objects.qvm"));

        Assert.Equal("location0", mission.CommonPack.Name);
        Assert.Equal("level1", mission.LevelPack.Name);
        Assert.Empty(mission.CommonPack.MissingModels);
        Assert.Empty(mission.LevelPack.MissingModels);
        Assert.DoesNotContain("AI navigation graphs", mission.PendingSteps);
        Assert.Equal(415, mission.LevelPack.Instances.Count);

        // objects.qvm built a level subtree.
        Assert.NotNull(fixture.LevelLoader.LoadedScriptPath);
        Assert.NotNull(fixture.LevelRoot.FirstChild);
    }

    [RequiresGameDataFact]
    public void MissionsOneThroughFiveFindEveryShippedNavGraph()
    {
        // Retail ships MISSIONS/location0/LEVELN/GRAPHS/GRAPH*.DAT while the descriptors spell the path
        // in lowercase; the MISSION: lookup has to find them whatever the host filesystem's casing rules.
        foreach ((int id, int expected) in new[] { (1, 11), (2, 14), (3, 18), (4, 13), (5, 13) })
        {
            Fixture fixture = Fixture.Create();
            LoadedMission mission = fixture.Loader.Load(id);

            IReadOnlyList<string> graphs = fixture.FileSystem.EnumerateFiles("MISSION:graphs", "graph*.dat");

            Assert.Equal(expected, graphs.Count);
            Assert.DoesNotContain("AI navigation graphs", mission.PendingSteps);
            Assert.All(graphs, graph => GraphFile.Read(fixture.FileSystem.ReadAllBytes(graph)));
        }
    }

    [RequiresGameDataFact]
    public void EveryInstanceResolvesToAModelAndItsTextures()
    {
        LoadedMission mission = Fixture.Create().Loader.Load(1);

        int instances = 0;
        int subsets = 0;
        int textured = 0;

        foreach (MtpInstance instance in mission.LevelPack.Instances)
        {
            Assert.True(mission.LevelPack.TryGetModel(instance, out Formats.World.MefModel? model), $"{instance.Name} has no model");
            instances++;

            MeshGeometry? geometry = MefMeshBuilder.Build(model!);
            if (geometry is null)
            {
                continue;
            }

            // Flattening must preserve the triangle count and keep every index in range.
            Assert.Equal(model!.TriangleCount, geometry.TriangleCount);
            foreach (ushort index in geometry.Indices)
            {
                Assert.InRange(index, 0, geometry.Vertices.Length - 1);
            }

            foreach (MeshSubset subset in geometry.Subsets)
            {
                subsets++;
                if (subset.HasMaterial && mission.LevelPack.TryGetTextureName(instance, subset.MaterialSlot, out string texture))
                {
                    Assert.NotEmpty(texture);
                    textured++;
                }
            }
        }

        Assert.Equal(415, instances);
        Assert.True(subsets > 1000, $"expected the level's draw records, saw {subsets}");
        Assert.True(textured > subsets / 2, $"only {textured} of {subsets} subsets resolved a texture");
    }

    [RequiresGameDataFact]
    public void AnUnknownMissionIdFailsTheWayTheOriginalDoes()
    {
        Fixture fixture = Fixture.Create();
        EngineFatalException error = Assert.Throws<EngineFatalException>(() => fixture.Loader.Load(99));
        Assert.Contains("The mission ID 99 does not exist.", error.Message, StringComparison.Ordinal);
    }

    private sealed record Fixture(
        VirtualFileSystem FileSystem,
        ResourceRegistry Resources,
        MissionRegistry Registry,
        MissionLoader Loader,
        LevelLoader LevelLoader,
        GameLoop Loop,
        LevelTask LevelRoot)
    {
        public static Fixture Create()
        {
            VirtualFileSystem fileSystem = new();
            fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
            ResourceRegistry resources = new(fileSystem);

            GameLoop loop = new(new ManualTimeSource());
            LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

            MissionRegistry registry = new();
            registry.Register(loop.Natives);
            registry.Scan(fileSystem, resources, loop.Natives);

            LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
            MissionLoader loader = new(registry, fileSystem, resources, levelLoader);

            return new Fixture(fileSystem, resources, registry, loader, levelLoader, loop, levelRoot);
        }
    }
}
