using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>What one of a weapon's two fire types fires and how much of it a load holds.</summary>
/// <param name="AmmoType">The ammunition it uses, or <see langword="null"/> for none.</param>
/// <param name="RoundsToFire">The rounds one use takes, <c>nRoundsToFire</c>: one for the kinds that use ammunition, none otherwise.</param>
/// <param name="BulletsPerRound">The bullets one round fires.</param>
/// <param name="RoundsPerClip">The rounds one load holds.</param>
/// <remarks>
/// One entry of <c>WeaponTypeGetAmmoInfoContext_s</c>, filled by the fire type's own <c>GetAmmoInfo</c> handler:
/// <c>Gun_GetAmmoInfoHandler</c> (<c>igi2 0x482710</c>) reads its parameters, the thrown and placed kinds report one round
/// of their ammunition per load, and a fire type with no handler of its own reports none
/// (<c>FireType_GetAmmoInfoHandler</c>, <c>0x470490</c>).
/// </remarks>
public readonly record struct Igi2FireTypeAmmo(Igi2AmmoTypeTask? AmmoType, int RoundsToFire, int BulletsPerRound, int RoundsPerClip)
{
    /// <summary>Gets what a slot with no fire type reports: an entry left zeroed (<c>WeaponType_GetAmmoInfoHandler</c>, <c>0x436340</c>).</summary>
    public static Igi2FireTypeAmmo Absent => default;

    /// <summary>Gets what a fire type that uses no ammunition reports, a one-round load of nothing.</summary>
    /// <remarks><c>FireType_GetAmmoInfoHandler</c> (<c>0x470490</c>), and the same from <c>Knife</c> (<c>0x4CF880</c>) and <c>PullPin</c> (<c>0x4C8040</c>).</remarks>
    public static Igi2FireTypeAmmo None => new(null, 0, 0, 1);

    /// <summary>Gets what a fire type that throws or places one of its ammunition per use reports.</summary>
    /// <param name="ammoType">The ammunition.</param>
    /// <returns>A one-round load of it.</returns>
    public static Igi2FireTypeAmmo OneRound(Igi2AmmoTypeTask? ammoType) => new(ammoType, 1, 1, 1);
}

/// <summary>A fire type that reports its own ammunition, rather than the base fire type's none.</summary>
public interface IIgi2FireTypeAmmo
{
    /// <summary>Reports what this fire type fires.</summary>
    /// <param name="tables">The session's ammunition, by number.</param>
    /// <returns>The report.</returns>
    Igi2FireTypeAmmo AmmoInfo(Igi2WeaponTables tables);
}

/// <content>The gun's report: its own ammunition, bullets per round and clip size (<c>Gun_GetAmmoInfoHandler</c>, <c>igi2 0x482710</c>).</content>
public sealed partial class Igi2GunTask : IIgi2FireTypeAmmo
{
    /// <summary>Gets a fire mode's fire sound, <c>azFireSound1[mode]</c> (<c>Gun+0x274</c>).</summary>
    /// <param name="mode">The fire mode, 0 to 2.</param>
    /// <returns>The sound's name.</returns>
    public string FireSound(int mode) => mode switch
    {
        0 => FireSound1,
        1 => FireSound11,
        _ => FireSound12,
    };

    /// <summary>Gets whether a fire mode's fire sound loops, <c>aisFireSoundLooped[mode]</c> (<c>Gun+0x2F8</c>).</summary>
    /// <param name="mode">The fire mode, 0 to 2.</param>
    /// <returns>The flag.</returns>
    public bool IsFireSoundLooped(int mode) => mode switch
    {
        0 => PlayFireSoundLooped,
        1 => PlayFireSoundLooped1,
        _ => PlayFireSoundLooped2,
    };

    /// <inheritdoc/>
    public Igi2FireTypeAmmo AmmoInfo(Igi2WeaponTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        return new(tables.Ammo(AmmoTypeId), 1, BulletsPerRound, RoundsPerClip);
    }
}

/// <content>One charge a load (<c>C4Bomb_GetAmmoInfoHandler</c>, <c>igi2 0x4EDB20</c>).</content>
public sealed partial class Igi2C4BombTask : IIgi2FireTypeAmmo
{
    /// <inheritdoc/>
    public Igi2FireTypeAmmo AmmoInfo(Igi2WeaponTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        return Igi2FireTypeAmmo.OneRound(tables.Ammo(AmmoType));
    }
}

/// <content>One mine a load (<c>ProximityMine_GetAmmoInfoHandler</c>, <c>igi2 0x507A50</c>).</content>
public sealed partial class Igi2ProximityMineTask : IIgi2FireTypeAmmo
{
    /// <inheritdoc/>
    public Igi2FireTypeAmmo AmmoInfo(Igi2WeaponTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        return Igi2FireTypeAmmo.OneRound(tables.Ammo(AmmoType));
    }
}

/// <content>
/// One mine a load: <c>RemoteMine</c> is registered under <c>ProximityMine</c> (<c>igi2 0x508155</c>) with no
/// <c>GetAmmoInfo</c> handler of its own, so it reports as the proximity mine does.
/// </content>
public sealed partial class Igi2RemoteMineTask : IIgi2FireTypeAmmo
{
    /// <inheritdoc/>
    public Igi2FireTypeAmmo AmmoInfo(Igi2WeaponTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        return Igi2FireTypeAmmo.OneRound(tables.Ammo(AmmoType));
    }
}

/// <content>One pack a load (<c>Medipack_GetAmmoInfoHandler</c>, <c>igi2 0x535D60</c>).</content>
public sealed partial class Igi2MedipackTask : IIgi2FireTypeAmmo
{
    /// <inheritdoc/>
    public Igi2FireTypeAmmo AmmoInfo(Igi2WeaponTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        return Igi2FireTypeAmmo.OneRound(tables.Ammo(AmmoType));
    }
}

/// <content>
/// One projectile a load, of the ammunition its <c>Ammo Type</c> names (<c>ProjectileLauncher_GetAmmoInfoHandler</c>,
/// <c>igi2 0x526A40</c>, reading what <c>ProjectileLauncher_UpdateInternalDataHandler</c>, <c>0x526880</c>, looked up).
/// </content>
public sealed partial class Igi2ProjectileLauncherTask : IIgi2FireTypeAmmo
{
    /// <inheritdoc/>
    public Igi2FireTypeAmmo AmmoInfo(Igi2WeaponTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        return Igi2FireTypeAmmo.OneRound(tables.Ammo(AmmoType));
    }
}

/// <content>The weapon's two fire types and what they fire.</content>
public sealed partial class Igi2WeaponTypeTask
{
    /// <summary>The number of fire types a weapon has: its primary and its secondary.</summary>
    public const int FireTypeSlots = 2;

    /// <summary>Gets the fire type in a slot: the first task in the parameter container numbered for that slot.</summary>
    /// <param name="slot">0 for the primary fire type, 1 for the secondary.</param>
    /// <returns>The fire type, or <see langword="null"/> when the weapon has none there.</returns>
    /// <remarks>
    /// A <c>WeaponTypeParameterContainer</c> files itself under its weapon by its <c>Parameter ID</c>
    /// (<c>WeaponTypeParameterContainer_FindChildrenHandler</c>, <c>igi2 0x4365F0</c>), and the weapon asks the first task
    /// in it (<c>WeaponType_GetAmmoInfoHandler</c>, <c>0x436340</c>).
    /// </remarks>
    public GameTask? FireType(int slot)
    {
        foreach (GameTask child in Children)
        {
            if (child is Igi2WeaponTypeParameterContainerTask container && container.ParameterId == slot)
            {
                return container.Children.FirstOrDefault();
            }
        }

        return null;
    }

    /// <summary>Gets whether a slot's ammunition must be loaded before the first shot, its <c>Force Reload</c> flag.</summary>
    /// <param name="slot">0 for the primary fire type, 1 for the secondary.</param>
    /// <returns>The flag.</returns>
    public bool IsForceReload(int slot) => slot == 0 ? PrimaryForceReload : SecondaryForceReload;

    /// <summary>Gets whether a slot is reloaded a round at a time, its <c>Reload rounds individiually</c> flag.</summary>
    /// <param name="slot">0 for the primary fire type, 1 for the secondary.</param>
    /// <returns>The flag.</returns>
    public bool IsReloadRoundsIndividually(int slot) => slot == 0 ? PrimaryReloadRoundsIndividually : SecondaryReloadRoundsIndividually;

    /// <summary>Gets the task type a slot's fire type is declared as, <c>atFireTypes[slot].eQTaskType</c>; <c>TASKTYPE_NONE</c> for none.</summary>
    /// <param name="slot">0 for the primary fire type, 1 for the secondary.</param>
    /// <returns>The type id.</returns>
    public int FireTaskType(int slot) => slot == 0 ? PrimaryFireTaskType : SecondaryFireTaskType;

    /// <summary>Gets the rounds a fire mode fires for one pull, <c>anRoundsInSuccession</c> (<c>WeaponType+0x2C8</c>).</summary>
    /// <param name="mode">The fire mode, 0 to 2.</param>
    /// <returns>The rounds: one for single shots, the clip for full automatic, and none for a fire type that is not a gun.</returns>
    public int RoundsInSuccession(int mode) => mode switch
    {
        0 => FiremodeRoundsInSuccession,
        1 => FiremodeRoundsInSuccession1,
        _ => FiremodeRoundsInSuccession2,
    };

    /// <summary>Gets how long a burst's fire sound plays before its tail, <c>avFireModeSoundStopTime</c> (<c>WeaponType+0x334</c>).</summary>
    /// <param name="mode">The fire mode, 0 to 2.</param>
    /// <returns>The time, in seconds.</returns>
    public float FireSoundStopTime(int mode) => mode switch
    {
        0 => FiremodeFiresoundStopTime,
        1 => FiremodeFiresoundStopTime1,
        _ => FiremodeFiresoundStopTime2,
    };

    /// <summary>Reports what a slot's fire type fires (<c>WeaponType_GetAmmoInfo</c>, <c>igi2 0x436930</c>).</summary>
    /// <param name="slot">0 for the primary fire type, 1 for the secondary.</param>
    /// <param name="tables">The session's ammunition, by number.</param>
    /// <returns>The report.</returns>
    public Igi2FireTypeAmmo AmmoInfo(int slot, Igi2WeaponTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        return FireType(slot) switch
        {
            null => Igi2FireTypeAmmo.Absent,
            IIgi2FireTypeAmmo reporting => reporting.AmmoInfo(tables),
            _ => Igi2FireTypeAmmo.None,
        };
    }
}
