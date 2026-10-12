using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Player;

/// <content>The locomotion states and the movement they share.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>The ground acceleration while standing, and toward full speed when running, per tick.</summary>
    /// <remarks>The <c>vMaxYDelta</c> the stand, turn and run states pass <c>HumanPlayer_MoveOnGround</c>.</remarks>
    public const float GroundAcceleration = 118.76579f;

    /// <summary>How long a clip change blends in, seconds, in most states.</summary>
    public const float StateBlendSeconds = 0.2f;

    /// <summary>The water depth at which a crouch with nothing else to do stands up.</summary>
    /// <remarks><c>HumanPlayer_CrouchStateCheckWaterDepth</c> (<c>igi2 0x4A8AD0</c>).</remarks>
    public const float CrouchWaterDepthLimit = 3276.8f;

    private TaskStateMachine CreateLocomotion()
    {
        var states = new TaskStateFunction?[Igi2PlayerState.Count];
        states[Igi2PlayerState.Stand] = (_, _) => EnterStand();
        states[Igi2PlayerState.StandTurn] = (_, _) => EnterStandTurn();
        states[Igi2PlayerState.Crouch] = (_, _) => EnterCrouch();
        states[Igi2PlayerState.CrouchTurn] = (_, _) => EnterCrouchTurn();
        states[Igi2PlayerState.Jump] = (_, _) => EnterJump();
        states[Igi2PlayerState.Fall] = (_, _) => EnterFall();
        states[Igi2PlayerState.Flat] = (_, _) => EnterFlat();
        states[Igi2PlayerState.Hit] = (_, _) => EnterHit();
        states[Igi2PlayerState.Die] = (_, _) => EnterDie();
        states[Igi2PlayerState.DieAlternate] = (_, _) => EnterDie();
        states[Igi2PlayerState.FlatCrawl] = (_, _) => EnterFlatCrawl();
        states[Igi2PlayerState.ActivateDoor] = (_, _) => EnterActivateDoor();
        states[Igi2PlayerState.ActivateTerminal] = (_, _) => EnterActivateTerminal();
        states[Igi2PlayerState.ActivateGenericTba] = (_, _) => EnterActivateGenericTba();
        states[Igi2PlayerState.ClimbLadder] = (_, _) => EnterClimbLadder();
        states[Igi2PlayerState.ClimbLadderGetOnTop] = (_, _) => EnterClimbLadderGetOnTop();
        states[Igi2PlayerState.ClimbLadderGetOffTop] = (_, _) => EnterClimbLadderGetOffTop();
        states[Igi2PlayerState.ClimbLadderSlideDown] = (_, _) => EnterClimbLadderSlideDown();
        foreach (Gait gait in Gaits)
        {
            states[gait.State] = (_, _) => EnterGait(gait);
        }

        var exits = new TaskStateFunction?[Igi2PlayerState.Count];
        exits[Igi2PlayerState.Jump] = (_, _) => ExitJump();
        exits[Igi2PlayerState.FlatCrawl] = (_, _) => ExitFlatCrawl();

        return new TaskStateMachine((_, _) => PackSignal(), states, Igi2PlayerState.Stand, exits: exits);
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
        float localX = (((Fpu)s.Y * o.M21) + ((Fpu)s.X * o.M11) + ((Fpu)o.M31 * s.Z)).F;
        float localY = (((Fpu)s.X * o.M12) + ((Fpu)o.M32 * s.Z) + ((Fpu)s.Y * o.M22)).F;

        Fpu y = Step((Fpu)forwardTarget - localY, maxForwardChange) + localY;
        Fpu x = Step((Fpu)sideTarget - localX, maxSideChange) + localX;

        // Back through the orientation with a zero local vertical, as the original spells it.
        GroundMovementSpeed = new Vector3f(
            ((x * o.M11) + (y * o.M12) + ((Fpu)o.M13 * 0f)).F,
            ((y * o.M22) + (x * o.M21) + ((Fpu)o.M23 * 0f)).F,
            ((x * o.M31) + (y * o.M32) + ((Fpu)o.M33 * 0f)).F);
    }

    /// <summary>A change clamped to at most a step either way, as <c>HumanPlayer_MoveOnGround</c> compares it.</summary>
    private static Fpu Step(Fpu change, float maxChange)
    {
        float least = -maxChange;
        if (least > change.Value)
        {
            return least;
        }

        return maxChange < change.Value ? maxChange : change;
    }

    /// <summary>Selects the posture precision a state aims for (<c>HumanPlayer_SetWeaponPrecision</c>, <c>igi2 0x49F5F0</c>).</summary>
    /// <param name="type">The posture.</param>
    /// <remarks>
    /// Every locomotion entry ends the same way: with a weapon held, the ideal precision is the posture's
    /// own plus <c>|turning speed| × turning modifier × 3</c>; with none it is zero. The posture's own is one of the
    /// weapon's seven, or for a jump the jumping modifier plus the running one, and from a crouch the crouched walk's
    /// as well. The sum is formed unrounded and stored once, as the original's FPU does.
    /// </remarks>
    private void AimPrecision(Igi2PrecisionType type)
    {
        if (Weapon.ActivePrecision is { } precision)
        {
            PrecisionType = type;
            IdealModifiedPrecision = ((Fpu.Abs(TurningSpeed) * precision.TurningModifier * 3f) + Posture(type, precision)).F;
        }
        else
        {
            PrecisionType = Igi2PrecisionType.Standing;
            IdealModifiedPrecision = 0f;
        }
    }

    /// <summary>The posture's own precision, by the switch in <c>HumanPlayer_SetWeaponPrecision</c> (<c>igi2 0x49F624</c>).</summary>
    private static Fpu Posture(Igi2PrecisionType type, Igi2WeaponPrecision p) => type switch
    {
        Igi2PrecisionType.Running => p.Running,
        Igi2PrecisionType.Walking => p.Walking,
        Igi2PrecisionType.Crouching => p.Crouching,
        Igi2PrecisionType.CrouchWalking => p.CrouchWalking,
        Igi2PrecisionType.LyingFlat => p.LyingFlat,
        Igi2PrecisionType.Crawling => p.Crawling,
        Igi2PrecisionType.Jumping => (Fpu)p.JumpingModifier + p.Running,
        Igi2PrecisionType.CrouchJumping => (Fpu)p.JumpingModifier + p.CrouchWalking + p.Running,
        _ => p.Standing,
    };

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
            Go(Igi2PlayerState.TreadWater);
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

    /// <summary>The interruptions the gaits and the crouched states check first, in their order: water, the ground, death, a hit.</summary>
    private bool TryMovingInterruptions()
    {
        if (Has(Igi2PlayerSignal.Swimming))
        {
            Go(Igi2PlayerState.TreadWater);
        }
        else if (!Has(Igi2PlayerSignal.Grounded))
        {
            Go(Igi2PlayerState.Fall);
        }
        else if (Has(Igi2PlayerSignal.Dead))
        {
            Go(Igi2PlayerState.Die);
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

    /// <summary>Lies down on any of the given signals, when lying is allowed and the body fits on the spot.</summary>
    /// <param name="request">The signals that ask for it: the stand/lie toggle standing, either way down crouched.</param>
    private bool TryLieDown(Igi2PlayerSignal request)
    {
        if ((Signal & request) == 0 || !Allows(Igi2AllowedStance.Lie))
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
        Fpu fraction = total != 0 ? (Fpu)(double)Animation.MainTime / (double)total : 0f;
        return Fpu.ToInt32((Fpu)(double)total * fraction);
    }

    // State 0 -- InitStateStand 0x4A2CA0, RunStateStand 0x4A2D80.
    private void EnterStand()
    {
        // Standing straightens any lean: the view's peek offset and speed are zeroed.
        View.PeekOffset = default;
        View.PeekSpeed = default;
        Pose = Igi2HumanPose.Standing;
        Animation.PlayState(Igi2PlayerState.StandAnimation, 0, StateBlendSeconds);
        RequestedUpperBody = 1;
        AimPrecision(Igi2PrecisionType.Standing);
        Locomotion.Current = (_, _) => RunStand();
    }

    private void RunStand()
    {
        MoveOnGround(0f, GroundAcceleration, 0f, GroundAcceleration);

        if (TryStandingInterruptions() || TryCrouch() || TryLieDown(Igi2PlayerSignal.LieDown) || TryStartMoving() || TryJump())
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
        AimPrecision(Igi2PrecisionType.Standing);
        Locomotion.Current = (_, _) => RunStandTurn();
    }

    private void RunStandTurn()
    {
        MoveOnGround(0f, GroundAcceleration, 0f, GroundAcceleration);

        if (TryStandingInterruptions() || TryCrouch() || TryLieDown(Igi2PlayerSignal.LieDown) || TryStartMoving() || TryJump())
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

    /// <summary>The three families of directional gait.</summary>
    private enum GaitFamily
    {
        Run,
        Walk,
        CrouchWalk,
    }

    /// <summary>A directional gait: one walk, run or crouched walk state and the numbers that make it that gait.</summary>
    /// <param name="Family">The family, which decides the posture, the entry and the checks.</param>
    /// <param name="State">The state.</param>
    /// <param name="ForwardTarget">The target speed along local Y at a movement-speed scale of 1.</param>
    /// <param name="ForwardStep">The most the Y speed changes per tick.</param>
    /// <param name="SideTarget">The target speed along local X at a movement-speed scale of 1.</param>
    /// <param name="SideStep">The most the X speed changes per tick.</param>
    /// <param name="TimeScale">The gait's own animation rate before the slope, or <see langword="null"/> when its rate is left alone.</param>
    /// <param name="Counterpart">The same direction at the other pace, which the walk bit switches to; unused crouched.</param>
    /// <param name="KeepPhaseFrom">The states a change from keeps the clip's place in its cycle.</param>
    /// <param name="CutAfterJump">Whether landing from a jump cuts straight to the clip instead of blending.</param>
    /// <param name="Direction">The next state for the held directions, or <see cref="Stay"/>.</param>
    private sealed record Gait(
        GaitFamily Family,
        int State,
        float ForwardTarget,
        float ForwardStep,
        float SideTarget,
        float SideStep,
        float? TimeScale,
        int Counterpart,
        int[] KeepPhaseFrom,
        bool CutAfterJump,
        Func<Igi2HumanPlayer, int> Direction);

    private const int Stay = -1;

    private const float RunForwardSpeed = 890.74341f;
    private const float RunBackwardSpeed = -681.98401f;
    private const float SideSpeed = 477.18399f;
    private const float RunBackwardStep = 90.931198f;
    private const float SideStep = 63.624531f;
    private const float WalkSpeed = 442.36801f;
    private const float WalkStep = 58.982403f;
    private const float CrouchWalkSpeed = 221.86667f;
    private const float CrouchWalkStep = 44.373333f;

    /// <summary>Straight forward or back: a sideways key turns it diagonal, letting go goes to <paramref name="still"/>.</summary>
    private static Func<Igi2HumanPlayer, int> Straight(Igi2PlayerSignal along, int onRight, int onLeft, int still) =>
        p => p.Has(Igi2PlayerSignal.Right) ? onRight
            : p.Has(Igi2PlayerSignal.Left) ? onLeft
            : p.Has(along) ? Stay : still;

    /// <summary>A diagonal, testing <paramref name="outer"/> and then <paramref name="inner"/>: whichever is lost first decides.</summary>
    /// <remarks>
    /// The two keys are not tested in the same order everywhere — the forward crouched diagonals test the
    /// sideways key first — which matters only when both go in the same tick.
    /// </remarks>
    private static Func<Igi2HumanPlayer, int> Diagonal(Igi2PlayerSignal outer, Igi2PlayerSignal inner, int innerLost, int outerLost) =>
        p => p.Has(outer) ? (p.Has(inner) ? Stay : innerLost) : outerLost;

    /// <summary>Sideways: forward or back joining turns it diagonal, letting go goes to <paramref name="still"/>.</summary>
    private static Func<Igi2HumanPlayer, int> Side(Igi2PlayerSignal side, int withForward, int withBackward, int still) =>
        p => !p.Has(side) ? still
            : p.Has(Igi2PlayerSignal.Forward) ? withForward
            : p.Has(Igi2PlayerSignal.Backward) ? withBackward : Stay;

    /// <summary>
    /// The twenty-four directional gaits, from their entries and per-ticks: the runs, states 3–10
    /// (<c>HumanPlayer_InitStateRun*</c> <c>igi2 0x4A38B0</c>–<c>0x4A5CC0</c>, <c>RunStateRun*</c>
    /// <c>0x4A39F0</c>–<c>0x4A5DE0</c>), the walks, states 11–18 (<c>0x4A61F0</c>–<c>0x4A84E0</c>,
    /// <c>0x4A6320</c>–<c>0x4A85F0</c>), and the crouched walks, states 26–33 (<c>0x4A9480</c>–<c>0x4AB590</c>,
    /// <c>0x4A9590</c>–<c>0x4AB6A0</c>). Every number is the bit pattern of the original's constant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The run rates are the <c>HUMANPLAYER_RUN*_ANIMTIMESCALE</c> globals at <c>0x63D764</c> — 1.0, the two
    /// side runs 1.2 — applied through <see cref="Igi2Human.ControlGait"/>. The walks never call it, so a walk plays at
    /// whatever rate its clip was left with.
    /// </para>
    /// <para>
    /// Runs and walks also differ in their entry: a walk asks for upper-body action 2 and aims the walking
    /// precision, a run action 3 and the running one; and only the straight and side runs cut to their clip
    /// on landing from a jump.
    /// </para>
    /// <para>
    /// The crouched walks share their exits with crouching still (see <see cref="TryCrouchedExits"/>), make
    /// no walk noise, and with their keys still held only check the water depth. Their phase-keeping sets
    /// are the original's, lopsided as they are: forward keeps the run's phase, backward the walk's.
    /// </para>
    /// </remarks>
    private static readonly Gait[] Gaits =
    [
        new(GaitFamily.Run, Igi2PlayerState.RunForward, RunForwardSpeed, GroundAcceleration, 0f, SideStep, 1f, Igi2PlayerState.WalkForward,
            [Igi2PlayerState.RunForwardRight, Igi2PlayerState.RunForwardLeft, Igi2PlayerState.WalkForward, Igi2PlayerState.CrouchWalkForward], true,
            Straight(Igi2PlayerSignal.Forward, Igi2PlayerState.RunForwardRight, Igi2PlayerState.RunForwardLeft, Igi2PlayerState.Stand)),
        new(GaitFamily.Run, Igi2PlayerState.RunForwardRight, RunForwardSpeed, GroundAcceleration, SideSpeed, SideStep, 1f, Igi2PlayerState.WalkForwardRight,
            [Igi2PlayerState.RunForward, Igi2PlayerState.WalkForwardRight, Igi2PlayerState.CrouchWalkForwardRight], false,
            Diagonal(Igi2PlayerSignal.Forward, Igi2PlayerSignal.Right, Igi2PlayerState.RunForward, Igi2PlayerState.RunRight)),
        new(GaitFamily.Run, Igi2PlayerState.RunForwardLeft, RunForwardSpeed, GroundAcceleration, -SideSpeed, SideStep, 1f, Igi2PlayerState.WalkForwardLeft,
            [Igi2PlayerState.RunForward, Igi2PlayerState.WalkForwardLeft, Igi2PlayerState.CrouchWalkForwardLeft], false,
            Diagonal(Igi2PlayerSignal.Forward, Igi2PlayerSignal.Left, Igi2PlayerState.RunForward, Igi2PlayerState.RunLeft)),
        new(GaitFamily.Run, Igi2PlayerState.RunBackward, RunBackwardSpeed, RunBackwardStep, 0f, SideStep, 1f, Igi2PlayerState.WalkBackward,
            [Igi2PlayerState.RunBackwardRight, Igi2PlayerState.RunBackwardLeft, Igi2PlayerState.WalkBackward, Igi2PlayerState.CrouchWalkBackward], true,
            Straight(Igi2PlayerSignal.Backward, Igi2PlayerState.RunBackwardRight, Igi2PlayerState.RunBackwardLeft, Igi2PlayerState.Stand)),
        new(GaitFamily.Run, Igi2PlayerState.RunBackwardRight, RunBackwardSpeed, RunBackwardStep, SideSpeed, SideStep, 1f, Igi2PlayerState.WalkBackwardRight,
            [Igi2PlayerState.RunBackward, Igi2PlayerState.WalkBackwardRight, Igi2PlayerState.CrouchWalkBackwardRight], false,
            Diagonal(Igi2PlayerSignal.Backward, Igi2PlayerSignal.Right, Igi2PlayerState.RunBackward, Igi2PlayerState.RunRight)),
        new(GaitFamily.Run, Igi2PlayerState.RunBackwardLeft, RunBackwardSpeed, RunBackwardStep, -SideSpeed, SideStep, 1f, Igi2PlayerState.WalkBackwardLeft,
            [Igi2PlayerState.RunBackward, Igi2PlayerState.WalkBackwardLeft, Igi2PlayerState.CrouchWalkBackwardLeft], false,
            Diagonal(Igi2PlayerSignal.Backward, Igi2PlayerSignal.Left, Igi2PlayerState.RunBackward, Igi2PlayerState.RunLeft)),
        new(GaitFamily.Run, Igi2PlayerState.RunRight, 0f, GroundAcceleration, SideSpeed, SideStep, 1.2f, Igi2PlayerState.WalkRight,
            [Igi2PlayerState.WalkRight, Igi2PlayerState.CrouchWalkRight], true,
            Side(Igi2PlayerSignal.Right, Igi2PlayerState.RunForwardRight, Igi2PlayerState.RunBackwardRight, Igi2PlayerState.Stand)),
        new(GaitFamily.Run, Igi2PlayerState.RunLeft, 0f, GroundAcceleration, -SideSpeed, SideStep, 1.2f, Igi2PlayerState.WalkLeft,
            [Igi2PlayerState.WalkLeft, Igi2PlayerState.CrouchWalkLeft], true,
            Side(Igi2PlayerSignal.Left, Igi2PlayerState.RunForwardLeft, Igi2PlayerState.RunBackwardLeft, Igi2PlayerState.Stand)),

        new(GaitFamily.Walk, Igi2PlayerState.WalkForward, WalkSpeed, WalkStep, 0f, SideStep, null, Igi2PlayerState.RunForward,
            [Igi2PlayerState.RunForward, Igi2PlayerState.WalkForwardRight, Igi2PlayerState.WalkForwardLeft, Igi2PlayerState.CrouchWalkForward], false,
            Straight(Igi2PlayerSignal.Forward, Igi2PlayerState.WalkForwardRight, Igi2PlayerState.WalkForwardLeft, Igi2PlayerState.Stand)),
        new(GaitFamily.Walk, Igi2PlayerState.WalkForwardRight, WalkSpeed, WalkStep, SideSpeed, SideStep, null, Igi2PlayerState.RunForwardRight,
            [Igi2PlayerState.RunForwardRight, Igi2PlayerState.WalkForward, Igi2PlayerState.CrouchWalkForwardRight], false,
            Diagonal(Igi2PlayerSignal.Forward, Igi2PlayerSignal.Right, Igi2PlayerState.WalkForward, Igi2PlayerState.WalkRight)),
        new(GaitFamily.Walk, Igi2PlayerState.WalkForwardLeft, WalkSpeed, WalkStep, -SideSpeed, SideStep, null, Igi2PlayerState.RunForwardLeft,
            [Igi2PlayerState.RunForwardLeft, Igi2PlayerState.WalkForward, Igi2PlayerState.CrouchWalkForwardLeft], false,
            Diagonal(Igi2PlayerSignal.Forward, Igi2PlayerSignal.Left, Igi2PlayerState.WalkForward, Igi2PlayerState.WalkLeft)),
        new(GaitFamily.Walk, Igi2PlayerState.WalkBackward, -WalkSpeed, WalkStep, 0f, SideStep, null, Igi2PlayerState.RunBackward,
            [Igi2PlayerState.RunBackward, Igi2PlayerState.WalkBackwardRight, Igi2PlayerState.WalkBackwardLeft, Igi2PlayerState.CrouchWalkBackward], false,
            Straight(Igi2PlayerSignal.Backward, Igi2PlayerState.WalkBackwardRight, Igi2PlayerState.WalkBackwardLeft, Igi2PlayerState.Stand)),
        new(GaitFamily.Walk, Igi2PlayerState.WalkBackwardRight, -WalkSpeed, WalkStep, SideSpeed, SideStep, null, Igi2PlayerState.RunBackwardRight,
            [Igi2PlayerState.RunBackwardRight, Igi2PlayerState.WalkBackward, Igi2PlayerState.CrouchWalkBackwardRight], false,
            Diagonal(Igi2PlayerSignal.Backward, Igi2PlayerSignal.Right, Igi2PlayerState.WalkBackward, Igi2PlayerState.WalkRight)),
        new(GaitFamily.Walk, Igi2PlayerState.WalkBackwardLeft, -WalkSpeed, WalkStep, -SideSpeed, SideStep, null, Igi2PlayerState.RunBackwardLeft,
            [Igi2PlayerState.RunBackwardLeft, Igi2PlayerState.WalkBackward, Igi2PlayerState.CrouchWalkBackwardLeft], false,
            // Letting go of backward goes to walking RIGHT: HumanPlayer_RunStateWalkBackwardLeft's own quirk.
            Diagonal(Igi2PlayerSignal.Backward, Igi2PlayerSignal.Left, Igi2PlayerState.WalkBackward, Igi2PlayerState.WalkRight)),
        new(GaitFamily.Walk, Igi2PlayerState.WalkRight, 0f, WalkStep, SideSpeed, SideStep, null, Igi2PlayerState.RunRight,
            [Igi2PlayerState.CrouchWalkRight], false,
            Side(Igi2PlayerSignal.Right, Igi2PlayerState.WalkForwardRight, Igi2PlayerState.WalkBackwardRight, Igi2PlayerState.Stand)),
        new(GaitFamily.Walk, Igi2PlayerState.WalkLeft, 0f, WalkStep, -SideSpeed, SideStep, null, Igi2PlayerState.RunLeft,
            [Igi2PlayerState.CrouchWalkLeft], false,
            Side(Igi2PlayerSignal.Left, Igi2PlayerState.WalkForwardLeft, Igi2PlayerState.WalkBackwardLeft, Igi2PlayerState.Stand)),

        new(GaitFamily.CrouchWalk, Igi2PlayerState.CrouchWalkForward, CrouchWalkSpeed, CrouchWalkStep, 0f, CrouchWalkStep, null, Stay,
            [Igi2PlayerState.RunForward], false,
            Straight(Igi2PlayerSignal.Forward, Igi2PlayerState.CrouchWalkForwardRight, Igi2PlayerState.CrouchWalkForwardLeft, Igi2PlayerState.Crouch)),
        new(GaitFamily.CrouchWalk, Igi2PlayerState.CrouchWalkForwardRight, CrouchWalkSpeed, CrouchWalkStep, CrouchWalkSpeed, CrouchWalkStep, null, Stay,
            [Igi2PlayerState.RunForwardRight, Igi2PlayerState.CrouchWalkForward, Igi2PlayerState.CrouchWalkRight], false,
            Diagonal(Igi2PlayerSignal.Right, Igi2PlayerSignal.Forward, Igi2PlayerState.CrouchWalkRight, Igi2PlayerState.CrouchWalkForward)),
        new(GaitFamily.CrouchWalk, Igi2PlayerState.CrouchWalkForwardLeft, CrouchWalkSpeed, CrouchWalkStep, -CrouchWalkSpeed, CrouchWalkStep, null, Stay,
            [Igi2PlayerState.RunForwardLeft, Igi2PlayerState.CrouchWalkForward, Igi2PlayerState.CrouchWalkLeft], false,
            Diagonal(Igi2PlayerSignal.Left, Igi2PlayerSignal.Forward, Igi2PlayerState.CrouchWalkLeft, Igi2PlayerState.CrouchWalkForward)),
        new(GaitFamily.CrouchWalk, Igi2PlayerState.CrouchWalkBackward, -CrouchWalkSpeed, CrouchWalkStep, 0f, CrouchWalkStep, null, Stay,
            [Igi2PlayerState.WalkBackward], false,
            Straight(Igi2PlayerSignal.Backward, Igi2PlayerState.CrouchWalkBackwardRight, Igi2PlayerState.CrouchWalkBackwardLeft, Igi2PlayerState.Crouch)),
        new(GaitFamily.CrouchWalk, Igi2PlayerState.CrouchWalkBackwardRight, -CrouchWalkSpeed, CrouchWalkStep, CrouchWalkSpeed, CrouchWalkStep, null, Stay,
            [Igi2PlayerState.RunBackwardRight, Igi2PlayerState.WalkBackwardRight, Igi2PlayerState.CrouchWalkBackward], false,
            Diagonal(Igi2PlayerSignal.Backward, Igi2PlayerSignal.Right, Igi2PlayerState.CrouchWalkBackward, Igi2PlayerState.CrouchWalkRight)),
        new(GaitFamily.CrouchWalk, Igi2PlayerState.CrouchWalkBackwardLeft, -CrouchWalkSpeed, CrouchWalkStep, -CrouchWalkSpeed, CrouchWalkStep, null, Stay,
            [Igi2PlayerState.RunBackwardLeft, Igi2PlayerState.WalkBackwardLeft, Igi2PlayerState.CrouchWalkBackward], false,
            Diagonal(Igi2PlayerSignal.Backward, Igi2PlayerSignal.Left, Igi2PlayerState.CrouchWalkBackward, Igi2PlayerState.CrouchWalkLeft)),
        new(GaitFamily.CrouchWalk, Igi2PlayerState.CrouchWalkRight, 0f, CrouchWalkStep, CrouchWalkSpeed, CrouchWalkStep, null, Stay,
            [Igi2PlayerState.RunRight], false,
            Side(Igi2PlayerSignal.Right, Igi2PlayerState.CrouchWalkForwardRight, Igi2PlayerState.CrouchWalkBackwardRight, Igi2PlayerState.Crouch)),
        new(GaitFamily.CrouchWalk, Igi2PlayerState.CrouchWalkLeft, 0f, CrouchWalkStep, -CrouchWalkSpeed, CrouchWalkStep, null, Stay,
            [Igi2PlayerState.RunLeft], false,
            Side(Igi2PlayerSignal.Left, Igi2PlayerState.CrouchWalkForwardLeft, Igi2PlayerState.CrouchWalkBackwardLeft, Igi2PlayerState.Crouch)),
    ];

    private void EnterGait(Gait gait)
    {
        Pose = gait.Family == GaitFamily.CrouchWalk ? Igi2HumanPose.Crouching : Igi2HumanPose.Standing;
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

        switch (gait.Family)
        {
            case GaitFamily.Run:
                RequestedUpperBody = 3;
                AimPrecision(Igi2PrecisionType.Running);
                break;
            case GaitFamily.Walk:
                RequestedUpperBody = 2;
                AimPrecision(Igi2PrecisionType.Walking);
                break;
            default:
                RequestedUpperBody = 5;
                AimPrecision(Igi2PrecisionType.CrouchWalking);
                break;
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
            ((Fpu)MovementSpeedScale * gait.ForwardTarget).F,
            gait.ForwardStep,
            ((Fpu)MovementSpeedScale * gait.SideTarget).F,
            gait.SideStep);

        if (gait.Family == GaitFamily.CrouchWalk)
        {
            if (!TryCrouchedExits())
            {
                GoOrCheckWaterDepth(gait.Direction(this));
            }

            return;
        }

        if (TryMovingInterruptions())
        {
            return;
        }

        // A run leaves when the walk bit comes on, a walk when it goes off.
        if (Has(Igi2PlayerSignal.Walking) == (gait.Family == GaitFamily.Run))
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
        else if (!TryCrouch() && !TryLieDown(Igi2PlayerSignal.LieDown))
        {
            Surroundings.SendWalkEvent();
        }
    }

    /// <summary>
    /// The exits every crouched state checks before its keys: the moving interruptions, then standing up —
    /// on letting go of a held crouch or on either way up — then lying down, then a jump.
    /// </summary>
    private bool TryCrouchedExits()
    {
        if (TryMovingInterruptions())
        {
            return true;
        }

        if (Allows(Igi2AllowedStance.Stand)
            && ((CrouchIsHeld && !Has(Igi2PlayerSignal.Crouch)) || Has(Igi2PlayerSignal.StandUp | Igi2PlayerSignal.RaiseStance)))
        {
            Go(Igi2PlayerState.Stand);
            return true;
        }

        return TryLieDown(Igi2PlayerSignal.LieDown | Igi2PlayerSignal.LowerStance) || TryJump();
    }

    /// <summary>Goes to a crouched state's next state; with nothing to do, deep water stands the player up.</summary>
    /// <remarks><c>HumanPlayer_CrouchStateCheckWaterDepth</c> (<c>igi2 0x4A8AD0</c>).</remarks>
    private void GoOrCheckWaterDepth(int next)
    {
        if (next != Stay)
        {
            Go(next);
        }
        else if (WaterDepth >= CrouchWaterDepthLimit)
        {
            Go(Igi2PlayerState.Stand);
        }
    }

    /// <summary>A direction held while crouched starts a crouched walk: right first, then left, forward, back.</summary>
    private int CrouchedStart() =>
        Has(Igi2PlayerSignal.Right) ? Igi2PlayerState.CrouchWalkRight
        : Has(Igi2PlayerSignal.Left) ? Igi2PlayerState.CrouchWalkLeft
        : Has(Igi2PlayerSignal.Forward) ? Igi2PlayerState.CrouchWalkForward
        : Has(Igi2PlayerSignal.Backward) ? Igi2PlayerState.CrouchWalkBackward
        : Stay;

    /// <summary>The entry crouching still and turning crouched share.</summary>
    /// <remarks>Coming up from lying flat takes a zoomed weapon out of its zoomed fire mode first.</remarks>
    private void EnterCrouched(int animation)
    {
        if (Pose == Igi2HumanPose.LyingFlat)
        {
            Weapon.LeaveZoomAlternateFireMode();
        }

        Pose = Igi2HumanPose.Crouching;
        Animation.PlayState(animation, 0, StateBlendSeconds);
        RequestedUpperBody = 4;
        AimPrecision(Igi2PrecisionType.Crouching);
    }

    // State 23 -- InitStateCrouch 0x4A89F0, RunStateCrouch 0x4A8B20.
    private void EnterCrouch()
    {
        EnterCrouched(Igi2PlayerState.CrouchAnimation);
        Locomotion.Current = (_, _) => RunCrouch();
    }

    private void RunCrouch()
    {
        MoveOnGround(0f, GroundAcceleration, 0f, GroundAcceleration);

        if (TryCrouchedExits())
        {
            return;
        }

        int next = CrouchedStart();
        GoOrCheckWaterDepth(next == Stay && Has(Igi2PlayerSignal.Turning) ? Igi2PlayerState.CrouchTurn : next);
    }

    // State 24 -- InitStateCrouchTurn 0x4A8F50, RunStateCrouchTurn 0x4A9030.
    private void EnterCrouchTurn()
    {
        _turnAnimationDone = false;
        EnterCrouched(Igi2PlayerState.CrouchTurn);
        Locomotion.Current = (_, _) => RunCrouchTurn();
    }

    private void RunCrouchTurn()
    {
        MoveOnGround(0f, GroundAcceleration, 0f, GroundAcceleration);

        if (TryCrouchedExits())
        {
            return;
        }

        int next = CrouchedStart();
        if (next != Stay)
        {
            Go(next);
            return;
        }

        // Unlike the standing turn, this one looks at the clip's end before marking it: a turn let go in the
        // tick its clip ends settles one tick later.
        if (!Has(Igi2PlayerSignal.Turning) && _turnAnimationDone)
        {
            Go(Igi2PlayerState.Crouch);
            return;
        }

        if (Has(Igi2PlayerSignal.AnimationEnded))
        {
            _turnAnimationDone = true;
        }

        GoOrCheckWaterDepth(Stay);
    }
}
