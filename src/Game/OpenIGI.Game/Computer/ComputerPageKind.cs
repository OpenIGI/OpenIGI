using OpenIGI.Game.Player;

namespace OpenIGI.Game.Computer;

/// <summary>A page of the player's computer that a key opens it on, in either game.</summary>
public enum ComputerPageKind
{
    /// <summary>No page asked for.</summary>
    None,

    /// <summary>The map.</summary>
    Map,

    /// <summary>The objectives.</summary>
    Objectives,

    /// <summary>The log of messages.</summary>
    Log,
}

/// <summary>The keys that open the computer straight on one of its text pages.</summary>
/// <remarks>
/// I.G.I. 2's rule (<c>HumanPlayer_IsActivateComputer</c> and the computer state's run): a page key opens the computer on its
/// page, turns an open computer to it, and closes a computer already showing it.
/// </remarks>
public static class ComputerPageKeys
{
    /// <summary>Each page key's action and the page it asks for.</summary>
    public static IReadOnlyList<(PlayerAction Action, PlayerInputSlot Slot, ComputerPageKind Page)> All { get; } =
    [
        (PlayerAction.ComputerObjectives, PlayerInputSlot.ComputerObjectives, ComputerPageKind.Objectives),
        (PlayerAction.ComputerLog, PlayerInputSlot.ComputerLog, ComputerPageKind.Log),
    ];

    /// <summary>The page a set of actions asks for: the first page key among them, or none.</summary>
    /// <param name="actions">The actions, held or pressed.</param>
    /// <returns>The page, or <see cref="ComputerPageKind.None"/>.</returns>
    public static ComputerPageKind Asked(PlayerAction actions)
    {
        foreach ((PlayerAction action, PlayerInputSlot _, ComputerPageKind page) in All)
        {
            if ((actions & action) != 0)
            {
                return page;
            }
        }

        return ComputerPageKind.None;
    }

    /// <summary>The page a tick's input asks for: the first page key that went down on it, or none.</summary>
    /// <param name="input">The tick's input.</param>
    /// <returns>The page, or <see cref="ComputerPageKind.None"/>.</returns>
    public static ComputerPageKind Asked(PlayerInputState input)
    {
        ArgumentNullException.ThrowIfNull(input);
        foreach ((PlayerAction _, PlayerInputSlot slot, ComputerPageKind page) in All)
        {
            if (input.IsRisingEdge(slot))
            {
                return page;
            }
        }

        return ComputerPageKind.None;
    }
}
