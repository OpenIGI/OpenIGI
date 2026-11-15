using OpenIGI.Core.Diagnostics;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Engine.Tasks;

/// <summary>
/// The script commands level data builds a task tree with: <c>Task_New</c> and
/// <c>Task_DeclareParameters</c>.
/// </summary>
/// <remarks>
/// <para>
/// A level's <c>objects.qvm</c> is one long sequence of these. Declarations come first, one per task
/// type, naming every parameter and its value type; then a single deeply nested <c>Task_New</c> tree.
/// </para>
/// <para>
/// Nesting works by argument, not by block. <c>Task_New</c> parses its parameters, then rebinds its own
/// symbol's context to the task it just made and evaluates its <em>remaining</em> arguments — so any
/// <c>Task_New</c> among them attaches to that task — and finally restores the previous context. That is
/// why parameter counts have to be exact: consume one too few and the next child becomes a parameter
/// value; one too many and a child is silently skipped.
/// </para>
/// </remarks>
public sealed class TaskScriptCommands
{
    private const string LogCategory = "Tasks";

    /// <summary>The script name of the task-spawning command.</summary>
    public const string TaskNew = "Task_New";

    /// <summary>The script name of the parameter-declaration command.</summary>
    public const string TaskDeclareParameters = "Task_DeclareParameters";

    private readonly TaskTree _tree;
    private readonly ScriptVarRegistry _variables;
    private readonly IOpenIgiLogger _logger;
    private QvmSymbol? _taskNewSymbol;
    private NativeRegistry? _symbols;
    private Rebinding? _rebinding;

    /// <summary>Initializes a new instance of the <see cref="TaskScriptCommands"/> class.</summary>
    /// <param name="tree">The tree tasks are created in.</param>
    /// <param name="variables">The registry exported task variables are published into.</param>
    /// <param name="logger">The logger for load diagnostics; defaults to a sink that discards them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> or <paramref name="variables"/> is <see langword="null"/>.</exception>
    public TaskScriptCommands(TaskTree tree, ScriptVarRegistry variables, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(variables);
        _tree = tree;
        _variables = variables;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the next <c>Task_New</c> inserts at the head of its
    /// parent's children instead of the tail.
    /// </summary>
    /// <remarks>The original's one-shot <c>byte_A758BC</c>, which <c>Task_New</c> consumes and clears.</remarks>
    public bool CreateAsFirstChild { get; set; }

    /// <summary>Gets the first task the current script created, which level loading treats as its root.</summary>
    public GameTask? FirstCreatedTask { get; private set; }

    /// <summary>Registers both commands in a script namespace.</summary>
    /// <param name="symbols">The namespace to register into.</param>
    /// <param name="parent">The task new top-level tasks attach to; defaults to the tree root.</param>
    /// <exception cref="ArgumentNullException"><paramref name="symbols"/> is <see langword="null"/>.</exception>
    public void Register(NativeRegistry symbols, GameTask? parent = null)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        _symbols = symbols;
        _taskNewSymbol = symbols.RegisterFunction(
            TaskNew,
            OnTaskNew,
            // The original registers both as variadic (declared count -1). The declared count is
            // advisory here — the call site's own count is what natives read — so zero says the same.
            argumentCount: 0,
            context: parent ?? _tree.Root);

        symbols.RegisterFunction(TaskDeclareParameters, OnDeclareParameters);
    }

    /// <summary>Resets the per-load state before a script runs.</summary>
    /// <param name="createAsFirstChild">Whether the script's first task prepends rather than appends.</param>
    /// <remarks>
    /// <para>
    /// The original's <c>Script_BeginTaskLoad</c> (<c>0x4F0E50</c>), which every script load calls between
    /// compiling the script and running it. Its placement matters: a script that fails to load leaves the
    /// previous load's state in place, because the reset never happens.
    /// </para>
    /// <para>
    /// The original also stores the script path and a line-info slot here, both of which only feed its
    /// error messages.
    /// </para>
    /// </remarks>
    public void BeginLoad(bool createAsFirstChild = false)
    {
        CreateAsFirstChild = createAsFirstChild;
        FirstCreatedTask = null;
    }

    /// <summary>Points <c>Task_New</c> at the task new top-level tasks should attach to.</summary>
    /// <param name="parent">The parent task.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parent"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The commands have not been registered yet.</exception>
    /// <remarks>Level loading does this before running a level script, binding it to the level root.</remarks>
    public void SetSymbolContext(GameTask parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        if (_taskNewSymbol is null)
        {
            throw new InvalidOperationException($"{nameof(Register)} must be called before {nameof(SetSymbolContext)}.");
        }

        _taskNewSymbol.Context = parent;
    }

    /// <summary>
    /// Gives a task already in the level the values a compiled <c>Task_New</c> call of its type gives: read exactly as a load reads
    /// them (the same VM, the same parameter readers), onto a task made apart from the tree, then moved onto the live one by
    /// name. The live task keeps its place, its id, its variables and everything it has done; only its parameters change.
    /// </summary>
    /// <param name="live">The task in the level.</param>
    /// <param name="call">A compiled script whose one statement is a <c>Task_New</c> of the task's type (children, if any, are
    /// not made).</param>
    /// <returns>Null when the values were read and given; otherwise why not (a script that faults, or names another type).</returns>
    /// <remarks>Not retail: an editor's change to a level that is running. A type an adapter reads is given them through it.</remarks>
    public string? Rebind(GameTask live, ReadOnlyMemory<byte> call)
    {
        ArgumentNullException.ThrowIfNull(live);
        if (_symbols is null)
        {
            return $"{nameof(Register)} has not been called.";
        }

        // Run in the level's own namespace (its constants -- TRUE, GAME_FREQUENCY -- are what the values are spelt with), Task_New
        // read rather than obeyed while it does.
        Rebinding done = new(live);
        _rebinding = done;
        try
        {
            QvmInterpreter interpreter = new(QvmProgram.Read(call), _symbols);
            if (interpreter.Run() is var error and not QvmError.None)
            {
                return $"The script faulted with {error}: {interpreter.ErrorMessage}";
            }
        }
        finally
        {
            _rebinding = null;
        }

        if (done.LevelValues is { } values)
        {
            live.Type.ParameterAdapter!.Apply(values, live);
            return null;
        }

        if (done.Made is not { } made)
        {
            return $"The script made no {live.Type.Name}.";
        }

        foreach (TaskParameter parameter in made.GetDeclaredScriptParameters())
        {
            if (made.TryGetScriptValue(parameter.Name, out object? value))
            {
                live.TrySetScriptValue(parameter.Name, value);
            }
        }

        return null;
    }

    // A Task_New met while a task is being given new values: read onto a task apart from the tree, or in the level's terms for a
    // type an adapter reads.
    private QvmValue ReadForRebind(QvmCallContext call, Rebinding rebinding)
    {
        GameTask live = rebinding.Live;
        if (rebinding.Made is not null || rebinding.LevelValues is not null || call.ArgumentCount < 3
            || !ReferenceEquals(_tree.Types.Get(call.GetString(1)), live.Type))
        {
            return QvmValue.Zero;
        }

        ScriptArgumentReader reader = new(call, ScriptArgumentReader.FirstParameterArgument);
        if (live.Type.ParameterAdapter is not null)
        {
            rebinding.LevelValues = TaskParameterValues.Read(reader, live.Type.Parameters);
        }
        else
        {
            GameTask made = live.Type.CreateInstance();
            made.InvokeBindScriptParameters(reader);
            rebinding.Made = made;
        }

        return QvmValue.Zero;
    }

    // What a rebind has read so far.
    private sealed class Rebinding(GameTask live)
    {
        public GameTask Live { get; } = live;

        public GameTask? Made { get; set; }

        public TaskParameterValues? LevelValues { get; set; }
    }
    private QvmValue OnDeclareParameters(QvmCallContext call)
    {
        if (call.ArgumentCount < 1)
        {
            return QvmValue.Zero;
        }

        string typeName = call.GetString(0);

        // Name/type string pairs follow the type name. A trailing odd argument would mean a malformed
        // declaration; take only the complete pairs rather than reading past the call site.
        var parameters = new List<TaskParameter>((call.ArgumentCount - 1) / 2);
        for (int i = 1; i + 1 < call.ArgumentCount; i += 2)
        {
            string parameterName = call.GetString(i);
            string parameterType = call.GetString(i + 1);

            if (!ScriptParamTypeInfo.TryParseName(parameterType, out ScriptParamType type))
            {
                throw new EngineFatalException(
                    $"Task type '{typeName}' declares parameter '{parameterName}' with unknown type '{parameterType}'.");
            }

            parameters.Add(new TaskParameter(parameterName, type));
        }

        _tree.Types.Declare(typeName, parameters);
        return QvmValue.Zero;
    }

    private QvmValue OnTaskNew(QvmCallContext call)
    {
        if (_rebinding is { } rebinding)
        {
            return ReadForRebind(call, rebinding);
        }

        if (call.ArgumentCount < 3)
        {
            throw new EngineFatalException(
                $"{TaskNew} needs at least an id, a type name and a label, but got {call.ArgumentCount} arguments.");
        }

        var parent = call.Context as GameTask;
        int id = call.GetInt32(0);
        string typeName = call.GetString(1);
        string label = call.GetString(2);

        TaskType type = _tree.Types.Get(typeName);

        GameTask task = CreateAsFirstChild
            ? _tree.NewSibling(parent, type)
            : _tree.New(parent, type);
        CreateAsFirstChild = false;

        task.Label = label;
        FirstCreatedTask ??= task;

        // A collision does not fault: the original checks Task_FromId first and quietly drops the id,
        // so the task stays anonymous and never exports named variables.
        bool hasId = id >= 0 && id < TaskTree.IdCapacity && _tree.TryBindId(id, task);
        if (id >= 0 && !hasId)
        {
            _logger.Warning(LogCategory, $"Task id {id} is already bound; '{typeName}' was created without one.");
        }

        var reader = new ScriptArgumentReader(call, ScriptArgumentReader.FirstParameterArgument);
        if (type.ParameterAdapter is { } adapter)
        {
            // A class made to stand in for this type: the level's values, read in the level's terms, adapted onto it.
            adapter.Apply(TaskParameterValues.Read(reader, type.Parameters), task);
        }
        else
        {
            task.InvokeBindScriptParameters(reader);
        }

        if (hasId)
        {
            _variables.Export(task);
        }

        task.Receive(TaskMessage.Parsed);

        EvaluateNestedChildren(call, task, reader.Position);
        return QvmValue.Zero;
    }

    private void EvaluateNestedChildren(QvmCallContext call, GameTask task, int firstChildArgument)
    {
        if (_taskNewSymbol is null)
        {
            return;
        }

        // Everything after the parameters is a child expression. Rebinding the context is what makes a
        // nested Task_New attach to this task rather than to its parent; the previous context has to be
        // restored afterwards, including when a child throws.
        object? previous = _taskNewSymbol.Context;
        _taskNewSymbol.Context = task;
        try
        {
            for (int i = firstChildArgument; i < call.ArgumentCount; i++)
            {
                call.GetInt32(i);
            }
        }
        finally
        {
            _taskNewSymbol.Context = previous;
        }
    }
}
