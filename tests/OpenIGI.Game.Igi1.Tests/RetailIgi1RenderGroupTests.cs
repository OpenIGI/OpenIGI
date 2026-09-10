using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>Project I.G.I. authors no render groups, and the draw order is what it always was.</summary>
/// <remarks>
/// <para>
/// The world renderer's draw order gained a key: the render group, ahead of the model name it had
/// always batched by. That was for the sequel, whose frame really is sixteen queues emptied in turn —
/// but it runs through the renderer both games share, so it needs saying that this one does not move.
/// </para>
/// <para>
/// It does not, and the reason is in the data rather than in a special case: no shipped level of this
/// game sets <c>RenderGroup</c> at all, so every placement carries the engine's default, the group
/// comparison is always nought, and the sort falls through to exactly the model-then-index order it used
/// before the key existed.
/// </para>
/// </remarks>
public sealed class RetailIgi1RenderGroupTests
{
    /// <summary>Every placement of every shipped mission sits in the default queue.</summary>
    [RequiresGameDataFact]
    public void Every_placement_sits_in_the_default_queue()
    {
        int missions = 0;
        int placements = 0;

        foreach (int mission in new[] { 1, 2, 3 })
        {
            WorldScene scene = SceneFor(mission);
            missions++;
            placements += scene.Placements.Count;

            Assert.NotEmpty(scene.Placements);
            Assert.All(
                scene.Placements,
                placement => Assert.Equal(WorldPlacement.DefaultRenderGroup, placement.RenderGroup));
        }

        Assert.Equal(3, missions);
        Assert.True(placements > 0);
    }

    /// <summary>And the draw order is the one the model key alone produces.</summary>
    /// <remarks>
    /// Computed both ways and compared index for index. This is the assertion that says the new key
    /// costs this game nothing — a weaker one, that the order is merely still sorted by model, would
    /// pass even if the group had quietly reshuffled placements that share a name.
    /// </remarks>
    [RequiresGameDataFact]
    public void The_draw_order_is_the_one_the_model_key_alone_produces()
    {
        WorldScene scene = SceneFor(1);
        int[] actual = WorldRenderer.BuildDrawOrder(scene);

        int[] expected = new int[scene.Placements.Count];
        for (int i = 0; i < expected.Length; i++)
        {
            expected[i] = i;
        }

        Array.Sort(expected, (left, right) =>
        {
            int byModel = string.CompareOrdinal(
                scene.Placements[left].Instance.Name,
                scene.Placements[right].Instance.Name);
            return byModel != 0 ? byModel : left.CompareTo(right);
        });

        Assert.Equal(expected, actual);
    }

    private static WorldScene SceneFor(int mission)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());

        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);

        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        PlayerTaskTypes.RegisterAll(loop.Types);

        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        LoadedMission loaded = new MissionLoader(registry, fileSystem, resources, levelLoader, null, null)
            .Load(mission);

        return WorldScene.Build(loaded, levelRoot, magic);
    }
}
