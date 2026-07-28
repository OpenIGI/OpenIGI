namespace OpenIGI.Game.Player;

/// <summary>
/// One of the nine weapon categories <c>DefineHumanPlayerWeaponCategory</c> publishes.
/// </summary>
/// <remarks>
/// The native (<c>0x410240</c>) decodes a flat <c>count, ids…</c> stream into nine fixed
/// <c>{ int count; int id[8]; }</c> records at <c>0x56E0A0</c>. The record is a fixed 36 bytes whatever
/// the count, so a category holds at most <see cref="MaximumMembers"/> weapons.
/// </remarks>
public sealed class HumanPlayerWeaponCategory
{
    /// <summary>The number of categories the table holds; the loop bound is fixed, not data-driven.</summary>
    public const int CategoryCount = 9;

    /// <summary>The most weapons one category's record can hold.</summary>
    public const int MaximumMembers = 8;

    private readonly int[] _weaponIds;

    /// <summary>Initializes a new instance of the <see cref="HumanPlayerWeaponCategory"/> class.</summary>
    /// <param name="weaponIds">The weapon ids in the category, in script order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="weaponIds"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">More ids than the record holds.</exception>
    public HumanPlayerWeaponCategory(IReadOnlyList<int> weaponIds)
    {
        ArgumentNullException.ThrowIfNull(weaponIds);
        if (weaponIds.Count > MaximumMembers)
        {
            throw new ArgumentException(
                $"A category record holds at most {MaximumMembers} weapons.",
                nameof(weaponIds));
        }

        _weaponIds = [.. weaponIds];
    }

    /// <summary>Gets the weapon ids in the category.</summary>
    public IReadOnlyList<int> WeaponIds => _weaponIds;

    /// <summary>Gets the number of weapons in the category.</summary>
    public int Count => _weaponIds.Length;
}
