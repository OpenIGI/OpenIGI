using OpenIGI.Engine.Vfs;
using Xunit;

namespace OpenIGI.Engine.Tests;

/// <summary>
/// Covers the case-folded fallback: the game's data names its own files in whatever case the author
/// typed, and on a case-sensitive filesystem the exact name is not enough to find them.
/// </summary>
/// <remarks>
/// These pass on Windows and on a default macOS volume for the uninteresting reason that the filesystem
/// itself folds case, and on Linux — or a case-sensitive APFS volume — because the fallback does. That
/// is the point: the same assertions hold either way, so the tests are meaningful wherever they run
/// without needing to know which filesystem they landed on.
/// </remarks>
public sealed class LocalFileDeviceCaseTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("openigi-case").FullName;

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void FindsAFileWhoseNameWasAskedForInAnotherCase()
    {
        Write("pc/GRAPH.RES", "graph");
        LocalFileDevice device = new(_root);

        Assert.True(device.Exists("pc/graph.res"));
        Assert.True(device.TryOpenRead("pc/graph.res", out Stream? stream));
        using (stream)
        {
            Assert.NotNull(stream);
        }
    }

    [Fact]
    public void FoldsEveryDirectoryOnTheWayDownAndNotJustTheFileName()
    {
        Write("Missions/Location0/Level1/objects.qvm", "objects");
        LocalFileDevice device = new(_root);

        Assert.True(device.Exists("missions/location0/level1/objects.qvm"));
    }

    [Fact]
    public void PrefersTheExactNameWhereBothCasesExist()
    {
        // Only reachable on a case-sensitive filesystem; where the platform folds case the second write
        // overwrites the first and there is one file, which the exact lookup still finds.
        Write("pc/graph.res", "lower");
        Write("pc/GRAPH.RES", "upper");
        LocalFileDevice device = new(_root);

        Assert.True(device.TryOpenRead("pc/graph.res", out Stream? stream));
        using StreamReader reader = new(stream!);
        Assert.Equal(File.ReadAllText(Path.Combine(_root, "pc", "graph.res")), reader.ReadToEnd());
    }

    [Fact]
    public void StillReportsAFileThatIsGenuinelyAbsent()
    {
        Write("pc/GRAPH.RES", "graph");
        LocalFileDevice device = new(_root);

        Assert.False(device.Exists("pc/missing.res"));
        Assert.False(device.TryOpenRead("pc/missing.res", out Stream? stream));
        Assert.Null(stream);
    }

    [Fact]
    public void EnumeratesInsideADirectoryNamedInAnotherCase()
    {
        Write("PC/one.res", "one");
        Write("PC/two.res", "two");
        LocalFileDevice device = new(_root);

        Assert.Equal(2, device.EnumerateFiles("pc", "*.res").Count());
    }

    [Fact]
    public void ResolvesAHostPathAsTheDiskSpellsIt()
    {
        // Both I.G.I. 2 releases ship upper-case top-level folders; the code asks for them in lower case.
        Write("MISSIONS/location1/common/location1.mtp", "pack");

        string? directory = LocalFileDevice.ResolveExisting(_root, "missions");
        string? file = LocalFileDevice.ResolveExisting(_root, "missions/LOCATION1/common/location1.mtp");

        Assert.NotNull(directory);
        // Not the spelling: where the host folds case the exact lookup already succeeds in the asked case.
        Assert.True(Directory.Exists(directory));
        Assert.NotNull(file);
        Assert.Equal("pack", File.ReadAllText(file));
    }

    [Fact]
    public void ResolvesNothingForAPathThatIsGenuinelyAbsent()
    {
        Write("MISSIONS/location1/common/location1.mtp", "pack");

        Assert.Null(LocalFileDevice.ResolveExisting(_root, "missions/location9"));
        Assert.Null(LocalFileDevice.ResolveExisting(_root, "common/anims/000.iff"));
    }

    private void Write(string relative, string content)
    {
        string full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }
}
