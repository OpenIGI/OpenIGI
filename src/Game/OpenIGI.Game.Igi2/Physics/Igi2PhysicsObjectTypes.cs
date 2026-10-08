using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Physics;

/// <summary>
/// One point a physics object touches the world with, as its type defines it: <c>PhysicsObjTypeCollPoint_s</c>
/// (24 bytes at <c>PhysicsObjType_s + 0x14C</c>).
/// </summary>
/// <param name="MagicVertex">The model's magic vertex the point sits on, <c>iMagicVertex</c>.</param>
/// <param name="Type">The point's kind, <c>iType</c>.</param>
/// <param name="Material">Its material, <c>iMaterial</c>.</param>
/// <param name="Bounce">Its coefficient of restitution, <c>vBounceFactor</c>.</param>
/// <param name="FrictionX">Its first friction coefficient, <c>vFrictionX</c>.</param>
/// <param name="FrictionY">Its second, <c>vFrictionY</c>: the one a contact's friction reads.</param>
public readonly record struct Igi2PhysicsContactPointType(int MagicVertex, int Type, int Material, float Bounce, float FrictionX, float FrictionY);

/// <summary>A kind of physics object, <c>PhysicsObjType_s</c>, as <c>DefinePhysicsObjType</c> defines it.</summary>
/// <param name="TaskType">The task type that moves objects of it, <c>eQTaskType</c>.</param>
/// <param name="Id">Its number, <c>nID</c>.</param>
/// <param name="Mass">Its mass, <c>vMass</c>.</param>
/// <param name="Size">Its box in metres, <c>tSize</c>.</param>
/// <param name="Name">Its name, <c>zName</c>.</param>
/// <param name="Description">Its description, <c>zDesc</c>.</param>
/// <param name="Model">The model it is the type of, <c>zModel</c>: what <c>PhysicsObjType_FindType</c> looks it up by.</param>
/// <param name="ContactPoints">Its contact points, <c>atCollPoint</c>.</param>
/// <param name="RestSpeed">The speed below which, in contact, it may rest, <c>vRestSpeed</c>.</param>
/// <param name="RestAngularVelocitySquared">The squared spin below which it may rest, <c>vRestAngMomSquared</c>.</param>
/// <param name="Parameters">The arguments after the common ones, which its task type parses for itself (<c>pxData</c>).</param>
public sealed record Igi2PhysicsObjectType(
    int TaskType,
    int Id,
    float Mass,
    Vector3f Size,
    string Name,
    string Description,
    string Model,
    IReadOnlyList<Igi2PhysicsContactPointType> ContactPoints,
    float RestSpeed,
    float RestAngularVelocitySquared,
    IReadOnlyList<QvmValue> Parameters);

/// <summary>
/// The physics object types, <c>PhysicsObjType_atPhysicsObjType</c>: every script under <c>LOCAL:physicsobj</c> defines
/// its own through <c>DefinePhysicsObjType</c> (<c>PhysicsObjType_DefinePhysicsObjTypeCB</c>, <c>igi2 0x43B900</c>).
/// </summary>
/// <remarks>
/// <c>Flow_CreateHandler</c> (<c>igi2 0x401600</c>) walks the directory with <c>PhysicsObjType_ProcessFileCB</c>
/// (<c>0x43BEC0</c>), running each script it finds. The vehicles, missiles and the grenades all live there.
/// </remarks>
public sealed class Igi2PhysicsObjectTypes
{
    /// <summary>The directory the scripts are read from.</summary>
    public const string Directory = "LOCAL:physicsobj";

    /// <summary>The native the scripts define each type with.</summary>
    public const string DefineNative = "DefinePhysicsObjType";

    /// <summary>The arguments before the contact points.</summary>
    private const int HeaderArguments = 10;

    /// <summary>The arguments each contact point takes.</summary>
    private const int PointArguments = 6;

    private const string LogCategory = "PhysicsObj";

    /// <summary>The task types the scripts name, as constant and type name.</summary>
    private static readonly (string Constant, string Type)[] TaskTypes =
    [
        ("TASKTYPE_GENERICPHYSICSOBJ", "GenericPhysicsObj"),
        ("TASKTYPE_GENERICPHYSICSMAGICOBJ", "GenericPhysicsMagicObj"),
        ("TASKTYPE_GRENADE", "Grenade"),
        ("TASKTYPE_CAR", "Car"),
        ("TASKTYPE_WHEEL", "Wheel"),
        ("TASKTYPE_HELI", "Heli"),
        ("TASKTYPE_ROTOR", "Rotor"),
        ("TASKTYPE_MISSILE", "Missile"),
        ("TASKTYPE_PLANE", "Plane"),
    ];

    /// <summary>
    /// The constants the type-specific arguments name: the grenade's kinds (<c>Grenade_Open</c>, <c>igi2 0x485390</c>) and
    /// the missile's smoke and guidance (<c>Missile_Open</c>, <c>0x52F5B0</c>).
    /// </summary>
    private static readonly (string Name, int Value)[] Constants =
    [
        ("GRENADE_EXPLOSIONTYPE_NORMAL", Igi2GrenadeKind.Normal),
        ("GRENADE_EXPLOSIONTYPE_FLASHBANG", Igi2GrenadeKind.Flashbang),
        ("GRENADE_EXPLOSIONTYPE_SMOKE", Igi2GrenadeKind.Smoke),
        ("MISSILE_SMOKE_NONE", 0),
        ("MISSILE_SMOKE_NORMAL", 1),
        ("MISSILE_TYPE_MISSILE_DIRECT", 0),
        ("MISSILE_TYPE_HIND_HELICOPTER", 1),
        ("MISSILE_TYPE_MISSILE_BALLISTIC", 2),
        ("MISSILE_TYPE_MISSILE_ANTITANK", 3),
        ("MISSILE_TYPE_MISSILE_KEYBOARD", 4),
        ("MISSILE_TYPE_BOMB", 5),
    ];

    private readonly List<Igi2PhysicsObjectType> _types = [];
    private readonly Dictionary<string, int> _typeIds = new(StringComparer.Ordinal);
    private readonly IOpenIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="Igi2PhysicsObjectTypes"/> class.</summary>
    /// <param name="logger">Where the load reports.</param>
    public Igi2PhysicsObjectTypes(IOpenIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the types, in the order they were defined.</summary>
    public IReadOnlyList<Igi2PhysicsObjectType> Types => _types;

    /// <summary>Registers the native, the task types the scripts name and the constants their own arguments use.</summary>
    /// <param name="natives">The script namespace; a constant an earlier module registered is left as it is.</param>
    /// <param name="types">The task-type registry; a named type no class implements yet is declared with no parameters.</param>
    public void Register(NativeRegistry natives, TaskTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(types);

        foreach ((string constant, string type) in TaskTypes)
        {
            TaskType registered = types.TryGet(type, out TaskType? known) ? known : types.Declare(type, []);
            _typeIds[type] = registered.Id;
            if (!natives.Contains(constant))
            {
                natives.RegisterConstant(constant, registered.Id);
            }
        }

        foreach ((string name, int value) in Constants)
        {
            if (!natives.Contains(name))
            {
                natives.RegisterConstant(name, value);
            }
        }

        natives.RegisterFunction(DefineNative, Define);
    }

    /// <summary>Runs every script under <see cref="Directory"/>.</summary>
    /// <param name="fileSystem">The file system the directory is walked on.</param>
    /// <param name="resources">Where the scripts are read from.</param>
    /// <param name="natives">The script namespace, with <see cref="Register"/> applied.</param>
    public void Load(VirtualFileSystem fileSystem, ResourceRegistry resources, NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _types.Clear();
        int scripts = ScriptTableLoader.WalkAndRun(fileSystem, resources, natives, Directory, "*.qvm", _logger);
        _logger.Information(LogCategory, $"{_types.Count} physics object types read from {scripts} scripts.");
    }

    /// <summary>Finds the type of a model, <c>PhysicsObjType_FindType</c> (<c>igi2 0x43C050</c>): the first defined for it.</summary>
    /// <param name="model">The model's name.</param>
    /// <param name="type">Receives the type.</param>
    /// <returns><see langword="false"/> when no type names the model.</returns>
    public bool TryFind(string model, out Igi2PhysicsObjectType type)
    {
        foreach (Igi2PhysicsObjectType defined in _types)
        {
            if (string.Equals(defined.Model, model, StringComparison.Ordinal))
            {
                type = defined;
                return true;
            }
        }

        type = null!;
        return false;
    }

    /// <summary>Gets whether a type is moved by a named task type.</summary>
    /// <param name="type">The physics object type.</param>
    /// <param name="taskType">The task type's name.</param>
    /// <returns><see langword="true"/> when that task type moves it.</returns>
    public bool IsMovedBy(Igi2PhysicsObjectType type, string taskType)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _typeIds.TryGetValue(taskType, out int id) && id == type.TaskType;
    }

    /// <summary>
    /// <c>DefinePhysicsObjType</c>: the task type, the number, the mass and the box, the three names, the contact points
    /// (each a magic vertex, a kind, a material, a bounce and two frictions), the two rest thresholds, then what the task
    /// type reads for itself. A model defined twice is fatal.
    /// </summary>
    private QvmValue Define(QvmCallContext call)
    {
        string model = call.GetString(8);
        foreach (Igi2PhysicsObjectType defined in _types)
        {
            if (string.Equals(defined.Model, model, StringComparison.Ordinal))
            {
                throw new EngineFatalException($"Physics obj model {model} is defined multiple times.");
            }
        }

        int count = call.GetInt32(9);
        Igi2PhysicsContactPointType[] points = new Igi2PhysicsContactPointType[Math.Max(count, 0)];
        int argument = HeaderArguments;
        for (int i = 0; i < points.Length; i++, argument += PointArguments)
        {
            points[i] = new Igi2PhysicsContactPointType(
                call.GetInt32(argument),
                call.GetInt32(argument + 1),
                call.GetInt32(argument + 2),
                call.GetSingle(argument + 3),
                call.GetSingle(argument + 4),
                call.GetSingle(argument + 5));
        }

        float restSpeed = call.GetSingle(argument);
        float restSpin = call.GetSingle(argument + 1);
        argument += 2;
        QvmValue[] parameters = new QvmValue[Math.Max(call.ArgumentCount - argument, 0)];
        for (int i = 0; i < parameters.Length; i++)
        {
            parameters[i] = call.GetValue(argument + i);
        }

        _types.Add(new Igi2PhysicsObjectType(
            call.GetInt32(0) & 0xFFFF,
            call.GetInt32(1),
            call.GetSingle(2),
            new Vector3f(call.GetSingle(3), call.GetSingle(4), call.GetSingle(5)),
            call.GetString(6),
            call.GetString(7),
            model,
            points,
            restSpeed,
            restSpin,
            parameters));
        return QvmValue.FromInt32(1);
    }
}
