using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Player;

/// <summary>
/// The animation-trigger table: what an animation event id fires.
/// </summary>
/// <remarks>
/// <para>
/// An animation's <c>BOED</c> events carry an id, a bone and a position
///. This table is what turns that id into something audible.
/// It is filled by running <c>animtrigger/animtrigger.qvm</c>, which is nothing but 50
/// <c>DefineAnimTrigger(eventId, taskType, resource, flag)</c> calls — the same shape as
/// <see cref="World.MagicObjectRegistry"/>.
/// </para>
/// <para>
/// Note what is <b>not</b> in it: event <c>1010</c>, which is by far the commonest in the retail
/// animation data. A table entry names one fixed sound, which cannot express a footstep whose sound
/// depends on the ground — and the four <c>walk_fence_*</c> entries that <em>are</em> here are a
/// specific surface. So the absence is consistent with 1010 being handled inside the engine.
/// </para>
/// </remarks>
public sealed class AnimTriggerRegistry
{
    private const string LogCategory = "Animation";

    /// <summary>The script that fills the table.</summary>
    public const string ScriptResource = "LOCAL:animtrigger/animtrigger.qvm";

    /// <summary>The native the script calls to publish an entry.</summary>
    public const string DefineNative = "DefineAnimTrigger";

    /// <summary>The prefix of the task-type constants the script's second argument uses.</summary>
    public const string TaskTypeConstantPrefix = "TASKTYPE_";

    private readonly Dictionary<int, AnimTriggerDefinition> _byId = [];
    private readonly Dictionary<int, string> _taskTypeNames = [];
    private readonly IOpenIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="AnimTriggerRegistry"/> class.</summary>
    /// <param name="logger">The logger for load diagnostics; defaults to a sink that discards them.</param>
    public AnimTriggerRegistry(IOpenIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the entries, keyed by event id.</summary>
    public IReadOnlyDictionary<int, AnimTriggerDefinition> Definitions => _byId;

    /// <summary>Gets the number of entries.</summary>
    public int Count => _byId.Count;

    /// <summary>Registers the <c>DefineAnimTrigger</c> native.</summary>
    /// <param name="natives">The script namespace the trigger script resolves against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="natives"/> is <see langword="null"/>.</exception>
    public void Register(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);
        natives.RegisterFunction(DefineNative, Define);
    }

    /// <summary>Reads and runs the trigger script.</summary>
    /// <param name="resources">Where the script is read from.</param>
    /// <param name="natives">The script namespace, with <see cref="Register"/> already applied.</param>
    /// <returns>The number of entries registered.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="EngineFatalException">The script is present but malformed or faults.</exception>
    /// <remarks>
    /// A missing script is not fatal — the original's loader reports
    /// <c>"AnimTrigger_ParseConfigFile(): Couldn't load script: %s"</c> and carries on, which costs only
    /// the sounds.
    /// </remarks>
    public int Load(ResourceRegistry resources, NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);

        _byId.Clear();
        _taskTypeNames.Clear();

        if (!resources.TryLoad(ScriptResource, out ReadOnlyMemory<byte> bytes))
        {
            _logger.Warning(
                LogCategory,
                $"AnimTrigger_ParseConfigFile(): Couldn't load script: {ScriptResource}");
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

        _logger.Information(LogCategory, $"Registered {Count} animation trigger(s).");
        return Count;
    }

    /// <summary>Looks an entry up by event id.</summary>
    /// <param name="eventId">The id from an animation event.</param>
    /// <param name="definition">Receives the entry when one is registered.</param>
    /// <returns><see langword="true"/> when the id fires something.</returns>
    public bool TryGet(int eventId, out AnimTriggerDefinition definition) =>
        _byId.TryGetValue(eventId, out definition);

    /// <summary>Gets the script constant a task type id came from, for diagnostics.</summary>
    /// <param name="taskType">The id.</param>
    /// <returns>The constant's name, or an empty string when it is not one this registry allocated.</returns>
    public string TaskTypeName(int taskType) => _taskTypeNames.GetValueOrDefault(taskType, string.Empty);

    /// <summary>
    /// Declares the <c>TASKTYPE_*</c> constants the script's second argument uses.
    /// </summary>
    /// <remarks>
    /// The same deliberate divergence as <see cref="World.MagicObjectRegistry"/>: the original's task
    /// classes export their own ids at start-up, so the numbers depend only on registration order and
    /// appear in no data file. Until those classes exist the ids are allocated in symbol-table order.
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
        int eventId = call.GetInt32(0);
        int taskType = call.GetInt32(1);
        AnimTriggerDefinition definition = new(
            eventId,
            TaskTypeName(taskType),
            call.GetString(2),
            call.GetInt32(3) != 0);

        // A repeated id keeps the first, matching the original's append-then-find-first lookup.
        _byId.TryAdd(eventId, definition);
        return QvmValue.FromInt32(1);
    }
}
