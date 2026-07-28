namespace OpenIGI.Game.Menus;

/// <summary>
/// Everything the menu manager needs from the shell around it: this tick's input, a way to measure and
/// draw a screen, and somewhere to send a sound or a decision to leave.
/// </summary>
/// <remarks>
/// <para>
/// The manager task owns the menu's per-tick logic, because that is where the original puts it —
/// <c>MenuManager_Update</c> (<c>0x418080</c>) is the class's update method and runs off the task
/// tree's walk. Anything platform-shaped that it needs comes through here, which keeps
/// <c>OpenIGI.Game</c> free of a window, a device or a font.
/// </para>
/// <para>
/// <see cref="PrepareScreen"/> exists because the line-up cannot run until every item knows its size,
/// and for a text entry that means resolving its label and measuring it in its own font. The original
/// does the same thing in its measure pass.
/// </para>
/// </remarks>
public interface IMenuHost
{
    /// <summary>Reads this tick's pointer.</summary>
    /// <param name="x">Receives the horizontal position in authored pixels.</param>
    /// <param name="y">Receives the vertical position in authored pixels.</param>
    /// <param name="buttonDown">Receives whether the select button is held.</param>
    /// <returns><see langword="false"/> when there is no pointer, which leaves the selection to the keys.</returns>
    bool TryGetPointer(out int x, out int y, out bool buttonDown);

    /// <summary>Drains this tick's input events.</summary>
    /// <returns>The events, in the order they happened.</returns>
    /// <remarks>The original drains a queue of these; an empty sequence is the normal case.</remarks>
    IEnumerable<MenuInputEvent> DrainEvents();

    /// <summary>Resolves and measures a screen's contents so the line-up can place them.</summary>
    /// <param name="screen">The screen about to be shown.</param>
    void PrepareScreen(MenuScreenTask screen);

    /// <summary>Draws a screen.</summary>
    /// <param name="screen">The screen to draw.</param>
    void DrawScreen(MenuScreenTask screen);

    /// <summary>Plays one of the menu's sounds.</summary>
    /// <param name="name">The sound resource, <c>menu_high</c> when the selection moves and <c>menu_sel</c> when an entry is chosen.</param>
    void PlaySound(string name);

    /// <summary>Reports that the manager wants to leave the menus.</summary>
    /// <param name="state">Which of the leave states it entered.</param>
    void LeaveMenus(MenuManagerState state);
}
