using OpenIGI.Core.Resources;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Ilff;
using Xunit;

namespace OpenIGI.Engine.Tests;

/// <summary>
/// Pins where a replacement asset has to be put, and what happens when one appears or goes.
/// </summary>
/// <remarks>
/// The overrides folder mirrors the installation: a file at the path an asset lives at replaces that
/// asset. For a loose file that is simply its own path. For one inside a <c>.res</c> archive it is the
/// archive's directory plus the file's name — which is both where a person would look for it and the only
/// key that can tell two assets apart when their names cannot, as 79 of this game's texture names cannot.
/// </remarks>
public sealed class ResourceOverridesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("openigi-overrides").FullName;

    /// <summary>An archived entry is addressed by its archive's directory, not by its own path.</summary>
    /// <remarks>
    /// The entry paths inside these archives are relative to bases that differ between archives — a
    /// level's entries are named <c>textures/x.tex</c> while the archive itself sits in
    /// <c>missions/.../level10/textures/</c>, and the shared archive's entries carry the full
    /// <c>common/textures/</c> prefix. Neither base is recoverable in general. The archive's own location
    /// is, and names are unique within one archive, so that is what the path is built from.
    /// </remarks>
    [Theory]
    [InlineData(
        "LOCAL:missions/location0/level10/textures/level10.res",
        "LOCAL:textures/000_04_1.tex",
        "missions/location0/level10/textures/000_04_1.tex")]
    [InlineData(
        "LOCAL:common/textures/textures.res",
        "LOCAL:common/textures/000_04_1.tex",
        "common/textures/000_04_1.tex")]
    [InlineData(
        "missions\\location0\\level1\\textures\\level1.res",
        "textures\\401_04_1.tex",
        "missions/location0/level1/textures/401_04_1.tex")]
    public void AnArchivedEntryIsAddressedByItsArchivesDirectory(
        string archive, string entry, string expected)
    {
        Assert.Equal(expected, OverridePath.For(archive, entry));
    }

    /// <summary>Two archives shipping one name give two different override paths.</summary>
    /// <remarks>
    /// The reason this scheme exists. A level override and the shared asset have the same NAME and must
    /// be replaceable independently; keyed by name there would be one slot for both.
    /// </remarks>
    [Fact]
    public void OneNameInTwoArchivesGivesTwoPaths()
    {
        string shared = OverridePath.For(
            "LOCAL:common/textures/textures.res", "LOCAL:common/textures/000_04_1.tex");
        string level = OverridePath.For(
            "LOCAL:missions/location0/level10/textures/level10.res", "LOCAL:textures/000_04_1.tex");

        Assert.NotEqual(shared, level);
    }

    /// <summary>A loose file is addressed by its own path, with the device alias dropped.</summary>
    [Theory]
    [InlineData("LOCAL:missions/location0/level1/terrain/terrain.tex")]
    [InlineData("missions/location0/level1/terrain/terrain.tex")]
    public void ALooseFileIsAddressedByItsOwnPath(string resolved)
    {
        Assert.Equal(
            "missions/location0/level1/terrain/terrain.tex", OverridePath.For(resolved));
    }

    /// <summary>A scan reports what moved, so a rescan can say whether it is worth reloading anything.</summary>
    [Fact]
    public void AScanReportsWhatMoved()
    {
        ResourceOverrides overrides = new(_root);

        Assert.False(overrides.Rescan().Moved);

        Write("common/textures/a.tex", [1, 2, 3]);
        ResourceOverrides.ScanResult added = overrides.Rescan();

        Assert.Equal(1, added.Total);
        Assert.Equal(1, added.Added);
        Assert.True(added.Moved);

        Write("common/textures/a.tex", [4, 5, 6, 7]);
        ResourceOverrides.ScanResult changed = overrides.Rescan();

        Assert.Equal(1, changed.Changed);
        Assert.Equal(0, changed.Added);

        File.Delete(Path.Combine(_root, "common", "textures", "a.tex"));
        ResourceOverrides.ScanResult removed = overrides.Rescan();

        Assert.Equal(0, removed.Total);
        Assert.Equal(1, removed.Removed);
    }

    /// <summary>An edited file must be re-read, not served from the first scan's copy.</summary>
    /// <remarks>
    /// The bytes are cached, because the registry holds them for the life of an entry and they cannot be
    /// a transient buffer. The cache is keyed on the file's length and timestamp so that editing a file
    /// in place — which is exactly what somebody iterating on a texture does — is picked up.
    /// </remarks>
    [Fact]
    public void AnEditedOverrideIsReReadRatherThanRememberedWrong()
    {
        ResourceOverrides overrides = new(_root);

        Write("common/textures/a.tex", [1, 1, 1]);
        overrides.Rescan();
        Assert.True(overrides.TryRead("common/textures/a.tex", out ReadOnlyMemory<byte> first));
        Assert.Equal(3, first.Length);

        Write("common/textures/a.tex", [2, 2, 2, 2, 2]);
        overrides.Rescan();
        Assert.True(overrides.TryRead("common/textures/a.tex", out ReadOnlyMemory<byte> second));
        Assert.Equal(5, second.Length);
    }

    /// <summary>No folder at all is the ordinary installation, and must not be an error.</summary>
    [Fact]
    public void AnInstallationWithNoOverridesScansCleanly()
    {
        ResourceOverrides absent = new(Path.Combine(_root, "not-here"));

        ResourceOverrides.ScanResult scan = absent.Rescan();

        Assert.Equal(0, scan.Total);
        Assert.False(absent.IsActive);
        Assert.False(absent.Has("common/textures/a.tex"));
    }

    /// <summary>Releases the temporary directory.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a test run over.
        }
    }

    private void Write(string relative, byte[] bytes)
    {
        string path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>An archive in the folder overrides the same paths its loose entries would have.</summary>
    /// <remarks>
    /// The point of the whole arrangement. A generated pack ships the game's own file types, and the
    /// loader is not told a second idea of what an override is: the archive's entries are registered at
    /// exactly the paths individual files would have claimed, so everything downstream is unchanged.
    /// </remarks>
    [Fact]
    public void AnArchiveContributesItsEntriesAsOverrides()
    {
        WriteArchive(
            "common/textures/textures.res",
            ("LOCAL:common/textures/water_igi.tex", [1, 2, 3]),
            ("LOCAL:common/textures/shadow1.tex", [4, 5]));

        ResourceOverrides overrides = new(_root);
        ResourceOverrides.ScanResult result = overrides.Rescan();

        Assert.Equal(2, result.Total);
        Assert.True(overrides.Has("common/textures/water_igi.tex"));
        Assert.True(overrides.TryRead("common/textures/shadow1.tex", out ReadOnlyMemory<byte> data));
        Assert.Equal(new byte[] { 4, 5 }, data.ToArray());
    }

    /// <summary>The archive file itself is not offered as an asset.</summary>
    /// <remarks>
    /// Registering it under its own path as well would mean an override for
    /// <c>common/textures/textures.res</c> -- which the game does load, as an archive -- and it would be
    /// this overlay, holding a fraction of the entries. Everything not in the pack would vanish.
    /// </remarks>
    [Fact]
    public void TheArchiveItselfIsNotAnOverride()
    {
        WriteArchive("common/textures/textures.res", ("LOCAL:common/textures/a.tex", [1]));

        ResourceOverrides overrides = new(_root);
        _ = overrides.Rescan();

        Assert.False(overrides.Has("common/textures/textures.res"));
    }

    /// <summary>A loose file beats an archived entry for the same path.</summary>
    /// <remarks>
    /// Someone who drops a single file next to a generated pack is correcting that pack. The specific,
    /// hand-placed thing is what they meant, whichever order the directory happens to enumerate in.
    /// </remarks>
    [Fact]
    public void ALooseFileWinsOverAnArchivedOne()
    {
        WriteArchive("common/textures/textures.res", ("LOCAL:common/textures/a.tex", [9, 9, 9]));
        Write("common/textures/a.tex", [7]);

        ResourceOverrides overrides = new(_root);
        _ = overrides.Rescan();

        Assert.True(overrides.TryRead("common/textures/a.tex", out ReadOnlyMemory<byte> data));
        Assert.Equal(new byte[] { 7 }, data.ToArray());
    }

    /// <summary>A rewritten archive is noticed by a rescan.</summary>
    [Fact]
    public void RewritingAnArchiveShowsUpAsAChange()
    {
        WriteArchive("common/textures/textures.res", ("LOCAL:common/textures/a.tex", [1, 2]));

        ResourceOverrides overrides = new(_root);
        _ = overrides.Rescan();
        Assert.True(overrides.TryRead("common/textures/a.tex", out ReadOnlyMemory<byte> first));
        Assert.Equal(new byte[] { 1, 2 }, first.ToArray());

        WriteArchive("common/textures/textures.res", ("LOCAL:common/textures/a.tex", [3, 4, 5]));

        ResourceOverrides.ScanResult result = overrides.Rescan();

        Assert.Equal(1, result.Changed);
        Assert.True(overrides.TryRead("common/textures/a.tex", out ReadOnlyMemory<byte> second));
        Assert.Equal(new byte[] { 3, 4, 5 }, second.ToArray());
    }

    /// <summary>A file that is not an archive at all is reported, not thrown.</summary>
    [Fact]
    public void ARubbishArchiveIsSkipped()
    {
        Write("common/textures/broken.res", [0, 1, 2, 3, 4, 5, 6, 7]);

        ResourceOverrides overrides = new(_root);
        ResourceOverrides.ScanResult result = overrides.Rescan();

        Assert.Equal(0, result.Total);
    }

    /// <summary>Reading from several threads while the folder is rescanned is safe and stays correct.</summary>
    /// <remarks>
    /// Not a hypothetical: a menu backdrop builds its level on a worker thread, and the registry it builds
    /// into resolves every archived entry through this store — while the render thread is free to rescan
    /// the folder from the console or take the pack out of the frame. A read mutates too, because a
    /// candidate caches its bytes the first time it is asked for, so the two collide on writes and not
    /// merely on reads.
    /// </remarks>
    [Fact]
    public void ReadingWhileRescanningIsSafe()
    {
        (string Name, byte[] Data)[] entries =
            [.. Enumerable.Range(0, 64).Select(i => ($"LOCAL:common/textures/{i}.tex", new byte[] { (byte)i, 7 }))];

        WriteArchive("common/textures/textures.res", entries);

        ResourceOverrides overrides = new(_root);
        _ = overrides.Rescan();

        List<Exception> failures = [];
        int wrong = 0;

        Parallel.For(0, 8, worker =>
        {
            try
            {
                for (int pass = 0; pass < 200; pass++)
                {
                    if (worker == 0)
                    {
                        _ = overrides.Rescan();
                        continue;
                    }

                    int index = (pass + worker) % entries.Length;

                    if (!overrides.TryRead($"common/textures/{index}.tex", out ReadOnlyMemory<byte> data)
                        || data.Span[0] != (byte)index)
                    {
                        Interlocked.Increment(ref wrong);
                    }
                }
            }
            catch (Exception error)
            {
                lock (failures)
                {
                    failures.Add(error);
                }
            }
        });

        Assert.Empty(failures);
        Assert.Equal(0, wrong);
        Assert.Equal(entries.Length, overrides.Count);
    }

    private void WriteArchive(string relative, params (string Name, byte[] Data)[] entries)
    {
        Write(
            relative,
            IresWriter.Write(
                [.. entries.Select(entry => new IresWriter.Entry(entry.Name, IresEntryKind.Body, entry.Data))]));
    }
}
