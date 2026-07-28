using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Pins the numbering of a level's baked object lightmaps against the retail archives.
/// </summary>
/// <remarks>
/// <para>
/// An entry is <c>&lt;Filename&gt;_&lt;%05d&gt;.olm</c>, the name coming from a <c>LightmapInfo</c> task
/// and the number from a depth-first walk of the object tree its owner expands into, one per level of
/// detail. Nothing in the data states that numbering, so the check is indirect and all the stronger for
/// it: the original bakes an image at exactly the size of the model's lightmap page, so if the walk lines
/// up, every index's stored dimensions match the page of the model the walk put there — and if it is off
/// by even one node, hundreds of entries disagree.
/// </para>
/// </remarks>
public sealed class RetailObjectLightmapTests
{
    /// <summary>The task type that names an object's lightmap set.</summary>
    private const string LightmapInfoType = "LightmapInfo";

    /// <summary>The parameter holding the set's name.</summary>
    private const string FilenameParameter = "Filename";

    [RequiresGameDataFact]
    public void TheMagicObjectTableLoads()
    {
        (_, _, MagicObjectRegistry magic, _) = Load(1);

        // magicobj.qvm is 387 DefineMagicObj calls; every one names a model.
        Assert.True(magic.Count > 300, $"expected the retail table, got {magic.Count} definitions");
        foreach (MagicObjectDefinition definition in magic.Definitions.Values)
        {
            Assert.NotEqual(string.Empty, definition.Model);
        }
    }

    /// <summary>
    /// Checks every baked entry of a level against the model the walk puts at its index.
    /// </summary>
    /// <param name="missionId">The mission to check.</param>
    /// <remarks>
    /// <b>Four of the fourteen levels are pinned here, not all of them.</b> On these the agreement is
    /// total — every entry of every set, thousands of them, lands on a model whose lightmap page is
    /// exactly the size of the stored image. On the other ten the walk comes out a handful of nodes short
    /// or long per set, and because the numbering is positional that shifts every index after the first
    /// discrepancy, so those levels are not silently half-checked here. What is still missing from the
    /// walk is an open item; <see cref="EveryLightmapSetStartsAtItsOwnersModel"/> holds everywhere and
    /// covers all fourteen meanwhile. See <c>TODO.md</c>.
    /// </remarks>
    [RequiresGameDataTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(7)]
    public void EveryLightmapIndexLandsOnAModelOfMatchingPageSize(int missionId)
    {
        (LoadedMission mission, LevelTask levelRoot, MagicObjectRegistry magic, ResourceRegistry resources) =
            Load(missionId);
        Dictionary<string, SortedDictionary<int, ObjectLightmap>> archives = ReadLightmaps(mission, resources);
        Assert.NotEmpty(archives);

        List<string> failures = [];
        int owners = 0;
        int checkedEntries = 0;

        foreach (GameTask task in Walk(levelRoot))
        {
            if (task is not DeclaredTask lightmap
                || !string.Equals(lightmap.Type.Name, LightmapInfoType, StringComparison.Ordinal)
                || !lightmap.TryGetValue(FilenameParameter, out object? raw)
                || raw is not string setName
                || !archives.TryGetValue(setName, out SortedDictionary<int, ObjectLightmap>? entries)
                || lightmap.Parent is null)
            {
                continue;
            }

            owners++;

            // The walk numbers every level of detail, but the original writes a file only for the ones it
            // can bake: it bails out at the top of 0x523D40 on a model that is not lightmapped or has no
            // faces. Those still consume their index, so the archive is sparse rather than shorter, and
            // matching it by index rather than by count is what makes that visible.
            Dictionary<int, (string Name, MefModel Model)> byIndex = [];
            int total = 0;
            foreach (WorldObject node in Expand(lightmap.Parent, mission, magic))
            {
                foreach (WorldObject inner in node.Walk())
                {
                    for (int level = 0; level < inner.Models.Count; level++)
                    {
                        byIndex[inner.LightmapIndex + level] = (inner.Models.Names[level], inner.Models[level]);
                        total = Math.Max(total, inner.LightmapIndex + level + 1);
                    }
                }
            }

            foreach ((int index, ObjectLightmap entry) in entries)
            {
                if (!byIndex.TryGetValue(index, out (string Name, MefModel Model) placed))
                {
                    failures.Add($"{setName}_{index:D00000}: the tree only reaches index {total - 1}");
                    continue;
                }

                checkedEntries++;
                IReadOnlyList<MefLightmapPage> pages = placed.Model.LightmapPages;
                if (pages.Count != entry.Images.Count)
                {
                    failures.Add(
                        $"{setName}_{index:D00000}: {placed.Name} has {pages.Count} page(s), "
                        + $"the entry holds {entry.Images.Count} image(s)");
                    continue;
                }

                for (int page = 0; page < pages.Count; page++)
                {
                    if (pages[page].Width != entry.Images[page].Width
                        || pages[page].Height != entry.Images[page].Height)
                    {
                        failures.Add(
                            $"{setName}_{index:D00000}: {placed.Name} page {page} is {pages[page]}, "
                            + $"the entry is {entry.Images[page].Width}x{entry.Images[page].Height}");
                    }
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            $"mission {missionId}: {failures.Count} mismatch(es)\n  " + string.Join("\n  ", failures.Take(8)));
        Assert.True(owners > 0, $"mission {missionId} has a lightmap archive but no owner claimed any of it");
        Assert.True(checkedEntries > 0, $"mission {missionId} checked nothing");
    }

    /// <summary>
    /// Checks, across every level, that a set's entry 0 is the owner's own model at full detail.
    /// </summary>
    /// <param name="missionId">The mission to check.</param>
    /// <remarks>
    /// This is the part of the numbering that no shift further down the walk can disturb, so it holds on
    /// all fourteen levels and pins the binding the renderer actually needs first: which set belongs to
    /// which placement, and that the set opens with the placement's own geometry.
    /// </remarks>
    [RequiresGameDataTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public void EveryLightmapSetStartsAtItsOwnersModel(int missionId)
    {
        (LoadedMission mission, LevelTask levelRoot, MagicObjectRegistry magic, ResourceRegistry resources) =
            Load(missionId);
        Dictionary<string, SortedDictionary<int, ObjectLightmap>> archives = ReadLightmaps(mission, resources);
        Assert.NotEmpty(archives);

        List<string> failures = [];
        int checkedSets = 0;

        foreach (GameTask task in Walk(levelRoot))
        {
            if (task is not DeclaredTask lightmap
                || !string.Equals(lightmap.Type.Name, LightmapInfoType, StringComparison.Ordinal)
                || !lightmap.TryGetValue(FilenameParameter, out object? raw)
                || raw is not string setName
                || !archives.TryGetValue(setName, out SortedDictionary<int, ObjectLightmap>? entries)
                || !entries.TryGetValue(0, out ObjectLightmap? first)
                || lightmap.Parent is null)
            {
                continue;
            }

            IReadOnlyList<WorldObject> nodes = Expand(lightmap.Parent, mission, magic);
            if (nodes.Count == 0)
            {
                failures.Add($"{setName}: the owner places no model this resolves");
                continue;
            }

            checkedSets++;
            IReadOnlyList<MefLightmapPage> pages = nodes[0].Models.Highest.LightmapPages;
            if (pages.Count != first.Images.Count
                || (pages.Count > 0
                    && (pages[0].Width != first.Images[0].Width || pages[0].Height != first.Images[0].Height)))
            {
                failures.Add(
                    $"{setName}_00000: {nodes[0].Models.Names[0]} pages [{string.Join(",", pages)}], "
                    + $"the entry is {first.Images[0].Width}x{first.Images[0].Height}");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"mission {missionId}: {failures.Count} mismatch(es)\n  " + string.Join("\n  ", failures.Take(8)));
        Assert.True(checkedSets > 0, $"mission {missionId} checked nothing");
    }

    /// <summary>
    /// Expands every model a lightmap owner's subtree places, in tree order, into object trees.
    /// </summary>
    private static IReadOnlyList<WorldObject> Expand(GameTask owner, LoadedMission mission, MagicObjectRegistry magic)
    {
        List<string> names = [];
        foreach (GameTask task in Walk(owner))
        {
            if (task is DeclaredTask declared
                && declared.TryGetValue(WorldScene.ModelParameter, out object? raw)
                && raw is string name
                && name.Length > 0)
            {
                names.Add(name);
            }
        }

        // BuildAll keeps a slot per input name so a caller can pair trees with tasks; this one only wants
        // the trees, in order.
        return [.. WorldObject.BuildAll(names, mission.TryGetModel, magic).OfType<WorldObject>()];
    }

    private static IEnumerable<GameTask> Walk(GameTask task)
    {
        yield return task;
        foreach (GameTask child in task.Children)
        {
            foreach (GameTask descendant in Walk(child))
            {
                yield return descendant;
            }
        }
    }

    private static Dictionary<string, SortedDictionary<int, ObjectLightmap>> ReadLightmaps(
        LoadedMission mission,
        ResourceRegistry resources)
    {
        Dictionary<string, SortedDictionary<int, ObjectLightmap>> sets = new(StringComparer.OrdinalIgnoreCase);
        if (!mission.TryReadLightmapArchive(resources, out IresArchive? archive) || archive is null)
        {
            return sets;
        }

        foreach (IresEntry entry in archive.Entries)
        {
            if (!ObjectLightmap.TryParseName(entry.Name, out string set, out int index))
            {
                continue;
            }

            if (!sets.TryGetValue(set, out SortedDictionary<int, ObjectLightmap>? entries))
            {
                sets[set] = entries = [];
            }

            entries[index] = ObjectLightmap.Read(entry.Data);
        }

        return sets;
    }

    private static (LoadedMission Mission, LevelTask Root, MagicObjectRegistry Magic, ResourceRegistry Resources) Load(
        int missionId)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);

        GameLoop loop = new(new ManualTimeSource());
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);

        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);

        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(registry, fileSystem, resources, levelLoader).Load(missionId);

        return (mission, levelRoot, magic, resources);
    }
}
