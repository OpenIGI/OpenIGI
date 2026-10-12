using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Climbing ladders: getting on, the steps up and down, getting off at the top, and sliding down.</content>
public sealed partial class Igi2HumanPlayer : IIgi2LadderClimber
{
    /// <summary>The steps from the top the climb ends at, where the player gets off, <c>nSteps - 6</c>.</summary>
    public const int TopStepsLeft = 6;

    /// <summary>How far down from the top a climb found from the top starts, 2.4 m.</summary>
    public const float TopStartDrop = -9830.4f;

    /// <summary>How far back from the ladder its foot leaves the player, half a metre.</summary>
    public const float StepBack = -2048f;

    /// <summary>How far up getting on at the top, or down getting off, moves the player, 0.6 m.</summary>
    public const float TopStep = 2457.6f;

    /// <summary>The order the ladder footsteps play their four sounds in, <c>igi2 0x6B7D08</c>.</summary>
    public static readonly int[] LadderFootstepOrder = [0, 1, 2, 3, 1, 2, 1, 2, 3, 2, 1, 0];

    private static readonly Vector3f Up = new(0f, 0f, 1f);
    private static readonly Vector3f Ahead = new(0f, 1f, 0f);

    private IIgi2SoundInstance? _slideSound;
    private int _ladderFootstep;

    /// <summary>Gets a value indicating whether the player is about to get on a ladder, <c>isInitClimbLadder</c>.</summary>
    public bool IsInitClimbLadder { get; private set; }

    /// <summary>Gets a value indicating whether the player is on a ladder, <c>isClimbLadder</c>.</summary>
    public bool IsClimbLadder { get; private set; }

    /// <summary>Gets a value indicating whether the player is about to get off a ladder, <c>isDeInitClimbLadder</c>.</summary>
    public bool IsDeInitClimbLadder { get; private set; }

    /// <inheritdoc/>
    public bool IsLadderActivateTop { get; set; }

    /// <summary>Gets the ladder's steps, <c>nSteps</c>.</summary>
    public int LadderSteps { get; private set; }

    /// <summary>Gets the step the player is on, <c>nCurrentStep</c>.</summary>
    public int LadderStep { get; private set; }

    /// <summary>Gets the step the player is moving by, <c>nStepChange</c>: one up, minus one down, nought still.</summary>
    public int LadderStepChange { get; private set; }

    /// <summary>Gets the posture the player had before a ladder or a fence, which it takes again after, <c>tStateContext.ePrevStance</c>.</summary>
    public Igi2HumanPose StanceBeforeClimb { get; private set; }

    /// <summary>
    /// Gets on or off the ladder being activated, <c>HumanPlayer_ClimbLadder</c>: run after both machines each tick while the player is
    /// getting on or is on one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Getting on, from the top: at the ladder's top vertex, 2.4 m down, facing it, six steps from the top, through getting on at the
    /// top. From the foot: on the step of the player's height between its foot and top (one up out of water), no lower than the
    /// first nor higher than six from the top, at that step's share of the way up, facing it. Either way the player is stopped,
    /// locked to it with the view looking ahead and the collision off, and stands for the camera's third mode.
    /// </para>
    /// <para>Getting off: everything put back, the standing clip, and a dead player falls.</para>
    /// </remarks>
    private void ClimbLadder()
    {
        if (IsInitClimbLadder)
        {
            if (ActivatingTarget is not Igi2LadderTask ladder)
            {
                IsInitClimbLadder = false;
                return;
            }

            Speed = default;
            ImpactSpeed = default;
            SlidingSpeed = default;
            if (IsLadderActivateTop)
            {
                PopPosition(ladder.MagicVertex(3));
                GroundMovementSpeed = default;
                FaceLadder(ladder);
                LadderStep = ladder.Steps - TopStepsLeft;
                MoveFree(TopStartDrop, Up, isPop: true);
            }
            else
            {
                Vector3d top = ladder.MagicVertex(3);
                Vector3d foot = ladder.MagicVertex(2);
                double z = foot.Z <= Position.Z ? (top.Z >= Position.Z ? Position.Z : top.Z) : foot.Z;
                int step = Fpu.ToInt32(((Fpu)z - foot.Z) * 0.0008138020509957453);
                if (IsSwimming)
                {
                    step++;
                }

                step = step < 0 ? 0 : step > ladder.Steps - TopStepsLeft ? ladder.Steps - TopStepsLeft : step;
                Fpu share = (Fpu)(double)step / ladder.Steps;
                PopPosition(new Vector3d(
                    (((Fpu)top.X - foot.X) * share + foot.X).Value,
                    (((Fpu)top.Y - foot.Y) * share + foot.Y).Value,
                    (((Fpu)top.Z - foot.Z) * share + foot.Z).Value));
                GroundMovementSpeed = default;
                FaceLadder(ladder);
                LadderStep = step;
            }

            Speed = default;
            ImpactSpeed = default;
            SlidingSpeed = default;
            IsInitClimbLadder = false;
            IsClimbLadder = true;
            IsActivating = true;
            IsLockOrientation = true;
            View.LookAhead();
            View.IsLockOrientation = true;
            IsDisableCollision = true;
            SnapFromGround();
            IsDisableDeltaTranslationScale = true;
            CameraMode = 3;
            IsLockAction = true;
            LadderSteps = ladder.Steps;
            Go(IsLadderActivateTop ? Igi2PlayerState.ClimbLadderGetOnTop : Igi2PlayerState.ClimbLadder);
            return;
        }

        if (!IsDeInitClimbLadder)
        {
            return;
        }

        IsDeInitClimbLadder = false;
        IsClimbLadder = false;
        EndActivate();
        IsLockOrientation = false;
        View.IsLockOrientation = false;
        IsDisableCollision = false;
        IsDisableDeltaTranslationScale = false;
        CameraMode = 1;
        IsLockAction = false;
        Animation.PlayState(Igi2PlayerState.Stand, 0, 0f);
        if (Has(Igi2PlayerSignal.Dead))
        {
            Go(Igi2PlayerState.Die);
        }
    }

    /// <summary>Faces the ladder: its heading turned half round, <c>Real32x9_GetAngles</c> and <c>Human_PopOrientation</c>.</summary>
    private void FaceLadder(Igi2LadderTask ladder)
    {
        IReadOnlyList<float> m = ladder.Frame;
        PopOrientation(((Fpu)Igi2Pose.Gamma(m[0], m[3]) + HalfTurn).F);
    }

    /// <summary>
    /// Lets go of the ground, <c>Human_SnapFromGround</c>: not grounded, and nothing underfoot.
    /// </summary>
    private void SnapFromGround()
    {
        Signal &= ~Igi2PlayerSignal.Grounded;
        DropGround();
    }

    /// <summary>
    /// A move along the body's own axes, <c>Human_MoveFree</c>: popped there at once, or taken as this tick's animation step.
    /// </summary>
    /// <param name="speed">How far.</param>
    /// <param name="unit">Which way, in the body's frame.</param>
    /// <param name="isPop">Whether the body is popped there rather than stepped.</param>
    private void MoveFree(float speed, Vector3f unit, bool isPop)
    {
        float lx = ((Fpu)unit.X * speed).F, ly = ((Fpu)unit.Y * speed).F, lz = ((Fpu)unit.Z * speed).F;
        Matrix3f o = Orientation;
        float x = ((((Fpu)lz * o.M13) + ((Fpu)ly * o.M12)) + ((Fpu)lx * o.M11)).F;
        float y = ((((Fpu)lz * o.M23) + ((Fpu)ly * o.M22)) + ((Fpu)lx * o.M21)).F;
        float z = ((((Fpu)lz * o.M33) + ((Fpu)ly * o.M32)) + ((Fpu)lx * o.M31)).F;
        if (isPop)
        {
            PopPosition(new Vector3d(
                ((Fpu)x + Position.X).Value,
                ((Fpu)y + Position.Y).Value,
                ((Fpu)z + Position.Z).Value));
        }
        else
        {
            DeltaTranslation = new Vector3f(x, y, z);
        }
    }

    /// <summary>
    /// A climbing step's move, <c>HumanPlayer_Climb</c>: 0.3 m a step over the clip's length in ticks, up or down, at a pace of
    /// steps.
    /// </summary>
    private void Climb(bool isDown, float steps)
    {
        int total = Animation.MainTotalTime;
        Fpu speed = (Fpu)(isDown ? -0.30000001192092896 : 0.30000001192092896) * steps * 4096.0 / (double)(30 * total / 4800);
        MoveFree(speed.F, Up, isPop: false);
    }

    /// <summary>A footstep on a ladder, <c>Human_MakeLadderFootstepSound</c>: the next of the order, unless walking.</summary>
    private void LadderFootstep()
    {
        int sound = LadderFootstepOrder[_ladderFootstep];
        _ladderFootstep = (_ladderFootstep + 1) % LadderFootstepOrder.Length;
        if (!Has(Igi2PlayerSignal.Walking))
        {
            Surroundings.PlaySoundAt($"walk_ladder_{sound + 1}", Position);
        }
    }

    /// <summary>The ladder states' clip and posture, as each of their inits sets them.</summary>
    private void BeginLadderState(bool fromEnd)
    {
        if (fromEnd)
        {
            Animation.PlayStateAtEnd(Locomotion.CurrentState, 0f);
        }
        else
        {
            Animation.PlayState(Locomotion.CurrentState, 0, 0f);
        }

        CurrentUpperBody = 0;
        RequestedUpperBody = 0;
        Animation.ClearUpperBody(0.4f);
        AimPrecision(Igi2PrecisionType.Standing);
    }

    private void SetClimbRate(double rate) =>
        Animation.MainTimeStep = Fpu.ToInt32((Fpu)(double)Igi2AnimController.TicksPerFrame * rate);

    /// <summary>The posture before the ladder's state: crouched, lying, or standing.</summary>
    private void GoBackToStance()
    {
        Go(StanceBeforeClimb switch
        {
            Igi2HumanPose.Crouching => Igi2PlayerState.Crouch,
            Igi2HumanPose.LyingFlat => Igi2PlayerState.Flat,
            _ => Igi2PlayerState.Stand,
        });
    }

    // State 45 -- HumanPlayer_InitStateClimbLadder: the clip held still, the vertical peek dropped, the posture kept for later and
    // standing, out of the water.
    private void EnterClimbLadder()
    {
        View.PeekOffset = new Vector3f(View.PeekOffset.X, View.PeekOffset.Y, 0f);
        View.PeekSpeed = new Vector3f(View.PeekSpeed.X, View.PeekSpeed.Y, 0f);
        StanceBeforeClimb = Pose;
        IsSwimming = false;
        Pose = Igi2HumanPose.Standing;
        BeginLadderState(fromEnd: false);
        LadderStepChange = 0;
        SetClimbRate(0.0);
        Locomotion.Current = (_, _) => RunClimbLadder();
    }

    // HumanPlayer_RunStateClimbLadder: a step's end counts the step and sounds; climbing that is not walking is heard; up from six
    // from the top gets off; down from the foot steps back and stands; the activate key, pressed afresh, slides down.
    private void RunClimbLadder()
    {
        Igi2PlayerSignal signal = Signal;
        MoveOnGround(0f, GroundAcceleration, 0f, GroundAcceleration);
        if ((signal & Igi2PlayerSignal.StopAnimation) != 0)
        {
            Signal &= ~Igi2PlayerSignal.StopAnimation;
            SetClimbRate(0.0);
            LadderStep += LadderStepChange;
            LadderStepChange = 0;
            LadderFootstep();
        }
        else if ((signal & (Igi2PlayerSignal.Forward | Igi2PlayerSignal.Backward)) != 0 && (signal & Igi2PlayerSignal.Walking) == 0)
        {
            Surroundings.SendAiEvent(new Ai.Igi2AiEvent(Ai.Igi2AiEvent.Ladder, Team, 1090, 81920f, Position, default, this, this));
        }

        if (LadderStepChange == 0)
        {
            if ((signal & Igi2PlayerSignal.Forward) != 0)
            {
                if (LadderStep == LadderSteps - TopStepsLeft)
                {
                    Go(Igi2PlayerState.ClimbLadderGetOffTop);
                    return;
                }

                LadderStepChange = 1;
            }
            else if ((signal & Igi2PlayerSignal.Backward) != 0)
            {
                if (LadderStep == 0)
                {
                    MoveFree(StepBack, Ahead, isPop: true);
                    IsDeInitClimbLadder = true;
                    DropGround();
                    GoBackToStance();
                    return;
                }

                LadderStepChange = -1;
            }
        }

        if (LadderStepChange != 0)
        {
            bool isWalking = (signal & Igi2PlayerSignal.Walking) != 0;
            bool isDown = LadderStepChange <= 0;
            SetClimbRate((isDown ? -1.0 : 1.0) * (isWalking ? 1.0 : 1.75));
            Climb(isDown, isWalking ? 2f : 3.5f);
        }

        if (Channels[Igi2HumanChannel.Activate] == 0f || Channels.Last[(int)Igi2HumanChannel.Activate] != 0f)
        {
            if ((signal & Igi2PlayerSignal.Dead) != 0)
            {
                IsDeInitClimbLadder = true;
            }

            if ((signal & Igi2PlayerSignal.Swimming) != 0)
            {
                IsDeInitClimbLadder = true;
                MoveFree(StepBack, Ahead, isPop: true);
                DropGround();
                Surroundings.StartSound("water_land_02");
                Go(Igi2PlayerState.TreadWater);
            }
        }
        else
        {
            Go(Igi2PlayerState.ClimbLadderSlideDown);
        }
    }

    // State 46 -- HumanPlayer_InitStateClimbLadderGetOnTop: the clip played back from its end.
    private void EnterClimbLadderGetOnTop()
    {
        BeginLadderState(fromEnd: true);
        SetClimbRate(-1.0);
        Locomotion.Current = (_, _) => RunClimbLadderGetOnTop();
    }

    // HumanPlayer_RunStateClimbLadderGetOnTop: at its start, up 0.6 m and on the ladder.
    private void RunClimbLadderGetOnTop()
    {
        Igi2PlayerSignal signal = Signal;
        SetClimbRate((signal & Igi2PlayerSignal.Walking) != 0 ? -1.0 : -2.0);
        if ((signal & Igi2PlayerSignal.StopAnimation) != 0)
        {
            Signal &= ~Igi2PlayerSignal.StopAnimation;
            LadderFootstep();
        }

        if ((signal & Igi2PlayerSignal.AnimationEnded) != 0)
        {
            MoveFree(TopStep, Up, isPop: false);
            Go(Igi2PlayerState.ClimbLadder);
        }
        else if ((signal & Igi2PlayerSignal.Dead) != 0)
        {
            IsDeInitClimbLadder = true;
        }
    }

    // State 47 -- HumanPlayer_InitStateClimbLadderGetOffTop: down 0.6 m into the clip.
    private void EnterClimbLadderGetOffTop()
    {
        BeginLadderState(fromEnd: false);
        MoveFree(-TopStep, Up, isPop: false);
        Locomotion.Current = (_, _) => RunClimbLadderGetOffTop();
    }

    // HumanPlayer_RunStateClimbLadderGetOffTop: at its end, 1.5 m ahead and two up, off the ladder in the old posture.
    private void RunClimbLadderGetOffTop()
    {
        Igi2PlayerSignal signal = Signal;
        SetClimbRate((signal & Igi2PlayerSignal.Walking) != 0 ? 1.0 : 2.0);
        if ((signal & Igi2PlayerSignal.StopAnimation) != 0)
        {
            Signal &= ~Igi2PlayerSignal.StopAnimation;
            LadderFootstep();
        }

        if ((signal & Igi2PlayerSignal.AnimationEnded) != 0)
        {
            IsDeInitClimbLadder = true;
            Matrix3f o = Orientation;
            PopPosition(new Vector3d(
                (((((Fpu)o.M13 * 8192.0) + ((Fpu)o.M12 * 6144.0)) + ((Fpu)o.M11 * 0.0)) + Position.X).Value,
                (((((Fpu)o.M23 * 8192.0) + ((Fpu)o.M22 * 6144.0)) + ((Fpu)o.M21 * 0.0)) + Position.Y).Value,
                (((((Fpu)o.M33 * 8192.0) + ((Fpu)o.M32 * 6144.0)) + ((Fpu)o.M31 * 0.0)) + Position.Z).Value));
            GroundMovementSpeed = default;
            GoBackToStance();
        }
        else if ((signal & Igi2PlayerSignal.Dead) != 0)
        {
            IsDeInitClimbLadder = true;
        }
    }

    // State 48 -- HumanPlayer_InitStateClimbLadderSlideDown: standing, falling free with the collision off, to wire_slide_1.
    private void EnterClimbLadderSlideDown()
    {
        Pose = Igi2HumanPose.Standing;
        BeginLadderState(fromEnd: false);
        IsDisableCollision = true;
        _slideSound?.Delete();
        _slideSound = Surroundings.StartSound("wire_slide_1");
        Locomotion.Current = (_, _) => RunClimbLadderSlideDown();
    }

    // HumanPlayer_RunStateClimbLadderSlideDown: the speed eased and pulled down until within a metre of the foot and about to land,
    // or in water; then half a metre back, stopped, the sounds gone, off the ladder.
    private void RunClimbLadderSlideDown()
    {
        Vector3d foot = (ActivatingTarget as Igi2LadderTask)?.MagicVertex(0) ?? Position;
        Vector3f s = Speed;
        float fall = (((Fpu)s.Z - 44.600887298583984) * 0.9900000095367432).F;
        Speed = new Vector3f(((Fpu)s.X * 0.9900000095367432).F, ((Fpu)s.Y * 0.9900000095367432).F, fall);
        Igi2PlayerSignal signal = Signal;
        bool isLanding = DistanceToGround > -4096.0 && -fall > DistanceToGround;
        bool isDown = false;
        if (((Fpu)Position.Z - foot.Z).Value < 4096.0)
        {
            IsDisableCollision = false;
            isDown = isLanding;
        }

        if (!isDown && (signal & Igi2PlayerSignal.Swimming) == 0)
        {
            return;
        }

        MoveFree(StepBack, Ahead, isPop: true);
        DropGround();
        Speed = default;
        ImpactSpeed = default;
        _timedActionSound?.Delete();
        _timedActionSound = null;
        _slideSound?.Delete();
        _slideSound = null;
        IsDisableCollision = false;
        IsDeInitClimbLadder = true;
        if ((signal & Igi2PlayerSignal.Swimming) != 0)
        {
            Surroundings.StartSound("water_land_02");
            Go(Igi2PlayerState.TreadWater);
        }
        else
        {
            GoBackToStance();
        }
    }
}
