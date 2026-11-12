using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World.Fracture;

/// <summary>What a breach leaves of a mesh, and what it blew out of it.</summary>
/// <param name="Kept">The mesh with the hole in it, its subsets as they were.</param>
/// <param name="Removed">The faces that were inside the hole, in the mesh's space, to be broken into its rubble.</param>
public sealed record BreachCut(MeshGeometry Kept, IReadOnlyList<FracturePolygon> Removed);

/// <summary>
/// Blows a ragged hole through a mesh: every face inside a rough ball is taken out and every face crossing its edge cut along
/// it, so a wall keeps everything but the hole and the hole's edge follows the ball's faces.
/// </summary>
/// <remarks>
/// <para>
/// The ball is convex -- a dozen or so planes at jittered distances round a centre, drawn from the blast's seed -- so taking it
/// out of a face is a sequence of splits: whatever is outside the first plane is kept, what is inside goes on to the next,
/// and what is inside all of them is the part removed. The kept mesh keeps its vertices, indices and subsets wherever the
/// ball did not reach, so a building is touched only round the hole and draws as it did everywhere else.
/// </para>
/// <para>Clean-room: plane clipping of a convex region out of a mesh, written from the geometry.</para>
/// </remarks>
public static class BreachCutter
{
    /// <summary>A rough ball of planes: a hole's shape.</summary>
    /// <param name="centre">Its centre.</param>
    /// <param name="radius">Its rough radius.</param>
    /// <param name="random">The stream to draw its roughness from: the blast's own.</param>
    /// <param name="faces">How many planes it has.</param>
    /// <returns>The planes, each with the ball inside it.</returns>
    public static FracturePlane[] RaggedBall(Vector3f centre, float radius, IgiRandom random, int faces = 14)
    {
        ArgumentNullException.ThrowIfNull(random);
        var planes = new FracturePlane[Math.Max(faces, 4)];
        float golden = MathF.PI * (3f - MathF.Sqrt(5f));
        for (int i = 0; i < planes.Length; i++)
        {
            // Spread round the sphere on a spiral, then nudged, so no two holes are cut alike.
            float z = 1f - ((i + 0.5f) * 2f / planes.Length);
            float ring = MathF.Sqrt(MathF.Max(0f, 1f - (z * z)));
            float angle = (i * golden) + (random.NextSingle() * 0.6f);
            Vector3f normal = new Vector3f(MathF.Cos(angle) * ring, MathF.Sin(angle) * ring, z)
                + (new Vector3f(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, random.NextSingle() - 0.5f) * 0.35f);
            normal = normal.Normalized();
            float reach = radius * (0.78f + (random.NextSingle() * 0.4f));
            planes[i] = new FracturePlane(normal, Vector3f.Dot(normal, centre) + reach);
        }

        return planes;
    }

    /// <summary>Cuts a hole through a mesh.</summary>
    /// <param name="mesh">The mesh, in its own space.</param>
    /// <param name="ball">The hole's planes, in the same space.</param>
    /// <returns>The cut, or <see langword="null"/> when the hole reaches none of it or the kept mesh would not fit its indices.</returns>
    public static BreachCut? Cut(MeshGeometry mesh, ReadOnlySpan<FracturePlane> ball)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        List<Vertex3D> vertices = [.. mesh.Vertices];
        List<ushort> indices = new(mesh.Indices.Length);
        List<MeshSubset> subsets = new(mesh.Subsets.Length);
        List<FracturePolygon> removed = [];
        bool touched = false;

        for (int s = 0; s < mesh.Subsets.Length; s++)
        {
            MeshSubset subset = mesh.Subsets[s];
            int start = indices.Count;
            int skinsFrom = subset.IndexOffset + subset.IndexCount - subset.SkinIndexCount;
            int skinStart = -1;
            for (int i = subset.IndexOffset; i + 2 < subset.IndexOffset + subset.IndexCount; i += 3)
            {
                if (i >= skinsFrom && skinStart < 0)
                {
                    skinStart = indices.Count;
                }

                ushort a = mesh.Indices[i];
                ushort b = mesh.Indices[i + 1];
                ushort c = mesh.Indices[i + 2];
                if (Clear(mesh.Vertices[a].Position, mesh.Vertices[b].Position, mesh.Vertices[c].Position, ball))
                {
                    indices.Add(a);
                    indices.Add(b);
                    indices.Add(c);
                    continue;
                }

                touched = true;
                FracturePolygon? rest = new([mesh.Vertices[a], mesh.Vertices[b], mesh.Vertices[c]], s);
                foreach (FracturePlane plane in ball)
                {
                    rest.Split(plane, out FracturePolygon? inside, out FracturePolygon? outside);
                    if (outside is not null && !Append(outside, vertices, indices))
                    {
                        return null;
                    }

                    rest = inside;
                    if (rest is null)
                    {
                        break;
                    }
                }

                if (rest is not null)
                {
                    removed.Add(rest);
                }
            }

            int count = indices.Count - start;
            int skins = skinStart < 0 ? 0 : indices.Count - skinStart;
            subsets.Add(subset with { IndexOffset = start, IndexCount = count, SkinIndexCount = skins });
        }

        if (!touched || removed.Count == 0)
        {
            return null;
        }

        return new BreachCut(new MeshGeometry([.. vertices], [.. indices], [.. subsets]), removed);
    }

    // Whether a face lies wholly outside one of the ball's planes, and so is untouched by it.
    private static bool Clear(Vector3f a, Vector3f b, Vector3f c, ReadOnlySpan<FracturePlane> ball)
    {
        foreach (FracturePlane plane in ball)
        {
            if (plane.Distance(a) > 0f && plane.Distance(b) > 0f && plane.Distance(c) > 0f)
            {
                return true;
            }
        }

        return false;
    }

    // A kept piece fanned into the mesh as new vertices; false once the mesh's sixteen-bit indices are spent.
    private static bool Append(FracturePolygon polygon, List<Vertex3D> vertices, List<ushort> indices)
    {
        if (vertices.Count + polygon.Vertices.Count > ushort.MaxValue)
        {
            return false;
        }

        int first = vertices.Count;
        vertices.AddRange(polygon.Vertices);
        for (int i = 1; i + 1 < polygon.Vertices.Count; i++)
        {
            indices.Add((ushort)first);
            indices.Add((ushort)(first + i));
            indices.Add((ushort)(first + i + 1));
        }

        return true;
    }
}
