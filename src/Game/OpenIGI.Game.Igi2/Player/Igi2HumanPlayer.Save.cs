using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Player;

/// <content>
/// Its save state, <c>HumanPlayer_ptSerializeParameterList</c> past a human's: its action machine, its weapon handling and precision,
/// its upper body, its timed actions, its ladder, fence, wire and peek, its damage flashes, its saves used, its swimming, its grenade,
/// its movement and its cheats.
/// </content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>Gets or sets the saves the player has made this mission, <c>nSavesUsed</c>.</summary>
    public int SavesUsed { get; set; }

    /// <summary>Writes the player's save state, a human's first.</summary>
    /// <param name="writer">The writer.</param>
    public void WritePlayerState(StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        WriteHumanState(writer, new Igi2HumanSaveExtras
        {
            IsWeaponReady = IsWeaponReady,
            IsWeaponFire = IsFire,
            IsDisableDeltaTranslationScale = IsDisableDeltaTranslationScale,
            IsLockActivate = IsActivateLocked,
            IsCheckActivateButton = IsCheckActivateButton,
            ActivateTime = ActivateTime,
            Difficulty = (int)Difficulty,
            TimesAlarmTriggered = TimesAlarmTriggered,
            Health = MissionHealth,
            VisibilityFactor = VisibilityFactor,
        });

        // Action: the action machine, written as a human's state is.
        writer.Int((int)Action.Flags);
        writer.Int(0);
        writer.Int(Action.PreviousState);
        writer.Int(0);
        writer.Int(Action.CurrentState);
        writer.Array(ReadOnlySpan<byte>.Empty);

        writer.Bool(IsLockAction);
        writer.Bool(IsWalk);
        writer.Int(WeaponCycleIndex);
        writer.Int(ChangeWeaponId);
        writer.Int(0);
        writer.Int(FireType);
        writer.Int((int)PrecisionType);
        writer.Real(IdealWeaponPrecision);
        writer.Real(ActualWeaponPrecision);
        writer.Real(IdealModifiedPrecision);
        writer.Real(ActualModifiedPrecision);
        writer.Int(CurrentUpperBody);
        writer.Int(RequestedUpperBody);
        writer.Int(TimedActionTime);
        writer.Int(TimedActionTimer);
        writer.Bool(IsProgressBar);
        writer.Int(0);
        writer.Int(SearchPhase);
        writer.Bool(IsInitClimbLadder);
        writer.Bool(IsClimbLadder);
        writer.Bool(IsDeInitClimbLadder);
        writer.Bool(IsLadderActivateTop);
        writer.Int(LadderSteps);
        writer.Int(LadderStep);
        writer.Int(LadderStepChange);
        writer.Bool(IsInitClimbFence);
        writer.Bool(IsClimbFence);
        writer.Bool(IsDeInitClimbFence);
        writer.Bool(IsInitClimbWire);
        writer.Bool(IsClimbWire);
        writer.Int(0);
        writer.Int(ClimbWireSoundSequence);
        writer.Bool(IsInitSlideWire);
        writer.Bool(IsSlideWire);
        writer.Real3(SlideWireDirection);
        writer.Real(SlideWireSpeed);
        writer.Bool(false);
        writer.Bool(false);
        for (int i = 0; i < 4; i++)
        {
            writer.Real(_damageFlash[i]);
        }

        writer.Real(FlashBangFlash);
        writer.Int(ElectricDamageTimeout);
        writer.Bool(false);
        writer.Bool(IsActivateLocked);
        writer.Int(SavesUsed);
        writer.Bool(IsSwimming);
        writer.Bool(false);
        writer.Bool(false);
        writer.Bool(false);
        writer.Int((int)WeaponStatus);
        writer.Int(GrenadeTickTime);
        writer.Int(GrenadeHeld);
        writer.Real(MovementSpeedScale);
        writer.Array(ReadOnlySpan<byte>.Empty);
        writer.Int(0);
        writer.Int(PerfectShotTimer);
        writer.Bool(false);
        writer.Bool(false);
        writer.Bool(false);
        writer.Bool(IsUnlimitedAmmo);
        writer.Bool(IsFire);
        writer.Bool(IsFirstFire);
        writer.Int(JumpWaitTimer);
        writer.Real(CrossHairDamping);
        writer.Int(TbaTimeout);
    }

    /// <summary>
    /// Reads the player's save state back, <c>HumanPlayer_DeSerializeHandler</c> (humanplayer.c): a gun it mans is let go and its
    /// weapon's overlay taken down first; once read, it is as <see cref="AfterLoad"/> leaves it.
    /// </summary>
    /// <param name="reader">The reader.</param>
    public void ReadPlayerState(StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (AttachParent is Weapons.Igi2StationaryGunTask gun)
        {
            gun.Deactivate();
            Detach();
            EndActivate();
            IsDisableDeltaTranslationScale = false;
        }

        // Before the list is read: the activation and the binoculars' weapon forgotten, the body attached to nothing.
        ActivatingTarget = null;
        Weapon.Firing?.SetOverlayStatus(Weapons.Igi2WeaponOverlayStatus.None);
        PreviousWeaponType = null;
        Detach();
        Igi2HumanSaveExtras extras = ReadHumanState(reader);
        IsWeaponReady = extras.IsWeaponReady;
        IsDisableDeltaTranslationScale = extras.IsDisableDeltaTranslationScale;
        IsCheckActivateButton = extras.IsCheckActivateButton;
        TimesAlarmTriggered = extras.TimesAlarmTriggered;
        MissionHealth = extras.Health;
        VisibilityFactor = extras.VisibilityFactor;

        uint actionFlags = unchecked((uint)reader.Int());
        reader.Int();
        int previousAction = reader.Int();
        reader.Int();
        int action = reader.Int();
        reader.Array();
        IsLockAction = reader.Bool();
        IsWalk = reader.Bool();
        WeaponCycleIndex = reader.Int();
        ChangeWeaponId = reader.Int();
        reader.Int();
        FireType = reader.Int();
        PrecisionType = (Igi2PrecisionType)reader.Int();
        IdealWeaponPrecision = reader.Real();
        ActualWeaponPrecision = reader.Real();
        IdealModifiedPrecision = reader.Real();
        ActualModifiedPrecision = reader.Real();
        CurrentUpperBody = reader.Int();
        RequestedUpperBody = reader.Int();
        TimedActionTime = reader.Int();
        TimedActionTimer = reader.Int();
        IsProgressBar = reader.Bool();
        reader.Int();
        SearchPhase = reader.Int();
        IsInitClimbLadder = reader.Bool();
        IsClimbLadder = reader.Bool();
        IsDeInitClimbLadder = reader.Bool();
        IsLadderActivateTop = reader.Bool();
        LadderSteps = reader.Int();
        LadderStep = reader.Int();
        LadderStepChange = reader.Int();
        IsInitClimbFence = reader.Bool();
        IsClimbFence = reader.Bool();
        IsDeInitClimbFence = reader.Bool();
        IsInitClimbWire = reader.Bool();
        IsClimbWire = reader.Bool();
        reader.Int();
        ClimbWireSoundSequence = reader.Int();
        IsInitSlideWire = reader.Bool();
        IsSlideWire = reader.Bool();
        SlideWireDirection = reader.Real3();
        SlideWireSpeed = reader.Real();
        reader.Bool();
        reader.Bool();
        for (int i = 0; i < 4; i++)
        {
            _damageFlash[i] = reader.Real();
        }

        FlashBangFlash = reader.Real();
        ElectricDamageTimeout = reader.Int();
        reader.Bool();
        IsActivateLocked = reader.Bool();
        SavesUsed = reader.Int();
        IsSwimming = reader.Bool();
        reader.Bool();
        reader.Bool();
        reader.Bool();
        WeaponStatus = (Igi2PlayerWeaponStatus)reader.Int();
        GrenadeTickTime = reader.Int();
        GrenadeHeld = reader.Int();
        MovementSpeedScale = reader.Real();
        reader.Array();
        reader.Int();
        PerfectShotTimer = reader.Int();
        reader.Bool();
        reader.Bool();
        reader.Bool();
        IsUnlimitedAmmo = reader.Bool();
        IsFire = reader.Bool();
        IsFirstFire = reader.Bool();
        JumpWaitTimer = reader.Int();
        CrossHairDamping = reader.Real();
        TbaTimeout = reader.Int();
        AfterLoad(extras, (actionFlags, previousAction, action));
    }

    // The rest of HumanPlayer_DeSerializeHandler, in its order: not quitting the computer; the heading free; no speed carried
    // from before the load (its legs', its take-off's, its last move's); not dying, its mark a shadow again; its weapon let go and
    // silent, its timed action's and slide's sounds gone, and back in hand -- binoculars or goggles give way to the primary weapon
    // -- with no upper-body clip; still; the action menu's history forgotten; the action machine idle; and the body in the stance
    // it was saved in, standing, crouching or lying, whatever state it was saved in.
    private void AfterLoad(Igi2HumanSaveExtras saved, (uint Signal, int Previous, int State) action)
    {
        IsQuitComputer = false;
        IsLockOrientation = false;
        View.IsLockOrientation = false;
        GroundMovementSpeed = default;
        ForgetCarriedSpeedsForLoad();
        IsBloodPuddle = false;
        Shadow.BecomeShadow();
        BoneDeltaMovement = default;
        BoneCollisionDelta = default;
        IsDeathPositionStabilized = false;
        if (Weapon.Firing is { } firing)
        {
            firing.ReleaseAutoFire();
            firing.Effects.StopAllSounds();
        }

        if (Weapon.Inventory is { } inventory && inventory.Current?.WeaponType is { } held)
        {
            Weapons.Igi2WeaponTypeTask? wanted = held.WeaponCategory is Weapons.Igi2WeaponCategory.Binoculars or Weapons.Igi2WeaponCategory.ThermoChemical
                ? inventory.PrimaryWeapon()
                : held;
            if (wanted is not null && inventory.FindWeapon(wanted.WeaponId) is { } item)
            {
                inventory.Activate(item);
            }
        }

        _timedActionSound?.Delete();
        _timedActionSound = null;
        CurrentUpperBody = 0;

        ActionMenu.ClearHistory();
        EnterLoadedState(Action, Igi2PlayerActionState.Idle, action.Signal, action.Previous, action.State);
        int stance = Pose switch
        {
            Igi2HumanPose.Crouching => Igi2PlayerState.Crouch,
            Igi2HumanPose.LyingFlat => Igi2PlayerState.Flat,
            _ => Igi2PlayerState.Stand,
        };
        EnterLoadedState(stance, saved);
    }
}
