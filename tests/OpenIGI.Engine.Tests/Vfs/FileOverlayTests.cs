using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Engine.Tests.Vfs;

/// <summary>A content layer's file stands in for the installation's at the same path for every reader of the file system.</summary>
public sealed class FileOverlayTests : IDisposable
{
    private readonly string _layer = Directory.CreateTempSubdirectory("openigi-overlay").FullName;

    [Fact]
    public void A_loose_file_in_a_layer_is_what_the_file_system_reads()
    {
        (VirtualFileSystem files, _) = Mount(("menusystem/mainmenu.qvm", [1, 1]));
        Layer("menusystem/mainmenu.qvm", [9, 9, 9]);
        Overlay(files);

        Assert.Equal([9, 9, 9], files.ReadAllBytes("LOCAL:menusystem/mainmenu.qvm"));
    }

    [Fact]
    public void A_file_only_a_layer_has_is_found_and_read()
    {
        (VirtualFileSystem files, _) = Mount();
        Layer("missions/location9/level1/mission.qvm", [4]);
        Overlay(files);

        Assert.True(files.Exists("LOCAL:missions/location9/level1/mission.qvm"));
        Assert.Equal([4], files.ReadAllBytes("LOCAL:missions/location9/level1/mission.qvm"));
    }

    [Fact]
    public void A_bypassed_layer_stands_in_for_nothing()
    {
        (VirtualFileSystem files, _) = Mount(("menusystem/mainmenu.qvm", [1]));
        Layer("menusystem/mainmenu.qvm", [9]);
        ResourceOverrides overrides = Overlay(files);
        overrides.Bypassed = true;

        Assert.Equal([1], files.ReadAllBytes("LOCAL:menusystem/mainmenu.qvm"));
    }

    [Fact]
    public void An_archive_is_read_whole_with_the_entries_a_layer_replaces()
    {
        byte[] archive = new IlffWriter("IRES")
            .AddName("LOCAL:common/sprites/a.tex")
            .AddBody([1, 2])
            .AddName("LOCAL:common/sprites/b.tex")
            .AddBody([3])
            .ToArray();
        (VirtualFileSystem files, _) = Mount(("common/sprites/sprites.res", archive));
        Layer("common/sprites/b.tex", [7, 7]);
        Overlay(files);

        IresArchive read = files.ReadArchive("LOCAL:common/sprites/sprites.res");

        Assert.Equal([1, 2], read.FindBody("a.tex")!.Data.ToArray());
        Assert.Equal([7, 7], read.FindBody("b.tex")!.Data.ToArray());
        Assert.Equal(archive, files.ReadAllBytes("LOCAL:common/sprites/sprites.res"));
    }

    public void Dispose() => Directory.Delete(_layer, recursive: true);

    private static (VirtualFileSystem Files, MemoryFileDevice Device) Mount(params (string Path, byte[] Data)[] installed)
    {
        MemoryFileDevice device = new();
        foreach ((string path, byte[] data) in installed)
        {
            device.Add(path, data);
        }

        VirtualFileSystem files = new();
        files.RegisterDevice(device);
        return (files, device);
    }

    private void Layer(string path, byte[] data)
    {
        string file = Path.Combine(_layer, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, data);
    }

    // The layer stands in through the registry, which hands its overrides to the file system it reads.
    private ResourceOverrides Overlay(VirtualFileSystem files)
    {
        ResourceOverrides overrides = new([new OverrideLayer("mod", "Mod", _layer)]);
        overrides.Rescan();
        _ = new ResourceRegistry(files) { Overrides = overrides };
        return overrides;
    }
}
