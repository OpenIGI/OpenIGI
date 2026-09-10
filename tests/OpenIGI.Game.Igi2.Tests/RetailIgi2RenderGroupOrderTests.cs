using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The order the world's objects are drawn in, over every shipped level.</summary>
/// <remarks>
/// <para>
/// The engine draws nothing directly: <c>World_Draw</c> enqueues each visible object into one of sixteen
/// call queues by its render group, and <c>QCube_CallHandlers</c> empties them nought to fifteen. So two
/// objects in different groups are separated by everything the groups between them draw, and a renderer
/// that batched across that boundary would put a pane of glass before the wall behind it because the two
/// share a mesh name.
/// </para>
/// <para>
/// What these check is that the group survives the walk and then decides the draw order — and that
/// Project I.G.I., which authors no groups at all, comes out in exactly the order it always did.
/// </para>
/// </remarks>
public sealed class RetailIgi2RenderGroupOrderTests
{
    /// <summary>Every level's placements carry a group, and the draw order is grouped by it.</summary>
    /// <remarks>
    /// Non-decreasing is the assertion, not "sorted": within a group the order is still by model, which
    /// is what keeps consecutive draws sharing GPU state. A sort that dropped the group key would fail
    /// this on any level that mixes two.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_draw_order_runs_through_the_groups_in_turn()
    {
        int levels = 0;
        int mixed = 0;
        HashSet<int> seen = [];

        foreach (Igi2Level level in Igi2Level.All())
        {
            WorldScene scene = SceneFor(level);
            int[] order = WorldRenderer.BuildDrawOrder(scene);

            levels++;
            Assert.Equal(scene.Placements.Count, order.Length);

            int previous = int.MinValue;
            HashSet<int> here = [];
            for (int i = 0; i < order.Length; i++)
            {
                int group = scene.Placements[order[i]].RenderGroup;

                Assert.True(
                    Igi2RenderGroups.IsValid(group),
                    $"{level} placed an object in queue {group}, which is not one of the sixteen.");
                Assert.True(
                    group >= previous,
                    $"{level} draws queue {group} after queue {previous}.");

                previous = group;
                here.Add(group);
                seen.Add(group);
            }

            if (here.Count > 1)
            {
                mixed++;
            }
        }

        Assert.Equal(24, levels);

        // Levels that use more than one queue are what make the ordering observable at all. If every
        // level used one, this suite would pass against a renderer that ignored the group entirely.
        Assert.True(mixed > 0, "no shipped level mixes two render groups, so nothing here is tested.");
        Assert.True(seen.Count > 1, $"the whole corpus only ever used queue {seen.First()}.");
    }

    /// <summary>Within one group the order is still by model, so the batching survives.</summary>
    /// <remarks>
    /// The other half. Making the group the outer key would be worthless if it cost the state batching
    /// it was layered over — a level's draws would then be one per object rather than one per run.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Within_a_group_the_order_is_still_by_model()
    {
        foreach (Igi2Level level in Igi2Level.All())
        {
            WorldScene scene = SceneFor(level);
            int[] order = WorldRenderer.BuildDrawOrder(scene);

            int checkedRuns = 0;
            for (int i = 1; i < order.Length; i++)
            {
                WorldPlacement previous = scene.Placements[order[i - 1]];
                WorldPlacement current = scene.Placements[order[i]];
                if (previous.RenderGroup != current.RenderGroup)
                {
                    continue;
                }

                checkedRuns++;
                Assert.True(
                    string.CompareOrdinal(previous.Instance.Name, current.Instance.Name) <= 0,
                    $"{level} broke model order inside queue {current.RenderGroup}.");
            }

            if (checkedRuns > 0)
            {
                return;
            }
        }

        Assert.Fail("no level had two placements in the same group.");
    }

    /// <summary>The groups the corpus actually uses are the ones the engine hands out.</summary>
    /// <remarks>
    /// <para>
    /// Recorded rather than asserted exactly, because which queues a level reaches for is a fact about
    /// the levels and not about the port. What <em>is</em> asserted is that they are queues the engine's
    /// own create handlers name — a level authoring queue 11 would mean the parameter is being read from
    /// the wrong offset, and nothing else here would notice.
    /// </para>
    /// <para>
    /// The default is five (<c>World_InitDynCubeObj</c>) and the sector interior twelve
    /// (<c>SectorObject_RegisterInteriorDynCubeObj…</c>), which between them is most of a level.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_groups_used_are_ones_the_engine_hands_out()
    {
        Dictionary<int, int> histogram = [];

        foreach (Igi2Level level in Igi2Level.All())
        {
            foreach (WorldPlacement placement in SceneFor(level).Placements)
            {
                histogram[placement.RenderGroup] = histogram.GetValueOrDefault(placement.RenderGroup) + 1;
            }
        }

        Assert.NotEmpty(histogram);
        Assert.All(histogram.Keys, group => Assert.True(Igi2RenderGroups.IsValid(group)));

        // The two the engine itself would choose carry the bulk of every level.
        int engineChosen = histogram.GetValueOrDefault(Igi2RenderGroups.Default)
            + histogram.GetValueOrDefault(Igi2RenderGroups.SectorInterior);
        int total = histogram.Values.Sum();

        Assert.True(
            engineChosen > total / 2,
            $"only {engineChosen} of {total} placements sit in the default or interior queue.");
    }

    /// <summary>A placement that says nothing takes the queue the engine writes into every object.</summary>
    /// <remarks>
    /// Which is what keeps Project I.G.I. bit-identical: it authors no groups, so every one of its
    /// placements carries this, the first comparison in the draw order is always nought, and the order
    /// is the one the renderer produced before the key existed.
    /// </remarks>
    [Fact]
    public void A_placement_that_says_nothing_takes_the_engines_default()
    {
        Assert.Equal(5, WorldPlacement.DefaultRenderGroup);
        Assert.Equal(WorldPlacement.DefaultRenderGroup, Igi2RenderGroups.Default);

        // And the table is the one read out of the create handlers.
        Assert.Equal(0, Igi2RenderGroups.Sky);
        Assert.Equal(2, Igi2RenderGroups.Terrain);
        Assert.Equal(14, Igi2RenderGroups.Water);
        Assert.Equal(16, Igi2RenderGroups.Count);
        Assert.False(Igi2RenderGroups.IsValid(-1));
        Assert.False(Igi2RenderGroups.IsValid(16));
    }

    private static WorldScene SceneFor(Igi2Level level)
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

        return Igi2WorldBuilder.Instance
            .Build(new WorldBuildRequest(mission, level.Root, resources, null, NullLogger.Instance))
            .Scene;
    }
}
