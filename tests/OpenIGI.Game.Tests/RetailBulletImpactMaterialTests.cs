using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Checks that retail Level 1–5 collision materials reach bullet decals intact.</summary>
public sealed class RetailBulletImpactMaterialTests
{
    [RequiresGameDataTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ACollisionTAMCMaterialSelectsItsOwnBulletMark(int missionId)
    {
        (LoadedMission mission, GameMaterialRegistry materials) = Load(missionId);
        Assert.True(materials.TryGetByName(BulletMarks.FallbackMaterial, out GameMaterial concrete));

        (int Global, GameMaterial Material)? selected = FindMarkedNonConcreteMaterial(mission, materials);
        Assert.True(selected.HasValue, $"mission {missionId} has no non-Concrete collision material");

        BulletMarks marks = new();
        WorldRayHit hit = new(default, new(0f, 0f, 1f), 0.0, selected.Value.Global);

        Assert.True(marks.TryAdd(hit, materials));
        BulletMark mark = Assert.Single(marks.Marks);
        Assert.Equal(selected.Value.Material.Entry.EntryTexture, mark.Texture);
        Assert.NotEqual(concrete.Entry.EntryTexture, mark.Texture);
    }

    private static (LoadedMission Mission, GameMaterialRegistry Materials) Load(int missionId)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        resources.Register(
            GameMaterialRegistry.ScriptResource,
            fileSystem.ReadAllBytes(GameMaterialRegistry.ScriptResource));

        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);

        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);

        GameMaterialRegistry materials = new();
        materials.Register(loop.Natives);
        materials.Load(resources, loop.Natives);

        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(registry, fileSystem, resources, levelLoader).Load(missionId);
        return (mission, materials);
    }

    private static (int Global, GameMaterial Material)? FindMarkedNonConcreteMaterial(
        LoadedMission mission,
        GameMaterialRegistry materials)
    {
        foreach (LoadedPack pack in new[] { mission.CommonPack, mission.LevelPack })
        {
            foreach (MtpInstance instance in pack.Instances)
            {
                if (!pack.TryGetModel(instance, out MefModel? model) || model is null)
                {
                    continue;
                }

                for (int meshIndex = 0; meshIndex < MefCollisionMesh.MeshLevels; meshIndex++)
                {
                    if (!MefCollisionMesh.TryRead(model.Container, meshIndex, out MefCollisionMesh? mesh)
                        || mesh is null)
                    {
                        continue;
                    }

                    foreach (MefCollisionFace face in mesh.Faces)
                    {
                        int global = mesh.GlobalMaterial(face.Material);
                        if (materials.TryGet(global, out GameMaterial material)
                            && !string.Equals(material.Name, BulletMarks.FallbackMaterial, StringComparison.Ordinal)
                            && material.RecordsImpacts)
                        {
                            return (global, material);
                        }
                    }
                }
            }
        }

        return null;
    }
}
