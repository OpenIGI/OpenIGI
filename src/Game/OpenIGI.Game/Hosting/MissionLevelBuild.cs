using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Level;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Hosting;

/// <summary>
/// A mission being brought up by the host: the loaded mission, its tree and how it arrived, and what each of the host's
/// mission systems leaves for the ones after it.
/// </summary>
/// <remarks>
/// <para>
/// Both titles pass through these systems: they build the picture, the sound and the I.G.I. rig, and a title that brings
/// its own player stands it in <see cref="MissionSystemNames.Player"/>. What a system leaves is set as it finishes, so a
/// later one that reads it finds it set; see <see cref="MissionSystemNames"/> for the order.
/// </para>
/// <para>
/// A mod edits these systems by this type (<c>IModBuilder.EditLevelSystems&lt;MissionLevelBuild&gt;</c>): what is here is
/// what a system of its own, inserted among the host's by their names, can read and leave.
/// </para>
/// </remarks>
public class MissionLevelBuild : LevelBuild
{
    /// <summary>Initializes a new instance of the <see cref="MissionLevelBuild"/> class.</summary>
    /// <param name="walker">The level's walk, which the cutscene runtime and the objectives tick.</param>
    /// <param name="services">The session's services.</param>
    /// <param name="loaded">The mission's descriptor, packs and terrain.</param>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="id">The mission id.</param>
    /// <param name="promoted">Whether the level arrived as the menu's backdrop.</param>
    /// <param name="world">The level's state, which the systems fill.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public MissionLevelBuild(
        LevelUpdateWalker walker,
        IServiceCatalog services,
        LoadedMission loaded,
        LevelTask levelRoot,
        int id,
        bool promoted,
        MissionState world)
        : base(walker, services)
    {
        Walker = walker;
        Mission = world ?? throw new ArgumentNullException(nameof(world));
        Loaded = loaded ?? throw new ArgumentNullException(nameof(loaded));
        LevelRoot = levelRoot ?? throw new ArgumentNullException(nameof(levelRoot));
        Id = id;
        Promoted = promoted;
    }

    /// <summary>
    /// Gets the level's state as the systems fill it: its player once <see cref="MissionSystemNames.Player"/> has run, the bodies
    /// its shots meet, and the lists another game's tasks join.
    /// </summary>
    public MissionState Mission { get; }

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

    /// <summary>Gets or sets the trace the soldiers and the cameras see through.</summary>
    public WorldTraceCast? WorldTrace { get; set; }

    /// <summary>Gets or sets the level's nav graphs by their task's id, in world space; empty when it ships none.</summary>
    public Dictionary<int, GraphFile>? Graphs { get; set; }
}

/// <summary>The mission systems, in the order a mission is brought up.</summary>
/// <remarks>
/// <para>
/// The order is load-bearing: the AI's ray cast closes over the terrain the first system sets, the interactions exist before
/// the brains are animated, and the weapon rosters load before <c>humanplayer.qvm</c> runs. A mod inserts its own system by
/// these names.
/// </para>
/// <para>
/// The game brings its own steps (the cutscenes, objectives, vehicles, interactions, cameras, nav graphs, brains and walk)
/// and sets them before the host's; a <c>...View</c> system is the host's half of a step, handing what the game's left to the
/// picture.
/// </para>
/// </remarks>
public static class MissionSystemNames
{
    /// <summary>The terrain, the sounds and the score.</summary>
    public const string Sounds = "Sounds";

    /// <summary>The mission's text and the cutscene runtime.</summary>
    public const string Cutscenes = "Cutscenes";

    /// <summary>The objective runtime.</summary>
    public const string Objectives = "Objectives";

    /// <summary>The test captures' script writes, made over the objectives just built.</summary>
    public const string TestCaptures = "TestCaptures";

    /// <summary>The map computer, the tactical computer and the comms cards.</summary>
    public const string Computer = "Computer";

    /// <summary>The scene walk, the world renderer and the collision index.</summary>
    public const string Scene = "Scene";

    /// <summary>The vehicles, and what can be destroyed.</summary>
    public const string Vehicles = "Vehicles";

    /// <summary>The destructibles and the cutscenes handed to the world renderer, and the weather.</summary>
    public const string SceneView = "SceneView";

    /// <summary>The soldiers' meshes.</summary>
    public const string Soldiers = "Soldiers";

    /// <summary>The doors, elevators, pickups and interactions.</summary>
    public const string Interactions = "Interactions";

    /// <summary>The doors, elevators, pickups and interactions handed to the world renderer.</summary>
    public const string InteractionsView = "InteractionsView";

    /// <summary>The security cameras, their sight, and the rotating objects.</summary>
    public const string Cameras = "Cameras";

    /// <summary>The cameras and the rotating objects handed to the world renderer, and the cameras' status lights.</summary>
    public const string CamerasView = "CamerasView";

    /// <summary>The nav graphs.</summary>
    public const string NavGraphs = "NavGraphs";

    /// <summary>The probe field, the path-traced lightmaps and the distance fields, started.</summary>
    public const string LightBakes = "LightBakes";

    /// <summary>The soldiers' brains, the alarm speakers and the guards' rounds.</summary>
    public const string NavigationAi = "NavigationAi";

    /// <summary>The alarm speakers handed to the world renderer.</summary>
    public const string AlarmsView = "AlarmsView";

    /// <summary>The actors joining the level's walk.</summary>
    public const string LevelWalk = "LevelWalk";

    /// <summary>The terrain, sky, sun, grade, camera and lighting.</summary>
    public const string Picture = "Picture";

    /// <summary>The player.</summary>
    public const string Player = "Player";

    /// <summary>The forest and the water, and the mission reported built.</summary>
    public const string Adornments = "Adornments";

    /// <summary>The game's events: the mission's watch set up, and the level told loaded. The last system, after every mod's.</summary>
    public const string Events = "Events";
}
