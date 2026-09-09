using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ilff;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The ground an I.G.I. 2 level declares, loaded and placed in the world.</summary>
public sealed class RetailIgi2TerrainSurfaceTests
{
    /// <summary>Every level's declared tiles load, with their three channels agreeing.</summary>
    /// <remarks>
    /// The level is the authority: 46 tiles are declared across the 24 levels against 50 shipped map
    /// sets, and the four spare belong to no level. Walking the archive instead would put two tiles into
    /// <c>location3/LEVEL4</c>, which declares no terrain at all.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_declared_tile_loads_with_its_three_channels()
    {
        int levels = 0;
        int tiles = 0;
        int materials = 0;
        int withMaterialMap = 0;
        int withLightmap = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            levels++;
            Igi2TerrainSurface surface = Surface(level);
            materials += surface.Materials.Count;

            foreach (Igi2TerrainTile tile in surface.Tiles)
            {
                tiles++;
                Assert.InRange(tile.Id, 0, 999);
                Assert.True(tile.Width > 0 && tile.Height > 0);
                Assert.True(tile.TexelWidth > 0 && tile.TexelHeight > 0);

                withMaterialMap += tile.Materials is null ? 0 : 1;
                withLightmap += tile.Lightmap is null ? 0 : 1;
            }
        }

        Assert.Equal(24, levels);
        Assert.Equal(46, tiles);
        Assert.Equal(tiles, withMaterialMap);
        Assert.Equal(tiles, withLightmap);
        Assert.True(materials > 40, $"Only {materials} terrain materials were declared.");
    }

    /// <summary>Samples sit at texel centres, so a tile covers exactly its authored extent.</summary>
    /// <remarks>
    /// <para>
    /// The arithmetic worth pinning, because getting it wrong is invisible until the ground is beside
    /// something else. A tile is <c>nWidth</c> texels across, not <c>nWidth - 1</c> spans, so the first
    /// sample sits half a texel inside the left edge and the last half a texel short of the right — and
    /// the distance between them is <c>(nWidth - 1)</c> texels, one texel short of the full extent.
    /// </para>
    /// <para>
    /// A corner-to-corner grid would put the first sample on the edge and stretch the spacing to
    /// <c>extent / (nWidth - 1)</c> — a 0.4% stretch on a 256-sample tile. That difference is too small
    /// to separate the two readings from the shipped data, which is why the arithmetic is taken from the
    /// binary and only asserted here.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_tile_spans_its_authored_extent_and_is_sampled_at_texel_centres()
    {
        int checkedTiles = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            foreach (Igi2TerrainTile tile in Surface(level).Tiles)
            {
                checkedTiles++;
                Vector3d first = tile.PositionOf(0, 0);
                Vector3d last = tile.PositionOf(tile.Width - 1, tile.Height - 1);

                double halfWidth = tile.Width * tile.TexelWidth * 0.5;
                double halfHeight = tile.Height * tile.TexelHeight * 0.5;

                // Half a texel inside each edge.
                Assert.Equal(tile.Centre.X - halfWidth + (tile.TexelWidth * 0.5), first.X, 3);
                Assert.Equal(tile.Centre.Y - halfHeight + (tile.TexelHeight * 0.5), first.Y, 3);
                Assert.Equal(tile.Centre.X + halfWidth - (tile.TexelWidth * 0.5), last.X, 3);
                Assert.Equal(tile.Centre.Y + halfHeight - (tile.TexelHeight * 0.5), last.Y, 3);

                // And the samples span one texel less than the extent, which is the whole point.
                Assert.Equal((tile.Width - 1) * tile.TexelWidth, last.X - first.X, 3);
            }
        }

        Assert.Equal(46, checkedTiles);
    }

    /// <summary>The ground lands under the level's objects rather than somewhere else entirely.</summary>
    /// <remarks>
    /// <para>
    /// A sanity check on the placement, and <b>deliberately not offered as proof of the sample
    /// spacing</b>. Measured both ways: the binary's mapping puts the median object 12,064 world units
    /// from the ground beneath it, and a naive corner grid — spacing from <c>Width - 1</c>, samples on
    /// corners — puts it at 12,981. Seven per cent apart, which is far too close to tell the two apart
    /// from data.
    /// </para>
    /// <para>
    /// So the spacing is settled by the binary, in two functions read in opposite directions, and this
    /// checks the class of error that data <em>can</em> catch: a wrong origin, a wrong unit scale, or
    /// transposed axes would put the ground kilometres away or under no objects at all.
    /// </para>
    /// <para>
    /// The offset is signed and its median is small because object origins sit above their footings
    /// about as often as below; the absolute median of 12,064 units is about three metres, which is
    /// where the middle of a crate sits when the crate is on the floor.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Placed_objects_sit_on_the_ground_the_tiles_describe()
    {
        List<double> offsets = [];

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2TerrainSurface surface = Surface(level);
            if (surface.IsEmpty)
            {
                continue;
            }

            foreach (Igi2Placement placement in level.Scene.StaticPlacements)
            {
                foreach (Igi2TerrainTile tile in surface.Tiles)
                {
                    double left = tile.Centre.X - (tile.Width * tile.TexelWidth * 0.5);
                    double top = tile.Centre.Y - (tile.Height * tile.TexelHeight * 0.5);
                    int x = (int)((placement.Position.X - left) / tile.TexelWidth);
                    int y = (int)((placement.Position.Y - top) / tile.TexelHeight);

                    if (x < 0 || y < 0 || x >= tile.Width || y >= tile.Height)
                    {
                        continue;
                    }

                    offsets.Add(placement.Position.Z - tile.Heights.GetHeight(x, y));
                    break;
                }
            }
        }

        Assert.Equal(8_240, offsets.Count);

        offsets.Sort();
        double median = offsets[offsets.Count / 2];
        double absoluteMedian = offsets.Select(Math.Abs).Order().ElementAt(offsets.Count / 2);

        // Signed, so a ground placed systematically high or low would show; absolute, so a ground placed
        // nowhere near the objects would too. Three metres is a crate's middle standing on a floor.
        Assert.InRange(median, -4096.0, 4096.0);
        Assert.InRange(absoluteMedian, 0.0, 5.0 * 4096.0);
    }

    private static Igi2TerrainSurface Surface(Igi2Level level)
    {
        string path = Path.Combine(level.Directory, "heightmaps", "heightmaps.res");
        IresArchive? archive = File.Exists(path) ? IresArchive.Read(File.ReadAllBytes(path)) : null;
        return Igi2TerrainSurface.Build(level.Root, archive);
    }
}
