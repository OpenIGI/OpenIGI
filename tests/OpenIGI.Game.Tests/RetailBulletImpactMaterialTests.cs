using OpenIGI.Core.Numerics;
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
using OpenIGI.Game.Igi1.World;

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

        Assert.True(marks.TryAdd(hit, new object(), materials));
        BulletMark mark = Assert.Single(marks.Marks);
        Assert.Equal(selected.Value.Material.Entry.EntryTexture, mark.Texture);
        Assert.NotEqual(concrete.Entry.EntryTexture, mark.Texture);
    }

    [RequiresGameDataFact]
    public void AnObjectTraceReportsTheGlobalTAMCMaterialRow()
    {
        (_, GameMaterialRegistry materials) = Load(1);
        WorldScene scene = RetailLevelOneGround.Scene();
        bool foundMappedFace = false;

        foreach (WorldPlacement placement in scene.Placements)
        {
            if (!MefCollisionMesh.TryRead(placement.Model.Container, meshIndex: 0, out MefCollisionMesh? mesh)
                || mesh is null)
            {
                continue;
            }

            foreach (MefCollisionFace face in mesh.Faces)
            {
                if (mesh.IsPortal(face.Material))
                {
                    continue;
                }

                int global = mesh.GlobalMaterial(face.Material);
                if (global == face.Material
                    || !materials.TryGet(global, out GameMaterial material)
                    || !material.RecordsImpacts)
                {
                    continue;
                }

                foundMappedFace = true;
                ObjectCollisionIndex index = ObjectCollisionIndex.Build([placement]);
                if (!TryCastThroughFace(placement, mesh, face, index, out WorldRayHit hit))
                {
                    continue;
                }

                Assert.Equal(global, hit.Material);
                BulletMarks marks = new();
                Assert.True(marks.TryAdd(hit, BulletMarks.SurfaceOf(placement.Owner, placement), materials));
                Assert.Equal(material.Entry.TextureFor(isEntry: true), Assert.Single(marks.Marks).Texture);
                return;
            }
        }

        Assert.True(foundMappedFace, "Level 1 has no mapped, impact-recording collision face to trace");
        Assert.Fail("No mapped Level 1 collision face could be reached through ObjectCollisionIndex");
    }

    private static bool TryCastThroughFace(
        WorldPlacement placement,
        MefCollisionMesh mesh,
        MefCollisionFace face,
        ObjectCollisionIndex index,
        out WorldRayHit hit)
    {
        Matrix3f rotation = placement.Orientation.Transposed();
        Vector3f localA = rotation.Transform(mesh.Vertices[face.I0]);
        Vector3f localB = rotation.Transform(mesh.Vertices[face.I1]);
        Vector3f localC = rotation.Transform(mesh.Vertices[face.I2]);
        Vector3d origin = placement.Position * WorldScene.UnitsPerMetre;
        Vector3d a = origin + new Vector3d(localA.X, localA.Y, localA.Z);
        Vector3d b = origin + new Vector3d(localB.X, localB.Y, localB.Z);
        Vector3d c = origin + new Vector3d(localC.X, localC.Y, localC.Z);

        Vector3d ab = b - a;
        Vector3d ac = c - a;
        Vector3d normal = new(
            (ab.Y * ac.Z) - (ab.Z * ac.Y),
            (ab.Z * ac.X) - (ab.X * ac.Z),
            (ab.X * ac.Y) - (ab.Y * ac.X));
        double length = Math.Sqrt(
            (normal.X * normal.X) + (normal.Y * normal.Y) + (normal.Z * normal.Z));
        if (length <= 1e-9)
        {
            hit = default;
            return false;
        }

        normal /= length;
        Vector3d centre = new(
            (a.X + b.X + c.X) / 3.0,
            (a.Y + b.Y + c.Y) / 3.0,
            (a.Z + b.Z + c.Z) / 3.0);
        const double reach = 128.0;
        Vector3d from = centre - (normal * reach);
        Vector3d to = centre + (normal * reach);
        if (index.TryCast(from, to, out hit))
        {
            return true;
        }

        return index.TryCast(to, from, out hit);
    }

    /// <summary>The retail material table, as level 1 loads it.</summary>
    internal static GameMaterialRegistry LevelOneMaterials() => Load(1).Materials;

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
