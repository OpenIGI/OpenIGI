using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using OpenIGI.Launcher.Services;
using OpenIGI.Releases;
using Xunit;

namespace OpenIGI.Launcher.Tests;

public sealed class BuildInstallerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("openigi-launcher-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static byte[] Zip(params (string Path, string Text)[] files)
    {
        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, string text) in files)
            {
                using StreamWriter writer = new(zip.CreateEntry(path).Open());
                writer.Write(text);
            }
        }

        return stream.ToArray();
    }

    private static byte[] TarGz(params (string Path, string Text)[] files)
    {
        using MemoryStream stream = new();
        using (GZipStream gzip = new(stream, CompressionLevel.Fastest, leaveOpen: true))
        using (TarWriter tar = new(gzip))
        {
            foreach ((string path, string text) in files)
            {
                PaxTarEntry entry = new(TarEntryType.RegularFile, path)
                {
                    DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)),
                    Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                };
                tar.WriteEntry(entry);
            }
        }

        return stream.ToArray();
    }

    private static ReleaseAsset Asset(string name, byte[] content) => new(
        name, ReleaseComponent.Game, "win-x64", content.Length, Convert.ToHexStringLower(SHA256.HashData(content)),
        $"https://dl.test/releases/{name}");

    private (BuildInstaller Installer, FakeServer Server) Make()
    {
        FakeServer server = new();
        return (new BuildInstaller(new HttpClient(server), _root), server);
    }

    [Fact]
    public async Task A_zip_is_installed_into_its_version_folder_and_becomes_current()
    {
        (BuildInstaller installer, FakeServer server) = Make();
        byte[] archive = Zip(("OpenIGI.Desktop.exe", "game"), ("Assets/fonts/a.ttf", "font"));
        ReleaseAsset asset = Asset("openigi-game-0.1.0-win-x64.zip", archive);
        server.Add("/releases/" + asset.Name, archive);

        Assert.Null(installer.Current());
        InstalledBuild build = await installer.InstallAsync("0.1.0", asset, "OpenIGI.Desktop.exe", new Progress<InstallProgress>(), CancellationToken.None);

        Assert.Equal("0.1.0", installer.Current()!.Version);
        Assert.Equal(Path.Combine(_root, "versions", "0.1.0"), build.Folder);
        Assert.Equal(["Assets/fonts/a.ttf", "OpenIGI.Desktop.exe"], build.Files.Select(static f => f.Path));
        Assert.Equal("game", File.ReadAllText(Path.Combine(build.Folder, "OpenIGI.Desktop.exe")));
        Assert.False(File.Exists(Path.Combine(_root, "downloads", asset.Name)));
        Assert.Empty(BuildInstaller.Verify(build));
    }

    [Fact]
    public async Task A_tarball_keeps_the_executable_bit()
    {
        (BuildInstaller installer, FakeServer server) = Make();
        byte[] archive = TarGz(("OpenIGI.Desktop", "#!/bin/sh"));
        ReleaseAsset asset = Asset("openigi-game-0.1.0-linux-x64.tar.gz", archive);
        server.Add("/releases/" + asset.Name, archive);

        InstalledBuild build = await installer.InstallAsync("0.1.0", asset, "OpenIGI.Desktop.exe", new Progress<InstallProgress>(), CancellationToken.None);

        string executable = Path.Combine(build.Folder, "OpenIGI.Desktop");
        Assert.True(File.Exists(executable));
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.GetUnixFileMode(executable).HasFlag(UnixFileMode.UserExecute));
        }
    }

    [Fact]
    public async Task A_single_file_build_is_installed_under_its_executable_name()
    {
        (BuildInstaller installer, FakeServer server) = Make();
        byte[] executable = [0x4D, 0x5A, 1, 2, 3];
        ReleaseAsset asset = Asset("openigi-game-0.3.0-win-x64-dotnet.exe", executable) with { SelfContained = false };
        server.Add("/releases/" + asset.Name, executable);

        InstalledBuild build = await installer.InstallAsync("0.3.0", asset, "OpenIGI.Desktop.exe", new Progress<InstallProgress>(), CancellationToken.None);

        Assert.Equal(["OpenIGI.Desktop.exe"], build.Files.Select(static f => f.Path));
        Assert.Equal(executable, File.ReadAllBytes(Path.Combine(build.Folder, "OpenIGI.Desktop.exe")));
        Assert.False(installer.Current()!.SelfContained);
        Assert.Empty(BuildInstaller.Verify(build));
    }

    [Fact]
    public async Task A_download_that_does_not_match_its_hash_installs_nothing()
    {
        (BuildInstaller installer, FakeServer server) = Make();
        byte[] archive = Zip(("OpenIGI.Desktop.exe", "game"));
        ReleaseAsset asset = Asset("openigi-game-0.1.0-win-x64.zip", archive);
        server.Add("/releases/" + asset.Name, Zip(("OpenIGI.Desktop.exe", "tampered")));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            installer.InstallAsync("0.1.0", asset with { Size = archive.Length }, "OpenIGI.Desktop.exe", new Progress<InstallProgress>(), CancellationToken.None));

        Assert.Null(installer.Current());
        Assert.False(Directory.Exists(Path.Combine(_root, "versions", "0.1.0")));
    }

    [Fact]
    public async Task A_dropped_download_resumes_from_where_it_stopped()
    {
        (BuildInstaller installer, FakeServer server) = Make();
        byte[] archive = Zip(("OpenIGI.Desktop.exe", Convert.ToBase64String(RandomNumberGenerator.GetBytes(200_000))), ("b.dll", "b"));
        ReleaseAsset asset = Asset("openigi-game-0.2.0-win-x64.zip", archive);
        server.Add("/releases/" + asset.Name, archive);

        server.CutAfter = archive.Length / 3;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            installer.InstallAsync("0.2.0", asset, "OpenIGI.Desktop.exe", new Progress<InstallProgress>(), CancellationToken.None));

        // The cut-off file failed its hash and was thrown away: the next attempt must not trust it.
        server.CutAfter = null;
        InstalledBuild build = await installer.InstallAsync("0.2.0", asset, "OpenIGI.Desktop.exe", new Progress<InstallProgress>(), CancellationToken.None);
        Assert.Equal("0.2.0", build.Version);

        // A partial left by a cancelled download, however, is continued with a range request.
        string partial = Path.Combine(_root, "downloads", asset.Name + ".part");
        File.WriteAllBytes(partial, archive[..1000]);
        server.Requests.Clear();
        await installer.InstallAsync("0.2.0", asset, "OpenIGI.Desktop.exe", new Progress<InstallProgress>(), CancellationToken.None);
        Assert.Equal(1000, server.Requests.Single().Headers.Range!.Ranges.Single().From);
    }

    [Fact]
    public async Task An_update_keeps_the_previous_build_and_drops_older_ones()
    {
        (BuildInstaller installer, FakeServer server) = Make();
        foreach (string version in new[] { "0.1.0", "0.2.0", "0.3.0" })
        {
            byte[] archive = Zip(("OpenIGI.Desktop.exe", version));
            ReleaseAsset asset = Asset($"openigi-game-{version}-win-x64.zip", archive);
            server.Add("/releases/" + asset.Name, archive);
            await installer.InstallAsync(version, asset, "OpenIGI.Desktop.exe", new Progress<InstallProgress>(), CancellationToken.None);
        }

        Assert.Equal("0.3.0", installer.Current()!.Version);
        Assert.Equal(
            ["0.2.0", "0.3.0"],
            Directory.EnumerateDirectories(Path.Combine(_root, "versions")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Switching_back_to_the_kept_build_downloads_nothing()
    {
        (BuildInstaller installer, FakeServer server) = Make();
        ReleaseAsset stable = await Install(installer, server, "0.1.0");
        await Install(installer, server, "0.2.0-beta.1");
        int requests = server.Requests.Count;

        InstalledBuild? build = installer.TryReuse("0.1.0", stable, "OpenIGI.Desktop.exe");

        Assert.NotNull(build);
        Assert.Equal("0.1.0", installer.Current()!.Version);
        Assert.Equal(requests, server.Requests.Count);

        // And the beta it replaced is kept in turn, so switching again is free too.
        Assert.True(Directory.Exists(Path.Combine(_root, "versions", "0.2.0-beta.1")));
    }

    [Fact]
    public async Task A_kept_build_that_was_changed_is_not_reused()
    {
        (BuildInstaller installer, FakeServer server) = Make();
        ReleaseAsset stable = await Install(installer, server, "0.1.0");
        await Install(installer, server, "0.2.0-beta.1");
        File.WriteAllText(Path.Combine(_root, "versions", "0.1.0", "OpenIGI.Desktop.exe"), "tampered");

        Assert.Null(installer.TryReuse("0.1.0", stable, "OpenIGI.Desktop.exe"));
        Assert.Equal("0.2.0-beta.1", installer.Current()!.Version);
    }

    [Fact]
    public async Task A_single_file_build_kept_without_a_record_is_recognised_by_the_release_hash()
    {
        (BuildInstaller installer, FakeServer server) = Make();
        byte[] executable = [0x4D, 0x5A, 0, 1];
        ReleaseAsset stable = Asset("openigi-game-0.1.0-win-x64.exe", executable);
        server.Add("/releases/" + stable.Name, executable);
        await installer.InstallAsync("0.1.0", stable, "OpenIGI.Desktop.exe", new Progress<InstallProgress>(), CancellationToken.None);
        await Install(installer, server, "0.2.0-beta.1");

        // As a launcher from before build records left it.
        File.Delete(Path.Combine(_root, "versions", "0.1.0.json"));

        Assert.NotNull(installer.TryReuse("0.1.0", stable, "OpenIGI.Desktop.exe"));
        Assert.Equal("0.1.0", installer.Current()!.Version);
    }

    [Fact]
    public void A_build_not_on_disk_is_not_reused()
    {
        (BuildInstaller installer, _) = Make();
        Assert.Null(installer.TryReuse("0.1.0", Asset("openigi-game-0.1.0-win-x64.zip", [1]), "OpenIGI.Desktop.exe"));
    }

    private static async Task<ReleaseAsset> Install(BuildInstaller installer, FakeServer server, string version)
    {
        byte[] archive = Zip(("OpenIGI.Desktop.exe", version));
        ReleaseAsset asset = Asset($"openigi-game-{version}-win-x64.zip", archive);
        server.Add("/releases/" + asset.Name, archive);
        await installer.InstallAsync(version, asset, "OpenIGI.Desktop.exe", new Progress<InstallProgress>(), CancellationToken.None);
        return asset;
    }

    [Fact]
    public void CleanDownloads_keeps_only_the_offered_release_and_its_partial()
    {
        (BuildInstaller installer, _) = Make();
        string downloads = Directory.CreateDirectory(Path.Combine(_root, "downloads")).FullName;
        File.WriteAllBytes(Path.Combine(downloads, "openigi-game-0.1.0-win-x64.exe"), new byte[100]);
        File.WriteAllBytes(Path.Combine(downloads, "openigi-game-0.1.0-win-x64.exe.part"), new byte[50]);
        File.WriteAllBytes(Path.Combine(downloads, "openigi-game-0.2.0-win-x64.exe.part"), new byte[30]);

        long freed = installer.CleanDownloads("openigi-game-0.2.0-win-x64.exe");

        Assert.Equal(150, freed);
        Assert.Equal(["openigi-game-0.2.0-win-x64.exe.part"], Directory.EnumerateFiles(downloads).Select(Path.GetFileName));
    }

    [Fact]
    public void CleanDownloads_with_nothing_on_offer_removes_everything()
    {
        (BuildInstaller installer, _) = Make();
        string downloads = Directory.CreateDirectory(Path.Combine(_root, "downloads")).FullName;
        File.WriteAllBytes(Path.Combine(downloads, "stale.zip"), new byte[10]);

        Assert.Equal(10, installer.CleanDownloads(null));
        Assert.Empty(Directory.EnumerateFiles(downloads));
        Assert.Equal(0, installer.CleanDownloads(null));
    }

    [Fact]
    public async Task Verify_finds_a_missing_or_changed_file()
    {
        (BuildInstaller installer, FakeServer server) = Make();
        byte[] archive = Zip(("OpenIGI.Desktop.exe", "game"), ("a.dll", "a"), ("b.dll", "b"));
        ReleaseAsset asset = Asset("openigi-game-0.1.0-win-x64.zip", archive);
        server.Add("/releases/" + asset.Name, archive);
        InstalledBuild build = await installer.InstallAsync("0.1.0", asset, "OpenIGI.Desktop.exe", new Progress<InstallProgress>(), CancellationToken.None);

        File.Delete(Path.Combine(build.Folder, "a.dll"));
        File.WriteAllText(Path.Combine(build.Folder, "b.dll"), "B");

        Assert.Equal(["a.dll", "b.dll"], BuildInstaller.Verify(build));
    }

    [Fact]
    public void A_damaged_pointer_reads_as_nothing_installed()
    {
        (BuildInstaller installer, _) = Make();
        File.WriteAllText(Path.Combine(_root, "current.json"), "{ not json");
        Assert.Null(installer.Current());
    }
}
