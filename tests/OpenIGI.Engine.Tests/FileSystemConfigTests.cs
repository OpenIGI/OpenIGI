using OpenIGI.Engine.Configuration;
using OpenIGI.Engine.Vfs;
using Xunit;

namespace OpenIGI.Engine.Tests;

/// <summary>Covers the optional <c>filesys.cfg</c> and the built-in alias set.</summary>
public sealed class FileSystemConfigTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("openigi-cfg").FullName;

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void DefaultsMatchTheAliasesTheOriginalBindsAtStartUp()
    {
        FileSystemConfig config = FileSystemConfig.CreateDefault();

        Assert.Equal(
            [
                VirtualFileSystem.MenuSystemAlias,
                VirtualFileSystem.ComputerAlias,
                VirtualFileSystem.IntroScreenAlias,
                VirtualFileSystem.StatusScreenAlias,
            ],
            config.Aliases.Select(alias => alias.Key).ToArray());

        Assert.Equal("LOCAL:computer/", config.Aliases.Single(a => a.Key == VirtualFileSystem.ComputerAlias).Value);
    }

    [Fact]
    public void ParsesBindingsAndIgnoresCommentsAndBlankLines()
    {
        FileSystemConfig config = FileSystemConfig.Parse(
            """
            # OpenIGI file system configuration

            MENUSYS:  = LOCAL:menusystem/
            ; a leading semicolon is a comment too
            MISSION:  = LOCAL:mods/mymod/level1/;LOCAL:missions/location0/level1/   # trailing comment

            """);

        Assert.Equal(2, config.Aliases.Count);
        Assert.Equal("MENUSYS:", config.Aliases[0].Key);
        Assert.Equal("LOCAL:menusystem/", config.Aliases[0].Value);
        Assert.Equal("LOCAL:mods/mymod/level1/;LOCAL:missions/location0/level1/", config.Aliases[1].Value);
    }

    [Fact]
    public void AppliesBindingsToAFileSystem()
    {
        var vfs = new VirtualFileSystem();
        vfs.RegisterDevice(new LocalFileDevice(_root));

        FileSystemConfig.CreateDefault().ApplyTo(vfs);

        Assert.Equal(["LOCAL:computer/font1.fnt"], vfs.ResolveCandidates("COMPUTER:font1.fnt"));
        Assert.Equal(["LOCAL:screens/game/status/a.spr"], vfs.ResolveCandidates("STATUSSCREEN:a.spr"));
    }

    [Fact]
    public void SearchListsSurviveTheRoundTripToTheFileSystem()
    {
        var vfs = new VirtualFileSystem();
        vfs.RegisterDevice(new LocalFileDevice(_root));

        FileSystemConfig.Parse("MISSION: = LOCAL:mods/a/;LOCAL:missions/b/").ApplyTo(vfs);

        Assert.Equal(
            ["LOCAL:mods/a/objects.qvm", "LOCAL:missions/b/objects.qvm"],
            vfs.ResolveCandidates("MISSION:objects.qvm"));
    }

    [Fact]
    public void FallsBackToTheDefaultsWhenNoFileExists()
    {
        string path = Path.Combine(_root, FileSystemConfig.DefaultFileName);

        FileSystemConfig config = FileSystemConfig.LoadOrDefault(path);

        Assert.Equal(FileSystemConfig.CreateDefault().Aliases, config.Aliases);
    }

    [Fact]
    public void ReadsAFileWhenOneExists()
    {
        string path = Path.Combine(_root, FileSystemConfig.DefaultFileName);
        File.WriteAllText(path, "COMPUTER: = LOCAL:mods/hd-computer/\n");

        FileSystemConfig config = FileSystemConfig.LoadOrDefault(path);

        KeyValuePair<string, string> alias = Assert.Single(config.Aliases);
        Assert.Equal("LOCAL:mods/hd-computer/", alias.Value);
    }

    [Theory]
    [InlineData("MENUSYS LOCAL:menusystem/")]
    [InlineData("MENUSYS = LOCAL:menusystem/")]
    [InlineData("MENUSYS: =")]
    public void RejectsMalformedLines(string text) =>
        Assert.Throws<FileSystemConfigException>(() => FileSystemConfig.Parse(text));
}
