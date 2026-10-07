namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>One weapon a human may carry, and the rounds loaded in each of its fire types.</summary>
/// <remarks><c>WeaponTypeItem_s</c>, one per weapon type, made when the inventory is (<c>Weapon_EnumWeaponTypeCB</c>, <c>igi2 0x51C040</c>).</remarks>
public sealed class Igi2WeaponItem
{
    private readonly int[] _loaded = new int[Igi2WeaponTypeTask.FireTypeSlots];

    internal Igi2WeaponItem(Igi2WeaponTypeTask weaponType, int number)
    {
        WeaponType = weaponType;
        Number = number;
    }

    /// <summary>Gets the weapon type.</summary>
    public Igi2WeaponTypeTask WeaponType { get; }

    /// <summary>Gets the weapon type's number, the value of its <c>WEAPON_ID_*</c> constant.</summary>
    public int Number { get; }

    /// <summary>Gets a value indicating whether the human carries this weapon.</summary>
    public bool IsActive { get; internal set; }

    /// <summary>Gets the rounds loaded in a fire type, <c>anFireTypeAmmoCount</c>.</summary>
    /// <param name="slot">0 for the primary fire type, 1 for the secondary.</param>
    /// <returns>The rounds.</returns>
    public int Loaded(int slot) => _loaded[slot];

    internal void SetLoaded(int slot, int rounds) => _loaded[slot] = rounds;
}

/// <summary>One kind of ammunition a human may carry, and how much of it.</summary>
/// <remarks><c>AmmoTypeItem_s</c>, one per ammunition type, made with no count and no limit (<c>Weapon_EnumAmmoTypeCB</c>, <c>igi2 0x51C000</c>).</remarks>
public sealed class Igi2AmmoItem
{
    /// <summary>The limit that is none.</summary>
    public const int NoLimit = -1;

    internal Igi2AmmoItem(Igi2AmmoTypeTask ammoType, int number)
    {
        AmmoType = ammoType;
        Number = number;
    }

    /// <summary>Gets the ammunition type.</summary>
    public Igi2AmmoTypeTask AmmoType { get; }

    /// <summary>Gets the ammunition type's number, the value of its <c>AMMO_ID_*</c> constant.</summary>
    public int Number { get; }

    /// <summary>Gets the rounds carried, besides those loaded.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the most that may be carried, or <see cref="NoLimit"/>.</summary>
    public int Limit { get; private set; } = NoLimit;

    /// <summary>Adds rounds, within the limit (<c>Weapon_AddAmmoToAmmoType</c>, <c>igi2 0x51F150</c>).</summary>
    /// <param name="rounds">The rounds; negative takes them away, never below none.</param>
    internal void Add(int rounds)
    {
        Count += rounds;
        Clamp();
    }

    /// <summary>Sets the limit and brings the count within it, as a human's creation does (<c>Human_CreateHandler</c>, <c>igi2 0x4FE043</c>).</summary>
    /// <param name="limit">The limit, or <see cref="NoLimit"/>.</param>
    internal void SetLimit(int limit)
    {
        Limit = limit;
        Clamp();
    }

    /// <summary>Takes every round away.</summary>
    internal void Empty() => Count = 0;

    private void Clamp()
    {
        if (Limit == NoLimit)
        {
            return;
        }

        if (Count < 0)
        {
            Count = 0;
        }
        else if (Count > Limit)
        {
            Count = Limit;
        }
    }
}

/// <summary>What a human carries: every weapon and every ammunition the session defines, each marked carried or not.</summary>
/// <remarks>
/// <para>
/// The original's <c>Weapon</c> task, which every human makes for itself (<c>Human_CreateHandler</c>, <c>igi2 0x4FDC90</c>):
/// <c>Weapon_CreateHandler</c> (<c>0x51C320</c>) lists one item for each ammunition type and one for each weapon type, in
/// the order the session made them, and carrying a weapon is that item marked active. Weapons and ammunition are found by
/// the name of their constant, compared exactly (<c>Weapon_GetWeaponTypeItemFromWeaponType</c>, <c>0x51E560</c>, and
/// <c>Weapon_GetAmmoTypeItemFromAmmoType</c>, <c>0x51E690</c>), which is how the natives that fill it name them.
/// </para>
/// <para>
/// Multiplayer's branches are not here: the single-player campaign never takes them.
/// </para>
/// </remarks>
public sealed class Igi2Inventory
{
    private readonly Igi2WeaponTables _tables;
    private readonly Igi2WeaponItem[] _weapons;
    private readonly Igi2AmmoItem[] _ammo;

    /// <summary>Initializes a new instance of the <see cref="Igi2Inventory"/> class, carrying nothing.</summary>
    /// <param name="tables">The session's weapons and ammunition.</param>
    public Igi2Inventory(Igi2WeaponTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        _tables = tables;
        _ammo = [.. tables.AmmoTypes.Select(static (a, i) => new Igi2AmmoItem(a, i))];
        _weapons = [.. tables.WeaponTypes.Select(static (w, i) => new Igi2WeaponItem(w, i))];
    }

    /// <summary>Gets one item for each weapon type, by number.</summary>
    public IReadOnlyList<Igi2WeaponItem> Weapons => _weapons;

    /// <summary>Gets one item for each ammunition type, by number.</summary>
    public IReadOnlyList<Igi2AmmoItem> Ammo => _ammo;

    /// <summary>Gets the weapon in hand (<c>Weapon+0x2C</c>, <c>Weapon_GetCurrentWeaponTypeItem</c>, <c>igi2 0x51E540</c>).</summary>
    public Igi2WeaponItem? Current { get; private set; }

    /// <summary>Gets the weapon the status display shows (<c>Weapon+0x28</c>, <c>Weapon_GetStatusDisplayWeaponType</c>, <c>igi2 0x51F7E0</c>).</summary>
    public Igi2WeaponItem? StatusDisplay { get; private set; }

    /// <summary>Gets the position of <see cref="Current"/> among the weapon items (<c>Weapon+0x30</c>).</summary>
    public int CurrentIndex { get; private set; }

    /// <summary>Gets the weapon's status, <c>eWeaponStatus</c> (<c>Weapon+0x38</c>).</summary>
    public Igi2WeaponStatus Status { get; set; }

    /// <summary>Gets or sets the ticks before the weapon may fire again, <c>nDelay</c> (<c>Weapon+0x48</c>).</summary>
    public int Delay { get; set; }

    /// <summary>Gets or sets a value indicating whether a held grenade's pin is out, <c>isPinPulled</c>.</summary>
    public bool IsPinPulled { get; set; }

    /// <summary>Gets or sets a pulled grenade's fuse, <c>nTimer</c>, in ticks.</summary>
    public int Timer { get; set; }

    /// <summary>Gets or sets the bullets fired since the weapon was put in hand, <c>nTotalBulletsFired</c>.</summary>
    public int TotalBulletsFired { get; set; }

    /// <summary>Finds a weapon by its number, the value of its <c>WEAPON_ID_*</c> constant (<c>WeaponType_GetWeaponTypeFromID</c>, <c>igi2 0x436710</c>).</summary>
    /// <param name="number">The number.</param>
    /// <returns>The item, or <see langword="null"/> for a number no weapon has.</returns>
    public Igi2WeaponItem? WeaponByNumber(int number) => (uint)number < (uint)_weapons.Length ? _weapons[number] : null;

    /// <summary>Finds a weapon by the name of its constant.</summary>
    /// <param name="weaponId">The name, <c>WEAPON_ID_*</c>.</param>
    /// <returns>The item, or <see langword="null"/>.</returns>
    public Igi2WeaponItem? FindWeapon(string weaponId) =>
        Array.Find(_weapons, w => string.Equals(w.WeaponType.WeaponId, weaponId, StringComparison.Ordinal));

    /// <summary>Finds an ammunition by the name of its constant.</summary>
    /// <param name="ammoId">The name, <c>AMMO_ID_*</c>.</param>
    /// <returns>The item, or <see langword="null"/>.</returns>
    public Igi2AmmoItem? FindAmmo(string ammoId) =>
        Array.Find(_ammo, a => string.Equals(a.AmmoType.AmmoId, ammoId, StringComparison.Ordinal));

    /// <summary>Applies the player tuning's ammunition limits, as every human's creation does.</summary>
    /// <param name="limits">The limits, by <c>AMMO_ID_*</c> number, in the order they were defined.</param>
    /// <remarks>
    /// <c>Human_CreateHandler</c> (<c>igi2 0x4FE043</c>) walks <c>HumanPlayer_tAmmoLimit</c> for soldiers as well as the
    /// player; a later entry for the same ammunition overrides an earlier one.
    /// </remarks>
    public void ApplyAmmoLimits(IEnumerable<Igi2AmmoLimit> limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        foreach (Igi2AmmoLimit limit in limits)
        {
            if (_tables.Ammo(limit.AmmoNumber) is { } ammoType && FindAmmo(ammoType.AmmoId) is { } item)
            {
                item.SetLimit(limit.Max);
            }
        }
    }

    /// <summary>Starts carrying a weapon, with a load of its ammunition when asked (<c>Weapon_EnableWeaponType</c>, <c>igi2 0x51EF00</c>).</summary>
    /// <param name="weaponId">The name of its constant, <c>WEAPON_ID_*</c>.</param>
    /// <param name="addAmmo">Whether to add a load of ammunition with it.</param>
    /// <returns><see langword="true"/> when it was not already carried.</returns>
    /// <remarks>
    /// <para>
    /// A weapon newly carried starts with one round loaded in a fire type that must be loaded before it fires and none in
    /// the others. Asked for ammunition, it is then given one full load for each fire type
    /// (<c>WeaponType_CalcGunPickupAmmo</c>, <c>0x438330</c>, with a fraction of one) through
    /// <see cref="AddAmmoForWeapon"/>.
    /// </para>
    /// <para>
    /// The first weapon carried, carried or not before, goes in hand — unless it is binoculars or the thermal goggles,
    /// which never do (categories 12 and 11).
    /// </para>
    /// </remarks>
    public bool EnableWeaponType(string weaponId, bool addAmmo)
    {
        if (FindWeapon(weaponId) is not { } item)
        {
            return false;
        }

        bool added = !item.IsActive;
        item.IsActive = true;
        if (added)
        {
            for (int slot = 0; slot < Igi2WeaponTypeTask.FireTypeSlots; slot++)
            {
                item.SetLoaded(slot, item.WeaponType.IsForceReload(slot) ? 1 : 0);
            }

            if (addAmmo)
            {
                AddAmmoForWeapon(item, PickupAmmo(item.WeaponType, 1f));
            }
        }

        if (Current is null && item.WeaponType.WeaponCategory is not (Igi2WeaponCategory.Binoculars or Igi2WeaponCategory.ThermoChemical))
        {
            Current = item;
            StatusDisplay = item;
            CurrentIndex = Array.IndexOf(_weapons, item);
        }

        return added;
    }

    /// <summary>Puts a carried weapon in hand (<c>Weapon_SingleActivateHandler</c>, <c>igi2 0x51D600</c>).</summary>
    /// <param name="item">The weapon.</param>
    /// <returns><see langword="true"/> when it went in hand; a weapon not carried does not.</returns>
    /// <remarks>
    /// <para>
    /// The weapon put away is dropped from the inventory when it was thrown or laid and none of it is left: a grenade or
    /// a proximity mine with no rounds carried, or an RPG-18 with none carried and none loaded either.
    /// </para>
    /// <para>
    /// The new weapon starts ready, with no delay, no pin out, no fuse and no bullets fired. Not yet: the fire types'
    /// own deactivate and activate handlers, and the overlay, which arrive with firing.
    /// </para>
    /// </remarks>
    public bool Activate(Igi2WeaponItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.IsActive)
        {
            return false;
        }

        if (Current is { } old && IsSpentWhenEmpty(old.WeaponType)
            && FireTypeRounds(old, 0).Available == 0
            && (!IsRpg18(old.WeaponType) || old.Loaded(0) == 0))
        {
            Disable(old, removeCarriedAmmo: true);
        }

        Current = item;
        StatusDisplay = item;
        CurrentIndex = Array.IndexOf(_weapons, item);
        Delay = 0;
        IsPinPulled = false;
        Timer = 0;
        TotalBulletsFired = 0;
        Status = Igi2WeaponStatus.Ready;
        return true;
    }

    /// <summary>Gets whether a weapon is used up when nothing of it is left: a grenade, a proximity mine or the RPG-18.</summary>
    /// <param name="weaponType">The weapon.</param>
    /// <returns><see langword="true"/> for those three.</returns>
    public static bool IsSpentWhenEmpty(Igi2WeaponTypeTask weaponType)
    {
        ArgumentNullException.ThrowIfNull(weaponType);
        return weaponType.WeaponType is Igi2WeaponTypeKind.Grenade or Igi2WeaponTypeKind.ProximityMine || IsRpg18(weaponType);
    }

    /// <summary>Gets whether a weapon is the single-shot launcher the engine names, <c>WEAPON_ID_RPG18</c> (<c>igi2 0x61E050</c>).</summary>
    /// <param name="weaponType">The weapon.</param>
    /// <returns><see langword="true"/> for the RPG-18.</returns>
    public static bool IsRpg18(Igi2WeaponTypeTask weaponType)
    {
        ArgumentNullException.ThrowIfNull(weaponType);
        return string.Equals(weaponType.WeaponId, Rpg18, StringComparison.Ordinal);
    }

    /// <summary>The weapon the engine treats as spent once fired, compared as sixteen characters.</summary>
    public const string Rpg18 = "WEAPON_ID_RPG18";

    /// <summary>Gives a weapon rounds, loading each empty fire type first and carrying the rest (<c>Weapon_AddAmmoForWeapon</c>, <c>igi2 0x51ED50</c>).</summary>
    /// <param name="item">The weapon.</param>
    /// <param name="rounds">The rounds for each fire type.</param>
    /// <remarks>
    /// A fire type with nothing loaded, and that need not be loaded by hand, takes up to a load's worth straight into the
    /// weapon. What is left goes to the carried count of the fire type's ammunition, within its limit.
    /// </remarks>
    public void AddAmmoForWeapon(Igi2WeaponItem item, ReadOnlySpan<int> rounds)
    {
        ArgumentNullException.ThrowIfNull(item);

        for (int slot = 0; slot < Igi2WeaponTypeTask.FireTypeSlots; slot++)
        {
            Igi2FireTypeAmmo ammo = item.WeaponType.AmmoInfo(slot, _tables);
            int left = rounds[slot];
            if (item.Loaded(slot) == 0 && !item.WeaponType.IsForceReload(slot))
            {
                int loaded = Math.Min(left, ammo.RoundsPerClip);
                item.SetLoaded(slot, loaded);
                left -= loaded;
            }

            if (ammo.AmmoType is { } ammoType && left > 0 && FindAmmo(ammoType.AmmoId) is { } carried)
            {
                carried.Add(left);
            }
        }
    }

    /// <summary>Adds rounds of an ammunition to what is carried (<c>Weapon_AddAmmoToAmmoType</c>, <c>igi2 0x51F150</c>).</summary>
    /// <param name="ammoId">The name of its constant, <c>AMMO_ID_*</c>.</param>
    /// <param name="rounds">The rounds.</param>
    public void AddAmmo(string ammoId, int rounds) => FindAmmo(ammoId)?.Add(rounds);

    /// <summary>Gets how much the carried weapons slow their carrier (<c>Weapon_GetSummedMovementSlowDown</c>, <c>igi2 0x51F550</c>).</summary>
    /// <remarks>Each carried weapon's <c>Movement Speed Modifier</c>, the one in hand in full and the rest by half.</remarks>
    public float SummedMovementSlowDown
    {
        get
        {
            Fpu sum = 0.0;
            foreach (Igi2WeaponItem item in _weapons)
            {
                if (item.IsActive)
                {
                    sum = ReferenceEquals(item, Current)
                        ? sum + item.WeaponType.MovementSpeedModifier
                        : sum + ((Fpu)item.WeaponType.MovementSpeedModifier * 0.5);
                }
            }

            return sum.F;
        }
    }

    /// <summary>Reports what a weapon holds and has to hand for one fire type (<c>Weapon_GetFireTypeAmmoInfoFromWeaponTypeItem</c>, <c>igi2 0x51E7C0</c>).</summary>
    /// <param name="item">The weapon.</param>
    /// <param name="slot">0 for the primary fire type, 1 for the secondary.</param>
    /// <returns>The report.</returns>
    public Igi2FireTypeRounds FireTypeRounds(Igi2WeaponItem item, int slot)
    {
        ArgumentNullException.ThrowIfNull(item);
        Igi2FireTypeAmmo ammo = item.WeaponType.AmmoInfo(slot, _tables);
        int available = ammo.AmmoType is { } ammoType && FindAmmo(ammoType.AmmoId) is { } carried ? carried.Count : 0;
        return new Igi2FireTypeRounds(ammo.AmmoType, item.Loaded(slot), available, ammo.BulletsPerRound, ammo.RoundsPerClip);
    }

    /// <summary>Gets whether a weapon may be put in hand (<c>Weapon_IsWeaponSelectable</c>, <c>igi2 0x51E700</c>).</summary>
    /// <param name="item">The weapon.</param>
    /// <returns>
    /// <see langword="true"/> when it is carried and either may be selected when empty, or one of its declared fire types
    /// takes no rounds, has rounds loaded, or has its ammunition carried.
    /// </returns>
    public bool IsSelectable(Igi2WeaponItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.IsActive)
        {
            return false;
        }

        if (item.WeaponType.SelectableWhenEmpty)
        {
            return true;
        }

        for (int slot = 0; slot < Igi2WeaponTypeTask.FireTypeSlots; slot++)
        {
            if (item.WeaponType.FireTaskType(slot) == Igi2WeaponConstants.NoTaskType)
            {
                continue;
            }

            Igi2FireTypeAmmo ammo = item.WeaponType.AmmoInfo(slot, _tables);
            if (ammo.RoundsToFire == 0 || item.Loaded(slot) != 0
                || (ammo.AmmoType is { } ammoType && FindAmmo(ammoType.AmmoId) is { Count: not 0 }))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the next carried weapon of a category after a given one, without wrapping
    /// (<c>Weapon_GetNextEnabledWeaponTypeFromCategory</c>, <c>igi2 0x51F380</c>).
    /// </summary>
    /// <param name="category">The <c>WEAPONCATEGORY_*</c> value.</param>
    /// <param name="after">The weapon to start after, or <see langword="null"/> to start from the first.</param>
    /// <returns>The weapon, or <see langword="null"/> when none follows.</returns>
    public Igi2WeaponItem? NextInCategory(int category, Igi2WeaponTypeTask? after)
    {
        foreach (Igi2WeaponItem item in _weapons)
        {
            if (after is not null)
            {
                if (ReferenceEquals(item.WeaponType, after))
                {
                    after = null;
                }

                continue;
            }

            if (item.IsActive && item.WeaponType.WeaponCategory == category)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>Stops carrying a weapon, emptying it and, when asked, its ammunition too (<c>Weapon_DisableWeaponType</c>, <c>igi2 0x51F060</c>).</summary>
    /// <param name="item">The weapon.</param>
    /// <param name="removeCarriedAmmo">Whether the carried rounds of its fire types' ammunition go too.</param>
    public void Disable(Igi2WeaponItem item, bool removeCarriedAmmo)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.IsActive = false;
        for (int slot = 0; slot < Igi2WeaponTypeTask.FireTypeSlots; slot++)
        {
            item.SetLoaded(slot, 0);
            if (removeCarriedAmmo && item.WeaponType.AmmoInfo(slot, _tables).AmmoType is { } ammoType && FindAmmo(ammoType.AmmoId) is { } carried)
            {
                carried.Empty();
            }
        }
    }

    /// <summary>Takes every weapon and every round away (<c>Weapon_ClearAllWeapons</c>, <c>igi2 0x51DCF0</c>).</summary>
    public void ClearAll()
    {
        Current = null;
        StatusDisplay = null;
        CurrentIndex = 0;
        foreach (Igi2WeaponItem item in _weapons)
        {
            item.IsActive = false;
            item.SetLoaded(0, 0);
            item.SetLoaded(1, 0);
        }

        foreach (Igi2AmmoItem item in _ammo)
        {
            item.Empty();
        }
    }

    /// <summary>Works out a fraction of a load for each of a weapon's fire types, never less than one round.</summary>
    /// <param name="weaponType">The weapon.</param>
    /// <param name="fraction">The fraction of a full load.</param>
    /// <returns>The rounds for each fire type.</returns>
    /// <remarks><c>WeaponType_CalcGunPickupAmmo</c> (<c>igi2 0x438330</c>): the clip size times the fraction at the FPU's precision, truncated.</remarks>
    public int[] PickupAmmo(Igi2WeaponTypeTask weaponType, float fraction)
    {
        ArgumentNullException.ThrowIfNull(weaponType);

        int[] rounds = new int[Igi2WeaponTypeTask.FireTypeSlots];
        for (int slot = 0; slot < rounds.Length; slot++)
        {
            int whole = Fpu.ToInt32(new Fpu(weaponType.AmmoInfo(slot, _tables).RoundsPerClip) * fraction);
            rounds[slot] = Math.Max(whole, 1);
        }

        return rounds;
    }
}

/// <summary>What a weapon holds and has to hand for one fire type, <c>WeaponGetFireTypeAmmoContext_s</c>.</summary>
/// <param name="AmmoType">The ammunition the fire type uses, or <see langword="null"/>.</param>
/// <param name="Loaded">The rounds loaded, <c>nAmmoInWeapon</c>.</param>
/// <param name="Available">The rounds carried besides, <c>nAmmoAvailable</c>.</param>
/// <param name="BulletsPerRound">The bullets one round fires.</param>
/// <param name="RoundsPerClip">The rounds one load holds.</param>
public readonly record struct Igi2FireTypeRounds(Igi2AmmoTypeTask? AmmoType, int Loaded, int Available, int BulletsPerRound, int RoundsPerClip);

/// <summary>The most of one ammunition a human may carry.</summary>
/// <param name="AmmoNumber">The ammunition's number, its <c>AMMO_ID_*</c> value.</param>
/// <param name="Max">The limit, or <see cref="Igi2AmmoItem.NoLimit"/>.</param>
public readonly record struct Igi2AmmoLimit(int AmmoNumber, int Max);

/// <summary>A weapon's status, <c>WeaponStatus_e</c>: the values the ported code sets or tests.</summary>
public enum Igi2WeaponStatus
{
    /// <summary>Ready to fire, <c>WEAPON_STATUS_READY</c>.</summary>
    Ready = 0,

    /// <summary>
    /// A status the player's idle state will not reload a weapon in (<c>Weapon+0x38 == 5</c>,
    /// <c>HumanPlayer_RunActionIdle</c>, <c>igi2 0x4B4142</c>); its name arrives with firing.
    /// </summary>
    Five = 5,

    /// <summary>A pulled grenade went off in hand, <c>WEAPON_STATUS_EXPLODED_IN_HAND</c> (<c>Weapon_RunDelayedHandler</c>, <c>igi2 0x51C4D4</c>).</summary>
    ExplodedInHand = 6,
}

/// <summary>The <c>WEAPONTYPE_*</c> values the ported code tests, <c>WeaponType+0x29C</c>.</summary>
internal static class Igi2WeaponTypeKind
{
    /// <summary><c>WEAPONTYPE_GRENADE</c>.</summary>
    public const int Grenade = 4;

    /// <summary><c>WEAPONTYPE_PROXIMITYMINE</c>.</summary>
    public const int ProximityMine = 7;
}

/// <summary>The <c>WEAPONCATEGORY_*</c> values the inventory itself tests.</summary>
internal static class Igi2WeaponCategory
{
    /// <summary><c>WEAPONCATEGORY_THERMOCHEMICAL</c>.</summary>
    public const int ThermoChemical = 11;

    /// <summary><c>WEAPONCATEGORY_BINOCULARS</c>.</summary>
    public const int Binoculars = 12;
}
