using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Every shipped I.G.I. 2 level, turned into the scene the shared world renderer draws.
/// </summary>
/// <remarks>
/// <para>
/// The end of the M2-3 data chain. Four milestones established the pieces separately — the placements,
/// the meshes, the materials, the lightmaps — and this is the one test that puts all of them through the
/// type a renderer actually consumes.
/// </para>
/// <para>
/// It also proves the claim the move rests on: <c>WorldRenderer</c> is title-agnostic. If it were not,
/// the sequel's scene could not be expressed in the placement record it draws.
/// </para>
/// </remarks>
public sealed class RetailIgi2DrawableSceneTests
{
    /// <summary>Every level builds a drawable scene, and its geometry is real.</summary>
    /// <remarks>
    /// Counted exactly. A regression anywhere upstream — the pack order, the mesh resolve, the lightmap
    /// binding — moves one of these numbers, and which one moves says which stage broke.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_level_builds_a_scene_the_shared_renderer_can_draw()
    {
        int levels = 0;
        int placements = 0;
        int lightmapped = 0;
        int unresolved = 0;
        long triangles = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            levels++;
            Igi2DrawableSceneResult built = Build(level);
            placements += built.Scene.Placements.Count;
            lightmapped += built.LightmappedCount;
            unresolved += built.UnresolvedCount;

            foreach (WorldPlacement placement in built.Scene.Placements)
            {
                Assert.NotNull(placement.Model);
                Assert.NotNull(placement.Pack);
                Assert.False(string.IsNullOrEmpty(placement.Instance.Name));
                triangles += placement.Model.TriangleCount;
            }
        }

        Assert.Equal(24, levels);
        Assert.Equal(9_338, placements);
        Assert.Equal(7_492, lightmapped);
        Assert.Equal(1_570_992L, triangles);

        // The two are `soldier_1`, which is in no archive anywhere in the installation -- the residue
        // RetailIgi2ModelTableTests names. A scene that silently dropped more than that would still look
        // fine on screen, which is why the number is pinned rather than tolerated.
        Assert.Equal(2, unresolved);
    }

    /// <summary>A lightmapped placement names an entry the level's archive really holds.</summary>
    /// <remarks>
    /// <para>
    /// The renderer looks a bake up by bare entry name, which is why <see cref="WorldPlacement"/> now
    /// carries one rather than only the pieces to format one. Project I.G.I. computes
    /// <c>&lt;set&gt;_&lt;%05d&gt;.olm</c>; the sequel reads the name out of the archive, because one of
    /// its 24 levels registers full paths where the rest register bare names.
    /// </para>
    /// <para>
    /// So this checks the thing the renderer will do: take the name off the placement, look it up the
    /// way the renderer keys its dictionary, and find the bytes.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_lightmapped_placement_names_an_entry_the_archive_holds()
    {
        int checkedEntries = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            if (!level.HasLightmaps)
            {
                continue;
            }

            Dictionary<string, ReadOnlyMemory<byte>> byBareName = new(StringComparer.OrdinalIgnoreCase);
            foreach (Formats.Ilff.IresEntry entry in level.LightmapArchive.Entries)
            {
                int slash = entry.Name.LastIndexOfAny(['/', '\\']);
                byBareName[slash >= 0 ? entry.Name[(slash + 1)..] : entry.Name] = entry.Data;
            }

            foreach (WorldPlacement placement in Build(level).Scene.Placements)
            {
                if (!placement.HasLightmap)
                {
                    continue;
                }

                string name = placement.LightmapEntryName();
                Assert.True(byBareName.TryGetValue(name, out ReadOnlyMemory<byte> bytes), $"'{name}' is not in the archive.");

                ObjectLightmap map = ObjectLightmap.Read(bytes, AssetFormatProfile.Igi2);
                Assert.NotEmpty(map.Images);
                checkedEntries++;
            }
        }

        Assert.Equal(7_492, checkedEntries);
    }

    /// <summary>The world scene carries no characters.</summary>
    /// <remarks>
    /// A character is drawn by a character presenter from a skinned mesh, not by the world pass. The
    /// filter is <see cref="Igi2WorldScene.CharacterTypes"/>, and this is what says it is actually
    /// applied: the difference between everything placed and everything drawable is exactly the
    /// characters plus whatever the packs do not resolve.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_world_scene_leaves_the_characters_to_a_character_presenter()
    {
        int placed = 0;
        int characters = 0;
        int drawable = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2DrawableSceneResult built = Build(level);
            placed += built.Placed.Placements.Count;
            characters += built.Placed.Placements.Count(static p => Igi2WorldScene.CharacterTypes.Contains(p.TypeName));
            drawable += built.Scene.Placements.Count;

            foreach (WorldPlacement placement in built.Scene.Placements)
            {
                Assert.DoesNotContain(placement.TaskName, Igi2WorldScene.CharacterTypes);
            }
        }

        Assert.Equal(10_569, placed);
        Assert.Equal(1_229, characters);
        // Everything placed, less the characters, less the two the shipped data cannot resolve.
        Assert.Equal(placed - characters - 2, drawable);
    }

    private static Igi2DrawableSceneResult Build(Igi2Level level)
    {
        // The real tree behind the registry, because the common pack names six animation sets and the
        // pack loader reads them straight through the file system rather than from a registered key.
        string root = GameDataSource.Igi2.RequireRoot();
        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(root));
        ResourceRegistry resources = new(files);
        List<LoadedPack> packs = [];

        foreach ((string directory, string name) in level.PackSources())
        {
            Register(resources, directory, name, root);
            packs.Add(LoadedPack.Open(
                ResourceDirectory(directory, root),
                name,
                resources,
                logger: null,
                AssetFormatProfile.Igi2));
        }

        string levelDirectory = ResourceDirectory(level.Directory, root);
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
            []);

        return Igi2DrawableScene.Build(
            mission,
            level.Root,
            level.Table,
            level.HasLightmaps ? level.Lightmaps : null,
            packs);
    }

    private static string ResourceDirectory(string directory, string root) =>
        "LOCAL:" + Path.GetRelativePath(root, directory).Replace('\\', '/') + "/";

    private static void Register(ResourceRegistry resources, string directory, string name, string root)
    {
        string prefix = ResourceDirectory(directory, root);
        Add(resources, $"{prefix}{name}.mtp", Path.Combine(directory, $"{name}.mtp"));
        Add(resources, $"{prefix}models/{name}.res", Path.Combine(directory, "models", $"{name}.res"));
        Add(resources, $"{prefix}textures/{name}.res", Path.Combine(directory, "textures", $"{name}.res"));
        Add(resources, $"{prefix}lightmaps/lightmaps.res", Path.Combine(directory, "lightmaps", "lightmaps.res"));
    }

    private static void Add(ResourceRegistry resources, string key, string path)
    {
        if (File.Exists(path))
        {
            resources.Register(key, File.ReadAllBytes(path));
        }
    }
}
