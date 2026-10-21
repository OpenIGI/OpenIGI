namespace OpenIGI.Game.Igi2.Saves;

/// <summary>
/// What the menus' save natives answer from, kept for the session: where the slots are, whose they are, and the slot the menu asked to
/// load as the next level starts (<c>Game_nMenuLoadFromSlot</c>, <c>-1</c> for none).
/// </summary>
public sealed class Igi2SaveSession
{
    /// <summary>Gets or sets where the slots are, once a level has said.</summary>
    public Igi2SaveSlots? Slots { get; set; }

    /// <summary>Gets or sets whose saves the menus list.</summary>
    public Igi2SaveOwner Owner { get; set; }

    /// <summary>Gets or sets the slot the menu asked to load as the next level starts, or <c>-1</c>.</summary>
    public int MenuLoadSlot { get; set; } = -1;

    /// <summary>Gets or sets the localized text of a key, for the load list's lines.</summary>
    public Func<string, string> Translate { get; set; } = static key => key;

    /// <summary>
    /// Takes the slot the menu asked for, <c>Game_SetMenuLoadFromSlotCB</c>: the restart line's id asks for none.
    /// </summary>
    /// <param name="slot">The slot, or <see cref="Igi2SaveSlots.RestartMission"/>.</param>
    public void SetMenuLoadSlot(int slot) => MenuLoadSlot = slot == Igi2SaveSlots.RestartMission ? -1 : slot;

    /// <summary>Takes back the slot the menu asked for, leaving none, as the level that loads it starts.</summary>
    /// <returns>The slot, or <c>-1</c>.</returns>
    public int TakeMenuLoadSlot()
    {
        int slot = MenuLoadSlot;
        MenuLoadSlot = -1;
        return slot;
    }
}
