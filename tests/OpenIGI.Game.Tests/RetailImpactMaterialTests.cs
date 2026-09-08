using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// What a round leaves and sounds like on the surfaces level one is actually built from, end to end:
/// a collision face names a per-mesh selector, the mesh's <c>TAMC</c> table resolves that to a global
/// game-material row, and that row alone decides the mark and the sample.
/// </summary>
/// <remarks>
/// This is the chain that was broken in the middle. Every mark in the game was attributed to
/// <c>Concrete</c> regardless of what was struck, which put a concrete hole on the 606 <c>Fence</c>
/// faces and the 695 <c>Glass</c> faces of level one alike — and on the rows that record no impact at
/// all, which the original skips outright.
/// </remarks>
public sealed class RetailImpactMaterialTests
{
    private static GameMaterialRegistry Materials()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        resources.Register(
            GameMaterialRegistry.ScriptResource,
            fileSystem.ReadAllBytes(GameMaterialRegistry.ScriptResource));

        GameLoop loop = new(new ManualTimeSource());
        GameMaterialRegistry materials = new();
        materials.Register(loop.Natives);
        materials.Load(resources, loop.Natives);
        return materials;
    }

    private static MefCollisionMesh FenceMesh(int level)
    {
        WorldPlacement fence = RetailLevelOneGround.Scene().Placements
            .First(placement => placement.Instance.Name == "303_01_1");
        Assert.True(MefCollisionMesh.TryRead(fence.Model.Container, level, out MefCollisionMesh? mesh));
        return mesh!;
    }

    /// <summary>
    /// The reported fence is built of three rows, and which one a face names decides everything. Its
    /// panels are <c>Fence</c>, which records no impact; its posts are <c>NormalMetal</c>, which does.
    /// </summary>
    [RequiresGameDataFact]
    public void TheFencePanelsLeaveNoMarkAndItsMetalPostsDo()
    {
        GameMaterialRegistry materials = Materials();
        MefCollisionMesh mesh = FenceMesh(0);

        int[] rows = [.. mesh.Faces.Select(face => mesh.GlobalMaterial(face.Material)).Distinct().Order()];
        Assert.Equal([1, 6, 18], rows);

        Assert.True(materials.TryGet(18, out GameMaterial fence));
        Assert.Equal("Fence", fence.Name);
        Assert.False(fence.RecordsImpacts);

        Assert.True(materials.TryGet(6, out GameMaterial metal));
        Assert.Equal("NormalMetal", metal.Name);
        Assert.True(metal.RecordsImpacts);

        BulletMarks marks = new();
        Assert.False(marks.TryAdd(Hit(18), materials));
        Assert.Empty(marks.Marks);

        Assert.True(marks.TryAdd(Hit(6), materials));
        Assert.Equal("bullethole_metal.tex", Assert.Single(marks.Marks).Texture);
    }

    /// <summary>
    /// The fence's lower collision level is entirely <c>HumanCollision</c> — a row that names neither a
    /// mark nor a sound, because it exists to stop a body rather than to be shot at.
    /// </summary>
    [RequiresGameDataFact]
    public void TheFencesBodyBlockerIsSilentAndUnmarked()
    {
        GameMaterialRegistry materials = Materials();
        MefCollisionMesh mesh = FenceMesh(1);

        Assert.All(
            mesh.Faces,
            face => Assert.Equal(21, mesh.GlobalMaterial(face.Material)));

        Assert.True(materials.TryGet(21, out GameMaterial blocker));
        Assert.Equal("HumanCollision", blocker.Name);
        Assert.False(blocker.RecordsImpacts);
        Assert.All(blocker.BulletSounds, sound => Assert.Equal(string.Empty, sound));

        BulletMarks marks = new();
        Assert.False(marks.TryAdd(Hit(21), materials));
        Assert.Equal(
            string.Empty,
            BulletImpactSounds.PickForImpact(materials, struckHuman: false, 21, new IgiRandom()));
    }

    /// <summary>
    /// Level one's collision names eighteen different rows, so attributing every hit to one of them was
    /// wrong for the overwhelming majority of the level rather than for an edge of it.
    /// </summary>
    [RequiresGameDataFact]
    public void LevelOneIsBuiltOfManyMaterialsAndEachResolvesToItsOwnRow()
    {
        GameMaterialRegistry materials = Materials();
        Dictionary<int, int> faces = [];

        foreach (MefModel model in RetailLevelOneGround.Models())
        {
            if (!MefCollisionMesh.TryRead(model.Container, out MefCollisionMesh? mesh) || mesh is null)
            {
                continue;
            }

            foreach (MefCollisionFace face in mesh.Faces)
            {
                int row = mesh.GlobalMaterial(face.Material);
                faces[row] = faces.TryGetValue(row, out int n) ? n + 1 : 1;
            }
        }

        // Every row the level names is a row the table declares — no face resolves to nothing.
        Assert.All(faces.Keys, row => Assert.True(materials.TryGet(row, out _)));
        Assert.True(faces.Count >= 15, $"level one named only {faces.Count} material rows");

        // And the rows that carry the fences and the glass are really there, in quantity.
        Assert.True(faces.GetValueOrDefault(18) > 100, "level one has Fence faces");
        Assert.True(faces.GetValueOrDefault(14) > 100, "level one has Glass faces");

        BulletMarks marks = new();
        Assert.True(marks.TryAdd(Hit(14), materials));
        Assert.Equal("hole_plexi1.tex", Assert.Single(marks.Marks).Texture);
    }

    private static WorldRayHit Hit(int material) =>
        new(new Vector3d(100, 200, 300), new Vector3f(0f, 0f, 1f), 10.0, material);
}
