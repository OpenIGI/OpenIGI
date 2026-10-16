namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>Which handler each fire type has, as its task type registers them (<c>Weapon_eFireQTaskEvent</c>, <c>Weapon_eReleaseQTaskEvent</c>).</summary>
/// <remarks>
/// <para>
/// A fire type is the parameters task in its weapon's slot, and the weapon finds its handlers through that task's type
/// (<c>QTask_aatQTaskFinalEvent[Weapon_eFireQTaskEvent][type]</c>, <c>Weapon_SingleFireHandler</c>,
/// <c>igi2 0x51CD40</c>). A type without one fires nothing: the weapon gives up before it spends, delays or kicks.
/// </para>
/// <para>
/// Ported: the <c>Gun</c> (<c>Gun_Open</c>, <c>igi2 0x4835E0</c>), the <c>FireMode</c> (<c>FireMode_Open</c>,
/// <c>0x51BE60</c>), the <c>Zoom</c> (<c>Zoom_Open</c>, <c>0x525DA0</c>), the <c>Knife</c> (<c>Knife_Open</c>,
/// <c>0x4CF970</c>), the <c>PullPin</c> (<c>PullPin_Open</c>, <c>0x4C80C0</c>) and the <c>ProjectileLauncher</c>
/// (<c>ProjectileLauncher_Open</c>, <c>0x526E20</c>), the designator, the proximity mine and the medipack. The charges (C4, remote
/// mines and bombs) are not: no single-player mission hands them out.
/// </para>
/// </remarks>
public static class Igi2FireHandlers
{
    /// <summary>Builds the lookup for a session.</summary>
    /// <param name="tables">The session's weapons and ammunition.</param>
    /// <returns>The handler for a fire type's parameters task, or <see langword="null"/> for a type with none.</returns>
    public static Func<object, IIgi2FireHandler?> Create(Igi2WeaponTables tables)
    {
        Igi2GunFire gun = new(tables);
        Igi2FireModeFire fireMode = new();
        Igi2ZoomFire zoom = new();
        Igi2KnifeFire knife = new();
        Igi2PullPinFire pin = new();
        Igi2ProjectileLauncherFire launcher = new(tables);
        Igi2LaserDesignatorFire designator = new();
        Igi2ProximityMineFire proximityMine = new();
        Igi2MedipackFire medipack = new();
        return parameters => parameters switch
        {
            Igi2PullPinTask => pin,
            Igi2ProjectileLauncherTask => launcher,
            Igi2KnifeTask => knife,
            Igi2GunTask => gun,
            Igi2FireModeTask => fireMode,
            Igi2ZoomTask => zoom,
            Igi2LaserDesignatorTask => designator,
            Igi2ProximityMineTask => proximityMine,
            Igi2MedipackTask => medipack,
            _ => null,
        };
    }
}
