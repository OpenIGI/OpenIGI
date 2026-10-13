using OpenIGI.Game.Igi2.Saves;

namespace OpenIGI.Game.Igi2.Player;

/// <content>
/// Its save state: the player's, with its weapon and its view as the child tasks retail keeps them as, the weapon first as
/// <c>Human_CreateHandler</c> makes it before <c>Human_UpdateInternalDataHandler</c> makes the view (human.c).
/// </content>
public sealed partial class Igi2HumanPlayerTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public IReadOnlyList<Igi2SerializedPart> SerializedParts => Human is { } human
        ? [new("Weapon", new InventoryState(human)), new("HumanView", new ViewState(human))]
        : [];

    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer) => Human?.WritePlayerState(writer);

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader) => Human?.ReadPlayerState(reader);

    private sealed class InventoryState(Igi2HumanPlayer human) : IIgi2Serialized
    {
        public void WriteState(Igi2StateWriter writer) => human.Weapon.Inventory?.WriteState(writer);

        public void ReadState(Igi2StateReader reader) => human.Weapon.Inventory?.ReadState(reader);
    }

    private sealed class ViewState(Igi2HumanPlayer human) : IIgi2Serialized
    {
        public void WriteState(Igi2StateWriter writer) => human.View.WriteState(writer);

        public void ReadState(Igi2StateReader reader) => human.View.ReadState(reader);
    }
}
