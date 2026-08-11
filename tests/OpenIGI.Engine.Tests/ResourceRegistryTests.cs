using System.Buffers.Binary;
using System.Text;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Engine.Tests;

/// <summary>Covers container detection, exploding and the registry-before-disk lookup order.</summary>
public sealed class ResourceRegistryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("openigi-res").FullName;

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void RegistersAnUnknownBufferAsOneOpaqueResource()
    {
        ResourceRegistry registry = CreateRegistry();

        ResourceEntry entry = registry.Register("LOCAL:common/textures/a.tex", "LOOP\0\0\0\0"u8.ToArray());

        Assert.Equal(ResourceKind.RawFile, entry.Kind);
        Assert.Equal(1, registry.Count);
        Assert.Null(entry.Parent);
    }

    [Fact]
    public void ExplodesAnArchiveIntoItsNamedEntries()
    {
        ResourceRegistry registry = CreateRegistry();
        byte[] archive = new IlffWriter("IRES")
            .AddName("LOCAL:common/textures/a.tex")
            .AddBody([1, 2, 3])
            .AddName("LOCAL:common/textures/b.tex")
            .AddBody([4])
            .ToArray();

        ResourceEntry container = registry.Register("LOCAL:common/textures/textures.res", archive);

        Assert.Equal(ResourceKind.Container, container.Kind);
        Assert.Equal(3, registry.Count);

        Assert.True(registry.TryGetEntry("LOCAL:common/textures/a.tex", out ResourceEntry? first));
        Assert.Equal(ResourceKind.Body, first.Kind);
        Assert.Equal([1, 2, 3], first.Data.ToArray());
        Assert.Equal("LOCAL:common/textures/textures.res", first.Parent);
    }

    [Fact]
    public void KeepsContextStringEntriesDistinctFromBodies()
    {
        ResourceRegistry registry = CreateRegistry();
        byte[] archive = new IlffWriter("IRES")
            .AddName("OBJ_1_COMPLETE")
            .AddContextString("Objective 1 Complete.")
            .ToArray();

        registry.Register("LANGUAGE:objectives.res", archive);

        Assert.True(registry.TryGetEntry("OBJ_1_COMPLETE", out ResourceEntry? entry));
        Assert.Equal(ResourceKind.ContextString, entry.Kind);
    }

    [Fact]
    public void RegistersAModelContainerWhole()
    {
        // A .mef is an ILFF container as well, but nothing inside it is addressable by name.
        ResourceRegistry registry = CreateRegistry();
        byte[] model = new IlffWriter("OCEM").AddChunk("HSEM", [1, 2, 3, 4]).ToArray();

        ResourceEntry entry = registry.Register("LOCAL:models/level1.mef", model);

        Assert.Equal(ResourceKind.RawFile, entry.Kind);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void ExplodesAFlatResourceArchive()
    {
        ResourceRegistry registry = CreateRegistry();
        byte[] archive = new FlatResourceWriter()
            .Add("first.dat", [1, 2])
            .Add("second.dat", [3])
            .ToArray();

        ResourceEntry container = registry.Register("LOCAL:dev/pack.res", archive);

        Assert.Equal(ResourceKind.Container, container.Kind);
        Assert.True(registry.TryGetEntry("first.dat", out ResourceEntry? first));
        Assert.Equal([1, 2], first.Data.ToArray());
        Assert.True(registry.TryGetEntry("second.dat", out ResourceEntry? second));
        Assert.Equal([3], second.Data.ToArray());
    }

    [Fact]
    public void ChecksTheRegistryBeforeTheDisk()
    {
        Write("common/textures/a.tex", "on disk");

        ResourceRegistry registry = CreateRegistry();
        registry.Register("LOCAL:common/textures/a.tex", "registered"u8.ToArray());

        Assert.Equal("registered", LoadText(registry, "LOCAL:common/textures/a.tex"));
    }

    [Fact]
    public void FallsBackToTheDiskAndCachesTheResult()
    {
        Write("common/textures/a.tex", "on disk");

        ResourceRegistry registry = CreateRegistry();
        Assert.Equal("on disk", LoadText(registry, "LOCAL:common/textures/a.tex"));
        Assert.Equal(1, registry.Count);

        File.Delete(Path.Combine(_root, "common/textures/a.tex"));
        Assert.Equal("on disk", LoadText(registry, "LOCAL:common/textures/a.tex"));
    }

    [Fact]
    public void AliasDiskCacheFollowsARebinding()
    {
        Write("missions/level1/AI/2220.qvm", "level one");
        Write("missions/level2/AI/2220.qvm", "level two");

        var fileSystem = new VirtualFileSystem();
        fileSystem.RegisterDevice(new LocalFileDevice(_root));
        fileSystem.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/level1/");
        var registry = new ResourceRegistry(fileSystem);

        Assert.Equal("level one", LoadText(registry, "MISSION:AI/2220.qvm"));

        fileSystem.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/level2/");

        Assert.Equal("level two", LoadText(registry, "MISSION:AI/2220.qvm"));
        Assert.True(registry.TryGetEntry("LOCAL:missions/level1/AI/2220.qvm", out _));
        Assert.True(registry.TryGetEntry("LOCAL:missions/level2/AI/2220.qvm", out _));
        Assert.False(registry.TryGetEntry("MISSION:AI/2220.qvm", out _));
    }

    [Fact]
    public void ExplodesAnArchiveItLoadsFromDisk()
    {
        byte[] archive = new IlffWriter("IRES")
            .AddName("LOCAL:common/textures/a.tex")
            .AddBody([7, 7, 7])
            .ToArray();
        WriteBytes("common/textures/textures.res", archive);

        ResourceRegistry registry = CreateRegistry();
        registry.Load("LOCAL:common/textures/textures.res");

        Assert.True(registry.TryGetEntry("LOCAL:common/textures/a.tex", out ResourceEntry? entry));
        Assert.Equal([7, 7, 7], entry.Data.ToArray());
    }

    [Fact]
    public void FreeingAnArchiveRemovesItsEntries()
    {
        ResourceRegistry registry = CreateRegistry();
        byte[] archive = new IlffWriter("IRES").AddName("a.tex").AddBody([1]).ToArray();
        registry.Register("LOCAL:pack.res", archive);
        registry.Register("LOCAL:standalone.dat", new byte[] { 9 });

        Assert.True(registry.Free("LOCAL:pack.res"));

        Assert.False(registry.TryGetEntry("a.tex", out _));
        Assert.True(registry.TryGetEntry("LOCAL:standalone.dat", out _));
        Assert.False(registry.Free("LOCAL:pack.res"));
    }

    [Fact]
    public void KeepsTheFirstRegistrationOfADuplicateName()
    {
        ResourceRegistry registry = CreateRegistry();
        registry.Register("LOCAL:a.dat", new byte[] { 1 });
        registry.Register("LOCAL:a.dat", new byte[] { 2 });

        Assert.Equal(1, registry.Count);
        Assert.True(registry.TryGetEntry("LOCAL:a.dat", out ResourceEntry? entry));
        Assert.Equal([1], entry.Data.ToArray());
    }

    [Fact]
    public void CanReplaceEntriesWhenAnArchiveRebindsAResourceNamespace()
    {
        ResourceRegistry registry = CreateRegistry();
        byte[] first = new IlffWriter("IRES")
            .AddName("LOCAL:textures/015_05_1.tex")
            .AddBody([1])
            .ToArray();
        byte[] second = new IlffWriter("IRES")
            .AddName("LOCAL:textures/015_05_1.tex")
            .AddBody([2])
            .ToArray();

        registry.Register("LOCAL:level1/textures/level1.res", first);
        registry.Register("LOCAL:level8/textures/level8.res", second, replaceExisting: true);

        Assert.True(registry.TryGetEntry("LOCAL:textures/015_05_1.tex", out ResourceEntry? entry));
        Assert.Equal([2], entry.Data.ToArray());
    }

    [Fact]
    public void NormalizesBackslashesInNames()
    {
        ResourceRegistry registry = CreateRegistry();
        registry.Register(@"LOCAL:menusystem\mainmenu.qsc", new byte[] { 1 });

        Assert.True(registry.TryGetEntry("LOCAL:menusystem/mainmenu.qsc", out _));
    }

    [Fact]
    public void MissingResourcesAreFatal()
    {
        ResourceRegistry registry = CreateRegistry();

        Assert.False(registry.TryLoad("LOCAL:absent.dat", out _));
        Assert.Throws<EngineFatalException>(() => registry.Load("LOCAL:absent.dat"));
    }

    [Fact]
    public void ACorruptArchiveIsFatal()
    {
        ResourceRegistry registry = CreateRegistry();
        byte[] archive = new IlffWriter("IRES").AddName("a.tex").AddBody([1]).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(8), 6);

        EngineFatalException error = Assert.Throws<EngineFatalException>(
            () => registry.Register("LOCAL:pack.res", archive));
        Assert.Contains("Error in resource file", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearRemovesEverything()
    {
        ResourceRegistry registry = CreateRegistry();
        registry.Register("LOCAL:a.dat", new byte[] { 1 });
        registry.Clear();

        Assert.Equal(0, registry.Count);
    }

    private ResourceRegistry CreateRegistry()
    {
        var vfs = new VirtualFileSystem();
        vfs.RegisterDevice(new LocalFileDevice(_root));
        return new ResourceRegistry(vfs);
    }

    private static string LoadText(ResourceRegistry registry, string name) =>
        Encoding.UTF8.GetString(registry.Load(name).Span);

    private void Write(string relativePath, string contents) =>
        WriteBytes(relativePath, Encoding.UTF8.GetBytes(contents));

    private void WriteBytes(string relativePath, byte[] contents)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, contents);
    }
}
