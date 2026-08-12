using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// One <c>DiscardTerrain</c> task resolved to the octree cube whose terrain the original suppresses.
/// </summary>
/// <remarks>
/// <para>
/// Retail <c>sub_52C290</c> snaps each task position down to a cube of twice
/// <c>0x40000000 &gt;&gt; Level</c>. During <c>sub_4D14E0</c>, the task's message-15 handler
/// (<c>sub_52C0B0</c>) sets <c>0xB12BB8</c>; the descent then clears that cube's terrain-child mask.
/// Its <c>DiscardTerrainArea</c> proxy keeps the descent resolving until this level, so a coarse parent
/// cannot cover the hole from a distance.
/// </para>
/// <para>
/// The bounds are three-dimensional. That matters where a level stacks discard cubes vertically around
/// a building: a cube only removes the terrain node occupying the same X, Y, and Z octant.
/// </para>
/// </remarks>
public sealed class TerrainDiscardRegion
{
    private TerrainDiscardRegion(int level, long minX, long minY, long minZ, long span)
    {
        Level = level;
        MinX = minX;
        MinY = minY;
        MinZ = minZ;
        MaxX = minX + span;
        MaxY = minY + span;
        MaxZ = minZ + span;
    }

    /// <summary>Gets the octree level of the discarded cube.</summary>
    public int Level { get; }

    /// <summary>Gets the inclusive lower X bound, in engine units.</summary>
    public long MinX { get; }

    /// <summary>Gets the inclusive lower Y bound, in engine units.</summary>
    public long MinY { get; }

    /// <summary>Gets the inclusive lower Z bound, in engine units.</summary>
    public long MinZ { get; }

    /// <summary>Gets the exclusive upper X bound, in engine units.</summary>
    public long MaxX { get; }

    /// <summary>Gets the exclusive upper Y bound, in engine units.</summary>
    public long MaxY { get; }

    /// <summary>Gets the exclusive upper Z bound, in engine units.</summary>
    public long MaxZ { get; }

    /// <summary>Gets the cube's half-width, in engine units.</summary>
    public int Extent => TerrainOctree.RootExtent >> Level;

    /// <summary>Builds a discard cube from the task's authored position and level.</summary>
    /// <param name="position">The task position, in engine units.</param>
    /// <param name="level">The task's <c>Level</c>.</param>
    /// <param name="region">Receives the snapped region.</param>
    /// <returns><see langword="false"/> when the terrain descent cannot represent the level.</returns>
    public static bool TryCreate(Vector3d position, int level, out TerrainDiscardRegion? region)
    {
        region = null;
        if (level is < 0 or > TerrainTraversal.MaximumExponent)
        {
            return false;
        }

        long span = 2L * (TerrainOctree.RootExtent >> level);
        region = new TerrainDiscardRegion(
            level,
            (long)Math.Floor(position.X) & -span,
            (long)Math.Floor(position.Y) & -span,
            (long)Math.Floor(position.Z) & -span,
            span);
        return true;
    }

    /// <summary>Tests the strict point bounds used by retail's query handler <c>sub_52BF00</c>.</summary>
    public bool Contains(double x, double y, double z) =>
        x > MinX && x < MaxX
        && y > MinY && y < MaxY
        && z > MinZ && z < MaxZ;

    /// <summary>Tests whether an octree cube lies wholly inside this discarded cube.</summary>
    public bool ContainsCube(in Vector3d centre, int extent) =>
        centre.X - extent >= MinX && centre.X + extent <= MaxX
        && centre.Y - extent >= MinY && centre.Y + extent <= MaxY
        && centre.Z - extent >= MinZ && centre.Z + extent <= MaxZ;

    /// <summary>Tests whether an octree cube overlaps this region with positive volume.</summary>
    /// <remarks>Touching at a face is not overlap; adjacent authored octree cubes must remain independent.</remarks>
    public bool IntersectsCube(in Vector3d centre, int extent) =>
        centre.X - extent < MaxX && centre.X + extent > MinX
        && centre.Y - extent < MaxY && centre.Y + extent > MinY
        && centre.Z - extent < MaxZ && centre.Z + extent > MinZ;

    /// <inheritdoc/>
    public override string ToString() =>
        $"terrain discard at level {Level}: ({MinX}, {MinY}, {MinZ})..({MaxX}, {MaxY}, {MaxZ})";
}
