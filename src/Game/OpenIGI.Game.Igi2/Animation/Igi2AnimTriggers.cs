using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Animation;

/// <summary>The anim trigger ids the human's clips carry, <c>HUMANANIM_TRIGGER_*</c> (<c>Human_Open</c>, <c>igi2 0x504EE8</c>).</summary>
public static class Igi2HumanAnimTrigger
{
    /// <summary><c>HUMANANIM_TRIGGER_OPENDRAWER</c>.</summary>
    public const int OpenDrawer = 0;

    /// <summary><c>HUMANANIM_TRIGGER_CLOSEDRAWER</c>.</summary>
    public const int CloseDrawer = 1;

    /// <summary><c>HUMANANIM_TRIGGER_DROPWEAPON</c>.</summary>
    public const int DropWeapon = 2;

    /// <summary><c>HUMANANIM_TRIGGER_THROWGRENADE</c>: the moment a thrown weapon leaves the hand.</summary>
    public const int ThrowGrenade = 3;

    /// <summary><c>HUMANANIM_TRIGGER_TIMEGRENADE</c>.</summary>
    public const int TimeGrenade = 4;

    /// <summary><c>HUMANANIM_TRIGGER_EMPTYBARREL</c>.</summary>
    public const int EmptyBarrel = 5;

    /// <summary><c>HUMANANIM_TRIGGER_LOADBARREL</c>.</summary>
    public const int LoadBarrel = 6;

    /// <summary><c>HUMANANIM_TRIGGER_STOPANIMATION</c>: where a clip holds while its key is held.</summary>
    public const int StopAnimation = 8;

    /// <summary><c>HUMANANIM_TRIGGER_THROWGRENADE_CREATE</c>.</summary>
    public const int ThrowGrenadeCreate = 9;

    /// <summary><c>HUMANANIM_TRIGGER_THROWGRENADE_ACTIVATE</c>.</summary>
    public const int ThrowGrenadeActivate = 10;

    /// <summary><c>HUMANANIM_TRIGGER_THROWGRENADE_RELEASE</c>.</summary>
    public const int ThrowGrenadeRelease = 11;

    /// <summary><c>HUMANANIM_TRIGGER_CLOSECOMBAT_DEALDAMAGE</c>.</summary>
    public const int CloseCombatDealDamage = 12;

    /// <summary><c>HUMANANIM_TRIGGER_DEADBODY_SOUND</c>.</summary>
    public const int DeadBodySound = 13;

    /// <summary><c>HUMANANIM_TRIGGER_FEETSOUND</c>.</summary>
    public const int FeetSound = 0x3F2;

    /// <summary>The constants, as the script names them.</summary>
    internal static readonly (string Name, int Value)[] Constants =
    [
        ("HUMANANIM_TRIGGER_OPENDRAWER", OpenDrawer),
        ("HUMANANIM_TRIGGER_CLOSEDRAWER", CloseDrawer),
        ("HUMANANIM_TRIGGER_DROPWEAPON", DropWeapon),
        ("HUMANANIM_TRIGGER_THROWGRENADE", ThrowGrenade),
        ("HUMANANIM_TRIGGER_TIMEGRENADE", TimeGrenade),
        ("HUMANANIM_TRIGGER_EMPTYBARREL", EmptyBarrel),
        ("HUMANANIM_TRIGGER_LOADBARREL", LoadBarrel),
        ("HUMANANIM_TRIGGER_STOPANIMATION", StopAnimation),
        ("HUMANANIM_TRIGGER_FEETSOUND", FeetSound),
        ("HUMANANIM_TRIGGER_THROWGRENADE_CREATE", ThrowGrenadeCreate),
        ("HUMANANIM_TRIGGER_THROWGRENADE_ACTIVATE", ThrowGrenadeActivate),
        ("HUMANANIM_TRIGGER_THROWGRENADE_RELEASE", ThrowGrenadeRelease),
        ("HUMANANIM_TRIGGER_CLOSECOMBAT_DEALDAMAGE", CloseCombatDealDamage),
        ("HUMANANIM_TRIGGER_DEADBODY_SOUND", DeadBodySound),
    ];
}

/// <summary>One anim trigger type, <c>AnimTriggerType_s</c>: its id, the task type that answers it and what it was defined with.</summary>
/// <param name="Id">The id clips carry, <c>iID</c>.</param>
/// <param name="TaskType">The task type whose <c>TriggerAnimTrigger</c> handler answers it, <c>eQTaskType</c>.</param>
/// <param name="Parameters">The definition's arguments after the first two, which that type parses for itself.</param>
public sealed record Igi2AnimTriggerType(int Id, int TaskType, IReadOnlyList<QvmValue> Parameters);

/// <summary>
/// The anim trigger types, <c>AnimTrigger_atAnimTriggerType</c>, as <c>animtrigger.qvm</c> defines them through
/// <c>DefineAnimTrigger</c> (<c>AnimTrigger_DefineAnimTriggerCB</c>, <c>igi2 0x433A10</c>).
/// </summary>
/// <remarks>
/// A clip's trigger fires the type of its id: <c>AnimTrigger_Trigger</c> (<c>igi2 0x433D50</c>) takes the first type defined
/// with it and sends its task type's <c>TriggerAnimTrigger</c> event with the object the clip plays on, the id and where it
/// fires. An id defined by nothing fires nothing.
/// </remarks>
public sealed class Igi2AnimTriggers
{
    /// <summary>The script.</summary>
    public const string Script = "LOCAL:animtrigger/animtrigger.qvm";

    /// <summary>The native the script defines each type with.</summary>
    public const string DefineNative = "DefineAnimTrigger";

    private const string LogCategory = "AnimTrigger";

    /// <summary>The task types the script names, as constant and type name.</summary>
    private static readonly (string Constant, string Type)[] TaskTypes =
    [
        ("TASKTYPE_HUMAN", "Human"),
        ("TASKTYPE_HUMANPLAYER", "HumanPlayer"),
        ("TASKTYPE_HUMANSOLDIER", "HumanSoldier"),
        ("TASKTYPE_ANIMSOUND", "AnimSound"),
    ];

    private readonly List<Igi2AnimTriggerType> _types = [];
    private readonly Dictionary<string, int> _typeIds = new(StringComparer.Ordinal);
    private readonly IOpenIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="Igi2AnimTriggers"/> class.</summary>
    /// <param name="logger">Where the load reports.</param>
    public Igi2AnimTriggers(IOpenIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the types, in the order they were defined.</summary>
    public IReadOnlyList<Igi2AnimTriggerType> Types => _types;

    /// <summary>Registers the native, the trigger ids and the task types the script names.</summary>
    /// <param name="natives">The script namespace.</param>
    /// <param name="types">The task-type registry; a named type no class implements yet is declared with no parameters.</param>
    public void Register(NativeRegistry natives, TaskTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(types);

        foreach ((string name, int value) in Igi2HumanAnimTrigger.Constants)
        {
            natives.RegisterConstant(name, value);
        }

        foreach ((string constant, string type) in TaskTypes)
        {
            TaskType registered = types.TryGet(type, out TaskType? known) ? known : types.Declare(type, []);
            _typeIds[type] = registered.Id;
            natives.RegisterConstant(constant, registered.Id);
        }

        natives.RegisterFunction(DefineNative, Define);
    }

    /// <summary>Runs the script.</summary>
    /// <param name="resources">Where the script is read from.</param>
    /// <param name="natives">The script namespace, with <see cref="Register"/> applied.</param>
    public void Load(ResourceRegistry resources, NativeRegistry natives)
    {
        _types.Clear();
        Igi2SessionScript.Run(resources, natives, Script);
        _logger.Information(LogCategory, $"{_types.Count} anim trigger types read.");
    }

    /// <summary>Finds the type a clip's trigger fires, the first defined with its id.</summary>
    /// <param name="id">The trigger's id.</param>
    /// <param name="type">Receives the type.</param>
    /// <returns><see langword="false"/> when no type has that id.</returns>
    public bool TryGet(int id, out Igi2AnimTriggerType type)
    {
        foreach (Igi2AnimTriggerType defined in _types)
        {
            if (defined.Id == id)
            {
                type = defined;
                return true;
            }
        }

        type = null!;
        return false;
    }

    /// <summary>Gets whether a type is answered by a named task type.</summary>
    /// <param name="type">The trigger type.</param>
    /// <param name="taskType">The task type's name.</param>
    /// <returns><see langword="true"/> when that task type answers it.</returns>
    public bool IsAnsweredBy(Igi2AnimTriggerType type, string taskType)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _typeIds.TryGetValue(taskType, out int id) && id == type.TaskType;
    }

    private QvmValue Define(QvmCallContext call)
    {
        QvmValue[] parameters = new QvmValue[Math.Max(call.ArgumentCount - 2, 0)];
        for (int i = 0; i < parameters.Length; i++)
        {
            parameters[i] = call.GetValue(i + 2);
        }

        _types.Add(new Igi2AnimTriggerType(call.GetInt32(0), call.GetInt32(1) & 0xFFFF, parameters));
        return QvmValue.FromInt32(1);
    }
}
