using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Runs the retail <c>animtrigger/animtrigger.qvm</c> and checks the table it publishes.
/// </summary>
/// <remarks>
/// This is what turns an animation's <c>BOED</c> event id into a sound. See
/// the original.
/// </remarks>
public sealed class RetailAnimTriggerTests
{
    [RequiresGameDataFact]
    public void TheTableHasFiftyEntriesAndAllOfThemAreSounds()
    {
        AnimTriggerRegistry registry = Load();

        Assert.Equal(50, registry.Count);
        Assert.All(registry.Definitions.Values, d => Assert.Equal("TASKTYPE_ANIMSOUND", d.TaskType));
        Assert.All(registry.Definitions.Values, d => Assert.NotEmpty(d.Resource));
    }

    [RequiresGameDataFact]
    public void TheIdsRunFromAThousandWithGaps()
    {
        AnimTriggerRegistry registry = Load();
        List<int> ids = [.. registry.Definitions.Keys.Order()];

        Assert.Equal(1000, ids[0]);
        Assert.Equal(1054, ids[^1]);

        // 55 ids would fill the range; only 50 are defined, so the table is sparse.
        Assert.Equal(50, ids.Count);
    }

    [RequiresGameDataFact]
    public void TheCommonestAnimationEventHasNoTrigger()
    {
        // Event 1010 accounts for 117 of the 245 events in the retail animation data and fires nothing
        // here. A table entry names one fixed sound, which cannot express a footstep whose sound
        // depends on the ground — while walk_fence_*, a specific surface, is present. So 1010 is
        // handled inside the engine, and its absence is evidence for that rather than an omission.
        AnimTriggerRegistry registry = Load();

        Assert.False(registry.TryGet(1010, out _));
        Assert.True(registry.TryGet(1011, out AnimTriggerDefinition next));
        Assert.Equal("mp5_reload_5", next.Resource);
    }

    [RequiresGameDataFact]
    public void TheM16A2ReloadEventsResolveToTheThreeRetailSounds()
    {
        AnimTriggerRegistry registry = Load();

        Assert.Equal("m16_reload_1", registry.Definitions[1003].Resource);
        Assert.Equal("m16_reload_2", registry.Definitions[1004].Resource);
        Assert.Equal("m16_reload_3", registry.Definitions[1005].Resource);
    }

    [RequiresGameDataFact]
    public void TheFlagIsSetExceptOnTheSurfaceSounds()
    {
        AnimTriggerRegistry registry = Load();

        List<string> clear = [.. registry.Definitions.Values
            .Where(d => !d.Flag)
            .Select(d => d.Resource)
            .Order()];

        Assert.Equal(["walk_fence_1", "walk_fence_2", "walk_fence_3", "walk_fence_4"], clear);
    }

    [RequiresGameDataFact]
    public void EveryEventIdInTheAnimationDataEitherResolvesOrIsEngineHandled()
    {
        // Cross-check against the other half of the system: every id the animation sets actually carry
        // is either in this table or is one of the small engine-internal ids.
        AnimTriggerRegistry registry = Load();
        int scripted = 0;
        int engine = 0;
        Dictionary<int, int> unresolved = [];

        foreach (int id in RetailAnimationEventIds())
        {
            if (registry.TryGet(id, out _))
            {
                scripted++;
            }
            else
            {
                unresolved[id] = unresolved.GetValueOrDefault(id) + 1;
                engine++;
            }
        }

        Assert.Equal(245, scripted + engine);
        Assert.Equal(97, scripted);

        // The unresolved ids split cleanly in two: a handful below thirteen, and the five ids in the
        // 1000-block that the table leaves out. Every one of those five gaps is used by the animation
        // data, so they are reserved for the engine rather than forgotten.
        List<int> high = [.. unresolved.Keys.Where(id => id >= 1000).Order()];
        Assert.Equal([1010, 1036, 1037, 1038, 1039], high);
        Assert.All(unresolved.Keys.Where(id => id < 1000), id => Assert.InRange(id, 0, 12));

        AnimTriggerRegistry table = registry;
        Assert.All(high, id => Assert.False(table.TryGet(id, out _)));
    }

    private static IEnumerable<int> RetailAnimationEventIds()
    {
        foreach (string name in new[] { "000", "001", "002", "003", "005", "006" })
        {
            string path = Path.Combine(GameData.RequireRoot(), "common", "anims", $"{name}.iff");
            Formats.Animation.BoneAnimationSet set =
                Formats.Animation.BoneAnimationSet.Read(File.ReadAllBytes(path));
            foreach (Formats.Animation.BoneAnimation animation in set.Animations)
            {
                foreach (Formats.Animation.BoneAnimationEvent e in animation.Events)
                {
                    yield return e.EventId;
                }
            }
        }
    }

    private static AnimTriggerRegistry Load()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);

        GameLoop loop = new(new ManualTimeSource());
        AnimTriggerRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Load(resources, loop.Natives);
        return registry;
    }
}
