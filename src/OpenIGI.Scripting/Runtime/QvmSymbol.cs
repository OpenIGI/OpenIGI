namespace OpenIGI.Scripting.Runtime;

/// <summary>
/// A named entry in the global script namespace: a native function, a constant or a variable.
/// </summary>
/// <remarks>
/// Mirrors the original's 28-byte symbol descriptor. The two fields that matter most are the kind,
/// which decides whether a call or an assignment may target the symbol, and — for a function — the
/// declared <see cref="ValueType"/>, which selects <em>which</em> slot of the native's result the
/// caller pushes. A native registered without a type returns an integer, whatever it computed.
/// </remarks>
public sealed class QvmSymbol
{
    private readonly QvmNativeFunction? _function;
    private readonly IQvmVariable? _variable;
    private readonly int _constant;

    private QvmSymbol(
        string name,
        QvmSymbolKind kind,
        QvmValueKind valueType,
        int argumentCount,
        QvmNativeFunction? function,
        IQvmVariable? variable,
        int constant,
        object? context)
    {
        Name = name;
        Kind = kind;
        ValueType = valueType;
        ArgumentCount = argumentCount;
        _function = function;
        _variable = variable;
        _constant = constant;
        Context = context;
    }

    /// <summary>Gets the name scripts refer to the symbol by.</summary>
    public string Name { get; }

    /// <summary>Gets what the symbol is.</summary>
    public QvmSymbolKind Kind { get; }

    /// <summary>Gets the declared type: what a variable stores, or which result slot a call reads.</summary>
    public QvmValueKind ValueType { get; }

    /// <summary>Gets the declared argument count of a function; zero for anything else.</summary>
    /// <remarks>
    /// Declarative only. The original passes the call site's own count to the native and lets it
    /// decide what to evaluate, so a mismatch is not an error.
    /// </remarks>
    public int ArgumentCount { get; }

    /// <summary>Gets or sets the object bound to this symbol, which its native receives on every call.</summary>
    /// <remarks>
    /// This is what <c>Script_SetSymbolContext</c> rebinds: level loading points <c>Task_New</c> at
    /// the level's own object factory before running its scripts.
    /// </remarks>
    public object? Context { get; set; }

    /// <summary>Gets the variable this symbol binds, or <see langword="null"/> when it is not a variable.</summary>
    public IQvmVariable? Variable => _variable;

    /// <summary>Gets a value indicating whether the symbol yields a value when a script reads it.</summary>
    public bool IsReadable => Kind is QvmSymbolKind.Variable or QvmSymbolKind.Constant;

    /// <summary>Creates a native function symbol.</summary>
    /// <param name="name">The script-visible name.</param>
    /// <param name="function">The implementation.</param>
    /// <param name="argumentCount">The declared argument count.</param>
    /// <param name="returnType">Which slot of the result a call pushes. <see cref="QvmValueKind.None"/> behaves as <see cref="QvmValueKind.Integer"/>.</param>
    /// <param name="context">The object the native receives, later rebindable through <see cref="Context"/>.</param>
    /// <returns>The symbol.</returns>
    public static QvmSymbol CreateFunction(
        string name,
        QvmNativeFunction function,
        int argumentCount = 0,
        QvmValueKind returnType = QvmValueKind.None,
        object? context = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(function);
        ArgumentOutOfRangeException.ThrowIfNegative(argumentCount);
        return new QvmSymbol(name, QvmSymbolKind.Function, returnType, argumentCount, function, null, 0, context);
    }

    /// <summary>Creates an integer constant symbol.</summary>
    /// <param name="name">The script-visible name.</param>
    /// <param name="value">The constant value.</param>
    /// <returns>The symbol.</returns>
    public static QvmSymbol CreateConstant(string name, int value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new QvmSymbol(name, QvmSymbolKind.Constant, QvmValueKind.Integer, 0, null, null, value, null);
    }

    /// <summary>Creates a variable symbol.</summary>
    /// <param name="name">The script-visible name.</param>
    /// <param name="variable">The storage binding.</param>
    /// <returns>The symbol.</returns>
    public static QvmSymbol CreateVariable(string name, IQvmVariable variable)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(variable);
        return new QvmSymbol(name, QvmSymbolKind.Variable, variable.ValueType, 0, null, variable, 0, null);
    }

    /// <summary>Reads the symbol's value.</summary>
    /// <returns>The constant, or the variable's current value.</returns>
    /// <exception cref="InvalidOperationException">The symbol is a function, which has no readable value.</exception>
    public QvmValue Read() => Kind switch
    {
        QvmSymbolKind.Constant => QvmValue.FromInt32(_constant),
        QvmSymbolKind.Variable => _variable!.Read(),
        _ => throw new InvalidOperationException($"The symbol '{Name}' is a function and has no value to read."),
    };

    /// <summary>Invokes the symbol's native function.</summary>
    /// <param name="call">The call in progress, from which the native pulls its arguments.</param>
    /// <returns>Whatever the native produced; the caller narrows it to <see cref="ValueType"/>.</returns>
    /// <exception cref="InvalidOperationException">The symbol is not a function.</exception>
    public QvmValue Invoke(QvmCallContext call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (_function is null)
        {
            throw new InvalidOperationException($"The symbol '{Name}' is not a function.");
        }

        return _function(call);
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Kind} {Name}";
}
