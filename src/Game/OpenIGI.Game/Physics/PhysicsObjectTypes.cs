using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Physics;

/// <summary>
/// One point a physics object touches the world with, as its type defines it: <c>PhysicsObjTypeCollPoint_s</c>
/// (24 bytes at the type's <c>+0x14C</c>).
/// </summary>
/// <param name="MagicVertex">The model's magic vertex the point sits on, <c>iMagicVertex</c>.</param>
/// <param name="Type">The point's kind, <c>iType</c>.</param>
/// <param name="Material">Its material, <c>iMaterial</c>.</param>
/// <param name="Bounce">Its coefficient of restitution, <c>vBounceFactor</c>.</param>
/// <param name="FrictionX">Its first friction coefficient, <c>vFrictionX</c>.</param>
/// <param name="FrictionY">Its second, <c>vFrictionY</c>: the one a contact's friction reads.</param>
public readonly record struct PhysicsContactPointType(int MagicVertex, int Type, int Material, float Bounce, float FrictionX, float FrictionY);

/// <summary>A kind of physics object, <c>PhysicsObjType_s</c> (728 bytes), as <c>DefinePhysicsObjType</c> defines it.</summary>
/// <param name="TaskType">The task type that moves objects of it, <c>eQTaskType</c>.</param>
/// <param name="TaskTypeName">That task type's name.</param>
/// <param name="Id">Its number, <c>nID</c>.</param>
/// <param name="Mass">Its mass, <c>vMass</c>.</param>
/// <param name="Size">Its box in metres, <c>tSize</c>.</param>
/// <param name="Name">Its name, <c>zName</c>.</param>
/// <param name="Description">Its description, <c>zDesc</c>.</param>
/// <param name="Model">The model it is the type of, <c>zModel</c>: what a type is looked up by.</param>
/// <param name="ContactPoints">Its contact points, <c>atCollPoint</c>.</param>
/// <param name="RestSpeed">The speed below which, in contact, it may rest, <c>vRestSpeed</c>.</param>
/// <param name="RestAngularVelocitySquared">The squared spin below which it may rest, <c>vRestAngMomSquared</c>.</param>
/// <param name="Parameters">The arguments after the common ones, which its task type parses for itself (<c>pxData</c>).</param>
/// <param name="Arguments">The whole call, for a reader that indexes it as the original's task types do.</param>
public sealed record PhysicsObjectType(
    int TaskType,
    string TaskTypeName,
    int Id,
    float Mass,
    Vector3f Size,
    string Name,
    string Description,
    string Model,
    IReadOnlyList<PhysicsContactPointType> ContactPoints,
    float RestSpeed,
    float RestAngularVelocitySquared,
    IReadOnlyList<QvmValue> Parameters,
    IReadOnlyList<QvmValue> Arguments);

/// <summary>
/// The physics object types: every script under <c>LOCAL:physicsobj</c> defines its own through <c>DefinePhysicsObjType</c>
/// (Project I.G.I. <c>0x4EDD20</c>, I.G.I. 2 <c>PhysicsObjType_DefinePhysicsObjTypeCB</c>, <c>igi2 0x43B900</c>).
/// </summary>
/// <remarks>
/// <para>
/// One engine native in both games, reading one layout: the task type, the number, the mass and the box, the three names,
/// the contact points (each a magic vertex, a kind, a material, a bounce and two frictions), the two rest thresholds, then
/// what the task type reads for itself. A model defined twice is fatal in both.
/// </para>
/// <para>
/// The scripts name task types and constants their game registers (the grenade's kinds, the missile's smoke and guidance),
/// which each game hands in. I.G.I. 2's flow walks the directory (<c>PhysicsObjType_ProcessFileCB</c>, <c>0x43BEC0</c>).
/// </para>
/// </remarks>
/// <param name="taskTypes">The task types the scripts name, as constant and type name.</param>
/// <param name="constants">The constants the type-specific arguments name.</param>
/// <param name="logger">Where the load reports.</param>
public sealed class PhysicsObjectTypes(
    IReadOnlyList<(string Constant, string Type)> taskTypes,
    IReadOnlyList<(string Name, int Value)> constants,
    IOpenIgiLogger? logger = null)
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

    private readonly IReadOnlyList<(string Constant, string Type)> _taskTypes = taskTypes ?? throw new ArgumentNullException(nameof(taskTypes));
    private readonly IReadOnlyList<(string Name, int Value)> _constants = constants ?? throw new ArgumentNullException(nameof(constants));
    private readonly List<PhysicsObjectType> _types = [];
    private readonly Dictionary<string, int> _typeIds = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _typeNames = [];
    private readonly IOpenIgiLogger _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the types, in the order they were defined.</summary>
    public IReadOnlyList<PhysicsObjectType> Types => _types;

    /// <summary>Registers the native, the task types the scripts name and the constants their own arguments use.</summary>
    /// <param name="natives">The script namespace; a constant an earlier module registered is left as it is.</param>
    /// <param name="types">
    /// The task-type registry, whose ids the task types take, a named type no class implements yet declared with no
    /// parameters; <see langword="null"/> numbers them here, for a reader that only needs their names.
    /// </param>
    public void Register(NativeRegistry natives, TaskTypeRegistry? types)
    {
        ArgumentNullException.ThrowIfNull(natives);
        int next = 1;
        foreach ((string constant, string type) in _taskTypes)
        {
            int id = types is null ? next++ : (types.TryGet(type, out TaskType? known) ? known : types.Declare(type, [])).Id;
            _typeIds[type] = id;
            _typeNames[id & 0xFFFF] = type;
            if (!natives.Contains(constant))
            {
                natives.RegisterConstant(constant, id);
            }
        }

        foreach ((string name, int value) in _constants)
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

    /// <summary>Finds the type of a model (<c>PhysicsObjType_FindType</c>, <c>igi2 0x43C050</c>): the first defined for it.</summary>
    /// <param name="model">The model's name.</param>
    /// <param name="type">Receives the type.</param>
    /// <returns><see langword="false"/> when no type names the model.</returns>
    public bool TryFind(string model, out PhysicsObjectType type)
    {
        foreach (PhysicsObjectType defined in _types)
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
    public bool IsMovedBy(PhysicsObjectType type, string taskType)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _typeIds.TryGetValue(taskType, out int id) && (id & 0xFFFF) == type.TaskType;
    }

    private QvmValue Define(QvmCallContext call)
    {
        string model = call.GetString(8);
        foreach (PhysicsObjectType defined in _types)
        {
            if (string.Equals(defined.Model, model, StringComparison.Ordinal))
            {
                throw new EngineFatalException($"Physics obj model {model} is defined multiple times.");
            }
        }

        int count = call.GetInt32(9);
        PhysicsContactPointType[] points = new PhysicsContactPointType[Math.Max(count, 0)];
        int argument = HeaderArguments;
        for (int i = 0; i < points.Length; i++, argument += PointArguments)
        {
            points[i] = new PhysicsContactPointType(
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
        QvmValue[] all = new QvmValue[call.ArgumentCount];
        for (int i = 0; i < all.Length; i++)
        {
            all[i] = call.GetValue(i);
        }

        int taskType = call.GetInt32(0) & 0xFFFF;
        _types.Add(new PhysicsObjectType(
            taskType,
            _typeNames.TryGetValue(taskType, out string? name) ? name : string.Empty,
            call.GetInt32(1),
            call.GetSingle(2),
            new Vector3f(call.GetSingle(3), call.GetSingle(4), call.GetSingle(5)),
            call.GetString(6),
            call.GetString(7),
            model,
            points,
            restSpeed,
            restSpin,
            all.Length > argument ? all[argument..] : [],
            all));
        return QvmValue.FromInt32(1);
    }
}
