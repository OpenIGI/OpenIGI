using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Saves;

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
    public void WritePlayerState(Igi2StateWriter writer)
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

    /// <summary>Reads the player's save state back; its action state is entered afresh.</summary>
    /// <param name="reader">The reader.</param>
    public void ReadPlayerState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Igi2HumanSaveExtras extras = ReadHumanState(reader);
        IsWeaponReady = extras.IsWeaponReady;
        IsDisableDeltaTranslationScale = extras.IsDisableDeltaTranslationScale;
        IsCheckActivateButton = extras.IsCheckActivateButton;
        TimesAlarmTriggered = extras.TimesAlarmTriggered;
        MissionHealth = extras.Health;

        uint actionFlags = unchecked((uint)reader.Int());
        reader.Int();
        reader.Int();
        reader.Int();
        int action = reader.Int();
        reader.Array();
        if (action >= 0 && action < Action.StateCount)
        {
            Action.Enter(action, this);
        }

        Action.Flags = actionFlags;
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
    }
}
