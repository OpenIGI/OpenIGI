using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>One authored retail <c>GenericPickup</c> task.</summary>
public sealed class GenericPickup
{
    internal GenericPickup(DeclaredTask task, Vector3d position, Matrix3f orientation, string modelName)
    {
        Task = task;
        Position = position;
        Orientation = orientation;
        ModelName = modelName;
    }

    /// <summary>Gets the task whose <c>isPickedUp</c> variable represents this item.</summary>
    public DeclaredTask Task { get; }

    /// <summary>Gets the pickup centre in engine units.</summary>
    public Vector3d Position { get; }

    /// <summary>Gets the authored model orientation.</summary>
    public Matrix3f Orientation { get; }

    /// <summary>Gets the authored model name.</summary>
    public string ModelName { get; }

    /// <summary>Gets whether the item has been consumed by the player.</summary>
    public bool IsCollected { get; internal set; }

    /// <summary>
    /// Gets the horizontal reach, in engine units: the model's bounding radius times its largest scale factor
    /// (<c>0x4D0950</c> by <c>+152..+160</c>, which are 1 for a placed item), but never under
    /// <see cref="PickupReach.Radius"/>.
    /// </summary>
    public double ReachRadius { get; internal set; } = PickupReach.Radius;
}

/// <summary>Runs the original game's automatic, proximity-based generic item pickups.</summary>
/// <remarks>
/// Retail's <c>GenericPickup</c> proximity handler at <c>0x46C630</c> answers the message the player sends every
/// tick (<c>0x410F70</c>) with the Human's ORIGIN (<c>+32</c>, 3877.2 units above the feet standing), accepts a
/// planar distance below the item's reach (<see cref="GenericPickup.ReachRadius"/>) and a vertical separation
/// below 4096 units, then publishes the task's <c>isPickedUp</c> variable. Tested from the feet, an item resting on
/// a crate about a metre up sat more than 4096 units above them and only came into reach mid-jump (Mission 2's
/// explosives room). Its following cleanup removes the item from the spatial world but leaves
/// the task and its exported variable available to mission expressions. This registry preserves that
/// lifetime split: the task remains in the tree while the renderer and collision index ignore the
/// consumed item.
/// </remarks>
public sealed class GenericPickupRegistry
{
    private readonly List<GenericPickup> _items = [];
    private readonly Dictionary<GameTask, GenericPickup> _byTask = [];

    private GenericPickupRegistry()
    {
    }

    /// <summary>Gets every authored generic pickup, including consumed items for diagnostics.</summary>
    public IReadOnlyList<GenericPickup> Items => _items;

    /// <summary>Builds the pickup list and exports each task's live <c>isPickedUp</c> variable.</summary>
    /// <param name="root">The level root.</param>
    /// <param name="variables">The script variables the pickups export into.</param>
    /// <param name="placements">The level's placements, whose models size each item's reach; none keeps 2867.2.</param>
    public static GenericPickupRegistry Build(
        GameTask root,
        ScriptVarRegistry variables,
        IEnumerable<WorldPlacement>? placements = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(variables);

        GenericPickupRegistry registry = new();
        registry.Collect(root, variables);
        foreach (WorldPlacement placement in placements ?? [])
        {
            if (placement.Owner is { } owner
                && registry._byTask.TryGetValue(owner, out GenericPickup? pickup)
                && placement.Model is { NativeMarkerBound: > 0f } model
                && float.IsFinite(model.NativeMarkerBound))
            {
                pickup.ReachRadius = Math.Max(pickup.ReachRadius, model.NativeMarkerBound);
            }
        }

        return registry;
    }

    /// <summary>Consumes every pickup within the retail reach of the Human's origin this tick.</summary>
    /// <param name="origin">The Human's origin (<c>+32</c>), not its feet.</param>
    public IReadOnlyList<GenericPickup> CollectAt(Vector3d origin)
    {
        if (Mirrored)
        {
            return [];
        }

        List<GenericPickup>? collected = null;
        foreach (GenericPickup pickup in _items)
        {
            if (pickup.IsCollected
                || !ConditionalContainerGate.IsTaskActive(pickup.Task)
                || !PickupReach.IsWithinRange(origin, pickup.Position, pickup.ReachRadius))
            {
                continue;
            }

            pickup.IsCollected = true;
            (collected ??= []).Add(pickup);
        }

        return collected ?? [];
    }

    /// <summary>
    /// Gets or sets whether the pickups are a co-op client's, taken when the server says who took them rather than by whoever
    /// walks over them here.
    /// </summary>
    public bool Mirrored { get; set; }

    /// <summary>Takes a pickup out of the level, as walking over it does.</summary>
    /// <param name="pickup">The pickup.</param>
    /// <returns><see langword="false"/> when it was already taken.</returns>
    public bool Take(GenericPickup pickup)
    {
        ArgumentNullException.ThrowIfNull(pickup);
        if (pickup.IsCollected || !_items.Contains(pickup))
        {
            return false;
        }

        pickup.IsCollected = true;
        return true;
    }

    /// <summary>Returns whether a world placement's owner has already been removed by pickup.</summary>
    public bool IsCollected(GameTask owner) =>
        owner is not null && _byTask.TryGetValue(owner, out GenericPickup? pickup) && pickup.IsCollected;

    private void Collect(GameTask task, ScriptVarRegistry variables)
    {
        if (task is DeclaredTask declared
            && (string.Equals(declared.Type.Name, "GenericPickup", StringComparison.Ordinal)
                || string.Equals(declared.Type.Name, "AmmoPickup", StringComparison.Ordinal))
            && declared.TryGet("Position", out Vector3d position))
        {
            string modelName = declared.TryGetValue("Model", out object? rawModel) && rawModel is string model
                ? ResolveModelName(model)
                : declared.TryGetValue("ID", out object? rawId) && rawId is string id
                    ? AmmoPickupCatalog.ModelNameForScriptId(id)
                    : string.Empty;
            Matrix3f orientation = declared.TryGetValue("Orientation", out object? rawOrientation)
                && rawOrientation is Vector3f angles
                    ? Matrix3f.FromEngineEulerAngles(angles.X, angles.Y, angles.Z)
                    : Matrix3f.Identity;

            GenericPickup pickup = new(declared, position, orientation, modelName);
            _items.Add(pickup);
            _byTask.Add(declared, pickup);
            variables.BindBool(declared, "isPickedUp", () => pickup.IsCollected, value => pickup.IsCollected = value);
        }

        foreach (GameTask child in task.Children)
        {
            Collect(child, variables);
        }
    }

    private static string ResolveModelName(string model) =>
        AmmoPickupCatalog.ModelNameForScriptId(model) is { Length: > 0 } ammoModel
            ? ammoModel
            : model;
}
