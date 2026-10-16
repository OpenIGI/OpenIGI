using OpenIGI.Game.Igi2.Computer;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Player;

/// <content>
/// The map computer: a computer key, freshly pressed on the ground, opens it on its screen; the player stands still, its view
/// locked, while another computer key turns to that screen and the same one, a weapon key, the binoculars or the goggles, a hit or
/// death closes it again. What the screens show is the shell's.
/// </content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>Gets or sets the level's map computer, or <see langword="null"/> with none.</summary>
    public Igi2Computer? Computer { get; set; }

    /// <summary>Gets or sets a value indicating whether the computer closes on its next tick, <c>isQuitComputer</c>.</summary>
    public bool IsQuitComputer { get; set; }

    /// <summary>Gets or sets the slot the save screen chose, one past it, nought for none, <c>nTempSaveSlot</c>.</summary>
    public int TempSaveSlot { get; set; }

    /// <summary>Gets or sets the slot to be saved to, one past it, nought for none, <c>nSaveSlot</c>: the delayed run writes it.</summary>
    public int SaveSlot { get; set; }

    /// <summary>Gets or sets the slot to be loaded, one past it, nought for none, <c>nLoadSlot</c>: the delayed run reads it.</summary>
    public int LoadSlot { get; set; }

    /// <summary>Gets the slot the quick load reads, one past it, nought for none (<c>HumanPlayer_GetQuickLoadSlot</c>).</summary>
    public int QuickLoadSlot { get; private set; }

    /// <summary>Gets a value indicating whether the computer may save, <c>HumanPlayer_IsSaveAllowed</c>: the action not locked.</summary>
    public bool IsSaveAllowed => !IsLockAction;

    /// <summary>The save screen starts a save, <c>HumanPlayer_BeginSaveToSlot</c>: the slot chosen, one of the battery's charges used.</summary>
    /// <param name="slot">The slot.</param>
    public void BeginSaveToSlot(int slot)
    {
        TempSaveSlot = slot + 1;
        SavesUsed++;
    }

    /// <summary>The save screen finishes it, <c>HumanPlayer_FinishSaveToSlot</c>: the slot to be written, and quick-loaded from.</summary>
    /// <param name="slot">The slot.</param>
    public void FinishSaveToSlot(int slot)
    {
        SaveSlot = slot + 1;
        QuickLoadSlot = slot + 1;
    }

    /// <summary>The save screen asks for a load, <c>HumanPlayer_LoadFromSlot</c>: the slot to be read, and quick-loaded from.</summary>
    /// <param name="slot">The slot.</param>
    public void LoadFromSlot(int slot)
    {
        LoadSlot = slot + 1;
        QuickLoadSlot = slot + 1;
    }

    /// <summary>
    /// Asks the computer to close, <c>HumanPlayer_QuitComputer</c>, as its screens' own close buttons do.
    /// </summary>
    public void QuitComputer() => IsQuitComputer = true;

    /// <summary>
    /// Whether a computer key went down this tick, <c>HumanPlayer_IsActivateComputer</c> in the single-player game: the map, the log,
    /// the objectives or the save screen's.
    /// </summary>
    private bool IsActivateComputer() =>
        Channels.WasPressed(Igi2HumanChannel.ComputerMap) || Channels.WasPressed(Igi2HumanChannel.ComputerLog)
        || Channels.WasPressed(Igi2HumanChannel.ComputerObjectives) || Channels.WasPressed(Igi2HumanChannel.ComputerIgiHq);

    /// <summary>
    /// Opens the computer, <c>HumanPlayer_ActivateComputer</c>: on the save screen for its key, the objectives for theirs, the log for
    /// its, the map otherwise; the view is taken out of any zoom and the action goes to the computer.
    /// </summary>
    private void ActivateComputer()
    {
        Igi2ComputerMenu mode = Channels[Igi2HumanChannel.ComputerIgiHq] != 0f ? Igi2ComputerMenu.Save
            : Channels[Igi2HumanChannel.ComputerObjectives] != 0f ? Igi2ComputerMenu.Objectives
            : Channels[Igi2HumanChannel.ComputerLog] != 0f ? Igi2ComputerMenu.Log
            : Igi2ComputerMenu.Map;
        if (Computer is { } computer)
        {
            computer.StartupMode = mode;
        }

        View.IsZoomMode = false;
        View.FieldOfView = (float)Math.Tan(0.7853981852531433);
        View.ObjectFieldOfView = (float)Math.Tan(0.5235987901687622);
        GoAction(Igi2PlayerActionState.Computer);
    }

    /// <summary>The computer action, <c>HumanPlayer_InitActionComputer</c>: the legs go to the computer state and the weapon is put down.</summary>
    private void EnterComputerAction()
    {
        Go(Igi2PlayerState.Computer);
        IsWeaponReady = false;
        Action.Current = (_, _) => RunComputerAction();
    }

    // The computer action's run handler (0x4C2310): once the legs have left the computer, the weapon is ready and the action idle.
    private void RunComputerAction()
    {
        if (Locomotion.CurrentState != Igi2PlayerState.Computer)
        {
            IsWeaponReady = true;
            GoAction(Igi2PlayerActionState.Idle);
        }
    }

    /// <summary>
    /// The computer state, <c>HumanPlayer_InitStateComputer</c>: a mounted gun being worked is paused; the crouch's clip crouched,
    /// the lying one lying, the stand's standing, with the stand's upper body unless lying; the standing precision; the view and
    /// the body locked; and the computer opened.
    /// </summary>
    /// <remarks>The sound listener's move to the view is the audio's, not ported here.</remarks>
    private void EnterComputer()
    {
        if (ActivatingTarget is Igi2StationaryGunTask gun)
        {
            gun.Pause();
        }

        if (Pose == Igi2HumanPose.LyingFlat)
        {
            Animation.PlayState(Igi2PlayerState.Flat, 0, StateBlendSeconds);
        }
        else
        {
            Animation.PlayState(Pose == Igi2HumanPose.Crouching ? Igi2PlayerState.Crouch : Igi2PlayerState.Stand, 0, StateBlendSeconds);
            RequestedUpperBody = 4;
        }

        AimPrecision(Igi2PrecisionType.Standing);
        View.IsLockOrientation = true;
        IsLockOrientation = true;
        Computer?.Open();
        Locomotion.Current = (_, _) => RunComputer();
    }

    /// <summary>
    /// <c>HumanPlayer_RunStateComputer</c>: the player stands its ground. A computer key turns to its screen, unless it is shown;
    /// that key again, a weapon key, the binoculars or the goggles, a quit asked for, a hit or death closes the computer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The map's key turns to the map only while it is enabled; while it is not, it closes the computer from the objectives and does
    /// nothing from another screen. The next and previous weapon keys are spent either way.
    /// </para>
    /// <para>
    /// Closing frees the view and the body; a slot the save screen chose becomes the one saved to and quick-loaded from; the
    /// computer closes and the body's impact stops. Binoculars raised before it are fired again, as they were; their key, or the
    /// goggles', raises them if they are not in hand. The legs then die, go back to a gun they were working, or to the stance's own
    /// state.
    /// </para>
    /// </remarks>
    private void RunComputer()
    {
        MoveOnGround(0f, GroundAcceleration, 0f, GroundAcceleration);
        Igi2PlayerSignal signal = Signal;
        bool activate = IsActivateComputer();
        bool leave = activate || Channels[Igi2HumanChannel.Binoculars] != 0f || Channels[Igi2HumanChannel.Thermal] != 0f
            || Channels[Igi2HumanChannel.NextWeapon] != 0f || Channels[Igi2HumanChannel.PrevWeapon] != 0f
            || IsQuitComputer || (signal & (Igi2PlayerSignal.Dead | Igi2PlayerSignal.Hit)) != 0;
        for (Igi2HumanChannel category = Igi2HumanChannel.WeaponCategory1; !leave && category <= Igi2HumanChannel.WeaponCategory9; category++)
        {
            leave = Channels[category] != 0f;
        }

        if (!leave)
        {
            return;
        }

        Channels[Igi2HumanChannel.NextWeapon] = 0f;
        Channels[Igi2HumanChannel.PrevWeapon] = 0f;
        if (Computer is { } computer && activate && TurnComputer(computer))
        {
            return;
        }

        CloseComputer(signal);
    }

    // The screen switch at the head of the close: whether a computer key only turned the computer to its screen.
    private bool TurnComputer(Igi2Computer computer)
    {
        Igi2ComputerMenu active = computer.ActiveMenu;
        if (Channels[Igi2HumanChannel.ComputerMap] != 0f)
        {
            if (active == Igi2ComputerMenu.Map)
            {
                return false;
            }

            if (computer.IsEnabled)
            {
                computer.RequestMenu(Igi2ComputerMenu.Map);
                return true;
            }

            return active != Igi2ComputerMenu.Objectives;
        }

        Igi2ComputerMenu asked = Channels[Igi2HumanChannel.ComputerLog] != 0f ? Igi2ComputerMenu.Log
            : Channels[Igi2HumanChannel.ComputerIgiHq] != 0f ? Igi2ComputerMenu.Save
            : Channels[Igi2HumanChannel.ComputerObjectives] != 0f ? Igi2ComputerMenu.Objectives
            : Igi2ComputerMenu.None;
        if (asked == Igi2ComputerMenu.None || asked == active)
        {
            return false;
        }

        computer.RequestMenu(asked);
        return true;
    }

    private void CloseComputer(Igi2PlayerSignal signal)
    {
        IsQuitComputer = false;
        View.IsLockOrientation = false;
        IsLockOrientation = false;
        if (TempSaveSlot != 0)
        {
            SaveSlot = TempSaveSlot;
            TempSaveSlot = 0;
            QuickLoadSlot = SaveSlot;
        }

        Computer?.Close();
        ImpactSpeed = default;
        if (PreviousWeaponType is not null)
        {
            Weapon.ClearOverlay();
            Weapon.Fire(0);
            if (Weapon.Firing is { } firing)
            {
                firing.FireTypeState(0).IsReleaseFire = true;
                if (Weapon.Inventory is { } inventory)
                {
                    inventory.Status = Igi2WeaponStatus.Ready;
                }
            }
        }

        Igi2WeaponTypeTask? held = Weapon.Inventory?.Current?.WeaponType;
        if (Channels[Igi2HumanChannel.Binoculars] != 0f && held?.WeaponId != BinocularsWeaponId)
        {
            CheckBinoculars(forceChangeWeapon: false);
        }

        if (Channels[Igi2HumanChannel.Thermal] != 0f && held?.WeaponId != ThermalWeaponId)
        {
            CheckBinoculars(forceChangeWeapon: false);
        }

        if ((signal & Igi2PlayerSignal.Dead) != 0)
        {
            Go(Igi2PlayerState.Die);
        }
        else if (Locomotion.PreviousState == Igi2PlayerState.ActivateStationaryGun && ActivatingTarget is Igi2StationaryGunTask gun)
        {
            gun.Unpause(this);
            Go(Igi2PlayerState.ActivateStationaryGun);
        }
        else
        {
            Go(Pose switch
            {
                Igi2HumanPose.Crouching => Igi2PlayerState.Crouch,
                Igi2HumanPose.LyingFlat => Igi2PlayerState.Flat,
                _ => Igi2PlayerState.Stand,
            });
        }
    }
}
