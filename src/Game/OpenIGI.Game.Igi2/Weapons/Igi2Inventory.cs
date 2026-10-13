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

    /// <summary>Gets the fire mode in use, <c>iActiveFireMode</c> (<c>WeaponTypeItem+0x18</c>); the first until a fire mode key changes it.</summary>
    public int ActiveFireMode { get; internal set; }

    /// <summary>Gets or sets a value indicating whether its laser dot shows, <c>isLaserDotOn</c>.</summary>
    public bool IsLaserDotOn { get; set; }

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

    /// <summary>Takes rounds fired straight from what is carried, with no limit applied (<c>Weapon_SingleFireHandler</c>, <c>igi2 0x51CD40</c>).</summary>
    /// <param name="rounds">The rounds.</param>
    internal void Spend(int rounds) => Count -= rounds;

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

    /// <summary>Gets the session's weapons and ammunition the inventory was made over.</summary>
    public Igi2WeaponTables Tables => _tables;

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
    /// The new weapon starts ready, with no delay, no pin out, no fuse and no bullets fired. The zoom of the weapon put
    /// away is the firing's (<see cref="Igi2WeaponFiring.Activate"/>), which comes here.
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

    /// <summary>
    /// Sets what a weapon holds and adds to what is carried of its ammunition, <c>Weapon_SetAmmoForWeapon</c>
    /// (<c>igi2 0x51EE40</c>): the first fire type is loaded with up to a load of the given rounds, the second with one,
    /// and each fire type's ammunition is given its rounds within its limit.
    /// </summary>
    /// <param name="item">The weapon.</param>
    /// <param name="ammoInClip">The rounds to load the first fire type with.</param>
    /// <param name="rounds">The rounds for each fire type's ammunition.</param>
    public void SetAmmoForWeapon(Igi2WeaponItem item, int ammoInClip, IReadOnlyList<int> rounds)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(rounds);
        int perClip = item.WeaponType.AmmoInfo(0, _tables).RoundsPerClip;
        item.SetLoaded(0, ammoInClip < perClip ? ammoInClip : perClip);
        item.SetLoaded(1, 1);
        for (int slot = 0; slot < Igi2WeaponTypeTask.FireTypeSlots; slot++)
        {
            if (item.WeaponType.AmmoInfo(slot, _tables).AmmoType is { } ammoType && FindAmmo(ammoType.AmmoId) is { } carried)
            {
                carried.Add(rounds[slot]);
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

    /// <summary>
    /// Writes its save state, <c>Weapon_ptSerializeParameterList</c>: its ammunition, each with its count, limit and number
    /// (<c>Weapon_ptAmmoSerializeListItemParameterList</c>); its weapons, each with the rounds loaded for its two fire types, its number,
    /// its fire mode, whether it is carried and whether its laser dot is on (<c>Weapon_ptWeaponSerializeListItemParameterList</c>); and
    /// the weapon in hand.
    /// </summary>
    /// <param name="writer">The writer.</param>
    public void WriteState(Saves.Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.List(_ammo, static (w, a) =>
        {
            w.Int(a.Count);
            w.Int(a.Limit);
            w.Int(a.Number);
        });
        writer.List(_weapons, static (w, item) =>
        {
            w.Int(item.Loaded(0));
            w.Int(item.Loaded(1));
            w.Int(item.Number);
            w.Int(item.ActiveFireMode);
            w.Bool(item.IsActive);
            w.Bool(item.IsLaserDotOn);
        });
        writer.Int(CurrentIndex);
    }

    /// <summary>Reads its save state back; the weapon in hand is put in hand again.</summary>
    /// <param name="reader">The reader.</param>
    public void ReadState(Saves.Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        foreach ((int count, int limit, int number) in reader.List(static r => (r.Int(), r.Int(), r.Int())))
        {
            if ((uint)number < (uint)_ammo.Length)
            {
                Igi2AmmoItem item = _ammo[number];
                item.SetLimit(Igi2AmmoItem.NoLimit);
                item.Empty();
                item.Add(count);
                item.SetLimit(limit);
            }
        }

        foreach ((int first, int second, int number, int mode, bool carried, bool dot) in
            reader.List(static r => (r.Int(), r.Int(), r.Int(), r.Int(), r.Bool(), r.Bool())))
        {
            if ((uint)number < (uint)_weapons.Length)
            {
                Igi2WeaponItem item = _weapons[number];
                item.SetLoaded(0, first);
                item.SetLoaded(1, second);
                item.ActiveFireMode = mode;
                item.IsActive = carried;
                item.IsLaserDotOn = dot;
            }
        }

        int current = reader.Int();
        if ((uint)current < (uint)_weapons.Length && _weapons[current].IsActive)
        {
            Activate(_weapons[current]);
        }
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

    /// <summary>It fired this tick, <c>WEAPON_STATUS_FIRE</c> (<c>Weapon_SingleFireHandler</c>, <c>igi2 0x51CD40</c>).</summary>
    Fire = 1,

    /// <summary>A burst has fired all its rounds and waits for the trigger to come up, <c>WEAPON_STATUS_LOCKED</c>.</summary>
    Locked = 2,

    /// <summary>It is waiting out its delay between shots, <c>WEAPON_STATUS_DELAY</c>.</summary>
    Delay = 3,

    /// <summary>It had nothing to fire, <c>WEAPON_STATUS_EMPTY</c>.</summary>
    Empty = 4,

    /// <summary>
    /// Waiting on the server, <c>WEAPON_STATUS_WAITING</c>: multiplayer only, and a status the player's idle state will
    /// not reload a weapon in (<c>HumanPlayer_RunActionIdle</c>, <c>igi2 0x4B4142</c>).
    /// </summary>
    Waiting = 5,

    /// <summary>A pulled grenade went off in hand, <c>WEAPON_STATUS_EXPLODED_IN_HAND</c> (<c>Weapon_RunDelayedHandler</c>, <c>igi2 0x51C4D4</c>).</summary>
    ExplodedInHand = 6,
}

/// <summary>The <c>WEAPONTYPE_*</c> values the ported code tests, <c>WeaponType+0x29C</c>.</summary>
internal static class Igi2WeaponTypeKind
{
    /// <summary><c>WEAPONTYPE_GUN</c>.</summary>
    public const int Gun = 0;

    /// <summary><c>WEAPONTYPE_PISTOL</c>.</summary>
    public const int Pistol = 1;

    /// <summary><c>WEAPONTYPE_SHOTGUN</c>: the last type whose shots count towards accuracy.</summary>
    public const int Shotgun = 2;

    /// <summary><c>WEAPONTYPE_KNIFE</c>.</summary>
    public const int Knife = 3;

    /// <summary><c>WEAPONTYPE_GRENADE</c>.</summary>
    public const int Grenade = 4;

    /// <summary><c>WEAPONTYPE_PROXIMITYMINE</c>.</summary>
    public const int ProximityMine = 7;

    /// <summary><c>WEAPONTYPE_SPECIAL</c>: the thermal goggles and the laser designator, which see in infrared.</summary>
    public const int Special = 9;
}

/// <summary>The <c>WEAPONCATEGORY_*</c> values the inventory itself tests.</summary>
internal static class Igi2WeaponCategory
{
    /// <summary><c>WEAPONCATEGORY_GRENADE</c>.</summary>
    public const int Grenade = 4;

    /// <summary><c>WEAPONCATEGORY_PROXIMITYMINE</c>.</summary>
    public const int ProximityMine = 5;

    /// <summary><c>WEAPONCATEGORY_FLASHBANG</c>.</summary>
    public const int Flashbang = 6;

    /// <summary><c>WEAPONCATEGORY_SMOKEGRENADE</c>.</summary>
    public const int SmokeGrenade = 7;

    /// <summary><c>WEAPONCATEGORY_MEDIPACK</c>.</summary>
    public const int Medipack = 8;

    /// <summary>
    /// Gets whether a category is carried as rounds of itself, with no load to fill: the thrown weapons, the mine and the
    /// medipack, which <c>GunPickup_PickupHandler</c> (<c>igi2 0x46C0A0</c>) and <c>Human_PickupGun</c> (<c>0x4FFF20</c>)
    /// both single out.
    /// </summary>
    /// <param name="category">The <c>WEAPONCATEGORY_*</c> value.</param>
    /// <returns><see langword="true"/> for those five.</returns>
    public static bool IsCarriedAsRounds(int category) =>
        category is Flashbang or Grenade or SmokeGrenade or ProximityMine or Medipack;

    /// <summary><c>WEAPONCATEGORY_THERMOCHEMICAL</c>.</summary>
    public const int ThermoChemical = 11;

    /// <summary><c>WEAPONCATEGORY_BINOCULARS</c>.</summary>
    public const int Binoculars = 12;
}
