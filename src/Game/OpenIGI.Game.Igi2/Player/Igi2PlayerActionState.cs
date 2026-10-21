namespace OpenIGI.Game.Igi2.Player;

/// <summary>The player's action-machine states, by their slot in the action table at <c>igi2 0x63D794</c>.</summary>
public static class Igi2PlayerActionState
{
    /// <summary>Locked: no action may start (<c>HumanPlayer_InitActionLock</c>, <c>0x4B3630</c>).</summary>
    public const int Lock = 0;

    /// <summary>Idle: ready for the next action (<c>HumanPlayer_InitActionIdle</c>, <c>0x4B3760</c>).</summary>
    public const int Idle = 1;

    /// <summary>A shot starting (<c>HumanPlayer_InitActionInitFire</c>, <c>0x4B5060</c>).</summary>
    public const int InitFire = 2;

    /// <summary>Firing (<c>HumanPlayer_InitActionFire</c>, <c>0x4B5830</c>).</summary>
    public const int Fire = 3;

    /// <summary>Letting go of the trigger (<c>HumanPlayer_InitActionFireRelease</c>, <c>0x4B5850</c>).</summary>
    public const int FireRelease = 4;

    /// <summary>Reloading (<c>HumanPlayer_InitActionReload</c>, <c>0x4B5C70</c>).</summary>
    public const int Reload = 5;

    /// <summary>Changing weapon (<c>HumanPlayer_InitActionChangeWeapon</c>, <c>0x4B7180</c>).</summary>
    public const int ChangeWeapon = 6;

    /// <summary>Peeking (<c>HumanPlayer_InitActionPeek</c>, <c>0x4B7C30</c>).</summary>
    public const int Peek = 7;

    /// <summary>At the computer (<c>HumanPlayer_InitActionComputer</c>, <c>0x4B7E60</c>).</summary>
    public const int Computer = 8;

    /// <summary>Firing with nothing to fire (<c>HumanPlayer_InitActionFireEmpty</c>, <c>0x4B5940</c>).</summary>
    public const int FireEmpty = 9;

    /// <summary>Throwing a grenade (<c>HumanPlayer_InitActionThrowGrenade</c>, <c>0x4B4D60</c>).</summary>
    public const int ThrowGrenade = 10;

    /// <summary>How many slots the table has.</summary>
    public const int Count = 11;
}
