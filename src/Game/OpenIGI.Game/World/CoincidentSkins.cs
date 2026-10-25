using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// Finds a model's coincident skins: triangles lying in the same plane as another of the model's, overlapping it and facing the
/// other way -- the two faces of a sheet modelled with no thickness, an awning's top and its underside.
/// </summary>
/// <remarks>
/// <para>
/// The original culls back faces, so of such a pair it only ever draws the face turned to the camera. A mesh drawn two-sided --
/// a sector, which encloses the viewer and so is never culled whole -- draws both at the same depth, and which wins a pixel then
/// turns on rounding: the sheet flickers between its two faces as the camera moves, and under a jittered projection every frame
/// while it stands still. Drawn culled, the skins of a pair are exactly one face from any side, as the original's are.
/// </para>
/// <para>
/// Each subset's skins are moved to the end of its index range and counted (<see cref="MeshSubset.SkinIndexCount"/>), so a
/// renderer drawing the subset two-sided can draw that tail culled. The rest of the subset keeps its order.
/// </para>
/// </remarks>
public static class CoincidentSkins
{
    // Two faces are one plane apart when their normals are this close to opposite.
    private const float OppositeDot = -0.999f;

    /// <summary>Reorders each subset's indices so its coincident skins come last, and counts them.</summary>
    /// <param name="vertices">The mesh's vertices, wound so each triangle faces its vertex normals.</param>
    /// <param name="indices">The mesh's triangle-list indices, reordered in place within each subset.</param>
    /// <param name="subsets">The mesh's subsets.</param>
    /// <param name="planeTolerance">How far apart two planes may be and count as one, in the vertices' units.</param>
    /// <returns>The subsets, each with its skins counted.</returns>
    public static MeshSubset[] Partition(Vertex3D[] vertices, ushort[] indices, IReadOnlyList<MeshSubset> subsets, float planeTolerance)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(subsets);
        bool[] skin = Find(vertices, indices, planeTolerance);
        MeshSubset[] result = new MeshSubset[subsets.Count];
        for (int s = 0; s < subsets.Count; s++)
        {
            MeshSubset subset = subsets[s];
            int first = subset.IndexOffset / 3;
            int count = subset.IndexCount / 3;
            List<int> open = new(count);
            List<int> culled = [];
            for (int t = first; t < first + count; t++)
            {
                (skin[t] ? culled : open).Add(t);
            }

            if (culled.Count > 0)
            {
                ushort[] reordered = new ushort[count * 3];
                int at = 0;
                foreach (int t in open.Concat(culled))
                {
                    reordered[at++] = indices[t * 3];
                    reordered[at++] = indices[(t * 3) + 1];
                    reordered[at++] = indices[(t * 3) + 2];
                }

                Array.Copy(reordered, 0, indices, subset.IndexOffset, reordered.Length);
            }

            result[s] = subset with { SkinIndexCount = culled.Count * 3 };
        }

        return result;
    }

    /// <summary>Marks every triangle that is one face of a coincident pair.</summary>
    /// <param name="vertices">The mesh's vertices.</param>
    /// <param name="indices">The mesh's triangle-list indices.</param>
    /// <param name="planeTolerance">How far apart two planes may be and count as one.</param>
    /// <returns>A flag per triangle.</returns>
    public static bool[] Find(Vertex3D[] vertices, ushort[] indices, float planeTolerance)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        int triangleCount = indices.Length / 3;
        bool[] skin = new bool[triangleCount];
        Plane[] planes = new Plane[triangleCount];
        Dictionary<long, List<int>> byPlane = [];
        float tolerance = MathF.Max(planeTolerance, 1e-7f);
        for (int t = 0; t < triangleCount; t++)
        {
            Vector3f a = vertices[indices[t * 3]].Position;
            Vector3f b = vertices[indices[(t * 3) + 1]].Position;
            Vector3f c = vertices[indices[(t * 3) + 2]].Position;
            Vector3f n = Vector3f.Cross(b - a, c - a);
            float length = n.Length;
            if (!(length > 1e-12f))
            {
                planes[t] = new Plane(default, 0f, 0f, false);
                continue;
            }

            n /= length;

            // The same plane from either side lands in one bucket: the normal turned so its largest part is positive.
            float sign = Dominant(n) < 0f ? -1f : 1f;
            float offset = Vector3f.Dot(n, a) * sign;
            planes[t] = new Plane(n, Vector3f.Dot(n, a), offset, true);
            long key = (long)MathF.Round(offset / tolerance);
            if (!byPlane.TryGetValue(key, out List<int>? bucket))
            {
                byPlane[key] = bucket = [];
            }

            bucket.Add(t);
        }

        for (int t = 0; t < triangleCount; t++)
        {
            if (!planes[t].Valid)
            {
                continue;
            }

            long key = (long)MathF.Round(planes[t].Canonical / tolerance);
            for (long k = key - 1; k <= key + 1; k++)
            {
                if (!byPlane.TryGetValue(k, out List<int>? bucket))
                {
                    continue;
                }

                foreach (int u in bucket)
                {
                    if (u <= t || (skin[t] && skin[u]))
                    {
                        continue;
                    }

                    Plane p = planes[t], q = planes[u];
                    if (Vector3f.Dot(p.Normal, q.Normal) > OppositeDot || MathF.Abs(p.Offset + q.Offset) > tolerance)
                    {
                        continue;
                    }

                    if (Overlap(vertices, indices, t, u, p.Normal))
                    {
                        skin[t] = true;
                        skin[u] = true;
                    }
                }
            }
        }

        return skin;
    }

    private static float Dominant(Vector3f n)
    {
        float ax = MathF.Abs(n.X), ay = MathF.Abs(n.Y), az = MathF.Abs(n.Z);
        return ax >= ay && ax >= az ? n.X : ay >= az ? n.Y : n.Z;
    }

    // Whether two triangles in one plane share any area: a corner or the middle of one inside the other, or an edge of each
    // crossing; judged in the plane's two widest axes.
    private static bool Overlap(Vertex3D[] vertices, ushort[] indices, int t, int u, Vector3f normal)
    {
        Span<(float X, float Y)> a = stackalloc (float, float)[3];
        Span<(float X, float Y)> b = stackalloc (float, float)[3];
        float ax = MathF.Abs(normal.X), ay = MathF.Abs(normal.Y), az = MathF.Abs(normal.Z);
        for (int k = 0; k < 3; k++)
        {
            a[k] = Flatten(vertices[indices[(t * 3) + k]].Position, ax, ay, az);
            b[k] = Flatten(vertices[indices[(u * 3) + k]].Position, ax, ay, az);
        }

        if (Inside(Middle(a), b) || Inside(Middle(b), a))
        {
            return true;
        }

        for (int k = 0; k < 3; k++)
        {
            if (Inside(a[k], b) || Inside(b[k], a))
            {
                return true;
            }

            for (int m = 0; m < 3; m++)
            {
                if (SegmentsCross(a[k], a[(k + 1) % 3], b[m], b[(m + 1) % 3]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static (float X, float Y) Flatten(Vector3f p, float ax, float ay, float az) =>
        az >= ax && az >= ay ? (p.X, p.Y) : ay >= ax ? (p.X, p.Z) : (p.Y, p.Z);

    private static (float X, float Y) Middle(ReadOnlySpan<(float X, float Y)> t) =>
        ((t[0].X + t[1].X + t[2].X) / 3f, (t[0].Y + t[1].Y + t[2].Y) / 3f);

    // Strictly inside: a point on an edge or a corner is not, so two triangles meeting along an edge are not an overlap.
    private static bool Inside((float X, float Y) p, ReadOnlySpan<(float X, float Y)> t)
    {
        float d0 = Side(t[0], t[1], p), d1 = Side(t[1], t[2], p), d2 = Side(t[2], t[0], p);
        float scale = MathF.Abs(Side(t[0], t[1], t[2])) * 1e-4f;
        return (d0 > scale && d1 > scale && d2 > scale) || (d0 < -scale && d1 < -scale && d2 < -scale);
    }

    // Whether two segments cross at a point inside both.
    private static bool SegmentsCross((float X, float Y) a, (float X, float Y) b, (float X, float Y) c, (float X, float Y) d)
    {
        float d1 = Side(c, d, a), d2 = Side(c, d, b), d3 = Side(a, b, c), d4 = Side(a, b, d);
        return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
    }

    private static float Side((float X, float Y) a, (float X, float Y) b, (float X, float Y) p) =>
        ((b.X - a.X) * (p.Y - a.Y)) - ((b.Y - a.Y) * (p.X - a.X));

    // A triangle's plane: its unit normal and offset, the offset as seen from the plane's canonical side, and whether it has one.
    private readonly record struct Plane(Vector3f Normal, float Offset, float Canonical, bool Valid);
}
