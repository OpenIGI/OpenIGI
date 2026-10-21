using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenIGI.Releases;
using Xunit;

namespace OpenIGI.Services.Tests;

public sealed class ContentLibraryTests : IDisposable
{
    private const string ChunkUrl = "https://dl.test/content/chunks/{h2}/{hash}.br";

    private readonly string _root = Directory.CreateTempSubdirectory("openigi-content-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static ContentManifest Manifest(string id, string version) => new(
        ContentManifest.CurrentSchema, id, version, id.ToUpperInvariant(), "Summary", "igi1", null, DateTimeOffset.UnixEpoch, 4,
        [new ContentFile("a.res", 4, "ff", [new ContentChunk("aa", 4, 3)])]);

    private void Publish(ContentManifest manifest, string? folder = null)
    {
        string path = Path.Combine(_root, manifest.Id, folder ?? manifest.Version);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, ContentManifest.FileName), ReleaseJson.Write(manifest));
    }

    private ContentStore Store() => new(
        Options.Create(new ContentOptions { Root = _root, ChunkUrl = ChunkUrl }),
        NullLogger<ContentStore>.Instance);

    [Fact]
    public void The_index_lists_each_pack_at_its_newest_version_with_ten_after_nine()
    {
        ContentLibrary library = new(
            [.. new[] { Manifest("hd-textures", "1.9.0"), Manifest("hd-textures", "1.10.0"), Manifest("voices", "1.0.0") }
                .Select(static m => (m, ReleaseJson.Write(m)))],
            ChunkUrl);

        Assert.Equal(ChunkUrl, library.Index.ChunkUrl);
        Assert.Equal(["hd-textures@1.10.0", "voices@1.0.0"], library.Index.Packs.Select(static p => $"{p.Id}@{p.Version}"));
        Assert.Equal(3, library.Count);
        Assert.Contains("\"1.9.0\"", library.Manifest("hd-textures", "1.9.0"), StringComparison.Ordinal);
        Assert.Contains("\"1.10.0\"", library.Manifest("hd-textures", null), StringComparison.Ordinal);
        Assert.Null(library.Manifest("hd-textures", "2.0.0"));
        Assert.Null(library.Manifest("nothing", null));
    }

    [Fact]
    public void Mods_are_listed_in_their_own_index_and_served_by_the_same_manifests()
    {
        ContentManifest mod = Manifest("igi2soldiers", "0.1.0") with
        {
            Kind = ContentKind.Mod,
            Mod = new ContentModInfo(["igi1"], ["OpenIGI"], RunsCode: true, "https://github.test/mods", "abc123"),
        };
        ContentLibrary library = new([.. new[] { Manifest("hd-textures", "1.0.0"), mod }.Select(static m => (m, ReleaseJson.Write(m)))], ChunkUrl);

        Assert.Equal(["hd-textures"], library.Index.Packs.Select(static p => p.Id));
        ContentPack listed = Assert.Single(library.Mods.Packs);
        Assert.Equal("igi2soldiers", listed.Id);
        Assert.Equal("abc123", listed.Mod!.Commit);
        Assert.Equal(ChunkUrl, library.Mods.ChunkUrl);
        Assert.Contains("\"mod\"", library.Manifest("igi2soldiers", null), StringComparison.Ordinal);
    }

    [Fact]
    public void A_scan_finds_published_versions_and_its_tag_changes_with_each_publish()
    {
        ContentStore store = Store();
        store.Scan();
        Assert.Empty(store.Current.Index.Packs);
        string empty = store.Current.Tag;

        Publish(Manifest("hd-textures", "1.0.0"));
        store.Scan();
        Assert.Equal("1.0.0", Assert.Single(store.Current.Index.Packs).Version);
        string first = store.Current.Tag;

        Publish(Manifest("hd-textures", "1.0.1"));
        store.Scan();
        Assert.Equal("1.0.1", Assert.Single(store.Current.Index.Packs).Version);
        Assert.NotEqual(empty, first);
        Assert.NotEqual(first, store.Current.Tag);
    }

    [Fact]
    public void A_manifest_in_the_wrong_folder_or_an_unfinished_version_is_skipped()
    {
        Publish(Manifest("hd-textures", "1.0.0"));
        Publish(Manifest("hd-textures", "9.9.9"), folder: "1.0.1");
        Directory.CreateDirectory(Path.Combine(_root, "hd-textures", "1.0.2"));
        Directory.CreateDirectory(Path.Combine(_root, "chunks", "aa"));

        ContentStore store = Store();
        store.Scan();

        Assert.Equal(1, store.Current.Count);
        Assert.Equal("1.0.0", Assert.Single(store.Current.Index.Packs).Version);
    }

    [Fact]
    public void A_pack_description_renames_and_pictures_a_pack_without_a_new_version()
    {
        Publish(Manifest("hd-textures", "1.0.0"));
        ContentStore store = new(
            Options.Create(new ContentOptions { Root = _root, ChunkUrl = ChunkUrl, ThumbnailUrl = "https://dl.test/content/{pack}/{name}" }),
            NullLogger<ContentStore>.Instance);
        store.Scan();
        string before = store.Current.Tag;

        File.WriteAllText(
            Path.Combine(_root, "hd-textures", ContentPackInfo.FileName),
            ReleaseJson.Write(new ContentPackInfo(1, "hd-textures", "HD Textures", null, "thumbnail-0123456789ab.png")));
        store.Scan();

        ContentPack pack = Assert.Single(store.Current.Index.Packs);
        Assert.Equal("HD Textures", pack.Title);
        Assert.Equal("Summary", pack.Summary);
        Assert.Equal("1.0.0", pack.Version);
        Assert.Equal("https://dl.test/content/hd-textures/thumbnail-0123456789ab.png", pack.Thumbnail);
        Assert.NotEqual(before, store.Current.Tag);
    }

    [Fact]
    public void A_runtime_pack_is_listed_with_its_kind_for_the_engine_rather_than_a_game()
    {
        ContentManifest runtime = Manifest(RuntimePacks.Streamline, "1.0.0") with { Game = RuntimePacks.Game, Kind = ContentKind.Runtime };
        ContentLibrary library = new([(runtime, ReleaseJson.Write(runtime)), (Manifest("hd-textures", "1.0.0"), ReleaseJson.Write(Manifest("hd-textures", "1.0.0")))], ChunkUrl);

        ContentPack listed = library.Index.Packs.Single(static p => p.Id == RuntimePacks.Streamline);
        Assert.Equal(ContentKind.Runtime, listed.Kind);
        Assert.Equal(RuntimePacks.Game, listed.Game);
        Assert.Equal(ContentKind.Overlay, library.Index.Packs.Single(static p => p.Id == "hd-textures").Kind);
    }

    [Fact]
    public void An_empty_store_still_says_where_chunks_come_from()
    {
        ContentStore store = Store();
        store.Scan();

        Assert.Empty(store.Current.Index.Packs);
        Assert.Equal(ChunkUrl, store.Current.Index.ChunkUrl);
    }

    [Fact]
    public void A_store_that_does_not_exist_yet_is_an_empty_index()
    {
        ContentStore store = new(
            Options.Create(new ContentOptions { Root = Path.Combine(_root, "missing"), ChunkUrl = ChunkUrl }),
            NullLogger<ContentStore>.Instance);
        store.Scan();

        Assert.Empty(store.Current.Index.Packs);
        Assert.Equal(ChunkUrl, store.Current.Index.ChunkUrl);
    }
}
