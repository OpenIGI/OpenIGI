using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// <see cref="Igi2TerrainMap"/> against every terrain map a retail I.G.I. 2 installation ships.
/// </summary>
/// <remarks>
/// The reader was written from <c>TerrainMap_Alloc</c> and <c>TerrainMap_SaveHandler</c> rather than by
/// measuring the files, so these are a check on the reading, not the source of it. Sweeping all 24
/// levels rather than sampling one is what makes a statement about the format instead of about a file —
/// the same standard the QVM and model suites are held to.
/// </remarks>
public sealed class RetailIgi2TerrainMapTests
{
    private static readonly GameDataSource Igi2 = GameDataSource.Igi2;

    /// <summary>Every terrain map in every level parses, and the three kinds arrive as a set.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Every_terrain_map_parses_and_the_three_kinds_arrive_together()
    {
        int archives = 0;
        int maps = 0;

        foreach (string path in HeightmapArchives())
        {
            archives++;
            IresArchive archive = IresArchive.Read(File.ReadAllBytes(path));
            Dictionary<string, List<string>> byTile = [];

            foreach (IresEntry entry in archive.Entries)
            {
                Igi2TerrainMapKind? kind = KindOf(entry.Name);
                if (kind is null)
                {
                    continue;
                }

                Igi2TerrainMap map = Igi2TerrainMap.Read(entry.Data, kind.Value);
                maps++;

                Assert.True(map.Width > 0 && map.Height > 0, $"'{entry.Name}' in '{path}' has no extent.");
                Assert.Equal(map.SampleCount * map.SampleSize, map.Samples.Length);

                string tile = Path.GetFileNameWithoutExtension(entry.Name);
                (byTile.TryGetValue(tile, out List<string>? seen) ? seen : byTile[tile] = [])
                    .Add(Path.GetExtension(entry.Name).ToLowerInvariant());
            }

            foreach ((string tile, List<string> extensions) in byTile)
            {
                Assert.Equal<string[]>([".thm", ".tlm", ".tmm"], [.. extensions.Order(StringComparer.Ordinal)]);
            }
        }

        Assert.True(archives > 0, "No heightmap archives were found.");
        Assert.True(maps > 0, "No terrain maps were read.");
    }

    /// <summary>
    /// A tile's three maps agree on their extent, so one set of grid coordinates addresses all three.
    /// </summary>
    /// <remarks>
    /// This is the assumption the renderer will rest on, and it is not one the format guarantees: each
    /// file states its own <c>nWidth</c> and <c>nHeight</c>, and the loader compares them against the
    /// task's rather than against each other.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_three_maps_of_a_tile_share_one_extent()
    {
        foreach (string path in HeightmapArchives())
        {
            IresArchive archive = IresArchive.Read(File.ReadAllBytes(path));
            Dictionary<string, Igi2TerrainMap> first = [];

            foreach (IresEntry entry in archive.Entries)
            {
                if (KindOf(entry.Name) is not { } kind)
                {
                    continue;
                }

                Igi2TerrainMap map = Igi2TerrainMap.Read(entry.Data, kind);
                string tile = Path.GetFileNameWithoutExtension(entry.Name);
                if (!first.TryAdd(tile, map))
                {
                    Igi2TerrainMap other = first[tile];
                    Assert.True(
                        map.Width == other.Width && map.Height == other.Height,
                        $"'{entry.Name}' in '{path}' is {map.Width}x{map.Height}, its tile is {other.Width}x{other.Height}.");
                }
            }
        }
    }

    /// <summary>
    /// Height maps are float32 world-space heights used as stored, and their <c>vOffset</c> and
    /// <c>vScale</c> are the identity pair on every shipped file.
    /// </summary>
    /// <remarks>
    /// Pinning the pair is what makes it safe for the reader to ignore them: were a file ever to carry
    /// something else, this fails rather than the terrain quietly coming out at the wrong altitude.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Height_maps_carry_the_identity_offset_and_scale()
    {
        int heightMaps = 0;

        foreach (Igi2TerrainMap map in ReadAll(Igi2TerrainMapKind.Height))
        {
            heightMaps++;
            Assert.Equal(0f, map.Offset);
            Assert.Equal(1f, map.Scale);

            float corner = map.GetHeight(0, 0);
            Assert.True(float.IsFinite(corner), $"A {map.Width}x{map.Height} height map has a non-finite corner.");
        }

        Assert.True(heightMaps > 0, "No height maps were read.");
    }

    /// <summary>
    /// Two of the fifty shipped height maps carry a mip pyramid past their declared extent, which the
    /// reader keeps out of the sample grid and the original never addresses.
    /// </summary>
    /// <remarks>
    /// The count is exact deliberately. Trailing bytes are the shape of a misread header, so a third file
    /// growing one should stop the build and be explained rather than absorbed.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Only_two_height_maps_carry_anything_past_their_extent()
    {
        List<int> tails = [];

        foreach (Igi2TerrainMap map in ReadAll(Igi2TerrainMapKind.Height))
        {
            if (!map.Trailing.IsEmpty)
            {
                tails.Add(map.Trailing.Length);
            }
        }

        // 128^2 + 64^2 + ... + 1^2 samples of four bytes: the mip chain below a 256x256 base.
        Assert.Equal<int[]>([87380, 87380], [.. tails]);
    }

    /// <summary>Lightmap texels are opaque, which is what the channel order says they should be.</summary>
    /// <remarks>
    /// <para>
    /// The writer composes a texel as <c>(0xFF &lt;&lt; 24) | (red &lt;&lt; 16) | (green &lt;&lt; 8) |
    /// blue</c>, so alpha is byte 3 and is always <c>0xFF</c>. Reading the bytes in any other order puts
    /// a colour channel where alpha belongs, and a real level would then show a non-opaque texel almost
    /// immediately — which is what makes this a test of the order and not merely of the content.
    /// </para>
    /// <para>
    /// A tile lit only in daylight would be white everywhere and prove nothing about the other three
    /// channels, so the sweep also asserts that some texel is not white.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Lightmap_texels_are_opaque_and_not_uniformly_white()
    {
        int lightMaps = 0;
        bool sawColour = false;

        foreach (Igi2TerrainMap map in ReadAll(Igi2TerrainMapKind.Light))
        {
            lightMaps++;

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    Bgra32 texel = map.GetLight(x, y);
                    Assert.True(
                        texel.A == 255,
                        $"A {map.Width}x{map.Height} lightmap has alpha {texel.A} at ({x}, {y}).");

                    sawColour |= texel.R != 255 || texel.G != 255 || texel.B != 255;
                }
            }
        }

        Assert.True(lightMaps > 0, "No lightmaps were read.");
        Assert.True(sawColour, "Every lightmap texel was white, so the channel order proves nothing.");
    }

    /// <summary>
    /// Material indices stay inside the byte range the level's <c>TerrainMaterial</c> tasks can declare.
    /// </summary>
    /// <remarks>
    /// A material map is one byte per sample, so the useful claim is not that the values fit — they
    /// cannot fail to — but that the retail corpus uses a small, contiguous set from zero. It does: the
    /// whole tree spans indices 0 to 7, which is a table of eight, and index zero is also the fallback
    /// the engine substitutes for a missing map.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Material_indices_span_a_small_contiguous_table()
    {
        HashSet<byte> used = [];

        foreach (Igi2TerrainMap map in ReadAll(Igi2TerrainMapKind.Material))
        {
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    used.Add(map.GetMaterial(x, y));
                }
            }
        }

        Assert.NotEmpty(used);
        Assert.Contains((byte)0, used);
        Assert.Equal<byte[]>([.. Enumerable.Range(0, used.Max() + 1).Select(static i => (byte)i)], [.. used.Order()]);
    }

    /// <summary>A map refuses to be read as one of the other two kinds.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_map_refuses_to_be_read_as_another_kind()
    {
        Igi2TerrainMap height = ReadAll(Igi2TerrainMapKind.Height).First();
        Assert.Equal(Igi2TerrainMapKind.Height, height.Kind);
        Assert.Throws<InvalidOperationException>(() => height.GetMaterial(0, 0));
        Assert.Throws<InvalidOperationException>(() => height.GetLight(0, 0));
    }

    private static IEnumerable<Igi2TerrainMap> ReadAll(Igi2TerrainMapKind kind)
    {
        foreach (string path in HeightmapArchives())
        {
            IresArchive archive = IresArchive.Read(File.ReadAllBytes(path));
            foreach (IresEntry entry in archive.Entries)
            {
                if (KindOf(entry.Name) == kind)
                {
                    yield return Igi2TerrainMap.Read(entry.Data, kind);
                }
            }
        }
    }

    private static Igi2TerrainMapKind? KindOf(string name) =>
        Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".thm" => Igi2TerrainMapKind.Height,
            ".tlm" => Igi2TerrainMapKind.Light,
            ".tmm" => Igi2TerrainMapKind.Material,
            _ => null,
        };

    private static IReadOnlyList<string> HeightmapArchives()
    {
        string root = Path.Combine(Igi2.RequireRoot(), "missions");
        return [.. Directory
            .EnumerateFiles(root, "*.res", SearchOption.AllDirectories)
            .Where(static p => Path.GetFileName(Path.GetDirectoryName(p))!
                .Equals("heightmaps", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)];
    }
}
