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
}

/// <summary>Runs the original game's automatic, proximity-based generic item pickups.</summary>
/// <remarks>
/// Retail's <c>GenericPickup::Update</c> at <c>0x46C630</c> uses the player's feet, accepts a planar
/// distance below 2867.2 units and a vertical separation below 4096 units, then publishes the task's
/// <c>isPickedUp</c> variable. Its following cleanup removes the item from the spatial world but leaves
/// the task and its exported variable available to mission expressions. This registry preserves that
/// lifetime split: the task remains in the tree while the renderer and collision index ignore the
/// consumed item.
/// </remarks>
public sealed class GenericPickupRegistry
{
    /// <summary>The original's horizontal pickup radius, in engine units.</summary>
    public const double PickupRadius = 2867.2;

    /// <summary>The original's vertical pickup tolerance, in engine units.</summary>
    public const double VerticalTolerance = 4096.0;

    private readonly List<GenericPickup> _items = [];
    private readonly Dictionary<GameTask, GenericPickup> _byTask = [];

    private GenericPickupRegistry()
    {
    }

    /// <summary>Gets every authored generic pickup, including consumed items for diagnostics.</summary>
    public IReadOnlyList<GenericPickup> Items => _items;

    /// <summary>Builds the pickup list and exports each task's live <c>isPickedUp</c> variable.</summary>
    public static GenericPickupRegistry Build(GameTask root, ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(variables);

        GenericPickupRegistry registry = new();
        registry.Collect(root, variables);
        return registry;
    }

    /// <summary>Consumes every pickup whose retail feet-range test succeeds this tick.</summary>
    public IReadOnlyList<GenericPickup> CollectAt(Vector3d position)
    {
        List<GenericPickup>? collected = null;
        foreach (GenericPickup pickup in _items)
        {
            if (pickup.IsCollected
                || !ConditionalContainerGate.IsTaskActive(pickup.Task)
                || !IsWithinRange(position, pickup.Position))
            {
                continue;
            }

            pickup.IsCollected = true;
            (collected ??= []).Add(pickup);
        }

        return collected ?? [];
    }

    /// <summary>Returns whether a world placement's owner has already been removed by pickup.</summary>
    public bool IsCollected(GameTask owner) =>
        owner is not null && _byTask.TryGetValue(owner, out GenericPickup? pickup) && pickup.IsCollected;

    /// <summary>Tests the exact horizontal/vertical split used by retail.</summary>
    public static bool IsWithinRange(Vector3d player, Vector3d pickup)
    {
        double x = pickup.X - player.X;
        double y = pickup.Y - player.Y;
        return Math.Sqrt((x * x) + (y * y)) < PickupRadius
            && Math.Abs(pickup.Z - player.Z) < VerticalTolerance;
    }

    private void Collect(GameTask task, ScriptVarRegistry variables)
    {
        if (task is DeclaredTask declared
            && (string.Equals(declared.Type.Name, "GenericPickup", StringComparison.Ordinal)
                || string.Equals(declared.Type.Name, "AmmoPickup", StringComparison.Ordinal))
            && TryReadPosition(declared, out Vector3d position))
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

    private static bool TryReadPosition(DeclaredTask task, out Vector3d position)
    {
        if (task.TryGetValue("Position", out object? raw) && raw is Vector3d value)
        {
            position = value;
            return true;
        }

        position = default;
        return false;
    }

    private static string ResolveModelName(string model) =>
        AmmoPickupCatalog.ModelNameForScriptId(model) is { Length: > 0 } ammoModel
            ? ammoModel
            : model;
}
