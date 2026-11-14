using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>A mission made from a retail one: its folder, its definition, and the campaign's last mission pointed at it.</summary>
public sealed class NewMissionTests
{
    /// <summary>The new mission's script defines it as the base's did but for its id, name, next mission and folders; the last mission now leads to it.</summary>
    [RequiresGameDataFact]
    public void A_mission_made_from_level_one_follows_mission_fourteen()
    {
        byte[] baseScript = File.ReadAllBytes(GameData.Resolve("missions/location0/level1/mission.qvm"));
        byte[] lastScript = File.ReadAllBytes(GameData.Resolve("missions/location0/level14/mission.qvm"));
        (string, ReadOnlyMemory<byte>)[] level =
        [
            ("mission.qvm", baseScript),
            ("objects.qvm", new byte[] { 1, 2, 3 }),
            ("terrain/terrain.hmp", new byte[] { 4 }),
        ];

        IReadOnlyList<(string GamePath, byte[] Bytes)> files = NewMission.Build(
            "missions/location0/level1", baseScript, level, 15, "Night Yard", ("missions/location0/level14", lastScript));

        Assert.Equal(
            ["missions/location0/level15/objects.qvm", "missions/location0/level15/terrain/terrain.hmp", "missions/location0/level15/mission.qvm", "missions/location0/level14/mission.qvm"],
            files.Select(static f => f.GamePath));
        Assert.Equal(
            "DefineMission(15, \"Night Yard\", \"Mission 1 Desc\", \"\", MISSION_NEXT_MISSION_UNDEFINED, \"missions/location0/level15\", \"missions/location0/common\", \"missions/location0/level15\", \"location0\", \"level1\", \"mission1.spr\");",
            QscPrinter.Print(QscDecompiler.Decompile(files[2].Bytes)).Trim());
        Assert.Equal(
            "DefineMission(14, \"Mission 14 Name\", \"Mission 14 Desc\", \"\", 15, \"missions/location0/level14\", \"missions/location0/common\", \"missions/location0/level14\", \"location0\", \"level14\", \"mission14.spr\");",
            QscPrinter.Print(QscDecompiler.Decompile(files[3].Bytes)).Trim());
    }

    /// <summary>
    /// Given the language's mission texts, the new mission is named by a key of its own, as the original's menus look names
    /// up, and the archive gains the name as typed and the base's description under the new keys, the rest as it was.
    /// </summary>
    [RequiresGameDataFact]
    public void A_new_missions_name_goes_into_the_language_archive_under_its_own_key()
    {
        byte[] baseScript = File.ReadAllBytes(GameData.Resolve("missions/location0/level1/mission.qvm"));
        byte[] texts = File.ReadAllBytes(GameData.Resolve("language/english/missions.res"));

        IReadOnlyList<(string GamePath, byte[] Bytes)> files = NewMission.Build(
            "missions/location0/level1", baseScript, [], 15, "Night Yard", null, [("language/english/missions.res", texts)]);

        Assert.Equal(["language/english/missions.res", "missions/location0/level15/mission.qvm"], files.Select(static f => f.GamePath));
        Assert.StartsWith(
            "DefineMission(15, \"Mission 15 Name\", \"Mission 15 Desc\", ",
            QscPrinter.Print(QscDecompiler.Decompile(files[1].Bytes)).Trim(),
            StringComparison.Ordinal);

        OpenIGI.Formats.Ilff.IresArchive before = OpenIGI.Formats.Ilff.IresArchive.Read(texts);
        OpenIGI.Formats.Ilff.IresArchive after = OpenIGI.Formats.Ilff.IresArchive.Read(files[0].Bytes);
        Assert.Equal(before.Entries.Count + 2, after.Entries.Count);
        Assert.Equal("Night Yard", after.Entries.Single(static e => e.Name.EndsWith("Mission 15 Name", StringComparison.Ordinal)).AsText());
        Assert.Equal(
            before.Entries.Single(static e => e.Name.EndsWith("Mission 1 Desc", StringComparison.Ordinal)).AsText(),
            after.Entries.Single(static e => e.Name.EndsWith("Mission 15 Desc", StringComparison.Ordinal)).AsText());
        Assert.True(before.Entries.Zip(after.Entries).All(static p => p.First.Name == p.Second.Name && p.First.Data.Span.SequenceEqual(p.Second.Data.Span)));
    }

    /// <summary>The key of a new mission is the base's with the new number where the base's stood as a word.</summary>
    [Fact]
    public void A_key_takes_the_new_missions_number()
    {
        Assert.Equal("Mission 15 Name", NewMission.KeyFor("Mission 1 Name", 1, 15));
        Assert.Equal("Mission 15 Desc", NewMission.KeyFor("Mission 14 Desc", 14, 15));
        Assert.Null(NewMission.KeyFor("Mission 14 Name", 1, 15));
        Assert.Null(NewMission.KeyFor("Trainyard", 1, 15));
    }

    /// <summary>The object lightmaps, named in their archive by the base's folder, are renamed into the new one, data untouched.</summary>
    [RequiresGameDataFact]
    public void Archives_named_by_the_base_folder_are_renamed_into_the_new_one()
    {
        byte[] baseScript = File.ReadAllBytes(GameData.Resolve("missions/location0/level1/mission.qvm"));
        byte[] lightmaps = File.ReadAllBytes(GameData.Resolve("missions/location0/level1/lightmaps/lightmaps.res"));
        IReadOnlyList<(string GamePath, byte[] Bytes)> files = NewMission.Build(
            "missions/location0/level1", baseScript, [("lightmaps/lightmaps.res", lightmaps)], 15, "Night Yard", null);

        OpenIGI.Formats.Ilff.IresArchive before = OpenIGI.Formats.Ilff.IresArchive.Read(lightmaps);
        OpenIGI.Formats.Ilff.IresArchive after = OpenIGI.Formats.Ilff.IresArchive.Read(files[0].Bytes);
        Assert.Equal(before.Entries.Count, after.Entries.Count);
        Assert.All(after.Entries, static e => Assert.StartsWith("LOCAL:missions/location0/level15/lightmaps/", e.Name, StringComparison.Ordinal));
        Assert.True(before.Entries.Zip(after.Entries).All(static p => p.First.Data.Span.SequenceEqual(p.Second.Data.Span)));
    }

    /// <summary>A name the original's record cannot hold is refused.</summary>
    [RequiresGameDataFact]
    public void A_name_longer_than_the_original_keeps_is_refused()
    {
        byte[] baseScript = File.ReadAllBytes(GameData.Resolve("missions/location0/level1/mission.qvm"));
        Assert.Throws<ArgumentException>(() => NewMission.Build("missions/location0/level1", baseScript, [], 15, new string('x', 32), null));
    }
}
