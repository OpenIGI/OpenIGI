using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The scene walk, reached the way the host reaches it.</summary>
/// <remarks>
/// <para>
/// The sibling suite checks what the walk finds. This one checks that it is reachable at all through
/// <see cref="ITitleWorldBuilder"/> — the seam the host now goes through — and that the mission carries
/// the four packs in the order the game opens them, because that order decides which pack a duplicated
/// model name resolves to.
/// </para>
/// <para>
/// Read from <c>Game_CreateHandler</c> (<c>igi2ed 0x4469A0</c>), which calls <c>MTP_Load</c> five times:
/// <c>common</c>, <c>new</c>, the location's, the level's, and <c>editor</c> — the last from
/// <c>LOCAL:editor/</c> under a "Loading editor..." caption, so it is the editor's own and no part of
/// the game.
/// </para>
/// </remarks>
public sealed class RetailIgi2WorldBuilderTests
{
    /// <summary>Every shipped level walks into a scene through the seam.</summary>
    /// <remarks>
    /// The count is the assertion. The host used to call the first game's walk directly, and the first
    /// game's walk over an I.G.I. 2 level places almost nothing — quietly, with the level loading and
    /// every other pass rendering. A builder that returned an empty scene for all 24 would look
    /// identical to that, so an empty one is a failure here.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_shipped_level_walks_into_a_scene_through_the_seam()
    {
        Igi2WorldBuilder builder = Igi2WorldBuilder.Instance;
        int levels = 0;
        int placements = 0;
        int lightmapped = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            (LoadedMission mission, ResourceRegistry resources) = MissionFor(level);
            WorldBuildResult result = builder.Build(
                new WorldBuildRequest(mission, level.Root, resources, null, NullLogger.Instance));

            levels++;
            placements += result.Scene.Placements.Count;
            lightmapped += result.LightmappedCount;

            Assert.NotEmpty(result.Scene.Placements);
            Assert.True(
                result.UnresolvedCount < result.Scene.Placements.Count,
                $"more of {level} failed to resolve than succeeded.");
        }

        Assert.Equal(24, levels);
        Assert.True(placements > 0, "the whole corpus walked into nothing.");
        Assert.True(lightmapped > 0, "not one placement bound a baked lightmap.");
    }

    /// <summary>A mission opens four packs, and the common one shadows the level's.</summary>
    /// <remarks>
    /// <c>MTP_VirModel_GetPtr</c> (<c>igi2ed 0x41EED0</c>) returns the first match walking from the head
    /// of the list, so the pack opened <em>first</em> wins a duplicated name. That is the opposite of
    /// Project I.G.I., where the level pack is opened second and shadows the common one — which is why
    /// this cannot be a shared walk with a parameter.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_mission_opens_four_packs_and_the_common_one_wins()
    {
        int checkedLevels = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            (LoadedMission mission, _) = MissionFor(level);
            IReadOnlyList<LoadedPack> packs = mission.PacksInLoadOrder;

            // common, new, the location's, the level's. A level whose location ships no pack has three.
            Assert.InRange(packs.Count, 3, 4);
            Assert.Equal("common", packs[0].Name, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("new", packs[1].Name, StringComparer.OrdinalIgnoreCase);

            checkedLevels++;
        }

        Assert.Equal(24, checkedLevels);
    }

    /// <summary>A mission that names no pack list still has the two the first game opens.</summary>
    /// <remarks>
    /// The fallback that keeps Project I.G.I. unchanged: it opens a common pack and a level pack, in
    /// that order, and a mission built without a list reports exactly those.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_mission_with_no_list_falls_back_to_the_two_the_first_game_opens()
    {
        (LoadedMission mission, _) = MissionFor(Igi2Level.All().First());
        LoadedMission twoPack = mission with { Packs = null };

        Assert.Equal(2, twoPack.PacksInLoadOrder.Count);
        Assert.Same(twoPack.CommonPack, twoPack.PacksInLoadOrder[0]);
        Assert.Same(twoPack.LevelPack, twoPack.PacksInLoadOrder[1]);
    }

    private static (LoadedMission Mission, ResourceRegistry Resources) MissionFor(Igi2Level level)
    {
        string root = GameDataSource.Igi2.RequireRoot();
        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(root));
        files.SetAlias(
            VirtualFileSystem.MissionAlias,
            "LOCAL:" + Path.GetRelativePath(root, level.Directory).Replace(Path.DirectorySeparatorChar, '/') + "/");

        ResourceRegistry resources = new(files);
        List<LoadedPack> packs = [];
        foreach ((string directory, string name) in level.PackSources())
        {
            packs.Add(LoadedPack.Open(
                "LOCAL:" + Path.GetRelativePath(root, directory).Replace(Path.DirectorySeparatorChar, '/') + "/",
                name,
                resources,
                logger: null,
                AssetFormatProfile.Igi2));
        }

        string levelDirectory =
            "LOCAL:" + Path.GetRelativePath(root, level.Directory).Replace(Path.DirectorySeparatorChar, '/') + "/";

        LoadedMission mission = new(
            new MissionDescriptor(
                Id: 1,
                Name: "igi2",
                Description: string.Empty,
                Tag: string.Empty,
                NextMissionId: MissionDescriptor.NextMissionUndefined,
                MissionDirectory: levelDirectory,
                CommonPackDirectory: packs[0].Directory,
                LevelPackDirectory: levelDirectory,
                CommonPackName: packs[0].Name,
                LevelPackName: packs[^1].Name,
                BriefingSprite: string.Empty),
            packs[0],
            packs[^1],
            [],
            Terrain: null,
            Packs: packs);

        return (mission, resources);
    }
}
