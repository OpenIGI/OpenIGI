using OpenIGI.Releases;
using Xunit;

namespace OpenIGI.Releases.Tests;

public sealed class ContentChunkerTests
{
    private static byte[] Random(int length, int seed)
    {
        byte[] data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static List<string> Chunks(byte[] data)
    {
        List<string> hashes = [];
        using MemoryStream stream = new(data);
        ContentChunker.Split(stream, chunk => hashes.Add(ContentChunker.Hash(chunk)));
        return hashes;
    }

    [Fact]
    public void Chunks_cover_the_input_exactly_and_stay_within_their_sizes()
    {
        byte[] data = Random(24 * 1024 * 1024, 1);
        List<int> sizes = [];
        using MemoryStream stream = new(data);
        using MemoryStream joined = new();
        ContentChunker.Split(stream, chunk =>
        {
            sizes.Add(chunk.Length);
            joined.Write(chunk);
        });

        Assert.Equal(data, joined.ToArray());
        Assert.All(sizes[..^1], size => Assert.InRange(size, ContentChunker.MinSize, ContentChunker.MaxSize));
        Assert.InRange(sizes.Average(), ContentChunker.AverageSize * 0.6, ContentChunker.AverageSize * 2.0);
    }

    [Fact]
    public void Bytes_inserted_in_the_middle_change_only_the_chunks_around_them()
    {
        // What a rebuilt texture does to its archive: everything after it moves.
        byte[] before = Random(64 * 1024 * 1024, 2);
        byte[] after = [.. before[..(30 * 1024 * 1024)], .. Random(70_000, 3), .. before[(30 * 1024 * 1024)..]];

        List<string> old = Chunks(before);
        HashSet<string> known = [.. old];
        List<string> next = Chunks(after);

        int fresh = next.Count(hash => !known.Contains(hash));
        Assert.InRange(fresh, 1, 3);
        Assert.True(next.Count - fresh >= old.Count - 3, $"{fresh} new of {next.Count}");
    }

    [Fact]
    public void Cutting_is_the_same_whatever_the_stream_reads_at_a_time()
    {
        byte[] data = Random(12 * 1024 * 1024, 4);
        List<string> whole = Chunks(data);

        List<string> trickled = [];
        using TrickleStream stream = new(data, 4093);
        ContentChunker.Split(stream, chunk => trickled.Add(ContentChunker.Hash(chunk)));

        Assert.Equal(whole, trickled);
    }

    [Fact]
    public void A_small_file_is_one_chunk_and_an_empty_one_none()
    {
        Assert.Single(Chunks(Random(1000, 5)));
        Assert.Empty(Chunks([]));
    }

    [Fact]
    public void A_stored_chunk_decodes_to_itself_and_a_wrong_one_is_refused()
    {
        byte[] data = Random(300_000, 6);
        ContentChunk chunk = new(ContentChunker.Hash(data), data.Length, 0);
        byte[] stored = ContentChunker.Encode(data);

        Assert.Equal(data, ContentChunker.Decode(stored, chunk));

        byte[] other = ContentChunker.Encode(Random(300_000, 7));
        Assert.Throws<InvalidDataException>(() => ContentChunker.Decode(other, chunk));
        Assert.Throws<InvalidDataException>(() => ContentChunker.Decode(stored, chunk with { Size = data.Length - 1 }));
    }

    [Fact]
    public void A_manifest_round_trips_without_its_derived_values()
    {
        ContentManifest manifest = new(
            ContentManifest.CurrentSchema, "hd-textures", "1.0.0", "High Resolution Textures", "Sharper.", "igi1", "0.2.0-beta.1",
            DateTimeOffset.UnixEpoch, 10,
            [new ContentFile("common/a.res", 10, "ff", [new ContentChunk("aa", 6, 4), new ContentChunk("bb", 4, 3)])]);

        string json = ReleaseJson.Write(manifest);
        ContentManifest read = ReleaseJson.ReadContent(json);

        Assert.DoesNotContain("parsedVersion", json, StringComparison.Ordinal);
        Assert.DoesNotContain("listing", json, StringComparison.Ordinal);
        Assert.Equal(manifest.Files[0].Chunks, read.Files[0].Chunks);
        Assert.Equal(7, read.DownloadSize);
        Assert.Equal(7, read.Listing.Download);
    }

    [Theory]
    [InlineData(null, "0.1.0", true)]
    [InlineData("0.2.0-beta.1", "0.1.0", false)]
    [InlineData("0.2.0-beta.1", "0.2.0-beta.1", true)]
    [InlineData("0.2.0-beta.1", "0.2.0", true)]
    [InlineData("0.2.0-beta.1", null, false)]
    public void A_pack_names_the_oldest_game_that_reads_it(string? minimum, string? game, bool supported)
    {
        ContentPack pack = new("p", "1.0.0", "P", "S", "igi1", minimum, DateTimeOffset.UnixEpoch, 1, 1);
        Assert.Equal(supported, pack.SupportsGame(game));
    }

    [Theory]
    [InlineData("hd-textures", true)]
    [InlineData("a", true)]
    [InlineData("-a", false)]
    [InlineData("a-", false)]
    [InlineData("HD", false)]
    [InlineData("../x", false)]
    [InlineData("", false)]
    public void Pack_names_are_plain(string id, bool valid) => Assert.Equal(valid, ContentManifest.IsValidId(id));

    [Fact]
    public void The_index_builds_chunk_addresses_from_its_template()
    {
        ContentIndex index = new("https://dl.test/content/chunks/{h2}/{hash}.br", []);
        Assert.Equal("https://dl.test/content/chunks/ab/abcdef.br", index.ChunkAddress("abcdef").ToString());
    }

    [Fact]
    public void A_pack_description_goes_over_the_newest_version_where_it_says_something()
    {
        ContentPack listing = new("hd", "1.0.0", "Old name", "Old words", "igi1", null, DateTimeOffset.UnixEpoch, 1, 1);
        static string Url(string pack, string name) => $"https://dl.test/content/{pack}/{name}";

        ContentPack renamed = new ContentPackInfo(1, "hd", "New name", null, "thumbnail-0123456789ab.webp").Apply(listing, Url);
        Assert.Equal("New name", renamed.Title);
        Assert.Equal("Old words", renamed.Summary);
        Assert.Equal("https://dl.test/content/hd/thumbnail-0123456789ab.webp", renamed.Thumbnail);

        ContentPack odd = new ContentPackInfo(1, "hd", " ", null, "../../etc/passwd").Apply(listing, Url);
        Assert.Equal("Old name", odd.Title);
        Assert.Null(odd.Thumbnail);

        string json = ReleaseJson.Write(new ContentPackInfo(1, "hd", "T", null, null));
        Assert.Equal("T", ReleaseJson.ReadPackInfo(json).Title);
    }

    [Theory]
    [InlineData(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10 }, "png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "jpg")]
    [InlineData(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' }, "webp")]
    [InlineData(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8' }, null)]
    public void A_picture_is_known_by_its_first_bytes(byte[] data, string? extension) =>
        Assert.Equal(extension, ContentPackInfo.PictureExtension(data));

    [Theory]
    [InlineData("thumbnail-0123456789ab.png", true)]
    [InlineData("thumbnail-0123456789ab.gif", false)]
    [InlineData("thumbnail-0123.png", false)]
    [InlineData("../thumbnail-0123456789ab.png", false)]
    public void A_picture_name_is_its_hash(string name, bool valid) => Assert.Equal(valid, ContentPackInfo.IsValidThumbnail(name));

    // Hands out a few bytes per read, as a network stream would.
    private sealed class TrickleStream(byte[] data, int step) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, step));
    }
}
