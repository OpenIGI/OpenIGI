namespace OpenIGI.Game.Menus;

/// <summary>
/// The abstract input codes the menu consumes, as the original numbers them.
/// </summary>
/// <remarks>
/// <para>
/// <c>Menu_HandleKeys</c> (<c>0x418580</c>) does not read scan codes. It drains an event queue of
/// small negative codes, so the same menu logic serves the keyboard, a pad or anything else the host
/// maps onto them. The values are negative because they share a space with positive codes the queue
/// also carries.
/// </para>
/// <para>
/// <see cref="Previous"/> and <see cref="Next"/> move the selection and are handled by the manager;
/// <see cref="Left"/>, <see cref="Right"/> and <see cref="Alternate"/> are forwarded to the selected
/// item, which is how a slider or a toggle takes input.
/// </para>
/// </remarks>
public enum MenuInputEvent
{
    /// <summary>No event.</summary>
    None = 0,

    /// <summary>Forwarded to the selected item. Decreases a slider or steps a list back.</summary>
    Left = -1,

    /// <summary>Forwarded to the selected item. Increases a slider or steps a list forward.</summary>
    Right = -2,

    /// <summary>Move the selection to the previous item, wrapping to the last.</summary>
    Previous = -3,

    /// <summary>Move the selection to the next item, wrapping to the first.</summary>
    Next = -4,

    /// <summary>Forwarded to the selected item as a secondary action.</summary>
    Alternate = -7,

    /// <summary>Choose the selected item.</summary>
    Enter = -8,

    /// <summary>Leave the current screen.</summary>
    Escape = -11,
}
