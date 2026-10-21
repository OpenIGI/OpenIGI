using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Where the body's collision sits for each posture.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>How far above the feet the footing query starts for a standing or crouching body: one metre.</summary>
    public const float UprightGroundCheck = 4096f;

    /// <summary>How far above the feet the footing query starts for a body lying flat: 0.3 metres.</summary>
    public const float LyingGroundCheck = 1228.8f;

    /// <summary>How far above the feet the footing query starts in a tucked jump: 1.5 metres.</summary>
    public const float TuckedGroundCheck = 6144f;

    /// <summary>How far the collision body is lifted in a tucked jump: half a metre.</summary>
    public const float TuckedCollisionLift = 2048f;

    /// <summary>The task type a train answers to, whose deck a body lying flat is checked from an upright height.</summary>
    public const string TrainType = "Train";

    /// <summary>
    /// Fits the collision body to the posture: <c>HumanPlayer_MaintainCollisionZOffset</c> (<c>igi2 0x4B89A0</c>),
    /// which <c>HumanPlayer_RunDelayedHandler</c> (<c>0x4B8A50</c>) calls each tick.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Standing or crouching, the footing is looked for from a metre up and the body is not lifted, unless the
    /// player is in a jump with crouch held. Then the legs are tucked: the body lifts half a metre, and the footing
    /// is looked for from a metre and a half.
    /// </para>
    /// <para>
    /// Lying flat, the footing is looked for from 0.3 metres, or from a metre on a train, whose deck would
    /// otherwise pass through a prone body as it moves. Any other posture keeps what it had.
    /// </para>
    /// </remarks>
    public void MaintainCollisionZOffset()
    {
        switch (Pose)
        {
            case Igi2HumanPose.Standing or Igi2HumanPose.Crouching:
                if (Signal.HasFlag(Igi2PlayerSignal.Crouch) && Locomotion.CurrentState == Igi2PlayerState.Jump)
                {
                    CollisionZOffset = TuckedCollisionLift;
                    GroundCheckZOffset = TuckedGroundCheck;
                    return;
                }

                CollisionZOffset = 0f;
                GroundCheckZOffset = UprightGroundCheck;
                break;

            case Igi2HumanPose.LyingFlat:
                CollisionZOffset = 0f;
                GroundCheckZOffset = GroundCollider?.Owner is Igi2CollisionOwner { TypeName: TrainType }
                    ? UprightGroundCheck
                    : LyingGroundCheck;
                break;
        }
    }
}
