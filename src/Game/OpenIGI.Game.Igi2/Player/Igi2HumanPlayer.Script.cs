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
    /// Puts the player back in its default states, <c>HumanPlayer_GoToDefaultState</c> (humanplayer.c): what a timed action locked
    /// is let go, the activation ends, the locomotion stands and the hands go idle, each through its state's exit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The original clears every flag a timed action's init sets (<c>HumanPlayer_InitStateActivateGenericTBA</c>): the action lock,
    /// the view's and the body's orientation locks, the interaction camera (back to mode 1), the progress bar and the delta-scale
    /// lock; and it deletes the timed action's and the slide wire's sounds. Then the activation ends (<c>isCheckActivateButton</c>
    /// set, the target dropped).
    /// </para>
    /// <para>
    /// A script pops the player out of a cutscene this way -- location2/level6 after the capture, location3/level4 as its midtro
    /// begins -- and both scenes start as the player begins hacking a computer. Ending only the activation left the player
    /// walking in the hacking camera with its view locked and its action locked, so it could neither look about nor shoot.
    /// </para>
    /// <para>
    /// The script's own activation lock (<c>HumanPlayer_LockActivate</c>, a byte of the player's own) is a different one and stays.
    /// </para>
    /// </remarks>
    public void GoToDefaultState()
    {
        IsLockAction = false;
        View.IsLockOrientation = false;
        IsLockOrientation = false;
        EndActivate();
        CameraMode = 1;
        IsProgressBar = false;
        IsDisableDeltaTranslationScale = false;
        _slideSound?.Delete();
        _slideSound = null;
        _timedActionSound?.Delete();
        _timedActionSound = null;
        Go(Igi2PlayerState.Stand);
        GoAction(Igi2PlayerActionState.Idle);
    }
}
