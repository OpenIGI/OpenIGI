using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.World;
using OpenIGI.Presentation;

namespace OpenIGI.Host;

/// <summary>A mission being brought up by the host, with what only the host's own systems hand each other: the world's renderer.</summary>
/// <remarks>See <see cref="MissionLevelBuild"/> for what every system, a mod's among them, reads and leaves.</remarks>
internal sealed class MissionBuild : MissionLevelBuild
{
    /// <summary>Initializes a new instance of the <see cref="MissionBuild"/> class.</summary>
    /// <param name="walker">The level's walk, which the cutscene runtime and the objectives tick.</param>
    /// <param name="services">The session's services.</param>
    /// <param name="loaded">The mission's descriptor, packs and terrain.</param>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="id">The mission id.</param>
    /// <param name="promoted">Whether the level arrived as the menu's backdrop.</param>
    /// <param name="world">The level's state, which the systems fill.</param>
    public MissionBuild(
        LevelUpdateWalker walker,
        IServiceCatalog services,
        LoadedMission loaded,
        LevelTask levelRoot,
        int id,
        bool promoted,
        MissionState world)
        : base(walker, services, loaded, levelRoot, id, promoted, world)
    {
    }

    /// <summary>Gets or sets the world's renderer.</summary>
    public IWorldPresenter? World { get; set; }
}
