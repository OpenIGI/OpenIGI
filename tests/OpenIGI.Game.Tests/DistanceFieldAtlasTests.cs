using Xunit;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Tests for <see cref="DistanceFieldAtlas"/>, which packs every model's field into one volume.
/// </summary>
/// <remarks>
/// The packing is pure index arithmetic over three axes, which is the kind of code that is either
/// exactly right or wrong in a way no picture reveals: a field written one slot over still produces a
/// volume full of plausible distances, and a trace against it silently reports the wrong model's
/// surface. So the tests here read fields back out of the atlas and compare them, voxel for voxel,
/// against the fields that went in.
/// </remarks>
public sealed class DistanceFieldAtlasTests
{
    /// <summary>A box of the given half-extents, as a drawable mesh.</summary>
    private static (Vertex3D[] Vertices, ushort[] Indices) Box(float halfX, float halfY, float halfZ)
    {
        Vector3f[] corners =
        [
            new(-halfX, -halfY, -halfZ), new(halfX, -halfY, -halfZ),
            new(halfX, halfY, -halfZ), new(-halfX, halfY, -halfZ),
            new(-halfX, -halfY, halfZ), new(halfX, -halfY, halfZ),
            new(halfX, halfY, halfZ), new(-halfX, halfY, halfZ),
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

        return (vertices, indices);
    }

    private static MeshDistanceField Field(float halfX, float halfY, float halfZ, int resolution = 16)
    {
        (Vertex3D[] vertices, ushort[] indices) = Box(halfX, halfY, halfZ);
        return MeshDistanceFieldBaker.Bake(vertices, indices, resolution)!;
    }

    /// <summary>An atlas of nothing is empty rather than a degenerate volume.</summary>
    [Fact]
    public void EmptyInputProducesAnEmptyAtlas()
    {
        DistanceFieldAtlas atlas = DistanceFieldAtlas.Build([]);

        Assert.Empty(atlas.Slots);
        Assert.Equal(0, atlas.VoxelCount);
    }

    /// <summary>Every slot must be large enough for the largest field.</summary>
    [Fact]
    public void SlotsAreSizedForTheLargestField()
    {
        MeshDistanceField small = Field(0.5f, 0.5f, 0.5f, 12);
        MeshDistanceField large = Field(2f, 0.5f, 0.5f, 24);

        DistanceFieldAtlas atlas = DistanceFieldAtlas.Build([small, large]);

        Assert.True(atlas.SlotWidth >= large.SizeX);
        Assert.True(atlas.SlotHeight >= Math.Max(small.SizeY, large.SizeY));
        Assert.True(atlas.SlotDepth >= Math.Max(small.SizeZ, large.SizeZ));
    }

    /// <summary>No two fields may be given the same slot.</summary>
    [Fact]
    public void EveryFieldGetsItsOwnSlot()
    {
        List<MeshDistanceField> fields = [];

        for (int i = 0; i < 9; i++)
        {
            fields.Add(Field(0.5f + (i * 0.1f), 0.5f, 0.5f, 10));
        }

        DistanceFieldAtlas atlas = DistanceFieldAtlas.Build(fields);

        HashSet<(float X, float Y, float Z)> corners = [];

        foreach (DistanceFieldAtlas.Slot slot in atlas.Slots)
        {
            Assert.True(
                corners.Add((slot.Corner.X, slot.Corner.Y, slot.Corner.Z)),
                $"two fields were packed at the same corner {slot.Corner}.");
        }

        Assert.Equal(9, atlas.Slots.Length);
    }

    /// <summary>Every slot must lie wholly inside the atlas.</summary>
    [Fact]
    public void EverySlotFitsInsideTheVolume()
    {
        List<MeshDistanceField> fields = [];

        for (int i = 0; i < 7; i++)
        {
            fields.Add(Field(0.5f, 0.5f + (i * 0.2f), 0.5f, 10));
        }

        DistanceFieldAtlas atlas = DistanceFieldAtlas.Build(fields);

        foreach (DistanceFieldAtlas.Slot slot in atlas.Slots)
        {
            Assert.True(slot.Corner.X + slot.Size.X <= atlas.Width, "a slot ran past the atlas width.");
            Assert.True(slot.Corner.Y + slot.Size.Y <= atlas.Height, "a slot ran past the atlas height.");
            Assert.True(slot.Corner.Z + slot.Size.Z <= atlas.Depth, "a slot ran past the atlas depth.");
        }
    }

    /// <summary>A field read back out of the atlas must be the field that went in.</summary>
    /// <remarks>
    /// <b>The test the whole file exists for.</b> Every other property here could hold while the copy
    /// itself transposed an axis or slipped a row, and the result would still be a volume of entirely
    /// plausible distances. Comparing voxel centres against the original field is the only check that
    /// distinguishes a correct packing from a convincing one.
    /// </remarks>
    [Fact]
    public void PackedFieldsReadBackIdenticalToTheOriginals()
    {
        MeshDistanceField first = Field(0.5f, 0.5f, 0.5f, 12);
        MeshDistanceField second = Field(1.5f, 0.4f, 0.9f, 14);
        MeshDistanceField third = Field(0.3f, 1.2f, 0.3f, 10);

        MeshDistanceField[] fields = [first, second, third];
        DistanceFieldAtlas atlas = DistanceFieldAtlas.Build(fields);

        for (int index = 0; index < fields.Length; index++)
        {
            MeshDistanceField source = fields[index];

            for (int z = 0; z < source.SizeZ; z++)
            {
                for (int y = 0; y < source.SizeY; y++)
                {
                    for (int x = 0; x < source.SizeX; x++)
                    {
                        // The voxel's centre in the model's own space, which is where both the source
                        // field and the atlas agree the sample belongs.
                        Vector3f centre = new(
                            source.Origin.X + ((x + 0.5f) * source.VoxelSize.X),
                            source.Origin.Y + ((y + 0.5f) * source.VoxelSize.Y),
                            source.Origin.Z + ((z + 0.5f) * source.VoxelSize.Z));

                        Assert.Equal(source.At(x, y, z), atlas.Sample(index, centre), 5);
                    }
                }
            }
        }
    }

    /// <summary>Padding around a field must read as empty space, never as solid.</summary>
    /// <remarks>
    /// The encoding is centred on 128 so the sign survives, which means a zero-filled voxel decodes to
    /// the NEGATIVE extreme — deep inside a surface. Leaving the atlas at its default would surround
    /// every model with a shell of solid rock that a trace stops dead in, and the symptom would be
    /// shadows and occlusion appearing in mid-air around every object.
    /// </remarks>
    [Fact]
    public void UnfilledPaddingReadsAsEmptySpace()
    {
        MeshDistanceField small = Field(0.5f, 0.5f, 0.5f, 8);
        MeshDistanceField large = Field(0.5f, 0.5f, 0.5f, 20);

        DistanceFieldAtlas atlas = DistanceFieldAtlas.Build([small, large]);
        DistanceFieldAtlas.Slot slot = atlas.Slots[0];

        // A voxel inside the small field's slot but past the edge of the field itself.
        int px = (int)slot.Corner.X + (int)slot.Size.X;
        int py = (int)slot.Corner.Y;
        int pz = (int)slot.Corner.Z;

        // Only meaningful if the small field really does leave padding in its slot.
        Assert.True(px < atlas.Width, "the smaller field filled its slot, so there is no padding to test.");

        byte stored = atlas.Volume[((pz * atlas.Height) + py) * atlas.Width + px];

        Assert.True(
            MeshDistanceField.Decode(stored, slot.Range) > 0f,
            "padding decoded as inside a surface, which would surround every model with solid rock.");
    }
}
