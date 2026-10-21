using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.World;

/// <summary>What the ground is like at one point: <c>Terrain_VertexFunction</c>'s outputs.</summary>
/// <param name="RelativeZ">The height, relative to the terrain's own Z.</param>
/// <param name="Normal">The unit normal of the cell triangle the point is on.</param>
/// <param name="Material">The terrain material id under the point, a <c>TerrainMaterial</c>'s <c>ID</c>.</param>
/// <param name="Tile">Which tile answered, by its index in the level's list.</param>
/// <param name="Light">How lit the ground is there, from the tile's lightmap: zero to two, one being mid grey.</param>
public readonly record struct Igi2TerrainSample(float RelativeZ, Vector3f Normal, byte Material, int Tile, float Light);

/// <summary>
/// The ground at a point, <c>Terrain_VertexFunction</c> (<c>igi2 0x5706F0</c>), with the arithmetic the client's FPU
/// performs.
/// </summary>
/// <remarks>
/// <para>
/// The point is relative to the terrain, as single floats. The finest tile that holds it, half a texel in from
/// its edges, answers; the first tile answers for anything else. The point becomes a fractional sample index,
/// clamped to the grid, and the height is interpolated over whichever of the cell's two triangles holds it, with
/// that triangle's normal.
/// </para>
/// <para>
/// The material is the corner material when all four agree. Where three agree it splits the cell along a
/// diagonal; otherwise it is the nearest corner's. The light is the nearest lightmap texel's average channel,
/// scaled so that 255 reads as two. A tile with no lightmap file was given a white one when it loaded.
/// </para>
/// </remarks>
public static class Igi2TerrainVertex
{
    /// <summary>
    /// <c>0.0078431377f</c>, which is 1/127.5: a lightmap byte of 255 lights the ground at twice its colour.
    /// </summary>
    private const float LightScale = 0.0078431377f;

    private const float White = 255f;

    /// <summary>Samples the ground at a point relative to the terrain.</summary>
    /// <param name="tiles">The level's tiles, in declared order.</param>
    /// <param name="x">The X relative to the terrain, <c>vInX</c>.</param>
    /// <param name="y">The Y relative to the terrain, <c>vInY</c>.</param>
    /// <param name="sample">The ground there.</param>
    /// <returns><see langword="false"/> when the level has no tiles.</returns>
    public static bool TrySample(IReadOnlyList<Igi2TerrainTile> tiles, float x, float y, out Igi2TerrainSample sample)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        sample = default;
        if (tiles.Count == 0)
        {
            return false;
        }

        // Backwards through the tiles; the first holds everything the others do not.
        int index = tiles.Count - 1;
        Frame frame;
        while (true)
        {
            frame = new Frame(tiles[index]);
            if (index == 0 || frame.Holds(x, y))
            {
                break;
            }

            index--;
        }

        Igi2TerrainTile tile = tiles[index];
        Fpu inverseTexelWidth = (Fpu)1f / frame.TexelWidth;
        float inverseTexelHeight = ((Fpu)1f / frame.TexelHeight).F;
        float column = (((((Fpu)x - frame.CentreX) + frame.HalfWidth) * inverseTexelWidth) - 0.5f).F;
        Fpu row = ((((Fpu)y - frame.CentreY) + frame.HalfHeight) * inverseTexelHeight) - 0.5f;

        // Clamped to the grid; an unordered value is kept.
        if (column < 0f)
        {
            column = 0f;
        }
        else if (tile.Width - 1 < column)
        {
            column = tile.Width - 1;
        }

        if (row.Value < 0.0)
        {
            row = 0f;
        }
        else if (tile.Height - 1 < row.Value)
        {
            row = (float)(tile.Height - 1);
        }

        int x0 = Fpu.ToInt32(column);
        int y0 = Fpu.ToInt32(row);
        int x1 = Math.Min(tile.Width - 1, x0 + 1);
        int y1 = Math.Min(tile.Height - 1, y0 + 1);
        float u = ((Fpu)column - x0).F;
        float v = (row - y0).F;

        float h00 = tile.Heights.GetHeight(x0, y0);
        float h10 = tile.Heights.GetHeight(x1, y0);
        float h01 = tile.Heights.GetHeight(x0, y1);
        float h11 = tile.Heights.GetHeight(x1, y1);

        float height;
        Fpu slopeX;
        Fpu slopeY;
        float oneMinusU = ((Fpu)1f - u).F;
        if (!(oneMinusU >= v))
        {
            // The far triangle, hung off the (x1, y1) corner; an unordered fraction lands here too.
            float alongY = ((Fpu)h10 - h11).F;
            Fpu alongX = (Fpu)h01 - h11;
            height = ((((Fpu)1f - v) * alongY) + (alongX * oneMinusU) + h11).F;
            slopeX = alongX * inverseTexelWidth;
            slopeY = (Fpu)alongY * inverseTexelHeight;
        }
        else
        {
            // The near triangle, hung off the (x0, y0) corner.
            float alongY = ((Fpu)h01 - h00).F;
            Fpu alongX = (Fpu)h10 - h00;
            height = (((Fpu)u * alongX) + ((Fpu)alongY * v) + h00).F;
            slopeX = -(alongX * inverseTexelWidth);
            slopeY = -((Fpu)alongY * inverseTexelHeight);
        }

        Fpu inverseLength = (Fpu)1.0 / Fpu.Sqrt((slopeX * slopeX) + (slopeY * slopeY) + 1.0);
        Vector3f normal = new((slopeX * inverseLength).F, ((Fpu)slopeY.F * inverseLength).F, inverseLength.F);

        // The nearest corner, by rounding each fraction half up.
        int nearest = (2 * Fpu.ToInt32((Fpu)v + 0.5f)) + Fpu.ToInt32((Fpu)u + 0.5f);
        byte material = Material(tile, x0, y0, x1, y1, u, v, nearest);

        float light = White;
        if (tile.Lightmap is { } lightmap)
        {
            int cx = (nearest & 1) == 0 ? x0 : x1;
            int cy = nearest < 2 ? y0 : y1;
            Bgra32 texel = lightmap.GetLight(cx, cy);
            light = (texel.R + texel.G + texel.B) / 3;
        }

        sample = new Igi2TerrainSample(height, normal, material, index, ((Fpu)light * LightScale).F);
        return true;
    }

    /// <summary>The material under the point: the corners', split along a diagonal where three agree.</summary>
    private static byte Material(Igi2TerrainTile tile, int x0, int y0, int x1, int y1, float u, float v, int nearest)
    {
        if (tile.Materials is not { } map)
        {
            return 0;
        }

        byte m00 = map.GetMaterial(x0, y0), m10 = map.GetMaterial(x1, y0);
        byte m01 = map.GetMaterial(x0, y1), m11 = map.GetMaterial(x1, y1);
        if (m00 == m10 && m00 == m01 && m00 == m11)
        {
            return m00;
        }

        // Three corners agree: the odd one out owns the triangle beyond the diagonal a half cell from it. The
        // comparisons pass an unordered sum the way the original's flag tests do.
        if (m00 == m10 && m00 == m01)
        {
            return !(((Fpu)v + u).Value > 1.5) ? m00 : m11;
        }

        if (m10 == m01 && m10 == m11)
        {
            return ((Fpu)v + u).Value < 0.5 ? m00 : m10;
        }

        if (m01 == m00 && m01 == m11)
        {
            return !(((Fpu)u + 1f - v).Value > 1.5) ? m01 : m10;
        }

        if (m00 == m10 && m00 == m11)
        {
            return ((Fpu)u + 1f - v).Value < 0.5 ? m01 : m11;
        }

        return nearest switch { 0 => m00, 1 => m10, 2 => m01, _ => m11 };
    }

    /// <summary>One tile's placement as the query computes it, relative to the terrain and at the client's precision.</summary>
    private readonly struct Frame
    {
        public Frame(Igi2TerrainTile tile)
        {
            CentreX = ((Fpu)tile.Centre.X - tile.TerrainOrigin.X).F;
            CentreY = ((Fpu)tile.Centre.Y - tile.TerrainOrigin.Y).F;
            Fpu width = (Fpu)tile.WorldWidth * 4096f;
            Fpu height = (Fpu)tile.WorldHeight * 4096f;
            HalfWidth = (width * 0.5f).F;
            HalfHeight = ((Fpu)0.5f * height).F;
            TexelWidth = (width / (double)tile.Width).F;
            TexelHeight = (height / (double)tile.Height).F;
        }

        public float CentreX { get; }

        public float CentreY { get; }

        public float HalfWidth { get; }

        public float HalfHeight { get; }

        public float TexelWidth { get; }

        public float TexelHeight { get; }

        /// <summary>Whether the point is inside the tile, half a texel in from each edge; strictly, an unordered point is out.</summary>
        public bool Holds(float x, float y)
        {
            Fpu halfTexelX = (Fpu)TexelWidth * 0.5f;
            if (!((((Fpu)CentreX - HalfWidth) + halfTexelX).Value < x) || !(x < ((((Fpu)HalfWidth + CentreX) - halfTexelX).Value)))
            {
                return false;
            }

            Fpu halfTexelY = (Fpu)TexelHeight * 0.5f;
            return (((Fpu)CentreY - HalfHeight) + halfTexelY).Value < y && y < (((Fpu)HalfHeight + CentreY) - halfTexelY).Value;
        }
    }
}
