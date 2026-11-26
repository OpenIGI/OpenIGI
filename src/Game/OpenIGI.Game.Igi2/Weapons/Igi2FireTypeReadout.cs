namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>What a fire type shows of its rounds under the weapon in hand, through its own status display handler.</summary>
public enum Igi2RoundsReadout
{
    /// <summary>Nothing: a fire type with no status display handler of its own (a grenade's pin, the knife, a zoom).</summary>
    None,

    /// <summary>The rounds loaded over those carried, <c>"%4d/%4d"</c> (<c>gun.c Gun_StatusDisplayHandler</c>).</summary>
    LoadedOverCarried,

    /// <summary>
    /// The rounds carried alone, <c>"%4d"</c>: the medipack, the projectile launcher and the proximity mine
    /// (<c>Medipack_StatusDisplayHandler</c>, <c>ProjectileLauncher_StatusDisplayHandler</c>, <c>ProximityMine_StatusDisplayHandler</c>),
    /// which use what is carried and never what is loaded.
    /// </summary>
    Carried,
}

/// <summary>A fire type with a status display handler of its own, which shows its rounds.</summary>
/// <remarks>
/// <c>WeaponType_StatusDisplayHandler</c> runs each of the weapon's two fire types' <c>StatusScreen_eStatusDisplayQTaskEvent</c> handler
/// in turn, and each draws its text where <c>WeaponType_GetFireTypeScreenPosition</c> puts it: the first ending ten pixels in from the
/// line's right end, the second starting there.
/// </remarks>
public interface IIgi2FireTypeReadout
{
    /// <summary>Gets what the fire type shows.</summary>
    Igi2RoundsReadout Readout { get; }
}

/// <content>Its rounds loaded over those carried.</content>
public sealed partial class Igi2GunTask : IIgi2FireTypeReadout
{
    /// <inheritdoc/>
    public Igi2RoundsReadout Readout => Igi2RoundsReadout.LoadedOverCarried;
}

/// <content>The packs carried.</content>
public sealed partial class Igi2MedipackTask : IIgi2FireTypeReadout
{
    /// <inheritdoc/>
    public Igi2RoundsReadout Readout => Igi2RoundsReadout.Carried;
}

/// <content>The projectiles carried.</content>
public sealed partial class Igi2ProjectileLauncherTask : IIgi2FireTypeReadout
{
    /// <inheritdoc/>
    public Igi2RoundsReadout Readout => Igi2RoundsReadout.Carried;
}

/// <content>The mines carried.</content>
public sealed partial class Igi2ProximityMineTask : IIgi2FireTypeReadout
{
    /// <inheritdoc/>
    public Igi2RoundsReadout Readout => Igi2RoundsReadout.Carried;
}

/// <content>The mines carried: <c>RemoteMine</c> is made from <c>ProximityMine</c> and keeps its status display handler.</content>
public sealed partial class Igi2RemoteMineTask : IIgi2FireTypeReadout
{
    /// <inheritdoc/>
    public Igi2RoundsReadout Readout => Igi2RoundsReadout.Carried;
}

/// <content>What each fire type shows of its rounds.</content>
public sealed partial class Igi2WeaponTypeTask
{
    /// <summary>Gets what the fire type in a slot shows of its rounds under the weapon in hand.</summary>
    /// <param name="slot">0 for the primary fire type, 1 for the secondary.</param>
    /// <returns>The readout; <see cref="Igi2RoundsReadout.None"/> for an empty slot or a fire type that shows nothing.</returns>
    public Igi2RoundsReadout Readout(int slot) => FireType(slot) is IIgi2FireTypeReadout readout ? readout.Readout : Igi2RoundsReadout.None;
}
