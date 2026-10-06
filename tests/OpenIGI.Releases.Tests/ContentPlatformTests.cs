using OpenIGI.Releases;
using Xunit;

namespace OpenIGI.Releases.Tests;

public sealed class ContentPlatformTests
{
    private static ContentManifest Manifest(IReadOnlyList<string>? platforms) => new(
        ContentManifest.CurrentSchema, "a-pack", "1.0.0", "A pack", "Summary", RuntimePacks.Game, null, DateTimeOffset.UnixEpoch, 4,
        [new ContentFile("a.dll", 4, "ff", [new ContentChunk("aa", 4, 3)])],
        ContentKind.Runtime,
        platforms);

    [Fact]
    public void A_pack_for_every_platform_is_written_exactly_as_before_platforms_existed()
    {
        string json = ReleaseJson.Write(Manifest(null));

        Assert.DoesNotContain("platforms", json, StringComparison.OrdinalIgnoreCase);
        Assert.Null(ReleaseJson.ReadContent(json).Platforms);
    }

    [Fact]
    public void Platforms_round_trip_and_reach_the_listing_and_the_index()
    {
        ContentManifest read = ReleaseJson.ReadContent(ReleaseJson.Write(Manifest(["win-x64"])));
        ContentPack described = new ContentPackInfo(ContentPackInfo.CurrentSchema, read.Id, "Renamed", null, null)
            .Apply(read.Listing, static (pack, file) => $"https://dl.test/{pack}/{file}");

        ContentIndex index = ReleaseJson.ReadContentIndex(ReleaseJson.Write(new ContentIndex("https://dl.test/{hash}", [described])));

        Assert.Equal(["win-x64"], read.Platforms);
        Assert.Equal(["win-x64"], index.Packs.Single().Platforms);
    }

    [Theory]
    [InlineData(null, "linux-x64", true)]
    [InlineData(new string[0], "osx-arm64", true)]
    [InlineData(new[] { "win-x64" }, "win-x64", true)]
    [InlineData(new[] { "win-x64" }, "linux-x64", false)]
    [InlineData(new[] { "win-x64" }, "osx-arm64", false)]
    [InlineData(new[] { "win-x64", "linux-x64" }, "linux-x64", true)]
    public void A_pack_is_offered_only_on_the_platforms_it_names(string[]? platforms, string platform, bool supported) =>
        Assert.Equal(supported, Manifest(platforms).Listing.SupportsPlatform(platform));

    [Theory]
    [InlineData("win-x64", true)]
    [InlineData("linux-arm64", true)]
    [InlineData("osx-arm64", true)]
    [InlineData("win", false)]
    [InlineData("Win-x64", false)]
    [InlineData("windows-x64", false)]
    [InlineData("", false)]
    public void Only_runtime_identifiers_are_platforms(string platform, bool valid) =>
        Assert.Equal(valid, ContentManifest.IsValidPlatform(platform));
}
