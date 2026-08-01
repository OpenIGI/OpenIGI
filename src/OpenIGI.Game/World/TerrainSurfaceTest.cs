using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>Where a surface test met the ground.</summary>
/// <param name="Height">The surface height, in the leaf's local frame.</param>
/// <param name="NormalX">The struck triangle's unit normal, X.</param>
/// <param name="NormalY">The struck triangle's unit normal, Y.</param>
/// <param name="NormalZ">The struck triangle's unit normal, Z.</param>
/// <param name="Triangle">Which triangle of the leaf answered.</param>
public readonly record struct TerrainSurfaceHit(
    double Height,
    double NormalX,
    double NormalY,
    double NormalZ,
    int Triangle);

/// <summary>
/// The surface test — <c>0x4CA010</c>. Turns a leaf's triangles into a height.
/// </summary>
/// <remarks>
/// <para>
/// This is the end of the chain the whole collision module exists to run: descend
/// (<see cref="TerrainPointDescent"/>), reach a leaf (<see cref="TerrainLeafQuery"/>), and here find
/// where the ground actually is.
/// </para>
/// <para>
/// The test is not a general ray-triangle intersection. It exploits the query being <b>vertical</b>:
/// for each triangle it finds the two points where the horizontal line <c>y = queryY</c> crosses the
/// triangle's edges, checks the query's X lies between them, and interpolates the height along that
/// span. Two rejections come first, on the triangle's X and Y bounds.
/// </para>
/// <para>
/// <b>Divergence, recorded.</b> The original folds a level-of-detail morph into its vertex heights when
/// a global patch is active — a bilinear sample biased by 64 and scaled by 256. That path is not
/// reproduced here, because it only engages while the renderer is blending between detail levels and
/// nothing in the port sets it. A query answered mid-morph would differ by the blend amount.
/// </para>
/// </remarks>
public static class TerrainSurfaceTest
{
    /// <summary>The slack every bounds and span comparison is given.</summary>
    /// <remarks>
    /// <c>0.0099999998</c> — a hundredth of a unit, written as a <see langword="float"/> and widened.
    /// It is applied on both sides of every test, so a query exactly on an edge hits rather than
    /// falling between two triangles.
    /// </remarks>
    public const double Epsilon = 0.0099999998;

    /// <summary>What the running best height starts at when nothing has been hit.</summary>
    /// <remarks>
    /// <c>268435460</c> — just over 2²⁸ — signed by the cast direction, so the first real hit always
    /// beats it.
    /// </remarks>
    public const double NoHitSentinel = 268435460.0;

    /// <summary>How far past the segment's end the test still looks.</summary>
    /// <remarks>A tenth of a unit, added or subtracted according to the cast direction.</remarks>
    public const double EndNudge = 0.1;

    /// <summary>Finds the ground beneath — or above — a point within a leaf.</summary>
    /// <param name="leaf">The leaf the descent reached.</param>
    /// <param name="localX">The query's X in the leaf's frame.</param>
    /// <param name="localY">The query's Y in the leaf's frame.</param>
    /// <param name="limitZ">
    /// How far the query reaches in the leaf's frame — the far end of the segment, already nudged.
    /// </param>
    /// <param name="downward">
    /// <see langword="true"/> for a cast that keeps the <i>lowest</i> surface at or above
    /// <paramref name="limitZ"/>; <see langword="false"/> to keep the highest at or below it.
    /// </param>
    /// <param name="hit">Receives the surface when one is found.</param>
    /// <returns><see langword="false"/> when no triangle of the leaf covers the point.</returns>
    /// <remarks>
    /// The direction decides both the sentinel's sign and which way the comparison runs, which is how
    /// one routine serves the ground query and the ceiling query alike.
    /// </remarks>
    public static bool TryFindSurface(
        in TerrainLeaf leaf,
        double localX,
        double localY,
        double limitZ,
        bool downward,
        out TerrainSurfaceHit hit)
    {
        hit = default;

        TerrainCommandBlock block = leaf.Block;
        int vertexCount = block.VertexCount;
        if (vertexCount == 0 || block.TriangleCount == 0)
        {
            return false;
        }

        // Every vertex, unpacked and oriented once. The orientation acts on X and Y only, and because
        // it is a permutation with sign it can be applied to the integers before the scale.
        double[] xs = new double[vertexCount];
        double[] ys = new double[vertexCount];
        double[] zs = new double[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            TerrainPackedVertex vertex = block.Vertex(i);
            (int ox, int oy) = TerrainOrientation.Transform(leaf.Cursor.Orientation, vertex.X, vertex.Y);

            xs[i] = ox * leaf.UnitScale;
            ys[i] = oy * leaf.UnitScale;
            zs[i] = vertex.Z * leaf.UnitScale;
        }

        // A mirrored leaf (orientation bit 2) reflects the vertices' X, which reverses every triangle's
        // winding — so the raw cross product comes out pointing DOWN. The original corrects this at the
        // end of 0x4CA010 by scaling the whole normal by v105 = -1 when that bit is set. Without it a
        // mirrored leaf reports a downward normal, SlopeDegrees reads it as past vertical, and the human
        // is given a maximum downhill slope-slide on ground that is perfectly walkable.
        double mirrorSign = (leaf.Cursor.Orientation & TerrainOrientation.Mirror) != 0 ? -1.0 : 1.0;

        double best = downward ? NoHitSentinel : -NoHitSentinel;
        bool found = false;

        for (int t = 0; t < block.TriangleCount; t++)
        {
            TerrainTriangle triangle = block.Triangle(t);
            int i0 = triangle.I0;
            int i1 = triangle.I1;
            int i2 = triangle.I2;

            if (i0 >= vertexCount || i1 >= vertexCount || i2 >= vertexCount)
            {
                continue;
            }

            // Reject on X, then on Y, before any arithmetic worth avoiding.
            if (!Straddles(xs[i0], xs[i1], xs[i2], localX) || !Straddles(ys[i0], ys[i1], ys[i2], localY))
            {
                continue;
            }

            if (!TryHeightAt(xs, ys, zs, i0, i1, i2, localX, localY, out double height))
            {
                continue;
            }

            bool better = downward
                ? height <= best && height >= limitZ
                : height >= best && height <= limitZ;

            if (!better)
            {
                continue;
            }

            best = height;
            found = true;

            (double nx, double ny, double nz) = Normal(xs, ys, zs, i0, i1, i2);
            hit = new TerrainSurfaceHit(height, nx * mirrorSign, ny * mirrorSign, nz * mirrorSign, t);
        }

        return found;
    }

    /// <summary>Whether a value lies within three others' range, with slack at both ends.</summary>
    private static bool Straddles(double a, double b, double c, double value)
    {
        double low = Math.Min(a, Math.Min(b, c));
        double high = Math.Max(a, Math.Max(b, c));

        return low - Epsilon <= value && high + Epsilon >= value;
    }

    /// <summary>
    /// The height of a triangle's plane at a point, by crossing the horizontal line through it.
    /// </summary>
    /// <remarks>
    /// The vertices are sorted by Y into low, middle and high. The line <c>y = queryY</c> crosses the
    /// long edge — low to high — at one X, and one of the two short edges at another; the query's X has
    /// to lie between them. The height is then interpolated first along the long edge and then across
    /// the span, which is the same order the original evaluates it in.
    /// </remarks>
    private static bool TryHeightAt(
        double[] xs,
        double[] ys,
        double[] zs,
        int i0,
        int i1,
        int i2,
        double localX,
        double localY,
        out double height)
    {
        height = 0.0;

        // Sort the three by Y.
        int low = i0;
        int mid = i1;
        int high = i2;
        Order(ys, ref low, ref mid);
        Order(ys, ref mid, ref high);
        Order(ys, ref low, ref mid);

        double spanY = ys[high] - ys[low];
        if (spanY == 0.0)
        {
            // A triangle edge-on to the query plane covers no area at this Y.
            return false;
        }

        double alongLong = (localY - ys[low]) / spanY;
        double longX = xs[low] + ((xs[high] - xs[low]) * alongLong);
        double longZ = zs[low] + ((zs[high] - zs[low]) * alongLong);

        double shortX;
        double shortZ;

        if (localY > ys[mid] || ys[low] == ys[mid])
        {
            double denominator = ys[high] - ys[mid];
            if (denominator == 0.0)
            {
                return false;
            }

            double alongUpper = (localY - ys[mid]) / denominator;
            shortX = xs[mid] + ((xs[high] - xs[mid]) * alongUpper);
            shortZ = zs[mid] + ((zs[high] - zs[mid]) * alongUpper);
        }
        else
        {
            double denominator = ys[mid] - ys[low];
            if (denominator == 0.0)
            {
                return false;
            }

            double alongLower = (localY - ys[low]) / denominator;
            shortX = xs[low] + ((xs[mid] - xs[low]) * alongLower);
            shortZ = zs[low] + ((zs[mid] - zs[low]) * alongLower);
        }

        // The query's X must lie between the two crossings, in either order.
        bool inside = (longX - Epsilon <= localX && shortX + Epsilon >= localX)
            || (longX + Epsilon >= localX && shortX - Epsilon <= localX);

        if (!inside)
        {
            return false;
        }

        double spanX = shortX - longX;
        height = spanX == 0.0
            ? longZ
            : longZ + ((localX - longX) / spanX * (shortZ - longZ));

        return true;
    }

    private static void Order(double[] ys, ref int first, ref int second)
    {
        if (ys[first] > ys[second])
        {
            (first, second) = (second, first);
        }
    }

    /// <summary>The triangle's unit normal, from the cross product of two of its edges.</summary>
    private static (double X, double Y, double Z) Normal(
        double[] xs,
        double[] ys,
        double[] zs,
        int i0,
        int i1,
        int i2)
    {
        double ax = xs[i1] - xs[i0];
        double ay = ys[i1] - ys[i0];
        double az = zs[i1] - zs[i0];
        double bx = xs[i2] - xs[i0];
        double by = ys[i2] - ys[i0];
        double bz = zs[i2] - zs[i0];

        // The original normalises only when the cross product is not degenerate, leaving a zero normal
        // as it is rather than dividing.
        double nx = (ay * bz) - (az * by);
        double ny = (az * bx) - (ax * bz);
        double nz = (ax * by) - (ay * bx);

        double lengthSquared = (nx * nx) + (ny * ny) + (nz * nz);
        if (lengthSquared == 0.0)
        {
            return (nx, ny, nz);
        }

        double inverse = 1.0 / Math.Sqrt(lengthSquared);
        return (nx * inverse, ny * inverse, nz * inverse);
    }
}
