using OpenIGI.Releases;
using OpenIGI.Tools.Release;
using Xunit;

namespace OpenIGI.Releases.Tests;

public sealed class SiteWriterTests : IDisposable
{
    private const string ChunkUrl = "https://github.com/OpenIGI/openigi-content/releases/download/chunks-{h2}/{hash}.br";

    private readonly string _root = Directory.CreateTempSubdirectory("openigi-site-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static ReleaseManifest Release(string version, ReleaseComponent component = ReleaseComponent.Game) => new(
        ReleaseManifest.CurrentSchema,
        version,
        ReleaseVersion.Parse(version).IsPrerelease ? ReleaseChannel.Beta : ReleaseChannel.Stable,
        DateTimeOffset.UnixEpoch,
        "commit",
        [],
        [new ReleaseAsset($"openigi-{component.ToString().ToLowerInvariant()}-{version}-win-x64.exe", component, "win-x64", 1, "ab")]);

    private static ContentManifest Manifest(string id, string version, ContentKind kind = ContentKind.Overlay) => new(
        ContentManifest.CurrentSchema, id, version, id, "Summary", "igi1", null, DateTimeOffset.UnixEpoch, 4,
        [new ContentFile("a.res", 4, "ff", [new ContentChunk("aa", 4, 3)])]) { Kind = kind };

    private string Read(string path) => File.ReadAllText(Path.Combine(_root, "site", path));

    [Fact]
    public void The_site_answers_every_path_the_launcher_and_the_buttons_ask_for()
    {
        ReleaseIndex releases = new(
            [Release("0.1.0"), Release("0.2.0-beta.1"), Release("0.1.1", ReleaseComponent.Launcher) with { Tag = "launcher-v0.1.1" }],
            "https://github.com/OpenIGI/OpenIGI/releases/download/{tag}/{name}");
        ContentManifest[] manifests = [Manifest("hd-textures", "1.0.0"), Manifest("hd-textures", "1.1.0"), Manifest("igi2soldiers", "0.2.0", ContentKind.Mod)];
        ContentLibrary content = new([.. manifests.Select(static m => (m, ReleaseJson.Write(m)))], ChunkUrl);

        string store = Directory.CreateDirectory(Path.Combine(_root, "store", "hd-textures")).FullName;
        File.WriteAllText(Path.Combine(store, "thumbnail-0123456789ab.png"), "png");

        SiteWriter.Write(Path.Combine(_root, "site"), releases, content, Path.Combine(_root, "store"));

        Assert.Equal("0.1.0", ReleaseJson.ReadFeed(Read("v1/channels/stable")).Latest!.Version);
        ChannelFeed beta = ReleaseJson.ReadFeed(Read("v1/channels/beta"));
        Assert.Equal(["0.2.0-beta.1", "0.1.0"], beta.Releases.Select(static r => r.Version));
        Assert.Equal("launcher-v0.1.1", beta.Launcher!.ReleaseTag);

        // v1/content and a pack's newest manifest are folders, as they share a name with what is under them.
        Assert.Equal(["hd-textures"], ReleaseJson.ReadContentIndex(Read("v1/content/index.html")).Packs.Select(static p => p.Id));
        Assert.Equal(ChunkUrl, ReleaseJson.ReadContentIndex(Read("v1/content/index.html")).ChunkUrl);
        Assert.Equal(["igi2soldiers"], ReleaseJson.ReadContentIndex(Read(ModPacks.IndexPath)).Packs.Select(static p => p.Id));
        Assert.Equal("1.1.0", ReleaseJson.ReadContent(Read("v1/content/hd-textures/index.html")).Version);
        Assert.Equal("1.0.0", ReleaseJson.ReadContent(Read("v1/content/hd-textures/1.0.0")).Version);
        Assert.Equal("0.2.0", ReleaseJson.ReadContent(Read("v1/content/igi2soldiers/0.2.0")).Version);
        Assert.Equal("png", Read("content/hd-textures/thumbnail-0123456789ab.png"));

        const string launcher = "https://github.com/OpenIGI/OpenIGI/releases/download/launcher-v0.1.1/openigi-launcher-0.1.1-win-x64.exe";
        Assert.Equal(launcher + "\n", Read("download/win-x64/url"));
        Assert.Contains($"url={launcher}", Read("download/win-x64/index.html"), StringComparison.Ordinal);
        Assert.StartsWith("ok 3 release(s)", Read("healthz"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_macOS_download_page_explains_the_quarantine_instead_of_sending_the_browser_straight_on()
    {
        const string url = "https://github.com/OpenIGI/OpenIGI/releases/download/launcher-v0.1.1/openigi-launcher-0.1.1-osx-arm64.tar.gz";
        ReleaseManifest launcher = Release("0.1.1", ReleaseComponent.Launcher) with
        {
            Tag = "launcher-v0.1.1",
            Assets = [new ReleaseAsset("openigi-launcher-0.1.1-osx-arm64.tar.gz", ReleaseComponent.Launcher, "osx-arm64", 1, "ab")],
        };
        ReleaseIndex releases = new([launcher], "https://github.com/OpenIGI/OpenIGI/releases/download/{tag}/{name}");

        SiteWriter.Write(Path.Combine(_root, "site"), releases, new ContentLibrary([], ChunkUrl), null);

        // The script that installs reads the address alone, whatever the page says.
        Assert.Equal(url + "\n", Read("download/osx-arm64/url"));
        string page = Read("download/osx-arm64/index.html");
        Assert.DoesNotContain("http-equiv", page, StringComparison.Ordinal);
        Assert.Contains("curl -fsSL https://openigi.com/install.sh | bash", page, StringComparison.Ordinal);
        Assert.Contains("xattr -dr com.apple.quarantine", page, StringComparison.Ordinal);
        Assert.Contains($"href=\"{url}\"", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("win-x64")]
    [InlineData("linux-x64")]
    public void Any_other_download_page_still_sends_the_browser_straight_on(string runtime)
    {
        string page = SiteWriter.DownloadPage(runtime, "0.1.1", "https://example.test/a&b");

        Assert.Contains("http-equiv=\"refresh\" content=\"0; url=https://example.test/a&amp;b\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("xattr", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("hd-textures_1.0.0.content.json", "hd-textures/1.0.0/content.json")]
    [InlineData("hd-textures.pack.json", "hd-textures/pack.json")]
    [InlineData("hd-textures.thumbnail-9eceac3df034.webp", "hd-textures/thumbnail-9eceac3df034.webp")]
    [InlineData("hd-textures.notes.txt", null)]
    [InlineData("hd-textures_one.content.json", null)]
    [InlineData("Bad_1.0.0.content.json", null)]
    public void The_manifests_release_lays_out_as_a_folder_store(string name, string? path) =>
        Assert.Equal(path?.Replace('/', Path.DirectorySeparatorChar), GitHubStore.FolderPath(name));
}
