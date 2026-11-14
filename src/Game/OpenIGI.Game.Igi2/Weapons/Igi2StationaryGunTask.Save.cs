namespace OpenIGI.Game.Igi2.Weapons;

/// <content>
/// What a save keeps of a mounted gun: its weapon, the <c>WeaponHandler</c> child its serialize handler writes against it
/// (retail's saves hold <c>StationaryGun/WeaponHandler</c> and nothing of the gun itself, which has no list of its own).
/// </content>
public sealed partial class Igi2StationaryGunTask : ISavedState
{
    /// <inheritdoc/>
    public bool IsSaved => false;

    /// <inheritdoc/>
    public IReadOnlyList<SavedPart> SavedParts =>
        _firing is { } firing ? [new SavedPart(Saves.Igi2SaveSlots.WeaponPart, firing.Inventory)] : [];

    /// <inheritdoc/>
    public void WriteState(StateWriter writer)
    {
    }

    /// <inheritdoc/>
    public void ReadState(StateReader reader)
    {
    }
}
