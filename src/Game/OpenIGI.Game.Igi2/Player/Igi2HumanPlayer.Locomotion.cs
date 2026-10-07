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
        foreach (Gait gait in Gaits)
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

    /// <summary>A directional gait: one walk or run state and the numbers that make it that gait.</summary>
    /// <param name="State">The state.</param>
    /// <param name="ForwardTarget">The target speed along local Y at a movement-speed scale of 1.</param>
    /// <param name="ForwardStep">The most the Y speed changes per tick.</param>
    /// <param name="SideTarget">The target speed along local X at a movement-speed scale of 1.</param>
    /// <param name="SideStep">The most the X speed changes per tick.</param>
    /// <param name="TimeScale">The gait's own animation rate before the slope, or <see langword="null"/> when its rate is left alone.</param>
    /// <param name="Counterpart">The same direction at the other pace, which the walk bit switches to.</param>
    /// <param name="KeepPhaseFrom">The states a change from keeps the clip's place in its cycle.</param>
    /// <param name="CutAfterJump">Whether landing from a jump cuts straight to the clip instead of blending.</param>
    /// <param name="Direction">The next state for the held directions, or <see cref="Stay"/>.</param>
    private sealed record Gait(
        int State,
        float ForwardTarget,
        float ForwardStep,
        float SideTarget,
        float SideStep,
        float? TimeScale,
        int Counterpart,
        int[] KeepPhaseFrom,
        bool CutAfterJump,
        Func<Igi2HumanPlayer, int> Direction)
    {
        /// <summary>Gets a value indicating whether this is a running gait, which the walk bit leaves.</summary>
        public bool IsRun => TimeScale is not null;
    }

    private const int Stay = -1;

    private const float RunForwardSpeed = 890.74341f;
    private const float RunBackwardSpeed = -681.98401f;
    private const float SideSpeed = 477.18399f;
    private const float RunBackwardStep = 90.931198f;
    private const float SideStep = 63.624531f;
    private const float WalkSpeed = 442.36801f;
    private const float WalkStep = 58.982403f;

    /// <summary>Straight forward or back: a sideways key turns it diagonal, letting go stops.</summary>
    private static Func<Igi2HumanPlayer, int> Straight(Igi2PlayerSignal along, int onRight, int onLeft) =>
        p => p.Has(Igi2PlayerSignal.Right) ? onRight
            : p.Has(Igi2PlayerSignal.Left) ? onLeft
            : p.Has(along) ? Stay : Igi2PlayerState.Stand;

    /// <summary>A diagonal: losing the side key goes straight, losing the main key goes sideways.</summary>
    private static Func<Igi2HumanPlayer, int> Diagonal(Igi2PlayerSignal along, Igi2PlayerSignal side, int straight, int sideways) =>
        p => p.Has(along) ? (p.Has(side) ? Stay : straight) : sideways;

    /// <summary>Sideways: forward or back joining turns it diagonal, letting go stops.</summary>
    private static Func<Igi2HumanPlayer, int> Side(Igi2PlayerSignal side, int withForward, int withBackward) =>
        p => !p.Has(side) ? Igi2PlayerState.Stand
            : p.Has(Igi2PlayerSignal.Forward) ? withForward
            : p.Has(Igi2PlayerSignal.Backward) ? withBackward : Stay;

    /// <summary>
    /// The sixteen standing gaits, from their entries and per-ticks: the runs, states 3–10
    /// (<c>HumanPlayer_InitStateRun*</c> <c>igi2 0x4A38B0</c>–<c>0x4A5CC0</c>, <c>RunStateRun*</c>
    /// <c>0x4A39F0</c>–<c>0x4A5DE0</c>), and the walks, states 11–18 (<c>0x4A61F0</c>–<c>0x4A84E0</c>,
    /// <c>0x4A6320</c>–<c>0x4A85F0</c>). Every number is the bit pattern of the original's constant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The run rates are the <c>HUMANPLAYER_RUN*_ANIMTIMESCALE</c> globals at <c>0x63D764</c> — 1.0, the two
    /// side runs 1.2 — applied through <see cref="ControlGait"/>. The walks never call it, so a walk plays at
    /// whatever rate its clip was left with.
    /// </para>
    /// <para>
    /// Runs and walks also differ in their entry: a walk asks for upper-body action 2 and aims the walking
    /// precision, a run action 3 and the running one; and only the straight and side runs cut to their clip
    /// on landing from a jump.
    /// </para>
    /// </remarks>
    private static readonly Gait[] Gaits =
    [
        new(Igi2PlayerState.RunForward, RunForwardSpeed, GroundAcceleration, 0f, SideStep, 1f, Igi2PlayerState.WalkForward,
            [Igi2PlayerState.RunForwardRight, Igi2PlayerState.RunForwardLeft, Igi2PlayerState.WalkForward, Igi2PlayerState.CrouchWalkForward], true,
            Straight(Igi2PlayerSignal.Forward, Igi2PlayerState.RunForwardRight, Igi2PlayerState.RunForwardLeft)),
        new(Igi2PlayerState.RunForwardRight, RunForwardSpeed, GroundAcceleration, SideSpeed, SideStep, 1f, Igi2PlayerState.WalkForwardRight,
            [Igi2PlayerState.RunForward, Igi2PlayerState.WalkForwardRight, Igi2PlayerState.CrouchWalkForwardRight], false,
            Diagonal(Igi2PlayerSignal.Forward, Igi2PlayerSignal.Right, Igi2PlayerState.RunForward, Igi2PlayerState.RunRight)),
        new(Igi2PlayerState.RunForwardLeft, RunForwardSpeed, GroundAcceleration, -SideSpeed, SideStep, 1f, Igi2PlayerState.WalkForwardLeft,
            [Igi2PlayerState.RunForward, Igi2PlayerState.WalkForwardLeft, Igi2PlayerState.CrouchWalkForwardLeft], false,
            Diagonal(Igi2PlayerSignal.Forward, Igi2PlayerSignal.Left, Igi2PlayerState.RunForward, Igi2PlayerState.RunLeft)),
        new(Igi2PlayerState.RunBackward, RunBackwardSpeed, RunBackwardStep, 0f, SideStep, 1f, Igi2PlayerState.WalkBackward,
            [Igi2PlayerState.RunBackwardRight, Igi2PlayerState.RunBackwardLeft, Igi2PlayerState.WalkBackward, Igi2PlayerState.CrouchWalkBackward], true,
            Straight(Igi2PlayerSignal.Backward, Igi2PlayerState.RunBackwardRight, Igi2PlayerState.RunBackwardLeft)),
        new(Igi2PlayerState.RunBackwardRight, RunBackwardSpeed, RunBackwardStep, SideSpeed, SideStep, 1f, Igi2PlayerState.WalkBackwardRight,
            [Igi2PlayerState.RunBackward, Igi2PlayerState.WalkBackwardRight, Igi2PlayerState.CrouchWalkBackwardRight], false,
            Diagonal(Igi2PlayerSignal.Backward, Igi2PlayerSignal.Right, Igi2PlayerState.RunBackward, Igi2PlayerState.RunRight)),
        new(Igi2PlayerState.RunBackwardLeft, RunBackwardSpeed, RunBackwardStep, -SideSpeed, SideStep, 1f, Igi2PlayerState.WalkBackwardLeft,
            [Igi2PlayerState.RunBackward, Igi2PlayerState.WalkBackwardLeft, Igi2PlayerState.CrouchWalkBackwardLeft], false,
            Diagonal(Igi2PlayerSignal.Backward, Igi2PlayerSignal.Left, Igi2PlayerState.RunBackward, Igi2PlayerState.RunLeft)),
        new(Igi2PlayerState.RunRight, 0f, GroundAcceleration, SideSpeed, SideStep, 1.2f, Igi2PlayerState.WalkRight,
            [Igi2PlayerState.WalkRight, Igi2PlayerState.CrouchWalkRight], true,
            Side(Igi2PlayerSignal.Right, Igi2PlayerState.RunForwardRight, Igi2PlayerState.RunBackwardRight)),
        new(Igi2PlayerState.RunLeft, 0f, GroundAcceleration, -SideSpeed, SideStep, 1.2f, Igi2PlayerState.WalkLeft,
            [Igi2PlayerState.WalkLeft, Igi2PlayerState.CrouchWalkLeft], true,
            Side(Igi2PlayerSignal.Left, Igi2PlayerState.RunForwardLeft, Igi2PlayerState.RunBackwardLeft)),

        new(Igi2PlayerState.WalkForward, WalkSpeed, WalkStep, 0f, SideStep, null, Igi2PlayerState.RunForward,
            [Igi2PlayerState.RunForward, Igi2PlayerState.WalkForwardRight, Igi2PlayerState.WalkForwardLeft, Igi2PlayerState.CrouchWalkForward], false,
            Straight(Igi2PlayerSignal.Forward, Igi2PlayerState.WalkForwardRight, Igi2PlayerState.WalkForwardLeft)),
        new(Igi2PlayerState.WalkForwardRight, WalkSpeed, WalkStep, SideSpeed, SideStep, null, Igi2PlayerState.RunForwardRight,
            [Igi2PlayerState.RunForwardRight, Igi2PlayerState.WalkForward, Igi2PlayerState.CrouchWalkForwardRight], false,
            Diagonal(Igi2PlayerSignal.Forward, Igi2PlayerSignal.Right, Igi2PlayerState.WalkForward, Igi2PlayerState.WalkRight)),
        new(Igi2PlayerState.WalkForwardLeft, WalkSpeed, WalkStep, -SideSpeed, SideStep, null, Igi2PlayerState.RunForwardLeft,
            [Igi2PlayerState.RunForwardLeft, Igi2PlayerState.WalkForward, Igi2PlayerState.CrouchWalkForwardLeft], false,
            Diagonal(Igi2PlayerSignal.Forward, Igi2PlayerSignal.Left, Igi2PlayerState.WalkForward, Igi2PlayerState.WalkLeft)),
        new(Igi2PlayerState.WalkBackward, -WalkSpeed, WalkStep, 0f, SideStep, null, Igi2PlayerState.RunBackward,
            [Igi2PlayerState.RunBackward, Igi2PlayerState.WalkBackwardRight, Igi2PlayerState.WalkBackwardLeft, Igi2PlayerState.CrouchWalkBackward], false,
            Straight(Igi2PlayerSignal.Backward, Igi2PlayerState.WalkBackwardRight, Igi2PlayerState.WalkBackwardLeft)),
        new(Igi2PlayerState.WalkBackwardRight, -WalkSpeed, WalkStep, SideSpeed, SideStep, null, Igi2PlayerState.RunBackwardRight,
            [Igi2PlayerState.RunBackwardRight, Igi2PlayerState.WalkBackward, Igi2PlayerState.CrouchWalkBackwardRight], false,
            Diagonal(Igi2PlayerSignal.Backward, Igi2PlayerSignal.Right, Igi2PlayerState.WalkBackward, Igi2PlayerState.WalkRight)),
        new(Igi2PlayerState.WalkBackwardLeft, -WalkSpeed, WalkStep, -SideSpeed, SideStep, null, Igi2PlayerState.RunBackwardLeft,
            [Igi2PlayerState.RunBackwardLeft, Igi2PlayerState.WalkBackward, Igi2PlayerState.CrouchWalkBackwardLeft], false,
            Diagonal(Igi2PlayerSignal.Backward, Igi2PlayerSignal.Left, Igi2PlayerState.WalkBackward, Igi2PlayerState.WalkLeft)),
        new(Igi2PlayerState.WalkRight, 0f, WalkStep, SideSpeed, SideStep, null, Igi2PlayerState.RunRight,
            [Igi2PlayerState.CrouchWalkRight], false,
            Side(Igi2PlayerSignal.Right, Igi2PlayerState.WalkForwardRight, Igi2PlayerState.WalkBackwardRight)),
        new(Igi2PlayerState.WalkLeft, 0f, WalkStep, -SideSpeed, SideStep, null, Igi2PlayerState.RunLeft,
            [Igi2PlayerState.CrouchWalkLeft], false,
            Side(Igi2PlayerSignal.Left, Igi2PlayerState.WalkForwardLeft, Igi2PlayerState.WalkBackwardLeft)),
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

        if (gait.IsRun)
        {
            RequestedUpperBody = 3;
            AimPrecision(Igi2PrecisionType.Running, static p => p.Running);
        }
        else
        {
            RequestedUpperBody = 2;
            AimPrecision(Igi2PrecisionType.Walking, static p => p.Walking);
        }

        Locomotion.Current = (_, _) => RunGait(gait);
    }

    private void RunGait(Gait gait)
    {
        if (gait.TimeScale is float timeScale)
        {
            ControlGait(timeScale);
        }

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

        // A run leaves when the walk bit comes on, a walk when it goes off.
        if (Has(Igi2PlayerSignal.Walking) == gait.IsRun)
        {
            Go(gait.Counterpart);
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
