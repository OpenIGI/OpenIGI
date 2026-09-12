using OpenIGI.Formats.Textures;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Pins the name a level's ground tiles are looked up by.
/// </summary>
/// <remarks>
/// <para>
/// A ground tile is an INDEX into a per-level atlas and carries no name, which is why the terrain was the
/// one surface no texture pack ever replaced — the packs are flat directories keyed by texture name, and
/// there was no key to offer them. These two functions manufacture one from the level the atlas belongs
/// to.
/// </para>
/// <para>
/// <b>Why it is worth a test file.</b> The name is a contract between two programs that never run
/// together: <c>ResExtract terrain</c> writes the files from a path on disk, and the renderer asks for
/// them from a resource path with an alias on the front. They agree only because one function makes the
/// string — and if that ever stops being true the failure is silent, a pack that loads and replaces
/// nothing, which is indistinguishable from the feature being switched off.
/// </para>
/// </remarks>
public sealed class TerrainTileNamingTests
{
    /// <summary>The two paths the two programs actually hold must produce the same key.</summary>
    /// <remarks>
    /// This is the whole point of the exercise. The renderer has a resource path — an alias, forward
    /// slashes, the <c>terrain/terrain</c> stem on the end. The extractor has a path relative to the
    /// installation directory, on Windows, with backslashes and no alias. Neither is more correct; they
    /// simply have to land on the same string.
    /// </remarks>
    [Theory]
    [InlineData("LOCAL:missions/location0/level1/terrain/terrain")]
    [InlineData("missions/location0/level1/terrain/terrain")]
    [InlineData("missions\\location0\\level1\\terrain\\terrain")]
    [InlineData("/missions/location0/level1/terrain/terrain")]
    [InlineData("LOCAL:MISSIONS/Location0/Level1/terrain/terrain")]
    public void EverySpellingOfOneLevelGivesOneKey(string path)
    {
        Assert.Equal("terrain_missions_location0_level1", TexAtlasFile.MakeTileSetKey(path));
    }

    /// <summary>Different levels must not collide, including across locations.</summary>
    /// <remarks>
    /// The reason the whole directory is used rather than its last segment: <c>level1</c> exists under
    /// every location, and keying on it would have every location's ground fight over one set of files.
    /// </remarks>
    [Fact]
    public void LevelsUnderDifferentLocationsDoNotCollide()
    {
        string first = TexAtlasFile.MakeTileSetKey("LOCAL:missions/location0/level1/terrain/terrain");
        string second = TexAtlasFile.MakeTileSetKey("LOCAL:missions/location1/level1/terrain/terrain");

        Assert.NotEqual(first, second);
    }

    /// <summary>A tile's name is its set and its index, zero padded so it sorts as the atlas is ordered.</summary>
    [Theory]
    [InlineData(0, "terrain_missions_location0_level1_00")]
    [InlineData(3, "terrain_missions_location0_level1_03")]
    [InlineData(17, "terrain_missions_location0_level1_17")]
    public void TileNamesCarryTheirIndex(int index, string expected)
    {
        string key = TexAtlasFile.MakeTileSetKey("LOCAL:missions/location0/level1/terrain/terrain");

        Assert.Equal(expected, TexAtlasFile.MakeTileName(key, index));
    }

    /// <summary>No path means no key, and no key means no name — not a name that matches everything.</summary>
    /// <remarks>
    /// A terrain built without a path is a real case -- <c>TerrainData</c>'s path argument is optional,
    /// and every test fixture omits it. The lookup has to come back empty so the caller falls through to
    /// the level's own tile, rather than asking a pack for <c>"terrain__00"</c> and being handed whatever
    /// happens to be named that.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("LOCAL:")]
    [InlineData("/")]
    public void AnAtlasWithNoPathHasNoName(string? path)
    {
        string key = TexAtlasFile.MakeTileSetKey(path);

        Assert.Equal(string.Empty, key);
        Assert.Equal(string.Empty, TexAtlasFile.MakeTileName(key, 0));
    }
}
