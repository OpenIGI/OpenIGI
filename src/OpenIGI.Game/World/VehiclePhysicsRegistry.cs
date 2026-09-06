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

    private static readonly string[] Scripts =
    [
        "LOCAL:physicsobj/helis/bell/heli.qvm",
        "LOCAL:physicsobj/helis/mil/heli.qvm",
    ];

    private readonly Dictionary<string, RotorPhysicsDefinition> _rotors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HelicopterPhysicsDefinition> _helicopters = new(StringComparer.OrdinalIgnoreCase);
    private readonly IIgiLogger _logger;
    private int _rotorType;
    private int _heliType;

    /// <summary>Creates an empty registry.</summary>
    public VehiclePhysicsRegistry(IIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Loads every shipped helicopter family.</summary>
    public int Load(ResourceRegistry resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        _rotors.Clear();
        _helicopters.Clear();

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
            $"Vehicle physics: {_helicopters.Count} helicopter(s), {_rotors.Count} rotor model(s).");
        return _helicopters.Count + _rotors.Count;
    }

    /// <summary>Looks up the Heli control record bound to a body model.</summary>
    public bool TryGetHelicopter(string model, out HelicopterPhysicsDefinition definition) =>
        _helicopters.TryGetValue(model, out definition);

    /// <summary>Looks up the Rotor record bound to a rotor model.</summary>
    public bool TryGetRotor(string model, out RotorPhysicsDefinition definition) =>
        _rotors.TryGetValue(model, out definition);

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

/// <summary>One local quadratic-drag sample from the Heli physics-object record.</summary>
public readonly record struct AerodynamicSurfaceDefinition(
    float Coefficient,
    OpenIGI.Core.Numerics.Vector3f Direction,
    OpenIGI.Core.Numerics.Vector3f Point);

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
