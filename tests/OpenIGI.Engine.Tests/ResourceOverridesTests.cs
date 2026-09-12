using OpenIGI.Core.Resources;
using OpenIGI.Engine.Resources;
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
}
