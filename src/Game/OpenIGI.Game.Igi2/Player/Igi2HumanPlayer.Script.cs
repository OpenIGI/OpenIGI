using OpenIGI.Game.Igi2.Interaction;

namespace OpenIGI.Game.Igi2.Player;

/// <content>What the level's scripts do to the player directly.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>
    /// Gets or sets the ticks before an electric fence may shock the player again, <c>nElectricDamageTimeOut</c>; a shock sets it to
    /// 45 and each tick counts it down.
    /// </summary>
    public int ElectricDamageTimeout { get; set; }

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
    /// The byte <c>HumanPlayer_LockActivate</c> writes, at <c>+3217</c> of the player: in the shipped game that is the second byte of
    /// the electric fence timeout, so locking holds shocks off for 256 ticks for each unit written, and unlocking clears that byte.
    /// </summary>
    /// <param name="value">The byte written.</param>
    public void LockActivate(int value) => ElectricDamageTimeout = (ElectricDamageTimeout & ~0xFF00) | ((value & 0xFF) << 8);

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
