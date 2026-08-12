using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// The leaf a collision query lands on, and the frame its geometry is expressed in.
/// </summary>
/// <param name="Cursor">Where the descent stopped.</param>
/// <param name="Block">The leaf's triangles and packed vertices.</param>
/// <param name="Origin">
/// The query point restated relative to the leaf's own corner, which is the frame the surface test
/// works in.
/// </param>
/// <param name="UnitScale">
/// What one unit of a packed vertex coordinate is worth here — the leaf's extent over
/// <see cref="TerrainPackedVertex.UnitsPerExtent"/>.
/// </param>
public readonly record struct TerrainLeaf(
    TerrainDescentCursor Cursor,
    TerrainCommandBlock Block,
    Vector3i Origin,
    double UnitScale);

/// <summary>
/// Takes a collision query from a world point down to the leaf whose geometry answers it.
/// </summary>
/// <remarks>
/// <para>
/// The front of <c>0x4CB050</c>: descend (<see cref="TerrainPointDescent"/>), then restate the query
/// point in the leaf's own frame before handing it to the surface test at <c>0x4CA010</c>.
/// </para>
/// <para>
/// <b>The geometry is the geometry the renderer draws.</b> The surface test unpacks its vertices with
/// the same three 6-bit fields biased by 24 that <see cref="TerrainPackedVertex"/> already decodes, at
/// the same <c>extent / 24</c> scale, and applies the orientation to X and Y only — never Z — exactly
/// as <see cref="TerrainOrientation.Transform"/> does. Together with the shared octree
/// (<c>collision.md</c> §2) that means a collision query and a drawn triangle read one set of numbers.
/// </para>
/// </remarks>
public static class TerrainLeafQuery
{
    /// <summary>The depth a terrain surface query descends to.</summary>
    /// <remarks>
    /// Sixteen — the cap, and the depth at which a leaf spans <c>0x40000000 &gt;&gt; 16</c> either side
    /// of its centre. The mask the original applies to find the leaf corner is <c>0xFFFF8000</c>, which
    /// is that same span read as a grid step of 32768.
    /// </remarks>
    public const int SurfaceDepth = 16;

    /// <summary>The mask that snaps a world coordinate to its leaf's corner.</summary>
    /// <remarks><c>0xFFFF8000</c> — a grid of 32768, which is twice the leaf half-extent.</remarks>
    public const int LeafCornerMask = unchecked((int)0xFFFF8000);

    /// <summary>Finds the leaf holding a point, and the point's position within it.</summary>
    /// <param name="terrain">The level's terrain.</param>
    /// <param name="point">The query point, already biased by the level's world origin.</param>
    /// <param name="leaf">Receives the leaf when one carries geometry.</param>
    /// <returns>
    /// <see langword="false"/> when the descent lands somewhere with no geometry — open sky or a part
    /// of the world the octree does not author, which is not an error.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="terrain"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The original restates X and Y against the <i>corner</i> of the leaf and Z against its
    /// <i>centre</i> — the two axes the octree subdivides on a grid, and the one it does not.
    /// </remarks>
    public static bool TryFindLeaf(TerrainData terrain, Vector3i point, out TerrainLeaf leaf)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        leaf = default;

        TerrainDescentCursor cursor = TerrainPointDescent.Descend(terrain.Octree, point, SurfaceDepth);
        if (cursor.Depth < SurfaceDepth
            || TerrainDiscardBinding.For(terrain).Discards(point.X, point.Y, point.Z))
        {
            // The tree stopped short or the level's task tree removed this cube, so nothing here carries
            // collision geometry. Retail applies DiscardTerrain to batched point queries in sub_52BF00,
            // clearing each point's result flag when it lies inside the task's strict 3-D bounds.
            return false;
        }

        TerrainNode node = terrain.Octree[cursor.NodeIndex];
        if (!terrain.Commands.TryGetBlock(node.CommandOffset, out TerrainCommandBlock block) || block.IsEmpty)
        {
            return false;
        }

        Vector3i origin = new(
            cursor.Extent + (point.X & LeafCornerMask),
            cursor.Extent + (point.Y & LeafCornerMask),
            cursor.Centre.Z);

        leaf = new TerrainLeaf(cursor, block, origin, UnitScaleFor(cursor.Extent));
        return true;
    }

    /// <summary>What one unit of a packed vertex coordinate is worth at a given extent.</summary>
    /// <param name="extent">The leaf's half-extent.</param>
    /// <returns>The scale in world units.</returns>
    /// <remarks>
    /// The surface test spells the reciprocal as <c>0.04166666666666666</c>, which is a
    /// <see langword="double"/> one twenty-fourth — the same divisor
    /// <see cref="TerrainChunk.UnitScale"/> reaches by a different route, and the reason a packed
    /// coordinate of ±24 spans a leaf exactly.
    /// </remarks>
    public static double UnitScaleFor(int extent) => extent * (1.0 / TerrainPackedVertex.UnitsPerExtent);
}
