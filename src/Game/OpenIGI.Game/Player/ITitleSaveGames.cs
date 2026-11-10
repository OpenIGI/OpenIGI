namespace OpenIGI.Game.Player;

/// <summary>A save slot a title offers: its number, whether it holds a save, and the line it is shown as.</summary>
/// <param name="Slot">The slot's number.</param>
/// <param name="IsUsed">Whether it holds a save of this mission.</param>
/// <param name="Description">What it holds, as the title words it, or empty for an empty slot.</param>
/// <param name="MissingMods">
/// The mods that change play the save was made with and that are not being played now; <see langword="null"/> for none.
/// </param>
/// <param name="When">When it was written, as the title words it, or empty for an empty slot.</param>
public readonly record struct TitleSaveSlot(
    int Slot, bool IsUsed, string Description, IReadOnlyList<OpenIGI.Core.Content.ModReference>? MissingMods = null, string When = "");

/// <summary>
/// The save games a title keeps of a running mission: its slots, how many saves the mission still allows, and saving to or loading
/// from a slot. The player's computer saves and loads them, as I.G.I. 2's does, and the pause menu lists them to load; the title
/// decides what they hold and how they are written.
/// </summary>
public interface ITitleSaveGames
{
    /// <summary>Gets the mission's slots: the five the computer saves to, then the quick save's.</summary>
    IReadOnlyList<TitleSaveSlot> Slots { get; }

    /// <summary>Gets a value indicating whether the mission can be saved as it stands.</summary>
    bool CanSave => true;

    /// <summary>Gets the share of the mission's saves that are left, the charge of the computer's battery: nought when none may be made.</summary>
    float BatteryLevel => SavesLeft == 0 ? 0f : 1f;

    /// <summary>
    /// Gets a value indicating whether the last load started the mission afresh to read the save onto the new level, rather than
    /// onto the level that is up: the host then shows the mission coming up, and the menu has nothing to resume. Both games
    /// read a save onto the level that is up, as I.G.I. 2 does; a mission is brought up again only for a save that would not read.
    /// </summary>
    bool LoadRestartsMission => false;

    /// <summary>Gets how many more saves the mission allows, or <c>-1</c> when there is no limit.</summary>
    int SavesLeft { get; }

    /// <summary>Saves the mission as it stands to a slot.</summary>
    /// <param name="slot">The slot's number.</param>
    /// <returns><see langword="true"/> once saved.</returns>
    bool Save(int slot);

    /// <summary>Saves the mission as it stands to the quick slot, which uses one of the mission's saves as any other does.</summary>
    /// <returns><see langword="true"/> once saved.</returns>
    bool QuickSave() => false;

    /// <summary>Puts the mission back as its newest save holds it, the quick slot's or a menu slot's.</summary>
    /// <returns><see langword="true"/> once loaded, or once the load is under way.</returns>
    bool QuickLoad() => false;

    /// <summary>Puts the mission back as a slot holds it.</summary>
    /// <param name="slot">The slot's number.</param>
    /// <returns><see langword="true"/> once loaded.</returns>
    bool Load(int slot);
}
