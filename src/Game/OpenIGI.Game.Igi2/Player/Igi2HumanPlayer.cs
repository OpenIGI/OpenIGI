using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>Which of the held weapon's precisions the player's posture selects, <c>ePrecisionType</c>.</summary>
public enum Igi2PrecisionType
{
    /// <summary>Standing still, or no weapon.</summary>
    Standing = 0,

    /// <summary>Running.</summary>
    Running = 1,

    /// <summary>Walking.</summary>
    Walking = 2,

    /// <summary>Crouching still.</summary>
    Crouching = 3,

    /// <summary>Walking crouched.</summary>
    CrouchWalking = 4,

    /// <summary>Lying flat.</summary>
    LyingFlat = 5,

    /// <summary>Crawling.</summary>
    Crawling = 6,

    /// <summary>In the air from a standing jump: running plus the jumping modifier.</summary>
    Jumping = 7,

    /// <summary>In the air from a crouched jump: running, the crouched walk and the jumping modifier.</summary>
    CrouchJumping = 8,
}

/// <summary>The player, as I.G.I. 2's <c>HumanPlayer_t</c> runs it.</summary>
/// <remarks>
/// <para>
/// The player's own part over the shared <see cref="Igi2Human"/> body: its tuning, its signal word, its
/// states and its action machine. It grows a retail function at a time; each member names the one it
/// comes from. The parts of the engine the states reach into — the animation layer, the collision probe,
/// the AI's ears, the held weapon — are behind interfaces, so the locomotion logic is testable on its own.
/// </para>
/// </remarks>
public sealed partial class Igi2HumanPlayer : Igi2Human
{
    /// <summary>The fire category whose weapon zooms on fire rather than shooting.</summary>
    /// <remarks>
    /// <c>WEAPONCATEGORY_LASERDESIGNATOR</c>, registered as 10 by <c>WeaponType_Open</c>
    /// (<c>igi2 0x436C60</c>) and compared against <c>WeaponType+0x2A0</c> ("Weapon Category") by the signal
    /// handler.
    /// </remarks>
    public const int LaserDesignatorCategory = 10;

    private IIgi2PlayerWeaponControl _weapon;

    /// <summary>Initializes a new instance of the <see cref="Igi2HumanPlayer"/> class.</summary>
    /// <param name="weapon">The held-weapon controls; <see cref="Igi2NoWeaponControl.Instance"/> for none.</param>
    /// <param name="animation">The animation layer the states drive.</param>
    /// <param name="surroundings">The world the states probe.</param>
    public Igi2HumanPlayer(
        IIgi2PlayerWeaponControl weapon,
        IIgi2HumanAnimation animation,
        IIgi2HumanSurroundings surroundings)
        : base(animation, surroundings, static human => ((Igi2HumanPlayer)human).CreateLocomotion())
    {
        ArgumentNullException.ThrowIfNull(weapon);
        _weapon = weapon;
        weapon.Bind(this);
        BodyTriggers = new Igi2PlayerBodyTriggers(this);
        View.ObjectAnimation.Target = new Igi2PlayerViewTriggers(this);
    }

    /// <summary>Gets what the body's clips fire their anim triggers on: the player (<see cref="TriggerFromBody"/>).</summary>
    public IIgi2AnimTriggerTarget BodyTriggers { get; }

    /// <summary>Gets or sets the held-weapon controls; the player becomes their holder.</summary>
    public IIgi2PlayerWeaponControl Weapon
    {
        get => _weapon;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _weapon = value;
            value.Bind(this);
        }
    }

    /// <inheritdoc/>
    protected override IIgi2HumanWeapon HumanWeapon => Weapon;

    /// <summary>Gets or sets the locomotion signal word, <c>tState.bSignal</c>.</summary>
    public Igi2PlayerSignal Signal
    {
        get => (Igi2PlayerSignal)Locomotion.Flags;
        set => Locomotion.Flags = (uint)value;
    }

    /// <summary>Gets or sets a value indicating whether walking is toggled on, <c>isWalk</c>.</summary>
    public bool IsWalk { get; set; }

    /// <summary>Gets or sets the perfect-shot timer, <c>nPerfectShotTimer</c>.</summary>
    /// <remarks>Counts up while the player keeps still and is zeroed by almost every input.</remarks>
    public int PerfectShotTimer { get; set; }

    /// <summary>Gets or sets the speed the player's own legs add on the ground, <c>tGroundMovementSpeed</c>.</summary>
    public Vector3f GroundMovementSpeed { get; set; }

    /// <summary>Gets or sets the player tuning the script set, <c>HumanPlayer_tGeneral</c>.</summary>
    public Igi2HumanPlayerGeneral General { get; set; } = Igi2HumanPlayerGeneral.Unset;

    /// <summary>Gets or sets the scale on every target speed, <c>vMovementSpeedScale</c>.</summary>
    /// <remarks>Set each tick from the carried weapons' slow-down; see <c>HumanPlayer_RunDelayedHandler</c>.</remarks>
    public float MovementSpeedScale { get; set; } = 1f;

    /// <summary>Gets the posture precision the current state selected.</summary>
    public Igi2PrecisionType PrecisionType { get; private set; }

    /// <summary>Gets the precision the current state aims for, <c>vIdealModifiedPrecision</c>.</summary>
    public float IdealModifiedPrecision { get; private set; }

    /// <summary>Gets the upper-body action playing, <c>eCurrentUpperBody</c>.</summary>
    public int CurrentUpperBody { get; private set; }

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
                    Weapon.Fire(1);
                }

                Weapon.ClearDelay();
            }

            signal |= Igi2PlayerSignal.Fire;
        }
        else if (designator && Weapon.IsZoomFireModeActive)
        {
            Weapon.Fire(1);
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

            if (Weapon.ActiveIsZoom && !IsZoomedModeAllowed)
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
