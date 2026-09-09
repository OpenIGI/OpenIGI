using OpenIGI.Engine.Configuration;
using OpenIGI.Engine.Titles;
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
        FileSystemConfig config = FileSystemConfig.CreateDefault(new StubLayout());

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

        FileSystemConfig.CreateDefault(new StubLayout()).ApplyTo(vfs);

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

        FileSystemConfig config = FileSystemConfig.LoadOrDefault(path, new StubLayout());

        Assert.Equal(FileSystemConfig.CreateDefault(new StubLayout()).Aliases, config.Aliases);
    }

    [Fact]
    public void ReadsAFileWhenOneExists()
    {
        string path = Path.Combine(_root, FileSystemConfig.DefaultFileName);
        File.WriteAllText(path, "COMPUTER: = LOCAL:mods/hd-computer/\n");

        FileSystemConfig config = FileSystemConfig.LoadOrDefault(path, new StubLayout());

        KeyValuePair<string, string> alias = Assert.Single(config.Aliases);
        Assert.Equal("LOCAL:mods/hd-computer/", alias.Value);
    }

    [Theory]
    [InlineData("MENUSYS LOCAL:menusystem/")]
    [InlineData("MENUSYS = LOCAL:menusystem/")]
    [InlineData("MENUSYS: =")]
    public void RejectsMalformedLines(string text) =>
        Assert.Throws<FileSystemConfigException>(() => FileSystemConfig.Parse(text));

    /// <summary>
    /// A title whose aliases happen to be I.G.I. 1's, written here rather than referenced.
    /// </summary>
    /// <remarks>
    /// These tests are about the configuration mechanism, not about either game, and
    /// <c>OpenIGI.Engine</c> must not know a title in order to be tested. The four bindings are the ones
    /// the original binds at start-up; the point of the test is that they arrive intact, whoever supplies
    /// them.
    /// </remarks>
    private sealed class StubLayout : ITitleDataLayout
    {
        public IReadOnlyList<KeyValuePair<string, string>> StartupAliases { get; } =
        [
            new(VirtualFileSystem.MenuSystemAlias, "LOCAL:menusystem/"),
            new(VirtualFileSystem.ComputerAlias, "LOCAL:computer/"),
            new(VirtualFileSystem.IntroScreenAlias, "LOCAL:screens/intro/"),
            new(VirtualFileSystem.StatusScreenAlias, "LOCAL:screens/game/status/"),
        ];

        public string MissionRoot => "LOCAL:missions";

        public string MissionScriptName => "mission.qvm";

        public string LevelScriptName => "objects.qvm";

        public int FirstMissionId => 1;

        public string CommonSearchDirectory => "LOCAL:common/";

        public IReadOnlyList<string> SharedArchives { get; } = [];

        public IReadOnlyList<string> SoundDirectories { get; } = [];

        public IReadOnlyList<string> StructuralTaskTypes { get; } = [];
    }
}
