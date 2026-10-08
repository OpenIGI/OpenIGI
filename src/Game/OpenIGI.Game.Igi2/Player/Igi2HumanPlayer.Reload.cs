using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Player;

/// <content>
/// Reloading: the action machine's reload state (<c>HumanPlayer_apfAction</c> 5) and its three steps, the start, the
/// loop and the end, each on the upper body and the first-person model (upper-body modes 8, 9 and 10).
/// </content>
/// <remarks>
/// The rounds go in when a step's clip has played: straight after the start for a weapon with no loop, on the way into
/// the loop or the end otherwise, and again each time round the loop. Not yet: the AI hearing the weapon handled
/// (<c>HumanPlayer_SendHandleWeaponAIEvent</c>, a range of 20480) at each.
/// </remarks>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>Starts a reload: the primary, the start on the upper body (<c>HumanPlayer_InitActionReload</c>, <c>igi2 0x4B5C70</c>).</summary>
    private void EnterReload()
    {
        FireType = 0;
        SetUpperBody(8);
        Action.Current = (_, _) => RunReload();
    }

    /// <summary>The reload's start, <c>HumanPlayer_RunActionReload</c> (<c>igi2 0x4B5F20</c>).</summary>
    /// <remarks>
    /// <para>
    /// Reloading keeps the perfect-shot timer at nothing. With nothing in hand the player is idle; a weapon change asked
    /// for takes the zoom off and changes. Otherwise it waits for the start's first-person clip.
    /// </para>
    /// <para>
    /// A weapon with a first-person reload loop goes to the loop, or, with fire held or one round short of a load, to the
    /// end, and reloads on the way. One without reloads and is idle.
    /// </para>
    /// </remarks>
    private void RunReload()
    {
        PerfectShotTimer = 0;
        if (HeldItem is not { } held)
        {
            GoAction(Igi2PlayerActionState.Idle);
            return;
        }

        if (ChangeWeaponId != -1)
        {
            View.DisableZoomMode();
            GoAction(Igi2PlayerActionState.ChangeWeapon);
            return;
        }

        if ((ActionSignal & Igi2PlayerSignal.AnimationEnded) == 0)
        {
            return;
        }

        if (Weapon.GetAnim(Igi2WeaponAnimRequest.FirstReloadLoop, View.ObjectRig) is null)
        {
            Weapon.Firing?.Reload();
            GoAction(Igi2PlayerActionState.Idle);
            return;
        }

        Igi2FireTypeRounds rounds = Weapon.Inventory!.FireTypeRounds(held, 0);
        if ((ActionSignal & Igi2PlayerSignal.Fire) == 0 && rounds.Loaded != rounds.RoundsPerClip - 1)
        {
            SetUpperBody(9);
            Action.Current = (_, _) => RunReloadLoop();
        }
        else
        {
            EndReload();
        }

        Weapon.Firing?.Reload();
    }

    /// <summary>The reload's loop, a round at a time, <c>HumanPlayer_RunActionReloadLoop</c> (<c>igi2 0x4B65E0</c>).</summary>
    /// <remarks>
    /// <para>
    /// A death goes to the end at once. A weapon change asked for takes the zoom off and changes. Otherwise, each time the
    /// loop's clip has played the player reloads, ending with fire held, with no more than one round carried, or one round
    /// short of a load, and looping again otherwise.
    /// </para>
    /// </remarks>
    private void RunReloadLoop()
    {
        PerfectShotTimer = 0;
        if ((Signal & Igi2PlayerSignal.Dead) != 0)
        {
            EndReload();
        }

        if (ChangeWeaponId != -1)
        {
            View.DisableZoomMode();
            GoAction(Igi2PlayerActionState.ChangeWeapon);
            return;
        }

        if ((ActionSignal & Igi2PlayerSignal.AnimationEnded) == 0 || HeldItem is not { } held)
        {
            return;
        }

        Igi2FireTypeRounds rounds = Weapon.Inventory!.FireTypeRounds(held, 0);
        if ((ActionSignal & Igi2PlayerSignal.Fire) != 0 || rounds.Available <= 1 || rounds.Loaded == rounds.RoundsPerClip - 1)
        {
            EndReload();
        }
        else
        {
            SetUpperBody(9);
        }

        Weapon.Firing?.Reload();
    }

    /// <summary>The reload's end on the upper body, then waits for it.</summary>
    private void EndReload()
    {
        SetUpperBody(10);
        Action.Current = (_, _) => RunReloadEnd();
    }

    /// <summary>Once the end's clip has played, idle (<c>HumanPlayer_RunActionReloadEnd</c>, <c>igi2 0x4B7130</c>).</summary>
    private void RunReloadEnd()
    {
        PerfectShotTimer = 0;
        if ((ActionSignal & Igi2PlayerSignal.AnimationEnded) != 0)
        {
            GoAction(Igi2PlayerActionState.Idle);
        }
    }
}
