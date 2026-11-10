using OpenIGI.Game.Igi2.Saves;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Its save state: the player's, with its weapon and its view as the child tasks retail keeps them as.</content>
public sealed partial class Igi2HumanPlayerTask : ISavedState
{
    /// <inheritdoc/>
    public IReadOnlyList<SavedPart> SavedParts => Human is { } human ? human.SavedParts(human.Weapon.Inventory) : [];

    /// <inheritdoc/>
    public void WriteState(StateWriter writer) => Human?.WritePlayerState(writer);

    /// <inheritdoc/>
    public void ReadState(StateReader reader) => Human?.ReadPlayerState(reader);
}
