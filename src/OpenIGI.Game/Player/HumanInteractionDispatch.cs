using OpenIGI.Game.World;

namespace OpenIGI.Game.Player;

/// <summary>What the interaction dispatcher decides "use" should do.</summary>
public enum InteractionAction
{
    /// <summary>Nothing — the type is not one the dispatcher acts on.</summary>
    None,

    /// <summary>Request a ladder climb; the mount runs next tick (dispatcher case 10).</summary>
    ClimbLadder,

    /// <summary>Open a door outright (dispatcher case 3, unlocked branch).</summary>
    OpenDoor,

    /// <summary>Pick a locked door's lock — enter the <c>picklock_loop</c> state (case 3, locked branch).</summary>
    PickLock,

    /// <summary>Vault or mantle a fence (dispatcher case 9).</summary>
    VaultFence,

    /// <summary>Pull a drawer open (dispatcher case 2).</summary>
    OpenDrawer,

    /// <summary>Enter a self-driving interactive locomotion state (cases 4–7, 12, 13).</summary>
    EnterState,
}

/// <summary>The dispatcher's decision: an action, and the locomotion state it enters when it enters one.</summary>
/// <param name="Action">What to do.</param>
/// <param name="LocomotionState">The state to enter, or <c>-1</c> when the action enters none.</param>
public readonly record struct InteractionDispatchResult(InteractionAction Action, int LocomotionState);

/// <summary>
/// The human's interaction dispatcher — IGI1 <c>sub_40CD30</c>, the "state-34 dispatch" that turns the
/// resolved activatable's <c>GetActivate</c> type into a locomotion state or an immediate action.
/// </summary>
/// <remarks>
/// Called from the action handler <c>0x40E9C0</c> when the player is idle (<c>human+3404 == -1</c>) and
/// the use input (<c>human+468</c>, <see cref="PlayerAction.Activate"/>) is held over a resolved
/// activatable.
/// </remarks>
public static class HumanInteractionDispatch
{
    /// <summary>The <c>picklock_loop</c> state a locked door enters.</summary>
    public const int PickLockState = 30;

    /// <summary>The <c>typecomp_loop</c> state a computer / keypad enters.</summary>
    public const int ComputerState = 32;

    /// <summary>Dispatches a resolved activatable by its type.</summary>
    /// <param name="type">The activatable's <c>GetActivate</c> type.</param>
    /// <param name="doorLocked">Whether a <see cref="InteractionActivateType.Door"/> is locked.</param>
    /// <returns>The action to take and the state it enters.</returns>
    public static InteractionDispatchResult Dispatch(InteractionActivateType type, bool doorLocked = false) =>
        Dispatch((int)type, doorLocked);

    /// <summary>Dispatches by the raw type value, so types not yet named still resolve.</summary>
    /// <param name="type">The <c>GetActivate</c> value the dispatcher's <c>switch</c> tests.</param>
    /// <param name="doorLocked">Whether a door (type 3) is locked.</param>
    /// <returns>The action to take and the state it enters.</returns>
    public static InteractionDispatchResult Dispatch(int type, bool doorLocked = false) => type switch
    {
        // case 3: a door opens outright, or picks its lock into state 30 when locked.
        (int)InteractionActivateType.Door =>
            doorLocked
                ? new InteractionDispatchResult(InteractionAction.PickLock, PickLockState)
                : new InteractionDispatchResult(InteractionAction.OpenDoor, -1),

        // case 10: a ladder requests a climb; case 9: a fence vaults; case 2: a drawer opens.
        (int)InteractionActivateType.Ladder => new(InteractionAction.ClimbLadder, -1),
        (int)InteractionActivateType.Fence => new(InteractionAction.VaultFence, -1),
        (int)InteractionActivateType.Drawer => new(InteractionAction.OpenDrawer, -1),

        // cases 4-7, 12, 13: the self-driving interactive states. Case 5 is the computer/keypad.
        4 => new(InteractionAction.EnterState, 31),
        5 => new(InteractionAction.EnterState, ComputerState),
        6 => new(InteractionAction.EnterState, 33),
        7 => new(InteractionAction.EnterState, 34),
        12 => new(InteractionAction.EnterState, 35),
        13 => new(InteractionAction.EnterState, 36),

        _ => new(InteractionAction.None, -1),
    };
}
