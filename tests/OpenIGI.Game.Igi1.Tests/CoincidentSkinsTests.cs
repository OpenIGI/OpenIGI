using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>
/// A sheet modelled with no thickness -- two faces in one plane, facing apart -- is found and drawn culled, so a mesh drawn
/// two-sided does not flicker between its two faces.
/// </summary>
public sealed class CoincidentSkinsTests
{
    [Fact]
    public void Two_faces_of_one_patch_facing_apart_are_skins()
    {
        // A square facing up and the same square, triangulated the other way, facing down.
        (Vertex3D[] vertices, ushort[] indices) = Mesh(
            Square(0f, 0f, 0f, up: true, diagonal: false),
            Square(0f, 0f, 0f, up: false, diagonal: true));

        Assert.All(CoincidentSkins.Find(vertices, indices, 1e-4f), Assert.True);
    }

    [Fact]
    public void Faces_apart_in_one_plane_or_a_hair_above_it_are_not()
    {
        // Side by side in one plane, only touching along an edge; and one a centimetre above the other.
        (Vertex3D[] beside, ushort[] besideIndices) = Mesh(Square(0f, 0f, 0f, up: true, diagonal: false), Square(1f, 0f, 0f, up: false, diagonal: false));
        Assert.All(CoincidentSkins.Find(beside, besideIndices, 1e-4f), Assert.False);

        (Vertex3D[] stacked, ushort[] stackedIndices) = Mesh(Square(0f, 0f, 0f, up: true, diagonal: false), Square(0f, 0f, 0.01f, up: false, diagonal: false));
        Assert.All(CoincidentSkins.Find(stacked, stackedIndices, 1e-4f), Assert.False);

        // Facing the same way, one over the other: overdraw, not a sheet's two faces.
        (Vertex3D[] doubled, ushort[] doubledIndices) = Mesh(Square(0f, 0f, 0f, up: true, diagonal: false), Square(0f, 0f, 0f, up: true, diagonal: true));
        Assert.All(CoincidentSkins.Find(doubled, doubledIndices, 1e-4f), Assert.False);
    }

    [Fact]
    public void A_subset_puts_its_skins_last_and_counts_them()
    {
        // A lone square first, then a sheet's two faces, all in one subset.
        (Vertex3D[] vertices, ushort[] indices) = Mesh(
            Square(5f, 5f, 0f, up: true, diagonal: false),
            Square(0f, 0f, 0f, up: true, diagonal: false),
            Square(0f, 0f, 0f, up: false, diagonal: true));
        ushort[] lone = indices[..6];

        MeshSubset[] subsets = CoincidentSkins.Partition(vertices, indices, [new MeshSubset(0, indices.Length, 0, 0)], 1e-4f);

        Assert.Equal(12, subsets[0].SkinIndexCount);
        Assert.Equal(lone, indices[..6]);
    }

    /// <summary>The first mission's small barracks' awning: one quad and its reverse, every face of it drawn culled.</summary>
    [RequiresGameDataFact]
    public void The_first_missions_barracks_awning_is_drawn_culled()
    {
        string path = Path.Combine(GameData.RequireRoot(), "missions", "location0", "level1", "models", "level1.res");
        IresArchive archive = IresArchive.Read(File.ReadAllBytes(path));
        IresEntry entry = archive.Entries.Single(static e => Path.GetFileNameWithoutExtension(e.Name.Replace('\\', '/')) == "418_03_1");
        MefModel model = MefModel.Read(entry.Data);

        MeshGeometry geometry = MefMeshBuilder.Build(model, (float)(1.0 / WorldScene.UnitsPerMetre))!;

        Assert.Equal(geometry.Indices.Length, geometry.Subsets.Sum(static s => s.SkinIndexCount));
    }

    private static (Vertex3D[] Vertices, ushort[] Indices) Mesh(params Vector3f[][] squares)
    {
        List<Vertex3D> vertices = [];
        List<ushort> indices = [];
        foreach (Vector3f[] square in squares)
        {
            Vector3f normal = Vector3f.Cross(square[1] - square[0], square[2] - square[0]).Normalized();
            int first = vertices.Count;
            vertices.AddRange(square.Select(p => new Vertex3D(p, normal, 0f, 0f)));
            indices.AddRange([(ushort)first, (ushort)(first + 1), (ushort)(first + 2), (ushort)(first + 3), (ushort)(first + 4), (ushort)(first + 5)]);
        }

        return ([.. vertices], [.. indices]);
    }

    // A unit square as two triangles of three corners each, wound to face up or down, split along one diagonal or the other.
    private static Vector3f[] Square(float x, float y, float z, bool up, bool diagonal)
    {
        Vector3f a = new(x, y, z), b = new(x + 1f, y, z), c = new(x + 1f, y + 1f, z), d = new(x, y + 1f, z);
        Vector3f[] corners = diagonal ? [a, b, d, b, c, d] : [a, b, c, a, c, d];
        if (!up)
        {
            (corners[1], corners[2]) = (corners[2], corners[1]);
            (corners[4], corners[5]) = (corners[5], corners[4]);
        }

        return corners;
    }
}
