using OpenIGI.Game.Multiplayer;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// A GOG copy of I.G.I. 2 joining a retail host: of the shared models archive, which GOG ships with three meshes changed, only
/// the pieces around them travel.
/// </summary>
/// <remarks>Needs both installations: the retail one from <c>IGI2_GAME_PATH</c>, the GOG one from <c>IGI2_GOG_PATH</c>.</remarks>
public sealed class Igi2RetailGogDeltaTests(ITestOutputHelper output)
{
    [Fact]
    public void A_gog_copy_fetches_a_sliver_of_the_retail_models_archive()
    {
        string? gog = Environment.GetEnvironmentVariable("IGI2_GOG_PATH");
        if (!GameDataSource.Igi2.IsAvailable || string.IsNullOrWhiteSpace(gog) || !Directory.Exists(gog))
        {
            return;
        }

        byte[] retail = File.ReadAllBytes(GameDataSource.Igi2.Resolve(Path.Combine("COMMON", "models", "common.res")));
        byte[] theirs = File.ReadAllBytes(Path.Combine(gog, "COMMON", "models", "common.res"));

        HashSet<(ulong, int)> have = [.. ContentChunks.Cut(theirs).Select(static c => (c.Hash, c.Length))];
        IReadOnlyList<ContentChunk> table = ContentChunks.Cut(retail);
        long missing = table.Where(c => !have.Contains((c.Hash, c.Length))).Sum(static c => (long)c.Length);
        output.WriteLine($"retail {retail.Length:N0} bytes in {table.Count} pieces; GOG lacks {missing:N0} bytes of them");

        Assert.True(missing < retail.Length / 20, $"{missing} of {retail.Length} bytes to fetch");
    }
}
