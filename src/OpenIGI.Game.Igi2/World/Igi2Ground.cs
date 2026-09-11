using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// The ground an I.G.I. 2 level puts under a body: the height field its tiles describe, sampled the way
/// the original samples it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole query is <c>Terrain_VertexFunction</c> (<c>igi2 0x5706F0</c>).</b> It picks the tile,
/// interpolates the four samples around the point over one of the cell's two triangles, and returns the
/// height <em>relative to the level's terrain</em> together with that triangle's normal.
/// <c>Terrain_GetNextZHandler</c> (<c>0x572E80</c>) — the handler every ground query in the game reaches
/// through — then adds the <c>Terrain</c> task's own Z and keeps the surface only when it falls between
/// the two ends of the probe.
/// </para>
/// <para>
/// <b>The normal is the triangle's, not a smoothed one.</b> The original derives it from the same two
/// differences the height came from, so the surface a body slides on is exactly the surface it stands
/// on. (<c>TerrainMap_GetNormal</c> takes a central difference over neighbouring samples instead, but
/// that one belongs to the lightmap bake and nothing else calls it.)
/// </para>
/// <para>
/// <b>A <see cref="DiscardTask"/> removes the ground here too.</b> <c>Terrain_GetNextZHandler</c> tests
/// the point against <c>DiscardTask_IsDiscardVertex</c> (<c>igi2ed 0x65A670</c>) before it accepts a
/// surface, so a body over one of the level's discard boxes gets no terrain at all and falls to
/// whatever collision mesh was built in the hole. The renderer applies the same boxes, which is what
/// keeps the surface a body stands on the surface it can see.
/// </para>
/// <para>
/// <b>Placed objects are not in here.</b> The body's own query already casts at the world's collision
/// meshes and keeps whichever surface is higher, exactly as it does for the first game, so a crate or a
/// building floor still wins over the ground beneath it.
/// </para>
/// </remarks>
public sealed class Igi2Ground : IGroundHeightField
{
    private readonly IReadOnlyList<Igi2TerrainTile> _tiles;
    private readonly Igi2TerrainDiscard[] _discards;

    /// <summary>Initializes a new instance of the <see cref="Igi2Ground"/> class.</summary>
    /// <param name="surface">The level's loaded tiles.</param>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
    public Igi2Ground(Igi2TerrainSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _tiles = surface.Tiles;
        _discards = surface.Discards;
    }

    /// <summary>Gets a value indicating whether this level has any ground to stand on.</summary>
    public bool IsEmpty => _tiles.Count == 0;

    /// <inheritdoc/>
    public bool TryGetGround(double x, double y, double z, double aboveUnits, out TerrainGround ground)
    {
        ground = default;
        if (!TrySample(x, y, out TerrainGround surface))
        {
            return false;
        }

        // 0x572E93: the surface counts only when it lies between the two ends of the probe. A body a
        // long way above the ground is not standing on it, and one already under it is not either.
        if (surface.Height > z + aboveUnits || surface.Height < z - TerrainHeightQuery.SearchDepthUnits)
        {
            return false;
        }

        ground = surface;
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A height field makes the swept test exact rather than approximate: the body is above the surface
    /// at one end and below it at the other, so the crossing is where the gap changes sign. Solved by
    /// interpolation along the segment, which is the same answer a triangle sweep would give on ground
    /// this smooth and needs no traversal at all.
    /// </remarks>
    public bool TryTraceDescent(Vector3d from, Vector3d target, out TerrainGround ground)
    {
        ground = default;
        if (target.Z >= from.Z)
        {
            return false;
        }

        if (!TrySample(from.X, from.Y, out TerrainGround above) || from.Z < above.Height)
        {
            return false;
        }

        if (!TrySample(target.X, target.Y, out TerrainGround below) || target.Z > below.Height)
        {
            return false;
        }

        // Where the gap closes. Both gaps are measured the same way round, so the fraction is between
        // zero and one whenever the signs differ, and the normal is the one at the far end -- the
        // surface the body has arrived on rather than the one it left.
        double gapAtStart = from.Z - above.Height;
        double gapAtEnd = target.Z - below.Height;
        double span = gapAtStart - gapAtEnd;
        double fraction = span > 0.0 ? gapAtStart / span : 0.0;

        double height = from.Z + ((target.Z - from.Z) * Math.Clamp(fraction, 0.0, 1.0));
        ground = new TerrainGround(height, below.NormalX, below.NormalY, below.NormalZ);
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"I.G.I. 2 ground over {_tiles.Count} tile(s), {_discards.Length} discard(s)";

    // Terrain_VertexFunction, 0x5706F0: the tile, the cell, the triangle, the height and the normal.
    private bool TrySample(double x, double y, out TerrainGround ground)
    {
        ground = default;
        if (_tiles.Count == 0)
        {
            return false;
        }

        // 0x62E9E3: the discard is tested before the surface is kept, and on X and Y alone. A point
        // inside a box has no terrain under it at any height.
        if (Igi2TerrainDiscard.AnyContains(_discards, x, y))
        {
            return false;
        }

        Igi2TerrainTile tile = Select(x, y);

        // 0x570790: the sample index, clamped to the grid. Clamping rather than refusing is the
        // original's own edge behaviour -- the outer half texel reads the edge sample flat.
        double column = Math.Clamp(tile.ColumnAt(x), 0.0, tile.Width - 1);
        double row = Math.Clamp(tile.RowAt(y), 0.0, tile.Height - 1);

        int x0 = (int)column;
        int y0 = (int)row;
        int x1 = Math.Min(tile.Width - 1, x0 + 1);
        int y1 = Math.Min(tile.Height - 1, y0 + 1);

        double u = column - x0;
        double v = row - y0;

        double h00 = tile.HeightAt(x0, y0);
        double h10 = tile.HeightAt(x1, y0);
        double h01 = tile.HeightAt(x0, y1);
        double h11 = tile.HeightAt(x1, y1);

        double height;
        double slopeX;
        double slopeY;

        if (1.0 - u < v)
        {
            // 0x570926: the far triangle, hung off the (x1, y1) corner.
            height = ((1.0 - v) * (h10 - h11)) + ((h01 - h11) * (1.0 - u)) + h11;
            slopeX = (h01 - h11) / tile.TexelWidth;
            slopeY = (h10 - h11) / tile.TexelHeight;
        }
        else
        {
            // 0x570966: the near triangle, hung off the (x0, y0) corner.
            height = (u * (h10 - h00)) + ((h01 - h00) * v) + h00;
            slopeX = -(h10 - h00) / tile.TexelWidth;
            slopeY = -(h01 - h00) / tile.TexelHeight;
        }

        double length = Math.Sqrt((slopeX * slopeX) + (slopeY * slopeY) + 1.0);
        ground = new TerrainGround(height, slopeX / length, slopeY / length, 1.0 / length);
        return true;
    }

    // 0x570750: backwards through the tiles, first containing one wins, and tile 0 answers for
    // everything else -- it is the level's whole ground and the finer tiles are patches laid on it.
    private Igi2TerrainTile Select(double x, double y)
    {
        for (int i = _tiles.Count - 1; i > 0; i--)
        {
            if (_tiles[i].CoversPoint(x, y))
            {
                return _tiles[i];
            }
        }

        return _tiles[0];
    }
}
