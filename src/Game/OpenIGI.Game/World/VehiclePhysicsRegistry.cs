using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
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
    private const string DefineNative = "DefinePhysicsObjType";
    private const string RotorType = "TASKTYPE_ROTOR";
    private const string HeliType = "TASKTYPE_HELI";
    private const string CarType = "TASKTYPE_CAR";
    private const string PlaneType = "TASKTYPE_PLANE";

    private static readonly string[] Scripts =
    [
        "LOCAL:physicsobj/cars/apc/car.qvm",
        "LOCAL:physicsobj/cars/buggy/car.qvm",
        "LOCAL:physicsobj/cars/limo/car.qvm",
        "LOCAL:physicsobj/cars/t80/car.qvm",
        "LOCAL:physicsobj/cars/truck/car.qvm",
        "LOCAL:physicsobj/cars/cutscene_truck/car.qvm",
        "LOCAL:physicsobj/helis/bell/heli.qvm",
        "LOCAL:physicsobj/helis/mil/heli.qvm",
        "LOCAL:physicsobj/planes/su27/plane.qvm",
        "LOCAL:physicsobj/planes/jsf/plane.qvm",
        "LOCAL:physicsobj/planes/cutscenemissile/plane.qvm",
    ];

    private readonly Dictionary<string, RotorPhysicsDefinition> _rotors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HelicopterPhysicsDefinition> _helicopters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CarPhysicsDefinition> _cars = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PlanePhysicsDefinition> _planes = new(StringComparer.OrdinalIgnoreCase);
    private readonly IOpenIgiLogger _logger;
    private int _rotorType;
    private int _heliType;
    private int _carType;
    private int _planeType;

    /// <summary>Creates an empty registry.</summary>
    public VehiclePhysicsRegistry(IOpenIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Loads every shipped helicopter family.</summary>
    public int Load(ResourceRegistry resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        _rotors.Clear();
        _helicopters.Clear();
        _cars.Clear();
        _planes.Clear();

        NativeRegistry natives = new();
        natives.RegisterFunction(DefineNative, Define);
        int nextType = 1;

        foreach (string script in Scripts)
        {
            if (!resources.TryLoad(script, out ReadOnlyMemory<byte> bytes))
            {
                continue;
            }

            QvmProgram program;
            try
            {
                program = QvmProgram.Read(bytes);
            }
            catch (QvmFormatException ex)
            {
                throw new EngineFatalException($"Couldn't load vehicle definition script: {script}", ex);
            }

            foreach (string? symbol in program.SymbolNames)
            {
                if (symbol is null || !symbol.StartsWith("TASKTYPE_", StringComparison.Ordinal))
                {
                    continue;
                }
                if (!natives.Contains(symbol))
                {
                    natives.RegisterConstant(symbol, nextType++);
                }
                if (natives.TryResolve(symbol, out QvmSymbol? registered))
                {
                    if (symbol == RotorType)
                    {
                        _rotorType = registered.Read().Integer;
                    }
                    if (symbol == HeliType)
                    {
                        _heliType = registered.Read().Integer;
                    }
                    if (symbol == CarType)
                    {
                        _carType = registered.Read().Integer;
                    }
                    if (symbol == PlaneType)
                    {
                        _planeType = registered.Read().Integer;
                    }
                }
            }

            QvmInterpreter interpreter = new(program, natives);
            QvmError error = interpreter.Run();
            if (error != QvmError.None)
            {
                throw new EngineFatalException(
                    $"{script} faulted with {error}: {interpreter.ErrorMessage}");
            }
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

    private QvmValue Define(QvmCallContext call)
    {
        if (call.ArgumentCount < 9)
        {
            return QvmValue.Zero;
        }

        int taskType = call.GetInt32(0);
        string model = call.GetString(8);
        if (model.Length == 0)
        {
            return QvmValue.Zero;
        }

        if (taskType == _rotorType && call.ArgumentCount >= 15)
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
        else if (taskType == _heliType && call.ArgumentCount >= 22)
        {
            // Bell/Mil use the same Heli-specific prefix. Argument 12 is the record's first float
            // (config +0); the nine after it are the fields read at config +4..+36 by 0x431E30,
            // after the common physics-object header. The tail is exact: 0x5031F0 reads ten floats
            // (args 12..21), the surface count (22) and its seven-value samples, so the record's
            // +0 can only be argument 12.
            int surfaceCount = Math.Min(call.GetInt32(22), (call.ArgumentCount - 23) / 7);
            AerodynamicSurfaceDefinition[] surfaces = new AerodynamicSurfaceDefinition[surfaceCount];
            for (int i = 0; i < surfaceCount; i++)
            {
                int field = 23 + (i * 7);
                surfaces[i] = new AerodynamicSurfaceDefinition(
                    call.GetSingle(field),
                    new(call.GetSingle(field + 4), call.GetSingle(field + 5), call.GetSingle(field + 6)),
                    new(call.GetSingle(field + 1), call.GetSingle(field + 2), call.GetSingle(field + 3)));
            }
            _helicopters.TryAdd(model, new HelicopterPhysicsDefinition(
                call.GetSingle(2),
                new(call.GetSingle(3), call.GetSingle(4), call.GetSingle(5)),
                new(call.GetSingle(13), call.GetSingle(15), call.GetSingle(17)),
                new(call.GetSingle(14), call.GetSingle(16), call.GetSingle(18)),
                call.GetSingle(20),
                call.GetSingle(21),
                call.GetSingle(12),
                surfaces)
            {
                // With no body points (arg 9 = 0 on both shipped airframes) the sleep thresholds are args 10 and 11.
                SleepSpeed = call.GetSingle(10),
                SleepSpin = call.GetSingle(11),
            });
        }
        else if (taskType == _carType && call.ArgumentCount >= 20)
        {
            QvmValue[] arguments = new QvmValue[call.ArgumentCount];
            for (int i = 0; i < arguments.Length; i++)
            {
                arguments[i] = call.GetValue(i);
            }

            // DefinePhysicsObjType (0x4EDD20) fills a 728-byte TYPE record: mass at +8, dimensions at
            // +12..+20, the wheel-BODY count at +328 (argument 9) and that many 24-byte body records
            // at +332 (three integers then three floats), then two trailing values. Everything after
            // those is the task-specific tail, and TYPE+724 points at the record the physics reads.
            int bodyCount = call.GetInt32(9);
            int parsedBodies = Math.Clamp(bodyCount, 0, Math.Max(0, (call.ArgumentCount - 10) / 6));
            CarBodyDefinition[] bodies = new CarBodyDefinition[parsedBodies];
            for (int body = 0; body < parsedBodies; body++)
            {
                int field = 10 + (body * 6);
                bodies[body] = new CarBodyDefinition(
                    call.GetInt32(field),
                    call.GetInt32(field + 1),
                    call.GetInt32(field + 2),
                    call.GetSingle(field + 3),
                    call.GetSingle(field + 4),
                    call.GetSingle(field + 5));
            }

            // The tail starts after the body records and their two trailing values. The base
            // physics-object handler consumes fourteen 4-byte fields from there -- the same
            // sequential fill the Heli record uses, read back by Heli::Tick (0x431E30) -- which puts
            // steering retention at record +24, the engine force at +28 and throttle retention at +32
            // (Car::Tick 0x42ECA0 and the wheel drive 0x430D10 read exactly those), and the
            // aerodynamic-surface count at +40. Every shipped car publishes nought there.
            int tail = 10 + (6 * parsedBodies) + 2;
            const int BaseRecordFields = 14;
            float steeringRetention = Single(arguments, tail + 6, 0.95f);
            float engineForce = Single(arguments, tail + 7, 0f);
            float throttleRetention = Single(arguments, tail + 8, 0.95f);
            int aerodynamicSurfaces = Integer(arguments, tail + 10, 0);

            // The CAR tail (0x42DB00) is a wheel count at record +280 followed by that many 20-byte
            // entries at +284. Each entry is read as SIX values in the order
            // int, int, float, int, float, float -- steerable, driven, maximum steer in DEGREES, the
            // wheel-body index, the suspension length (scaled by 4096 into world units) and the
            // spring stiffness. A negative body index has no collision body: the suspension sweep
            // (0x42EF00) and the friction call (0x430D10) both skip it, though it still draws.
            int wheelCountIndex = tail + BaseRecordFields;
            int declaredWheels = Integer(arguments, wheelCountIndex, 0);
            int parsedWheels = Math.Clamp(
                declaredWheels,
                0,
                Math.Max(0, (call.ArgumentCount - wheelCountIndex - 1) / 6));
            CarWheelDefinition[] wheels = new CarWheelDefinition[parsedWheels];
            for (int wheel = 0; wheel < parsedWheels; wheel++)
            {
                int field = wheelCountIndex + 1 + (wheel * 6);
                wheels[wheel] = new CarWheelDefinition(
                    Integer(arguments, field, 0) != 0,
                    Integer(arguments, field + 1, 0) != 0,
                    Single(arguments, field + 2, 0f),
                    Integer(arguments, field + 3, -1),
                    Single(arguments, field + 4, 0f) * (float)WorldScene.UnitsPerMetre,
                    Single(arguments, field + 5, 0f));
            }

            // After the wheels 0x42DB00 reads one float (+604, scaled by 4096), two bytes (+608,
            // +609), the engine sound set (+610), then the destruction block: an explosion-point
            // count (+684) and that many magic-vertex indices (+644, ten slots), and last the wreck
            // model (+688). The dying car (0x42E5A0) sets one Explosion0 off at each of those
            // magic vertices -- the Mission 1 truck at 6 and 7, the APC and T80 at 4 to 8.
            int explosionCountIndex = wheelCountIndex + 1 + (parsedWheels * 6) + 4;
            int explosionCount = Math.Clamp(
                Integer(arguments, explosionCountIndex, 0),
                0,
                Math.Min(CarPhysicsDefinition.MaximumExplosionPoints, Math.Max(0, call.ArgumentCount - explosionCountIndex - 2)));
            int[] explosionPoints = new int[explosionCount];
            for (int point = 0; point < explosionCount; point++)
            {
                explosionPoints[point] = Integer(arguments, explosionCountIndex + 1 + point, 0);
            }

            _cars.TryAdd(model, new CarPhysicsDefinition(
                call.GetSingle(2),
                new(call.GetSingle(3), call.GetSingle(4), call.GetSingle(5)),
                bodyCount,
                bodies,
                steeringRetention,
                engineForce,
                throttleRetention,
                aerodynamicSurfaces,
                wheels,
                call.GetString(call.ArgumentCount - 1),
                arguments)
            {
                ExplosionPoints = explosionPoints,
                SkidSteer = Integer(arguments, wheelCountIndex + 1 + (parsedWheels * 6) + 2, 0) != 0,
                EngineSoundSet = wheelCountIndex + 1 + (parsedWheels * 6) + 3 < arguments.Length
                    && arguments[wheelCountIndex + 1 + (parsedWheels * 6) + 3].Kind == QvmValueKind.String
                        ? arguments[wheelCountIndex + 1 + (parsedWheels * 6) + 3].Text
                        : string.Empty,
            });
        }
        else if (taskType == _planeType && call.ArgumentCount >= 9)
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

        return QvmValue.FromInt32(1);
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
/// <param name="Aerodynamics">The authored quadratic-drag samples.</param>
public readonly record struct HelicopterPhysicsDefinition(
    float Mass,
    OpenIGI.Core.Numerics.Vector3f Dimensions,
    OpenIGI.Core.Numerics.Vector3f Torque,
    OpenIGI.Core.Numerics.Vector3f Smoothing,
    float HighCollectiveStep,
    float LowCollectiveStep,
    float MaximumSpeed,
    IReadOnlyList<AerodynamicSurfaceDefinition> Aerodynamics)
{
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
    IReadOnlyList<CarBodyDefinition> Bodies,
    float SteeringRetention,
    float EngineForce,
    float ThrottleRetention,
    int AerodynamicSurfaceCount,
    IReadOnlyList<CarWheelDefinition> Wheels,
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
                if (Wheels[i].Driven && Wheels[i].HasBody)
                {
                    driven++;
                }
            }

            return driven;
        }
    }
}

/// <summary>
/// One 20-byte wheel entry from the CAR record (<c>0x42DB00</c> fills it; <c>0x42EF00</c> and
/// <c>0x430D10</c> read it).
/// </summary>
/// <param name="Steerable">Record <c>+284</c>: whether the wheel turns with the steering input.</param>
/// <param name="Driven">Record <c>+285</c>: whether the engine force is applied at this wheel.</param>
/// <param name="MaximumSteerDegrees">
/// Record <c>+288</c>. The wheel's angle is <c>MaximumSteerDegrees * pi/180 * steering</c>.
/// </param>
/// <param name="BodyIndex">
/// Record <c>+292</c>. Negative means the wheel has no collision body: retail skips it in the
/// suspension sweep and the friction call, but it is still drawn.
/// </param>
/// <param name="SuspensionLengthUnits">Record <c>+296</c>, the authored length scaled by 4096.</param>
/// <param name="SpringStiffness">Record <c>+300</c>.</param>
public readonly record struct CarWheelDefinition(
    bool Steerable,
    bool Driven,
    float MaximumSteerDegrees,
    int BodyIndex,
    float SuspensionLengthUnits,
    float SpringStiffness)
{
    /// <summary>Gets whether this wheel has a collision body and takes part in the physics.</summary>
    public bool HasBody => BodyIndex >= 0;

    /// <summary>Gets the maximum steering deflection in radians.</summary>
    public float MaximumSteerRadians => MaximumSteerDegrees * (MathF.PI / 180f);
}

/// <summary>
/// One 24-byte wheel body from the TYPE record. The two trailing floats are the pair the tyre model
/// (<c>0x430960</c>) scales its wheel-space slip by; the handbrake raises the longitudinal one to the
/// lateral value, which is how the two are told apart.
/// </summary>
/// <param name="Index">The body index the CAR wheel entries refer to.</param>
/// <param name="Slot1">Second integer slot.</param>
/// <param name="Slot2">Third integer slot.</param>
/// <param name="Slot3">First float slot.</param>
/// <param name="LateralFriction">Body <c>+472</c>: the across-the-wheel grip (0.95 on every car).</param>
/// <param name="LongitudinalFriction">Body <c>+476</c>: the rolling resistance (0.05).</param>
public readonly record struct CarBodyDefinition(
    int Index,
    int Slot1,
    int Slot2,
    float Slot3,
    float LateralFriction,
    float LongitudinalFriction);

/// <summary>One local quadratic-drag sample from the Heli physics-object record.</summary>
public readonly record struct AerodynamicSurfaceDefinition(
    float Coefficient,
    OpenIGI.Core.Numerics.Vector3f Direction,
    OpenIGI.Core.Numerics.Vector3f Point);

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
