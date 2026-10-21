using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Wires: climbing one hand over hand, and riding one down.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>How far a climbing step carries the player along the wire, a tick.</summary>
    public const float WireClimbStep = 221.86666870117188f;

    /// <summary>How far short of a wire's ends the player stays, three quarters of a metre.</summary>
    public const double WireEndMargin = 3072.0;

    /// <summary>The order the wire drag sounds play their two sounds in, <c>HumanPlayer_anClimbWireSoundSequence</c>.</summary>
    public static readonly int[] WireDragOrder = [0, 1];

    /// <summary>Gets a value indicating whether the player is about to climb a wire, <c>isInitClimbWire</c>.</summary>
    public bool IsInitClimbWire { get; private set; }

    /// <summary>Gets a value indicating whether the player is climbing a wire, <c>isClimbWire</c>.</summary>
    public bool IsClimbWire { get; private set; }

    /// <summary>Gets a value indicating whether the player is about to ride a wire down, <c>isInitSlideWire</c>.</summary>
    public bool IsInitSlideWire { get; private set; }

    /// <summary>Gets a value indicating whether the player is riding a wire down, <c>isSlideWire</c>.</summary>
    public bool IsSlideWire { get; private set; }

    /// <summary>Gets the way down the wire being ridden, <c>tSlideWireDirection</c>.</summary>
    public Vector3f SlideWireDirection { get; private set; }

    /// <summary>Gets the speed down the wire being ridden, <c>vSlideWireSpeed</c>.</summary>
    public float SlideWireSpeed { get; private set; }

    /// <summary>Gets the next of the wire drag sounds, <c>nClimbWireSoundSequence</c>.</summary>
    public int ClimbWireSoundSequence { get; private set; }

    /// <summary>
    /// Gets on or off the wire being activated to climb it, <c>HumanPlayer_ClimbWire</c>: run after both machines each tick while the
    /// player is getting on or is on one.
    /// </summary>
    /// <remarks>
    /// Getting on: onto the wire at the player's place along it, three quarters of a metre clear of either end, facing along it away
    /// from its middle, stopped and locked to it with the collision off, for the camera's third mode. On it: a fresh press of activate,
    /// coming within three quarters of a metre of an end, or death lets go, and the player falls.
    /// </remarks>
    private void ClimbWire()
    {
        if (ActivatingTarget is not Igi2WireTask wire)
        {
            IsInitClimbWire = false;
            IsClimbWire = false;
            return;
        }

        if (IsInitClimbWire)
        {
            (_, double y, _) = wire.Local(Position);
            double low = ((Fpu)wire.LocalMagicVertex(0).Y + WireEndMargin).Value;
            double high = ((Fpu)wire.LocalMagicVertex(1).Y - WireEndMargin).Value;
            if (low > y)
            {
                y = low;
            }
            else if (high < y)
            {
                y = high;
            }

            PopPosition(wire.World(new Vector3d(0.0, y, 0.0)));
            GroundMovementSpeed = default;
            Speed = default;
            ImpactSpeed = default;
            SlidingSpeed = default;
            float gamma = Igi2Pose.Angles(wire.Frame).Gamma;
            PopOrientation(y > 0.0 ? ((Fpu)gamma + HalfTurn).F : gamma);
            IsInitClimbWire = false;
            IsClimbWire = true;
            LockOntoWire();
            Go(Igi2PlayerState.ClimbWire);
            return;
        }

        if (WireLetsGo(wire))
        {
            IsClimbWire = false;
            LetGoOfWire(wire);
        }
    }

    /// <summary>
    /// Gets on or off the wire being activated to ride it down, <c>HumanPlayer_SlideWire</c>: run after both machines each tick while
    /// the player is getting on or is on one.
    /// </summary>
    /// <remarks>
    /// Getting on: hanging three quarters of a metre under the wire at the player's place along it, clear of its ends, facing down it,
    /// still, with <c>wire_slide_1</c> playing. On it, as climbing: a fresh press, an end or death lets go.
    /// </remarks>
    private void SlideWire()
    {
        if (ActivatingTarget is not Igi2WireTask wire)
        {
            IsInitSlideWire = false;
            IsSlideWire = false;
            return;
        }

        if (IsInitSlideWire)
        {
            (_, double y, _) = wire.Local(Position);
            Vector3f first = wire.LocalMagicVertex(0);
            double along = ((Fpu)first.Y + WireEndMargin).Value;
            if (along <= y)
            {
                along = ((Fpu)wire.LocalMagicVertex(1).Y - WireEndMargin).Value;
                if (along >= y)
                {
                    along = y;
                }
            }

            Vector3d hang = wire.World(new Vector3d(first.X, along, first.Z));
            PopPosition(new Vector3d(hang.X, hang.Y, ((Fpu)hang.Z - WireEndMargin).Value));
            GroundMovementSpeed = default;
            float gamma = Igi2Pose.Angles(wire.Frame).Gamma;
            Vector3d start = wire.World(new Vector3d(first.X, first.Y, first.Z));
            Vector3f last = wire.LocalMagicVertex(1);
            Vector3d end = wire.World(new Vector3d(last.X, last.Y, last.Z));
            Fpu dx = (Fpu)end.X - start.X, dy = (Fpu)end.Y - start.Y, dz = (Fpu)end.Z - start.Z;
            if (dz.Value > 0.0)
            {
                dx = -dx;
                dy = -dy;
                dz = -dz;
                gamma = ((Fpu)gamma + HalfTurn).F;
            }

            SlideWireSpeed = 0f;
            Fpu scale = (Fpu)1.0 / Fpu.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            SlideWireDirection = new Vector3f((scale * dx).F, (scale * dy).F, (scale * dz).F);
            PopOrientation(gamma);
            IsInitSlideWire = false;
            IsSlideWire = true;
            LockOntoWire();
            _slideSound?.Delete();
            _slideSound = Surroundings.StartSound("wire_slide_1");
            Go(Igi2PlayerState.SlideWire);
            return;
        }

        if (WireLetsGo(wire))
        {
            IsSlideWire = false;
            _timedActionSound?.Delete();
            _timedActionSound = null;
            _slideSound?.Delete();
            _slideSound = null;
            LetGoOfWire(wire);
        }
    }

    /// <summary>What getting onto a wire, either way, locks: the activation, the orientation and the view, the collision and the camera.</summary>
    private void LockOntoWire()
    {
        IsActivating = true;
        IsLockOrientation = true;
        View.IsLockOrientation = true;
        IsDisableCollision = true;
        SnapFromGround();
        View.LookAhead();
        IsDisableDeltaTranslationScale = true;
        CameraMode = 3;
        IsLockAction = true;
    }

    /// <summary>Whether the player lets go: a fresh press of activate, within three quarters of a metre of an end, or dead.</summary>
    private bool WireLetsGo(Igi2WireTask wire)
    {
        (_, double y, _) = wire.Local(Position);
        bool isAtEnd = ((Fpu)wire.LocalMagicVertex(0).Y + WireEndMargin).Value > y
            || ((Fpu)wire.LocalMagicVertex(1).Y - WireEndMargin).Value < y;
        return (Channels[Igi2HumanChannel.Activate] != 0f && Channels.Last[(int)Igi2HumanChannel.Activate] == 0f)
            || isAtEnd
            || Has(Igi2PlayerSignal.Dead);
    }

    /// <summary>
    /// Letting go of a wire: everything locked put back, the wire no longer in use, and the player falling, or dying. The activation's
    /// target is kept, as the original keeps it, so the wire is told it is activated until something else is.
    /// </summary>
    private void LetGoOfWire(Igi2WireTask wire)
    {
        IsActivating = false;
        IsCheckActivateButton = true;
        IsLockOrientation = false;
        View.IsLockOrientation = false;
        IsDisableCollision = false;
        IsDisableDeltaTranslationScale = false;
        CameraMode = 1;
        IsLockAction = false;
        wire.Release();
        Go(Has(Igi2PlayerSignal.Dead) ? Igi2PlayerState.Die : Igi2PlayerState.Fall);
    }

    // State 50 -- HumanPlayer_InitStateClimbWire: standing, the clip begun, the time frame unset.
    private void EnterClimbWire()
    {
        Pose = Igi2HumanPose.Standing;
        BeginLadderState(fromEnd: false);
        Locomotion.Current = (_, _) => RunClimbWire();
    }

    // HumanPlayer_RunStateClimbWire: a step's end stops; forward or back starts one, with a drag sound; each tick of a step carries the
    // player along the wire.
    private void RunClimbWire()
    {
        Igi2PlayerSignal signal = Signal;
        if ((signal & Igi2PlayerSignal.StopAnimation) != 0)
        {
            Signal &= ~Igi2PlayerSignal.StopAnimation;
            SetClimbRate(0.0);
            LadderStepChange = 0;
        }

        if (LadderStepChange == 0)
        {
            if ((signal & Igi2PlayerSignal.Forward) != 0)
            {
                LadderStepChange = 1;
                SetClimbRate(1.0);
            }

            if ((signal & Igi2PlayerSignal.Backward) != 0)
            {
                LadderStepChange = -1;
                SetClimbRate(-1.0);
            }

            if (LadderStepChange != 0)
            {
                int sound = WireDragOrder[ClimbWireSoundSequence];
                ClimbWireSoundSequence = (ClimbWireSoundSequence - 1) & 1;
                Surroundings.StartSound($"wire_drag_{sound + 1}");
            }
        }

        if (LadderStepChange != 0)
        {
            MoveFree(((Fpu)(double)LadderStepChange * WireClimbStep).F, new Vector3f(0f, 1f, 0f), isPop: true);
        }
    }

    // State 51 -- HumanPlayer_InitStateSlideWire: standing, the clip begun, still.
    private void EnterSlideWire()
    {
        Pose = Igi2HumanPose.Standing;
        BeginLadderState(fromEnd: false);
        Speed = default;
        ImpactSpeed = default;
        SlidingSpeed = default;
        Locomotion.Current = (_, _) => RunSlideWire();
    }

    // HumanPlayer_RunStateSlideWire: the speed down the wire eased and pulled by its slope, carrying the player down it, and no
    // landing hurts.
    private void RunSlideWire()
    {
        Vector3f d = SlideWireDirection;
        float speed = (((Fpu)(-d.Z) * 88.30976104736328) + ((Fpu)SlideWireSpeed * 0.9900000095367432)).F;
        SlideWireSpeed = speed;
        IsProtectedFromFallDamage = true;
        Speed = new Vector3f(((Fpu)speed * d.X).F, ((Fpu)speed * d.Y).F, ((Fpu)speed * d.Z).F);
    }
}
