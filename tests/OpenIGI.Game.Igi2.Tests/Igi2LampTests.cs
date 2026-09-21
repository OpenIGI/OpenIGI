using OpenIGI.Engine.Resources;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The lamps an I.G.I. 2 level lights itself with.</summary>
/// <remarks>
/// <para>
/// Neither game declares a light. Every lamp is geometry with a bright texture and the light it appears
/// to cast was painted into a lightmap, so the port infers the rest — and that inference reads a scene,
/// which makes it title-agnostic and means it worked for the sequel from the day the sequel had a scene.
/// </para>
/// <para>
/// What did not work was everything around it. Both games number their missions independently over
/// ranges that OVERLAP, and both number their models out of one range and collide there too, so the
/// sequel was reading Project I.G.I.'s answers for both. These pin the two collisions, because neither
/// announces itself: the wrong list loads cleanly, matches nothing, and leaves the level dark.
/// </para>
/// </remarks>
public sealed class Igi2LampTests
{
    /// <summary>The sequel's levels have lamps, and the detector finds them.</summary>
    /// <remarks>
    /// The claim that makes the rest worth fixing. Fifty-two groups of emitter texture over twelve of the
    /// twenty-four levels, found from the geometry's own shape and the brightness of the pages it is
    /// skinned with, with nothing named by hand. The twelve that find none are the levels played outdoors.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_sequels_levels_have_lamps_and_the_detector_finds_them()
    {
        int groups = 0;
        int levelsWithLamps = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            (Igi2DrawableSceneResult built, ResourceRegistry resources) = Igi2LevelScene.Build(level);
            LampDetector.Lamps lamps = LampDetector.Detect(built.Scene, resources, [], null);
            groups += lamps.Count;
            levelsWithLamps += lamps.Count > 0 ? 1 : 0;
        }

        Assert.Equal(52, groups);
        Assert.Equal(12, levelsWithLamps);
    }

    /// <summary>A hand-named lamp of one game is not a lamp of the other.</summary>
    /// <remarks>
    /// <para>
    /// <c>207_01_1</c> and <c>208_01_1</c> are named in the manifest as Project I.G.I.'s ceiling
    /// fittings. I.G.I. 2 places models of exactly those names, and they are not its lamps. Handing the
    /// whole list to either game tells it two of its objects glow and binds them to textures they do not
    /// carry, which is why an entry says which game it is about and why an unmarked one means the first.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_hand_named_lamp_of_one_game_is_not_a_lamp_of_the_other()
    {
        HashSet<string> collisions = new(StringComparer.OrdinalIgnoreCase);
        foreach (Igi2Level level in Igi2Level.All())
        {
            foreach (Igi2Placement placement in level.Scene.Placements)
            {
                if (placement.Model is "207_01_1" or "208_01_1")
                {
                    collisions.Add(placement.Model);
                }
            }
        }

        Assert.Equal(2, collisions.Count);

        // And the filter is what keeps them apart: an unmarked entry answers for the first game only.
        LampManifest.Entry named = new("207_01_1", ["316_04_1"], 3f, IsLamp: true, "a first-game fitting");
        Assert.Equal(LampManifest.DefaultTitle, named.Title);
        Assert.NotEqual("igi2", named.Title);
    }

    /// <summary>The two games' mission numbers overlap, so their fixture files must not share a name.</summary>
    /// <remarks>
    /// <para>
    /// Project I.G.I. runs missions 1 to 17. I.G.I. 2 numbers its own <c>location × 10 + level</c>,
    /// which starts at 11 — so the sequel's first seven sat exactly on top of the first game's last
    /// seven. A saved fixture list WINS over detection, so the sequel loaded a list of the other game's
    /// textures, matched none of them, and never surveyed its own level at all. That is the whole reason
    /// its lamps were dark.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_two_games_mission_numbers_overlap()
    {
        int overlapping = 0;
        foreach (Igi2Level level in Igi2Level.All())
        {
            int id = MissionId(level.Directory);
            overlapping += id is >= 1 and <= 17 ? 1 : 0;
        }

        Assert.Equal(7, overlapping);
    }

    /// <summary>The sequel's mission number, from its directory, the way the catalogue forms it.</summary>
    private static int MissionId(string directory)
    {
        string path = directory.Replace('\\', '/').ToLowerInvariant();
        int at = path.LastIndexOf("location", StringComparison.Ordinal);
        if (at < 0)
        {
            return 0;
        }

        string tail = path[(at + "location".Length)..];
        string[] parts = tail.Split('/');
        return parts.Length >= 2
            && int.TryParse(parts[0], out int location)
            && int.TryParse(parts[1].Replace("level", string.Empty), out int level)
                ? (location * 10) + level
                : 0;
    }
}
