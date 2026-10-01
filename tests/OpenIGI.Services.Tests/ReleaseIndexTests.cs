using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenIGI.Releases;
using Xunit;

namespace OpenIGI.Services.Tests;

public sealed class ReleaseIndexTests
{
    private const string Template = "https://dl.openigi.com/releases/{version}/{name}";

    private static ReleaseManifest Release(string version) => new(
        ReleaseManifest.CurrentSchema,
        version,
        ReleaseVersion.Parse(version).IsPrerelease ? ReleaseChannel.Beta : ReleaseChannel.Stable,
        DateTimeOffset.UnixEpoch,
        "commit",
        [new ReleaseNoteSection("Fixes", [new ReleaseNote("render", $"Change in {version}")])],
        [new ReleaseAsset($"openigi-launcher-{version}-win-x64.zip", ReleaseComponent.Launcher, "win-x64", 1, "ab")]);

    private static readonly ReleaseIndex Index = new(
        [Release("0.1.0-beta.1"), Release("0.1.0"), Release("0.2.0-beta.9"), Release("0.2.0-beta.10"), Release("0.1.1")],
        Template);

    [Fact]
    public void Stable_players_are_only_offered_stable_releases()
    {
        ChannelFeed feed = Index.Feed(ReleaseChannel.Stable, since: null);
        Assert.Equal("0.1.1", feed.Latest!.Version);
        Assert.Equal(["0.1.1"], feed.Releases.Select(static r => r.Version));
    }

    [Fact]
    public void Beta_players_get_the_newest_of_everything_with_beta_ten_after_beta_nine()
    {
        ChannelFeed feed = Index.Feed(ReleaseChannel.Beta, since: "0.1.0");
        Assert.Equal("0.2.0-beta.10", feed.Latest!.Version);
        Assert.Equal(["0.2.0-beta.10", "0.2.0-beta.9", "0.1.1"], feed.Releases.Select(static r => r.Version));
    }

    [Fact]
    public void A_player_who_is_up_to_date_is_offered_nothing_new()
    {
        ChannelFeed feed = Index.Feed(ReleaseChannel.Stable, since: "0.1.1+abcdef");
        Assert.Equal("0.1.1", feed.Latest!.Version);
        Assert.Empty(feed.Releases);
    }

    [Fact]
    public void Every_asset_gets_its_download_address()
    {
        ReleaseAsset asset = Index.Feed(ReleaseChannel.Beta, since: null).Latest!.Assets.Single();
        Assert.Equal("https://dl.openigi.com/releases/0.2.0-beta.10/openigi-launcher-0.2.0-beta.10-win-x64.zip", asset.Url);
    }

    [Fact]
    public void The_tag_changes_only_when_what_is_served_changes()
    {
        ReleaseIndex same = new([Release("0.1.1"), Release("0.1.0"), Release("0.2.0-beta.10"), Release("0.2.0-beta.9"), Release("0.1.0-beta.1")], Template);
        ReleaseIndex more = new([Release("0.1.1"), Release("0.1.2")], Template);
        ReleaseIndex moved = new([Release("0.1.1"), Release("0.1.0"), Release("0.2.0-beta.10"), Release("0.2.0-beta.9"), Release("0.1.0-beta.1")],
            "https://github.com/OpenIGI/OpenIGI/releases/download/v{version}/{name}");

        Assert.Equal(Index.Tag, same.Tag);
        Assert.NotEqual(Index.Tag, more.Tag);
        Assert.NotEqual(Index.Tag, moved.Tag);
    }

    [Fact]
    public void An_empty_store_answers_with_no_release()
    {
        ChannelFeed feed = ReleaseIndex.Empty.Feed(ReleaseChannel.Beta, since: "0.1.0");
        Assert.Null(feed.Latest);
        Assert.Empty(feed.Releases);
    }

    [Fact]
    public void The_store_indexes_complete_releases_and_skips_broken_ones()
    {
        string root = Directory.CreateTempSubdirectory("openigi-store-").FullName;
        try
        {
            Write(root, Release("0.1.0"));
            Write(root, Release("0.2.0-beta.1"));
            Directory.CreateDirectory(Path.Combine(root, "0.3.0-beta.1"));      // still uploading: no manifest yet
            Directory.CreateDirectory(Path.Combine(root, "garbage"));
            File.WriteAllText(Path.Combine(root, "garbage", ReleaseManifest.FileName), "{ not json");

            ReleaseStore store = new(
                Options.Create(new ReleaseOptions { Root = root, DownloadUrl = Template }),
                NullLogger<ReleaseStore>.Instance);
            store.Scan();

            Assert.Equal(2, store.Current.Count);
            Assert.Equal("0.2.0-beta.1", store.Current.Feed(ReleaseChannel.Beta, null).Latest!.Version);

            string tag = store.Current.Tag;
            store.Scan();
            Assert.Equal(tag, store.Current.Tag);

            Write(root, Release("0.3.0-beta.1"));
            store.Scan();
            Assert.Equal("0.3.0-beta.1", store.Current.Feed(ReleaseChannel.Beta, null).Latest!.Version);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Write(string root, ReleaseManifest manifest)
    {
        string dir = Directory.CreateDirectory(Path.Combine(root, manifest.Version)).FullName;
        File.WriteAllText(Path.Combine(dir, ReleaseManifest.FileName), ReleaseJson.Write(manifest));
    }
}
