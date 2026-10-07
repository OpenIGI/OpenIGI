using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Player;

/// <content>The locomotion states and the movement they share.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>The animation controller's ticks per game frame, <c>AnimController_nTicksPerFrame</c> (<c>igi2 0x63681C</c>).</summary>
    public const int AnimationTicksPerFrame = 160;

    /// <summary>The ground acceleration while standing, and toward full speed when running, per tick.</summary>
    /// <remarks>The <c>vMaxYDelta</c> the stand, turn and run states pass <c>HumanPlayer_MoveOnGround</c>.</remarks>
    public const float GroundAcceleration = 118.76579f;

    /// <summary>The sideways acceleration while running forward, per tick.</summary>
    public const float RunForwardSideAcceleration = 63.624531f;

    /// <summary>Running forward's target speed at a movement-speed scale of 1, units per tick.</summary>
    public const float RunForwardSpeed = 890.74341f;

    /// <summary>How long a clip change blends in, seconds, in most states.</summary>
    public const float StateBlendSeconds = 0.2f;

    /// <summary>The animation time scale running forward (<c>HUMANPLAYER_RUNFORWARD_ANIMTIMESCALE</c>, 0x63D764).</summary>
    public const float RunForwardAnimationTimeScale = 1f;

    private TaskStateMachine CreateLocomotion()
    {
        var states = new TaskStateFunction?[Igi2PlayerState.Count];
        states[Igi2PlayerState.Stand] = (_, _) => EnterStand();
        states[Igi2PlayerState.StandTurn] = (_, _) => EnterStandTurn();
        states[Igi2PlayerState.RunForward] = (_, _) => EnterRunForward();

        return new TaskStateMachine(
            (_, _) => PackSignal(),
            states,
            Igi2PlayerState.Stand,
            exits: new TaskStateFunction?[Igi2PlayerState.Count]);
    }

    /// <summary>Steers the ground speed toward local targets, at most a given change per tick on each axis.</summary>
    /// <param name="forwardTarget">The target speed along the body's local Y axis.</param>
    /// <param name="maxForwardChange">The most the Y speed may change this tick.</param>
    /// <param name="sideTarget">The target speed along the body's local X axis.</param>
    /// <param name="maxSideChange">The most the X speed may change this tick.</param>
    /// <remarks>
    /// <c>HumanPlayer_MoveOnGround</c> (<c>igi2 0x4BEFB0</c>): the world speed is taken into the body's frame
    /// with the orientation's transpose, each horizontal axis moves toward its target clamped to its limit,
    /// the local vertical is dropped, and the result goes back through the orientation. So the legs never
    /// push vertically; gravity and the ground do that.
    /// </remarks>
    public void MoveOnGround(float forwardTarget, float maxForwardChange, float sideTarget, float maxSideChange)
    {
        Matrix3f o = Orientation;
        Vector3f s = GroundMovementSpeed;

        // The original keeps the two local components in float temporaries before stepping them.
        double localX = (float)((s.Y * (double)o.M21) + (s.X * (double)o.M11) + (o.M31 * (double)s.Z));
        double localY = (float)((s.X * (double)o.M12) + (o.M32 * (double)s.Z) + (s.Y * (double)o.M22));

        localY += Math.Clamp(forwardTarget - localY, -(double)maxForwardChange, maxForwardChange);
        localX += Math.Clamp(sideTarget - localX, -(double)maxSideChange, maxSideChange);

        GroundMovementSpeed = new Vector3f(
            (float)((localX * o.M11) + (localY * o.M12)),
            (float)((localY * o.M22) + (localX * o.M21)),
            (float)((localX * o.M31) + (localY * o.M32)));
    }

    /// <summary>Sets the gait's animation rate for this tick: slower uphill.</summary>
    /// <param name="timeScale">The state's own rate.</param>
    /// <remarks>
    /// <c>Human_RunningControl</c> (<c>igi2 0x5030E0</c>):
    /// <c>(1 − clamp(groundAngleFactor, −0.2, 1)) × 160 × timeScale</c> clip ticks per tick, truncated.
    /// </remarks>
    public void ControlGait(float timeScale)
    {
        double factor = Math.Clamp((double)GroundAngleFactor, -0.2, 1.0);
        Animation.MainTimeStep = (int)((1.0 - factor) * AnimationTicksPerFrame * timeScale);
    }

    /// <summary>Selects the posture precision a state aims for.</summary>
    /// <param name="type">The posture.</param>
    /// <param name="posture">Picks the weapon's precision for the posture.</param>
    /// <remarks>
    /// Every locomotion entry ends the same way: with a weapon held, the ideal precision is the posture's
    /// own plus <c>|turning speed| × turning modifier × 3</c>; with none it is zero.
    /// </remarks>
    private void AimPrecision(Igi2PrecisionType type, Func<Igi2WeaponPrecision, float> posture)
    {
        if (Weapon.ActivePrecision is { } precision)
        {
            PrecisionType = type;
            IdealModifiedPrecision = posture(precision) + (MathF.Abs(TurningSpeed) * precision.TurningModifier * 3f);
        }
        else
        {
            PrecisionType = Igi2PrecisionType.Standing;
            IdealModifiedPrecision = 0f;
        }
    }

    private void Go(int state) => Locomotion.Enter(state, this);

    private bool Has(Igi2PlayerSignal bit) => (Signal & bit) != 0;

    private bool Allows(Igi2AllowedStance stance) => (AllowedStance & stance) != 0;

    /// <summary>The interruptions standing still checks first, in its order: death, water, the ground, a hit.</summary>
    private bool TryStandingInterruptions()
    {
        if (Has(Igi2PlayerSignal.Dead))
        {
            Go(Igi2PlayerState.Die);
        }
        else if (Has(Igi2PlayerSignal.Swimming))
        {
            Go(Igi2PlayerState.Swim);
        }
        else if (!Has(Igi2PlayerSignal.Grounded))
        {
            Go(Igi2PlayerState.Fall);
        }
        else if (Has(Igi2PlayerSignal.Hit))
        {
            Go(Igi2PlayerState.Hit);
        }
        else
        {
            return false;
        }

        return true;
    }

    /// <summary>Lower-stance toggles into a crouch; holding crouch, or being refused standing, crouches until released.</summary>
    private bool TryCrouch()
    {
        if (Has(Igi2PlayerSignal.LowerStance))
        {
            CrouchIsHeld = false;
            Go(Igi2PlayerState.Crouch);
            return true;
        }

        if ((Has(Igi2PlayerSignal.Crouch) || !Allows(Igi2AllowedStance.Stand)) && Allows(Igi2AllowedStance.Crouch))
        {
            CrouchIsHeld = true;
            Go(Igi2PlayerState.Crouch);
            return true;
        }

        return false;
    }

    /// <summary>The stand/lie toggle lies down when lying is allowed and the body fits on the spot.</summary>
    private bool TryLieDown()
    {
        if (!Has(Igi2PlayerSignal.LieDown) || !Allows(Igi2AllowedStance.Lie))
        {
            return false;
        }

        // The probe runs with the posture set to lying flat and puts it back, as the original does.
        Igi2HumanPose pose = Pose;
        Pose = Igi2HumanPose.LyingFlat;
        bool fits = Surroundings.FitsLyingFlat();
        Pose = pose;

        if (!fits)
        {
            return false;
        }

        Go(Igi2PlayerState.Flat);
        return true;
    }

    /// <summary>A direction held from standing starts a walk or a run, forward first, then back, right, left.</summary>
    private bool TryStartMoving()
    {
        bool walking = Has(Igi2PlayerSignal.Walking);
        if (Has(Igi2PlayerSignal.Forward))
        {
            Go(walking ? Igi2PlayerState.WalkForward : Igi2PlayerState.RunForward);
        }
        else if (Has(Igi2PlayerSignal.Backward))
        {
            Go(walking ? Igi2PlayerState.WalkBackward : Igi2PlayerState.RunBackward);
        }
        else if (Has(Igi2PlayerSignal.Right))
        {
            Go(walking ? Igi2PlayerState.WalkRight : Igi2PlayerState.RunRight);
        }
        else if (Has(Igi2PlayerSignal.Left))
        {
            Go(walking ? Igi2PlayerState.WalkLeft : Igi2PlayerState.RunLeft);
        }
        else
        {
            return false;
        }

        return true;
    }

    /// <summary>Jump on the press, when jumping is allowed.</summary>
    private bool TryJump()
    {
        if (!Channels.WasPressed(Igi2HumanChannel.Jump) || !Allows(Igi2AllowedStance.Jump))
        {
            return false;
        }

        Go(Igi2PlayerState.Jump);
        return true;
    }

    /// <summary>Clip progress as a fraction, for a gait change that keeps its phase.</summary>
    private int PhaseContinuationTime()
    {
        int total = Animation.MainTotalTime;
        double fraction = total != 0 ? Animation.MainTime / (double)total : 0.0;
        return (int)(total * fraction);
    }

    // State 0 -- InitStateStand 0x4A2CA0, RunStateStand 0x4A2D80.
    private void EnterStand()
    {
        Pose = Igi2HumanPose.Standing;
        Animation.PlayState(Igi2PlayerState.Stand, 0, StateBlendSeconds);
        RequestedUpperBody = 1;
        AimPrecision(Igi2PrecisionType.Standing, static p => p.Standing);
        Locomotion.Current = (_, _) => RunStand();
    }

    private void RunStand()
    {
        MoveOnGround(0f, GroundAcceleration, 0f, GroundAcceleration);

        if (TryStandingInterruptions() || TryCrouch() || TryLieDown() || TryStartMoving() || TryJump())
        {
            return;
        }

        if (Has(Igi2PlayerSignal.Turning))
        {
            Go(Igi2PlayerState.StandTurn);
        }
    }

    private bool _turnAnimationDone;

    // State 1 -- InitStateStandTurn 0x4A32B0, RunStateStandTurn 0x4A3370.
    private void EnterStandTurn()
    {
        _turnAnimationDone = false;
        Pose = Igi2HumanPose.Standing;
        Animation.PlayState(Igi2PlayerState.StandTurn, 0, StateBlendSeconds);
        RequestedUpperBody = 1;
        Animation.LoopMain();
        AimPrecision(Igi2PrecisionType.Standing, static p => p.Standing);
        Locomotion.Current = (_, _) => RunStandTurn();
    }

    private void RunStandTurn()
    {
        MoveOnGround(0f, GroundAcceleration, 0f, GroundAcceleration);

        if (TryStandingInterruptions() || TryCrouch() || TryLieDown() || TryStartMoving() || TryJump())
        {
            return;
        }

        // The turn plays out its clip before it can settle: once the clip has ended, letting go of the turn
        // returns to standing.
        if (Has(Igi2PlayerSignal.AnimationEnded))
        {
            _turnAnimationDone = true;
        }

        if (_turnAnimationDone && !Has(Igi2PlayerSignal.Turning))
        {
            Go(Igi2PlayerState.Stand);
        }
    }

    // State 3 -- InitStateRunForward 0x4A38B0, RunStateRunForward 0x4A39F0.
    private void EnterRunForward()
    {
        Pose = Igi2HumanPose.Standing;
        int previous = Locomotion.PreviousState;
        if (previous is Igi2PlayerState.RunForwardRight or Igi2PlayerState.RunForwardLeft
            or Igi2PlayerState.WalkForward or Igi2PlayerState.CrouchWalkForward)
        {
            // From another forward gait the clip carries on at the same point in its cycle.
            int time = PhaseContinuationTime();
            Animation.PlayState(Igi2PlayerState.RunForward, time, StateBlendSeconds);
        }
        else
        {
            Animation.PlayState(
                Igi2PlayerState.RunForward,
                0,
                previous == Igi2PlayerState.Jump ? 0f : StateBlendSeconds);
        }

        RequestedUpperBody = 3;
        AimPrecision(Igi2PrecisionType.Running, static p => p.Running);
        Locomotion.Current = (_, _) => RunRunForward();
    }

    private void RunRunForward()
    {
        ControlGait(RunForwardAnimationTimeScale);
        MoveOnGround(MovementSpeedScale * RunForwardSpeed, GroundAcceleration, 0f, RunForwardSideAcceleration);

        if (Has(Igi2PlayerSignal.Swimming))
        {
            Go(Igi2PlayerState.Swim);
            return;
        }

        if (!Has(Igi2PlayerSignal.Grounded))
        {
            Go(Igi2PlayerState.Fall);
            return;
        }

        if (Has(Igi2PlayerSignal.Dead))
        {
            Go(Igi2PlayerState.Die);
        }
        else if (Has(Igi2PlayerSignal.Hit))
        {
            Go(Igi2PlayerState.Hit);
        }
        else if (Has(Igi2PlayerSignal.Walking))
        {
            Go(Igi2PlayerState.WalkForward);
        }
        else if (TryJump())
        {
        }
        else if (Has(Igi2PlayerSignal.Right))
        {
            Go(Igi2PlayerState.RunForwardRight);
        }
        else if (Has(Igi2PlayerSignal.Left))
        {
            Go(Igi2PlayerState.RunForwardLeft);
        }
        else if (Has(Igi2PlayerSignal.Forward))
        {
            if (!TryCrouch() && !TryLieDown())
            {
                Surroundings.SendWalkEvent();
            }
        }
        else
        {
            Go(Igi2PlayerState.Stand);
        }
    }
}
