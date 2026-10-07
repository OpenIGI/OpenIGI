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

    /// <summary>How long a clip change blends in, seconds, in most states.</summary>
    public const float StateBlendSeconds = 0.2f;

    private TaskStateMachine CreateLocomotion()
    {
        var states = new TaskStateFunction?[Igi2PlayerState.Count];
        states[Igi2PlayerState.Stand] = (_, _) => EnterStand();
        states[Igi2PlayerState.StandTurn] = (_, _) => EnterStandTurn();
        foreach (Gait gait in RunGaits)
        {
            states[gait.State] = (_, _) => EnterGait(gait);
        }

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

    /// <summary>A directional gait: one walk, run or crawl state and the numbers that make it that gait.</summary>
    /// <param name="State">The state.</param>
    /// <param name="ForwardTarget">The target speed along local Y at a movement-speed scale of 1.</param>
    /// <param name="ForwardStep">The most the Y speed changes per tick.</param>
    /// <param name="SideTarget">The target speed along local X at a movement-speed scale of 1.</param>
    /// <param name="SideStep">The most the X speed changes per tick.</param>
    /// <param name="TimeScale">The gait's own animation rate, before the slope.</param>
    /// <param name="WalkingCounterpart">Where the walk bit sends a running gait.</param>
    /// <param name="KeepPhaseFrom">The states a change from keeps the clip's place in its cycle.</param>
    /// <param name="CutAfterJump">Whether landing from a jump cuts straight to the clip instead of blending.</param>
    /// <param name="Direction">The next state for the held directions, or <see cref="Stay"/>.</param>
    private sealed record Gait(
        int State,
        float ForwardTarget,
        float ForwardStep,
        float SideTarget,
        float SideStep,
        float TimeScale,
        int WalkingCounterpart,
        int[] KeepPhaseFrom,
        bool CutAfterJump,
        Func<Igi2HumanPlayer, int> Direction);

    private const int Stay = -1;

    private const float RunForwardSpeed = 890.74341f;
    private const float RunBackwardSpeed = -681.98401f;
    private const float RunSideSpeed = 477.18399f;
    private const float RunBackwardStep = 90.931198f;
    private const float RunSideStep = 63.624531f;

    /// <summary>
    /// The running gaits, states 3 to 10, from their entries (<c>HumanPlayer_InitStateRun*</c>,
    /// <c>igi2 0x4A38B0</c>–<c>0x4A5CC0</c>) and per-ticks (<c>0x4A39F0</c>–<c>0x4A5DE0</c>). The rates are the
    /// <c>HUMANPLAYER_RUN*_ANIMTIMESCALE</c> globals at <c>0x63D764</c>: 1.0, except the two side runs at 1.2.
    /// </summary>
    private static readonly Gait[] RunGaits =
    [
        new(Igi2PlayerState.RunForward, RunForwardSpeed, GroundAcceleration, 0f, RunSideStep, 1f,
            Igi2PlayerState.WalkForward, [Igi2PlayerState.RunForwardRight, Igi2PlayerState.RunForwardLeft, Igi2PlayerState.WalkForward, Igi2PlayerState.CrouchWalkForward], true,
            static p => p.Has(Igi2PlayerSignal.Right) ? Igi2PlayerState.RunForwardRight
                : p.Has(Igi2PlayerSignal.Left) ? Igi2PlayerState.RunForwardLeft
                : p.Has(Igi2PlayerSignal.Forward) ? Stay : Igi2PlayerState.Stand),
        new(Igi2PlayerState.RunForwardRight, RunForwardSpeed, GroundAcceleration, RunSideSpeed, RunSideStep, 1f,
            Igi2PlayerState.WalkForwardRight, [Igi2PlayerState.RunForward, Igi2PlayerState.WalkForwardRight, Igi2PlayerState.CrouchWalkForwardRight], false,
            static p => p.Has(Igi2PlayerSignal.Forward)
                ? (p.Has(Igi2PlayerSignal.Right) ? Stay : Igi2PlayerState.RunForward)
                : Igi2PlayerState.RunRight),
        new(Igi2PlayerState.RunForwardLeft, RunForwardSpeed, GroundAcceleration, -RunSideSpeed, RunSideStep, 1f,
            Igi2PlayerState.WalkForwardLeft, [Igi2PlayerState.RunForward, Igi2PlayerState.WalkForwardLeft, Igi2PlayerState.CrouchWalkForwardLeft], false,
            static p => p.Has(Igi2PlayerSignal.Forward)
                ? (p.Has(Igi2PlayerSignal.Left) ? Stay : Igi2PlayerState.RunForward)
                : Igi2PlayerState.RunLeft),
        new(Igi2PlayerState.RunBackward, RunBackwardSpeed, RunBackwardStep, 0f, RunSideStep, 1f,
            Igi2PlayerState.WalkBackward, [Igi2PlayerState.RunBackwardRight, Igi2PlayerState.RunBackwardLeft, Igi2PlayerState.WalkBackward, Igi2PlayerState.CrouchWalkBackward], true,
            static p => p.Has(Igi2PlayerSignal.Right) ? Igi2PlayerState.RunBackwardRight
                : p.Has(Igi2PlayerSignal.Left) ? Igi2PlayerState.RunBackwardLeft
                : p.Has(Igi2PlayerSignal.Backward) ? Stay : Igi2PlayerState.Stand),
        new(Igi2PlayerState.RunBackwardRight, RunBackwardSpeed, RunBackwardStep, RunSideSpeed, RunSideStep, 1f,
            Igi2PlayerState.WalkBackwardRight, [Igi2PlayerState.RunBackward, Igi2PlayerState.WalkBackwardRight, Igi2PlayerState.CrouchWalkBackwardRight], false,
            static p => p.Has(Igi2PlayerSignal.Backward)
                ? (p.Has(Igi2PlayerSignal.Right) ? Stay : Igi2PlayerState.RunBackward)
                : Igi2PlayerState.RunRight),
        new(Igi2PlayerState.RunBackwardLeft, RunBackwardSpeed, RunBackwardStep, -RunSideSpeed, RunSideStep, 1f,
            Igi2PlayerState.WalkBackwardLeft, [Igi2PlayerState.RunBackward, Igi2PlayerState.WalkBackwardLeft, Igi2PlayerState.CrouchWalkBackwardLeft], false,
            static p => p.Has(Igi2PlayerSignal.Backward)
                ? (p.Has(Igi2PlayerSignal.Left) ? Stay : Igi2PlayerState.RunBackward)
                : Igi2PlayerState.RunLeft),
        new(Igi2PlayerState.RunRight, 0f, GroundAcceleration, RunSideSpeed, RunSideStep, 1.2f,
            Igi2PlayerState.WalkRight, [Igi2PlayerState.WalkRight, Igi2PlayerState.CrouchWalkRight], true,
            static p => !p.Has(Igi2PlayerSignal.Right) ? Igi2PlayerState.Stand
                : p.Has(Igi2PlayerSignal.Forward) ? Igi2PlayerState.RunForwardRight
                : p.Has(Igi2PlayerSignal.Backward) ? Igi2PlayerState.RunBackwardRight : Stay),
        new(Igi2PlayerState.RunLeft, 0f, GroundAcceleration, -RunSideSpeed, RunSideStep, 1.2f,
            Igi2PlayerState.WalkLeft, [Igi2PlayerState.WalkLeft, Igi2PlayerState.CrouchWalkLeft], true,
            static p => !p.Has(Igi2PlayerSignal.Left) ? Igi2PlayerState.Stand
                : p.Has(Igi2PlayerSignal.Forward) ? Igi2PlayerState.RunForwardLeft
                : p.Has(Igi2PlayerSignal.Backward) ? Igi2PlayerState.RunBackwardLeft : Stay),
    ];

    private void EnterGait(Gait gait)
    {
        Pose = Igi2HumanPose.Standing;
        int previous = Locomotion.PreviousState;
        if (Array.IndexOf(gait.KeepPhaseFrom, previous) >= 0)
        {
            // From a neighbouring gait the clip carries on at the same point in its cycle.
            Animation.PlayState(gait.State, PhaseContinuationTime(), StateBlendSeconds);
        }
        else
        {
            Animation.PlayState(gait.State, 0, gait.CutAfterJump && previous == Igi2PlayerState.Jump ? 0f : StateBlendSeconds);
        }

        RequestedUpperBody = 3;
        AimPrecision(Igi2PrecisionType.Running, static p => p.Running);
        Locomotion.Current = (_, _) => RunGait(gait);
    }

    private void RunGait(Gait gait)
    {
        ControlGait(gait.TimeScale);
        MoveOnGround(
            MovementSpeedScale * gait.ForwardTarget,
            gait.ForwardStep,
            MovementSpeedScale * gait.SideTarget,
            gait.SideStep);

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
            return;
        }

        if (Has(Igi2PlayerSignal.Hit))
        {
            Go(Igi2PlayerState.Hit);
            return;
        }

        if (Has(Igi2PlayerSignal.Walking))
        {
            Go(gait.WalkingCounterpart);
            return;
        }

        if (TryJump())
        {
            return;
        }

        int next = gait.Direction(this);
        if (next != Stay)
        {
            Go(next);
        }
        else if (!TryCrouch() && !TryLieDown())
        {
            Surroundings.SendWalkEvent();
        }
    }
}
