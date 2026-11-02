using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>The optional refinement of the nearer chunks onto <see cref="TerrainSmoothing"/>.</summary>
public static partial class TerrainChunkMesh
{
    /// <summary>
    /// Splits every triangle of a chunk into <c>N²</c> and lifts the new points onto its level's smooth
    /// ground.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each refined vertex is a weighted blend of up to three of the chunk's own vertices. Every
    /// attribute the passes carry (position, morph target, texture coordinates, material weights) is
    /// the same blend of what those vertices carry, so before the smooth ground is applied the refined
    /// chunk draws exactly the picture the unrefined one did. A point on an edge is keyed by the edge,
    /// so the two triangles sharing it share the vertex and blend it with the same weights.
    /// </para>
    /// <para>
    /// The smooth ground moves the position, and the coarser level's smooth ground moves the morph
    /// target: the morph becomes the one minus the other. The vertex stage slides a vertex onto its
    /// target as the chunk recedes, so the chunk eases from its own smoothed ground onto its coarser
    /// neighbour's at exactly the rate the level of detail hands over, and meets that neighbour where it
    /// is actually drawn. Nothing in the shader's morph changes. Where the coarser level is drawn as
    /// stored (<paramref name="coarser"/> is <see langword="null"/>), the target is the stored ground.
    /// </para>
    /// </remarks>
    private static Refinement Refine(
        in TerrainChunk chunk,
        TerrainSmoothField smooth,
        TerrainSmoothField? coarser,
        ReadOnlySpan<Vector3d> offsets,
        ReadOnlySpan<Vector3d> morphs,
        ReadOnlySpan<ushort> triangles)
    {
        int n = smooth.Subdivisions;
        int cornerCount = offsets.Length;
        var sources = new List<Source>(cornerCount * 4);
        for (int i = 0; i < cornerCount; i++)
        {
            sources.Add(new Source(i, i, i, 1.0, 0.0, 0.0));
        }

        var edges = new Dictionary<(int, int, int), int>();
        int triangleCount = triangles.Length / 3;
        var refinedTriangles = new ushort[triangleCount * n * n * 3];
        int cursor = 0;
        Span<int> grid = stackalloc int[(TerrainSmoothField.MaximumSubdivisions + 1) * (TerrainSmoothField.MaximumSubdivisions + 1)];

        for (int t = 0; t < triangleCount; t++)
        {
            int a = triangles[t * 3];
            int b = triangles[(t * 3) + 1];
            int c = triangles[(t * 3) + 2];

            // The point i steps from a towards b and j from a towards c.
            for (int j = 0; j <= n; j++)
            {
                for (int i = 0; i + j <= n; i++)
                {
                    int index;
                    if (i == 0 && j == 0)
                    {
                        index = a;
                    }
                    else if (i == n)
                    {
                        index = b;
                    }
                    else if (j == n)
                    {
                        index = c;
                    }
                    else if (j == 0)
                    {
                        index = EdgePoint(a, b, i);
                    }
                    else if (i == 0)
                    {
                        index = EdgePoint(a, c, j);
                    }
                    else if (i + j == n)
                    {
                        index = EdgePoint(b, c, j);
                    }
                    else
                    {
                        index = sources.Count;
                        sources.Add(new Source(a, b, c, (double)(n - i - j) / n, (double)i / n, (double)j / n));
                    }

                    grid[(j * (n + 1)) + i] = index;
                }
            }

            // Both kinds of sub-triangle keep the parent's winding: i runs towards b and j towards c.
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i + j < n; i++)
                {
                    int p00 = grid[(j * (n + 1)) + i];
                    int p10 = grid[(j * (n + 1)) + i + 1];
                    int p01 = grid[((j + 1) * (n + 1)) + i];
                    refinedTriangles[cursor++] = (ushort)p00;
                    refinedTriangles[cursor++] = (ushort)p10;
                    refinedTriangles[cursor++] = (ushort)p01;

                    if (i + j + 1 < n)
                    {
                        int p11 = grid[((j + 1) * (n + 1)) + i + 1];
                        refinedTriangles[cursor++] = (ushort)p10;
                        refinedTriangles[cursor++] = (ushort)p11;
                        refinedTriangles[cursor++] = (ushort)p01;
                    }
                }
            }
        }

        smooth.Focus(chunk.Position.X, chunk.Position.Y, chunk.Position.Z);
        coarser?.Focus(chunk.Position.X, chunk.Position.Y, chunk.Position.Z);

        int count = sources.Count;
        var refinedOffsets = new Vector3d[count];
        var refinedMorphs = new Vector3d[count];
        var normals = new Vector3f[count];
        var normalWeights = new float[count];

        for (int r = 0; r < count; r++)
        {
            Source s = sources[r];
            Vector3d own = Blend(offsets, s);
            Vector3d far = Blend(offsets, s) + Blend(morphs, s);
            if (coarser is not null)
            {
                (double lift, _) = coarser.Displace(
                    chunk.Position.X + far.X,
                    chunk.Position.Y + far.Y,
                    chunk.Position.Z + far.Z,
                    chunk.Position.Z,
                    out _);
                far = new Vector3d(far.X, far.Y, far.Z + lift);
            }

            (double offset, double weight) = smooth.Displace(
                chunk.Position.X + own.X,
                chunk.Position.Y + own.Y,
                chunk.Position.Z + own.Z,
                chunk.Position.Z,
                out (double X, double Y, double Z) normal);

            own = new Vector3d(own.X, own.Y, own.Z + offset);
            refinedOffsets[r] = own;
            refinedMorphs[r] = far - own;
            normals[r] = new Vector3f((float)normal.X, (float)normal.Y, (float)normal.Z);
            normalWeights[r] = (float)weight;
        }

        return new Refinement([.. sources], refinedOffsets, refinedMorphs, normals, normalWeights, refinedTriangles.AsSpan(0, cursor).ToArray());

        // A point on an edge belongs to the edge, not the triangle: keyed from its lower-numbered end
        // and blended from there, so whichever triangle reaches it first, both get bit-identical weights.
        int EdgePoint(int from, int to, int step)
        {
            int lo = Math.Min(from, to);
            int hi = Math.Max(from, to);
            int along = from == lo ? step : n - step;
            if (!edges.TryGetValue((lo, hi, along), out int index))
            {
                index = sources.Count;
                sources.Add(new Source(lo, hi, hi, (double)(n - along) / n, (double)along / n, 0.0));
                edges[(lo, hi, along)] = index;
            }

            return index;
        }
    }

    /// <summary>Records what one of the chunk's own vertices carries in a pass, for the refined vertices to blend.</summary>
    /// <remarks>Six rows of <paramref name="count"/>: U, V, the far U and V, and the two weight coefficients.</remarks>
    private static void StoreCorner(double[] corners, int count, int i, double u, double v, double mu, double mv, double c0, double c1)
    {
        corners[i] = u;
        corners[count + i] = v;
        corners[(2 * count) + i] = mu;
        corners[(3 * count) + i] = mv;
        corners[(4 * count) + i] = c0;
        corners[(5 * count) + i] = c1;
    }

    /// <summary>Writes one pass's refined vertices, each the blend of the corners <see cref="StoreCorner"/> recorded.</summary>
    /// <remarks>
    /// <c>weighted</c> says whether the pass carries a material weight. The light pass does not: it keeps
    /// the plain white colour every unrefined light pass has.
    /// </remarks>
    private static void EmitRefined(
        Vertex3D[] vertices,
        int vertexBase,
        Refinement refined,
        double[] corners,
        int count,
        float scale,
        bool weighted)
    {
        ReadOnlySpan<double> u = corners.AsSpan(0, count);
        ReadOnlySpan<double> v = corners.AsSpan(count, count);
        ReadOnlySpan<double> mu = corners.AsSpan(2 * count, count);
        ReadOnlySpan<double> mv = corners.AsSpan(3 * count, count);
        ReadOnlySpan<double> c0 = corners.AsSpan(4 * count, count);
        ReadOnlySpan<double> c1 = corners.AsSpan(5 * count, count);

        for (int r = 0; r < refined.Sources.Length; r++)
        {
            Source s = refined.Sources[r];
            Vector3d offset = refined.Offsets[r];
            Vector3d morph = refined.Morphs[r];
            double ru = Blend(u, s);
            double rv = Blend(v, s);

            vertices[vertexBase + r] = new Vertex3D(
                new Vector3f((float)(offset.X * scale), (float)(offset.Y * scale), (float)(offset.Z * scale)),
                new Vector3f((float)(morph.X * scale), (float)(morph.Y * scale), (float)(morph.Z * scale)),
                (float)ru,
                (float)rv,
                (float)(Blend(mu, s) - ru),
                (float)(Blend(mv, s) - rv),
                weighted ? PackCoefficients(Blend(c0, s), Blend(c1, s)) : new Bgra32(255, 255, 255, 255));
        }
    }

    private static Vector3d Blend(ReadOnlySpan<Vector3d> values, in Source s) => new(
        (values[s.A].X * s.WA) + (values[s.B].X * s.WB) + (values[s.C].X * s.WC),
        (values[s.A].Y * s.WA) + (values[s.B].Y * s.WB) + (values[s.C].Y * s.WC),
        (values[s.A].Z * s.WA) + (values[s.B].Z * s.WB) + (values[s.C].Z * s.WC));

    private static double Blend(ReadOnlySpan<double> values, in Source s) =>
        (values[s.A] * s.WA) + (values[s.B] * s.WB) + (values[s.C] * s.WC);

    /// <summary>
    /// A material pass's weight at a vertex as two coefficients, <c>c0 + c1 · fade</c>: the alpha
    /// selector's four cases, written so that a blend of two vertices is the blend of their weights.
    /// </summary>
    /// <remarks>The selector itself cannot be blended: halfway between "one" and "nothing" is no selector.</remarks>
    private static (double C0, double C1) WeightCoefficients(byte selector) => selector switch
    {
        255 => (1.0, 0.0),
        170 => (1.0, -1.0),
        85 => (0.0, 1.0),
        _ => (0.0, 0.0),
    };

    /// <summary>Packs blended weight coefficients into a terrain vertex's colour.</summary>
    /// <remarks>
    /// Red holds <c>c0</c>, green the positive part of <c>c1</c> and blue its negative part, and the
    /// alpha is one. A selector vertex is white in all three, and a coefficient vertex never is, since
    /// green and blue are never both set. The vertex stage tells the two apart by that alone.
    /// </remarks>
    private static Bgra32 PackCoefficients(double c0, double c1)
    {
        static byte Encode(double v) => (byte)Math.Clamp((int)Math.Round(v * 255.0), 0, 255);
        return new Bgra32(Encode(Math.Max(0.0, -c1)), Encode(Math.Max(0.0, c1)), Encode(c0), 255);
    }

    /// <summary>One refined vertex: the blend of up to three of the chunk's own.</summary>
    private readonly record struct Source(int A, int B, int C, double WA, double WB, double WC);

    /// <summary>A chunk refined onto the smooth ground.</summary>
    /// <param name="Sources">How each refined vertex blends the chunk's own; the first ones are the chunk's own vertices.</param>
    /// <param name="Offsets">Each refined vertex's position, relative to the chunk centre, in engine units.</param>
    /// <param name="Morphs">Each one's offset to its far position.</param>
    /// <param name="Normals">The smooth ground's normal at each one.</param>
    /// <param name="NormalWeights">How far to trust that normal over the triangles' own.</param>
    /// <param name="Triangles">The refined triangles, with the parent's winding.</param>
    private sealed record Refinement(
        Source[] Sources,
        Vector3d[] Offsets,
        Vector3d[] Morphs,
        Vector3f[] Normals,
        float[] NormalWeights,
        ushort[] Triangles);
}
