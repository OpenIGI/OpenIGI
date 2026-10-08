using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Pickups;

/// <summary>
/// The pickups a level has, <c>GameFunctions_ptPickupQTaskList</c>: in the order they were made, with the level the sight
/// to each is checked through.
/// </summary>
/// <remarks>
/// <c>GunPickup_CreateHandler</c> (<c>igi2 0x46A510</c>) registers each pickup as it is made, so the level's own come in
/// the order it declares them; <c>GunPickup_DeleteHandler</c> (<c>0x46B980</c>) takes one out. Not yet: the other two
/// pickup types, <c>AmmoPickup</c> and <c>GenericPickup</c>, which register on the same list.
/// </remarks>
public sealed class Igi2PickupList
{
    private readonly List<Igi2GunPickup> _items;

    /// <summary>Initializes a new instance of the <see cref="Igi2PickupList"/> class.</summary>
    /// <param name="items">The pickups, in the order they were made.</param>
    /// <param name="sight">The level the sight to a pickup is checked through.</param>
    /// <param name="materials">The session's materials, which a sight passes.</param>
    public Igi2PickupList(IEnumerable<Igi2GunPickup> items, IIgi2SightQuery sight, Igi2GameMaterials materials)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(materials);
        _items = [.. items];
        Sight = sight;
        Materials = materials;
    }

    /// <summary>Raised when a pickup is deleted, once it was taken.</summary>
    public event Action<Igi2GunPickup>? Deleted;

    /// <summary>Gets the pickups, in the order they were made.</summary>
    public IReadOnlyList<Igi2GunPickup> Items => _items;

    /// <summary>Gets the level the sight to a pickup is checked through.</summary>
    public IIgi2SightQuery Sight { get; }

    /// <summary>Gets the session's materials.</summary>
    public Igi2GameMaterials Materials { get; }

    /// <summary>Makes the pickups a level places, as <c>GunPickup_CreateHandler</c> and its internal data make each.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="tables">The session's weapon tables.</param>
    /// <param name="models">Resolves a model name to its mesh, or answers <see langword="null"/>.</param>
    /// <param name="sight">The level the sight to a pickup is checked through.</param>
    /// <param name="materials">The session's materials.</param>
    /// <returns>The list.</returns>
    /// <remarks>
    /// A pickup naming a weapon the session has no type for gives nothing and is left out. Its model is the weapon's
    /// <c>Fixed Model</c>, whose LOD 0 radius sets its reach; a model nothing ships counts a radius of one
    /// (<c>GunPickup_UpdateInternalDataHandler</c>, <c>igi2 0x46BA00</c>).
    /// </remarks>
    public static Igi2PickupList Build(
        GameTask levelRoot,
        Igi2WeaponTables tables,
        Func<string, MefModel?> models,
        IIgi2SightQuery sight,
        Igi2GameMaterials materials)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(models);

        List<Igi2GunPickup> items = [];
        foreach (GameTask task in Igi2TaskTree.Descendants(levelRoot))
        {
            if (task is not Igi2GunPickupTask placed
                || tables.WeaponTypes.FirstOrDefault(w => string.Equals(w.WeaponId, placed.WeaponId, StringComparison.Ordinal)) is not { } weaponType)
            {
                continue;
            }

            float radius = weaponType.FixedModel.Length > 0 && models(weaponType.FixedModel) is { } model ? model.NativeMarkerBound : 1f;
            Igi2Pose pose = Igi2Pose.FromAngles(placed.Position, placed.Orientation);
            items.Add(new Igi2GunPickup(placed, weaponType, tables, placed.Position, pose.Orientation, radius));
        }

        return new Igi2PickupList(items, sight, materials);
    }

    /// <summary>
    /// The pickups' run pass, <c>GunPickup_RunHandler</c> (<c>igi2 0x46A760</c>): each taken one is deleted, and leaves the
    /// list.
    /// </summary>
    public void Run()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            Igi2GunPickup pickup = _items[i];
            if (!pickup.IsDelete)
            {
                continue;
            }

            _items.RemoveAt(i--);
            Deleted?.Invoke(pickup);
        }
    }
}
