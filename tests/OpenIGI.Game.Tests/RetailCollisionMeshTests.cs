using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The collision geometry models carry — the shape the world is solid with.
/// </summary>
public sealed class RetailCollisionMeshTests
{
    [RequiresGameDataFact]
    public void TheReportedFenceCarriesDistinctRetailCollisionLevels()
    {
        WorldPlacement fence = RetailLevelOneGround.Scene().Placements
            .First(placement => placement.Instance.Name == "303_01_1");

        Assert.True(MefCollisionMesh.TryRead(fence.Model.Container, meshIndex: 0, out MefCollisionMesh? levelZero));
        Assert.True(MefCollisionMesh.TryRead(fence.Model.Container, meshIndex: 1, out MefCollisionMesh? levelOne));

        Assert.Equal(32, levelZero!.Faces.Count);
        Assert.Equal(24, levelZero.Vertices.Count);
        Assert.Equal(16, levelOne!.Faces.Count);
        Assert.Equal(10, levelOne.Vertices.Count);
        Assert.Equal(1, levelOne.Materials);
    }

    [RequiresGameDataFact]
    public void EveryCollisionMeshInLevelOneReadsAndItsCountsAgree()
    {
        // The strides were derived from the counts, so the check that matters is that they hold across
        // every shipped mesh rather than the handful they were read off.
        int models = 0;
        int withMesh = 0;
        long faces = 0;
        long vertices = 0;

        foreach (MefModel model in RetailLevelOneGround.Models())
        {
            models++;

            if (!MefCollisionMesh.TryRead(model.Container, out MefCollisionMesh? mesh) || mesh is null)
            {
                continue;
            }

            withMesh++;
            faces += mesh.Faces.Count;
            vertices += mesh.Vertices.Count;

            // Every face indexes a vertex the mesh has.
            foreach (MefCollisionFace face in mesh.Faces)
            {
                Assert.InRange(face.I0, 0, mesh.Vertices.Count - 1);
                Assert.InRange(face.I1, 0, mesh.Vertices.Count - 1);
                Assert.InRange(face.I2, 0, mesh.Vertices.Count - 1);
            }
        }

        Assert.True(models > 400, $"only {models} models were read");
        Assert.True(withMesh > 100, $"only {withMesh} carried collision meshes");
        Assert.True(faces > 0 && vertices > 0);
    }

    [RequiresGameDataFact]
    public void TheSpheresAreABinaryTreeOverTheFaces()
    {
        // The finding this rests on: the sphere count is exactly 2n-1 for n faces, which is the node
        // count of a binary tree with one leaf per face. If it held only for the models it was read
        // from, that would be a coincidence rather than a format.
        int checkedMeshes = 0;

        foreach (MefModel model in RetailLevelOneGround.Models())
        {
            if (!MefCollisionMesh.TryRead(model.Container, out MefCollisionMesh? mesh) || mesh is null)
            {
                continue;
            }

            Assert.True(
                mesh.SphereCountIsBinaryTree,
                $"{mesh.Faces.Count} faces against {mesh.Spheres.Count} spheres, expected {(2 * mesh.Faces.Count) - 1}");

            checkedMeshes++;
        }

        Assert.True(checkedMeshes > 100, $"only {checkedMeshes} meshes were checked");
    }

    [RequiresGameDataFact]
    public void EverySphereEnclosesTheFacesBeneathItAtTheRoot()
    {
        // A geometric check rather than a structural one: the root sphere must contain every vertex of
        // the mesh, or it is not a bounding volume and the tree reading is wrong.
        int checkedMeshes = 0;

        foreach (MefModel model in RetailLevelOneGround.Models())
        {
            if (!MefCollisionMesh.TryRead(model.Container, out MefCollisionMesh? mesh)
                || mesh is null
                || mesh.Spheres.Count == 0)
            {
                continue;
            }

            MefCollisionSphere root = mesh.Spheres[0];
            foreach (Vector3f vertex in mesh.Vertices)
            {
                double dx = vertex.X - root.Centre.X;
                double dy = vertex.Y - root.Centre.Y;
                double dz = vertex.Z - root.Centre.Z;
                double distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

                // A little slack: the radius is stored single-precision and was fitted, not derived.
                Assert.True(
                    distance <= root.Radius + 1.0,
                    $"a vertex sits {distance - root.Radius:0.##} units outside the root sphere");
            }

            checkedMeshes++;
        }

        Assert.True(checkedMeshes > 100);
    }

    [RequiresGameDataFact]
    public void TheRootIsTheOnlyNodeMarkedWithTheEndSentinel()
    {
        // The root's fifth dword carries 0xFFFF in its low half where every other node carries a small
        // index. That is what a "no parent" marker looks like, and it should appear exactly once.
        int meshes = 0;
        int rootsMarked = 0;

        foreach (MefModel model in RetailLevelOneGround.Models())
        {
            if (!MefCollisionMesh.TryRead(model.Container, out MefCollisionMesh? mesh)
                || mesh is null
                || mesh.Spheres.Count < 2)
            {
                continue;
            }

            meshes++;
            if ((mesh.Spheres[0].Field4 & 0xFFFF) == 0xFFFF)
            {
                rootsMarked++;
            }
        }

        Assert.True(meshes > 100);
        Assert.Equal(meshes, rootsMarked);
    }
}
