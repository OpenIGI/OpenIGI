using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>What building a region of terrain made.</summary>
/// <param name="Ctr">The new <c>terrain.ctr</c>.</param>
/// <param name="Cmd">The new <c>terrain.cmd</c>.</param>
/// <param name="Nodes">How many nodes the region's new column is.</param>
/// <param name="Clamped">How many vertex heights were held inside their cube: ground steeper than the format holds there.</param>
public sealed record TerrainRegionResult(byte[] Ctr, byte[] Cmd, int Nodes, int Clamped)
{
    /// <summary>Reads the result back as terrain the game can stand on and draw, with the level's other data.</summary>
    /// <param name="level">The level's terrain the region was built in.</param>
    /// <returns>The terrain with the region in it and no heightmap bound.</returns>
    public TerrainData ToTerrain(TerrainData level)
    {
        ArgumentNullException.ThrowIfNull(level);
        return new TerrainData(
            TerrainOctree.Read(Ctr),
            TerrainCommands.Read(Cmd),
            level.Visibility,
            heightmap: null,
            level.Lightmaps,
            level.Tiles,
            level.Materials,
            level.TileMaps,
            []);
    }
}

/// <summary>
/// Builds new ground for a square of a Project I.G.I. level: any shape, as high as the octree there can hold, where the
/// heightmap can only nudge the ground a few metres (<see cref="Igi1EditableTerrain"/>).
/// </summary>
/// <remarks>
/// <para>
/// The square is one column of the octree at a depth (<see cref="TerrainRegionGenerator"/>), whose subtree is made again
/// from the heights and hung from the node one level up whose cube the new ground fits best; the column's other cubes are
/// left empty, as retail's are. The coarser nodes above, which draw the level from afar, keep their geometry with the
/// vertices inside the square moved to the new heights, so the new ground shows at a distance too. The square's edge
/// should keep the ground's own height, where it meets the untouched terrain.
/// </para>
/// </remarks>
public static class TerrainRegionBuild
{
    /// <summary>The depth of a 512 m square.</summary>
    public const int DefaultExponent = 10;

    // The shallowest node the original draws (TerrainTraversal.MinimumExponent); nothing above it needs reshaping.
    private const int DrawnExponent = 7;

    /// <summary>Builds the region.</summary>
    /// <param name="terrain">The level's terrain.</param>
    /// <param name="exponent">The depth of the region's column.</param>
    /// <param name="minX">The region's lowest X, in engine units, a multiple of its width.</param>
    /// <param name="minY">The region's lowest Y.</param>
    /// <param name="height">The new ground's height at a point, in engine units.</param>
    /// <returns>The new files, or <see langword="null"/> when the level has no terrain over the square to hang it from.</returns>
    public static TerrainRegionResult? Build(TerrainData terrain, int exponent, long minX, long minY, Func<long, long, double> height)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        return Build(terrain.Octree.Data, terrain.Commands.Data, exponent, minX, minY, height);
    }

    /// <summary>Builds the region into a level's terrain files, touching nothing live: safe away from the main thread.</summary>
    /// <param name="ctr">The level's <c>terrain.ctr</c>.</param>
    /// <param name="cmd">The level's <c>terrain.cmd</c>.</param>
    /// <param name="exponent">The depth of the region's column.</param>
    /// <param name="minX">The region's lowest X, in engine units, a multiple of its width.</param>
    /// <param name="minY">The region's lowest Y.</param>
    /// <param name="height">The new ground's height at a point, in engine units.</param>
    /// <returns>The new files, or <see langword="null"/> when the level has no terrain over the square to hang it from.</returns>
    public static TerrainRegionResult? Build(ReadOnlyMemory<byte> ctr, ReadOnlyMemory<byte> cmd, int exponent, long minX, long minY, Func<long, long, double> height)
    {
        ArgumentNullException.ThrowIfNull(height);

        // The edge comes from the level as it shipped: what its own blocks draw at each depth where the region meets it.
        TerrainOctree originalTree = TerrainOctree.Read(ctr);
        TerrainCommands originalBlocks = TerrainCommands.Read(cmd);
        long sideUnits = 2L * ((long)TerrainOctree.RootExtent >> exponent);
        TerrainRegionGenerator generator = new(exponent, minX, minY, height)
        {
            Border = (depth, x, y, near) => DrawnAt(originalTree, originalBlocks, depth, Math.Clamp(x, minX + 1, minX + sideUnits - 1), Math.Clamp(y, minY + 1, minY + sideUnits - 1), near),
        };
        long side = generator.Side;
        long maxX = minX + side, maxY = minY + side;
        bool Touches(TerrainNodePlace place) =>
            place.CentreX + place.Extent > minX && place.CentreX - place.Extent < maxX
            && place.CentreY + place.Extent > minY && place.CentreY - place.Extent < maxY;

        // The nodes one level up whose column holds the square: the walk itself finds them, changing nothing.
        List<TerrainNodePlace> parents = [];
        TerrainOctreeRewriter.Rewrite(
            ctr,
            cmd,
            place =>
            {
                if (place.Exponent == exponent - 1 && Touches(place))
                {
                    parents.Add(place);
                    return false;
                }

                return place.Exponent < exponent - 1 && Touches(place);
            },
            static (_, _) => null);
        if (parents.Count == 0)
        {
            return null;
        }

        // The new ground hangs from every one of them whose cube it reaches.
        Dictionary<long, TerrainNodePlace> byHeight = parents.GroupBy(static p => p.CentreZ).ToDictionary(static g => g.Key, static g => g.First());
        IReadOnlyDictionary<long, TerrainGeneratedNode[]> columns = generator.Build(byHeight.Keys);
        int clamped = generator.Clamped;
        int count = columns.Values.SelectMany(static c => c).Distinct().Sum(static n => n.Count);

        (byte[] Ctr, byte[] Cmd)? files = TerrainOctreeRewriter.Rewrite(
            ctr,
            cmd,
            place => place.Exponent < exponent && Touches(place),
            (place, block) => place.Exponent is >= DrawnExponent && place.Exponent < exponent ? Reshape(generator, place, block) : null,
            (place, octant) =>
            {
                if (place.Exponent != exponent - 1 || !Holds(place, octant, minX, minY))
                {
                    return null;
                }

                // Every parent at a height gets the same nodes: the walk can reach one cube through more than one record.
                TerrainGeneratedNode[] halves = columns.TryGetValue(place.CentreZ, out TerrainGeneratedNode[]? found) ? found : [TerrainRegionGenerator.Empty, TerrainRegionGenerator.Empty];
                return new TerrainChildReplacement(halves[(octant & 4) != 0 ? 1 : 0]);
            });
        return files is { } written ? new TerrainRegionResult(written.Ctr, written.Cmd, count, clamped) : null;
    }

    /// <summary>
    /// What a depth's blocks draw at a point, the octree alone with no heightmap added: the slice of
    /// the column nearest a height, in engine units, or null when no block there covers the point.
    /// </summary>
    /// <param name="tree">The octree.</param>
    /// <param name="blocks">Its blocks.</param>
    /// <param name="depth">The depth.</param>
    /// <param name="x">The point's X, in engine units.</param>
    /// <param name="y">The point's Y.</param>
    /// <param name="near">A height near the surface, in engine units, to pick the slice by.</param>
    /// <returns>The height, in engine units.</returns>
    public static double? DrawnAt(TerrainOctree tree, TerrainCommands blocks, int depth, long x, long y, double near)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(blocks);
        long extent = (long)TerrainOctree.RootExtent >> depth;

        // The cube at the height first, then those above and below it in turn.
        for (int k = 0; k < 12; k++)
        {
            double z = near + ((k % 2 == 0 ? 1 : -1) * (k / 2) * 2 * extent);
            if (z is < int.MinValue or > int.MaxValue)
            {
                continue;
            }

            TerrainDescentCursor cursor = TerrainPointDescent.Descend(tree, new Vector3i((int)x, (int)y, (int)z), depth);
            if (cursor.Depth != depth || !blocks.TryGetBlock(tree[cursor.NodeIndex].CommandOffset, out TerrainCommandBlock block) || block.IsEmpty)
            {
                continue;
            }

            TerrainLeaf slice = new(cursor, block, cursor.Centre, TerrainLeafQuery.UnitScaleFor(cursor.Extent));
            if (TerrainSurfaceTest.TryFindSurface(slice, x - cursor.Centre.X, y - cursor.Centre.Y, -2.0 * cursor.Extent, downward: true, out TerrainSurfaceHit hit))
            {
                return hit.Height + cursor.Centre.Z;
            }
        }

        return null;
    }

    // Whether an octant of a node is the region's column: its corner is the region's.
    private static bool Holds(TerrainNodePlace parent, int octant, long minX, long minY)
    {
        long step = parent.Extent / 2;
        long centreX = parent.CentreX + ((octant & 1) != 0 ? step : -step);
        long centreY = parent.CentreY + ((octant & 2) != 0 ? step : -step);
        return centreX - step == minX && centreY - step == minY;
    }

    // A coarse node's block with its vertices inside the region at the new heights, or null when none moves a step.
    private static byte[]? Reshape(TerrainRegionGenerator generator, TerrainNodePlace place, TerrainCommandBlock block)
    {
        if (block.IsEmpty)
        {
            return null;
        }

        double unit = place.Unit;
        int[] heights = new int[block.VertexCount];
        bool changed = false;
        for (int i = 0; i < heights.Length; i++)
        {
            TerrainPackedVertex vertex = block.Vertex(i);
            heights[i] = vertex.Z;
            (int localX, int localY) = TerrainOrientation.Transform(place.Orientation, vertex.X, vertex.Y);
            double x = place.CentreX + (localX * unit) - generator.MinX;
            double y = place.CentreY + (localY * unit) - generator.MinY;

            // Strictly inside: the edge keeps the height the untouched ground beyond it shares.
            if (x <= 0 || y <= 0 || x >= generator.Side || y >= generator.Side)
            {
                continue;
            }

            int z = (int)Math.Round((Sample(generator, x, y) - place.CentreZ) / unit);
            if (z != heights[i])
            {
                heights[i] = z;
                changed = true;
            }
        }

        // A coarse quad may lean out of the top of its cube: it is only drawn, never collided with, at these depths.
        return changed ? block.WithHeights(heights, highest: 63 - TerrainPackedVertex.UnitsPerExtent) : null;
    }

    // The region's height at a point inside it, bilinear between its samples.
    private static double Sample(TerrainRegionGenerator generator, double x, double y)
    {
        double fx = x / generator.Spacing, fy = y / generator.Spacing;
        int ix = (int)Math.Floor(fx), iy = (int)Math.Floor(fy);
        double tx = fx - ix, ty = fy - iy;
        double top = generator.Height(ix, iy) + ((generator.Height(ix + 1, iy) - generator.Height(ix, iy)) * tx);
        double bottom = generator.Height(ix, iy + 1) + ((generator.Height(ix + 1, iy + 1) - generator.Height(ix, iy + 1)) * tx);
        return top + ((bottom - top) * ty);
    }
}
