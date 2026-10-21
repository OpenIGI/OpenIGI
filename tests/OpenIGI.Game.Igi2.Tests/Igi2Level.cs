using System.Diagnostics.CodeAnalysis;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// One shipped I.G.I. 2 level, loaded far enough to ask world questions of it.
/// </summary>
/// <remarks>
/// <para>
/// The scene, the pack table, the lightmap index and the archives behind them all come from the same
/// directory and are all needed together the moment a test asks anything end to end. Three suites had
/// grown their own copy of this walk; this is that copy, once.
/// </para>
/// <para>
/// The archives load lazily. A texture archive is tens of megabytes and most tests want none of them, so
/// paying for one per level per test is the difference between a suite that runs in a second and one
/// that does not.
/// </para>
/// </remarks>
internal sealed class Igi2Level
{
    private readonly Lazy<IReadOnlyList<IresArchive>> _models;
    private readonly Lazy<IReadOnlyList<IresArchive>> _textures;
    private readonly Lazy<IresArchive> _lightmapArchive;
    private readonly Lazy<Igi2LightmapIndex> _lightmaps;
    private readonly Lazy<IReadOnlySet<int>> _taskIds;
    private readonly Lazy<Igi2WorldScene> _scene;

    private readonly string _commonDirectory;

    private Igi2Level(string directory, string commonDirectory, GameTask root, Igi2ModelTable table)
    {
        _commonDirectory = commonDirectory;
        Directory = directory;
        Root = root;
        Table = table;

        _scene = new(() => Igi2WorldScene.Build(root, ModelFor));
        _models = new(() => Archives(directory, commonDirectory, "models"));
        _textures = new(() => Archives(directory, commonDirectory, "textures"));
        _lightmapArchive = new(() => IresArchive.Read(File.ReadAllBytes(LightmapArchivePath(directory))));
        _lightmaps = new(() => Igi2LightmapIndex.Read(_lightmapArchive.Value));
        _taskIds = new(() =>
        {
            HashSet<int> ids = [];
            CollectIds(root, ids);
            return ids;
        });
    }

    /// <summary>Gets the level's loaded task tree.</summary>
    public GameTask Root { get; private init; } = null!;

    /// <summary>Gets the level's directory on disk.</summary>
    public string Directory { get; }

    /// <summary>Gets everything the level places in the world.</summary>
    /// <remarks>
    /// Built with the level's own models to hand, because a <c>Train</c> cannot be placed without one:
    /// its carriages are spaced by the distance between their model's two couplings. Lazy for the same
    /// reason -- the archives are, and a harness that never looks at the scene should not open them.
    /// </remarks>
    public Igi2WorldScene Scene => _scene.Value;

    /// <summary>Resolves a placement's model name to the mesh behind it, through the pack table.</summary>
    /// <param name="model">The name as level data writes it.</param>
    /// <returns>The mesh, or <see langword="null"/> when nothing the level ships holds it.</returns>
    public MefModel? ModelFor(string model) =>
        Table.TryResolveMesh(model, out string? mesh) && TryReadModel(mesh, out MefModel? read) ? read : null;

    /// <summary>Gets the level's pack table, over its own pack and its location's common one.</summary>
    public Igi2ModelTable Table { get; }

    /// <summary>Gets the level's model archives, its own first and its location's second.</summary>
    public IReadOnlyList<IresArchive> Models => _models.Value;

    /// <summary>Gets the level's texture archives, in the same order.</summary>
    public IReadOnlyList<IresArchive> Textures => _textures.Value;

    /// <summary>Gets the level's <c>lightmaps.res</c>.</summary>
    public IresArchive LightmapArchive => _lightmapArchive.Value;

    /// <summary>Gets the level's baked object lightmaps, indexed by the task each belongs to.</summary>
    public Igi2LightmapIndex Lightmaps => _lightmaps.Value;

    /// <summary>Gets the level task id of every task the level declares one for.</summary>
    public IReadOnlySet<int> TaskIds => _taskIds.Value;

    /// <summary>Gets the directory and pack name of each pack the game opens, in load order.</summary>
    /// <returns>The four sources: the global common pack, <c>new</c>, the location's, and the level's.</returns>
    public IEnumerable<(string Directory, string Name)> PackSources()
    {
        string root = GameDataSource.Igi2.RequireRoot();
        foreach ((string directory, string name) in new[]
                 {
                     (Path.Combine(root, "COMMON"), "common"),
                     (Path.Combine(root, "COMMON"), "new"),
                     (_commonDirectory, Path.GetFileNameWithoutExtension(
                         System.IO.Directory.Exists(_commonDirectory)
                             ? System.IO.Directory.EnumerateFiles(_commonDirectory, "*.mtp").FirstOrDefault() ?? string.Empty
                             : string.Empty)),
                     (Directory, Path.GetFileNameWithoutExtension(
                         System.IO.Directory.EnumerateFiles(Directory, "*.mtp").First())),
                 })
        {
            if (name.Length > 0 && File.Exists(Path.Combine(directory, name + ".mtp")))
            {
                yield return (directory, name);
            }
        }
    }

    /// <summary>Gets a value indicating whether the level ships baked object lightmaps.</summary>
    public bool HasLightmaps => File.Exists(LightmapArchivePath(Directory));

    /// <summary>Loads the level's ground from its <c>heightmaps.res</c>.</summary>
    /// <returns>The ground, which is empty for a level that ships no maps.</returns>
    public Igi2TerrainSurface Terrain()
    {
        string path = Path.Combine(Directory, "heightmaps", "heightmaps.res");
        IresArchive? archive = File.Exists(path) ? IresArchive.Read(File.ReadAllBytes(path)) : null;
        return Igi2TerrainSurface.Build(Root, archive);
    }

    /// <summary>Loads one shipped level by its location and level directory names.</summary>
    /// <param name="location">The location directory, e.g. <c>location1</c>.</param>
    /// <param name="level">The level directory, e.g. <c>level1</c>.</param>
    /// <returns>The level.</returns>
    public static Igi2Level Find(string location, string level) =>
        All().Single(l =>
            Path.GetFileName(l.Directory).Equals(level, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(Path.GetDirectoryName(l.Directory)!).Equals(location, StringComparison.OrdinalIgnoreCase));

    /// <summary>Loads every shipped level that has a pack, in mission order.</summary>
    /// <returns>The levels.</returns>
    public static IEnumerable<Igi2Level> All()
    {
        foreach (string script in RetailIgi2LevelLoadTests.LevelScripts())
        {
            string directory = Path.GetDirectoryName(script)!;
            string commonDirectory = Path.Combine(Path.GetDirectoryName(directory)!, "common");

            string? levelPack = System.IO.Directory.EnumerateFiles(directory, "*.mtp").FirstOrDefault();
            if (levelPack is null)
            {
                continue;
            }

            // The order Game_CreateHandler loads them in: the global common pack, then new, then the
            // location's, then the level's. The first to name a model wins, so the order is behaviour.
            string root = GameDataSource.Igi2.RequireRoot();
            List<MtpPack> packs = [];
            foreach (string candidate in new[]
                     {
                         Path.Combine(root, "COMMON", "common.mtp"),
                         Path.Combine(root, "COMMON", "new.mtp"),
                         System.IO.Directory.Exists(commonDirectory)
                             ? System.IO.Directory.EnumerateFiles(commonDirectory, "*.mtp").FirstOrDefault() ?? string.Empty
                             : string.Empty,
                         levelPack,
                     })
            {
                if (candidate.Length > 0 && File.Exists(candidate))
                {
                    packs.Add(MtpPack.Read(File.ReadAllBytes(candidate)));
                }
            }

            RetailIgi2LevelLoadTests.Harness harness = new(script);
            harness.Loader.LoadScript(RetailIgi2LevelLoadTests.Harness.ScriptName);

            yield return new Igi2Level(
                directory,
                commonDirectory,
                harness.LevelRoot,
                Igi2ModelTable.Create(packs));
        }
    }

    /// <summary>Reads a resource out of a set of archives, nearest first.</summary>
    /// <param name="archives">The archives to search.</param>
    /// <param name="resource">The resource name.</param>
    /// <param name="bytes">Receives the bytes.</param>
    /// <returns><see langword="false"/> when no archive holds it.</returns>
    public static bool TryRead(IReadOnlyList<IresArchive> archives, string resource, out ReadOnlyMemory<byte> bytes)
    {
        foreach (IresArchive archive in archives)
        {
            if (archive.TryGetEntry(resource, out IresEntry? entry))
            {
                bytes = entry.Data;
                return true;
            }
        }

        bytes = default;
        return false;
    }

    /// <summary>Reads one of the level's meshes under the sequel's format profile.</summary>
    /// <param name="mesh">The mesh's bare name.</param>
    /// <param name="model">Receives the parsed model.</param>
    /// <returns><see langword="false"/> when no archive holds it.</returns>
    public bool TryReadModel(string mesh, [NotNullWhen(true)] out MefModel? model)
    {
        if (TryRead(Models, Igi2WorldScene.ModelResourceName(mesh), out ReadOnlyMemory<byte> bytes))
        {
            model = MefModel.Read(bytes, AssetFormatProfile.Igi2);
            return true;
        }

        model = null;
        return false;
    }

    private static string LightmapArchivePath(string directory) =>
        Path.Combine(directory, "lightmaps", "lightmaps.res");

    private static void CollectIds(GameTask task, HashSet<int> into)
    {
        foreach (GameTask child in task.Children)
        {
            if (child.Id != GameTask.NoId)
            {
                into.Add(child.Id);
            }

            CollectIds(child, into);
        }
    }

    private static List<IresArchive> Archives(string directory, string commonDirectory, string kind)
    {
        // The four packs' sibling archives, in the same order: COMMON/<kind>/ holds common.res and
        // new.res, then the location's, then the level's.
        List<IresArchive> archives = [];
        foreach (string root in new[]
                 {
                     Path.Combine(GameDataSource.Igi2.RequireRoot(), "COMMON", kind),
                     Path.Combine(commonDirectory, kind),
                     Path.Combine(directory, kind),
                 })
        {
            if (System.IO.Directory.Exists(root))
            {
                archives.AddRange(System.IO.Directory
                    .EnumerateFiles(root, "*.res")
                    .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)
                    .Select(static p => IresArchive.Read(File.ReadAllBytes(p))));
            }
        }

        return archives;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Path.GetFileName(Directory)}, {Scene.Placements.Count} placements";
}
