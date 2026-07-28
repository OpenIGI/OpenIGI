using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Engine.Tasks;

/// <summary>
/// The named task fields mission scripts can read and write: <c>&lt;taskname&gt;.&lt;field&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// A task with a script id exports selected fields under its own name, so a mission script can say
/// <c>AlarmControl_98.isAlarm</c> or <c>CutScene_1200.nTick</c>. The original binds the field's
/// <em>address</em> (<c>QVM_BindVar</c>, <c>0x4B7E80</c>), so these are live aliases, not snapshots:
/// scripts read <em>and</em> write through them and expect the engine to see the write immediately.
/// Bindings here are getter/setter pairs for the same reason.
/// </para>
/// <para>
/// Names are built by <see cref="GameTask.Name"/>, which is <c>TypeName_id</c> — the label in the
/// <c>Task_New</c> call is discarded by the engine and is not what scripts use. See the re-note, §5.1
/// and §8.
/// </para>
/// </remarks>
public sealed class ScriptVarRegistry
{
    private readonly NativeRegistry _symbols;
    private readonly Dictionary<GameTask, List<string>> _byTask = [];

    /// <summary>Initializes a new instance of the <see cref="ScriptVarRegistry"/> class.</summary>
    /// <param name="symbols">The script namespace the bindings are published into.</param>
    /// <exception cref="ArgumentNullException"><paramref name="symbols"/> is <see langword="null"/>.</exception>
    public ScriptVarRegistry(NativeRegistry symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        _symbols = symbols;
    }

    /// <summary>Gets the number of live bindings.</summary>
    public int Count
    {
        get
        {
            int total = 0;
            foreach (List<string> names in _byTask.Values)
            {
                total += names.Count;
            }

            return total;
        }
    }

    /// <summary>Binds an integer field.</summary>
    /// <param name="task">The owning task.</param>
    /// <param name="field">The field name, such as <c>nTick</c>.</param>
    /// <param name="get">Reads the current value.</param>
    /// <param name="set">Stores a new value.</param>
    /// <param name="size">The storage width in bytes: 1, 2 or 4.</param>
    /// <returns>The symbol name that was bound.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="field"/> is empty.</exception>
    public string BindInt(GameTask task, string field, Func<int> get, Action<int> set, int size = sizeof(int)) =>
        Bind(task, field, new QvmIntegerVariable(get, set, size));

    /// <summary>Binds a floating-point field.</summary>
    /// <param name="task">The owning task.</param>
    /// <param name="field">The field name.</param>
    /// <param name="get">Reads the current value.</param>
    /// <param name="set">Stores a new value.</param>
    /// <returns>The symbol name that was bound.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="field"/> is empty.</exception>
    public string BindReal(GameTask task, string field, Func<double> get, Action<double> set) =>
        Bind(task, field, new QvmFloatVariable(get, set));

    /// <summary>Binds a fixed-capacity text field.</summary>
    /// <param name="task">The owning task.</param>
    /// <param name="field">The field name, such as <c>zData</c>.</param>
    /// <param name="get">Reads the current value.</param>
    /// <param name="set">Stores a new value.</param>
    /// <param name="capacity">The buffer size in bytes, including the terminator.</param>
    /// <returns>The symbol name that was bound.</returns>
    /// <remarks>
    /// The original stores these in a fixed char array and its assignment is
    /// <c>strncpy(dst, src, capacity - 1)</c> with an explicit terminator, so a longer value is
    /// truncated rather than rejected.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="field"/> is empty.</exception>
    public string BindText(GameTask task, string field, Func<string> get, Action<string> set, int capacity) =>
        Bind(task, field, new QvmStringVariable(get, set, capacity));

    /// <summary>Binds a boolean field, which the original stores as one byte.</summary>
    /// <param name="task">The owning task.</param>
    /// <param name="field">The field name, such as <c>isOn</c>.</param>
    /// <param name="get">Reads the current value.</param>
    /// <param name="set">Stores a new value.</param>
    /// <returns>The symbol name that was bound.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="field"/> is empty.</exception>
    public string BindBool(GameTask task, string field, Func<bool> get, Action<bool> set)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        return BindInt(task, field, () => get() ? 1 : 0, value => set(value != 0), size: 1);
    }

    /// <summary>Binds a latched boolean together with its <c>Last</c> shadow.</summary>
    /// <param name="task">The owning task.</param>
    /// <param name="field">The field name, such as <c>isPressed</c>.</param>
    /// <param name="flag">The flag to bind.</param>
    /// <param name="shadowField">
    /// The shadow's name. Defaults to <paramref name="field"/> with <c>Last</c> inserted after its
    /// <c>is</c> or <c>n</c> prefix, which covers the regular pairs; irregular ones such as
    /// <c>isTrigger</c>/<c>nTriggerLastTick</c> must be spelled out.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="field"/> is empty.</exception>
    /// <remarks>The shadow is read-only to scripts in practice, but the original binds it writable, so it is bound writable here too.</remarks>
    public void BindFlag(GameTask task, string field, ScriptFlag flag, string? shadowField = null)
    {
        ArgumentNullException.ThrowIfNull(flag);
        ArgumentException.ThrowIfNullOrEmpty(field);

        BindBool(task, field, () => flag.Value, value => flag.Value = value);
        BindBool(task, shadowField ?? ShadowNameOf(field), () => flag.Last, _ => { });
    }

    /// <summary>Binds a field to an arbitrary variable implementation.</summary>
    /// <param name="task">The owning task.</param>
    /// <param name="field">The field name.</param>
    /// <param name="variable">The storage binding.</param>
    /// <returns>The symbol name that was bound.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="field"/> is empty.</exception>
    public string Bind(GameTask task, string field, IQvmVariable variable)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentException.ThrowIfNullOrEmpty(field);

        string symbol = $"{task.Name}.{field}";

        // A second export of the same task would otherwise throw. The original guards against that with
        // a per-task "already exported" byte; this keeps the registry itself idempotent as well.
        _symbols.Unregister(symbol);
        _symbols.RegisterVariable(symbol, variable);

        if (!_byTask.TryGetValue(task, out List<string>? names))
        {
            names = [];
            _byTask[task] = names;
        }

        if (!names.Contains(symbol))
        {
            names.Add(symbol);
        }

        return symbol;
    }

    /// <summary>Exports a task's named variables, unless it has already exported them.</summary>
    /// <param name="task">The task to export.</param>
    /// <returns><see langword="true"/> when the export ran.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The guard reproduces the per-type "already exported" byte every canonical <c>ExportVars</c>
    /// handler checks first (for example <c>0x41E5A0</c>).
    /// </remarks>
    public bool Export(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (task.IsExported)
        {
            return false;
        }

        task.IsExported = true;
        task.InvokeExportVars(this);
        return true;
    }

    /// <summary>Unexports a task's named variables.</summary>
    /// <param name="task">The task to unexport.</param>
    /// <returns><see langword="true"/> when the task had been exported.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    public bool Unexport(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (!task.IsExported)
        {
            return false;
        }

        task.InvokeUnexportVars(this);
        UnbindAll(task);
        task.IsExported = false;
        return true;
    }

    /// <summary>Removes every binding a task owns.</summary>
    /// <param name="task">The task.</param>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    public void UnbindAll(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (!_byTask.Remove(task, out List<string>? names))
        {
            return;
        }

        foreach (string symbol in names)
        {
            _symbols.Unregister(symbol);
        }
    }

    /// <summary>Gets the symbol names a task has bound.</summary>
    /// <param name="task">The task.</param>
    /// <returns>The bound names, empty when the task has exported nothing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<string> GetBoundNames(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return _byTask.TryGetValue(task, out List<string>? names) ? names : [];
    }

    /// <summary>Reads a bound variable by its full symbol name.</summary>
    /// <param name="symbol">The full name, such as <c>Switch_94.isPressed</c>.</param>
    /// <param name="value">Receives the current value.</param>
    /// <returns><see langword="true"/> when the name is a bound variable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="symbol"/> is <see langword="null"/>.</exception>
    public bool TryRead(string symbol, out QvmValue value)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        if (_symbols.TryResolve(symbol, out QvmSymbol? resolved) && resolved.Kind == QvmSymbolKind.Variable)
        {
            value = resolved.Read();
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Gets the type a bound variable declares.</summary>
    /// <param name="symbol">The full name, such as <c>SlideBar_950.vValue</c>.</param>
    /// <returns>
    /// The declared type, or <see cref="QvmValueKind.None"/> when the name is not a bound variable.
    /// </returns>
    /// <remarks>
    /// An assignment picks its evaluator from this — the original's symbol type tag of 1, 2 or 3. See
    /// the original.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="symbol"/> is <see langword="null"/>.</exception>
    public QvmValueKind GetValueKind(string symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        return _symbols.TryResolve(symbol, out QvmSymbol? resolved) && resolved.Variable is { } variable
            ? variable.ValueType
            : QvmValueKind.None;
    }

    /// <summary>Writes a bound variable by its full symbol name.</summary>
    /// <param name="symbol">The full name.</param>
    /// <param name="value">The value to store.</param>
    /// <returns><see langword="true"/> when the name is a bound variable that accepted the value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="symbol"/> is <see langword="null"/>.</exception>
    public bool TryWrite(string symbol, QvmValue value)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        return _symbols.TryResolve(symbol, out QvmSymbol? resolved)
            && resolved.Variable is { } variable
            && variable.TryWrite(value, out _);
    }

    private static string ShadowNameOf(string field)
    {
        if (field.StartsWith("is", StringComparison.Ordinal))
        {
            return string.Concat("isLast", field.AsSpan(2));
        }

        if (field.StartsWith('n'))
        {
            return string.Concat("nLast", field.AsSpan(1));
        }

        return string.Concat("last", field);
    }
}
