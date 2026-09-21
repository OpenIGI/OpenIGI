using OpenIGI.Engine.Resources;
using Xunit;

namespace OpenIGI.Engine.Tests;

/// <summary>The walk five of the original's tables share.</summary>
public sealed class ScriptTableLoaderTests
{
    /// <summary>A walked path keeps the alias the walk started from.</summary>
    /// <remarks>
    /// The behaviour the previous version got right by accident. It searched the path for the literal
    /// substring <c>missions/</c> and re-prefixed <c>LOCAL:</c>, which is correct only when the tree is
    /// rooted exactly there.
    /// </remarks>
    [Fact]
    public void A_path_under_the_root_is_returned_unchanged()
    {
        Assert.Equal(
            "LOCAL:missions/location0/level1/mission.qvm",
            ScriptTableLoader.ToResourceName("LOCAL:missions/location0/level1/mission.qvm", "LOCAL:missions"));
    }

    /// <summary>A relative path is put back under the alias the caller walked.</summary>
    [Fact]
    public void A_relative_path_regains_its_alias()
    {
        Assert.Equal(
            "LOCAL:missions/location0/level1/mission.qvm",
            ScriptTableLoader.ToResourceName("missions/location0/level1/mission.qvm", "LOCAL:missions"));
    }

    /// <summary>
    /// A tree that is not rooted at <c>missions/</c> keeps its full path.
    /// </summary>
    /// <remarks>
    /// This is the case the substring search got wrong. Searching for <c>missions/</c> in
    /// <c>LOCAL:pc/missions/…</c> and re-prefixing would produce <c>LOCAL:missions/…</c>, silently
    /// dropping the <c>pc/</c> — and the load would then fail with a "vanished between the scan and the
    /// load" warning naming a path nobody typed. It never bit because I.G.I. 1's tree happens to be
    /// rooted exactly where the search looked.
    /// </remarks>
    [Fact]
    public void A_tree_below_a_subdirectory_keeps_its_prefix()
    {
        Assert.Equal(
            "LOCAL:pc/missions/location1/level1/mission.qvm",
            ScriptTableLoader.ToResourceName("LOCAL:pc/missions/location1/level1/mission.qvm", "LOCAL:pc/missions"));
    }

    /// <summary>Windows separators are normalised, as the walk may produce either.</summary>
    [Fact]
    public void Backslashes_are_normalised()
    {
        Assert.Equal(
            "LOCAL:missions/location0/mission.qvm",
            ScriptTableLoader.ToResourceName(@"LOCAL:missions\location0\mission.qvm", "LOCAL:missions"));
    }

    /// <summary>The arguments a caller can get wrong are refused rather than half-handled.</summary>
    [Fact]
    public void Empty_arguments_are_refused()
    {
        Assert.Throws<ArgumentNullException>(() => ScriptTableLoader.ToResourceName(null!, "LOCAL:missions"));
        Assert.Throws<ArgumentException>(() => ScriptTableLoader.ToResourceName("a", string.Empty));
    }
}
