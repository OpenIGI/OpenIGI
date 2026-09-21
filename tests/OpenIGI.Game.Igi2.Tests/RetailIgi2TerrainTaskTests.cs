using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// The three terrain task classes against the levels that declare them.
/// </summary>
/// <remarks>
/// <para>
/// Registering a class turns every declaration into a check on its <c>[ScriptParam]</c> attributes:
/// <see cref="TaskTypeRegistry.Declare"/> compares the declared list against the compiled one and throws
/// when they disagree. So loading all 24 levels is really 22 of them asserting the parameter names,
/// types and wire order — a much stronger claim than any test written by hand, and one recovered from
/// shipped data rather than from a disassembler.
/// </para>
/// <para>
/// The rest goes past the declaration and checks that the values mean what the classes say they mean, by
/// resolving each tile against the archive its files actually live in.
/// </para>
/// </remarks>
public sealed class RetailIgi2TerrainTaskTests
{
    /// <summary>Every level that declares the terrain types agrees with the classes implementing them.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Every_declaring_level_agrees_with_the_terrain_classes()
    {
        int declaring = 0;

        foreach (string script in RetailIgi2LevelLoadTests.LevelScripts())
        {
            RetailIgi2LevelLoadTests.Harness harness = new(script);

            // Throws if a declared parameter list disagrees with the class.
            harness.Loader.LoadScript(RetailIgi2LevelLoadTests.Harness.ScriptName);

            if (harness.Loop.Types.TryGet(TerrainTask.TypeName, out TaskType? terrain) && terrain.IsDeclared)
            {
                declaring++;
                Assert.False(terrain.IsDeclarationOnly, $"'{script}' left Terrain a placeholder.");
            }
        }

        // 22 of the 24 shipped levels. The two that do not are location1/level2, which has no terrain and
        // ships no heightmap archive, and location3/LEVEL4 -- see Some_shipped_terrain_data_is_orphaned.
        Assert.Equal(22, declaring);
    }

    /// <summary>A level's terrain tiles carry extents and ids the map files can actually be found under.</summary>
    /// <remarks>
    /// The end-to-end claim: the id a <c>TerrainMap</c> task declares, put through
    /// <see cref="TerrainMapTask.ResourceNameFor"/>, names a file that exists in the level's own
    /// <c>heightmaps.res</c> — and that file's header agrees with the extent the task declared, which is
    /// exactly the check <c>TerrainMap_LoadTerrainMap</c> makes before it accepts one.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_tile_resolves_to_maps_that_agree_with_it()
    {
        int tiles = 0;

        foreach ((string script, IresArchive archive, RetailIgi2LevelLoadTests.Harness harness) in Levels())
        {
            foreach (TerrainMapTask tile in TasksOf<TerrainMapTask>(harness.LevelRoot))
            {
                tiles++;

                Assert.InRange(tile.MapId, 0, TerrainMapTask.MaximumId - 1);
                Assert.True(tile.MapWidth > 0 && tile.MapHeight > 0, $"'{script}' tile {tile.MapId} has no extent.");
                Assert.True(
                    tile.WorldWidth > 0f && tile.WorldHeight > 0f,
                    $"'{script}' tile {tile.MapId} has no world size.");

                AssertResolves(archive, tile, Igi2TerrainMapKind.Height);
                AssertResolves(archive, tile, Igi2TerrainMapKind.Material);
                AssertResolves(archive, tile, Igi2TerrainMapKind.Light);
            }
        }

        // 46 tiles against 50 shipped map sets; the other four are orphaned, below.
        Assert.Equal(46, tiles);
    }

    /// <summary>
    /// Tile ids are sparse, so the id is a name and not a position in a list.
    /// </summary>
    /// <remarks>
    /// <c>location1/level1</c> and <c>location2/level1</c> each declare tiles 0 and 2, and
    /// <c>location2/LEVEL6</c> declares only tile 2. Anything that walked tiles by index rather than by
    /// the declared id would load the wrong file for those three levels and the right one everywhere
    /// else, which is the kind of bug that survives a spot check.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Tile_ids_are_sparse()
    {
        bool sawGap = false;

        foreach ((string _, IresArchive _, RetailIgi2LevelLoadTests.Harness harness) in Levels())
        {
            List<int> ids = [.. TasksOf<TerrainMapTask>(harness.LevelRoot).Select(static t => t.MapId).Order()];
            sawGap |= ids.Count > 0 && ids[^1] != ids.Count - 1;
        }

        Assert.True(sawGap, "No level declared a sparse tile id, so the sparse case is untested.");
    }

    /// <summary>Some shipped terrain data belongs to no level, and the reader must not assume otherwise.</summary>
    /// <remarks>
    /// <para>
    /// Two levels ship a <c>heightmaps.res</c> nothing reads. <c>location3/LEVEL4</c> declares no terrain
    /// at all yet carries two complete tiles, and <c>multiplayer/demo</c> carries two with no
    /// <c>objects.qvm</c> to declare them. Together those are the four map sets that separate the 50
    /// shipped ones from the 46 a level asks for.
    /// </para>
    /// <para>
    /// Worth pinning because it decides the direction of the relationship: the level is the authority and
    /// the archive is not, so anything that enumerated a level's terrain by walking its archive would
    /// load two tiles into a level the game leaves empty.
    /// </para>
    /// <para>
    /// It also explains the odd pair found in the format work: <c>location3/LEVEL4</c>'s two height maps
    /// are the only shipped files carrying a mip pyramid past their declared extent. Being the orphaned
    /// output of an older revision fits that exactly.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Some_shipped_terrain_data_is_orphaned()
    {
        string missions = Path.Combine(GameDataSource.Igi2.RequireRoot(), "missions");

        List<string> orphaned = [];
        foreach (string archive in Directory.EnumerateFiles(missions, "heightmaps.res", SearchOption.AllDirectories))
        {
            string levelDirectory = Path.GetDirectoryName(Path.GetDirectoryName(archive))!;
            string script = Path.Combine(levelDirectory, LevelScriptName);

            if (!File.Exists(script))
            {
                orphaned.Add(Path.GetFileName(levelDirectory));
                continue;
            }

            RetailIgi2LevelLoadTests.Harness harness = new(script);
            harness.Loader.LoadScript(RetailIgi2LevelLoadTests.Harness.ScriptName);

            if (!TasksOf<TerrainMapTask>(harness.LevelRoot).Any())
            {
                orphaned.Add(Path.GetFileName(levelDirectory));
            }
        }

        Assert.Equal<string[]>(["LEVEL4", "demo"], [.. orphaned.Order(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// A tile's material samples select the materials its level declares, apart from a scatter of stale
    /// ones the game shipped with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the claim that makes <see cref="TerrainMaterialTask.MaterialId"/> a lookup key rather than
    /// a label, and it is the test that would catch reading a <c>.tmm</c> at the wrong stride or offset —
    /// bytes taken from the wrong place miss the declared set roughly half the time.
    /// </para>
    /// <para>
    /// It is a ratio and not an equality because five shipped levels do miss, and the counts say why:
    /// 201 samples in <c>location1/level3</c>, 517 in <c>location3/level1</c>, 14 in two levels and 6 in
    /// two more — <b>758 samples out of 6.3 million, one in eight thousand</b>. Each is a few dozen
    /// texels left painted with a material the author later deleted from the level. A bound three orders
    /// of magnitude below a misread separates the two cleanly, where an equality would only force this
    /// test to enumerate the game's own loose ends.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Material_samples_select_declared_materials_apart_from_stale_scatter()
    {
        long samples = 0;
        long undeclared = 0;

        foreach ((string _, IresArchive archive, RetailIgi2LevelLoadTests.Harness harness) in Levels())
        {
            HashSet<int> declared =
                [.. TasksOf<TerrainMaterialTask>(harness.LevelRoot).Select(static m => m.MaterialId)];
            if (declared.Count == 0)
            {
                continue;
            }

            foreach (TerrainMapTask tile in TasksOf<TerrainMapTask>(harness.LevelRoot))
            {
                Igi2TerrainMap materials = ReadMap(archive, tile, Igi2TerrainMapKind.Material);

                foreach (byte selector in materials.Samples.Span)
                {
                    samples++;
                    if (!declared.Contains(selector))
                    {
                        undeclared++;
                    }
                }
            }
        }

        Assert.True(samples > 6_000_000, $"Only {samples} material samples were swept.");
        Assert.True(
            undeclared < samples / 1000,
            $"{undeclared} of {samples} material samples select a material their level does not declare.");
    }

    private const string LevelScriptName = "objects.qvm";

    private static IEnumerable<(string Script, IresArchive Archive, RetailIgi2LevelLoadTests.Harness Harness)> Levels()
    {
        foreach (string script in RetailIgi2LevelLoadTests.LevelScripts())
        {
            string archivePath = Path.Combine(Path.GetDirectoryName(script)!, "heightmaps", "heightmaps.res");
            if (!File.Exists(archivePath))
            {
                continue;
            }

            RetailIgi2LevelLoadTests.Harness harness = new(script);
            harness.Loader.LoadScript(RetailIgi2LevelLoadTests.Harness.ScriptName);

            yield return (script, IresArchive.Read(File.ReadAllBytes(archivePath)), harness);
        }
    }

    private static void AssertResolves(IresArchive archive, TerrainMapTask tile, Igi2TerrainMapKind kind)
    {
        Igi2TerrainMap map = ReadMap(archive, tile, kind);

        Assert.Equal(tile.MapWidth, map.Width);
        Assert.Equal(tile.MapHeight, map.Height);
    }

    private static Igi2TerrainMap ReadMap(IresArchive archive, TerrainMapTask tile, Igi2TerrainMapKind kind)
    {
        string name = EntryNameOf(tile, kind);
        Assert.True(archive.TryGetEntry(name, out IresEntry? entry), $"'{name}' is not in the archive.");
        return Igi2TerrainMap.Read(entry!.Data, kind);
    }

    /// <summary>Strips the alias off a resource name, leaving the name the archive files it under.</summary>
    /// <remarks>
    /// The archive keys its entries by bare filename — the resource script the editor generates adds them
    /// that way. Resolving the alias is the virtual file system's job, and this suite deliberately does
    /// not mount a mission, so it does the last step itself rather than pretending to.
    /// </remarks>
    private static string EntryNameOf(TerrainMapTask tile, Igi2TerrainMapKind kind)
    {
        string resource = tile.ResourceNameFor(kind);
        return resource[(resource.LastIndexOf('/') + 1)..];
    }

    private static IEnumerable<T> TasksOf<T>(GameTask root)
        where T : GameTask
    {
        foreach (GameTask child in root.Children)
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (T nested in TasksOf<T>(child))
            {
                yield return nested;
            }
        }
    }
}
