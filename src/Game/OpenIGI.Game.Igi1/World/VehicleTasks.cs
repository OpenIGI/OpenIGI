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

        // The last thing a car's run does is its own destruction (Car_Run's tail, 0x42E320 -> 0x42E5A0).
        Dying?.Invoke();
    }

    /// <summary>
    /// Gets or sets the vehicle's destruction for this tick -- the blasts of one that was hit, its countdown and its wreck --
    /// or <see langword="null"/> for a vehicle the level's destructibles do not keep.
    /// </summary>
    internal Action? Dying { get; set; }

    /// <summary>Runs the type's own handler.</summary>
    /// <param name="vehicles">The level's vehicles.</param>
    /// <returns>Whether the handler ran, and with it the task's children.</returns>
    private protected abstract bool Run(LevelVehicles vehicles);
}

/// <summary>A vehicle that carries an eye, guns and a door: a car or a helicopter, which the levels declare alike.</summary>
public abstract class MotorVehicleTask : VehicleTask
{
    /// <summary>Gets a value indicating whether the vehicle is a helicopter, whose eye sits at its origin and tilts with it.</summary>
    public abstract bool IsHelicopter { get; }

    /// <summary>Gets the model it is drawn as, whose attachments are its guns ("Model"), or <see langword="null"/> where the level names none.</summary>
    public string? Model => TryGetValue("Model", out object? value) ? value as string : null;

    /// <summary>Gets the expression that opens its door ("Open door").</summary>
    public string OpenDoor => DeclaredTaskValues.TextOrEmpty(this, "Open door");

    /// <summary>Gets the expression that lets it see and fire ("Can fire").</summary>
    public string CanFire => DeclaredTaskValues.TextOrEmpty(this, "Can fire");

    /// <summary>Gets the height of its view cone, in degrees ("ViewCone Alpha (degrees)").</summary>
    public float ViewConeAlpha => DeclaredTaskValues.RealConverted(this, "ViewCone Alpha (degrees)", 180f);

    /// <summary>Gets the width of its view cone, in degrees ("ViewCone Gamma (degrees)").</summary>
    public float ViewConeGamma => DeclaredTaskValues.RealConverted(this, "ViewCone Gamma (degrees)", 360f);

    /// <summary>Gets the length of its view cone, in metres ("ViewCone Length (meter)").</summary>
    public float ViewConeLength => DeclaredTaskValues.RealConverted(this, "ViewCone Length (meter)", 0f);
}

/// <summary>A car or a truck: Project I.G.I.'s <c>Car</c>.</summary>
public sealed class CarTask : MotorVehicleTask
{
    /// <inheritdoc/>
    public override bool IsHelicopter => false;

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
public sealed class HeliTask : MotorVehicleTask
{
    /// <inheritdoc/>
    public override bool IsHelicopter => true;

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

    /// <summary>Gets the id of the car it drives ("Car ID"), or <see langword="null"/> where the level gives none.</summary>
    public int? CarId => TryGetValue("Car ID", out object? value) && value is int id ? id : null;

    /// <summary>Gets the id of the graph it drives on ("Graph ID"), or <see langword="null"/> where the level gives none.</summary>
    public int? GraphId => TryGetValue("Graph ID", out object? value) && value is int id ? id : null;

    /// <summary>Gets the id of the route it follows ("Route ID"), or <see langword="null"/> where the level gives none.</summary>
    public int? RouteId => TryGetValue("Route ID", out object? value) && value is int id ? id : null;

    /// <summary>Gets or sets the car it drives and the route it follows, or <see langword="null"/> for one this level's graphs gave no route.</summary>
    internal CarAiState? Driver { get; set; }

    /// <inheritdoc/>
    private protected override bool Run(LevelVehicles vehicles)
    {
        if (Driver is not { } driver)
        {
            return false;
        }

        vehicles.RunCarAi(driver);
        return true;
    }
}

/// <summary>A soldier's mounted gun that fires by itself: Project I.G.I.'s <c>AIStationaryGunHolder</c>.</summary>
public sealed class StationaryGunHolderTask : VehicleTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = StationaryGunHolders.TypeName;

    /// <summary>Gets the model that holds its guns as attachments ("Holder model"), or <see langword="null"/> where the level names none.</summary>
    public string? HolderModel => TryGetValue("Holder model", out object? value) ? value as string : null;

    /// <summary>Gets the expression that gives the team of the holder ("Team expression").</summary>
    public string TeamExpression => DeclaredTaskValues.TextOrEmpty(this, "Team expression");

    /// <summary>Gets the expression that says whether the holder is on ("On expression").</summary>
    public string OnExpression => DeclaredTaskValues.TextOrEmpty(this, "On expression");

    /// <summary>Gets the height of the view cone, in degrees ("Viewcone Alpha").</summary>
    public float ViewconeAlpha => DeclaredTaskValues.RealConverted(this, "Viewcone Alpha", 360f);

    /// <summary>Gets the width of the view cone, in degrees ("Viewcone Gamma").</summary>
    public float ViewconeGamma => DeclaredTaskValues.RealConverted(this, "Viewcone Gamma", 360f);

    /// <summary>Gets the length of the view cone, in metres ("Viewcone Length").</summary>
    public float ViewconeLength => DeclaredTaskValues.RealConverted(this, "Viewcone Length", 10f);

    /// <inheritdoc/>
    private protected override bool Run(LevelVehicles vehicles)
    {
        vehicles.RunGunHolder(this);
        return true;
    }
}

/// <summary>A jet that flies its cut-scene's path: Project I.G.I.'s <c>Plane</c>.</summary>
/// <remarks>
/// Its run handler (<c>0x433690</c>) starts <c>su27</c> unless its "Play sound" expression reads 0, then runs its children.
/// The sound is the vehicles' (<see cref="VehicleBodies.VehicleSoundSources"/>), which reads the expression from here.
/// </remarks>
public sealed class PlaneTask : VehicleTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Plane";

    /// <summary>Gets the expression that says whether the engine sounds ("Play sound"); empty is always.</summary>
    public string PlaySound => DeclaredTaskValues.TextOrEmpty(this, "Play sound");

    /// <inheritdoc/>
    private protected override bool Run(LevelVehicles vehicles) => false;
}

/// <summary>One car of a train: Project I.G.I.'s <c>Train</c>, a locomotive with its wagons as children.</summary>
/// <remarks>
/// Its run handler (<c>0x42B700</c>) starts its loop unless its "Play Sound" expression reads 0 -- <c>train_loop_1</c> for a
/// car that pulls wagons, <c>train_loop_2</c> for any other -- sets the loop's two parameters from its locomotive's thrust,
/// then runs its children. The sound is the vehicles' (<see cref="VehicleBodies.VehicleSoundSources"/>).
/// </remarks>
public sealed class TrainTask : VehicleTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Train";

    /// <summary>Gets the expression that says whether the car sounds ("Play Sound"); empty is always.</summary>
    public string PlaySound => DeclaredTaskValues.TextOrEmpty(this, "Play Sound");

    /// <summary>Gets a value indicating whether this car pulls wagons: one of its children is a car too.</summary>
    public bool PullsWagons => Children.Any(static child => child is TrainTask);

    /// <inheritdoc/>
    private protected override bool Run(LevelVehicles vehicles) => false;
}
