using System.Text;
using OpenIGI.Engine.Vfs;
using Xunit;

namespace OpenIGI.Engine.Tests;

/// <summary>
/// The in-memory device answers every question the disk device does, the same way, for the same tree --
/// which is what lets a browser's installation stand in for one on disk.
/// </summary>
public sealed class MemoryFileDeviceTests : IDisposable
{
    private static readonly string[] Tree =
    [
        "pc/GRAPH.RES",
        "Missions/Location0/Level1/objects.qvm",
        "Missions/Location0/Level1/terrain/terrain.qvm",
        "Missions/Location0/Level2/objects.qvm",
        "common/sounds/a.wav",
        "common/sounds/B.WAV",
    ];

    private readonly string _root = Directory.CreateTempSubdirectory("openigi-memory").FullName;

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_file_reads_back_the_bytes_it_was_given_whatever_the_case_it_is_asked_for_in()
    {
        MemoryFileDevice device = new();
        device.Add("pc/GRAPH.RES", Encoding.ASCII.GetBytes("graph"));

        Assert.True(device.Exists("PC\\graph.res"));
        Assert.True(device.TryOpenRead("pc/graph.res", out Stream? stream));

        using (stream)
        using (StreamReader reader = new(stream!))
        {
            Assert.Equal("graph", reader.ReadToEnd());
        }

        Assert.False(device.TryOpenRead("pc/missing.res", out Stream? none));
        Assert.Null(none);
    }

    [Fact]
    public void Its_streams_seek_like_a_files()
    {
        MemoryFileDevice device = new();
        device.Add("data.bin", new byte[] { 1, 2, 3, 4, 5 });

        Assert.True(device.TryOpenRead("data.bin", out Stream? opened));
        using Stream stream = opened!;

        stream.Seek(-2, SeekOrigin.End);
        Assert.Equal(4, stream.ReadByte());

        stream.Position = 1;
        byte[] two = new byte[2];
        Assert.Equal(2, stream.Read(two, 0, 2));
        Assert.Equal([2, 3], two);
        Assert.False(stream.CanWrite);
    }

    [Theory]
    [InlineData("", "*")]
    [InlineData("common/sounds", "*.wav")]
    [InlineData("MISSIONS/location0/level1", "*.QVM")]
    [InlineData("Missions/Location0", "*")]
    public void It_lists_what_the_disk_device_lists_for_the_same_tree(string directory, string pattern)
    {
        (LocalFileDevice disk, MemoryFileDevice memory) = Both();

        Assert.Equal(Sorted(disk.EnumerateFiles(directory, pattern)), Sorted(memory.EnumerateFiles(directory, pattern)));
        Assert.Equal(Sorted(disk.EnumerateDirectories(directory)), Sorted(memory.EnumerateDirectories(directory)));
    }

    [Fact]
    public void A_file_added_later_exists_from_then_and_one_removed_is_gone()
    {
        MemoryFileDevice device = new();
        Assert.False(device.Exists("Missions/Location0/Level2/objects.qvm"));

        device.Add("Missions/Location0/Level2/objects.qvm", new byte[] { 7 });
        Assert.True(device.Exists("missions/location0/level2/objects.qvm"));
        Assert.Equal(1, device.Count);

        Assert.True(device.Remove("MISSIONS/Location0/Level2/objects.qvm"));
        Assert.False(device.Exists("Missions/Location0/Level2/objects.qvm"));
    }

    [Fact]
    public void Mounted_in_the_virtual_file_system_it_is_the_installation()
    {
        MemoryFileDevice device = new();
        device.Add("pc/graph.res", Encoding.ASCII.GetBytes("graph"));

        VirtualFileSystem fileSystem = new(OpenIGI.Core.Diagnostics.NullLogger.Instance);
        fileSystem.RegisterDevice(device);

        Assert.True(fileSystem.Exists("LOCAL:pc/GRAPH.RES"));
    }

    private (LocalFileDevice Disk, MemoryFileDevice Memory) Both()
    {
        MemoryFileDevice memory = new();

        foreach (string path in Tree)
        {
            string full = Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, path);
            memory.Add(path, Encoding.UTF8.GetBytes(path));
        }

        return (new LocalFileDevice(_root), memory);
    }

    private static string[] Sorted(IEnumerable<string> paths) =>
        [.. paths.Select(path => path.ToUpperInvariant()).Order(StringComparer.Ordinal)];
}
