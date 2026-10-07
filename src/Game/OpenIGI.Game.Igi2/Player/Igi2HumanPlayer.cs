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
}

/// <summary>The player, as I.G.I. 2's <c>HumanPlayer_t</c> runs it.</summary>
/// <remarks>
/// <para>
/// One class for the player's simulation: the channel block its input fills, the locomotion signal word
/// and the per-player flags the states read. It grows a retail function at a time; each member names the
/// one it comes from.
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
    public Igi2HumanPlayer(IIgi2PlayerWeaponControl weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        Weapon = weapon;
    }

    /// <summary>Gets the channel block the player's input fills.</summary>
    public Igi2HumanChannels Channels { get; } = new();

    /// <summary>Gets or sets the held-weapon controls.</summary>
    public IIgi2PlayerWeaponControl Weapon { get; set; }

    /// <summary>Gets or sets the locomotion signal word, <c>tState.bSignal</c>.</summary>
    public Igi2PlayerSignal Signal { get; set; }

    /// <summary>Gets or sets a value indicating whether walking is toggled on, <c>isWalk</c>.</summary>
    public bool IsWalk { get; set; }

    /// <summary>Gets a value indicating whether the human runs rather than walks, <c>Human_s.isRunning</c>.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Gets or sets the posture.</summary>
    public Igi2HumanPose Pose { get; set; }

    /// <summary>Gets or sets the postures allowed this tick.</summary>
    public Igi2AllowedStance AllowedStance { get; set; } = Igi2AllowedStance.Stand | Igi2AllowedStance.Crouch | Igi2AllowedStance.Lie | Igi2AllowedStance.Jump;

    /// <summary>Gets or sets the perfect-shot timer, <c>nPerfectShotTimer</c>.</summary>
    /// <remarks>Counts up while the player keeps still and is zeroed by almost every input.</remarks>
    public int PerfectShotTimer { get; set; }

    /// <summary>Gets or sets a value indicating whether the main animation controller has finished its clip.</summary>
    public bool MainAnimationEnded { get; set; }

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

        if (MainAnimationEnded)
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

        // Crouch held raises the bit every tick but only resets the timer on the press: the original tests
        // the current and last values together and skips the reset when both are set.
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
