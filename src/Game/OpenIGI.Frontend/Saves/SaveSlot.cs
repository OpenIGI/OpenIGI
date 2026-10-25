namespace OpenIGI.Frontend.Saves;

/// <summary>
/// One save-game slot: which profile saved it, how far it had reached, when, and the screenshot taken
/// at the moment of saving.
/// </summary>
/// <remarks>
/// This is the port's own save-slot record, distinct from the retail profile surface. A retail profile
/// carries only the furthest mission reached; a save slot is a named, timestamped, illustrated point the
/// player can return to, which is what the shell's Profiles/saves screen presents. It never touches the
/// retail <c>config.qvm</c> — slots live in the port's own directory.
/// </remarks>
/// <param name="Slot">The zero-based slot index.</param>
/// <param name="ProfileName">The name of the profile the save belongs to.</param>
/// <param name="MissionId">The mission the save is at.</param>
/// <param name="MissionName">The mission's resolved display name, stored so the list needs no lookup.</param>
/// <param name="SavedAt">When the save was written.</param>
/// <param name="ScreenshotFile">The screenshot file name relative to the saves directory, or null.</param>
public sealed record SaveSlot(
    int Slot,
    string ProfileName,
    int MissionId,
    string MissionName,
    DateTimeOffset SavedAt,
    string? ScreenshotFile)
{
    /// <summary>Gets the mods that change play the slot was saved with, or <see langword="null"/> for a slot saved before any were recorded.</summary>
    public IReadOnlyList<OpenIGI.Core.Content.ModReference>? Mods { get; init; }
}
