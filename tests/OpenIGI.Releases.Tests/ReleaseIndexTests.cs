using OpenIGI.Releases;
using OpenIGI.Tools.Release;
using Xunit;

namespace OpenIGI.Releases.Tests;

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
        ChannelFeed feed = Index.Published(ReleaseChannel.Stable, keep: 10);
        Assert.Equal("0.1.1", feed.Latest!.Version);
        Assert.Equal(["0.1.1", "0.1.0"], feed.Releases.Select(static r => r.Version));
    }

    [Fact]
    public void Beta_players_get_the_newest_of_everything_with_beta_ten_after_beta_nine()
    {
        ChannelFeed feed = Index.Published(ReleaseChannel.Beta, keep: 3);
        Assert.Equal("0.2.0-beta.10", feed.Latest!.Version);
        Assert.Equal(["0.2.0-beta.10", "0.2.0-beta.9", "0.1.1"], feed.Releases.Select(static r => r.Version));
    }

    [Fact]
    public void The_launcher_comes_from_stable_releases_whichever_channel_is_asked()
    {
        // The fixtures give every release a launcher, betas included; only stable ones may be offered.
        Assert.Equal("0.1.1", Index.Published(ReleaseChannel.Stable, keep: 10).Launcher!.Version);
        Assert.Equal("0.1.1", Index.Published(ReleaseChannel.Beta, keep: 10).Launcher!.Version);
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

        ChannelFeed feed = index.Published(ReleaseChannel.Stable, keep: 10);
        Assert.Equal("0.1.0", feed.Latest!.Version);
        Assert.Equal(["0.1.0"], feed.Releases.Select(static r => r.Version));
        Assert.Equal("launcher-v0.1.1", feed.Launcher!.ReleaseTag);

        // Each release's files are where its folder is, whatever its version says.
        Assert.Equal("https://dl.openigi.com/releases/launcher-0.1.1/openigi-launcher-0.1.1-win-x64.zip", feed.Launcher.Assets.Single().Url);
        Assert.Equal("https://dl.openigi.com/releases/0.1.0/openigi-game-0.1.0-win-x64.exe", feed.Latest.Find(ReleaseComponent.Game, "win-x64")!.Url);

        // With only the combined release, its launcher is the one offered.
        Assert.Equal("v0.1.0", new ReleaseIndex([(combined, "0.1.0")], Template).Published(ReleaseChannel.Beta, keep: 10).Launcher!.ReleaseTag);
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
        ReleaseAsset asset = Index.Published(ReleaseChannel.Beta, keep: 1).Latest!.Find(ReleaseComponent.Launcher, "win-x64")!;
        Assert.Equal("https://dl.openigi.com/releases/0.2.0-beta.10/openigi-launcher-0.2.0-beta.10-win-x64.zip", asset.Url);
    }

    [Fact]
    public void An_empty_store_answers_with_no_release()
    {
        ChannelFeed feed = ReleaseIndex.Empty.Published(ReleaseChannel.Beta, keep: 10);
        Assert.Null(feed.Latest);
        Assert.Empty(feed.Releases);
    }

    [Fact]
    public void A_scan_indexes_complete_releases_and_skips_broken_ones()
    {
        string root = Directory.CreateTempSubdirectory("openigi-store-").FullName;
        try
        {
            Write(root, Release("0.1.0"));
            Write(root, Release("0.2.0-beta.1"));
            Directory.CreateDirectory(Path.Combine(root, "0.3.0-beta.1"));      // no manifest: not a release
            Directory.CreateDirectory(Path.Combine(root, "garbage"));
            File.WriteAllText(Path.Combine(root, "garbage", ReleaseManifest.FileName), "{ not json");

            List<string> skipped = [];
            ReleaseIndex index = StoreScan.Releases(root, Template, (path, _) => skipped.Add(path));

            Assert.Equal(2, index.Count);
            Assert.Equal("0.2.0-beta.1", index.Published(ReleaseChannel.Beta, keep: 1).Latest!.Version);
            Assert.Equal([Path.Combine(root, "garbage", ReleaseManifest.FileName)], skipped);
            Assert.Equal(0, StoreScan.Releases(Path.Combine(root, "missing"), Template, (_, _) => { }).Count);
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
