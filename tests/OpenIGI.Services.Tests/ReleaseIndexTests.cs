using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenIGI.Releases;
using Xunit;

namespace OpenIGI.Services.Tests;

public sealed class ReleaseIndexTests
{
    private const string Template = "https://dl.openigi.com/releases/{folder}/{name}";

    private static ReleaseManifest Release(string version) => new(
        ReleaseManifest.CurrentSchema,
        version,
        ReleaseVersion.Parse(version).IsPrerelease ? ReleaseChannel.Beta : ReleaseChannel.Stable,
        DateTimeOffset.UnixEpoch,
        "commit",
        [new ReleaseNoteSection("Fixes", [new ReleaseNote("render", $"Change in {version}")])],
        [new ReleaseAsset($"openigi-launcher-{version}-win-x64.zip", ReleaseComponent.Launcher, "win-x64", 1, "ab"),
         new ReleaseAsset($"openigi-game-{version}-win-x64.exe", ReleaseComponent.Game, "win-x64", 1, "cd")]);

    private static ReleaseManifest LauncherOnly(string version) => Release(version) with
    {
        Tag = ReleaseManifest.LauncherTagPrefix + version,
        Assets = [new ReleaseAsset($"openigi-launcher-{version}-win-x64.zip", ReleaseComponent.Launcher, "win-x64", 1, "ab")],
    };

    private static ReleaseAsset Game(string version) =>
        new($"openigi-game-{version}-win-x64.exe", ReleaseComponent.Game, "win-x64", 1, "cd");

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
    public void The_launcher_comes_from_stable_releases_whichever_channel_is_asked()
    {
        // The fixtures give every release a launcher, betas included; only stable ones may be offered.
        Assert.Equal("0.1.1", Index.Feed(ReleaseChannel.Stable, since: null).Launcher!.Version);
        Assert.Equal("0.1.1", Index.Feed(ReleaseChannel.Beta, since: "0.2.0-beta.10").Launcher!.Version);
        Assert.Equal("0.1.1", Index.NewestLauncher("win-x64")!.Version);
        Assert.Null(Index.NewestLauncher("osx-arm64"));
        Assert.Null(new ReleaseIndex([Release("0.2.0-beta.1")], Template).NewestLauncher());
    }

    [Fact]
    public void A_launcher_release_is_never_a_game_update_but_is_the_feeds_launcher()
    {
        // v0.1.0 shipped the game and the first launcher together; launcher-v0.1.1 is the launcher alone.
        ReleaseManifest combined = Release("0.1.0") with
        {
            Assets = [Game("0.1.0"), new ReleaseAsset("openigi-launcher-0.1.0-win-x64.exe", ReleaseComponent.Launcher, "win-x64", 1, "ab")],
        };
        ReleaseManifest launcher = LauncherOnly("0.1.1");
        ReleaseIndex index = new([(combined, "0.1.0"), (launcher, "launcher-0.1.1")], Template);

        ChannelFeed feed = index.Feed(ReleaseChannel.Stable, since: "0.1.0");
        Assert.Equal("0.1.0", feed.Latest!.Version);
        Assert.Empty(feed.Releases);
        Assert.Equal("launcher-v0.1.1", feed.Launcher!.ReleaseTag);

        // Each release's files are where its folder is, whatever its version says.
        Assert.Equal("https://dl.openigi.com/releases/launcher-0.1.1/openigi-launcher-0.1.1-win-x64.zip", feed.Launcher.Assets.Single().Url);
        Assert.Equal("https://dl.openigi.com/releases/0.1.0/openigi-game-0.1.0-win-x64.exe", feed.Latest.Find(ReleaseComponent.Game, "win-x64")!.Url);

        // With only the combined release, its launcher is the one offered.
        Assert.Equal("v0.1.0", new ReleaseIndex([(combined, "0.1.0")], Template).Feed(ReleaseChannel.Beta, null).Launcher!.ReleaseTag);
    }

    [Fact]
    public void A_github_address_is_built_from_the_releases_own_tag()
    {
        ReleaseManifest launcher = LauncherOnly("0.1.1");
        ReleaseIndex index = new([(launcher, "launcher-0.1.1")], "https://github.com/OpenIGI/OpenIGI/releases/download/{tag}/{name}");
        Assert.Equal(
            "https://github.com/OpenIGI/OpenIGI/releases/download/launcher-v0.1.1/openigi-launcher-0.1.1-win-x64.zip",
            index.NewestLauncher()!.Assets.Single().Url);
        Assert.Equal("v0.2.0", Release("0.2.0").ReleaseTag);
    }

    [Fact]
    public void Every_asset_gets_its_download_address()
    {
        ReleaseAsset asset = Index.Feed(ReleaseChannel.Beta, since: null).Latest!.Find(ReleaseComponent.Launcher, "win-x64")!;
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
