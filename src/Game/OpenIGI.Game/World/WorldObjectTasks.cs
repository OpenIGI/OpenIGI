using OpenIGI.Core.Numerics;
using OpenIGI.Game.Level;

namespace OpenIGI.Game.World;

/// <summary>A door: Project I.G.I.'s <c>Door</c>, whose run handler (<c>0x446D10</c>) runs its children and then its own state machine.</summary>
/// <remarks>
/// <see cref="DoorRegistry"/> keeps the level's doors and each one's state; the task runs its door where the level's walk
/// reaches it. Its parameters are still the ones the level declares, read by name.
/// </remarks>
public sealed class DoorTask : DeclaredLevelTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Door";

    /// <summary>Gets or sets the level's doors, which step this one; none until they are built.</summary>
    internal DoorRegistry? Doors { get; set; }

    /// <summary>Gets or sets the door this task is, or <see langword="null"/> until a registry keeps it.</summary>
    internal PlacedDoor? Door { get; set; }

    /// <summary>Gets a value indicating whether the door's lock can be picked ("Pickable").</summary>
    public bool IsPickable => DeclaredTaskValues.BoolStrict(this, "Pickable");

    /// <summary>Gets how long the lock takes to pick, in seconds ("Pick lock time (s)").</summary>
    public float PickLockSeconds => DeclaredTaskValues.RealFromNumber(this, "Pick lock time (s)", 4f);

    /// <summary>Gets the expression that says whether the door is locked ("Locked expression").</summary>
    public string LockedExpression => DeclaredTaskValues.TextOrFallback(this, "Locked expression", string.Empty);

    /// <summary>Gets the expression that opens the door ("Open door expression").</summary>
    public string OpenExpression => DeclaredTaskValues.TextOrFallback(this, "Open door expression", string.Empty);

    /// <summary>Gets the expression that closes the door ("Close door expression").</summary>
    public string CloseExpression => DeclaredTaskValues.TextOrFallback(this, "Close door expression", string.Empty);

    /// <summary>Gets where the door stands closed ("Position start").</summary>
    public Vector3d PositionStart => Declared("Position start", default(Vector3d));

    /// <summary>Gets how far along X a sliding door travels ("Position stop X").</summary>
    public float PositionStopX => Declared("Position stop X", 0f);

    /// <summary>Gets how far along Y a sliding door travels ("Position stop Y").</summary>
    public float PositionStopY => Declared("Position stop Y", 0f);

    /// <summary>Gets the slider's position ("Position slider").</summary>
    public float PositionSlider => Declared("Position slider", 0f);

    /// <summary>Gets how far a swinging door opens ("Max angle").</summary>
    public float MaxAngle => Declared("Max angle", 0f);

    /// <summary>Gets how long the door takes to open, in seconds ("Open time"); one second where the level gives none.</summary>
    public float OpenTime => Declared("Open time", 1f);

    /// <summary>Gets the definition played as the door opens ("Open sound").</summary>
    public string OpenSound => Declared("Open sound", string.Empty);

    /// <summary>Gets the definition played as the door closes ("Close sound").</summary>
    public string CloseSound => Declared("Close sound", string.Empty);

    /// <summary>Gets the definition played while the door moves ("Move sound").</summary>
    public string MoveSound => Declared("Move sound", string.Empty);

    /// <summary>Gets the door's authored orientation as a matrix's nine reals ("Orientation": three angles, or the one about Z).</summary>
    public float[] BaseOrientation => TryGetValue("Orientation", out object? raw)
        ? raw switch
        {
            Vector3f angles => RetailOrientation.Euler(angles.X, angles.Y, angles.Z),
            float gamma => RetailOrientation.Euler(0f, 0f, gamma),
            _ => [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f],
        }
        : [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];

    /// <inheritdoc/>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.RunChildren(this);
        if (Door is { } door)
        {
            Doors?.RunFromWalk(door);
        }
    }
}

/// <summary>A fan, a dome or a turning sign: Project I.G.I.'s <c>RotatingObject</c>, whose run handler (<c>0x439090</c>) runs no children.</summary>
/// <remarks><see cref="RotatingObjectRegistry"/> keeps each one's speed and angle; the task steps its own where the level's walk reaches it.</remarks>
public sealed class RotatingObjectTask : DeclaredLevelTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "RotatingObject";

    /// <summary>Gets or sets the level's rotating objects, which step this one; none until they are built.</summary>
    internal RotatingObjectRegistry? Rotators { get; set; }

    /// <summary>Gets or sets the object this task turns, or <see langword="null"/> until a registry steps it from the walk.</summary>
    internal PlacedRotatingObject? Rotator { get; set; }

    /// <summary>Gets how fast it turns about X, in degrees a second ("Degrees per second X-axis").</summary>
    public float DegreesPerSecondX => DeclaredTaskValues.RealFromNumberOrShort(this, "Degrees per second X-axis");

    /// <summary>Gets how fast it turns about Y, in degrees a second ("Degrees per second Y-axis").</summary>
    public float DegreesPerSecondY => DeclaredTaskValues.RealFromNumberOrShort(this, "Degrees per second Y-axis");

    /// <summary>Gets how fast it turns about Z, in degrees a second ("Degrees per second Z-axis").</summary>
    public float DegreesPerSecondZ => DeclaredTaskValues.RealFromNumberOrShort(this, "Degrees per second Z-axis");

    /// <summary>Gets how fast its turn about X gathers ("Acceleration, degrees per second X-axis").</summary>
    public float AccelerationX => DeclaredTaskValues.RealFromNumberOrShort(this, "Acceleration, degrees per second X-axis");

    /// <summary>Gets how fast its turn about Y gathers ("Acceleration, degrees per second Y-axis").</summary>
    public float AccelerationY => DeclaredTaskValues.RealFromNumberOrShort(this, "Acceleration, degrees per second Y-axis");

    /// <summary>Gets how fast its turn about Z gathers ("Acceleration, degrees per second Z-axis").</summary>
    public float AccelerationZ => DeclaredTaskValues.RealFromNumberOrShort(this, "Acceleration, degrees per second Z-axis");

    /// <summary>Gets the expression that says whether it turns ("On expression").</summary>
    public string OnExpression => DeclaredTaskValues.TextTrimmed(this, "On expression");

    /// <summary>Gets the definition played while it turns ("Rotate sound").</summary>
    public string RotateSound => DeclaredTaskValues.TextTrimmed(this, "Rotate sound");

    /// <summary>Gets a value indicating whether a round breaks it ("Destructable").</summary>
    public bool IsDestructable => Declared("Destructable", false);

    /// <inheritdoc/>
    /// <remarks>With nothing to step it, it is walked as a plain container is.</remarks>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (Rotator is { } rotator && Rotators is { } rotators)
        {
            rotators.RunFromWalk(rotator);
        }
        else
        {
            level.RunChildren(this);
        }
    }
}

/// <summary>A weapon or a box of ammunition lying in the level: Project I.G.I.'s <c>GunPickup</c> and <c>AmmoPickup</c>.</summary>
/// <remarks>
/// Its run handler deletes the task once the pickup has been taken (<see cref="TakenPickupTask"/>), which the registry that
/// keeps the pickup tells it.
/// </remarks>
public sealed class PickupTask : DeclaredLevelTask
{
    /// <summary>The script names level data instantiates this class's types under.</summary>
    public static readonly IReadOnlyList<string> TypeNames = ["GunPickup", "AmmoPickup"];

    /// <summary>Gets or sets what the task does once its pickup is taken; none until a registry keeps the pickup.</summary>
    internal TakenPickupUpdate? Taken { get; set; }

    /// <inheritdoc/>
    /// <remarks>One no registry keeps is walked as a plain container is.</remarks>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (Taken is { } taken)
        {
            taken.Run(this);
        }
        else
        {
            level.RunChildren(this);
        }
    }
}

/// <summary>A lift: Project I.G.I.'s <c>Elevator</c>.</summary>
/// <remarks>
/// Its run handler (<c>0x43D280</c>) runs its children first -- the doors, switches and indicators that ride it -- then its
/// own floor expressions and its move, after which it carries what stands on it. <see cref="ElevatorRegistry"/> keeps each
/// lift's state; what steps the lifts is the task's <see cref="Runner"/>.
/// </remarks>
public sealed class ElevatorTask : DeclaredLevelTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Elevator";

    /// <summary>Gets or sets what carries out the lift's run handler; <see langword="null"/> while nothing does.</summary>
    public ITaskRun<ElevatorTask>? Runner { get; set; }

    /// <summary>Gets or sets the lift this task is, or <see langword="null"/> until a registry keeps it.</summary>
    public PlacedElevator? Elevator { get; set; }

    /// <summary>The number of "Wanted floor" expressions a lift declares.</summary>
    public const int WantedFloorCount = 10;

    /// <summary>Gets where the car stands at the level's start ("Position"), or <see langword="null"/> where the level gives none.</summary>
    public Vector3d? Position => TryGetValue("Position", out object? value) && value is Vector3d position ? position : null;

    /// <summary>Gets the car's authored orientation ("Orientation").</summary>
    public Matrix3f Orientation => TryGetValue("Orientation", out object? value) && value is Vector3f angles
        ? WorldScene.ToOrientation(angles)
        : Matrix3f.Identity;

    /// <summary>Gets the car's model ("Model").</summary>
    public string Model => Declared("Model", string.Empty);

    /// <summary>Gets the id of the spline the car rides ("SplineObj taskid"), or <see langword="null"/> where the level gives none.</summary>
    public int? SplineTaskId => TryGetValue("SplineObj taskid", out object? value) && value is int id ? id : null;

    /// <summary>Gets the car's top speed, in metres a second ("Max speed").</summary>
    public double MaxSpeed => Declared("Max speed", 1f);

    /// <summary>Gets how fast the car gathers and loses speed ("Speed inertia").</summary>
    public double SpeedInertia => Declared("Speed inertia", 1f);

    /// <summary>Gets the expression that lets the car leave ("Can start").</summary>
    public string CanStart => Declared("Can start", string.Empty);

    /// <summary>Gets the expression that sends the car up a floor ("Floor up").</summary>
    public string FloorUp => Declared("Floor up", string.Empty);

    /// <summary>Gets the expression that sends the car down a floor ("Floor down").</summary>
    public string FloorDown => Declared("Floor down", string.Empty);

    /// <summary>Gets the definition played as the car starts ("Start sound").</summary>
    public string StartSound => Declared("Start sound", string.Empty);

    /// <summary>Gets the definition played as the car stops ("Stop sound").</summary>
    public string StopSound => Declared("Stop sound", string.Empty);

    /// <summary>Gets the definition played while the car moves ("Move sound").</summary>
    public string MoveSound => Declared("Move sound", string.Empty);

    /// <summary>Gets the expression that calls the car to a floor ("Wanted floor 0" to "Wanted floor 9").</summary>
    /// <param name="floor">The floor, from zero.</param>
    /// <returns>The expression, empty where the level gives none.</returns>
    public string WantedFloor(int floor) =>
        Declared(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Wanted floor {floor}"), string.Empty);

    /// <inheritdoc/>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (Runner is { } runner)
        {
            runner.Run(this, level);
        }
        else
        {
            level.RunWithoutHandler(this);
        }
    }
}
