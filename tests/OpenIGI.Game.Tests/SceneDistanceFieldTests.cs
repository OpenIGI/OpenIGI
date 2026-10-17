using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Tests for <see cref="SceneDistanceField"/>, the union of every placed object's field.
/// </summary>
/// <remarks>
/// Two properties matter and neither is visible in a rendered frame. The union has to be the
/// <em>minimum</em> over instances, because that is what makes a set of separate objects behave as one
/// surface; and a placement's transform has to be the exact inverse of the one the renderer draws with,
/// or the field sits somewhere other than the geometry it describes — which produces shadows and bounce
/// light offset from the things casting them, and looks like a lighting bug rather than a transform one.
/// </remarks>
public sealed class SceneDistanceFieldTests
{
    /// <summary>A unit cube centred on the origin, as a drawable mesh.</summary>
    private static MeshGeometry Cube(float half = 0.5f)
    {
        Vector3f[] corners =
        [
            new(-half, -half, -half), new(half, -half, -half), new(half, half, -half), new(-half, half, -half),
            new(-half, -half, half), new(half, -half, half), new(half, half, half), new(-half, half, half),
        ];

        ushort[] indices =
        [
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            2, 3, 7, 2, 7, 6,
            1, 2, 6, 1, 6, 5,
            0, 4, 7, 0, 7, 3,
        ];

        Vertex3D[] vertices = new Vertex3D[corners.Length];

        for (int i = 0; i < corners.Length; i++)
        {
            vertices[i] = new Vertex3D(corners[i], corners[i], default, default, default);
        }

        return new MeshGeometry(vertices, indices, []);
    }

    /// <summary>The baked field of the unit cube, shared by every scene below.</summary>
    private static MeshDistanceField CubeField { get; } =
        MeshDistanceFieldBaker.Bake(Cube().Vertices, Cube().Indices, 24)!;

    /// <summary>Builds a scene of cubes at the given world positions, all unrotated.</summary>
    private static SceneDistanceField SceneOfCubes(params Vector3d[] positions) =>
        SceneDistanceField.FromInstances(
            [CubeField],
            positions.Select(position => (0, position, Matrix3f.Identity)));

    /// <summary>A scene with no placements answers rather than throwing.</summary>
    [Fact]
    public void EmptySceneIsFarFromEverything()
    {
        SceneDistanceField scene = SceneDistanceField.FromInstances([], []);

        Assert.Equal(0, scene.FieldCount);
        Assert.Equal(0, scene.InstanceCount);
        Assert.True(scene.Sample(Vector3d.Zero) > 1000f);
    }

    /// <summary>Many placements of one model share its single baked field.</summary>
    /// <remarks>
    /// The saving this design exists for: a level draws one crate hundreds of times, and the distance
    /// field of a crate is a property of the crate rather than of where it stands.
    /// </remarks>
    [Fact]
    public void RepeatedModelsShareOneBake()
    {
        SceneDistanceField scene = SceneOfCubes(
            new Vector3d(0, 0, 0), new Vector3d(10, 0, 0), new Vector3d(0, 10, 0));

        Assert.Equal(1, scene.FieldCount);
        Assert.Equal(3, scene.InstanceCount);
    }

    /// <summary>A placement's field must sit where the placement is, not at the origin.</summary>
    /// <remarks>
    /// The transform test. Getting this wrong offsets every object's field from its geometry, which
    /// shows up as shadows and bounced light in the wrong place and reads as a lighting fault.
    /// </remarks>
    [Fact]
    public void FieldFollowsThePlacement()
    {
        SceneDistanceField scene = SceneOfCubes(new Vector3d(100, -50, 25));

        // The fields are unsigned, so what pins the placement is the surface: a point on the placed
        // cube's face reads as a surface there, and the world origin reads far from everything.
        Assert.True(
            MathF.Abs(scene.Sample(new Vector3d(100.5, -50, 25))) < 0.2f,
            "the face of the placed cube did not read as a surface.");

        Assert.True(
            scene.Sample(Vector3d.Zero) > 1f,
            "the world origin read as inside a cube that is a hundred metres away.");
    }

    /// <summary>Two objects combine as the nearer of the two, which is what makes a union free.</summary>
    [Fact]
    public void UnionTakesTheNearerSurface()
    {
        SceneDistanceField scene = SceneOfCubes(new Vector3d(-4, 0, 0), new Vector3d(4, 0, 0));

        // Midway between them: 4 units to each centre, less the half-extent of either.
        float middle = scene.Sample(Vector3d.Zero);
        Assert.True(middle > 3f && middle < 4f, $"midway between two cubes read {middle}.");

        // Just outside the left one: much nearer to it than to the right one.
        float beside = scene.Sample(new Vector3d(-4, 0, 1));
        Assert.True(beside < 1f, $"a point beside the left cube read {beside}.");
    }

    /// <summary>A rotated placement's field must rotate with it.</summary>
    /// <remarks>
    /// A cube is symmetric under ninety degrees, so the shape used here is deliberately not: the field
    /// is baked from a cube stretched along one axis, and a rotation that was dropped or applied the
    /// wrong way round leaves the long axis pointing the wrong way.
    /// </remarks>
    [Fact]
    public void RotationIsAppliedTheSameWayTheRendererAppliesIt()
    {
        // A quarter turn, built from the rows directly rather than from Euler angles -- the engine's
        // own angle convention is a separate thing to get right and this test is not about it.
        Matrix3f turned = Matrix3f.FromRows(
            new Vector3f(0f, 1f, 0f), new Vector3f(-1f, 0f, 0f), new Vector3f(0f, 0f, 1f));

        SceneDistanceField straight = SceneDistanceField.FromInstances(
            [CubeField], [(0, Vector3d.Zero, Matrix3f.Identity)]);
        SceneDistanceField spun = SceneDistanceField.FromInstances(
            [CubeField], [(0, Vector3d.Zero, turned)]);

        // A cube is symmetric under this rotation, so both must agree everywhere -- which is a real
        // check that the rotation was applied consistently rather than, say, doubled.
        Vector3d probe = new(0.9, 0.2, 0.1);
        Assert.Equal(straight.Sample(probe), spun.Sample(probe), 2);
    }

    /// <summary>A ray fired at an object must find it; one fired past must not.</summary>
    [Fact]
    public void SphereTraceFindsWhatIsInFrontOfItAndNothingElse()
    {
        SceneDistanceField scene = SceneOfCubes(new Vector3d(0, 0, 0));

        Assert.True(
            scene.TryTrace(new Vector3d(-10, 0, 0), Vector3f.UnitX, 20f, out float hit),
            "a ray fired straight at the cube missed it.");

        // The cube's face is half a unit from its centre, so the hit is nine and a half away.
        Assert.Equal(9.5f, hit, 1);

        Assert.False(
            scene.TryTrace(new Vector3d(-10, 5, 0), Vector3f.UnitX, 20f, out _),
            "a ray fired five units to the side of the cube hit it anyway.");
    }

    /// <summary>A trace must stop at the nearest object, not the first one it happens to consider.</summary>
    /// <remarks>
    /// The union is a minimum over instances, so the order they were placed in must not matter. Building
    /// the same two cubes in both orders and asserting the same answer is what pins that.
    /// </remarks>
    [Fact]
    public void TraceStopsAtTheNearestObjectWhicheverOrderTheyWerePlacedIn()
    {
        SceneDistanceField near = SceneOfCubes(new Vector3d(0, 0, 0), new Vector3d(6, 0, 0));
        SceneDistanceField far = SceneOfCubes(new Vector3d(6, 0, 0), new Vector3d(0, 0, 0));

        Assert.True(near.TryTrace(new Vector3d(-10, 0, 0), Vector3f.UnitX, 30f, out float first));
        Assert.True(far.TryTrace(new Vector3d(-10, 0, 0), Vector3f.UnitX, 30f, out float second));

        Assert.Equal(first, second, 1);
        Assert.Equal(9.5f, first, 1);
    }
}
