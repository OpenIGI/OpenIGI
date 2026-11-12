using OpenIGI.Core.Numerics;

using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>Project I.G.I.'s objects as the picture asks after them: its doors, lifts, cameras, cut-scene vehicles and the rest.</summary>
/// <remarks>
/// Reads the mission's registries as they stand when asked, so one that is built, replaced or dropped during a level's
/// bring-up needs no telling. The order a moved placement is looked for in is the draw's own: a door before the lift that
/// carries it, then the cut-scene's rotors and vehicles, an alarm light's reflector, a rotating object, a planted charge.
/// </remarks>
/// <param name="mission">The mission whose objects these are.</param>
public sealed class Igi1PlacementObjects(MissionWorld mission) : IPlacementObjects
{
    private readonly MissionWorld _mission = mission ?? throw new ArgumentNullException(nameof(mission));

    /// <inheritdoc/>
    public bool Claims(WorldPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        OpenIGI.Engine.Tasks.GameTask owner = placement.Owner;
        return _mission.Doors?.Claims(owner) == true
            || _mission.Elevators?.Claims(owner) == true
            || _mission.Cutscenes?.Claims(placement) == true
            || _mission.AlarmSpeakers?.ClaimsReflector(placement) == true
            || _mission.RotatingObjects?.Claims(placement) == true
            || _mission.Interactions?.ClaimsPlaced(owner) == true;
    }

    /// <inheritdoc/>
    public bool IsTaken(WorldPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return _mission.GenericPickups?.IsCollected(placement.Owner) == true;
    }

    /// <inheritdoc/>
    public bool IsBroken(WorldPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return _mission.Destructibles?.IsDestroyed(placement) == true;
    }

    /// <inheritdoc/>
    public bool IsLightGone(WorldPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return _mission.AlarmSpeakers?.IsLightGone(placement.Owner) == true;
    }

    /// <inheritdoc/>
    public bool WearsDestroyedModel(WorldPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return _mission.AlarmSpeakers?.IsDestroyed(placement.Owner) == true
            || _mission.Destructibles?.IsDestroyed(placement) == true;
    }

    /// <inheritdoc/>
    public int LightVariantOf(WorldPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return _mission.AlarmSpeakers?.LightVariantOf(placement) ?? -1;
    }

    /// <inheritdoc/>
    public bool TryGetSwivelOrientation(WorldPlacement placement, float tickFraction, bool smooth, out Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(placement);
        orientation = default;
        return _mission.SecurityCameras?.TryGetHeadOrientation(placement, tickFraction, smooth, out orientation) == true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A StationaryGun's stand turns with its gun: <c>0x474420</c> writes the traverse into the task's own orientation (the
    /// authored one times Z(traverse)), and that is what the stand model is drawn with. Only the traverse -- the elevation
    /// belongs to the gun alone -- so the yoke stays square to the gun it holds.
    /// </remarks>
    public bool TryGetStandOrientation(WorldPlacement placement, out Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(placement);

        // The type test first: TryGet walks the level's interactions, and this runs per placement.
        if (placement.Owner is { } owner
            && string.Equals(owner.Type.Name, "StationaryGun", StringComparison.Ordinal)
            && _mission.Interactions?.TryGet(owner, out WorldInteraction? interaction) == true
            && interaction?.StationaryGun is { } gun)
        {
            orientation = gun.TurnedOrientation;
            return true;
        }

        orientation = default;
        return false;
    }

    /// <inheritdoc/>
    public bool TryGetDrawTransform(
        WorldPlacement placement,
        float tickFraction,
        bool smooth,
        out Vector3d position,
        out Matrix3f orientation,
        out RotorDrawState rotor,
        out bool hasRotor)
    {
        ArgumentNullException.ThrowIfNull(placement);
        position = placement.Position;
        orientation = placement.Orientation;
        rotor = default;
        hasRotor = false;

        if (_mission.Doors?.TryGetDrawTransform(
            placement.Owner, tickFraction, smooth, out Vector3d doorPosition, out Matrix3f doorOrientation) == true)
        {
            position = doorPosition;
            orientation = doorOrientation;
            if (_mission.Elevators?.TryGetDisplacement(placement.Owner, tickFraction, smooth, out Vector3d doorDisplacement) == true)
            {
                position += doorDisplacement;
            }

            return true;
        }

        if (_mission.Elevators?.TryGetDrawTransform(
            placement.Owner,
            placement.Position,
            placement.Orientation,
            tickFraction,
            smooth,
            out Vector3d elevatorPosition,
            out Matrix3f elevatorOrientation) == true)
        {
            position = elevatorPosition;
            orientation = elevatorOrientation;
            return true;
        }

        if (_mission.Cutscenes?.TryGetRotorDrawState(placement, out rotor) == true)
        {
            position = rotor.Position;
            hasRotor = true;
            return true;
        }

        if (_mission.Cutscenes?.TryGetPlacementDrawTransform(
            placement, out Vector3d cinematicPosition, out Matrix3f cinematicOrientation) == true)
        {
            position = cinematicPosition;
            orientation = cinematicOrientation;
            return true;
        }

        if (_mission.AlarmSpeakers?.TryGetReflectorOrientation(placement, tickFraction, out Matrix3f reflectorOrientation) == true)
        {
            orientation = reflectorOrientation;
            return true;
        }

        if (_mission.RotatingObjects?.TryGetDrawTransform(
            placement, tickFraction, smooth, out Vector3d rotatingPosition, out Matrix3f rotatingOrientation) == true)
        {
            position = rotatingPosition;
            orientation = rotatingOrientation;
            return true;
        }

        if (_mission.Interactions?.TryGetPlacedPose(placement.Owner, out Vector3d placedUnits, out Matrix3f placedOrientation) == true)
        {
            // A planted charge: state 36 moved its EditRigidObj to where the plant's trace struck.
            position = placedUnits / WorldScene.UnitsPerMetre;
            orientation = placedOrientation;
            return true;
        }

        return false;
    }
}
