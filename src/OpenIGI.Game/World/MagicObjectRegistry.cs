using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.World;

/// <summary>
/// The magic-object table: the map from a model attachment's name to the object that hangs off it.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes a level bigger than its script. <c>0x4DFA50</c> walks the attachments of every
/// placed model, looks each name up here, and spawns a child object carrying the definition's model at
/// the attachment's position and orientation — then does the same to that model's own attachments. A
/// level-1 garage whose script node has no children at all still ends up with 64 pieces this way.
/// </para>
/// <para>
/// The table is filled by running <c>magicobj/magicobj.qvm</c>, which is nothing but 387
/// <c>DefineMagicObj(name, model, TASKTYPE_*)</c> calls.
/// </para>
/// </remarks>
public sealed class MagicObjectRegistry
{
    private const string LogCategory = "World";

    /// <summary>The native the script calls to publish a definition.</summary>
    public const string DefineNative = "DefineMagicObj";

    /// <summary>The script that fills the table.</summary>
    public const string ScriptResource = "LOCAL:magicobj/magicobj.qvm";

    /// <summary>The prefix of the task-type constants the script's third argument uses.</summary>
    public const string TaskTypeConstantPrefix = "TASKTYPE_";

    private readonly Dictionary<string, MagicObjectDefinition> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> _taskTypeNames = [];
    private readonly IIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="MagicObjectRegistry"/> class.</summary>
    /// <param name="logger">The logger for load diagnostics; defaults to a sink that discards them.</param>
    public MagicObjectRegistry(IIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the definitions, keyed by attachment name.</summary>
    public IReadOnlyDictionary<string, MagicObjectDefinition> Definitions => _byName;

    /// <summary>Gets the number of definitions.</summary>
    public int Count => _byName.Count;

    /// <summary>Registers the <c>DefineMagicObj</c> native.</summary>
    /// <param name="natives">The script namespace the magic-object script resolves against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="natives"/> is <see langword="null"/>.</exception>
    public void Register(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);
        natives.RegisterFunction(DefineNative, Define);
    }

    /// <summary>Reads and runs the magic-object script.</summary>
    /// <param name="resources">Where the script is read from.</param>
    /// <param name="natives">The script namespace, with <see cref="Register"/> already applied.</param>
    /// <returns>The number of definitions registered.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="EngineFatalException">The script is present but malformed or faults.</exception>
    /// <remarks>
    /// A missing script is not an error: an installation without one simply has no magic objects, and
    /// every level still loads — it just loses the pieces its models attach.
    /// </remarks>
    public int Load(ResourceRegistry resources, NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);

        _byName.Clear();
        _taskTypeNames.Clear();

        if (!resources.TryLoad(ScriptResource, out ReadOnlyMemory<byte> bytes))
        {
            _logger.Warning(LogCategory, $"No magic-object script at '{ScriptResource}'; models attach nothing.");
            return 0;
        }

        QvmProgram program;
        try
        {
            program = QvmProgram.Read(bytes);
        }
        catch (QvmFormatException ex)
        {
            throw new EngineFatalException($"Couldn't load script: {ScriptResource}", ex);
        }

        DeclareTaskTypeConstants(program, natives);

        QvmInterpreter interpreter = new(program, natives);
        QvmError error = interpreter.Run();
        if (error != QvmError.None)
        {
            throw new EngineFatalException(
                $"{ScriptResource} faulted with {error}: {interpreter.ErrorMessage}");
        }

        _logger.Information(LogCategory, $"Registered {Count} magic object(s).");
        return Count;
    }

    /// <summary>Looks a definition up by attachment name.</summary>
    /// <param name="name">The attachment name.</param>
    /// <param name="definition">Receives the definition when one is registered.</param>
    /// <returns><see langword="true"/> when the name is defined.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool TryGet(string name, out MagicObjectDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.TryGetValue(name, out definition);
    }

    /// <summary>Gets the script constant a task type id came from, for diagnostics.</summary>
    /// <param name="taskType">The id from a <see cref="MagicObjectDefinition"/>.</param>
    /// <returns>The constant's name, or an empty string when the id is not one this registry allocated.</returns>
    public string TaskTypeName(int taskType) => _taskTypeNames.GetValueOrDefault(taskType, string.Empty);

    /// <summary>
    /// Declares the <c>TASKTYPE_*</c> constants the script's third argument uses.
    /// </summary>
    /// <remarks>
    /// <b>Divergence, deliberate.</b> The original allocates a task type id at startup as each class
    /// registers and exports it under this name (<c>0x4B8810</c>), so the number depends only on the order
    /// classes happen to initialise and never appears in a data file. None of those classes exist yet, so
    /// the ids here are allocated in the order the script's symbol table lists them and kept alongside
    /// their names. Nothing reads the value yet; when the classes arrive they export their own ids and
    /// this fallback stops firing for them.
    /// </remarks>
    private void DeclareTaskTypeConstants(QvmProgram program, NativeRegistry natives)
    {
        int next = 1;
        foreach (string? symbol in program.SymbolNames)
        {
            if (symbol is null
                || !symbol.StartsWith(TaskTypeConstantPrefix, StringComparison.Ordinal)
                || natives.Contains(symbol))
            {
                continue;
            }

            natives.RegisterConstant(symbol, next);
            _taskTypeNames[next] = symbol;
            next++;
        }
    }

    private QvmValue Define(QvmCallContext call)
    {
        int taskType = call.GetInt32(2);
        string typeName = TaskTypeName(taskType);
        bool explosive = string.Equals(
            typeName,
            "TASKTYPE_EXPLODEMAGICOBJ",
            StringComparison.Ordinal);
        bool isRadarDish = string.Equals(
            typeName,
            "TASKTYPE_RADARDISH",
            StringComparison.Ordinal);

        // ExplodeMagicObj extends DefineMagicObj with the native effect recipe. The first three
        // arguments are shared by every definition. The retail QVM's extra arguments are, in order:
        // damaged model, sound, FireBalls, Fragments, Delay, damage scale, falloff radius, full radius,
        // and maximum damage before explosion. Other magic families do not own an explosion recipe,
        // even when they use a longer call signature for their own data.
        string damagedModel = explosive && call.ArgumentCount > 3 ? call.GetString(3) : string.Empty;
        string sound = explosive && call.ArgumentCount > 4 ? call.GetString(4) : string.Empty;
        float fireballs = explosive && call.ArgumentCount > 5 ? call.GetSingle(5) : 0f;
        int fragments = explosive && call.ArgumentCount > 6 ? call.GetInt32(6) : 0;
        int delayTicks = explosive && call.ArgumentCount > 7 ? call.GetInt32(7) : 0;
        float damageFactor = explosive && call.ArgumentCount > 8 ? call.GetSingle(8) : 0f;
        float falloff = explosive && call.ArgumentCount > 9 ? call.GetSingle(9) : 0f;
        float radius = explosive && call.ArgumentCount > 10 ? call.GetSingle(10) : 0f;
        float threshold = explosive && call.ArgumentCount > 11 ? call.GetSingle(11) : 0f;

        // TASKTYPE_RADARDISH (parsed by 0x449220): arg 3 sound name, arg 4 rotation speed (deg/s), arg 5 acceleration (deg/s^2).
        string dishSound = isRadarDish && call.ArgumentCount > 3 ? call.GetString(3) : string.Empty;
        float dishSpeed = isRadarDish && call.ArgumentCount > 4 ? call.GetSingle(4) : 0f;
        float dishAccel = isRadarDish && call.ArgumentCount > 5 ? call.GetSingle(5) : 0f;

        MagicObjectDefinition definition = new(
            call.GetString(0),
            call.GetString(1),
            taskType,
            damagedModel,
            sound,
            fireballs,
            fragments,
            delayTicks,
            damageFactor,
            falloff,
            radius,
            threshold,
            dishSound,
            dishSpeed,
            dishAccel);

        // The original appends unconditionally and its lookup takes the first match, so a repeated name
        // keeps the earlier definition.
        _byName.TryAdd(definition.Name, definition);
        return QvmValue.FromInt32(1);
    }
}
