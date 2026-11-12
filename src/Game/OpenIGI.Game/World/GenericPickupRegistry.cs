using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Level;

namespace OpenIGI.Game.World;

/// <summary>One authored retail <c>GenericPickup</c> task.</summary>
public sealed class GenericPickup : Saves.IExchangedState
{
    /// <summary>Initializes a new instance of the <see cref="GenericPickup"/> class.</summary>
    public GenericPickup(DeclaredTask task, Vector3d position, Matrix3f orientation, string modelName)
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
    public bool IsCollected { get; set; }

    // Not retail: Project I.G.I. kept no save games. Whether it was taken is all play changes of it.
    void Saves.IExchangedState.Exchange(Saves.StateExchange exchange) => IsCollected = exchange.Bool(IsCollected);

    /// <summary>
    /// Gets the horizontal reach, in engine units: the model's bounding radius times its largest scale factor
    /// (<c>0x4D0950</c> by <c>+152..+160</c>, which are 1 for a placed item), but never under
    /// <see cref="PickupReach.Radius"/>.
    /// </summary>
    public double ReachRadius { get; set; } = PickupReach.Radius;
}
