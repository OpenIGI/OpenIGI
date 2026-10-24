using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Player;

/// <summary>
/// Which carried weapon the player asks for: the number keys, the binocular key and the weapon cycle.
/// </summary>
/// <remarks>
/// <para>
/// The selection half of <see cref="PlayerWeapons"/>. It runs <c>sub_411000</c> and <c>sub_40F760</c>
/// ahead of the state machine and leaves its answer as a pending weapon id; the change state reads
/// that id and, once the rig is out of the frame, calls <see cref="SwapToPending"/> to put the weapon
/// in hand.
/// </para>
/// <para>
/// It shares the carried list with its owner rather than copying it, so a weapon picked up mid-level
/// is selectable on the next tick.
/// </para>
/// </remarks>
internal sealed class PlayerWeaponSelector
{
    private readonly List<CarriedWeapon> _carried;
    private readonly List<int> _cycle = [];
    private readonly List<HumanPlayerWeaponCategory> _categories = [];
    private int _categoryIndex = -1;
    private int _memberIndex;

    private int _cycleIndex = PlayerWeapons.NoCycleIndex;
    private int _pendingWeaponId = -1;
    private int _heldIndex = -1;
    private int _categoryPick = -1;
    private int _wheelPick = -1;
    private int _lastNonBinocularWeaponId = -1;
    private bool _binocularTogglePending;
    private bool _binocularOpenPending;

    /// <summary>Initializes a new instance of the <see cref="PlayerWeaponSelector"/> class.</summary>
    /// <param name="carried">The owner's carried list, shared rather than copied.</param>
    /// <param name="weaponCycle"><c>DefineHumanPlayerWeaponCycle</c>'s list of weapon ids, in order.</param>
    internal PlayerWeaponSelector(List<CarriedWeapon> carried, IReadOnlyList<int> weaponCycle)
    {
        _carried = carried;
        _cycle.AddRange(weaponCycle);
    }

    /// <summary>Gets the weapon id a change is pending to, or <c>-1</c>.</summary>
    internal int PendingWeaponId => _pendingWeaponId;

    /// <summary>Gets the weapon in hand, or <see langword="null"/> before one has been selected.</summary>
    internal CarriedWeapon? InHand => _heldIndex < 0 ? null : _carried[_heldIndex];

    /// <summary>Replaces the nine weapon categories the number keys select.</summary>
    /// <param name="categories"><c>DefineHumanPlayerWeaponCategory</c>'s rows, in script order.</param>
    internal void UseCategories(IReadOnlyList<HumanPlayerWeaponCategory> categories)
    {
        _categories.Clear();
        _categories.AddRange(categories);
    }

    /// <summary>Appends a weapon to the switch cycle unless it is already in it.</summary>
    /// <param name="weaponId">The weapon's <see cref="WeaponType.WeaponId"/>.</param>
    internal void EnsureCycleContains(int weaponId)
    {
        if (!_cycle.Contains(weaponId))
        {
            _cycle.Add(weaponId);
        }
    }

    /// <summary>Consumes a binocular toggle once the binoculars are in hand.</summary>
    /// <returns><see langword="true"/> when a toggle or open request was consumed.</returns>
    internal bool ConsumeBinocularToggle()
    {
        if ((!_binocularTogglePending && !_binocularOpenPending)
            || InHand?.Weapon.Category != WeaponCategory.Binocular)
        {
            return false;
        }

        _binocularTogglePending = false;
        _binocularOpenPending = false;
        return true;
    }

    // 0x411000, the number keys. It runs immediately before the cycler in HumanPlayer_Update and
    // hands its answer over through input frame slot 13 -- a one-tick scratch register that carries no
    // binding of its own. The port passes it directly instead of
    // writing into the frame, which is the one thing here that is not literal.
    internal void SelectCategory(PlayerInputState input)
    {
        // The remaster's wheel answers through the same register, taken here so a number key pressed on the same tick wins.
        _categoryPick = _wheelPick;
        _wheelPick = -1;

        if (_categories.Count == 0)
        {
            return;
        }

        // The original tests all nine in order and takes the LAST that fired, not the first.
        int wanted = -1;
        for (int i = 0; i < HumanPlayerWeaponCategory.CategoryCount; i++)
        {
            PlayerInputSlot slot = PlayerInputSlot.WeaponCategory1 + i;
            if (input.IsRisingEdge(slot))
            {
                wanted = i;
            }
        }

        if (wanted < 0 || wanted >= _categories.Count)
        {
            return;
        }

        // A different category restarts at its first member; the same one steps on, which is what makes
        // pressing 2 twice walk from the Glock to the Colt.
        if (wanted != _categoryIndex)
        {
            _memberIndex = 0;
        }

        HumanPlayerWeaponCategory category = _categories[wanted];
        if (category.Count == 0)
        {
            _categoryIndex = wanted;
            return;
        }

        for (int tried = 0; tried < category.Count; tried++)
        {
            int id = category.WeaponIds[_memberIndex];
            _memberIndex++;
            if (_memberIndex >= category.Count)
            {
                _memberIndex = 0;
            }

            if (IndexOf(id) >= 0)
            {
                _categoryPick = id;
                // Number-key selection and the weapon wheel are two views over the same retail
                // cycle. Keep the wheel cursor on the member selected here; otherwise the next/prev
                // edge starts from the cursor's old position and can return the weapon just selected
                // (or jump to the wrong category) instead of advancing from it.
                int cycleIndex = _cycle.IndexOf(id);
                if (cycleIndex >= 0)
                {
                    _cycleIndex = cycleIndex;
                }

                break;
            }
        }

        _categoryIndex = wanted;
    }

    /// <summary>
    /// Takes a weapon as the weapon wheel picks it: as a number key's pick on the next tick (<see cref="SelectCategory"/>), which
    /// the cycler hands on as the pending change, with the cycle's cursor kept on it so the next and previous keys step on from it.
    /// </summary>
    /// <param name="weaponId">The weapon's id; one not carried, or the one in hand, changes nothing.</param>
    internal void Pick(int weaponId)
    {
        if (IndexOf(weaponId) < 0 || (InHand is { } held && held.Weapon.WeaponId == weaponId))
        {
            return;
        }

        _wheelPick = weaponId;
        int cycleIndex = _cycle.IndexOf(weaponId);
        if (cycleIndex >= 0)
        {
            _cycleIndex = cycleIndex;
        }
    }

    internal void SelectBinoculars(PlayerInputState input)
    {
        if (!input.IsRisingEdge(PlayerInputSlot.Binoculars))
        {
            return;
        }

        int binocularIndex = _carried.FindIndex(
            carried => carried.Weapon.Category == WeaponCategory.Binocular);
        if (binocularIndex < 0)
        {
            return;
        }

        if (_heldIndex == binocularIndex)
        {
            _binocularTogglePending = true;
            _binocularOpenPending = false;
            _pendingWeaponId = IndexOf(_lastNonBinocularWeaponId);
            if (_pendingWeaponId < 0)
            {
                _pendingWeaponId = _carried.FindIndex(
                    carried => carried.Weapon.Category != WeaponCategory.Binocular);
            }

            if (_pendingWeaponId >= 0)
            {
                _pendingWeaponId = _carried[_pendingWeaponId].Weapon.WeaponId;
            }
        }
        else
        {
            _binocularTogglePending = false;
            _binocularOpenPending = true;
            _pendingWeaponId = _carried[binocularIndex].Weapon.WeaponId;
        }
    }

    // 0x40F760, reduced to the edges that reach it in practice.
    internal void CycleWeapons(PlayerInputState input)
    {
        int step = 0;
        if (_cycleIndex == PlayerWeapons.NoCycleIndex || input.IsRisingEdge(PlayerInputSlot.NextWeapon))
        {
            step = 1;
        }

        if (input.IsRisingEdge(PlayerInputSlot.PreviousWeapon))
        {
            step = -1;
        }

        int heldId = InHand is { } held ? held.Weapon.WeaponId : -1;

        // The cycler falls through to the category's answer when neither edge fired -- the original
        // reads it out of the scratch slot at exactly this point.
        if (step == 0 || _cycle.Count == 0)
        {
            if (_categoryPick != -1 && _categoryPick != heldId && IndexOf(_categoryPick) >= 0)
            {
                _pendingWeaponId = _categoryPick;
            }

            return;
        }

        for (int n = _cycle.Count; n > 0; n--)
        {
            _cycleIndex += step;
            if (_cycleIndex >= _cycle.Count)
            {
                _cycleIndex = 0;
            }
            else if (_cycleIndex < 0)
            {
                _cycleIndex = _cycle.Count - 1;
            }

            int candidate = _cycle[_cycleIndex];

            // 0x408720: carried, and selectable -- which in retail every carried weapon is, because
            // the runtime loader always stores 1 in the record's +1756.
            if (IndexOf(candidate) >= 0)
            {
                if (candidate != heldId)
                {
                    _pendingWeaponId = candidate;
                    SetBinocularOpenPending(candidate);
                }

                return;
            }
        }
    }

    /// <summary>Steps the cycle to the next carried weapon, for the auto-switch off a spent grenade.</summary>
    /// <returns><see langword="true"/> when a different carried weapon was selected.</returns>
    /// <remarks>The <c>a2 = 1</c> path of <c>0x40F760</c>, reduced to stepping to the next carried entry.</remarks>
    internal bool SelectNextCarried()
    {
        if (_cycle.Count == 0)
        {
            return false;
        }

        int heldId = InHand is { } held ? held.Weapon.WeaponId : -1;
        for (int n = _cycle.Count; n > 0; n--)
        {
            _cycleIndex++;
            if (_cycleIndex >= _cycle.Count)
            {
                _cycleIndex = 0;
            }

            int candidate = _cycle[_cycleIndex];
            if (IndexOf(candidate) >= 0 && candidate != heldId)
            {
                _pendingWeaponId = candidate;
                return true;
            }
        }

        return false;
    }

    internal void SetBinocularOpenPending(int weaponId)
    {
        if (IndexOf(weaponId) >= 0 && _carried[IndexOf(weaponId)].Weapon.Category == WeaponCategory.Binocular)
        {
            _binocularOpenPending = true;
        }
        else
        {
            _binocularOpenPending = false;
        }
    }

    /// <summary>
    /// Puts the pending weapon in hand and clears the request — the exchange half of <c>0x40F280</c>.
    /// </summary>
    internal void SwapToPending()
    {
        int index = IndexOf(_pendingWeaponId);
        if (index >= 0)
        {
            if (InHand is { Weapon.Category: not WeaponCategory.Binocular } previous)
            {
                _lastNonBinocularWeaponId = previous.Weapon.WeaponId;
            }

            _heldIndex = index;
        }

        _pendingWeaponId = -1;
    }

    internal int IndexOf(int weaponId)
    {
        for (int i = 0; i < _carried.Count; i++)
        {
            if (_carried[i].Weapon.WeaponId == weaponId)
            {
                return i;
            }
        }

        return -1;
    }
}
