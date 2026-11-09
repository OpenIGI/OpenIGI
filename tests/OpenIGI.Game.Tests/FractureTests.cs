using Xunit;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Game.World.Fracture;

namespace OpenIGI.Game.Tests;

/// <summary>The bomb blast's clean-room fracture: Voronoi pieces of a mesh, and a ragged hole through one.</summary>
public sealed class FractureTests
{
    /// <summary>A closed cube breaks into closed pieces whose boxes fill no more than the cube and no less than most of it.</summary>
    [Fact]
    public void CubeBreaksIntoClosedPiecesThatFillIt()
    {
        List<FracturePolygon> cube = Cube(1f);
        Vector3f[] seeds = VoronoiFracture.SeedsToward(new Vector3f(-1, -1, -1), new Vector3f(1, 1, 1), new Vector3f(1, 0, 0), 12, new IgiRandom(7u));
        List<FractureChunk> pieces = VoronoiFracture.Break(cube, seeds, [], 1e-6f);

        Assert.NotEmpty(pieces);
        float volume = 0f;
        foreach (FractureChunk piece in pieces)
        {
            Assert.True(IsClosed(piece), "every piece has a face on each side of it");
            volume += Enclosed(piece);
        }

        Assert.InRange(volume, 8f * 0.99f, 8f * 1.01f);
    }

    /// <summary>The same mesh and seed always break into the same pieces.</summary>
    [Fact]
    public void SameSeedBreaksTheSameWay()
    {
        static List<FractureChunk> Break(uint seed)
        {
            Vector3f[] seeds = VoronoiFracture.SeedsToward(new Vector3f(-1, -1, -1), new Vector3f(1, 1, 1), default, 9, new IgiRandom(seed));
            return VoronoiFracture.Break(Cube(1f), seeds, [], 1e-6f);
        }

        List<FractureChunk> a = Break(42u);
        List<FractureChunk> b = Break(42u);
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Centre, b[i].Centre);
            Assert.Equal(a[i].Faces.Count, b[i].Faces.Count);
        }
    }

    /// <summary>A hole through a slab keeps the slab beyond it, takes out what is inside it, and keeps the subset whole.</summary>
    [Fact]
    public void BreachCutsAHoleThroughASlab()
    {
        MeshGeometry slab = Slab(8f, 0.3f, 16);
        FracturePlane[] ball = BreachCutter.RaggedBall(default, 1.5f, new IgiRandom(3u));
        BreachCut? cut = BreachCutter.Cut(slab, ball);

        Assert.NotNull(cut);
        Assert.NotEmpty(cut.Removed);
        Assert.Single(cut.Kept.Subsets);
        Assert.Equal(cut.Kept.Indices.Length, cut.Kept.Subsets[0].IndexCount);

        // Nothing kept lies wholly inside the ball, and the far corners are still there.
        for (int i = 0; i < cut.Kept.Indices.Length; i += 3)
        {
            Vector3f middle = (cut.Kept.Vertices[cut.Kept.Indices[i]].Position + cut.Kept.Vertices[cut.Kept.Indices[i + 1]].Position
                + cut.Kept.Vertices[cut.Kept.Indices[i + 2]].Position) * (1f / 3f);
            Assert.Contains(ball, plane => plane.Distance(middle) > -1e-4f);
        }

        Assert.Contains(cut.Kept.Vertices, v => v.Position.X <= -3.99f && v.Position.Z <= -3.99f);
    }

    /// <summary>A hole that misses the mesh leaves it alone.</summary>
    [Fact]
    public void BreachThatMissesCutsNothing()
    {
        FracturePlane[] ball = BreachCutter.RaggedBall(new Vector3f(50, 0, 0), 1f, new IgiRandom(3u));
        Assert.Null(BreachCutter.Cut(Slab(4f, 0.3f, 4), ball));
    }

    // Closed: its faces' area-weighted normals cancel, as they do for any closed surface.
    private static bool IsClosed(FractureChunk piece)
    {
        Vector3f sum = default;
        float area = 0f;
        foreach (FracturePolygon face in piece.Faces)
        {
            sum += Normal(face);
            area += face.Area();
        }

        return sum.Length <= area * 0.02f;
    }

    // The volume a closed piece encloses, by the divergence theorem.
    private static float Enclosed(FractureChunk piece)
    {
        float volume = 0f;
        foreach (FracturePolygon face in piece.Faces)
        {
            Vector3f origin = face.Vertices[0].Position;
            for (int i = 1; i + 1 < face.Vertices.Count; i++)
            {
                volume += Vector3f.Dot(origin, Vector3f.Cross(face.Vertices[i].Position, face.Vertices[i + 1].Position)) / 6f;
            }
        }

        return MathF.Abs(volume);
    }

    private static Vector3f Normal(FracturePolygon face)
    {
        Vector3f sum = default;
        Vector3f origin = face.Vertices[0].Position;
        for (int i = 1; i + 1 < face.Vertices.Count; i++)
        {
            sum += Vector3f.Cross(face.Vertices[i].Position - origin, face.Vertices[i + 1].Position - origin);
        }

        // Faces are wound either way in retail meshes; orient each by its own normal attribute.
        return Vector3f.Dot(sum, face.Vertices[0].Normal) < 0f ? sum * -0.5f : sum * 0.5f;
    }

    // An axis-aligned cube of half-size h, two triangles a side, wound and lit outward.
    private static List<FracturePolygon> Cube(float h)
    {
        List<FracturePolygon> faces = [];
        Vector3f[] axes = [Vector3f.UnitX, Vector3f.UnitY, Vector3f.UnitZ];
        foreach (Vector3f n in axes)
        {
            foreach (float sign in new[] { 1f, -1f })
            {
                Vector3f normal = n * sign;
                Vector3f u = MathF.Abs(normal.Z) > 0.5f ? Vector3f.UnitX : Vector3f.UnitZ;
                Vector3f v = Vector3f.Cross(normal, u);
                Vector3f c = normal * h;
                Vertex3D V(float a, float b) => new(c + (u * (a * h)) + (v * (b * h)), normal, a, b);
                faces.Add(new FracturePolygon([V(-1, -1), V(1, -1), V(1, 1)], 0));
                faces.Add(new FracturePolygon([V(-1, -1), V(1, 1), V(-1, 1)], 0));
            }
        }

        return faces;
    }

    // A flat slab in XZ, centred on the origin, as a grid of quads on both faces.
    private static MeshGeometry Slab(float size, float thickness, int cells)
    {
        List<Vertex3D> vertices = [];
        List<ushort> indices = [];
        foreach (float y in new[] { -thickness / 2f, thickness / 2f })
        {
            int first = vertices.Count;
            for (int j = 0; j <= cells; j++)
            {
                for (int i = 0; i <= cells; i++)
                {
                    float x = -size / 2f + (size * i / cells);
                    float z = -size / 2f + (size * j / cells);
                    vertices.Add(new Vertex3D(new Vector3f(x, y, z), new Vector3f(0, MathF.Sign(y), 0), i / (float)cells, j / (float)cells));
                }
            }

            for (int j = 0; j < cells; j++)
            {
                for (int i = 0; i < cells; i++)
                {
                    int a = first + (j * (cells + 1)) + i;
                    indices.AddRange([(ushort)a, (ushort)(a + 1), (ushort)(a + cells + 2), (ushort)a, (ushort)(a + cells + 2), (ushort)(a + cells + 1)]);
                }
            }
        }

        return new MeshGeometry([.. vertices], [.. indices], [new MeshSubset(0, indices.Count, 0, 0)]);
    }
}
