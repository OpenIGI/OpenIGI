
namespace OpenIGI.Game.Igi1.Menus;

/// <summary>
/// A key the manager has posted to the selected window for it to act on.
/// </summary>
/// <remarks>
/// <para>
/// The manager does not act on left, right or enter itself. <c>MenuManager_HandleKeys</c>
/// (<c>0x418580</c>) moves the selection on up and down, and for the other three writes the raw key
/// code into the <em>selected window's</em> <c>+80</c>. Each control type then consumes it in its own
/// update — which is how a slider knows to step, a toggle knows to flip, and an entry knows it was
/// chosen, all from the same three keys.
/// </para>
/// <para>
/// The values are the original's key codes, kept as-is because they are what the field holds.
/// </para>
/// </remarks>
public enum MenuWindowCommand
{
    /// <summary>Nothing posted.</summary>
    None = 0,

    /// <summary>Left. A slider steps down; a toggle turns on.</summary>
    Left = -1,

    /// <summary>Right. A slider steps up; a toggle turns off.</summary>
    Right = -2,

    /// <summary>Enter. An entry runs its <c>On Click Script</c>.</summary>
    Choose = -7,
}
