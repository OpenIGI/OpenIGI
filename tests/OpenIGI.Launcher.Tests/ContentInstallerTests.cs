using System.Net;
using System.Security.Cryptography;
using OpenIGI.Launcher.Services;
using OpenIGI.Releases;
using Xunit;

namespace OpenIGI.Launcher.Tests;

public sealed class ContentInstallerTests : IDisposable
{
    private static readonly ContentIndex Index = new("https://dl.test/content/chunks/{h2}/{hash}.br", []);

    private readonly string _root = Directory.CreateTempSubdirectory("openigi-content-").FullName;
    private readonly FakeServer _server = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static byte[] Random(int length, int seed)
    {
        byte[] data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    // Publishes a version the way the tool does: every file cut into chunks, every chunk stored on the server.
    private ContentManifest Publish(string version, params (string Path, byte[] Data)[] files)
    {
        List<ContentFile> listed = [];
        foreach ((string path, byte[] data) in files.OrderBy(static f => f.Path, StringComparer.Ordinal))
        {
            List<ContentChunk> chunks = [];
            using MemoryStream stream = new(data);
            ContentChunker.Split(stream, chunk =>
            {
                string hash = ContentChunker.Hash(chunk);
                byte[] stored = ContentChunker.Encode(chunk);
                _server.Add($"/content/chunks/{hash[..2]}/{hash}.br", stored);
                chunks.Add(new ContentChunk(hash, chunk.Length, stored.Length));
            });
            listed.Add(new ContentFile(path, data.Length, Convert.ToHexStringLower(SHA256.HashData(data)), chunks));
        }

        return new ContentManifest(ContentManifest.CurrentSchema, "hd-textures", version, "HD", "Sharper.", "igi1", null,
            DateTimeOffset.UnixEpoch, listed.Sum(static f => f.Size), listed);
    }

    private ContentInstaller Installer() => new(new HttpClient(_server), _root);

    private List<string> ChunkRequests() =>
        [.. _server.Requests.Select(static r => r.RequestUri!.AbsolutePath).Where(static p => p.StartsWith("/content/chunks/", StringComparison.Ordinal))];

    private byte[] Installed(string path) => File.ReadAllBytes(Path.Combine(_root, "files", path));

    [Fact]
    public async Task A_fresh_install_writes_every_file_and_records_the_version()
    {
        byte[] level = Random(6_000_000, 1);
        byte[] sprite = Random(40_000, 2);
        ContentManifest v1 = Publish("1.0.0", ("missions/level1.res", level), ("common/sprites.res", sprite));
        ContentInstaller installer = Installer();

        Assert.Null(installer.Installed());
        ContentPlan plan = installer.Plan(v1);
        Assert.Equal(v1.DownloadSize, plan.DownloadBytes);

        await installer.InstallAsync(Index, v1, null, null, CancellationToken.None);

        Assert.Equal(level, Installed("missions/level1.res"));
        Assert.Equal(sprite, Installed("common/sprites.res"));
        Assert.Equal("1.0.0", installer.Installed()!.Version);
        Assert.Equal(plan.Download.Count, ChunkRequests().Count);
        Assert.False(Directory.Exists(Path.Combine(_root, "chunks.part")));
        Assert.True(installer.Plan(v1).IsEmpty);
    }

    [Fact]
    public async Task An_update_downloads_only_the_chunks_that_changed()
    {
        byte[] level = Random(16_000_000, 3);
        ContentManifest v1 = Publish("1.0.0", ("level.res", level), ("old.tex", Random(5000, 4)), ("same.tex", Random(9000, 5)));
        ContentInstaller installer = Installer();
        await installer.InstallAsync(Index, v1, null, null, CancellationToken.None);
        _server.Requests.Clear();

        // A texture grows in the middle of the archive, one file goes, one arrives.
        byte[] grown = [.. level[..7_000_000], .. Random(70_000, 6), .. level[7_000_000..]];
        byte[] added = Random(3000, 7);
        ContentManifest v2 = Publish("1.1.0", ("level.res", grown), ("new.tex", added), ("same.tex", Random(9000, 5)));

        ContentPlan plan = installer.Plan(v2);
        Assert.Equal(["level.res", "new.tex"], plan.Build.Select(static f => f.Path));
        Assert.Equal(["old.tex"], plan.Remove);
        Assert.InRange(plan.Download.Count, 2, 4);
        Assert.True(plan.DownloadBytes < grown.Length / 4, $"{plan.DownloadBytes} bytes to update");

        await installer.InstallAsync(Index, v2, null, null, CancellationToken.None);

        Assert.Equal(grown, Installed("level.res"));
        Assert.Equal(added, Installed("new.tex"));
        Assert.False(File.Exists(Path.Combine(_root, "files", "old.tex")));
        Assert.Equal(plan.Download.Count, ChunkRequests().Count);
        Assert.Equal("1.1.0", installer.Installed()!.Version);
    }

    [Fact]
    public async Task An_interrupted_install_carries_on_without_fetching_a_chunk_twice()
    {
        ContentManifest v1 = Publish("1.0.0", ("level.res", Random(9_000_000, 8)));
        string failing = v1.Files[0].Chunks[^1].Hash;
        bool fail = true;
        _server.Override = request => fail && request.RequestUri!.AbsolutePath.Contains(failing, StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : null;

        ContentInstaller installer = Installer();
        await Assert.ThrowsAsync<HttpRequestException>(() => installer.InstallAsync(Index, v1, null, null, CancellationToken.None));
        Assert.Null(installer.Installed());

        fail = false;
        int before = ChunkRequests().Count;
        ContentPlan resumed = installer.Plan(v1);
        Assert.Single(resumed.Download);

        await installer.InstallAsync(Index, v1, null, null, CancellationToken.None);
        List<string> again = ChunkRequests()[before..];
        Assert.Equal([$"/content/chunks/{failing[..2]}/{failing}.br"], again);
        Assert.Equal("1.0.0", installer.Installed()!.Version);
    }

    [Fact]
    public async Task A_chunk_that_is_not_what_its_name_says_is_refused()
    {
        ContentManifest v1 = Publish("1.0.0", ("level.res", Random(3_000_000, 9)));
        string hash = v1.Files[0].Chunks[0].Hash;
        _server.Add($"/content/chunks/{hash[..2]}/{hash}.br", ContentChunker.Encode(Random(v1.Files[0].Chunks[0].Size, 10)));

        ContentInstaller installer = Installer();
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(Index, v1, null, null, CancellationToken.None));
        Assert.Null(installer.Installed());
    }

    [Fact]
    public async Task A_verify_finds_a_damaged_file_and_a_repair_restores_only_it()
    {
        byte[] level = Random(5_000_000, 11);
        ContentManifest v1 = Publish("1.0.0", ("level.res", level), ("other.res", Random(2_000_000, 12)));
        ContentInstaller installer = Installer();
        await installer.InstallAsync(Index, v1, null, null, CancellationToken.None);
        _server.Requests.Clear();

        string path = Path.Combine(_root, "files", "level.res");
        byte[] tampered = File.ReadAllBytes(path);
        tampered[1234] ^= 0xFF;
        File.WriteAllBytes(path, tampered);

        IReadOnlyList<string> damaged = installer.Verify(v1);
        Assert.Equal(["level.res"], damaged);

        HashSet<string> rebuild = [.. damaged];
        ContentPlan plan = installer.Plan(v1, rebuild);
        Assert.Equal(["level.res"], plan.Build.Select(static f => f.Path));

        await installer.InstallAsync(Index, v1, rebuild, null, CancellationToken.None);

        Assert.Equal(level, Installed("level.res"));
        Assert.Empty(installer.Verify(v1));
        Assert.All(ChunkRequests(), request =>
            Assert.Contains(v1.Files.Single(static f => f.Path == "level.res").Chunks, c => request.Contains(c.Hash, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Removing_a_pack_deletes_its_folder()
    {
        ContentManifest v1 = Publish("1.0.0", ("a.res", Random(1000, 13)));
        ContentInstaller installer = Installer();
        await installer.InstallAsync(Index, v1, null, null, CancellationToken.None);

        installer.Uninstall();

        Assert.False(Directory.Exists(_root));
        Assert.Null(installer.Installed());
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task A_manifest_cannot_write_outside_the_pack()
    {
        ContentManifest v1 = Publish("1.0.0", ("../escape.res", Random(1000, 14)));
        ContentInstaller installer = Installer();

        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(Index, v1, null, null, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(_root, "escape.res")));
    }
}
