using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Modding.Testing;

/// <summary>A Project I.G.I. mission loaded headless, as <see cref="MissionFixture.Igi1"/> loads it.</summary>
/// <param name="Loop">The loop its tasks live in.</param>
/// <param name="FileSystem">The file system the installation is mounted in.</param>
/// <param name="Resources">The resources its archives are open in.</param>
/// <param name="Loaded">The mission as the loader read it.</param>
/// <param name="Level">The mission's level.</param>
/// <param name="Scene">Its placed models, as its world scene places them.</param>
/// <param name="Directory">The mission's directory in the installation.</param>
/// <param name="Graphs">The mission's graphs, as the level places them, by their task ids.</param>
public sealed record Igi1Mission(
    GameLoop Loop,
    VirtualFileSystem FileSystem,
    ResourceRegistry Resources,
    LoadedMission Loaded,
    LevelTask Level,
    WorldScene Scene,
    string Directory,
    IReadOnlyDictionary<int, GraphFile> Graphs)
{
    /// <summary>Gets the mission's terrain, or <see langword="null"/> where it has none it can use.</summary>
    public TerrainData? Terrain => Loaded.Terrain is { IsUsable: true } terrain ? terrain : null;

    /// <summary>Gets the player's task, or <see langword="null"/> for a mission with none.</summary>
    public HumanPlayerTask? Player => PlayerSpawn.FindGameplayTask(Level);
}
