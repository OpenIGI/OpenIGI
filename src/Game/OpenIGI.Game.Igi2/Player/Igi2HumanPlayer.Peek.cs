using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Player;

/// <content>
/// Peeking: while the peek key is held the player stands its ground, its body locked in its heading, and the mouse leans the view
/// out to either side, or up out of a crouch or off the ground, as far as the player's general settings allow; the weapon is
/// lowered aside meanwhile. Letting go returns to the stance it peeked from.
/// </content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>The weapon's tilt aside while peeking, <c>HumanPlayer_InitActionPeek</c>: 35° down and 20° across.</summary>
    private const float PeekObjectPitch = -0.6108652353286743f;

    private const float PeekObjectYaw = -0.3490658700466156f;

    /// <summary>How fast the weapon tilts to and from it, a tick, <c>vStepAlpha</c> and <c>vStepGamma</c>.</summary>
    private const float PeekObjectPitchStep = 0.0581776425242424f;

    private const float PeekObjectYawStep = 0.03324436768889427f;

    /// <summary>How far a whole unit of look moves the lean, world units.</summary>
    private const double PeekLookScale = 15000.0;

    /// <summary>Gets or sets a value indicating whether the look is inverted, as the active profile's input options keep it.</summary>
    public bool InvertMouse { get; set; }

    /// <summary>
    /// The peek action, <c>HumanPlayer_InitActionPeek</c>: activation is locked, the legs go to the peek state, the weapon tilts
    /// aside and the body's heading is locked.
    /// </summary>
    private void EnterPeekAction()
    {
        IsActivateLocked = true;
        Go(Igi2PlayerState.Peek);
        SetIdealObjectOrientation(PeekObjectPitch, PeekObjectYaw);
        IsLockOrientation = true;
        Action.Current = (_, _) => RunPeekAction();
    }

    /// <summary>
    /// <c>HumanPlayer_RunActionPeek</c>: on letting go of the key, or dying, the activate key must be let go of afresh, the heading
    /// and activation are freed, the lean is taken back, the legs return to the stance's own state, the weapon tilts back, and
    /// the action is idle again.
    /// </summary>
    private void RunPeekAction()
    {
        if (Channels[Igi2HumanChannel.Peek] != 0f && (ActionSignal & Igi2PlayerSignal.Dead) == 0)
        {
            return;
        }

        IsCheckActivateButton = true;
        IsLockOrientation = false;
        IsActivateLocked = false;
        View.PeekOffset = default;
        Go(Pose switch
        {
            Igi2HumanPose.Crouching => Igi2PlayerState.Crouch,
            Igi2HumanPose.LyingFlat => Igi2PlayerState.Flat,
            _ => Igi2PlayerState.Stand,
        });
        SetIdealObjectOrientation(0f, 0f);
        GoAction(Igi2PlayerActionState.Idle);
    }

    /// <summary>
    /// The peek state, <c>HumanPlayer_InitStatePeek</c>: the crouch's or the lying clip for those stances, the peek's own standing,
    /// and the standing precision aimed for.
    /// </summary>
    private void EnterPeek()
    {
        int animation = Pose switch
        {
            Igi2HumanPose.Crouching => Igi2PlayerState.Crouch,
            Igi2HumanPose.LyingFlat => Igi2PlayerState.Flat,
            _ => Igi2PlayerState.Peek,
        };
        Animation.PlayState(animation, 0, StateBlendSeconds);
        AimPrecision(Igi2PrecisionType.Standing);
        Locomotion.Current = (_, _) => RunPeek();
    }

    /// <summary>
    /// <c>HumanPlayer_RunStatePeek</c>: the player stands its ground, the binoculars may be raised (changing weapon), the look across
    /// leans the view sideways within the peek's reach, and, crouched or lying, the look up raises it within its height, the right
    /// way up whichever way the look is inverted; the look is then spent.
    /// </summary>
    private void RunPeek()
    {
        MoveOnGround(0f, GroundAcceleration, 0f, GroundAcceleration);
        CheckBinoculars(forceChangeWeapon: true);
        Igi2HumanPlayerGeneral general = General;
        Vector3f lean = View.PeekOffset;
        Vector3f speed = View.PeekSpeed;
        float lookX = Channels[Igi2HumanChannel.LookX];
        if (lookX != 0f)
        {
            speed = new Vector3f(((Fpu)general.PeekOffsetX / general.PeekTime).F, speed.Y, speed.Z);
            float reach = general.PeekOffsetX;
            float x = (((Fpu)lookX * PeekLookScale) + lean.X).F;
            lean = new Vector3f(-reach > x ? -reach : (reach >= x ? x : reach), lean.Y, lean.Z);
        }

        float lookY = Channels[Igi2HumanChannel.LookY];
        if (Pose is Igi2HumanPose.Crouching or Igi2HumanPose.LyingFlat && lookY != 0f)
        {
            speed = new Vector3f(speed.X, speed.Y, ((Fpu)general.PeekOffsetZ / general.PeekTime).F);
            Fpu z = ((Fpu)(InvertMouse ? 1.0 : -1.0) * lookY * PeekLookScale) + lean.Z;
            float height = z.Value < 0.0 ? 0f : (z.Value <= general.PeekOffsetZ ? z.F : general.PeekOffsetZ);
            lean = new Vector3f(lean.X, lean.Y, height);
        }

        View.PeekOffset = lean;
        View.PeekSpeed = speed;
        Channels[Igi2HumanChannel.LookX] = 0f;
        Channels[Igi2HumanChannel.LookY] = 0f;
    }

    // HumanView_SetIdealObjectOrientation, with the peek's steps.
    private void SetIdealObjectOrientation(float pitch, float yaw)
    {
        View.IdealObjectPitch = pitch;
        View.IdealObjectYaw = yaw;
        View.ObjectPitchStep = PeekObjectPitchStep;
        View.ObjectYawStep = PeekObjectYawStep;
    }
}
