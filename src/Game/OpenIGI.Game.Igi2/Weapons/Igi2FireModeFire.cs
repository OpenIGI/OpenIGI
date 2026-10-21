namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>What a <c>FireMode</c> does when fired: <c>FireMode_WeaponFireHandler</c> (<c>igi2 0x51BE00</c>).</summary>
/// <remarks>
/// The secondary fire key on a weapon whose second slot is a fire mode steps the weapon in hand to its next fire mode,
/// wrapping after its <c>Available FireModes</c>. The single-player game steps it only while the weapon is ready (status 0,
/// <c>Weapon+0x38</c>); the hold time it tests is the multiplayer game's. It fires nothing, so it spends, delays and kicks nothing,
/// and it has no release.
/// </remarks>
public sealed class Igi2FireModeFire : IIgi2FireHandler
{
    /// <inheritdoc/>
    public bool HasRelease => false;

    /// <inheritdoc/>
    public Igi2FireResult Fire(in Igi2FireRequest request)
    {
        if (request.Weapon.Inventory.Status == Igi2WeaponStatus.Ready && request.Weapon.Inventory.Current is { } item)
        {
            int next = item.ActiveFireMode + 1;
            item.ActiveFireMode = next >= request.WeaponType.AvailableFireModes ? 0 : next;
        }

        return Igi2FireResult.Nothing;
    }

    /// <inheritdoc/>
    public Igi2FireResult Release(in Igi2FireRequest request) => default;
}
