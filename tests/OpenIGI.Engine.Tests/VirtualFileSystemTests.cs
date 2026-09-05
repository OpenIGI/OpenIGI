using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Vfs;
using Xunit;

namespace OpenIGI.Engine.Tests;

/// <summary>Covers alias substitution, search lists and device dispatch.</summary>
public sealed class VirtualFileSystemTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("openigi-vfs").FullName;

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void PassesThroughPathsThatMatchNoAlias()
    {
        VirtualFileSystem vfs = CreateFileSystem();
        Assert.Equal(["LOCAL:common/textures/a.tex"], vfs.ResolveCandidates("LOCAL:common/textures/a.tex"));
    }

    [Fact]
    public void SubstitutesAnAliasPrefix()
    {
        VirtualFileSystem vfs = CreateFileSystem();
        vfs.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/location0/level1/");

        Assert.Equal(
            ["LOCAL:missions/location0/level1/sounds/alarm.wav"],
            vfs.ResolveCandidates("MISSION:sounds/alarm.wav"));
    }

    [Fact]
    public void ExpandsASemicolonSearchListInOrder()
    {
        VirtualFileSystem vfs = CreateFileSystem();
        vfs.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:mods/mymod/level1/;LOCAL:missions/location0/level1/");

        Assert.Equal(
            ["LOCAL:mods/mymod/level1/objects.qvm", "LOCAL:missions/location0/level1/objects.qvm"],
            vfs.ResolveCandidates("MISSION:objects.qvm"));
    }

    [Fact]
    public void FallsThroughToTheNextSearchEntryWhenAFileIsMissing()
    {
        Write("missions/level1/objects.qvm", "base");
        Write("mods/mymod/level1/mission.qvm", "override");

        VirtualFileSystem vfs = CreateFileSystem();
        vfs.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:mods/mymod/level1/;LOCAL:missions/level1/");

        Assert.Equal("LOCAL:mods/mymod/level1/mission.qvm", Resolve(vfs, "MISSION:mission.qvm"));
        Assert.Equal("LOCAL:missions/level1/objects.qvm", Resolve(vfs, "MISSION:objects.qvm"));
        Assert.False(vfs.Exists("MISSION:absent.qvm"));
    }

    [Fact]
    public void NormalizesBackslashesInAliasesAndTargets()
    {
        Write("menusystem/mainmenu.qsc", "menu");

        VirtualFileSystem vfs = CreateFileSystem();

        // The original binds MENUSYS: to the backslash-terminated "LOCAL:menusystem\".
        vfs.SetAlias(VirtualFileSystem.MenuSystemAlias, @"LOCAL:menusystem\");

        Assert.Equal("LOCAL:menusystem/mainmenu.qsc", Resolve(vfs, @"MENUSYS:mainmenu.qsc"));
        Assert.Equal("menu", ReadText(vfs, @"MENUSYS:mainmenu.qsc"));
    }

    [Fact]
    public void MatchesAliasesCaseInsensitively()
    {
        Write("computer/font1.fnt", "font");

        VirtualFileSystem vfs = CreateFileSystem();
        vfs.SetAlias(VirtualFileSystem.ComputerAlias, "LOCAL:computer/");

        Assert.Equal("font", ReadText(vfs, "computer:font1.fnt"));
    }

    [Fact]
    public void RebindsAnAliasAtRuntime()
    {
        Write("language/english/messages.res", "english");
        Write("language/german/messages.res", "german");

        VirtualFileSystem vfs = CreateFileSystem();
        vfs.SetAlias(VirtualFileSystem.LanguageAlias, "LOCAL:language/english/");
        Assert.Equal("english", ReadText(vfs, "LANGUAGE:messages.res"));

        vfs.SetAlias(VirtualFileSystem.LanguageAlias, "LOCAL:language/german/");
        Assert.Equal("german", ReadText(vfs, "LANGUAGE:messages.res"));
        Assert.Single(vfs.AliasNames);
    }

    [Fact]
    public void RemovesAnAlias()
    {
        VirtualFileSystem vfs = CreateFileSystem();
        vfs.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/");

        Assert.True(vfs.RemoveAlias(VirtualFileSystem.MissionAlias));
        Assert.False(vfs.RemoveAlias(VirtualFileSystem.MissionAlias));
        Assert.Equal(["MISSION:sounds"], vfs.ResolveCandidates("MISSION:sounds"));
    }

    [Fact]
    public void RefusesToChainOneAliasOntoAnother()
    {
        // Aliases do not nest: the original validates that a target names a device before it binds
        // (0x4B12F0), and resolution substitutes exactly once before dispatching to that device.
        VirtualFileSystem vfs = CreateFileSystem();
        vfs.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/");

        Assert.Throws<EngineFatalException>(() => vfs.SetAlias("LEVEL:", "MISSION:level1/"));
    }

    [Fact]
    public void RejectsAnAliasNameWithoutATrailingColon()
    {
        VirtualFileSystem vfs = CreateFileSystem();
        Assert.Throws<ArgumentException>(() => vfs.SetAlias("MISSION", "LOCAL:missions/"));
    }

    [Fact]
    public void RejectsAnAliasTargetOnAnUnregisteredDevice()
    {
        VirtualFileSystem vfs = CreateFileSystem();

        EngineFatalException error = Assert.Throws<EngineFatalException>(
            () => vfs.SetAlias(VirtualFileSystem.MissionAlias, "CDROM:missions/"));
        Assert.Contains("Device not present", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAPathWithoutADeviceAsFatal()
    {
        VirtualFileSystem vfs = CreateFileSystem();

        EngineFatalException error = Assert.Throws<EngineFatalException>(() => vfs.Exists("common/textures/a.tex"));
        Assert.Contains("Illegal device name", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadAllBytesIsFatalForAMissingFile()
    {
        VirtualFileSystem vfs = CreateFileSystem();
        Assert.Throws<EngineFatalException>(() => vfs.ReadAllBytes("LOCAL:nothing.dat"));
    }

    [Fact]
    public void EnumeratesFilesAcrossEverySearchEntry()
    {
        Write("missions/level1/a.res", "a");
        Write("mods/mymod/level1/b.res", "b");

        VirtualFileSystem vfs = CreateFileSystem();
        vfs.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:mods/mymod/level1/;LOCAL:missions/level1/");

        IReadOnlyList<string> files = vfs.EnumerateFiles(VirtualFileSystem.MissionAlias, "*.res");

        Assert.Equal(["LOCAL:mods/mymod/level1/b.res", "LOCAL:missions/level1/a.res"], files);
    }

    [Fact]
    public void EnumeratesRetailFilesWithoutDependingOnHostCaseSensitivity()
    {
        Write("MISSIONS/LEVEL1/GRAPHS/GRAPH9.DAT", "graph");

        VirtualFileSystem vfs = CreateFileSystem();

        IReadOnlyList<string> files = vfs.EnumerateFiles("LOCAL:missions/level1/graphs", "graph*.dat");

        Assert.Single(files);
        Assert.Equal("GRAPH9.DAT", Path.GetFileName(files[0]), ignoreCase: true);
    }

    [Fact]
    public void ConfinesDevicePathsToTheDeviceRoot()
    {
        var device = new LocalFileDevice(_root);
        Assert.Equal(device.RootDirectory, device.ToFullPath("../../secrets.txt"));
        Assert.Equal(device.RootDirectory, device.ToFullPath("/"));
    }

    private VirtualFileSystem CreateFileSystem()
    {
        var vfs = new VirtualFileSystem();
        vfs.RegisterDevice(new LocalFileDevice(_root));
        return vfs;
    }

    private static string Resolve(VirtualFileSystem vfs, string name)
    {
        Assert.True(vfs.TryResolve(name, out string? resolved));
        return resolved;
    }

    private static string ReadText(VirtualFileSystem vfs, string name) =>
        System.Text.Encoding.UTF8.GetString(vfs.ReadAllBytes(name));

    private void Write(string relativePath, string contents)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }
}
