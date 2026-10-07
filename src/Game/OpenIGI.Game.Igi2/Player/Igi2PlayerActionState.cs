namespace OpenIGI.Game.Igi2.Player;

/// <summary>The player's action-machine states, by their slot in the action table at <c>igi2 0x63D794</c>.</summary>
public static class Igi2PlayerActionState
{
    /// <summary>Locked: no action may start (<c>HumanPlayer_InitActionLock</c>, <c>0x4B3630</c>).</summary>
    public const int Lock = 0;

    /// <summary>Idle: ready for the next action (<c>HumanPlayer_InitActionIdle</c>, <c>0x4B3760</c>).</summary>
    public const int Idle = 1;
}
