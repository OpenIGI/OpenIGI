using OpenIGI.Game.Ai;

namespace OpenIGI.Game.World;

/// <summary>The level's objects, as the developer inspector lists and finds them.</summary>
public interface IInspectableMission
{
    /// <summary>Gets the level's doors, once built.</summary>
    IReadOnlyList<PlacedDoor>? Doors { get; }

    /// <summary>Gets the level's lifts, once built.</summary>
    IReadOnlyList<PlacedElevator>? Elevators { get; }

    /// <summary>Gets the level's world interactions, once built.</summary>
    IReadOnlyList<WorldInteraction>? Interactions { get; }

    /// <summary>Gets the weapons lying in the level, once built.</summary>
    IReadOnlyList<GunPickup>? GunPickups { get; }

    /// <summary>Gets the other things lying in the level to be picked up, once built.</summary>
    IReadOnlyList<GenericPickup>? GenericPickups { get; }

    /// <summary>Gets the level's alarm speakers, once built.</summary>
    IReadOnlyList<AlarmSpeaker>? AlarmSpeakers { get; }

    /// <summary>Gets the level's security cameras, once built.</summary>
    IReadOnlyList<SecurityCamera>? SecurityCameras { get; }
}
