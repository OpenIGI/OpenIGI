namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// A laser sight's switch, <c>laserdotswitch.c</c>: firing does nothing; letting go turns the weapon's laser dot on or off
/// (<c>LaserDotSwitch_WeaponReleaseHandler</c>).
/// </summary>
internal sealed class Igi2LaserDotSwitchFire : IIgi2FireHandler
{
    /// <inheritdoc/>
    public bool HasRelease => true;

    /// <inheritdoc/>
    public Igi2FireResult Fire(in Igi2FireRequest request) => Igi2FireResult.Nothing;

    /// <inheritdoc/>
    public Igi2FireResult Release(in Igi2FireRequest request)
    {
        if (request.Weapon.Inventory.Current is { } item)
        {
            item.IsLaserDotOn = !item.IsLaserDotOn;
        }

        return Igi2FireResult.Nothing;
    }
}
