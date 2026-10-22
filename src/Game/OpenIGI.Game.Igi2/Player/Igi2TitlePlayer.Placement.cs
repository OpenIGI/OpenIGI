using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Developer fly-camera placement, matching IGI1's return-to-walking policy.</content>
public sealed partial class Igi2TitlePlayer : ITitlePlayerPlacement
{
    /// <inheritdoc/>
    public bool TryPlace(Vector3d cameraPosition, float yaw)
    {
        if (!CanPreviewLook) { return false; }
        // Remaster developer policy: PlayerCamera.Place/PlayerBody.PlaceOnGround search up to 500 metres below the camera.
        WorldLineTrace trace = new(1) { Mesh = 1 };
        trace.Starts[0] = new(cameraPosition.X, cameraPosition.Y,
            ((Fpu)cameraPosition.Z + HumanGroundQuery.ProbeOriginStandingInUnits).Value);
        CollisionWorld.NextZ(trace, [-PlayerBody.PlacementSearchUnits], uint.MaxValue);
        if (!trace.Intersections[0].IsHit) { return false; }

        Vector3d feet = trace.Intersections[0].Position;
        Human.Position = feet;
        Human.WorldPreviousPosition = feet;
        Human.Speed = default;
        Human.AccumulatedSpeed = default;
        Human.ImpactSpeed = default;
        Human.TotalSpeed = default;
        Human.SlidingSpeed = default;
        Human.GroundMovementSpeed = default;
        Human.DeltaTranslation = default;
        Human.Alpha = 0f;
        // The free camera turns clockwise from Y; IGI2's body gamma turns counterclockwise.
        Human.Gamma = -yaw;
        Human.IdealGamma = Human.Gamma;
        Human.Orientation = Igi2Pose.FromAngles(default, new(0f, 0f, Human.Gamma)).ToMatrix();
        Human.Channels.Restore([], []);
        Array.Clear(_frame);
        ResetLook();
        Human.Locomotion.Enter(Igi2PlayerState.Stand, Human);
        Human.GroundTolerance = Igi2Human.LandingGroundTolerance;
        Human.MaintainCollisionZOffset();
        // human.c Human_GroundCollision / Human_UpdatePlatformPos: acquire the new floor and its platform frame.
        Human.GroundCollision(feet, ref feet, Human.GroundTolerance);
        Human.Position = feet;
        Human.UpdatePlatformPosition(forcePosition: true, forceOrientation: true);
        float pitch = Human.View.IdealPitch;
        Human.View.LookAhead();
        Human.View.Pitch = pitch;
        Human.View.IdealPitch = pitch;
        Human.View.ImpactOffset = 0f;
        Human.View.ShakeFactor = 0f;
        Human.TouchPose();
        Human.UpdateViewBody(updateAnimation: false);
        _heldTick = -1;
        return true;
    }
}
