using OpenIGI.Game.Igi2.Multiplayer;
using OpenIGI.Game.Multiplayer;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Which of I.G.I. 2's files a session makes the host's: what plays, what only looks, and what belongs to which map.</summary>
public sealed class Igi2GameplayFilesTests
{
    // Campaign mission 11 (location1/level1) and the multiplayer map sandstorm, as their missions name their folders.
    private static readonly GameplayFileRules Rules = new(
        Igi2GameplayFiles.Layout,
        static map => map switch
        {
            "11" => ["missions/location1/level1", "missions/location1/common"],
            "41" => ["missions/multiplayer/sandstorm", "missions/multiplayer/common"],
            _ => null,
        });

    [Theory]
    [InlineData("missions/location1/level1/objects.qvm", true)]
    [InlineData("missions/location1/level1/ai/1350.qvm", true)]
    [InlineData("missions/location1/level1/graphs/graph4.dat", true)]
    [InlineData("missions/location1/level1/heightmaps/heightmaps.res", true)]
    [InlineData("missions/location1/level1/models/level1.res", true)]
    [InlineData("missions/location1/common/models/location1.res", true)]
    [InlineData("missions/location1/common/location1.mtp", true)]
    [InlineData("common/models/common.res", true)]
    [InlineData("common/ai/squaddefault.qvm", true)]
    [InlineData("common/common.dat", true)]
    [InlineData("weapons/weapons.qvm", true)]
    [InlineData("physicsobj/crate.qvm", true)]
    [InlineData("lod.qvm", true)]
    [InlineData("missions/location1/level1/textures/level1.res", false)]
    [InlineData("missions/location1/level1/lightmaps/lightmaps.res", false)]
    [InlineData("missions/location1/level1/envmaps/water.res", false)]
    [InlineData("missions/location1/level1/sounds/sounds.res", false)]
    [InlineData("missions/location1/common/sounds/sounds.qvm", false)]
    [InlineData("missions/location1/common/textures/sky_layer1.tex", false)]
    [InlineData("missions/multiplayer/sandstorm/textures/sand1.tga", false)]
    [InlineData("common/fonts/fonts_hi.res", false)]
    [InlineData("common/fonts/loadfontbig640.fnt", false)]
    [InlineData("common/sprites/sprites.res", false)]
    [InlineData("common/textures/common.res", false)]
    [InlineData("common/sounds/sounds.res", false)]
    [InlineData("language/english/language.res", false)]
    [InlineData("computer/computer.res", false)]
    [InlineData("menusystem/menusystem.res", false)]
    [InlineData("config.qvm", false)]
    [InlineData("igi2.exe", false)]
    [InlineData("weapons/evil.dll", false)]
    public void Only_the_files_a_level_plays_from_are_compared(string path, bool gameplay) =>
        Assert.Equal(gameplay, Rules.IsGameplay(path));

    [Theory]
    [InlineData("11", "missions/location1/level1/objects.qvm", true)]
    [InlineData("11", "missions/location1/common/models/location1.res", true)]
    [InlineData("11", "common/models/common.res", true)]
    [InlineData("11", "missions/location1/level2/objects.qvm", false)]
    [InlineData("11", "missions/location2/common/models/location2.res", false)]
    [InlineData("11", "missions/multiplayer/sandstorm/objects.qvm", false)]
    [InlineData("41", "missions/multiplayer/sandstorm/heightmaps/heightmaps.res", true)]
    [InlineData("41", "missions/multiplayer/common/models/multiplayer.res", true)]
    [InlineData("41", "missions/location1/level1/objects.qvm", false)]
    [InlineData("99", "common/models/common.res", false)]
    public void A_list_of_a_map_names_only_that_maps_folders_and_the_shared_ones(string map, string path, bool listable) =>
        Assert.Equal(listable, Rules.IsListable(map, path));

    [Theory]
    [InlineData("missions/location1/level1/objects.qvm", true)]
    [InlineData("missions/location1/common/location1.mtp", true)]
    [InlineData("common/models/common.res", false)]
    [InlineData("weapons/weapons.qvm", false)]
    public void A_maps_own_files_are_told_from_those_read_once_a_game(string path, bool level) =>
        Assert.Equal(level, Rules.IsLevelFile(path));

    [Fact]
    public void A_host_lists_its_maps_folders_and_the_shared_tables()
    {
        Memory host = new()
        {
            ["missions/location1/level1/objects.qvm"] = [1, 2, 3, 4],
            ["missions/location1/level1/textures/level1.res"] = [1],
            ["missions/location1/level2/objects.qvm"] = [5],
            ["missions/location1/common/models/location1.res"] = [6, 7],
            ["common/models/common.res"] = [8],
            ["common/fonts/fonts_hi.res"] = [9],
            ["lod.qvm"] = [10],
        };

        SessionFileList list = Rules.Build("11", host);

        Assert.Equal(
            ["common/models/common.res", "lod.qvm", "missions/location1/common/models/location1.res", "missions/location1/level1/objects.qvm"],
            list.Files.Select(f => f.Path).Order(StringComparer.Ordinal));
    }

    private sealed class Memory : Dictionary<string, byte[]>, ISessionFileSource
    {
        public GameplayFileRules Rules => Igi2GameplayFilesTests.Rules;

        public IEnumerable<string> ListFiles(string directory) => Keys.Where(k => k.StartsWith(directory + "/", StringComparison.Ordinal));

        public byte[]? Read(string path) => TryGetValue(path, out byte[]? data) ? data : null;
    }
}
