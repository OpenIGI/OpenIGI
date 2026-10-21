using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Turns a shipped level into the scene the shared world renderer draws, the way the host does.</summary>
/// <remarks>
/// The packs are opened in <c>Game_CreateHandler</c>'s order and registered under the <c>LOCAL:</c>
/// names the pack loader reads them back through, so the registry returned beside the scene is one a
/// <c>WorldRenderer</c> can resolve textures and lightmaps from.
/// </remarks>
internal static class Igi2LevelScene
{
    /// <summary>Builds the drawable scene, keeping the registry it was read through.</summary>
    /// <param name="level">The level.</param>
    /// <param name="magic">The magic object types attach points are looked up in, or <see langword="null"/> to place them by name.</param>
    /// <returns>The scene and the registry.</returns>
    public static (Igi2DrawableSceneResult Built, ResourceRegistry Resources) Build(Igi2Level level, World.Igi2MagicObjTypes? magic = null)
    {
        (List<LoadedPack> packs, ResourceRegistry resources) = Packs(level);
        string root = GameDataSource.Igi2.RequireRoot();
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

        return (
            Igi2DrawableScene.Build(
                mission,
                level.Root,
                level.Table,
                level.HasLightmaps ? level.Lightmaps : null,
                packs,
                magic: magic),
            resources);
    }

    /// <summary>Opens the level's four packs in load order, through a registry they register their archives in.</summary>
    /// <param name="level">The level.</param>
    /// <returns>The packs and the registry.</returns>
    public static (List<LoadedPack> Packs, ResourceRegistry Resources) Packs(Igi2Level level)
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

        return (packs, resources);
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
