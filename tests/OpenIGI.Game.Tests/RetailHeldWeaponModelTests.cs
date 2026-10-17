using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

public sealed class RetailHeldWeaponModelTests
{
    [RequiresGameDataFact]
    public void TheDualUziUsesTheGunX2ChildAndOppositeFirstPersonHand()
    {
        (LoadedMission mission, MagicObjectRegistry magic) = Load();

        Assert.True(magic.TryGet("111_05_1", out MagicObjectDefinition gunX2));
        Assert.Equal("TASKTYPE_GUNX2", magic.TaskTypeName(gunX2.TaskType));
        Assert.True(WorldObject.TryBuild("111_04_1", mission.TryGetModel, magic, out WorldObject? root));
        Assert.Equal(
            ["111_04_1", "1005_01_1", "111_05_1", "1005_01_1"],
            root!.Walk().Select(static node => node.Name));

        WeaponType uzix2 = RetailWeaponRoster.Value.Weapons.Single(w => w.ScriptId == "WEAPON_ID_UZIX2");
        BoneAnimationSet viewSet = BoneAnimationSet.Read(File.ReadAllBytes(
            Path.Combine(GameData.RequireRoot(), "common", "anims", "005.iff")));
        HumanView view = new(viewSet);
        Assert.NotNull(view.PlayWeaponAnimation(uzix2.StandAnimation, loops: true));

        BoneAttachment primary = view.HeldWeapon;
        BoneAttachment secondary = view.HeldWeaponSecondary;
        Assert.True(
            secondary.Position.X < primary.Position.X - 1000d,
            $"Retail UZIX2 attachments should occupy opposite hands; primary X={primary.Position.X}, secondary X={secondary.Position.X}.");
    }

    [RequiresGameDataFact]
    public void TheRetailProximityMineModelPutsItsContactPlaneAtTheObjectOrigin()
    {
        (LoadedMission mission, _) = Load();
        Assert.True(mission.TryGetModel("136_01_1", out MefModel? mine));

        float minimumY = float.PositiveInfinity;
        float maximumY = float.NegativeInfinity;
        foreach (MefVertex vertex in mine!.Vertices)
        {
            minimumY = Math.Min(minimumY, vertex.Position.Y);
            maximumY = Math.Max(maximumY, vertex.Position.Y);
        }

        // The raw 136_01_1 bounds are approximately X ±900, Y 0..400, Z ±295. The zero side is
        // the authored wall/floor contact plane, so adding the TASKTYPE_GRENADE half extent floats it.
        Assert.InRange(minimumY, 0f, 0.1f);
        Assert.InRange(maximumY, 399f, 401f);
    }

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
            ["111_04_1"] = ["111_04_1", "1005_01_1", "111_05_1", "1005_01_1"],
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
