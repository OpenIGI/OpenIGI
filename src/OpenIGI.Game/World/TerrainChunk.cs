using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// One terrain chunk the traversal selected for drawing: an octree node, placed and at a chosen level of
/// detail.
/// </summary>
/// <remarks>
/// <para>
/// This is the 48-byte descriptor <c>sub_4D14E0</c> emits into <c>0xB16CE0</c>, minus the three fields
/// that name run-time bindings a level script publishes (the heightmap, the terrain lightmap and the tile
/// layers) and the hash-cache pointer, which is an allocation detail rather than data.
/// </para>
/// <para>
/// <see cref="Position"/> is in engine units and is <see cref="Vector3d"/> because the original stores it
/// as three doubles: it is accumulated from integer octant offsets over a descent of up to sixteen
/// levels through a cube of ±2^30 units, which is more precision than a <see cref="float"/> holds.
/// </para>
/// </remarks>
/// <param name="Position">The chunk's centre in engine units.</param>
/// <param name="LodExponent">
/// The level-of-detail exponent, which is also the node's depth below the root. Retail chunks are emitted
/// between <see cref="TerrainTraversal.MinimumExponent"/> and <see cref="TerrainTraversal.MaximumExponent"/>.
/// </param>
/// <param name="Orientation">The orientation the descent reached the node with; see <see cref="TerrainOrientation"/>.</param>
/// <param name="CommandOffset">The chunk's block in <c>terrain.cmd</c>.</param>
public readonly record struct TerrainChunk(
    Vector3d Position,
    int LodExponent,
    int Orientation,
    uint CommandOffset)
{
    /// <summary>Gets the chunk's half-width in engine units.</summary>
    public int Extent => TerrainOctree.RootExtent >> LodExponent;

    /// <summary>Gets the engine units one unit of a packed vertex coordinate is worth.</summary>
    /// <remarks>
    /// The original computes this as <c>(1 &lt;&lt; (27 - e)) / 3</c>, the divisor being a constant
    /// <see cref="double"/> at <c>0x54EBA0</c>. It is the same thing as
    /// <c><see cref="Extent"/> / <see cref="TerrainPackedVertex.UnitsPerExtent"/></c>, which is the check
    /// that a coordinate of ±24 spans the chunk exactly.
    /// </remarks>
    public double UnitScale => (1 << (27 - LodExponent)) / 3.0;

    /// <summary>Gets a value indicating whether the chunk's own vertices carry a heightmap displacement.</summary>
    /// <remarks>
    /// Below <see cref="TerrainTraversal.HeightmapExponent"/> the terrain is too coarse for the heightmap
    /// to mean anything and the original does not sample it. At the exponent itself it samples but keeps
    /// the result in a separate channel to cross-fade into the band above, and only past it does the
    /// height reach Z.
    /// </remarks>
    public bool AppliesHeightmap => LodExponent > TerrainTraversal.HeightmapExponent;

    /// <inheritdoc/>
    public override string ToString() =>
        $"chunk at {Position} (LOD {LodExponent}, extent {Extent}, orientation {Orientation})";
}
