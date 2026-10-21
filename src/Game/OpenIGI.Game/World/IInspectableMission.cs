using OpenIGI.Game.Ai;

namespace OpenIGI.Game.World;

/// <summary>The level's registries, as the developer inspector lists and finds them.</summary>
public interface IInspectableMission
{
    /// <summary>Gets the level's doors, once built.</summary>
    DoorRegistry? Doors { get; }

    /// <summary>Gets the level's lifts, once built.</summary>
    ElevatorRegistry? Elevators { get; }

    /// <summary>Gets the level's world interactions, once built.</summary>
    WorldInteractionRegistry? Interactions { get; }

    /// <summary>Gets the weapons lying in the level, once built.</summary>
    GunPickupRegistry? GunPickups { get; }

    /// <summary>Gets the other things lying in the level to be picked up, once built.</summary>
    GenericPickupRegistry? GenericPickups { get; }

    /// <summary>Gets the level's alarm speakers, once built.</summary>
    AlarmSpeakerRegistry? AlarmSpeakers { get; }

    /// <summary>Gets the level's security cameras, once built.</summary>
    SecurityCameraRegistry? SecurityCameras { get; }
}
