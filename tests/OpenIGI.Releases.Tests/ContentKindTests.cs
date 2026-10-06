using OpenIGI.Releases;
using Xunit;

namespace OpenIGI.Releases.Tests;

public sealed class ContentKindTests
{
    private static ContentManifest Manifest(ContentKind kind, string game = "igi1") => new(
        ContentManifest.CurrentSchema, "a-pack", "1.0.0", "A pack", "Summary", game, null, DateTimeOffset.UnixEpoch, 4,
        [new ContentFile("a.dll", 4, "ff", [new ContentChunk("aa", 4, 3)])],
        kind);

    [Fact]
    public void An_overlay_manifest_is_written_exactly_as_before_kinds_existed()
    {
        string json = ReleaseJson.Write(Manifest(ContentKind.Overlay));

        Assert.DoesNotContain("kind", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_manifest_from_before_kinds_reads_as_an_overlay()
    {
        string legacy = ReleaseJson.Write(Manifest(ContentKind.Overlay));

        Assert.Equal(ContentKind.Overlay, ReleaseJson.ReadContent(legacy).Kind);
    }

    [Fact]
    public void A_runtime_manifest_round_trips_and_its_listing_keeps_the_kind()
    {
        ContentManifest read = ReleaseJson.ReadContent(ReleaseJson.Write(Manifest(ContentKind.Runtime, RuntimePacks.Game)));

        Assert.Equal(ContentKind.Runtime, read.Kind);
        Assert.Equal(ContentKind.Runtime, read.Listing.Kind);
        Assert.Equal(RuntimePacks.Game, read.Listing.Game);
    }

    [Fact]
    public void A_runtime_listing_survives_the_index_and_a_pack_description()
    {
        ContentPack listing = Manifest(ContentKind.Runtime, RuntimePacks.Game).Listing;
        ContentPack described = new ContentPackInfo(ContentPackInfo.CurrentSchema, listing.Id, "Renamed", null, null)
            .Apply(listing, static (pack, file) => $"https://dl.test/{pack}/{file}");

        ContentIndex index = ReleaseJson.ReadContentIndex(ReleaseJson.Write(new ContentIndex("https://dl.test/{hash}", [described])));

        Assert.Equal(ContentKind.Runtime, index.Packs.Single().Kind);
        Assert.Equal("Renamed", index.Packs.Single().Title);
    }

    [Fact]
    public void Only_the_streamline_runtime_is_handed_to_the_game_and_by_the_variable_the_game_reads()
    {
        Assert.Equal("OPENIGI_STREAMLINE_PATH", RuntimePacks.VariableFor(RuntimePacks.Streamline));
        Assert.Null(RuntimePacks.VariableFor("hd-textures"));
        Assert.True(ContentManifest.IsValidId(RuntimePacks.Streamline));
    }
}
