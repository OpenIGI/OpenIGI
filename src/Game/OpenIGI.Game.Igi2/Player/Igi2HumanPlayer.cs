using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>A human's posture, <c>Human_s.ePose</c>.</summary>
public enum Igi2HumanPose
{
    /// <summary>Standing.</summary>
    Standing = 0,

    /// <summary>Crouching.</summary>
    Crouching = 1,

    /// <summary>Lying flat.</summary>
    LyingFlat = 2,
}

/// <summary>The postures a human is allowed into, <c>Human_s.bAllowedStance</c>.</summary>
/// <remarks>Cleared bit by bit by whatever forbids a posture this tick: water depth, a held launcher, a jump cooldown.</remarks>
[Flags]
public enum Igi2AllowedStance : byte
{
    /// <summary>Nothing allowed.</summary>
    None = 0,

    /// <summary>Standing.</summary>
    Stand = 0x1,

    /// <summary>Crouching.</summary>
    Crouch = 0x2,

    /// <summary>Lying flat.</summary>
    Lie = 0x4,

    /// <summary>Jumping.</summary>
    Jump = 0x8,

    /// <summary>Everything.</summary>
    All = Stand | Crouch | Lie | Jump,
}

/// <summary>Which of the held weapon's precisions the player's posture selects, <c>ePrecisionType</c>.</summary>
public enum Igi2PrecisionType
{
    /// <summary>Standing still, or no weapon.</summary>
    Standing = 0,

    /// <summary>Running.</summary>
    Running = 1,
}

/// <summary>The player, as I.G.I. 2's <c>HumanPlayer_t</c> runs it.</summary>
/// <remarks>
/// <para>
/// One class for the player's simulation: the channel block its input fills, the locomotion state
/// machine, and the per-player state its states read and write. It grows a retail function at a time;
/// each member names the one it comes from. The parts of the engine the states reach into — the
/// animation layer, the collision probe, the AI's ears, the held weapon — are behind interfaces, so the
/// locomotion logic is testable on its own.
/// </para>
/// </remarks>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>The fire category whose weapon zooms on fire rather than shooting.</summary>
    /// <remarks>
    /// <c>WEAPONCATEGORY_LASERDESIGNATOR</c>, registered as 10 by <c>WeaponType_Open</c>
    /// (<c>igi2 0x436C60</c>) and compared against <c>WeaponType+0x2A0</c> ("Weapon Category") by the signal
    /// handler.
    /// </remarks>
    public const int LaserDesignatorCategory = 10;

    /// <summary>Initializes a new instance of the <see cref="Igi2HumanPlayer"/> class.</summary>
    /// <param name="weapon">The held-weapon controls; <see cref="Igi2NoWeaponControl.Instance"/> for none.</param>
    /// <param name="animation">The animation layer the states drive.</param>
    /// <param name="surroundings">The world the states probe.</param>
    public Igi2HumanPlayer(
        IIgi2PlayerWeaponControl weapon,
        IIgi2HumanAnimation animation,
        IIgi2HumanSurroundings surroundings)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        ArgumentNullException.ThrowIfNull(animation);
        ArgumentNullException.ThrowIfNull(surroundings);
        Weapon = weapon;
        Animation = animation;
        Surroundings = surroundings;
        Locomotion = CreateLocomotion();
    }

    /// <summary>Gets the channel block the player's input fills.</summary>
    public Igi2HumanChannels Channels { get; } = new();

    /// <summary>Gets or sets the held-weapon controls.</summary>
    public IIgi2PlayerWeaponControl Weapon { get; set; }

    /// <summary>Gets the animation layer the states drive.</summary>
    public IIgi2HumanAnimation Animation { get; }

    /// <summary>Gets the world the states probe.</summary>
    public IIgi2HumanSurroundings Surroundings { get; }

    /// <summary>Gets the locomotion state machine, <c>Human_s.tState</c>.</summary>
    public TaskStateMachine Locomotion { get; }

    /// <summary>Gets or sets the locomotion signal word, <c>tState.bSignal</c>.</summary>
    public Igi2PlayerSignal Signal
    {
        get => (Igi2PlayerSignal)Locomotion.Flags;
        set => Locomotion.Flags = (uint)value;
    }

    /// <summary>Gets or sets a value indicating whether walking is toggled on, <c>isWalk</c>.</summary>
    public bool IsWalk { get; set; }

    /// <summary>Gets a value indicating whether the human runs rather than walks, <c>Human_s.isRunning</c>.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Gets or sets the posture.</summary>
    public Igi2HumanPose Pose { get; set; }

    /// <summary>Gets or sets the postures allowed this tick.</summary>
    public Igi2AllowedStance AllowedStance { get; set; } = Igi2AllowedStance.All;

    /// <summary>Gets or sets the perfect-shot timer, <c>nPerfectShotTimer</c>.</summary>
    /// <remarks>Counts up while the player keeps still and is zeroed by almost every input.</remarks>
    public int PerfectShotTimer { get; set; }

    /// <summary>Gets or sets the body's orientation, <c>tModelObj.tOrientation</c>: rows are the local X, Y and Z axes.</summary>
    public Matrix3f Orientation { get; set; } = Matrix3f.Identity;

    /// <summary>Gets or sets the speed the player's own legs add on the ground, <c>tGroundMovementSpeed</c>.</summary>
    public Vector3f GroundMovementSpeed { get; set; }

    /// <summary>Gets or sets how much the ground's slope slows the gait, <c>Human_s.vGroundAngleFactor</c>.</summary>
    public float GroundAngleFactor { get; set; }

    /// <summary>Gets or sets the turning speed, <c>Human_s.vTurningSpeed</c>.</summary>
    public float TurningSpeed { get; set; }

    /// <summary>Gets or sets the scale on every target speed, <c>vMovementSpeedScale</c>.</summary>
    /// <remarks>Set each tick from the carried weapons' slow-down; see <c>HumanPlayer_RunDelayedHandler</c>.</remarks>
    public float MovementSpeedScale { get; set; } = 1f;

    /// <summary>Gets the posture precision the current state selected.</summary>
    public Igi2PrecisionType PrecisionType { get; private set; }

    /// <summary>Gets the precision the current state aims for, <c>vIdealModifiedPrecision</c>.</summary>
    public float IdealModifiedPrecision { get; private set; }

    /// <summary>Gets the upper-body action the current state asks for, <c>eRequestedUpperBody</c>.</summary>
    public int RequestedUpperBody { get; private set; }

    /// <summary>Gets a value indicating whether the crouch the player is in was entered by holding the key.</summary>
    /// <remarks><c>tStateContext.tCrouchContext.isHoldButtonMode</c>: a held crouch ends on release, a toggled one does not.</remarks>
    public bool CrouchIsHeld { get; private set; }

    /// <summary>Packs this tick's signal word from the channels.</summary>
    /// <remarks>
    /// <c>HumanPlayer_StateSignalHandler</c> (<c>igi2 0x4A2800</c>), run by the state machine before the
    /// current state. The order of the tests is the original's, because a few of them act on the weapon as
    /// they go and one can clear a channel a later test reads.
    /// </remarks>
    public void PackSignal()
    {
        Igi2HumanChannels channels = Channels;
        Igi2PlayerSignal signal = Signal & Igi2PlayerSignal.Carried;

        if (channels.IsHeld(Igi2HumanChannel.LookX))
        {
            signal |= Igi2PlayerSignal.Turning;
        }

        if (Animation.MainEnded)
        {
            signal |= Igi2PlayerSignal.AnimationEnded;
        }

        // Modify-walk-run inverts the walk toggle while held: running when the two agree.
        if (IsWalk == channels.IsHeld(Igi2HumanChannel.ModifyWalkRun))
        {
            IsRunning = true;
            signal &= ~Igi2PlayerSignal.Walking;
        }
        else
        {
            IsRunning = false;
            signal |= Igi2PlayerSignal.Walking;
        }

        signal |= Held(Igi2HumanChannel.MoveLeft, Igi2PlayerSignal.Left);
        signal |= Held(Igi2HumanChannel.MoveRight, Igi2PlayerSignal.Right);
        signal |= Held(Igi2HumanChannel.MoveForwards, Igi2PlayerSignal.Forward);
        signal |= Held(Igi2HumanChannel.MoveBackwards, Igi2PlayerSignal.Backward);

        bool designator = Weapon.ActiveCategory == LaserDesignatorCategory;
        if (channels.IsHeld(Igi2HumanChannel.Fire))
        {
            // The designator lases on fire: pressing it raises the zoom, and the trigger latch is cleared so
            // the press does not also count as a shot.
            if (designator)
            {
                if (!Weapon.IsZoomFireModeActive)
                {
                    Weapon.Fire();
                }

                Weapon.ClearTrigger();
            }

            signal |= Igi2PlayerSignal.Fire;
        }
        else if (designator && Weapon.IsZoomFireModeActive)
        {
            Weapon.Fire();
        }

        if (channels.IsHeld(Igi2HumanChannel.AlternateFire))
        {
            // Two independent chances to raise the bit, as the original writes it: suppressed only when the
            // held weapon is a designator AND a zoom that may not zoom right now.
            if (designator)
            {
                channels[Igi2HumanChannel.AlternateFire] = 0f;
            }
            else
            {
                signal |= Igi2PlayerSignal.AlternateFire;
            }

            if (Weapon.ActiveIsZoom && !Weapon.IsZoomedModeAllowed)
            {
                channels[Igi2HumanChannel.AlternateFire] = 0f;
            }
            else
            {
                signal |= Igi2PlayerSignal.AlternateFire;
            }
        }

        signal |= Held(Igi2HumanChannel.Reload, Igi2PlayerSignal.Reload);
        signal |= Held(Igi2HumanChannel.Activate, Igi2PlayerSignal.Activate);
        signal |= Pressed(Igi2HumanChannel.RaiseStance, Igi2PlayerSignal.RaiseStance);
        signal |= Pressed(Igi2HumanChannel.LowerStance, Igi2PlayerSignal.LowerStance);

        if (channels.WasPressed(Igi2HumanChannel.ToggleStandLie))
        {
            signal |= Pose == Igi2HumanPose.LyingFlat ? Igi2PlayerSignal.StandUp : Igi2PlayerSignal.LieDown;
            PerfectShotTimer = 0;
        }

        if (channels.IsHeld(Igi2HumanChannel.ModifyWalkRun))
        {
            signal |= Igi2PlayerSignal.ModifyWalkRun;
        }

        // Crouch held raises the bit every tick but only resets the timer when the channel changes: the
        // original tests the current and last values together and skips the reset when they agree.
        if (channels.IsHeld(Igi2HumanChannel.Crouch))
        {
            signal |= Igi2PlayerSignal.Crouch;
        }

        if (channels.IsHeld(Igi2HumanChannel.Crouch) != (channels.Last[(int)Igi2HumanChannel.Crouch] != 0f))
        {
            PerfectShotTimer = 0;
        }

        signal |= Held(Igi2HumanChannel.Jump, Igi2PlayerSignal.Jump);
        signal |= Held(Igi2HumanChannel.NextWeapon, Igi2PlayerSignal.NextWeapon);

        if (channels.WasPressed(Igi2HumanChannel.ZoomIn))
        {
            Weapon.StepActionMenu(1);
        }

        if (channels.WasPressed(Igi2HumanChannel.ZoomOut))
        {
            Weapon.StepActionMenu(-1);
        }

        if (channels.WasPressed(Igi2HumanChannel.ZoomIn))
        {
            Weapon.Zoom(zoomIn: true);
            PerfectShotTimer = 0;
        }

        if (channels.WasPressed(Igi2HumanChannel.ZoomOut))
        {
            Weapon.Zoom(zoomIn: false);
            PerfectShotTimer = 0;
        }

        Signal = signal;
    }

    private Igi2PlayerSignal Held(Igi2HumanChannel channel, Igi2PlayerSignal bit)
    {
        if (!Channels.IsHeld(channel))
        {
            return Igi2PlayerSignal.None;
        }

        PerfectShotTimer = 0;
        return bit;
    }

    private Igi2PlayerSignal Pressed(Igi2HumanChannel channel, Igi2PlayerSignal bit)
    {
        if (!Channels.WasPressed(channel))
        {
            return Igi2PlayerSignal.None;
        }

        PerfectShotTimer = 0;
        return bit;
    }
}
