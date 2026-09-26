using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>Segment traversal through the terrain octree's authored triangle leaves.</summary>
/// <remarks>
/// Unlike <see cref="TerrainSightBlock"/>, this does not sample a heightfield. It walks every depth-16
/// octree cell crossed by the segment and intersects the segment with the exact packed triangles in
/// that leaf. That preserves cliffs, thin ridges and underside geometry which a fixed-distance height
/// sample can miss.
/// </remarks>
public static class TerrainRayTrace
{
    private const int LeafSize = 2 * (TerrainOctree.RootExtent >> TerrainLeafQuery.SurfaceDepth);

    /// <summary>Casts a rich request at terrain geometry.</summary>
    public static bool TryTrace(TerrainData terrain, in WorldTraceRequest request, out WorldTraceHit hit)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        hit = default;
        if ((request.Mask & WorldTraceMask.Terrain) == 0)
        {
            return false;
        }

        Vector3d direction = new(
            request.End.X - request.Origin.X,
            request.End.Y - request.Origin.Y,
            request.End.Z - request.Origin.Z);
        double length = request.Length;
        if (length <= 0.0)
        {
            return false;
        }

        int cellX = Cell(request.Origin.X);
        int cellY = Cell(request.Origin.Y);
        int cellZ = Cell(request.Origin.Z);
        int endX = Cell(request.End.X);
        int endY = Cell(request.End.Y);
        int endZ = Cell(request.End.Z);

        AxisStepper x = AxisStepper.Create(request.Origin.X, direction.X, cellX);
        AxisStepper y = AxisStepper.Create(request.Origin.Y, direction.Y, cellY);
        AxisStepper z = AxisStepper.Create(request.Origin.Z, direction.Z, cellZ);

        double nearest = double.PositiveInfinity;
        WorldRayHit nearestHit = default;
        bool found = false;
        HashSet<(int Node, int X, int Y, int Z)> visited = [];

        // A 3-D DDA visits exactly 1 + the crossed grid planes. The extra allowance handles a segment
        // starting on a plane without risking an unbounded loop on damaged/extreme input.
        long required = 4L
            + Math.Abs((long)endX - cellX)
            + Math.Abs((long)endY - cellY)
            + Math.Abs((long)endZ - cellZ);
        int budget = (int)Math.Min(required, 1_000_000L);

        for (int visit = 0; visit < budget; visit++)
        {
            Vector3i point = new(CellCentre(cellX), CellCentre(cellY), CellCentre(cellZ));
            if (TerrainLeafQuery.TryFindLeaf(terrain, point, out TerrainLeaf leaf)
                && visited.Add((leaf.Cursor.NodeIndex, leaf.Origin.X, leaf.Origin.Y, leaf.Origin.Z)))
            {
                TestLeaf(leaf, request.Origin, direction, length, ref nearest, ref nearestHit, ref found);
            }

            if (cellX == endX && cellY == endY && cellZ == endZ)
            {
                break;
            }

            double next = Math.Min(x.Next, Math.Min(y.Next, z.Next));
            if (next > 1.0)
            {
                break;
            }

            // Advance every tied axis: a ray through an exact grid edge enters the diagonally adjacent
            // cell, not either zero-width neighbour on its sides.
            if (x.Next <= next)
            {
                cellX += x.Step;
                x = x.Advance();
            }

            if (y.Next <= next)
            {
                cellY += y.Step;
                y = y.Advance();
            }

            if (z.Next <= next)
            {
                cellZ += z.Step;
                z = z.Advance();
            }
        }

        if (!found)
        {
            return false;
        }

        nearestHit = nearestHit with { Material = GameMaterialOf(terrain) };
        hit = WorldTraceHit.FromRayHit(request, nearestHit, WorldTraceHitKind.Terrain, label: "terrain");
        return true;
    }

    /// <summary>The game material row a hit on this terrain reports.</summary>
    /// <param name="terrain">The level's terrain.</param>
    /// <returns>The row to look up in the game material table.</returns>
    /// <remarks>
    /// <para>
    /// <c>0x4CB050</c> fills the trace's entry and exit material at <c>0x4CB7C3</c> from
    /// <c>scene[0xD3C + 4·index]</c> — the same per-level array <c>0x4C1840</c> indexes for a mesh face.
    /// The index comes from <c>0x4CB810</c>: the low byte of the <c>CreateTerrainMaterial</c> record's
    /// first field (<c>C28FE0</c>, argument 1, stored by <c>0x4C6670</c>) for the texture modifier that
    /// covers the point, or for terrain material 0 when none does.
    /// </para>
    /// <para>
    /// So the answer is the level's own, not a constant: <c>Ground</c> in most of the campaign and
    /// <c>Snow</c> in the four Arctic missions. See <see cref="FootstepSounds.TerrainGameMaterialIndex"/>
    /// for why one row answers for the whole map. A terrain that declares no material takes row 1, the
    /// entry <c>0x4C1840</c> falls back to (<c>scene+3392</c>).
    /// </para>
    /// </remarks>
    public static int GameMaterialOf(TerrainData terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        int declared = FootstepSounds.TerrainGameMaterialIndex(terrain.Materials)
            ?? MefCollisionMesh.DefaultGlobalMaterial;
        return declared & 0xFF;
    }

    /// <summary>Returns a trace delegate bound to one terrain.</summary>
    public static WorldTraceCast Caster(TerrainData terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        return (in WorldTraceRequest request, out WorldTraceHit hit) => TryTrace(terrain, request, out hit);
    }

    private static void TestLeaf(
        in TerrainLeaf leaf,
        Vector3d origin,
        Vector3d direction,
        double length,
        ref double nearest,
        ref WorldRayHit nearestHit,
        ref bool found)
    {
        TerrainCommandBlock block = leaf.Block;
        Vector3d[] vertices = new Vector3d[block.VertexCount];
        for (int i = 0; i < vertices.Length; i++)
        {
            TerrainPackedVertex packed = block.Vertex(i);
            (int x, int y) = TerrainOrientation.Transform(leaf.Cursor.Orientation, packed.X, packed.Y);
            double worldX = leaf.Origin.X + (x * leaf.UnitScale);
            double worldY = leaf.Origin.Y + (y * leaf.UnitScale);
            double worldZ = leaf.Origin.Z + (packed.Z * leaf.UnitScale);

            // 0x4C9020 adds the bound heightmap's sample exactly as the ground query does.
            if (leaf.Heights.IsBound)
            {
                worldZ += leaf.Heights.Sample(worldX, worldY);
            }

            vertices[i] = new Vector3d(worldX, worldY, worldZ);
        }

        for (int i = 0; i < block.TriangleCount; i++)
        {
            TerrainTriangle triangle = block.Triangle(i);
            if (triangle.I0 >= vertices.Length || triangle.I1 >= vertices.Length || triangle.I2 >= vertices.Length)
            {
                continue;
            }

            Vector3d a = vertices[triangle.I0];
            Vector3d b = vertices[triangle.I1];
            Vector3d c = vertices[triangle.I2];
            if (!RayTriangle.Intersect(origin, direction, a, b, c, out double fraction)
                || fraction >= nearest)
            {
                continue;
            }

            Vector3d normal = RayTriangle.Normal(a, b, c);
            if (normal is { X: 0.0, Y: 0.0, Z: 0.0 })
            {
                continue;
            }

            if (((normal.X * direction.X) + (normal.Y * direction.Y) + (normal.Z * direction.Z)) > 0.0)
            {
                normal = new Vector3d(-normal.X, -normal.Y, -normal.Z);
            }

            nearest = fraction;
            found = true;
            nearestHit = new WorldRayHit(
                new Vector3d(
                    origin.X + (direction.X * fraction),
                    origin.Y + (direction.Y * fraction),
                    origin.Z + (direction.Z * fraction)),
                new Vector3f((float)normal.X, (float)normal.Y, (float)normal.Z),
                length * fraction);
        }
    }

    private static int Cell(double coordinate) => (int)Math.Floor(coordinate / LeafSize);

    private static int CellCentre(int cell)
    {
        long centre = ((long)cell * LeafSize) + (LeafSize / 2L);
        return (int)Math.Clamp(centre, int.MinValue, int.MaxValue);
    }

    private readonly record struct AxisStepper(int Step, double Next, double Delta)
    {
        public static AxisStepper Create(double origin, double direction, int cell)
        {
            if (direction > 0.0)
            {
                double boundary = ((long)cell + 1L) * LeafSize;
                return new AxisStepper(1, (boundary - origin) / direction, LeafSize / direction);
            }

            if (direction < 0.0)
            {
                double boundary = (long)cell * LeafSize;
                return new AxisStepper(-1, (boundary - origin) / direction, -LeafSize / direction);
            }

            return new AxisStepper(0, double.PositiveInfinity, double.PositiveInfinity);
        }

        public AxisStepper Advance() => this with { Next = Next + Delta };
    }
}
