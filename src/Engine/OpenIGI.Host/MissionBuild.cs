using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Level;
using OpenIGI.Game.World;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Presentation;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Host;

/// <summary>
/// A mission being brought up by the host: the loaded mission, its tree and how it arrived, and what each of the host's
/// mission systems leaves for the ones after it.
/// </summary>
/// <remarks>
/// Both titles pass through these systems: they build the picture, the sound and the I.G.I. rig, and a title that brings
/// its own player stands it in <see cref="MissionSystemNames.Player"/>. What a system leaves is set as it finishes, so a
/// later one that reads it finds it set; see <see cref="MissionSystemNames"/> for the order.
/// </remarks>
internal sealed class MissionBuild : LevelBuild
{
    /// <summary>Initializes a new instance of the <see cref="MissionBuild"/> class.</summary>
    /// <param name="walker">The level's walk, which the cutscene runtime and the objectives tick.</param>
    /// <param name="services">The session's services.</param>
    /// <param name="loaded">The mission's descriptor, packs and terrain.</param>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="id">The mission id.</param>
    /// <param name="promoted">Whether the level arrived as the menu's backdrop.</param>
    public MissionBuild(LevelUpdateWalker walker, IServiceCatalog services, LoadedMission loaded, LevelTask levelRoot, int id, bool promoted)
        : base(walker, services)
    {
        Walker = walker;
        Loaded = loaded ?? throw new ArgumentNullException(nameof(loaded));
        LevelRoot = levelRoot ?? throw new ArgumentNullException(nameof(levelRoot));
        Id = id;
        Promoted = promoted;
    }

    /// <summary>Gets the level's walk.</summary>
    public LevelUpdateWalker Walker { get; }

    /// <summary>Gets the mission's descriptor, packs and terrain.</summary>
    public LoadedMission Loaded { get; }

    /// <summary>Gets the level's task tree.</summary>
    public LevelTask LevelRoot { get; }

    /// <summary>Gets the mission id.</summary>
    public int Id { get; }

    /// <summary>Gets a value indicating whether the level arrived as the menu's backdrop rather than being loaded.</summary>
    public bool Promoted { get; }

    /// <summary>Gets or sets the mission's text, addressed by its own keys.</summary>
    public MissionStringResolver? MissionText { get; set; }

    /// <summary>Gets or sets the objective runtime, once made; the cutscene natives clear its messages.</summary>
    public MissionObjectiveRuntime? Objectives { get; set; }

    /// <summary>Gets or sets the level's placed objects, walked.</summary>
    public WorldScene? Scene { get; set; }

    /// <summary>Gets or sets the collision index over the placed objects.</summary>
    public ObjectCollisionIndex? Solids { get; set; }

    /// <summary>Gets or sets the world's renderer.</summary>
    public IWorldPresenter? World { get; set; }

    /// <summary>Gets or sets the trace the soldiers and the cameras see through.</summary>
    public WorldTraceCast? WorldTrace { get; set; }

    /// <summary>Gets or sets the level's nav graphs by their task's id, in world space; empty when it ships none.</summary>
    public Dictionary<int, GraphFile>? Graphs { get; set; }
}

/// <summary>The host's mission systems, in the order a mission is brought up.</summary>
/// <remarks>
/// The order is load-bearing: the AI's ray cast closes over the terrain the first system sets, the interactions exist before
/// the brains are animated, and the weapon rosters load before <c>humanplayer.qvm</c> runs.
/// </remarks>
internal static class MissionSystemNames
{
    /// <summary>The terrain, the sounds and the score.</summary>
    public const string Sounds = "Sounds";

    /// <summary>The mission's text and the cutscene runtime.</summary>
    public const string Cutscenes = "Cutscenes";

    /// <summary>The objective runtime.</summary>
    public const string Objectives = "Objectives";

    /// <summary>The map computer, the tactical computer and the comms cards.</summary>
    public const string Computer = "Computer";

    /// <summary>The scene walk, the world renderer, the collision index, the vehicles and the weather.</summary>
    public const string Scene = "Scene";

    /// <summary>The soldiers' meshes.</summary>
    public const string Soldiers = "Soldiers";

    /// <summary>The doors, elevators, pickups and interactions.</summary>
    public const string Interactions = "Interactions";

    /// <summary>The security cameras and the rotating objects.</summary>
    public const string Cameras = "Cameras";

    /// <summary>The nav graphs.</summary>
    public const string NavGraphs = "NavGraphs";

    /// <summary>The probe field, the path-traced lightmaps and the distance fields, started.</summary>
    public const string LightBakes = "LightBakes";

    /// <summary>The soldiers' brains, the alarm speakers and the guards' rounds.</summary>
    public const string NavigationAi = "NavigationAi";

    /// <summary>The actors joining the level's walk.</summary>
    public const string LevelWalk = "LevelWalk";

    /// <summary>The terrain, sky, sun, grade, camera and lighting.</summary>
    public const string Picture = "Picture";

    /// <summary>The player.</summary>
    public const string Player = "Player";

    /// <summary>The forest and the water, and the mission reported built.</summary>
    public const string Adornments = "Adornments";
}
