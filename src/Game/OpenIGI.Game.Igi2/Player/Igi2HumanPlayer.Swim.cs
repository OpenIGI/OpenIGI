using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Player;

/// <content>
/// Swimming, states 19 to 21 (<c>humanplayer.c HumanPlayer_InitStateSwim</c>, <c>HumanPlayer_RunStateSwim</c>,
/// <c>HumanPlayer_RunStateTreadWater</c>): forward, backward and treading water.
/// </content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>The stroke forward, in world units a tick.</summary>
    public const float SwimForwardSpeed = 163.8400115966797f;

    /// <summary>The stroke backward, in world units a tick.</summary>
    public const float SwimBackwardSpeed = 81.92000579833984f;

    /// <summary>How far a swimmer's depth may drift from <see cref="SwimDepth"/> before the body is put back, in world units.</summary>
    public const float SwimDepthTolerance = 4.096f;

    /// <summary>How far the eye is lifted while swimming, the view's vertical peek: half a metre.</summary>
    public const float SwimViewLift = 2048f;

    // Human_CreateSwimStrokeSound's twelve strokes, swim_01 to swim_04 in its own order.
    private static readonly int[] SwimStrokes = [1, 2, 3, 4, 2, 3, 1, 4, 2, 1, 4, 3];

    private int _swimStroke;

    /// <summary>
    /// <c>HumanPlayer_InitStateSwim</c> (<c>igi2 0x4AB680</c>): the vertical peek becomes the lift that keeps the eye above the
    /// water; an automatic weapon lets go and its sounds stop; the zoom and its overlay come down; the clip blends in over a fifth
    /// of a second; coming from dry land the action machine locks and the weapon goes down; the body lets go of the ground and its
    /// speed; the action machine stays locked while swimming. Treading water stirs it; a stroke sounds once it starts.
    /// </summary>
    private void EnterSwim()
    {
        View.PeekSpeed = new Vector3f(View.PeekSpeed.X, View.PeekSpeed.Y, 0f);
        View.PeekOffset = new Vector3f(View.PeekOffset.X, View.PeekOffset.Y, SwimViewLift);
        if (Weapon.Firing is { } firing)
        {
            firing.ReleaseAutoFire();
            firing.Effects.StopAllSounds();
            firing.SetOverlayStatus(Igi2WeaponOverlayStatus.None);
        }

        int state = Locomotion.CurrentState;
        Animation.PlayState(state, 0, StateBlendSeconds);
        if (Locomotion.PreviousState is not (Igi2PlayerState.SwimForward or Igi2PlayerState.SwimBackward or Igi2PlayerState.TreadWater))
        {
            Action.Current = (_, _) => RunLock();
            SetWeaponTypeAnim(Igi2WeaponAnimRequest.FirstActivate, 0, reverse: false);
        }

        Speed = default;
        Signal &= ~Igi2PlayerSignal.Grounded;
        DropGround();
        IsLockAction = true;
        IsSwimming = true;
        CurrentUpperBody = 0;
        RequestedUpperBody = 0;
        if (state == Igi2PlayerState.TreadWater)
        {
            Locomotion.Current = (_, _) => RunTreadWater();
            Surroundings.StirWater(IsRunning);
        }
        else
        {
            Signal |= Igi2PlayerSignal.StopAnimation;
            Locomotion.Current = (_, _) => RunSwim();
        }
    }

    /// <summary>
    /// <c>HumanPlayer_RunStateSwim</c> (<c>igi2 0x4AB860</c>): a stroke sounds when one starts; the body is kept at swimming depth;
    /// forward or back strokes along the body; out of the water and on the ground it stands; dead it dies; the strokes turn from
    /// one way to the other, and with no key held it treads water.
    /// </summary>
    private void RunSwim()
    {
        SwimUpdateActivate();
        Igi2PlayerSignal signal = Signal;
        if ((signal & Igi2PlayerSignal.StopAnimation) != 0)
        {
            Signal &= ~Igi2PlayerSignal.StopAnimation;
            SwimStrokeSound();
        }

        KeepSwimDepth();
        if ((signal & Igi2PlayerSignal.Forward) != 0)
        {
            StepOnGround(SwimForwardSpeed, 0f);
        }
        else if ((signal & Igi2PlayerSignal.Backward) != 0)
        {
            StepOnGround(SwimBackwardSpeed, 3.1415927410125732f);
        }

        if (LeaveWater(signal))
        {
            return;
        }

        Igi2PlayerSignal strokes = signal & (Igi2PlayerSignal.Forward | Igi2PlayerSignal.Backward);
        int state = Locomotion.CurrentState;
        if (state == Igi2PlayerState.SwimForward && strokes == Igi2PlayerSignal.Backward)
        {
            Go(Igi2PlayerState.SwimBackward);
        }
        else if (state == Igi2PlayerState.SwimBackward && strokes == Igi2PlayerSignal.Forward)
        {
            Go(Igi2PlayerState.SwimForward);
        }
        else if (strokes == 0)
        {
            Go(Igi2PlayerState.TreadWater);
        }
    }

    /// <summary>
    /// <c>HumanPlayer_RunStateTreadWater</c> (<c>igi2 0x4AB2A0</c>): kept at swimming depth; out of the water and on the ground it
    /// stands; dead it dies; a stroke key swims that way.
    /// </summary>
    private void RunTreadWater()
    {
        SwimUpdateActivate();
        Igi2PlayerSignal signal = Signal;
        KeepSwimDepth();
        if (LeaveWater(signal))
        {
            return;
        }

        Igi2PlayerSignal strokes = signal & (Igi2PlayerSignal.Forward | Igi2PlayerSignal.Backward);
        if (strokes == Igi2PlayerSignal.Forward)
        {
            Go(Igi2PlayerState.SwimForward);
        }
        else if (strokes == Igi2PlayerSignal.Backward)
        {
            Go(Igi2PlayerState.SwimBackward);
        }
    }

    /// <summary>The body is put back at swimming depth once it has drifted more than a hair from it, and stops.</summary>
    private void KeepSwimDepth()
    {
        double off = (double)WaterDepth - SwimDepth;
        if (Math.Abs(off) > SwimDepthTolerance)
        {
            Vector3d at = Position;
            PopPosition(new Vector3d(at.X, at.Y, off + at.Z));
            GroundMovementSpeed = default;
        }
    }

    /// <summary>
    /// Shallow enough and on the ground, the swim ends: the weapon the binoculars replaced comes back, the weapon is raised from
    /// the upper body's change (mode twelve), the lock lifts, the eye comes down and the player stands. Dead, it dies.
    /// </summary>
    /// <returns>Whether the state was left.</returns>
    private bool LeaveWater(Igi2PlayerSignal signal)
    {
        if (WaterDepth < SwimDepth && (signal & Igi2PlayerSignal.Grounded) != 0)
        {
            View.PeekSpeed = new Vector3f(View.PeekSpeed.X, View.PeekSpeed.Y, 0f);
            View.PeekOffset = new Vector3f(View.PeekOffset.X, View.PeekOffset.Y, 0f);
            if (PreviousWeaponType is { } previous && Weapon.Inventory?.FindWeapon(previous.WeaponId) is { } item)
            {
                Weapon.Activate(item);
                PreviousWeaponType = null;
            }

            SetUpperBody(12);
            Action.Current = (_, _) => RunRaiseAfterSwim();
            IsSwimming = false;
            IsLockAction = false;
            Go(Igi2PlayerState.Stand);
            return true;
        }

        if ((signal & Igi2PlayerSignal.Dead) != 0)
        {
            IsLockAction = false;
            Go(Igi2PlayerState.Die);
            return true;
        }

        return false;
    }

    // sub_4C2030: once the weapon's clip has played out, the weapon is ready and the action machine idles.
    private void RunRaiseAfterSwim()
    {
        if ((ActionSignal & Igi2PlayerSignal.AnimationEnded) != 0)
        {
            IsWeaponReady = true;
            GoAction(Igi2PlayerActionState.Idle);
        }
    }

    /// <summary>
    /// <c>HumanPlayer_SwimUpdateActivate</c> (<c>igi2 0x4AAFF0</c>): what the swimmer could activate, found as on land but with no
    /// footing asked for, so a ladder can be taken from the water.
    /// </summary>
    private void SwimUpdateActivate()
    {
        Matrix3f v = View.Orientation;
        Interaction.Igi2EvaluateActivate context = new(View.Position, [v.M11, v.M12, v.M13, v.M21, v.M22, v.M23, v.M31, v.M32, v.M33], isPlayer: true)
        {
            Activator = this,
        };
        Vector3f r = RootTranslation;
        float radius = (Fpu.Sqrt((((Fpu)r.X * r.X) + ((Fpu)r.Y * r.Y)) + ((Fpu)r.Z * r.Z)) + ModelRadius).F;
        foreach (Interaction.IIgi2Activatable candidate in Surroundings.FindActivatables(Position, radius))
        {
            candidate.EvaluateActivate(context);
        }

        EvaluateTarget = context.Best;
    }

    /// <summary><c>Human_CreateSwimStrokeSound</c> (<c>igi2 0x506E40</c>): the next of twelve strokes, at the view.</summary>
    private void SwimStrokeSound()
    {
        _swimStroke = _swimStroke + 1 < SwimStrokes.Length ? _swimStroke + 1 : 0;
        Surroundings.PlaySound(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"swim_{SwimStrokes[_swimStroke]:00}"));
    }
}
