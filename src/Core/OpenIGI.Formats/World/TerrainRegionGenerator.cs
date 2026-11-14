using System.Buffers.Binary;

namespace OpenIGI.Formats.World;

/// <summary>A node made by <see cref="TerrainRegionGenerator"/>: its block and its children by world octant.</summary>
public sealed class TerrainGeneratedNode
{
    /// <summary>Gets the node's block, header first.</summary>
    public required byte[] Block { get; init; }

    /// <summary>Gets the children, indexed by world octant (bit 0 +X, bit 1 +Y, bit 2 +Z); null where there is none.</summary>
    public TerrainGeneratedNode?[] Children { get; } = new TerrainGeneratedNode?[TerrainOrientation.Count];

    /// <summary>Gets how many distinct nodes this one and everything under it are, counting a shared node once.</summary>
    public int Count
    {
        get
        {
            HashSet<TerrainGeneratedNode> seen = new(ReferenceEqualityComparer.Instance);
            Stack<TerrainGeneratedNode> open = new([this]);
            while (open.TryPop(out TerrainGeneratedNode? node))
            {
                if (seen.Add(node))
                {
                    foreach (TerrainGeneratedNode? child in node.Children)
                    {
                        if (child is not null)
                        {
                            open.Push(child);
                        }
                    }
                }
            }

            return seen.Count;
        }
    }
}

/// <summary>
/// Makes new terrain for a square of the world from a height field: one column of the octree at a depth, built down to the
/// finest depth the original draws, laid out as the retail files lay terrain out.
/// </summary>
/// <remarks>
/// <para>
/// Retail terrain is a height surface cut into the octree's cubes. A node draws the part of its column's surface inside its
/// own cube, at two levels of detail at once: its base vertices are the coarse surface (the quad over the column's corners)
/// cut to the cube, and its interpolated vertices the fine one (the quads over its children's corners) cut the same way,
/// each sliding onto a base vertex as the camera draws away (<c>0x512C30</c>), so a node seen from afar is the coarse
/// surface and the step to its children is seamless. A slope climbs through the cubes stacked over a column, each holding
/// the slice between its floor and its roof, where the cut leaves vertices (retail's base heights are all on those faces
/// or at thirds between them); no vertex leaves its cube, and every node has all eight children, the ones the surface
/// misses empty, which is what lets collision (<c>0x4C9D70</c>) always descend to the depth it reads.
/// </para>
/// <para>
/// A fine vertex slides onto the corner the fine grid collapses it to (the end of its edge with the lower world X or Y, the
/// centre onto the lower-left corner, which folds the four cells onto the coarse quad split from that corner to the far
/// one) when the cube holds that corner; otherwise onto the nearest base vertex on the same faces of the cube, so the two
/// sides of a shared face choose alike.
/// </para>
/// </remarks>
public sealed class TerrainRegionGenerator
{
    /// <summary>The finest depth drawn or collided with: the original pushes its walk only into children shallower than 16
    /// (<c>0x4D14E0</c>), and collision stops at 16 (<c>0x4C9D70</c>), so nothing reads deeper.</summary>
    public const int LeafExponent = 16;

    /// <summary>The depth whose corners are the height samples: the leaves' children's corners, 4 m apart.</summary>
    public const int SampleExponent = LeafExponent + 1;

    /// <summary>The most vertices a block may have: the original's per-chunk vertex records (<c>0x510010</c>).</summary>
    public const int MaximumVertices = 63;

    // A vertex steeper than this projects its texture down the slope rather than across the ground (about 40 degrees).
    private const double SteepSlope = 0.84;

    // Every triangle ends its own run, so the original's strip walk never carries vertices from one to the next.
    private const uint RestartRun = 0xF0000000;

    private const int Unit = TerrainPackedVertex.UnitsPerExtent;

    // How many sample cells a leaf spans.
    private const int LeafSpan = 1 << (SampleExponent - LeafExponent);

    // The fine mesh over a column: four cells of the 3 × 3 grid (ids are grid indices j * 3 + i), each split from its
    // (-,-) corner to its (+,+) one and wound counter-clockwise seen from above, as retail's front faces are.
    private static readonly (int A, int B, int C)[] FineTriangles =
    [
        (0, 1, 4), (0, 4, 3),
        (1, 2, 5), (1, 5, 4),
        (3, 4, 7), (3, 7, 6),
        (4, 5, 8), (4, 8, 7),
    ];

    // The coarse quad: the column's corners, split from the lower-left corner to the far one, as the fine mesh collapses.
    private static readonly (int A, int B, int C)[] CoarseTriangles = [(0, 2, 8), (0, 8, 6)];

    // The corner each grid point collapses to: edge middles to the end with the lower X or Y, the centre to the lower left.
    private static readonly int[] CollapsesTo = [0, 0, 2, 0, 0, 2, 6, 6, 8];

    private readonly double[] _heights;
    private readonly int _cells;
    private readonly long _spacing;
    private readonly int[] _clampedAt = new int[LeafExponent + 1];

    /// <summary>Initializes a new instance of the <see cref="TerrainRegionGenerator"/> class.</summary>
    /// <param name="exponent">The depth of the region's column; its square is twice that depth's extent across.</param>
    /// <param name="minX">The region's lowest X, in engine units; a multiple of its width.</param>
    /// <param name="minY">The region's lowest Y.</param>
    /// <param name="height">The ground's height at a point, in engine units, asked once at every sample.</param>
    /// <exception cref="ArgumentOutOfRangeException">The depth is not between 1 and <see cref="LeafExponent"/>.</exception>
    public TerrainRegionGenerator(int exponent, long minX, long minY, Func<long, long, double> height)
    {
        ArgumentNullException.ThrowIfNull(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(exponent, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(exponent, LeafExponent);
        Exponent = exponent;
        MinX = minX;
        MinY = minY;
        _spacing = 2L * ExtentOf(SampleExponent);
        _cells = 1 << (SampleExponent - exponent);
        _heights = new double[(_cells + 1) * (_cells + 1)];
        for (int y = 0; y <= _cells; y++)
        {
            for (int x = 0; x <= _cells; x++)
            {
                _heights[(y * (_cells + 1)) + x] = height(minX + (x * _spacing), minY + (y * _spacing));
            }
        }
    }

    /// <summary>Gets the node a column's other cubes hold: no geometry and no children, one instance shared by them all.</summary>
    public static TerrainGeneratedNode Empty { get; } = new() { Block = new byte[TerrainCommandBlock.HeaderSize] };

    /// <summary>
    /// Gets the edge the untouched ground draws at a depth, read where the region's edge meets it: given the depth, a point on
    /// the edge in engine units and the region's own height there, its height in engine units, or null to keep the region's.
    /// </summary>
    /// <remarks>
    /// The region's heights are the ground as it is walked on, heightmap and all, sampled 4 m apart; the neighbour's blocks
    /// are the octree alone, simplified at every coarser depth, so its edge can stand metres off those heights. Whatever
    /// heightmap the original adds, it adds by world position, the same on both sides of the edge. Taking every depth's edge
    /// from the neighbour's own blocks closes the crack the sky would otherwise show through.
    /// </remarks>
    public Func<int, long, long, double, double?>? Border { get; init; }

    /// <summary>Gets the depth of the region's column.</summary>
    public int Exponent { get; }

    /// <summary>Gets the region's lowest X, in engine units.</summary>
    public long MinX { get; }

    /// <summary>Gets the region's lowest Y, in engine units.</summary>
    public long MinY { get; }

    /// <summary>Gets the region's width, in engine units.</summary>
    public long Side => 2L * ExtentOf(Exponent);

    /// <summary>Gets the distance between height samples, in engine units.</summary>
    public long Spacing => _spacing;

    /// <summary>Gets how many samples the region is across, less one.</summary>
    public int Cells => _cells;

    /// <summary>Gets how many columns the last <see cref="Build"/> could not hold whole: ground past the parents' cubes, or a
    /// cut too fine for a block's vertices.</summary>
    public int Clamped { get; private set; }

    /// <summary>Gets <see cref="Clamped"/> by depth.</summary>
    public IReadOnlyList<int> ClampedAt => _clampedAt;

    /// <summary>Gets the sampled height at a sample of the region, in engine units.</summary>
    /// <param name="x">The sample's column, from the region's lowest X.</param>
    /// <param name="y">The sample's row.</param>
    /// <returns>The height; samples past the edge read the edge.</returns>
    public double Height(int x, int y) => _heights[(Math.Clamp(y, 0, _cells) * (_cells + 1)) + Math.Clamp(x, 0, _cells)];

    /// <summary>Builds the region's subtree, to hang from the nodes one level up whose column holds it.</summary>
    /// <param name="parentCentresZ">The centre Z of each node one level up the region's column may hang from.</param>
    /// <returns>
    /// For each parent, by its centre Z, the region's nodes in its lower and upper halves (index 0 and 1): the new ground in
    /// the cubes it reaches, <see cref="Empty"/> in the ones it does not.
    /// </returns>
    /// <remarks>The subtree reaches no higher or lower than the parents' cubes; ground beyond them is held to them.</remarks>
    public IReadOnlyDictionary<long, TerrainGeneratedNode[]> Build(IReadOnlyCollection<long> parentCentresZ)
    {
        ArgumentNullException.ThrowIfNull(parentCentresZ);
        if (parentCentresZ.Count == 0)
        {
            throw new ArgumentException("The region needs a node to hang from.", nameof(parentCentresZ));
        }

        Clamped = 0;
        Array.Clear(_clampedAt);
        long parentExtent = ExtentOf(Exponent - 1);
        double floor = parentCentresZ.Min() - parentExtent;
        double ceiling = parentCentresZ.Max() + parentExtent;

        // Every node by depth, column and centre Z: a cube for each slice of each column's surface.
        Dictionary<(int Exponent, int CellX, int CellY, long CentreZ), TerrainGeneratedNode> nodes = [];
        for (int exponent = Exponent; exponent <= LeafExponent; exponent++)
        {
            long extent = ExtentOf(exponent);
            int span = 1 << (SampleExponent - exponent);
            for (int cellY = 0; cellY < _cells; cellY += span)
            {
                for (int cellX = 0; cellX < _cells; cellX += span)
                {
                    double[] grid = new double[9];
                    double[] slid = new double[9];
                    bool held = false;
                    for (int j = 0; j <= 2; j++)
                    {
                        for (int i = 0; i <= 2; i++)
                        {
                            int gx = cellX + (i * span / 2), gy = cellY + (j * span / 2);
                            double h = Height(gx, gy);
                            bool onEdge = gx == 0 || gy == 0 || gx == _cells || gy == _cells;
                            (double X, double Y) at = (MinX + (gx * _spacing), MinY + (gy * _spacing));

                            // On the region's edge, the edge the untouched neighbour draws at this depth, so the two meet.
                            double fine = onEdge && Border?.Invoke(exponent, (long)at.X, (long)at.Y, h) is { } edge ? edge : h;
                            double kept = Math.Clamp(fine, floor + 1, ceiling - 1);
                            held |= kept != fine;
                            grid[(j * 3) + i] = kept;

                            // Where the slice slides to as it recedes: the depth above at rest, in that depth's steps, so a
                            // cube fully slid meets a coarser neighbour exactly, the way the original's do.
                            double coarse = onEdge && Border?.Invoke(exponent - 1, (long)at.X, (long)at.Y, h) is { } above ? above : h;
                            double step = (double)ExtentOf(exponent - 1) / Unit;
                            slid[(j * 3) + i] = Math.Clamp(Math.Round(coarse / step) * step, floor + step, ceiling - step);
                        }
                    }

                    if (held)
                    {
                        Clamped++;
                        _clampedAt[exponent]++;
                    }

                    for (long centreZ = Containing(grid.Min(), extent); centreZ - extent <= grid.Max(); centreZ += 2 * extent)
                    {
                        if (Block(cellX, cellY, centreZ, extent, span, grid, slid) is { } block)
                        {
                            nodes[(exponent, cellX, cellY, centreZ)] = new TerrainGeneratedNode { Block = block };
                        }
                    }
                }
            }
        }

        // Every cube on the way down to a slice, with no geometry where it holds none of its own.
        foreach ((int exponent, int cellX, int cellY, long centreZ) in nodes.Keys.ToArray())
        {
            int e = exponent, x = cellX, y = cellY;
            long z = centreZ;
            while (e > Exponent)
            {
                int upSpan = 1 << (SampleExponent - (e - 1));
                (e, x, y, z) = (e - 1, x / upSpan * upSpan, y / upSpan * upSpan, Containing(z, ExtentOf(e - 1)));
                if (!nodes.ContainsKey((e, x, y, z)))
                {
                    nodes[(e, x, y, z)] = new TerrainGeneratedNode { Block = Empty.Block };
                }
            }
        }

        // Children: every node above the leaves has all eight, the ones the ground does not reach empty.
        foreach (((int exponent, int cellX, int cellY, long centreZ), TerrainGeneratedNode node) in nodes)
        {
            if (exponent == LeafExponent)
            {
                continue;
            }

            long childExtent = ExtentOf(exponent + 1);
            int childSpan = 1 << (SampleExponent - (exponent + 1));
            for (int octant = 0; octant < TerrainOrientation.Count; octant++)
            {
                (int, int, int, long) key = (
                    exponent + 1,
                    cellX + ((octant & 1) * childSpan),
                    cellY + (((octant >> 1) & 1) * childSpan),
                    centreZ + ((octant & 4) != 0 ? childExtent : -childExtent));
                node.Children[octant] = nodes.TryGetValue(key, out TerrainGeneratedNode? child) ? child : Empty;
            }
        }

        // The region's own column, by the parent each cube of it hangs from.
        long extent0 = ExtentOf(Exponent);
        Dictionary<long, TerrainGeneratedNode[]> roots = [];
        foreach (long parent in parentCentresZ.Distinct())
        {
            roots[parent] =
            [
                nodes.TryGetValue((Exponent, 0, 0, parent - extent0), out TerrainGeneratedNode? below) ? below : Empty,
                nodes.TryGetValue((Exponent, 0, 0, parent + extent0), out TerrainGeneratedNode? above) ? above : Empty,
            ];
        }

        return roots;
    }

    // The centre of the cube of a depth holding a height: cube centres are odd multiples of the half-width.
    private static long Containing(double z, long extent) => ((long)Math.Floor(z / (2.0 * extent)) * 2 * extent) + extent;

    // One cube's slice of a column: the fine mesh and the coarse quad cut to the cube's floor and roof, or null when the
    // surface leaves nothing to draw in it. A drawn-only cube on the region's edge is skirted, unless that costs it more
    // vertices than the original holds.
    private byte[]? Block(int cellX, int cellY, long centreZ, long extent, int span, double[] grid, double[] slid, bool skirted = true)
    {
        // The grid in packed units about the cube's centre.
        (double X, double Y, double Z)[] points = new (double, double, double)[9];
        (double X, double Y, double Z)[] targets = new (double, double, double)[9];
        for (int g = 0; g < 9; g++)
        {
            points[g] = (((g % 3) - 1) * Unit, ((g / 3) - 1) * Unit, (grid[g] - centreZ) * Unit / extent);
            targets[g] = (points[g].X, points[g].Y, (slid[g] - centreZ) * Unit / extent);
        }

        List<(int X, int Y, int Z)> fine = [];
        List<(int A, int B, int C)> triangles = [];
        Dictionary<(int, int, int), int> fineIndex = [];
        Dictionary<(int, int, int), int> fromGrid = [];
        Cut(FineTriangles, points, fine, fineIndex, triangles, fromGrid);
        if (triangles.Count == 0)
        {
            return null;
        }

        HashSet<(int X, int Y, int Z)> hung = skirted && span > LeafSpan ? Skirt(cellX, cellY, span, fine, fineIndex, triangles) : [];

        List<(int X, int Y, int Z)> coarse = [];
        Cut(CoarseTriangles, targets, coarse, [], [], []);

        // Base vertices are the coarse slice's; the fine slice's others slide onto them.
        List<(int X, int Y, int Z)> bases = [.. coarse.Distinct()];
        Dictionary<(int, int, int), int> baseIndex = [];
        for (int i = 0; i < bases.Count; i++)
        {
            baseIndex[bases[i]] = i;
        }

        List<((int X, int Y, int Z) Where, int Source)> extras = [];
        Dictionary<(int, int, int), int> placed = [];
        foreach ((int X, int Y, int Z) vertex in fine)
        {
            if (baseIndex.ContainsKey(vertex) || placed.ContainsKey(vertex))
            {
                continue;
            }

            int source = hung.Contains(vertex) ? -1 : Source(vertex);
            if (source < 0)
            {
                // Nothing coarse to slide onto in this cube: the vertex stands as it is, a base vertex of its own.
                baseIndex[vertex] = bases.Count;
                bases.Add(vertex);
                continue;
            }

            placed[vertex] = extras.Count;
            extras.Add((vertex, source));
        }

        if (bases.Count + extras.Count > MaximumVertices)
        {
            if (hung.Count > 0)
            {
                return Block(cellX, cellY, centreZ, extent, span, grid, slid, skirted: false);
            }

            // More than the original can hold: the slice is dropped, and counted.
            Clamped++;
            return null;
        }

        // A fine vertex's target: the corner the grid collapses it to, if the cube holds it, else the nearest base vertex
        // on all the same faces of the cube.
        int Source((int X, int Y, int Z) vertex)
        {
            if (fromGrid.TryGetValue(vertex, out int g) && baseIndex.TryGetValue(Quantised(targets[CollapsesTo[g]]), out int corner) && corner < 64)
            {
                return corner;
            }

            int best = -1;
            long bestDistance = long.MaxValue;
            for (int i = 0; i < Math.Min(bases.Count, 64); i++)
            {
                (int bx, int by, int bz) = bases[i];
                if (!SameFaces(vertex, bases[i]) || hung.Contains(bases[i]))
                {
                    continue;
                }

                long distance = ((long)(bx - vertex.X) * (bx - vertex.X)) + ((long)(by - vertex.Y) * (by - vertex.Y)) + ((long)(bz - vertex.Z) * (bz - vertex.Z));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        // Base vertices nothing draws and nothing slides onto cost the cache's room for nothing: they go.
        HashSet<int> needed = [.. extras.Select(static e => e.Source)];
        foreach ((int a, int b, int c) in triangles)
        {
            foreach ((int X, int Y, int Z) v in (ReadOnlySpan<(int, int, int)>)[fine[a], fine[b], fine[c]])
            {
                if (baseIndex.TryGetValue(v, out int used))
                {
                    needed.Add(used);
                }
            }
        }

        int[] renumbered = new int[bases.Count];
        List<(int X, int Y, int Z)> kept = [];
        for (int i = 0; i < bases.Count; i++)
        {
            renumbered[i] = needed.Contains(i) ? kept.Count : -1;
            if (renumbered[i] >= 0)
            {
                kept.Add(bases[i]);
            }
        }

        bases = kept;
        for (int i = 0; i < extras.Count; i++)
        {
            extras[i] = (extras[i].Where, renumbered[extras[i].Source]);
        }

        foreach ((int X, int Y, int Z) key in baseIndex.Keys.ToArray())
        {
            if (renumbered[baseIndex[key]] < 0)
            {
                baseIndex.Remove(key);
            }
            else
            {
                baseIndex[key] = renumbered[baseIndex[key]];
            }
        }

        int baseCount = bases.Count;
        int IndexOf((int X, int Y, int Z) vertex) =>
            baseIndex.TryGetValue(vertex, out int b) ? b : baseCount + placed[vertex];

        byte[] block = new byte[TerrainCommandBlock.HeaderSize + (triangles.Count * 4) + ((bases.Count + extras.Count) * 4)];
        BinaryPrimitives.WriteUInt16LittleEndian(block, (ushort)triangles.Count);
        BinaryPrimitives.WriteInt16LittleEndian(block.AsSpan(2), (short)(triangles.Count * 4));
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(4), (ushort)bases.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(6), (ushort)extras.Count);
        int cursor = TerrainCommandBlock.HeaderSize;
        foreach ((int a, int b, int c) in triangles)
        {
            uint packed = (uint)IndexOf(fine[a]) | ((uint)IndexOf(fine[b]) << 8) | ((uint)IndexOf(fine[c]) << 16) | RestartRun;
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(cursor), packed);
            cursor += 4;
        }

        foreach ((int x, int y, int z) in bases)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(cursor), Pack(x, y, z, 0, Axis(cellX, cellY, span, x, y)));
            cursor += 4;
        }

        foreach (((int x, int y, int z), int source) in extras)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(cursor), Pack(x, y, z, source, Axis(cellX, cellY, span, x, y)));
            cursor += 4;
        }

        return block;

        // Cuts triangles of the grid to the cube's slab, collecting the slice's vertices and triangles; a vertex that is a
        // grid point is remembered as one, for its collapse.
        static void Cut((int A, int B, int C)[] mesh, (double X, double Y, double Z)[] points, List<(int X, int Y, int Z)> into, Dictionary<(int, int, int), int> index, List<(int A, int B, int C)> faces, Dictionary<(int, int, int), int> grids)
        {
            foreach ((int a, int b, int c) in mesh)
            {
                List<(double X, double Y, double Z, int Grid)> polygon = [(points[a].X, points[a].Y, points[a].Z, a), (points[b].X, points[b].Y, points[b].Z, b), (points[c].X, points[c].Y, points[c].Z, c)];
                polygon = Clip(Clip(polygon, -Unit, keepAbove: true), Unit, keepAbove: false);
                if (polygon.Count < 3)
                {
                    continue;
                }

                int[] ids = new int[polygon.Count];
                for (int k = 0; k < polygon.Count; k++)
                {
                    (int X, int Y, int Z) q = Quantised((polygon[k].X, polygon[k].Y, polygon[k].Z));
                    if (!index.TryGetValue(q, out int id))
                    {
                        id = into.Count;
                        index[q] = id;
                        into.Add(q);
                    }

                    if (polygon[k].Grid >= 0)
                    {
                        grids.TryAdd(q, polygon[k].Grid);
                    }

                    ids[k] = id;
                }

                // A fan over the convex slice, skipping what quantising flattened.
                for (int k = 1; k + 1 < ids.Length; k++)
                {
                    if (ids[0] != ids[k] && ids[k] != ids[k + 1] && ids[0] != ids[k + 1] && Spans(into[ids[0]], into[ids[k]], into[ids[k + 1]]))
                    {
                        faces.Add((ids[0], ids[k], ids[k + 1]));
                    }
                }
            }
        }
    }

    // Skirts the slice on each of the region's edges its cube stands on: a curtain from every segment of the surface along
    // that face down to the cube's floor, facing out of the region. The neighbour beyond is the original's ground, cut and
    // morphed by its own rules, so the two edges part by up to a few metres as the levels of detail slide; the curtain
    // fills that gap, and from outside it is below the neighbour's ground everywhere else. Its top is the surface's own
    // vertices, so it slides with them; its hung vertices are returned, base vertices that slide nowhere. A leaf has
    // none: the original's collision (0x4C9020) reads a leaf as a closed surface, and only leaves are collided with.
    private HashSet<(int X, int Y, int Z)> Skirt(int cellX, int cellY, int span, List<(int X, int Y, int Z)> fine, Dictionary<(int, int, int), int> fineIndex, List<(int A, int B, int C)> triangles)
    {
        HashSet<(int X, int Y, int Z)> hung = [];
        List<(int Axis, int Side)> faces = [];
        if (cellX == 0)
        {
            faces.Add((0, -1));
        }

        if (cellX + span == _cells)
        {
            faces.Add((0, 1));
        }

        if (cellY == 0)
        {
            faces.Add((1, -1));
        }

        if (cellY + span == _cells)
        {
            faces.Add((1, 1));
        }

        int count = triangles.Count;
        foreach ((int axis, int side) in faces)
        {
            bool OnFace((int X, int Y, int Z) v) => (axis == 0 ? v.X : v.Y) == side * Unit;
            for (int t = 0; t < count; t++)
            {
                (int a, int b, int c) = triangles[t];
                foreach ((int p, int q) in (ReadOnlySpan<(int, int)>)[(a, b), (b, c), (c, a)])
                {
                    (int X, int Y, int Z) top0 = fine[p], top1 = fine[q];
                    if (!OnFace(top0) || !OnFace(top1) || (top0.Z == -Unit && top1.Z == -Unit))
                    {
                        continue;
                    }

                    int low0 = Hang(top0), low1 = Hang(top1);
                    Face(p, q, low1);
                    Face(p, low1, low0);
                }
            }

            // Adds a curtain triangle wound to face out of the region, unless quantising flattened it.
            void Face(int i, int j, int k)
            {
                if (i == j || j == k || i == k || !Spans(fine[i], fine[j], fine[k]))
                {
                    return;
                }

                (int X, int Y, int Z) u = (fine[j].X - fine[i].X, fine[j].Y - fine[i].Y, fine[j].Z - fine[i].Z);
                (int X, int Y, int Z) v = (fine[k].X - fine[i].X, fine[k].Y - fine[i].Y, fine[k].Z - fine[i].Z);
                long normal = axis == 0 ? ((long)u.Y * v.Z) - ((long)u.Z * v.Y) : ((long)u.Z * v.X) - ((long)u.X * v.Z);
                triangles.Add(normal * side > 0 ? (i, j, k) : (i, k, j));
            }
        }

        return hung;

        // The vertex on the cube's floor below a top vertex: the top itself when it is on the floor already.
        int Hang((int X, int Y, int Z) top)
        {
            (int X, int Y, int Z) low = (top.X, top.Y, -Unit);
            if (!fineIndex.TryGetValue(low, out int id))
            {
                id = fine.Count;
                fineIndex[low] = id;
                fine.Add(low);
                hung.Add(low);
            }

            return id;
        }
    }

    // Sutherland–Hodgman against one horizontal plane; a vertex the cut makes carries no grid point.
    private static List<(double X, double Y, double Z, int Grid)> Clip(List<(double X, double Y, double Z, int Grid)> polygon, double plane, bool keepAbove)
    {
        List<(double X, double Y, double Z, int Grid)> kept = [];
        for (int k = 0; k < polygon.Count; k++)
        {
            var current = polygon[k];
            var next = polygon[(k + 1) % polygon.Count];
            bool inCurrent = keepAbove ? current.Z >= plane : current.Z <= plane;
            bool inNext = keepAbove ? next.Z >= plane : next.Z <= plane;
            if (inCurrent)
            {
                kept.Add(current);
            }

            if (inCurrent != inNext)
            {
                double t = (plane - current.Z) / (next.Z - current.Z);
                kept.Add((current.X + ((next.X - current.X) * t), current.Y + ((next.Y - current.Y) * t), plane, -1));
            }
        }

        return kept;
    }

    private static (int X, int Y, int Z) Quantised((double X, double Y, double Z) p) =>
        ((int)Math.Round(p.X), (int)Math.Round(p.Y), Math.Clamp((int)Math.Round(p.Z), -Unit, Unit));

    // Whether three vertices make a triangle at all: quantising can line them up. A vertical one is a triangle too.
    private static bool Spans((int X, int Y, int Z) a, (int X, int Y, int Z) b, (int X, int Y, int Z) c)
    {
        long ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
        long vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
        return (uy * vz) - (uz * vy) != 0 || (uz * vx) - (ux * vz) != 0 || (ux * vy) - (uy * vx) != 0;
    }

    // Whether a base vertex lies on every face of the cube the vertex does: its side faces and its floor or roof.
    private static bool SameFaces((int X, int Y, int Z) vertex, (int X, int Y, int Z) candidate) =>
        (Math.Abs(vertex.X) != Unit || candidate.X == vertex.X)
        && (Math.Abs(vertex.Y) != Unit || candidate.Y == vertex.Y)
        && (Math.Abs(vertex.Z) != Unit || candidate.Z == vertex.Z);

    // The texture projection at a packed position of a column: across the ground where it is gentle, down the slope where
    // it is steep, with U along the axis the slope does not climb (0x510010 reads an odd selector as U from Y, an even one
    // as U from X).
    private int Axis(int cellX, int cellY, int span, int x, int y)
    {
        int sx = cellX + (int)Math.Round((x + Unit) / (2.0 * Unit) * span);
        int sy = cellY + (int)Math.Round((y + Unit) / (2.0 * Unit) * span);
        int reach = Math.Max(1, span / 2);
        double dx = (Height(sx + reach, sy) - Height(sx - reach, sy)) / (2.0 * reach * _spacing);
        double dy = (Height(sx, sy + reach) - Height(sx, sy - reach)) / (2.0 * reach * _spacing);
        return Math.Max(Math.Abs(dx), Math.Abs(dy)) < SteepSlope ? 0 : Math.Abs(dx) > Math.Abs(dy) ? 1 : 2;
    }

    private static long ExtentOf(int exponent) => (long)TerrainOctree.RootExtent >> exponent;

    // A vertex dword: X at bit 26, Y at 20, Z at 14, the texture axes at 8 and 6, and the slide source in the low six bits.
    private static uint Pack(int x, int y, int z, int source, int axis) =>
        ((uint)(x + Unit) << 26)
        | ((uint)(y + Unit) << 20)
        | ((uint)(z + Unit) << 14)
        | ((uint)axis << 8)
        | ((uint)axis << 6)
        | (uint)(source & 0x3F);
}
