using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// One <c>TextureModifier</c> from a level's script, resolved into the record the terrain renderer reads:
/// a square of the world, a bit mask over it, and the ground material the set bits select.
/// </summary>
/// <remarks>
/// <para>
/// This is the run-time table at <c>0xBA5050</c>, which <c>sub_5062A0</c> fills from a
/// <c>TextureModifier</c> task. The task's five parameters map onto it directly:
/// </para>
/// <list type="table">
/// <item><term><c>Position</c></term><description>the centre of the square the mask covers.</description></item>
/// <item><term><c>Level</c></term><description>an octree level, so the square is <c>2^30 &gt;&gt; Level</c> either side of the centre.</description></item>
/// <item><term><c>Bitmap ID</c></term><description>a record of <c>terrain.bit</c>.</description></item>
/// <item><term><c>Size</c></term><description>that record's cells per edge; the two agree in all nine of level 1's modifiers.</description></item>
/// <item><term><c>Material Index</c></term><description>the terrain material a set bit selects.</description></item>
/// </list>
/// <para>
/// <b>A deliberate divergence.</b> The original activates a modifier over a <em>different</em> square
/// from the one it samples — the activation box is the position snapped down to a whole cell, while the
/// mask is centred on the unsnapped position — so near an edge it indexes past the end of the mask and
/// reads whatever follows it. Here a sample outside the mask simply does not match, and the next layer
/// (or the base material) applies. The two squares coincide only when a modifier sits at the centre of
/// its cell, and none of level 1's does.
/// </para>
/// </remarks>
public sealed class TerrainTileLayer
{
    private TerrainTileLayer(
        int materialId,
        int level,
        int size,
        double originX,
        double originY,
        double scale,
        TerrainVisibilityGrid mask)
    {
        MaterialId = materialId;
        Level = level;
        Size = size;
        OriginX = originX;
        OriginY = originY;
        Scale = scale;
        Mask = mask;
    }

    /// <summary>Gets the terrain material a set bit selects.</summary>
    public int MaterialId { get; }

    /// <summary>Gets the octree level the square's size comes from; higher is smaller and wins.</summary>
    public int Level { get; }

    /// <summary>Gets the mask's cells per edge.</summary>
    public int Size { get; }

    /// <summary>Gets the world X of the mask's first column, in engine units.</summary>
    public double OriginX { get; }

    /// <summary>Gets the world Y of the mask's first row, in engine units.</summary>
    public double OriginY { get; }

    /// <summary>Gets mask cells per engine unit.</summary>
    public double Scale { get; }

    /// <summary>Gets the bit mask.</summary>
    public TerrainVisibilityGrid Mask { get; }

    /// <summary>Gets the square's half-width in engine units.</summary>
    public int Extent => TerrainOctree.RootExtent >> Level;

    /// <summary>Builds a layer from a <c>TextureModifier</c>'s parameters and the level's bit masks.</summary>
    /// <param name="position">The task's position, in engine units.</param>
    /// <param name="level">The task's <c>Level</c>.</param>
    /// <param name="materialId">The task's <c>Material Index</c>.</param>
    /// <param name="bitmapId">The task's <c>Bitmap ID</c>.</param>
    /// <param name="visibility">The level's <c>terrain.bit</c>.</param>
    /// <param name="layer">Receives the layer.</param>
    /// <returns><see langword="false"/> when the bitmap id carries no mask, or the level is out of range.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="visibility"/> is <see langword="null"/>.</exception>
    public static bool TryCreate(
        Vector3d position,
        int level,
        int materialId,
        int bitmapId,
        TerrainVisibility visibility,
        out TerrainTileLayer? layer)
    {
        ArgumentNullException.ThrowIfNull(visibility);

        layer = null;
        if (level is < 0 or > 30
            || bitmapId is < 0 or >= TerrainGridTable.RecordCount
            || !visibility.TryGetGrid(bitmapId, out TerrainVisibilityGrid mask))
        {
            return false;
        }

        int size = mask.Size;
        if (size <= 0 || !int.IsPow2(size))
        {
            return false;
        }

        int extent = TerrainOctree.RootExtent >> level;

        // The mask's n cells span the square of 2 * extent, and the last cell's samples are pulled
        // inside by (n - 1) / n so that the far edge does not fall off the end.
        int scaleExponent = 31 - level - int.Log2(size);
        if (scaleExponent is < 0 or > 62)
        {
            return false;
        }

        double scale = (size - 1.0) / size / (1L << scaleExponent);

        layer = new TerrainTileLayer(
            materialId,
            level,
            size,
            position.X - extent,
            position.Y - extent,
            scale,
            mask);
        return true;
    }

    /// <summary>Tests whether this layer covers a world position.</summary>
    /// <param name="worldX">The world X in engine units.</param>
    /// <param name="worldY">The world Y in engine units.</param>
    /// <returns><see langword="true"/> when the position falls on a set cell of the mask.</returns>
    public bool Covers(double worldX, double worldY)
    {
        double cellX = (worldX - OriginX) * Scale;
        double cellY = (worldY - OriginY) * Scale;

        // Truncation towards zero would fold the cell either side of the origin onto each other, so
        // anything negative is outside rather than in cell zero.
        return cellX >= 0.0 && cellY >= 0.0 && Mask.IsSet((int)cellX, (int)cellY);
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"tile layer: material {MaterialId}, {Size}x{Size} mask at level {Level}";
}
