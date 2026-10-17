using OpenIGI.Core.Numerics;
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

    /// <summary>Gets or sets the level a falling pickup's lines meet, or <see langword="null"/> while nothing falls.</summary>
    public IIgi2CollisionQuery? Collision { get; set; }

    /// <summary>Gets or sets the run stream a falling pickup's drop sound draws from.</summary>
    public IgiRandom? Random { get; set; }

    /// <summary>Gets or sets what plays a sound at a point.</summary>
    public Action<string, Vector3d>? PlaySound { get; set; }

    /// <summary>
    /// Gets or sets the level's dynamic tasks, <c>Level_ptDynamicQTask</c>, which a pickup made at run time is run in; or
    /// <see langword="null"/> to leave it to <see cref="Run"/>.
    /// </summary>
    public World.Igi2DynamicTasks? Dynamic { get; set; }

    /// <summary>
    /// One pickup's run handler, <c>GunPickup_RunHandler</c> (<c>igi2 0x46A760</c>): it falls if it is falling, and if it was taken it
    /// is deleted and leaves the list.
    /// </summary>
    /// <param name="pickup">The pickup.</param>
    /// <returns>Whether it is still there.</returns>
    public bool Step(Igi2GunPickup pickup)
    {
        ArgumentNullException.ThrowIfNull(pickup);
        if (pickup.IsMoving && Collision is { } world && Random is { } random)
        {
            pickup.Fall(world, random, PlaySound ?? (static (_, _) => { }));
        }

        if (!pickup.IsDelete)
        {
            return true;
        }

        if (_items.Remove(pickup))
        {
            Deleted?.Invoke(pickup);
        }

        return false;
    }

    /// <summary>Puts a pickup made at run time on the list, as <c>GunPickup_CreateHandler</c> (<c>igi2 0x46A729</c>) registers it.</summary>
    /// <param name="pickup">The pickup.</param>
    public void Add(Igi2GunPickup pickup)
    {
        ArgumentNullException.ThrowIfNull(pickup);
        _items.Add(pickup);
        if (Dynamic is { } dynamic)
        {
            pickup.IsDriven = true;
            dynamic.Add(new Driver(this, pickup));
        }
    }

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
        foreach (GameTask task in levelRoot.Descendants())
        {
            if (task is not Igi2GunPickupTask placed
                || tables.WeaponTypes.FirstOrDefault(w => string.Equals(w.WeaponId, placed.WeaponId, StringComparison.Ordinal)) is not { } weaponType)
            {
                continue;
            }

            float radius = weaponType.FixedModel.Length > 0 && models(weaponType.FixedModel) is { } model ? model.NativeMarkerBound : 1f;
            Igi2Pose pose = Igi2Pose.FromAngles(placed.Position, placed.Orientation);
            Igi2GunPickup pickup = new(placed, weaponType, tables, placed.Position, pose.Orientation, radius);
            placed.Pickup = pickup;
            items.Add(pickup);
        }

        Igi2PickupList list = new(items, sight, materials);
        foreach (Igi2GunPickup pickup in items)
        {
            if (pickup.Task is Igi2GunPickupTask placed)
            {
                placed.Pickups = list;
            }
        }

        return list;
    }

    /// <summary>
    /// The pickups' run pass, <c>GunPickup_RunHandler</c> (<c>igi2 0x46A760</c>): each falling one falls, and each taken one is
    /// deleted and leaves the list.
    /// </summary>
    /// <remarks>
    /// Only the pickups nothing else runs: a level's own run in its task's place in the level's tree once the level runs it
    /// (<see cref="Igi2GunPickupTask.Run"/>), and one made at run time in the level's dynamic tasks.
    /// </remarks>
    public void Run()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            Igi2GunPickup pickup = _items[i];
            if (pickup.IsDriven)
            {
                continue;
            }

            if (!Step(pickup))
            {
                i--;
            }
        }
    }

    // A pickup made at run time, run in the level's dynamic tasks.
    private sealed class Driver(Igi2PickupList list, Igi2GunPickup pickup) : World.IIgi2DynamicTask
    {
        public bool Run() => list.Step(pickup);
    }
}
