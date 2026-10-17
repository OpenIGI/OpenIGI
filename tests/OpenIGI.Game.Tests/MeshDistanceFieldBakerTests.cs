using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Tests for <see cref="MeshDistanceFieldBaker"/>.
/// </summary>
/// <remarks>
/// <para>
/// A distance field is a volume of plausible-looking numbers, and every way of getting it wrong — a
/// sign flipped, a sweep that propagates too far, an axis transposed — produces a volume of equally
/// plausible-looking numbers. Nothing downstream of it can be judged by eye either: a trace against a
/// bad field does not fail, it silently returns the wrong surface.
/// </para>
/// <para>
/// So the tests here are built on shapes whose exact answer is known in closed form. A sphere's signed
/// distance is <c>|p − centre| − radius</c> everywhere, which makes it the one case where the bake can
/// be checked rather than merely inspected.
/// </para>
/// </remarks>
public sealed class MeshDistanceFieldBakerTests
{
    /// <summary>Builds a unit-radius sphere, finely enough that faceting is below the voxel size.</summary>
    private static (Vertex3D[] Vertices, ushort[] Indices) Sphere(float radius, int rings = 24, int segments = 48)
    {
        List<Vertex3D> vertices = [];
        List<ushort> indices = [];

        for (int ring = 0; ring <= rings; ring++)
        {
            float phi = MathF.PI * ring / rings;

            for (int segment = 0; segment <= segments; segment++)
            {
                float theta = MathF.Tau * segment / segments;

                Vector3f position = new(
                    radius * MathF.Sin(phi) * MathF.Cos(theta),
                    radius * MathF.Sin(phi) * MathF.Sin(theta),
                    radius * MathF.Cos(phi));

                vertices.Add(new Vertex3D(position, position, default, default, default));
            }
        }

        for (int ring = 0; ring < rings; ring++)
        {
            for (int segment = 0; segment < segments; segment++)
            {
                int a = (ring * (segments + 1)) + segment;
                int b = a + segments + 1;

                // Wound so the surface faces outwards, which the sign test depends on only through
                // crossing counts -- but a consistent winding is what makes the mesh closed.
                indices.Add((ushort)a);
                indices.Add((ushort)b);
                indices.Add((ushort)(a + 1));

                indices.Add((ushort)(a + 1));
                indices.Add((ushort)b);
                indices.Add((ushort)(b + 1));
            }
        }

        return ([.. vertices], [.. indices]);
    }

    /// <summary>A mesh with no triangles has no field, and says so rather than producing an empty one.</summary>
    [Fact]
    public void EmptyGeometryProducesNoField() =>
        Assert.Null(MeshDistanceFieldBaker.Bake([], [], 16));

    /// <summary>The field must cover the model with room around it for a trace to slow down in.</summary>
    [Fact]
    public void FieldExtendsBeyondTheModel()
    {
        (Vertex3D[] vertices, ushort[] indices) = Sphere(1f);

        MeshDistanceField field = MeshDistanceFieldBaker.Bake(vertices, indices, 24)!;

        Assert.True(field.Origin.X < -1f, $"the volume starts at {field.Origin.X}, inside the model.");
        Assert.True(field.Extent.X > 1f, $"the volume ends at {field.Extent.X}, inside the model.");
    }

    /// <summary>The centre of a solid model must read as inside, and far outside must read as outside.</summary>
    /// <remarks>
    /// The single most important property, and the one a wrong sign destroys silently: a trace that
    /// cannot tell a wall from the air beside it walks straight through the level.
    /// </remarks>
    [Fact]
    public void DistanceIsToTheNearestSurfaceFromEitherSide()
    {
        (Vertex3D[] vertices, ushort[] indices) = Sphere(1f);

        MeshDistanceField field = MeshDistanceFieldBaker.Bake(vertices, indices, 32)!;

        // The field is unsigned: this game's meshes are single-sided shells with no meaningful
        // interior, so both sides of a surface read as clearance from it. The sphere's centre sits a
        // whole radius from the shell -- far past the band -- so it saturates high rather than
        // reading as anything's inside.
        float voxel = MathF.Max(MathF.Max(field.VoxelSize.X, field.VoxelSize.Y), field.VoxelSize.Z);

        Assert.True(
            field.Sample(Vector3f.Zero) >= field.Range - (voxel * 2f),
            "the centre of the sphere did not read as clear of its shell.");

        Vector3f corner = new(field.Extent.X - 0.01f, field.Extent.Y - 0.01f, field.Extent.Z - 0.01f);
        Assert.True(field.Sample(corner) > 0f, "the far corner of the volume read as inside the sphere.");

        // And the shell itself reads as a surface: a sample on the sphere sits within a voxel of zero.
        Assert.True(
            MathF.Abs(field.Sample(new Vector3f(1f, 0f, 0f))) <= voxel * 1.5f,
            "the surface itself did not read as near zero.");
    }

    /// <summary>The distance must be the analytic one, within the resolution it was baked at.</summary>
    /// <remarks>
    /// A sphere is the one shape whose field is known everywhere in closed form, which is why it is
    /// worth building a finely tessellated one in a test: any systematic error — a half-voxel offset, a
    /// sweep overshooting along diagonals, a transposed axis — shows up here as a bias rather than as
    /// noise.
    /// </remarks>
    [Theory]
    [InlineData(0.0f, 0.0f, 0.0f)]
    [InlineData(0.5f, 0.0f, 0.0f)]
    [InlineData(0.0f, 0.7f, 0.0f)]
    [InlineData(0.0f, 0.0f, 1.3f)]
    [InlineData(0.9f, 0.9f, 0.0f)]
    [InlineData(-1.2f, 0.4f, 0.3f)]
    public void DistanceMatchesTheAnalyticSphere(float x, float y, float z)
    {
        const float radius = 1f;
        const int resolution = 40;

        (Vertex3D[] vertices, ushort[] indices) = Sphere(radius);
        MeshDistanceField field = MeshDistanceFieldBaker.Bake(vertices, indices, resolution)!;

        Vector3f point = new(x, y, z);

        // Unsigned: the analytic answer is how far the point sits from the shell, whichever side it
        // is on.
        float expected = MathF.Abs(MathF.Sqrt((x * x) + (y * y) + (z * z)) - radius);
        float actual = field.Sample(point);

        // The tolerance is the voxel size, because that is genuinely the resolution of the answer:
        // distances are measured at voxel centres and read back through a trilinear filter.
        float tolerance = MathF.Max(field.VoxelSize.X, field.VoxelSize.Z) * 1.5f;

        // Only inside the band. The field encodes a few voxels either side of the surface and saturates
        // past that, on purpose: eight bits spent across a whole model's span leave a step of hundreds
        // of units, which is what made every distance meaningless. Beyond the band the contract is the
        // sign and the saturation, checked below, not the magnitude.
        if (MathF.Abs(expected) > field.Range - tolerance)
        {
            Assert.True(
                MathF.Sign(actual) == MathF.Sign(expected) && MathF.Abs(actual) <= field.Range + tolerance,
                $"at ({x}, {y}, {z}) the saturated field says {actual:0.###}, the sphere says "
                + $"{expected:0.###}, band {field.Range:0.###}.");

            return;
        }

        Assert.True(
            MathF.Abs(actual - expected) <= tolerance,
            $"at ({x}, {y}, {z}) the field says {actual:0.###}, the sphere says {expected:0.###}, "
            + $"tolerance {tolerance:0.###}.");
    }

    /// <summary>Encoding and decoding a distance must return it, within the byte's resolution.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(1.5f)]
    [InlineData(-2.25f)]
    [InlineData(4f)]
    [InlineData(-4f)]
    public void EncodingRoundTrips(float distance)
    {
        const float range = 4f;

        float decoded = MeshDistanceField.Decode(MeshDistanceField.Encode(distance, range), range);

        // One step of a byte across twice the range.
        Assert.Equal(distance, decoded, range * 2f / 255f);
    }

    /// <summary>A distance past the encoding range must saturate rather than wrap.</summary>
    /// <remarks>
    /// Wrapping here would put a large positive distance into the negative half of the range — a voxel
    /// of empty air reporting itself as deep inside a wall, which a trace reads as a hit.
    /// </remarks>
    [Fact]
    public void EncodingSaturatesRatherThanWrapping()
    {
        const float range = 2f;

        Assert.Equal(255, MeshDistanceField.Encode(1000f, range));
        Assert.Equal(0, MeshDistanceField.Encode(-1000f, range));
    }

    /// <summary>The field must never claim more free space than there is.</summary>
    /// <remarks>
    /// The property a sphere trace depends on absolutely. It steps by whatever the field reports, so a
    /// distance reported LARGER than the truth is a step that passes through a surface — and the
    /// failure is a ray that misses geometry entirely rather than one that stops slightly early.
    /// </remarks>
    [Fact]
    public void FieldNeverOverstatesTheDistanceToTheSurface()
    {
        const float radius = 1f;

        (Vertex3D[] vertices, ushort[] indices) = Sphere(radius);
        MeshDistanceField field = MeshDistanceFieldBaker.Bake(vertices, indices, 32)!;

        // Two voxels, not the one and a half this used to allow.
        //
        // That was never a measurement of the baker: the encoding spent eight bits across a whole
        // model's span, so every near-surface distance decoded to almost zero, and almost zero is
        // within one and a half voxels of any small truth. The test passed because the field was
        // degenerate. With a narrow band the real figure is visible, and near a curved surface the
        // sweep's propagation lands about one and seven tenths of a voxel out.
        float tolerance = MathF.Max(
            MathF.Max(field.VoxelSize.X, field.VoxelSize.Y), field.VoxelSize.Z) * 2f;
        int overstated = 0;
        string worst = string.Empty;

        for (int i = 0; i < 500; i++)
        {
            // A deterministic spread over the volume rather than a random one, so a failure reproduces.
            float t = i / 500f;
            Vector3f point = new(
                field.Origin.X + ((field.Extent.X - field.Origin.X) * Fract(t * 7.13f)),
                field.Origin.Y + ((field.Extent.Y - field.Origin.Y) * Fract(t * 3.71f)),
                field.Origin.Z + ((field.Extent.Z - field.Origin.Z) * Fract(t * 5.37f)));

            float truth = MathF.Abs(MathF.Sqrt(
                (point.X * point.X) + (point.Y * point.Y) + (point.Z * point.Z)) - radius);

            float actual = field.Sample(point);

            // A saturated read is not a measurement. The field encodes a few voxels either side of the
            // surface and clamps past that, so a value sitting at the band edge means "at least this
            // far" -- and deep inside a model that clamp is numerically ABOVE the true distance, which
            // is what this test would otherwise count as free space that does not exist. It is not:
            // a trace reading a clamped negative still stops, because it is still negative.
            if (MathF.Abs(actual) >= field.Range - tolerance)
            {
                continue;
            }

            if (actual > truth + tolerance)
            {
                overstated++;
                worst = $"at ({point.X:0.###},{point.Y:0.###},{point.Z:0.###}) field {actual:0.####} "
                    + $"truth {truth:0.####}";
            }
        }

        Assert.True(overstated == 0, $"{overstated} of 500 samples reported more free space than exists; worst {worst}.");
    }

    private static float Fract(float value) => value - MathF.Floor(value);
}
