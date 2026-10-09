using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <summary>The soldier's upper-body handler, <c>tUpperBodyState.pfSignalHandler</c>.</summary>
public enum Igi2SoldierUpperBody
{
    /// <summary><c>HumanSoldier_IdleUpperBodySignalHandler</c> (<c>igi2 0x4E3CC0</c>).</summary>
    Idle,

    /// <summary><c>HumanSoldier_FireUpperBodySignalHandler</c> (<c>igi2 0x4E3530</c>).</summary>
    Fire,

    /// <summary><c>HumanSoldier_AimUpperBodySignalHandler</c> (<c>igi2 0x4E36E0</c>).</summary>
    Aim,

    /// <summary><c>HumanSoldier_ReloadUpperBodySignalHandler</c> (<c>igi2 0x4E38F0</c>).</summary>
    Reload,

    /// <summary><c>HumanSoldier_TenseUpperBodySignalHandler</c> (<c>igi2 0x4E3A00</c>).</summary>
    Tense,

    /// <summary><c>HumanSoldier_HitUpperBodySignalHandler</c> (<c>igi2 0x4E3AF0</c>).</summary>
    Hit,

    /// <summary><c>HumanSoldier_RaiseWeaponUpperBodySignalHandler</c> (<c>igi2 0x4E3BC0</c>).</summary>
    RaiseWeapon,
}

/// <content>The soldier's upper body: what its arms and weapon do over its legs' clip.</content>
public sealed partial class Igi2HumanSoldier
{
    /// <summary>The upper-body row the idle arms play.</summary>
    public const int UpperIdle = 0;

    /// <summary>The upper-body row a shot plays.</summary>
    public const int UpperFire = 10;

    /// <summary>The upper-body row a reload plays.</summary>
    public const int UpperReload = 11;

    /// <summary>The upper-body row aiming plays.</summary>
    public const int UpperAim = 15;

    /// <summary>The upper-body row raising the weapon plays.</summary>
    public const int UpperRaise = 16;

    /// <summary>The upper-body row a tense soldier holds.</summary>
    public const int UpperTense = 102;

    /// <summary>The sounds raising the weapon makes, one drawn at random.</summary>
    public static IReadOnlyList<string> ChangeWeaponSounds { get; } = ["changeweapon_01", "changeweapon_02", "changeweapon_03", "changeweapon_04"];

    /// <summary>A tick's ticks in a second's share, <c>0x395A740E</c>: one 4800th.</summary>
    private const float ClipTicksToSeconds = 0.00020833334f;

    /// <summary>Runs the upper body's handler.</summary>
    private void RunUpperBody()
    {
        switch (UpperBodyState)
        {
            case Igi2SoldierUpperBody.Idle:
                RunIdleUpperBody();
                break;
            case Igi2SoldierUpperBody.Fire:
                RunFireUpperBody();
                break;
            case Igi2SoldierUpperBody.Aim:
                RunAimUpperBody();
                break;
            case Igi2SoldierUpperBody.Reload:
                RunReloadUpperBody();
                break;
            case Igi2SoldierUpperBody.Tense:
                RunTenseUpperBody();
                break;
            case Igi2SoldierUpperBody.Hit:
                RunHitUpperBody();
                break;
            case Igi2SoldierUpperBody.RaiseWeapon:
                RunRaiseWeaponUpperBody();
                break;
        }
    }

    /// <summary>Whether the upper body may play at all: not kept off, alive and not lying.</summary>
    private bool IsUpperBodyFree => (UpperBodyFlags & 1) == 0 && LifeState == 0 && Pose != Igi2HumanPose.LyingFlat;

    /// <summary>Plays an upper-body row and moves to a handler.</summary>
    private void PlayUpper(int row, Igi2SoldierUpperBody next)
    {
        Animation.PlayUpperBodyState(row, 0, 0f);
        UpperBodyState = next;
    }

    /// <summary>Clears the upper body and goes back to idle.</summary>
    private void BackToIdle()
    {
        Animation.ClearUpperBody(0f);
        UpperBodyState = Igi2SoldierUpperBody.Idle;
    }

    /// <summary>Lets go of the primary trigger and the fire type, as the fire handler does.</summary>
    private void ReleasePrimary()
    {
        if (Weapon.Firing is { } firing)
        {
            firing.FireTypeState(0).IsReleaseFire = true;
            firing.FireType = 0;
        }
    }

    /// <summary>
    /// The idle arms, <c>HumanSoldier_IdleUpperBodySignalHandler</c> (<c>igi2 0x4E3CC0</c>).
    /// </summary>
    /// <remarks>
    /// Off when the upper body may not play. Otherwise, off a mounted gun and once every blend has finished: a hit clears the
    /// arms for the hit handler, a reload plays, tension or aiming raises the weapon with one of four sounds (and steps the
    /// main clip at twice the raise's length in seconds a frame), and with no upper-body clip at all the idle arms start,
    /// blended in over a second once the mind has started.
    /// </remarks>
    private void RunIdleUpperBody()
    {
        Igi2SoldierSignal s = Signal;
        if (!IsUpperBodyFree)
        {
            Animation.ClearUpperBody(0f);
            return;
        }

        if (Locomotion.CurrentState == Igi2SoldierState.StationaryGun || !Animation.IsBlendFinished)
        {
            return;
        }

        if ((s & Igi2SoldierSignal.Hit) != 0)
        {
            Animation.ClearUpperBody(0f);
            UpperBodyState = Igi2SoldierUpperBody.Hit;
        }
        else if ((s & Igi2SoldierSignal.Reload) != 0)
        {
            PlayUpper(UpperReload, Igi2SoldierUpperBody.Reload);
        }
        else if ((s & (Igi2SoldierSignal.Tense | Igi2SoldierSignal.Aim)) != 0)
        {
            Surroundings.PlaySound(ChangeWeaponSounds[(int)Random.NextBounded(4)]);
            Animation.PlayUpperBodyState(UpperRaise, 0, 0f);
            UpperBodyState = Igi2SoldierUpperBody.RaiseWeapon;
            if (Animation.UpperBodyTotalTime is int total)
            {
                Fpu ticks = (Fpu)(double)total * ClipTicksToSeconds;
                float scale = (ticks + ticks).F;
                Animation.MainTimeStep = Fpu.ToInt32((Fpu)(double)Igi2AnimController.TicksPerFrame * scale);
            }
        }
        else if (Animation.UpperBodyTotalTime is null)
        {
            Animation.PlayUpperBodyState(UpperIdle, 0, Mind is { HasStarted: true } ? 1f : 0f);
        }
    }

    /// <summary>
    /// Firing, <c>HumanSoldier_FireUpperBodySignalHandler</c> (<c>igi2 0x4E3530</c>): the trigger held while the fire channel is.
    /// </summary>
    private void RunFireUpperBody()
    {
        Igi2SoldierSignal s = Signal;
        if (Animation.UpperBodyTotalTime is null || !Weapon.IsHolding || !IsUpperBodyFree)
        {
            if (Weapon.Firing is not null)
            {
                ReleasePrimary();
            }

            BackToIdle();
            return;
        }

        if ((s & Igi2SoldierSignal.Dead) != 0)
        {
            ReleasePrimary();
            BackToIdle();
            return;
        }

        if ((s & Igi2SoldierSignal.Hit) != 0)
        {
            ReleasePrimary();
            UpperBodyState = Igi2SoldierUpperBody.Hit;
            return;
        }

        if ((s & Igi2SoldierSignal.UpperBodyEnded) != 0)
        {
            Animation.PlayUpperBodyState(UpperFire, 0, 0f);
        }

        if ((s & Igi2SoldierSignal.Fire) != 0)
        {
            Weapon.Fire(0);
            return;
        }

        ReleasePrimary();
        if ((s & Igi2SoldierSignal.Aim) != 0)
        {
            PlayUpper(UpperAim, Igi2SoldierUpperBody.Aim);
        }
        else if ((s & Igi2SoldierSignal.Reload) != 0)
        {
            PlayUpper(UpperReload, Igi2SoldierUpperBody.Reload);
        }
        else if ((s & Igi2SoldierSignal.Tense) != 0)
        {
            PlayUpper(UpperTense, Igi2SoldierUpperBody.Tense);
        }
        else
        {
            BackToIdle();
        }
    }

    /// <summary>Aiming, <c>HumanSoldier_AimUpperBodySignalHandler</c> (<c>igi2 0x4E36E0</c>).</summary>
    /// <remarks>
    /// Lowering the weapon, for whatever reason, sounds one of the four change sounds. A hit lowers it too: the handler tests the
    /// hit and dead bits together first, so its own hit branch never runs, nor does the reload handler's.
    /// </remarks>
    private void RunAimUpperBody()
    {
        Igi2SoldierSignal s = Signal;
        if (Animation.UpperBodyTotalTime is null || (s & (Igi2SoldierSignal.Hit | Igi2SoldierSignal.Dead)) != 0
            || Pose == Igi2HumanPose.LyingFlat || (s & (Igi2SoldierSignal.Tense | Igi2SoldierSignal.Aim)) == 0 || LifeState != 0
            || (UpperBodyFlags & 1) != 0)
        {
            Surroundings.PlaySound(ChangeWeaponSounds[(int)(Random.NextRaw() & 3)]);
            BackToIdle();
        }
        else if ((s & Igi2SoldierSignal.Fire) != 0)
        {
            PlayUpper(UpperFire, Igi2SoldierUpperBody.Fire);
        }
        else if ((s & Igi2SoldierSignal.Reload) != 0)
        {
            PlayUpper(UpperReload, Igi2SoldierUpperBody.Reload);
        }
    }

    /// <summary>
    /// Reloading, <c>HumanSoldier_ReloadUpperBodySignalHandler</c> (<c>igi2 0x4E38F0</c>): when the reload's clip ends the weapon
    /// reloads and the arms go on to what is asked.
    /// </summary>
    /// <remarks>The weapon's own reload (<c>Weapon_Reload</c>, <c>igi2 0x51E120</c>) is not ported yet.</remarks>
    private void RunReloadUpperBody()
    {
        Igi2SoldierSignal s = Signal;
        if (Animation.UpperBodyTotalTime is null || (s & (Igi2SoldierSignal.Hit | Igi2SoldierSignal.Dead)) != 0
            || Pose == Igi2HumanPose.LyingFlat || LifeState != 0 || (UpperBodyFlags & 1) != 0)
        {
            BackToIdle();
            return;
        }

        if ((s & Igi2SoldierSignal.UpperBodyEnded) == 0)
        {
            return;
        }

        if ((s & Igi2SoldierSignal.Fire) != 0)
        {
            PlayUpper(UpperFire, Igi2SoldierUpperBody.Fire);
        }
        else if ((s & Igi2SoldierSignal.Aim) != 0)
        {
            PlayUpper(UpperAim, Igi2SoldierUpperBody.Aim);
        }
        else if ((s & Igi2SoldierSignal.Tense) != 0)
        {
            PlayUpper(UpperTense, Igi2SoldierUpperBody.Tense);
        }
        else
        {
            BackToIdle();
        }
    }

    /// <summary>Holding tense, <c>HumanSoldier_TenseUpperBodySignalHandler</c> (<c>igi2 0x4E3A00</c>).</summary>
    private void RunTenseUpperBody()
    {
        Igi2SoldierSignal s = Signal;
        if ((s & Igi2SoldierSignal.Dead) != 0 || Animation.UpperBodyTotalTime is null || (s & Igi2SoldierSignal.Tense) == 0
            || Pose == Igi2HumanPose.LyingFlat || LifeState != 0 || (UpperBodyFlags & 1) != 0)
        {
            BackToIdle();
        }
        else if ((s & Igi2SoldierSignal.Hit) != 0)
        {
            UpperBodyState = Igi2SoldierUpperBody.Hit;
        }
        else if ((s & Igi2SoldierSignal.Fire) != 0)
        {
            PlayUpper(UpperFire, Igi2SoldierUpperBody.Fire);
        }
        else if ((s & Igi2SoldierSignal.Aim) != 0)
        {
            PlayUpper(UpperAim, Igi2SoldierUpperBody.Aim);
        }
        else if ((s & Igi2SoldierSignal.Reload) != 0)
        {
            PlayUpper(UpperReload, Igi2SoldierUpperBody.Reload);
        }
        else if ((s & Igi2SoldierSignal.UpperBodyEnded) != 0)
        {
            Animation.PlayUpperBodyState(UpperTense, 0, 0f);
        }
    }

    /// <summary>
    /// After a hit, <c>HumanSoldier_HitUpperBodySignalHandler</c> (<c>igi2 0x4E3AF0</c>): waits for the main clip to end, then the
    /// arms go on to what is asked, or idle.
    /// </summary>
    private void RunHitUpperBody()
    {
        Igi2SoldierSignal s = Signal;
        if (Animation.UpperBodyTotalTime is not null && LifeState == 0 && (UpperBodyFlags & 1) == 0 && (s & Igi2SoldierSignal.MainEnded) == 0)
        {
            return;
        }

        if ((UpperBodyFlags & 1) == 0)
        {
            if ((s & Igi2SoldierSignal.Fire) != 0)
            {
                PlayUpper(UpperFire, Igi2SoldierUpperBody.Fire);
                return;
            }

            if ((s & Igi2SoldierSignal.Aim) != 0)
            {
                PlayUpper(UpperAim, Igi2SoldierUpperBody.Aim);
                return;
            }

            if ((s & Igi2SoldierSignal.Reload) != 0)
            {
                PlayUpper(UpperReload, Igi2SoldierUpperBody.Reload);
                return;
            }
        }

        Animation.ClearUpperBody(1f);
        UpperBodyState = Igi2SoldierUpperBody.Idle;
    }

    /// <summary>
    /// Raising the weapon, <c>HumanSoldier_RaiseWeaponUpperBodySignalHandler</c> (<c>igi2 0x4E3BC0</c>): when the raise ends the arms
    /// go on to what is asked.
    /// </summary>
    private void RunRaiseWeaponUpperBody()
    {
        Igi2SoldierSignal s = Signal;
        if (!IsUpperBodyFree)
        {
            UpperBodyState = Igi2SoldierUpperBody.Idle;
            Animation.ClearUpperBody(0f);
            return;
        }

        if ((s & Igi2SoldierSignal.UpperBodyEnded) == 0)
        {
            return;
        }

        if ((s & Igi2SoldierSignal.Reload) != 0)
        {
            PlayUpper(UpperReload, Igi2SoldierUpperBody.Reload);
        }
        else if ((s & Igi2SoldierSignal.Fire) != 0)
        {
            PlayUpper(UpperFire, Igi2SoldierUpperBody.Fire);
        }
        else if ((s & Igi2SoldierSignal.Aim) != 0)
        {
            PlayUpper(UpperAim, Igi2SoldierUpperBody.Aim);
        }
        else if ((s & Igi2SoldierSignal.Tense) != 0)
        {
            PlayUpper(UpperTense, Igi2SoldierUpperBody.Tense);
        }
        else
        {
            UpperBodyState = Igi2SoldierUpperBody.Idle;
        }
    }
}
