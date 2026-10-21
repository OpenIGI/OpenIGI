using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// The terrain half of the original's batch segment query, <c>RayCast</c> (<c>0x4CC2A0</c>), with its exact
/// arithmetic: the answer a car's wheel rays get, bit for bit.
/// </summary>
/// <remarks>
/// <para>
/// The original's terrain ray cast, step for step: one box over the whole batch fixes how deep
/// the first ray's start is descended (<c>0x4C9A30</c>); from there each ray walks the octree depth first,
/// nearest child first, admitting children by an integer box with ±16385 of vertical slack and a slab test
/// (<c>0x4CC810</c>); the <b>first</b> leaf with a hit ends the walk; the leaf's triangles are tested with a
/// float plane hit and a projected barycentric test (<c>0x4CD300</c>); a <c>DiscardTerrain</c> box around the
/// hit point drops it (<c>0x52BF00</c>). Every operation is an <see cref="X87"/> one.
/// </para>
/// <para>
/// This is deliberately separate from <see cref="TerrainRayTrace"/>, which serves gameplay rays: the two
/// disagree on which hit wins when leaves overlap, and routing bullets through this one is a separate decision.
/// On the levels that ship a <c>terrain.hmp</c>, a leaf inside a <c>HeightMap</c> task's cube has its vertices
/// raised by that grid first, which is how the descent's enter hooks leave it.
/// </para>
/// </remarks>
public sealed class RetailTerrainRay
{
    /// <summary>The depth at which octree nodes are leaves with triangles.</summary>
    public const int LeafDepth = 16;

    private const int MaxStack = 8 * (LeafDepth + 2);

    private readonly TerrainOctree _tree;
    private readonly TerrainCommands _commands;
    private readonly IReadOnlyList<TerrainDiscardRegion> _discards;
    private readonly TerrainHeightBindings? _heights;
    private readonly Entry[] _stack = new Entry[MaxStack];
    private readonly bool[] _firstVisit = new bool[MaxStack];
    private readonly int[] _order = new int[8];
    private double[] _vx = new double[64];
    private double[] _vy = new double[64];
    private double[] _vz = new double[64];

    /// <summary>Initializes a new instance of the <see cref="RetailTerrainRay"/> class.</summary>
    /// <param name="tree">The terrain octree (<c>terrain.ctr</c>).</param>
    /// <param name="commands">The triangle blocks (<c>terrain.cmd</c>).</param>
    /// <param name="discards">The level's <c>DiscardTerrain</c> boxes.</param>
    /// <param name="heights">The level's heightmap patches, or <see langword="null"/> for none.</param>
    public RetailTerrainRay(
        TerrainOctree tree,
        TerrainCommands commands,
        IReadOnlyList<TerrainDiscardRegion> discards,
        TerrainHeightBindings? heights = null)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _discards = discards ?? throw new ArgumentNullException(nameof(discards));
        _heights = heights is { IsEmpty: false } ? heights : null;
    }

    /// <summary>Casts a batch of segments against the terrain, as <c>0x4CC2A0</c> with terrain lists.</summary>
    /// <param name="starts">The segment starts.</param>
    /// <param name="ends">The segment ends.</param>
    /// <param name="results">
    /// One result per segment. A result that already holds a hit (from objects) keeps it unless the terrain's
    /// is strictly nearer; otherwise it must be cleared by the caller, with <see cref="RetailRayResult.Point"/>
    /// at the segment's end.
    /// </param>
    public void Cast(ReadOnlySpan<Vector3d> starts, ReadOnlySpan<Vector3d> ends, Span<RetailRayResult> results)
    {
        int n = starts.Length;
        if (n == 0)
        {
            return;
        }

        // One box over every start and end, both corners starting at the first start.
        double maxX = starts[0].X, maxY = starts[0].Y, maxZ = starts[0].Z;
        double minX = maxX, minY = maxY, minZ = maxZ;
        for (int i = 0; i < n; i++)
        {
            Grow(starts[i], ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
            Grow(ends[i], ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
        }

        maxX = X87.Add(maxX, 2.0);
        maxY = X87.Add(maxY, 2.0);
        maxZ = X87.Add(maxZ, 16387.0);
        minX = X87.Add(minX, -2.0);
        minY = X87.Add(minY, -2.0);
        minZ = X87.Add(minZ, -16387.0);

        // The depth the batch box shares, truncating conversions.
        int spread = ((Trunc(maxZ) ^ Trunc(minZ)) | (Trunc(maxY) ^ Trunc(minY))) | (Trunc(maxX) ^ Trunc(minX));
        int depth = 30 - (31 - int.LeadingZeroCount(spread));
        depth = Math.Min(depth, LeafDepth);

        // Only the first ray's start is descended, floor-converted.
        TerrainDescentCursor cursor = TerrainPointDescent.Descend(
            _tree,
            new Vector3i(Floor(starts[0].X), Floor(starts[0].Y), Floor(starts[0].Z)),
            depth);

        for (int i = 0; i < n; i++)
        {
            if (!Walk(cursor, starts[i], ends[i], out Vector3d hit, out float nx, out float ny, out float nz, out bool front))
            {
                continue;
            }

            ref RetailRayResult result = ref results[i];
            if (result.Hit && !(Dist2(starts[i], result.Point) > Dist2(starts[i], hit)))
            {
                continue;
            }

            if (Vetoed(hit))
            {
                continue;
            }

            result.Point = hit;
            result.NormalX = nx;
            result.NormalY = ny;
            result.NormalZ = nz;
            result.HasFrontMaterial = front;
            result.HasSurface = !front;
            result.SurfaceForce = 0f;
            result.Distance = Dist(hit, starts[i]);
            result.Hit = true;
            result.Struck = null;
            result.Material = 1;
        }
    }

    private static void Grow(
        Vector3d p,
        ref double minX,
        ref double minY,
        ref double minZ,
        ref double maxX,
        ref double maxY,
        ref double maxZ)
    {
        if (p.X > maxX)
        {
            maxX = p.X;
        }
        else if (p.X < minX)
        {
            minX = p.X;
        }

        if (p.Y > maxY)
        {
            maxY = p.Y;
        }
        else if (p.Y < minY)
        {
            minY = p.Y;
        }

        if (maxZ < p.Z)
        {
            maxZ = p.Z;
        }
        else if (p.Z < minZ)
        {
            minZ = p.Z;
        }
    }

    /// <summary><c>fistp</c> under RC = chop.</summary>
    private static int Trunc(double value) => IgiMath.TruncToInt32(value);

    /// <summary>The descent's conversion: truncate, then step down for a negative non-integer.</summary>
    private static int Floor(double value)
    {
        int i = IgiMath.TruncToInt32(value);
        return value < 0.0 && i != value ? i - 1 : i;
    }

    /// <summary><c>0x4B32F0</c>: <c>((dz·dz) + (dy·dy)) + dx·dx</c>, <c>d = a − b</c>.</summary>
    private static double Dist2(Vector3d a, Vector3d b)
    {
        X87 dx = (X87)a.X - b.X, dy = (X87)a.Y - b.Y, dz = (X87)a.Z - b.Z;
        return (dz * dz + dy * dy + dx * dx).Value;
    }

    /// <summary><c>0x4B32B0</c>: the square root of <see cref="Dist2"/>.</summary>
    private static double Dist(Vector3d a, Vector3d b) => X87.SquareRoot(Dist2(a, b));

    /// <summary><c>DiscardTerrain</c>'s op 11 (<c>0x52BF00</c>): the half-open box <c>(min, max]</c> on each axis.</summary>
    private bool Vetoed(Vector3d p)
    {
        foreach (TerrainDiscardRegion region in _discards)
        {
            if ((p.X > region.MinX) != (p.X > region.MaxX)
                && (p.Y > region.MinY) != (p.Y > region.MaxY)
                && (p.Z > region.MinZ) != (p.Z > region.MaxZ))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>Terrain_RayDFS</c> -- <c>0x4CC810</c>.</summary>
    private bool Walk(
        in TerrainDescentCursor cursor,
        Vector3d s,
        Vector3d e,
        out Vector3d hit,
        out float nx,
        out float ny,
        out float nz,
        out bool front)
    {
        hit = default;
        nx = ny = nz = 0f;
        front = false;

        int rx0 = Trunc(Math.Min(s.X, e.X)), rx1 = Trunc(Math.Max(s.X, e.X));
        int ry0 = Trunc(Math.Min(s.Y, e.Y)), ry1 = Trunc(Math.Max(s.Y, e.Y));
        int rz0 = Trunc(Math.Min(s.Z, e.Z)), rz1 = Trunc(Math.Max(s.Z, e.Z));
        float[] d = [((X87)e.X - s.X).F, ((X87)e.Y - s.Y).F, ((X87)e.Z - s.Z).F];

        // Rank the axes, then order the octants farthest first along the ray.
        float ax = MathF.Abs(d[0]), ay = MathF.Abs(d[1]), az = MathF.Abs(d[2]);
        int major, second, third;
        if (ax > ay && ax > az)
        {
            major = 0;
            (second, third) = ay > az ? (1, 2) : (2, 1);
        }
        else if (ay > az)
        {
            major = 1;
            (second, third) = ax > az ? (0, 2) : (2, 0);
        }
        else
        {
            major = 2;
            (second, third) = ax > ay ? (0, 1) : (1, 0);
        }

        int dirMajor = d[major] > 0.0f ? 1 : 0;
        int dirSecond = d[second] > 0.0f ? 1 : 0;
        int dirThird = d[third] > 0.0f ? 1 : 0;
        for (int i = 0; i < 8; i++)
        {
            _order[i] = ((dirMajor ^ (i & 1)) << major)
                | ((dirSecond ^ ((i >> 1) & 1)) << second)
                | ((dirThird ^ ((i >> 2) & 1)) << third);
        }

        // The explicit stack, children popped nearest first.
        int sp = 0;
        _stack[0] = new Entry(cursor.Extent, cursor.Shift, cursor.Orientation, cursor.Centre.X, cursor.Centre.Y, cursor.Centre.Z, cursor.NodeIndex);
        _firstVisit[0] = true;
        while (sp >= 0)
        {
            Entry entry = _stack[sp];
            if (30 - entry.Shift >= LeafDepth)
            {
                if (LeafTest(entry, s, e, d, out hit, out nx, out ny, out nz, out front))
                {
                    return true;
                }

                sp--;
                continue;
            }

            if (!_firstVisit[sp])
            {
                sp--;
                continue;
            }

            _firstVisit[sp] = false;
            TerrainNode node = _tree[entry.Node];
            int h = (int)((uint)entry.Extent >> 1);
            for (int i = 0; i < 8; i++)
            {
                int oct = _order[i];
                int slot = TerrainOrientation.Slots[(8 * entry.Orientation) + oct];
                if ((node.ChildMask & (1 << slot)) == 0)
                {
                    continue;
                }

                long cx = entry.Cx + ((long)h * ((2 * (oct & 1)) - 1));
                long cy = entry.Cy + ((long)h * ((oct & 2) - 1));
                long cz = entry.Cz + ((long)h * (((oct >> 1) & 2) - 1));
                if (rx0 > cx + h || rx1 < cx - h || ry0 > cy + h || ry1 < cy - h
                    || rz0 > cz + h + 16385 || rz1 < cz - h - 16385)
                {
                    continue;
                }

                if (!SlabTest(cx, cy, cz, h, s, e, d, major, second, third))
                {
                    continue;
                }

                sp++;
                _firstVisit[sp] = true;
                _stack[sp] = new Entry(
                    h,
                    entry.Shift - 1,
                    TerrainOrientation.Compose(entry.Orientation, node.Orientation(slot)),
                    (int)cx,
                    (int)cy,
                    (int)cz,
                    node.Child(slot));
            }
        }

        return false;
    }

    /// <summary>The child's box against the infinite line, one face per axis in rank order.</summary>
    private static bool SlabTest(
        long cx,
        long cy,
        long cz,
        int h,
        Vector3d s,
        Vector3d e,
        float[] d,
        int major,
        int second,
        int third)
    {
        float hf = h;
        float hz = ((X87)hf + 16385.0f).F;
        double[] c = [cx, cy, cz];
        double[] lo = [((X87)c[0] - h).Value, ((X87)c[1] - h).Value, ((X87)c[2] - hz).Value];
        double[] hi = [((X87)h + c[0]).Value, ((X87)h + c[1]).Value, ((X87)hz + c[2]).Value];
        double[] f = [hf, hf, (double)((long)h + 16385)];
        double[] sa = [s.X, s.Y, s.Z];
        double[] ea = [e.X, e.Y, e.Z];

        foreach (int a in (ReadOnlySpan<int>)[major, second, third])
        {
            double face = ((X87)c[a] + (d[a] < 0.0f ? -f[a] : f[a])).Value;
            X87 t = ((X87)face - sa[a]) / ((X87)ea[a] - sa[a]);
            int u = (a + 1) % 3, v = (a + 2) % 3;
            double pu = (((X87)ea[u] - sa[u]) * t + sa[u]).Value;
            double pv = (((X87)ea[v] - sa[v]) * t + sa[v]).Value;
            if (lo[u] <= pu && pu <= hi[u] && lo[v] <= pv && pv <= hi[v])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>Leaf_RayTest</c> -- <c>0x4CD300</c>: the nearest triangle hit in one leaf.</summary>
    private bool LeafTest(
        in Entry entry,
        Vector3d s,
        Vector3d e,
        float[] d,
        out Vector3d hit,
        out float nx,
        out float ny,
        out float nz,
        out bool front)
    {
        hit = default;
        nx = ny = nz = 0f;
        front = false;
        TerrainNode node = _tree[entry.Node];
        if (!_commands.TryGetBlock(node.CommandOffset, out TerrainCommandBlock block) || block.TriangleCount <= 0)
        {
            return false;
        }

        int orient = entry.Orientation;
        int level = 30 - entry.Shift;
        float sign = (orient & 4) != 0 ? -1.0f : 1.0f;
        double scale = ((X87)(double)(0x40000000 >> level) * 0.041666666666666664).Value;

        int vertices = block.VertexCount;
        if (_vx.Length < vertices)
        {
            _vx = new double[vertices];
            _vy = new double[vertices];
            _vz = new double[vertices];
        }

        double ox = entry.Cx, oy = entry.Cy, oz = entry.Cz;
        TerrainHeightBinding patch = _heights?.Resolve(new Vector3d(ox, oy, oz)) ?? default;
        for (int j = 0; j < vertices; j++)
        {
            TerrainPackedVertex packed = block.Vertex(j);
            double x = ((X87)(double)packed.X * scale).Value;
            double y = ((X87)(double)packed.Y * scale).Value;
            double z = ((X87)(double)packed.Z * scale).Value;
            if ((orient & 4) != 0)
            {
                x = -x;
            }

            if ((orient & 1) != 0)
            {
                (x, y) = (orient & 2) != 0 ? (y, -x) : (-y, x);
            }
            else if ((orient & 2) != 0)
            {
                (x, y) = (-x, -y);
            }

            _vx[j] = X87.Add(x, ox);
            _vy[j] = X87.Add(y, oy);
            _vz[j] = X87.Add(z, oz);
            if (patch.IsBound)
            {
                _vz[j] = Patch(patch, _vx[j], _vy[j], _vz[j]);
            }
        }

        Vector3d best = e;
        float bnx = 0f, bny = 0f, bnz = 0f;
        bool found = false;
        float adx = MathF.Abs(((X87)s.X - e.X).F), ady = MathF.Abs(((X87)s.Y - e.Y).F), adz = MathF.Abs(((X87)s.Z - e.Z).F);
        int axis = adx > ady && adx > adz ? 0 : ady > adz ? 1 : 2;

        for (int k = 0; k < block.TriangleCount; k++)
        {
            TerrainTriangle tri = block.Triangle(k);
            int i0 = tri.I0, i1 = tri.I1, i2 = tri.I2;
            if (i0 >= vertices || i1 >= vertices || i2 >= vertices)
            {
                continue;
            }

            double e1x = X87.Add(_vx[i1], -_vx[i0]), e1y = X87.Add(_vy[i1], -_vy[i0]), e1z = X87.Add(_vz[i1], -_vz[i0]);
            double e2x = X87.Add(_vx[i2], -_vx[i0]), e2y = X87.Add(_vy[i2], -_vy[i0]), e2z = X87.Add(_vz[i2], -_vz[i0]);

            // Cross (0x4B3190) and Normalize (0x4B3100).
            double cnx = ((X87)e2z * e1y - (X87)e1z * e2y).Value;
            double cny = (-((X87)e2z * e1x - (X87)e1z * e2x)).Value;
            double cnz = ((X87)e1x * e2y - (X87)e2x * e1y).Value;
            X87 len = X87.Sqrt((X87)cnx * cnx + (X87)cny * cny + (X87)cnz * cnz);
            X87 inv = (X87)1.0 / len;
            float nfx = (inv * cnx).F, nfy = (inv * cny).F, nfz = (inv * cnz).F;
            float e1fx = X87.ToFloat(e1x), e1fy = X87.ToFloat(e1y), e1fz = X87.ToFloat(e1z);
            float e2fx = X87.ToFloat(e2x), e2fy = X87.ToFloat(e2y), e2fz = X87.ToFloat(e2z);
            float wx = ((X87)s.X - _vx[i0]).F, wy = ((X87)s.Y - _vy[i0]).F, wz = ((X87)s.Z - _vz[i0]).F;

            // PlaneHit (0x4B2C70).
            X87 den = (X87)d[1] * nfy + (X87)d[0] * nfx + (X87)nfz * d[2];
            if (!(den.Value > 1e-5f || den.Value < -1e-5f))
            {
                continue;
            }

            X87 num = (X87)wz * nfz + (X87)wx * nfx + (X87)wy * nfy;
            X87 t = num / den;
            float px = ((X87)wx - t * d[0]).F;
            float py = ((X87)wy - t * d[1]).F;
            float pz = ((X87)wz - t * d[2]).F;

            if (!InTriangle(px, py, pz, e1fx, e1fy, e1fz, e2fx, e2fy, e2fz, nfx, nfy, nfz))
            {
                continue;
            }

            Vector3d candidate = new(
                ((X87)(double)px + _vx[i0]).Value,
                ((X87)(double)py + _vy[i0]).Value,
                ((X87)(double)pz + _vz[i0]).Value);
            double ca = axis == 0 ? candidate.X : axis == 1 ? candidate.Y : candidate.Z;
            double sa = axis == 0 ? s.X : axis == 1 ? s.Y : s.Z;
            double ea = axis == 0 ? e.X : axis == 1 ? e.Y : e.Z;
            if (!(Math.Min(sa, ea) <= ca && ca <= Math.Max(sa, ea)))
            {
                continue;
            }

            if (!(Dist2(s, best) > Dist2(s, candidate)))
            {
                continue;
            }

            best = candidate;
            bnx = nfx;
            bny = nfy;
            bnz = nfz;
            found = true;
        }

        if (!found)
        {
            return false;
        }

        nx = ((X87)bnx * sign).F;
        ny = ((X87)bny * sign).F;
        nz = ((X87)bnz * sign).F;
        X87 dd = (X87)nz * ((X87)e.Z - s.Z) + (X87)ny * ((X87)e.Y - s.Y) + (X87)nx * ((X87)e.X - s.X);
        front = dd.Value < 0.0f;
        hit = best;
        return true;
    }

    /// <summary>The heightmap patch on one vertex: the grid's bilinear sample, less its bias, times 256.</summary>
    /// <remarks>
    /// The origin passes through a float and the index conversions truncate. Retail reads the byte grid without
    /// a bounds check; a vertex outside it (packed vertices reach past their leaf) reads the nearest edge here.
    /// </remarks>
    private static double Patch(in TerrainHeightBinding patch, double x, double y, double z)
    {
        float oxf = X87.ToFloat(patch.OriginX);
        float oyf = X87.ToFloat(patch.OriginY);
        float scale = X87.ToFloat(patch.Scale);
        X87 u = ((X87)x - oxf) * scale;
        float vf = (((X87)y - oyf) * scale).F;
        int iu = Trunc(u.Value);
        int iv = Trunc(vf);
        X87 fu = u - iu;
        X87 fv = (X87)vf - iv;
        TerrainHeightGrid grid = patch.Grid;
        double h00 = Corner(grid, iu, iv), h10 = Corner(grid, iu + 1, iv);
        double h01 = Corner(grid, iu, iv + 1), h11 = Corner(grid, iu + 1, iv + 1);
        X87 a = (X87)h00 + (X87)(h10 - h00) * fu;
        X87 b = fu * (h11 - h01) + h01;
        return (((b - a) * fv + a - 64.0) * 256.0f + z).Value;
    }

    private static int Corner(in TerrainHeightGrid grid, int x, int y) =>
        grid.Corner(Math.Clamp(x, 0, grid.Size), Math.Clamp(y, 0, grid.Size));

    /// <summary><c>InTriangle</c> -- <c>0x4B2D90</c>: the projected barycentric test, no early return.</summary>
    private static bool InTriangle(
        float px,
        float py,
        float pz,
        float e1x,
        float e1y,
        float e1z,
        float e2x,
        float e2y,
        float e2z,
        float nx,
        float ny,
        float nz)
    {
        bool ok = true;
        float a = MathF.Abs(nx), b = MathF.Abs(ny), c = MathF.Abs(nz);
        X87 s1, s2, det;
        if (b > a && b > c)
        {
            det = (X87)e2z * e1x - (X87)e2x * e1z;
            ok &= det.Value != 0;
            s2 = ((X87)e1x * pz - (X87)px * e1z) / det;
            if (MathF.Abs(e1x) > MathF.Abs(e1z))
            {
                s1 = ((X87)px - s2 * e2x) / e1x;
            }
            else
            {
                ok &= e1z != 0;
                s1 = ((X87)pz - s2 * e2z) / e1z;
            }
        }
        else if (c > a)
        {
            det = (X87)e1x * e2y - (X87)e2x * e1y;
            ok &= det.Value != 0;
            s2 = ((X87)e1x * py - (X87)px * e1y) / det;
            if (MathF.Abs(e1x) > MathF.Abs(e1y))
            {
                s1 = ((X87)px - s2 * e2x) / e1x;
            }
            else
            {
                ok &= e1y != 0;
                s1 = ((X87)py - s2 * e2y) / e1y;
            }
        }
        else
        {
            det = (X87)e2z * e1y - (X87)e1z * e2y;
            ok &= det.Value != 0;
            s2 = ((X87)pz * e1y - (X87)py * e1z) / det;
            if (MathF.Abs(e1y) > MathF.Abs(e1z))
            {
                s1 = ((X87)py - s2 * e2y) / e1y;
            }
            else
            {
                ok &= e1z != 0;
                s1 = ((X87)pz - s2 * e2z) / e1z;
            }
        }

        if (s2.Value < -1e-5f || s2.Value > 1.00001f)
        {
            ok = false;
        }

        if (s1.Value < -1e-5f || (s1 + s2).Value > 1.00001f)
        {
            ok = false;
        }

        return ok;
    }

    private readonly record struct Entry(int Extent, int Shift, int Orientation, int Cx, int Cy, int Cz, int Node);
}
