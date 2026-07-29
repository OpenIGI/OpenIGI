using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>The ground under a point.</summary>
/// <param name="Height">The surface height in world units.</param>
/// <param name="NormalX">The surface's unit normal, X.</param>
/// <param name="NormalY">The surface's unit normal, Y.</param>
/// <param name="NormalZ">The surface's unit normal, Z — positive on ground you can stand on.</param>
public readonly record struct TerrainGround(double Height, double NormalX, double NormalY, double NormalZ);

/// <summary>
/// Height-at-a-point: the query the player's feet stand on and the one nothing in the port could do
/// before.
/// </summary>
/// <remarks>
/// <para>
/// The whole chain in one call — descend the octree (<see cref="TerrainPointDescent"/>), reach the leaf
/// (<see cref="TerrainLeafQuery"/>), and interpolate the triangle
/// (<see cref="TerrainSurfaceTest"/>) — which together are the terrain half of <c>0x4CBE40</c>.
/// </para>
/// <para>
/// <b>Terrain only.</b> The original's downward cast also sweeps placed models (<c>0x4CDB20</c>), so a
/// point standing on a crate reads the ground beneath the crate here. That branch is still unread.
/// </para>
/// </remarks>
public static class TerrainHeightQuery
{
    /// <summary>How far below a query point the ground is looked for, in world units.</summary>
    /// <remarks>
    /// <para>
    /// <c>16385</c> — the offset <c>0x4CB050</c> puts the far end of its segment at. That is just over
    /// <b>four metres</b>, which is short: this is a query for a body already near the ground, not a
    /// probe from arbitrary height. The human's own step-down budgets are 0, 2048 and 8192 units, all
    /// well inside it.
    /// </para>
    /// <para>
    /// Not to be confused with the <c>16387</c> the batched cast pads its <i>bounding box</i> by when
    /// choosing a descent depth; that is a different number for a different purpose.
    /// </para>
    /// </remarks>
    public const double SearchDepthUnits = 16385.0;

    /// <summary>Finds the ground under a point.</summary>
    /// <param name="terrain">The level's terrain.</param>
    /// <param name="x">The point's X, in world units biased by the level origin.</param>
    /// <param name="y">The point's Y, likewise.</param>
    /// <param name="z">The point's Z — the height to search down from.</param>
    /// <param name="ground">Receives the surface when one is found.</param>
    /// <returns><see langword="false"/> when nothing is under the point within the search depth.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="terrain"/> is <see langword="null"/>.</exception>
    public static bool TryGetGround(TerrainData terrain, double x, double y, double z, out TerrainGround ground)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ground = default;

        // The descent is three-dimensional, so a query from well above the ground lands in an empty
        // node with nothing in it. That is what the original's Z walk exists for: it descends, tests,
        // and on a miss steps to the Z-adjacent node and tries again. The search depth spans less than
        // one leaf, so the column is at most two of them.
        double bottom = z - SearchDepthUnits;

        int leafHeight = 2 * (TerrainOctree.RootExtent >> TerrainLeafQuery.SurfaceDepth);
        double probeZ = z;

        while (true)
        {
            Vector3i point = new((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(probeZ));

            if (TerrainLeafQuery.TryFindLeaf(terrain, point, out TerrainLeaf leaf))
            {
                double localX = x - leaf.Origin.X;
                double localY = y - leaf.Origin.Y;

                // The far end of the search, restated in the leaf's frame and carrying the original's
                // tenth-of-a-unit nudge.
                double limitZ = bottom - leaf.Origin.Z - TerrainSurfaceTest.EndNudge;

                if (TerrainSurfaceTest.TryFindSurface(
                        leaf, localX, localY, limitZ, downward: true, out TerrainSurfaceHit hit))
                {
                    double height = hit.Height + leaf.Origin.Z;
                    if (height <= z)
                    {
                        ground = new TerrainGround(height, hit.NormalX, hit.NormalY, hit.NormalZ);
                        return true;
                    }
                }
            }

            if (probeZ <= bottom)
            {
                return false;
            }

            probeZ = Math.Max(bottom, probeZ - leafHeight);
        }
    }
}
