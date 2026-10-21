using OpenIGI.Core.Numerics;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Weapons;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.World;

/// <summary>Vehicle and rotor constants published by the retail <c>physicsobj</c> QVM scripts.</summary>
/// <remarks>
/// <c>DefinePhysicsObjType</c> has one common prefix followed by a task-type-specific tail.  The
/// executable's Rotor parser is <c>0x42D9F0</c>; Heli consumes its alternating torque/smoothing values
/// in <c>0x431E30</c>. Running the shipped define scripts preserves those model-specific constants and
/// avoids a Mission-1-only table in the renderer.
/// </remarks>
public sealed class VehiclePhysicsRegistry
{
    private const string LogCategory = "World";
    private const string RotorType = "Rotor";
    private const string HeliType = "Heli";
    private const string CarType = "Car";
    private const string PlaneType = "Plane";

    /// <summary>The task types Project I.G.I.'s <c>physicsobj</c> scripts name, as constant and type name.</summary>
    public static readonly IReadOnlyList<(string Constant, string Type)> TaskTypes =
    [
        ("TASKTYPE_GENERICPHYSICSOBJ", "GenericPhysicsObj"),
        ("TASKTYPE_GENERICPHYSICSMAGICOBJ", "GenericPhysicsMagicObj"),
        ("TASKTYPE_GRENADE", "Grenade"),
        ("TASKTYPE_CAR", CarType),
        ("TASKTYPE_WHEEL", "Wheel"),
        ("TASKTYPE_HELI", HeliType),
        ("TASKTYPE_ROTOR", RotorType),
        ("TASKTYPE_MISSILE", "Missile"),
        ("TASKTYPE_PLANE", PlaneType),
    ];

    /// <summary>
    /// The constants the scripts' type-specific arguments name: the grenade's kinds (<c>0x476730</c>) and the missile's
    /// smoke and guidance (<c>0x004277D3</c>, <c>0x004277EB</c>).
    /// </summary>
    public static readonly IReadOnlyList<(string Name, int Value)> Constants =
    [
        ("GRENADE_EXPLOSIONTYPE_NORMAL", (int)GrenadeExplosionType.Normal),
        ("GRENADE_EXPLOSIONTYPE_FLASHBANG", (int)GrenadeExplosionType.Flashbang),
        ("MISSILE_SMOKE_NONE", (int)MissileSmoke.None),
        ("MISSILE_SMOKE_NORMAL", (int)MissileSmoke.Normal),
        ("MISSILE_TYPE_MISSILE_DIRECT", (int)MissileType.MissileDirect),
        ("MISSILE_TYPE_HIND_HELICOPTER", (int)MissileType.HindHelicopter),
        ("MISSILE_TYPE_MISSILE_BALLISTIC", (int)MissileType.MissileBallistic),
        ("MISSILE_TYPE_MISSILE_ANTITANK", (int)MissileType.MissileAntiTank),
        ("MISSILE_TYPE_MISSILE_KEYBOARD", (int)MissileType.MissileKeyboard),
        ("MISSILE_TYPE_BOMB", (int)MissileType.Bomb),
    ];

    private readonly Dictionary<string, RotorPhysicsDefinition> _rotors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HelicopterPhysicsDefinition> _helicopters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CarPhysicsDefinition> _cars = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PlanePhysicsDefinition> _planes = new(StringComparer.OrdinalIgnoreCase);
    private readonly IOpenIgiLogger _logger;

    /// <summary>Creates an empty registry.</summary>
    public VehiclePhysicsRegistry(IOpenIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets every physics object type the scripts define, vehicles and the rest.</summary>
    public PhysicsObjectTypes Types { get; private set; } = new(TaskTypes, Constants);

    /// <summary>Runs every script under <c>LOCAL:physicsobj</c> and keeps the vehicles' records.</summary>
    /// <param name="resources">Where the scripts are read from, over the file system they are found on.</param>
    /// <returns>How many helicopter, rotor and plane records were kept.</returns>
    public int Load(ResourceRegistry resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        _rotors.Clear();
        _helicopters.Clear();
        _cars.Clear();
        _planes.Clear();

        PhysicsObjectTypes types = new(TaskTypes, Constants, _logger);
        NativeRegistry natives = new();
        types.Register(natives, null);
        try
        {
            types.Load(resources.FileSystem, resources, natives);
        }
        catch (EngineFatalException ex)
        {
            throw new EngineFatalException($"Couldn't load vehicle definition script: {ex.Message}", ex);
        }

        Types = types;
        foreach (PhysicsObjectType type in types.Types)
        {
            Define(new Row(type));
        }

        _logger.Information(LogCategory,
            $"Vehicle physics: {_helicopters.Count} helicopter(s), {_cars.Count} car(s), {_planes.Count} plane(s), {_rotors.Count} rotor model(s).");
        return _helicopters.Count + _rotors.Count + _planes.Count;
    }

    /// <summary>Looks up the Heli control record bound to a body model.</summary>
    public bool TryGetHelicopter(string model, out HelicopterPhysicsDefinition definition) =>
        _helicopters.TryGetValue(model, out definition);

    /// <summary>Looks up the Rotor record bound to a rotor model.</summary>
    public bool TryGetRotor(string model, out RotorPhysicsDefinition definition) =>
        _rotors.TryGetValue(model, out definition);

    private static float Single(QvmValue[] arguments, int index, float fallback) =>
        (uint)index < (uint)arguments.Length ? arguments[index].ToSingle() : fallback;

    private static int Integer(QvmValue[] arguments, int index, int fallback) =>
        (uint)index < (uint)arguments.Length ? arguments[index].ToInt32() : fallback;

    /// <summary>Every loaded car model name, for diagnostics.</summary>
    public IReadOnlyCollection<string> CarModels => _cars.Keys;

    /// <summary>Looks up the proven common CAR physics header for a body model.</summary>
    public bool TryGetCar(string model, out CarPhysicsDefinition definition) =>
        _cars.TryGetValue(model, out definition);

    /// <summary>Looks up the retained PLANE physics record for a body model.</summary>
    /// <remarks>
    /// The record is intentionally indexed, not schematized: the SU27/JSF/CutSceneMissile
    /// definitions share a scalar tail (mass 10000, body 5/16/2.5, thrust 0.7/0.001,
    /// max-speed 700, response 1.7, smoothing 0.9, limiter 40) plus aerodynamic surface
    /// records whose native field mapping awaits the 0x433B50 translation loop.
    /// </remarks>
    public bool TryGetPlane(string model, out PlanePhysicsDefinition definition) =>
        _planes.TryGetValue(model, out definition);

    private void Define(Row call)
    {
        if (call.ArgumentCount < 9)
        {
            return;
        }

        string taskType = call.TaskType;
        string model = call.GetString(8);
        if (model.Length == 0)
        {
            return;
        }

        if (taskType == RotorType && call.ArgumentCount >= 15)
        {
            int tail = call.ArgumentCount - 6;
            _rotors.TryAdd(model, new RotorPhysicsDefinition(
                call.GetInt32(tail) != 0,
                call.GetInt32(tail + 1) != 0,
                call.GetInt32(tail + 2),
                call.GetSingle(tail + 3) * (MathF.PI / 180f),
                call.GetSingle(tail + 4),
                call.GetString(tail + 5)));
        }
        else if (taskType == HeliType && call.ArgumentCount >= 22)
        {
            // The Heli record (0x5031F0) is a vehicle's (VehicleType_DefineVehicleType): its handling from the first of the type's
            // own arguments, then its wings, made ready as 0x4EC4D0 makes them.
            int next = 0;
            VehicleTypeDefinition vehicle = VehicleTypeDefinition.Parse<X87>(call.Type.Parameters, ref next);
            _helicopters.TryAdd(model, new HelicopterPhysicsDefinition(
                call.Type.Mass,
                call.Type.Size,
                new(vehicle.MaxDeltaAlpha, vehicle.MaxDeltaBeta, vehicle.MaxDeltaGamma),
                new(vehicle.DeltaAlphaInertia, vehicle.DeltaBetaInertia, vehicle.DeltaGammaInertia),
                vehicle.ThrustInertia,
                vehicle.ThrustStartStopInertia,
                vehicle.MaxSpeed,
                vehicle.Wings)
            {
                MaximumThrust = vehicle.MaxThrust,
                SleepSpeed = call.Type.RestSpeed,
                SleepSpin = call.Type.RestAngularVelocitySquared,
            });
        }
        else if (taskType == CarType && call.ArgumentCount >= 20)
        {
            // CarType's parse (0x42DB00) from the type's own arguments: the vehicle's handling (steering retention +24, engine
            // +28, throttle retention +32, wing count +40), the wheels (+280, +284), the wheel drop (+604), the attach and
            // crawler bytes (+608, +609), the engine's sound set (+610), the explosion points (+684, +644) and the wreck (+688).
            CarTypeDefinition car = CarTypeDefinition.Parse<X87>(call.Type.Parameters);
            _cars.TryAdd(model, new CarPhysicsDefinition(
                call.Type.Mass,
                call.Type.Size,
                call.Type.ContactPoints.Count,
                call.Type.ContactPoints,
                car.Vehicle.DeltaGammaInertia,
                car.Vehicle.MaxThrust,
                car.Vehicle.ThrustInertia,
                car.Vehicle.Wings.Count,
                car.Wheels,
                car.DestroyedModel,
                call.Type.Arguments)
            {
                ExplosionPoints = car.ExplodeVertices,
                SkidSteer = car.IsCrawler,
                EngineSoundSet = car.EngineSound,
            });
        }
        else if (taskType == PlaneType && call.ArgumentCount >= 9)
        {
            // SU27/JSF/CutSceneMissile share the indexed scalar tail documented above.
            // Retain every definition value untouched; the 0x433B50 motion-path field
            // mapping is an explicit follow-up in the native translation loop, so no
            // smoothing/thrust semantics are assigned here.
            QvmValue[] arguments = new QvmValue[call.ArgumentCount];
            for (int i = 0; i < arguments.Length; i++)
            {
                arguments[i] = call.GetValue(i);
            }

            _planes.TryAdd(model, new PlanePhysicsDefinition(arguments));
        }
    }

    // A defined type's row, read by argument as the original's task types read theirs.
    private readonly struct Row(PhysicsObjectType type)
    {
        public string TaskType => type.TaskTypeName;

        public int ArgumentCount => type.Arguments.Count;

        public QvmValue GetValue(int index) => index < type.Arguments.Count ? type.Arguments[index] : QvmValue.Zero;

        public int GetInt32(int index) => GetValue(index).ToInt32();

        public float GetSingle(int index) => GetValue(index).ToSingle();

        public string GetString(int index) => GetValue(index) is { Kind: QvmValueKind.String } value ? value.Text : string.Empty;

        public PhysicsObjectType Type => type;
    }
}

/// <summary>The Heli control constants consumed once per 30 Hz physics step.</summary>
/// <param name="Mass">The airframe mass.</param>
/// <param name="Dimensions">The body extents in metres, used for the inertia box.</param>
/// <param name="Torque">The per-axis control torque.</param>
/// <param name="Smoothing">The per-axis control smoothing.</param>
/// <param name="HighCollectiveStep">The collective step used at or above 0.8.</param>
/// <param name="LowCollectiveStep">The collective step used below 0.8.</param>
/// <param name="MaximumSpeed">
/// The record's first float (config <c>+0</c>), in km/h — 200 for both shipped families.
/// <c>RotorPrimitive::Draw</c> (<c>0x42C630</c>) measures the airframe's axial speed against
/// <c>MaximumSpeed * 0.4</c> when it flaps the blade.
/// </param>
/// <param name="Wings">The surfaces the air pushes on, as <c>0x4EC4D0</c> readies them: the point times 4096, the normal made unit.</param>
public readonly record struct HelicopterPhysicsDefinition(
    float Mass,
    OpenIGI.Core.Numerics.Vector3f Dimensions,
    OpenIGI.Core.Numerics.Vector3f Torque,
    OpenIGI.Core.Numerics.Vector3f Smoothing,
    float HighCollectiveStep,
    float LowCollectiveStep,
    float MaximumSpeed,
    IReadOnlyList<Wing> Wings)
{
    /// <summary>Gets the record's most thrust (config <c>+28</c>), the vehicle part's <c>vMaxThrust</c>.</summary>
    public float MaximumThrust { get; init; }

    /// <summary>Gets the sleep speed threshold (<c>TYPE + 716</c>).</summary>
    public float SleepSpeed { get; init; }

    /// <summary>Gets the sleep threshold on |ω|² (<c>TYPE + 720</c>).</summary>
    public float SleepSpin { get; init; }
}

/// <summary>The CAR physics record, as the retail parsers build it.</summary>
/// <param name="Mass">The body mass, from the TYPE record's <c>+8</c>.</param>
/// <param name="Dimensions">The body extents in metres, used for the inertia box.</param>
/// <param name="BodyCount">The declared wheel-body count (TYPE <c>+328</c>).</param>
/// <param name="Bodies">The wheel bodies (TYPE <c>+332</c>, stride 24).</param>
/// <param name="SteeringRetention">
/// Record <c>+24</c>. <c>Car::Tick</c> (<c>0x42ECA0</c>) blends the live steering as
/// <c>current * retention + target * (1 - retention)</c> once per 30 Hz tick.
/// </param>
/// <param name="EngineForce">
/// Record <c>+28</c>. The wheel drive (<c>0x430D10</c>) turns it into
/// <c>EngineForce * throttle * g / drivenWheelCount</c>, minus the body's speed.
/// </param>
/// <param name="ThrottleRetention">Record <c>+32</c>, the throttle's half of the same blend.</param>
/// <param name="AerodynamicSurfaceCount">
/// Record <c>+40</c>. <c>Car::Tick</c> only runs the drag pass when this is non-zero; every shipped
/// car publishes nought, so cars carry no aerodynamics.
/// </param>
/// <param name="Wheels">The wheel entries (record <c>+284</c>, stride 20).</param>
/// <param name="DestroyedModel">The final CAR definition value: the authored wreck-model binding.</param>
/// <param name="Arguments">The untouched definition values.</param>
public readonly record struct CarPhysicsDefinition(
    float Mass,
    OpenIGI.Core.Numerics.Vector3f Dimensions,
    int BodyCount,
    IReadOnlyList<PhysicsContactPointType> Bodies,
    float SteeringRetention,
    float EngineForce,
    float ThrottleRetention,
    int AerodynamicSurfaceCount,
    IReadOnlyList<CarWheelTypeDefinition> Wheels,
    string DestroyedModel,
    IReadOnlyList<QvmValue> Arguments)
{
    /// <summary>The record's explosion-point slots, <c>+644..+684</c>.</summary>
    public const int MaximumExplosionPoints = 10;

    /// <summary>
    /// Gets the magic-vertex indices the dying car explodes at — record <c>+644</c>, counted at <c>+684</c>.
    /// </summary>
    /// <remarks>
    /// Empty means one blast at the car's origin sized from its own bound; <c>0x42E5A0</c> branches on the
    /// count.
    /// </remarks>
    public IReadOnlyList<int> ExplosionPoints { get; init; } = [];

    /// <summary>Gets whether the car steers by easing the drive on one side -- record byte <c>+609</c>.</summary>
    /// <remarks>
    /// The APC and T80 have no steerable wheel. With this byte set, <c>0x430D10</c> scales each driven
    /// wheel's drive by <c>0x4304B0</c>: under positive steering the right-hand wheels (body side 1) drive at
    /// <c>1 - 2s</c>, under negative steering the left-hand ones at <c>1 + 2s</c>.
    /// </remarks>
    public bool SkidSteer { get; init; }

    /// <summary>Gets the name of the car's engine sound set -- record <c>+610</c>.</summary>
    /// <remarks>
    /// <c>0x42E320</c> instantiates it as a child of the car (<c>0x4E6B00</c>) while the car's "Play sound" holds. The
    /// APC names <c>apc_sounds</c>, the trucks and limousine <c>truck_sounds</c>; the T80's <c>t80_sounds</c> and the
    /// buggy's <c>buggy_engine_1</c> are defined by no retail sound script, so those cars are silent.
    /// </remarks>
    public string EngineSoundSet { get; init; } = string.Empty;

    /// <summary>Number of values in the original <c>DefinePhysicsObjType</c> call.</summary>
    public int ArgumentCount => Arguments.Count;

    /// <summary>Returns an untouched definition value by its retail argument index.</summary>
    public QvmValue ArgumentAt(int index) =>
        (uint)index < (uint)Arguments.Count ? Arguments[index] : QvmValue.Zero;

    /// <summary>Counts the driven wheels the engine force is divided between.</summary>
    public int DrivenWheelCount
    {
        get
        {
            int driven = 0;
            for (int i = 0; i < Wheels.Count; i++)
            {
                if (Wheels[i].IsEngine && Wheels[i].HasContactPoint)
                {
                    driven++;
                }
            }

            return driven;
        }
    }
}

/// <summary>The retained PLANE physics record published by <c>DefinePhysicsObjType</c>.</summary>
/// <remarks>
/// The shared type reader consumes the header through argument 11.  The Plane-specific
/// config begins at argument 12: <see cref="ControlRetention"/> is read by
/// <c>Plane_Physics</c> at config +8/+12/+16 and the two collective increments at
/// config +32/+36.  Keeping the original argument list as well preserves the still-unmapped
/// aerodynamic tail without inventing names for it.
/// </remarks>
public readonly record struct PlanePhysicsDefinition(
    IReadOnlyList<QvmValue> Arguments)
{
    /// <summary>Number of values in the original <c>DefinePhysicsObjType</c> call.</summary>
    public int ArgumentCount => Arguments.Count;

    /// <summary>Returns an untouched definition value by its retail argument index.</summary>
    public QvmValue ArgumentAt(int index) =>
        (uint)index < (uint)Arguments.Count ? Arguments[index] : QvmValue.Zero;

    /// <summary>Retail retention for the pitch, roll, and yaw channel targets.</summary>
    public OpenIGI.Core.Numerics.Vector3f ControlRetention => new(
        ArgumentAt(14).ToSingle(),
        ArgumentAt(15).ToSingle(),
        ArgumentAt(16).ToSingle());

    /// <summary>Retail collective increment once live thrust reaches the 0.8 threshold.</summary>
    public float HighThrustStep => ArgumentAt(20).ToSingle();

    /// <summary>Retail collective increment below the 0.8 threshold.</summary>
    public float LowThrustStep => ArgumentAt(21).ToSingle();
}

/// <summary>The Rotor record parsed by retail function <c>0x42D9F0</c>.</summary>
public readonly record struct RotorPhysicsDefinition(
    bool ProducesLift,
    bool IsTailRotor,
    int BladeSamples,
    float MaximumTilt,
    float PhaseStep,
    string Sound)
{
    /// <summary>Angular phase added for one unit of collective during one 30 Hz tick.</summary>
    public float PhasePerCollectiveTick => BladeSamples * PhaseStep;
}
