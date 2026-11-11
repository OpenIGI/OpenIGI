using OpenIGI.Game.Level;

namespace OpenIGI.Game.World;

/// <summary>A task of a level's vehicles: a car, a helicopter, what steers a car, or a mounted-gun holder.</summary>
/// <remarks>
/// Each has a run handler of its own in the original. Its state is kept by the level's vehicles
/// (<see cref="LevelVehicles"/>), which the task runs through where the level's walk reaches it.
/// </remarks>
public abstract class VehicleTask : DeclaredLevelTask
{
    /// <summary>Gets or sets the level's vehicles; none until a level's are built.</summary>
    internal LevelVehicles? Vehicles { get; set; }

    /// <inheritdoc/>
    /// <remarks>With no vehicles that run in this walk, it is walked as a plain container is.</remarks>
    public sealed override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (Vehicles is not { } vehicles || !vehicles.RunsIn(level) || !Run(vehicles))
        {
            level.RunChildren(this);
        }
    }

    /// <summary>Runs the type's own handler.</summary>
    /// <param name="vehicles">The level's vehicles.</param>
    /// <returns>Whether the handler ran, and with it the task's children.</returns>
    private protected abstract bool Run(LevelVehicles vehicles);
}

/// <summary>A car or a truck: Project I.G.I.'s <c>Car</c>.</summary>
public sealed class CarTask : VehicleTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Car";

    /// <inheritdoc/>
    private protected override bool Run(LevelVehicles vehicles)
    {
        vehicles.RunCar(this);
        return true;
    }
}

/// <summary>A helicopter: Project I.G.I.'s <c>Heli</c>.</summary>
public sealed class HeliTask : VehicleTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Heli";

    /// <inheritdoc/>
    private protected override bool Run(LevelVehicles vehicles)
    {
        vehicles.RunHeli(this);
        return true;
    }
}

/// <summary>What steers a car along the level's graph: Project I.G.I.'s <c>CarAI</c>.</summary>
public sealed class CarAiTask : VehicleTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "CarAI";

    /// <inheritdoc/>
    private protected override bool Run(LevelVehicles vehicles) => vehicles.RunCarAi(this);
}

/// <summary>A soldier's mounted gun that fires by itself: Project I.G.I.'s <c>AIStationaryGunHolder</c>.</summary>
public sealed class StationaryGunHolderTask : VehicleTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = StationaryGunHolders.TypeName;

    /// <inheritdoc/>
    private protected override bool Run(LevelVehicles vehicles)
    {
        vehicles.RunGunHolder(this);
        return true;
    }
}
