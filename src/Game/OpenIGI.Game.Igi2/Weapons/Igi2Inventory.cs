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

/// <summary>The most of one ammunition a human may carry.</summary>
/// <param name="AmmoNumber">The ammunition's number, its <c>AMMO_ID_*</c> value.</param>
/// <param name="Max">The limit, or <see cref="Igi2AmmoItem.NoLimit"/>.</param>
public readonly record struct Igi2AmmoLimit(int AmmoNumber, int Max);

/// <summary>The <c>WEAPONCATEGORY_*</c> values the inventory itself tests.</summary>
internal static class Igi2WeaponCategory
{
    /// <summary><c>WEAPONCATEGORY_THERMOCHEMICAL</c>.</summary>
    public const int ThermoChemical = 11;

    /// <summary><c>WEAPONCATEGORY_BINOCULARS</c>.</summary>
    public const int Binoculars = 12;
}
