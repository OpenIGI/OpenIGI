using OpenIGI.Game.Igi2.Interaction;

namespace OpenIGI.Game.Igi2.Player;

/// <content>What the level's scripts do to the player directly.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>
    /// Activates a task as though the player had found it, <c>HumanPlayer_ActivateScriptFunction</c> (humanplayer.c): it becomes what
    /// the view found, the zoom comes off and the activation starts.
    /// </summary>
    /// <param name="target">What to activate.</param>
    public void ScriptActivate(IIgi2Activatable target)
    {
        ArgumentNullException.ThrowIfNull(target);
        EvaluateTarget = target;
        View.DisableZoomMode();
        InitActivate(target);
    }

    /// <summary>
    /// Locks or unlocks the activate key, <c>HumanPlayer_LockActivateScriptFunction</c>: the byte written is
    /// <c>isPlayerActivateLocked</c>, and while it is set the key reads as up each tick once the activate update has run.
    /// </summary>
    /// <param name="value">The byte written.</param>
    public void LockActivate(int value) => IsActivateLocked = (byte)value != 0;

    /// <summary>
    /// Takes every weapon from the player but the knife, which goes in hand, <c>HumanPlayer_DisableAllWeapons</c>: each weapon carried
    /// is dropped with the rounds in the pocket for it, and the upper body's mode is cleared.
    /// </summary>
    public void DisableAllWeapons()
    {
        if (Weapon.Inventory is not { } inventory)
        {
            return;
        }

        foreach (Weapons.Igi2WeaponItem item in inventory.Weapons)
        {
            inventory.Disable(item, removeCarriedAmmo: true);
        }

        inventory.EnableWeaponType(Flow.Igi2LevelNatives.KnifeId, addAmmo: false);
        if (inventory.FindWeapon(Flow.Igi2LevelNatives.KnifeId) is { } knife)
        {
            inventory.Activate(knife);
        }

        CurrentUpperBody = 0;
    }

    /// <summary>
    /// Puts the player back in its default states, <c>HumanPlayer_GoToDefaultState</c> (humanplayer.c): the activation ends, the
    /// locomotion stands and the hands go idle, each through its state's exit.
    /// </summary>
    /// <remarks>
    /// The original also clears a handful of flags of its view and status screen and drops two tasks the player owns; the ones
    /// this port keeps elsewhere are reset by the states entered.
    /// </remarks>
    public void GoToDefaultState()
    {
        EndActivate();
        Go(Igi2PlayerState.Stand);
        GoAction(Igi2PlayerActionState.Idle);
    }
}
