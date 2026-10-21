using System.Numerics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>The lowest and highest height over every power-of-two block of a tile.</summary>
/// <remarks>
/// <para>
/// <c>TerrainMap_AllocateMinMaxHeightMipMaps</c> (<c>igi2ed 0x6296B0</c>) allocates
/// <c>floor(log2(max(nWidth, nHeight))) + 1</c> levels and <c>8 * nWidth * nHeight</c> bytes for the
/// finest — two floats a sample, a minimum and a maximum — halving both extents each level up.
/// <c>TerrainRender_IsSquareOutOfSight</c> then reads the one cell that covers the square it is testing
/// and uses the pair as the box's Z extent.
/// </para>
/// <para>
/// Levels are indexed here by the base-two logarithm of the block they cover, which is the opposite
/// direction from the original's <c>nLevels - log2(size) - 1</c> but names the same cell. The original
/// also refuses the pyramid for its two finest levels and falls back to a per-level maximum deviation
/// (<c>TerrainMap_t::avDeltaZ</c>, reached at <c>0x66AF9E</c>); this uses the pyramid all the way down,
/// which is strictly tighter and costs nothing once the pyramid exists.
/// </para>
/// </remarks>
public sealed class Igi2TerrainHeightPyramid
{
    private readonly float[][] _minimum;
    private readonly float[][] _maximum;
    private readonly int[] _pitch;

    private Igi2TerrainHeightPyramid(float[][] minimum, float[][] maximum, int[] pitch)
    {
        _minimum = minimum;
        _maximum = maximum;
        _pitch = pitch;
    }

    /// <summary>Gets how many levels the pyramid has; level <c>L</c> covers blocks of <c>2^L</c> samples.</summary>
    public int LevelCount => _pitch.Length;

    /// <summary>Builds the pyramid for one tile.</summary>
    /// <param name="tile">The tile.</param>
    /// <returns>The pyramid.</returns>
    public static Igi2TerrainHeightPyramid Build(Igi2TerrainTile tile)
    {
        int extent = Math.Max(tile.Width, tile.Height);
        int levels = 1;
        while (1 << levels <= extent)
        {
            levels++;
        }

        float[][] minimum = new float[levels][];
        float[][] maximum = new float[levels][];
        int[] pitch = new int[levels];

        int width = tile.Width;
        int height = tile.Height;
        pitch[0] = width;
        minimum[0] = new float[width * height];
        maximum[0] = new float[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float z = (float)tile.HeightAt(x, y);
                minimum[0][(y * width) + x] = z;
                maximum[0][(y * width) + x] = z;
            }
        }

        for (int level = 1; level < levels; level++)
        {
            int coarseWidth = Math.Max(1, (width + 1) / 2);
            int coarseHeight = Math.Max(1, (height + 1) / 2);
            pitch[level] = coarseWidth;
            minimum[level] = new float[coarseWidth * coarseHeight];
            maximum[level] = new float[coarseWidth * coarseHeight];

            for (int y = 0; y < coarseHeight; y++)
            {
                for (int x = 0; x < coarseWidth; x++)
                {
                    float low = float.PositiveInfinity;
                    float high = float.NegativeInfinity;
                    for (int dy = 0; dy < 2; dy++)
                    {
                        for (int dx = 0; dx < 2; dx++)
                        {
                            int sx = (x * 2) + dx;
                            int sy = (y * 2) + dy;
                            if (sx >= width || sy >= height)
                            {
                                continue;
                            }

                            low = MathF.Min(low, minimum[level - 1][(sy * pitch[level - 1]) + sx]);
                            high = MathF.Max(high, maximum[level - 1][(sy * pitch[level - 1]) + sx]);
                        }
                    }

                    minimum[level][(y * coarseWidth) + x] = low;
                    maximum[level][(y * coarseWidth) + x] = high;
                }
            }

            width = coarseWidth;
            height = coarseHeight;
        }

        return new Igi2TerrainHeightPyramid(minimum, maximum, pitch);
    }

    /// <summary>Gets the height range over one block.</summary>
    /// <param name="level">The level; the block is <c>2^level</c> samples across.</param>
    /// <param name="blockX">The block's column at that level.</param>
    /// <param name="blockY">The block's row at that level.</param>
    /// <param name="minimum">Receives the lowest height in the block.</param>
    /// <param name="maximum">Receives the highest.</param>
    /// <remarks>
    /// A block index past the tile is clamped rather than refused. The quadtree's root spans one sample
    /// more than the tile in each axis — see <see cref="Igi2TerrainLod"/> — so the last block along an
    /// edge is genuinely asked for and its neighbour's range is the closest true answer available.
    /// </remarks>
    public void GetBounds(int level, int blockX, int blockY, out float minimum, out float maximum)
    {
        int clamped = Math.Clamp(level, 0, _pitch.Length - 1);
        int pitch = _pitch[clamped];
        int rows = _minimum[clamped].Length / pitch;
        int index = (Math.Clamp(blockY, 0, rows - 1) * pitch) + Math.Clamp(blockX, 0, pitch - 1);

        minimum = _minimum[clamped][index];
        maximum = _maximum[clamped][index];
    }
}
