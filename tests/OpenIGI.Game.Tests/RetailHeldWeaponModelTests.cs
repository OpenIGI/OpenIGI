using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class RetailHeldWeaponModelTests
{
    [RequiresGameDataFact]
    public void TheGlockAndMp5AreMultipartObjectsWithDrawableChildren()
    {
        (LoadedMission mission, MagicObjectRegistry magic) = Load();
        Assert.True(magic.TryGet("1005_01_1", out MagicObjectDefinition effect));
        Assert.Equal("TASKTYPE_GUNFLAME", magic.TaskTypeName(effect.TaskType));
        Dictionary<string, string[]> expected = new(StringComparer.OrdinalIgnoreCase)
        {
            ["117_01_1"] = ["117_01_1", "1005_01_1", "117_02_1", "117_03_1"],
            ["103_01_1"] = ["103_01_1", "103_02_1", "103_03_1"],
        };

        foreach ((string model, string[] names) in expected)
        {
            Assert.True(WorldObject.TryBuild(model, mission.TryGetModel, magic, out WorldObject? root));
            WorldObject[] parts = [.. root!.Walk()];
            Assert.Equal(names, parts.Select(static part => part.Name));

            foreach (WorldObject part in parts)
            {
                MtpInstance[] instances = [.. new[] { mission.LevelPack, mission.CommonPack }
                    .SelectMany(static pack => pack.Instances)];
                MtpInstance instance = Assert.Single(
                    instances,
                    candidate => string.Equals(candidate.Name, part.Name, StringComparison.OrdinalIgnoreCase));
                Assert.Equal(part.Models.Highest.DrawRecords.Count, instance.SlotCount);
            }
        }
    }

    private static (LoadedMission Mission, MagicObjectRegistry Magic) Load()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        MissionLoader loader = new(registry, fileSystem, resources, levelLoader);
        return (loader.Load(1), magic);
    }
}
