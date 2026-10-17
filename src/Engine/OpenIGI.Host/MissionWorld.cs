using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Host;

/// <summary>
/// The played mission's world, as the host keeps it: the level's registries of what can be opened, ridden, picked
/// up, seen, heard and blown up, and its cutscene and objective runtimes.
/// </summary>
/// <remarks>
/// Each is <see langword="null"/> until the mission system that makes it has run (see <see cref="MissionSystemNames"/>), and
/// is dropped again as the mission is released. Held in one place so that what reads the level -- the inspector, the
/// sounds, the effects -- is handed the mission rather than the whole host.
/// </remarks>
internal sealed class MissionWorld
{
    /// <summary>Gets or sets the doors.</summary>
    public DoorRegistry? Doors { get; set; }

    /// <summary>Gets or sets the elevators.</summary>
    public ElevatorRegistry? Elevators { get; set; }

    /// <summary>Gets or sets what the player and the soldiers use: switches, terminals, ladders and the rest.</summary>
    public WorldInteractionRegistry? Interactions { get; set; }

    /// <summary>Gets or sets the weapons lying in the level.</summary>
    public GunPickupRegistry? GunPickups { get; set; }

    /// <summary>Gets or sets the level's other pickups.</summary>
    public GenericPickupRegistry? GenericPickups { get; set; }

    /// <summary>Gets or sets the alarm speakers.</summary>
    public AlarmSpeakerRegistry? AlarmSpeakers { get; set; }

    /// <summary>Gets or sets what can be destroyed.</summary>
    public DestructibleObjectRegistry? Destructibles { get; set; }

    /// <summary>Gets or sets the security cameras.</summary>
    public SecurityCameraRegistry? SecurityCameras { get; set; }

    /// <summary>Gets or sets the radar dishes and the authored rotating tasks.</summary>
    public RotatingObjectRegistry? RotatingObjects { get; set; }

    /// <summary>Gets or sets the level's authored ambience boxes.</summary>
    public AmbientAreaRegistry? Ambience { get; set; }

    /// <summary>Gets or sets the cutscene runtime, which also walks the level each tick.</summary>
    public CutsceneRuntime? Cutscenes { get; set; }

    /// <summary>Gets or sets the objective runtime.</summary>
    public MissionObjectiveRuntime? Objectives { get; set; }
}
