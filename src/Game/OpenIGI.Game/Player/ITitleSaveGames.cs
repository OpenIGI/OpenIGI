namespace OpenIGI.Game.Player;

/// <summary>A save slot a title offers: its number, whether it holds a save, and the line it is shown as.</summary>
/// <param name="Slot">The slot's number.</param>
/// <param name="IsUsed">Whether it holds a save of this mission.</param>
/// <param name="Description">What it holds, as the title words it, or empty for an empty slot.</param>
/// <param name="MissingMods">
/// The mods that change play the save was made with and that are not being played now; <see langword="null"/> for none.
/// </param>
public readonly record struct TitleSaveSlot(int Slot, bool IsUsed, string Description, IReadOnlyList<OpenIGI.Core.Content.ModReference>? MissingMods = null);

/// <summary>
/// The save games a title keeps of a running mission: its slots, how many saves the mission still allows, and saving to or loading
/// from a slot. The host's pause menu offers them; the title decides what they hold and how they are written.
/// </summary>
public interface ITitleSaveGames
{
    /// <summary>Gets the slots this mission may be saved to and loaded from.</summary>
    IReadOnlyList<TitleSaveSlot> Slots { get; }

    /// <summary>Gets how many more saves the mission allows, or <c>-1</c> when there is no limit.</summary>
    int SavesLeft { get; }

    /// <summary>Saves the mission as it stands to a slot.</summary>
    /// <param name="slot">The slot's number.</param>
    /// <returns><see langword="true"/> once saved.</returns>
    bool Save(int slot);

    /// <summary>Puts the mission back as a slot holds it.</summary>
    /// <param name="slot">The slot's number.</param>
    /// <returns><see langword="true"/> once loaded.</returns>
    bool Load(int slot);
}
