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
    private readonly IIgiLogger _logger;
    private int _rotorType;
    private int _heliType;
    private int _carType;
    private int _planeType;

    /// <summary>Creates an empty registry.</summary>
    public VehiclePhysicsRegistry(IIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

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
                surfaces));
        }
        else if (taskType == _carType && call.ArgumentCount >= 20)
        {
            // Every shipped CAR QVM uses this common header: mass and dimensions are
            // arguments 2..5, wheel count is argument 9, and the steering/throttle
            // retention/response pairs are 14..15 and 20..21. Keep the record limited
            // to fields proven by native 0x42ECA0; contact-tail semantics are still
            // being mapped in the separate vehicle audit.
            QvmValue[] arguments = new QvmValue[call.ArgumentCount];
            for (int i = 0; i < arguments.Length; i++)
            {
                arguments[i] = call.GetValue(i);
            }

            // DefinePhysicsObjType's native parser reads exactly six values per wheel after the
            // wheel-count argument: three integer slots followed by three floating-point slots. Keep
            // those records separate from the opaque task-specific tail. The slot names are
            // intentionally positional: 0x42D1D0 consumes this structure, but its field semantics
            // have not yet passed the paired native CFG/type-validation loop.
            int declaredWheelCount = call.GetInt32(9);
            int parsedWheelCount = Math.Clamp(declaredWheelCount, 0, (call.ArgumentCount - 10) / 6);
            CarWheelDefinition[] wheels = new CarWheelDefinition[parsedWheelCount];
            for (int wheel = 0; wheel < parsedWheelCount; wheel++)
            {
                int field = 10 + (wheel * 6);
                wheels[wheel] = new CarWheelDefinition(
                    call.GetInt32(field),
                    call.GetInt32(field + 1),
                    call.GetInt32(field + 2),
                    call.GetSingle(field + 3),
                    call.GetSingle(field + 4),
                    call.GetSingle(field + 5));
            }

            _cars.TryAdd(model, new CarPhysicsDefinition(
                call.GetSingle(2),
                new(call.GetSingle(3), call.GetSingle(4), call.GetSingle(5)),
                declaredWheelCount,
                call.GetSingle(14),
                call.GetSingle(15),
                call.GetSingle(20),
                call.GetSingle(21),
                arguments,
                wheels));
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
    IReadOnlyList<AerodynamicSurfaceDefinition> Aerodynamics);

/// <summary>The common CAR header consumed by the native smoothing path.</summary>
public readonly record struct CarPhysicsDefinition(
    float Mass,
    OpenIGI.Core.Numerics.Vector3f Dimensions,
    int WheelCount,
    float SteeringRetention,
    float SteeringResponse,
    float ThrottleRetention,
    float ThrottleResponse,
    IReadOnlyList<QvmValue> Arguments,
    IReadOnlyList<CarWheelDefinition> Wheels)
{
    /// <summary>Number of values in the original <c>DefinePhysicsObjType</c> call.</summary>
    public int ArgumentCount => Arguments.Count;

    /// <summary>Returns an untouched definition value by its retail argument index.</summary>
    public QvmValue ArgumentAt(int index) =>
        (uint)index < (uint)Arguments.Count ? Arguments[index] : QvmValue.Zero;
}

/// <summary>
/// One six-value wheel record as stored by IGI1's <c>DefinePhysicsObjType</c> parser.
/// </summary>
/// <remarks>
/// The native parser's storage shape is established, but the three integer and three floating-point
/// slots are still positional until the CAR task callback has completed the native translation audit.
/// Keeping the names positional prevents a guessed meaning from becoming a gameplay contract.
/// </remarks>
public readonly record struct CarWheelDefinition(
    int Integer0,
    int Integer1,
    int Integer2,
    float Float0,
    float Float1,
    float Float2);

/// <summary>One local quadratic-drag sample from the Heli physics-object record.</summary>
public readonly record struct AerodynamicSurfaceDefinition(
    float Coefficient,
    OpenIGI.Core.Numerics.Vector3f Direction,
    OpenIGI.Core.Numerics.Vector3f Point);

/// <summary>The retained PLANE physics record (indexed retail arguments).</summary>
public readonly record struct PlanePhysicsDefinition(
    IReadOnlyList<QvmValue> Arguments)
{
    /// <summary>Number of values in the original <c>DefinePhysicsObjType</c> call.</summary>
    public int ArgumentCount => Arguments.Count;

    /// <summary>Returns an untouched definition value by its retail argument index.</summary>
    public QvmValue ArgumentAt(int index) =>
        (uint)index < (uint)Arguments.Count ? Arguments[index] : QvmValue.Zero;
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
