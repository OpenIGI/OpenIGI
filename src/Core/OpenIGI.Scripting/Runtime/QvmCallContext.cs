namespace OpenIGI.Scripting.Runtime;

/// <summary>
/// One call in progress: what a native function reads its arguments through.
/// </summary>
/// <remarks>
/// <para>
/// A QVM call passes code offsets, not values. Each <c>Get</c> here runs the argument's expression
/// block on the interpreter that issued the call, then restores the stack — the original's deferred
/// argument evaluation, which is why a native may skip an argument or evaluate it repeatedly.
/// </para>
/// <para>
/// An argument that fails to evaluate — because its expression left nothing on the stack, or left
/// something of the wrong type — reads as the default value, which is what the original's argument
/// helpers return in that case. Use the <c>TryGet</c> overloads to tell that apart from a real zero.
/// </para>
/// </remarks>
public sealed class QvmCallContext
{
    private readonly QvmInterpreter _interpreter;
    private readonly int _argumentTableOffset;

    internal QvmCallContext(QvmInterpreter interpreter, QvmSymbol symbol, int argumentTableOffset, int argumentCount)
    {
        _interpreter = interpreter;
        _argumentTableOffset = argumentTableOffset;
        ArgumentCount = argumentCount;
        Symbol = symbol;
    }

    /// <summary>Gets the symbol being called.</summary>
    public QvmSymbol Symbol { get; }

    /// <summary>Gets the object bound to the symbol through <see cref="QvmSymbol.Context"/>.</summary>
    public object? Context => Symbol.Context;

    /// <summary>Gets the number of arguments the call site passes.</summary>
    /// <remarks>This is the count encoded at the call site, which need not match <see cref="QvmSymbol.ArgumentCount"/>.</remarks>
    public int ArgumentCount { get; }

    /// <summary>Gets the interpreter running the call, for natives that need to inspect its state.</summary>
    public QvmInterpreter Interpreter => _interpreter;

    /// <summary>Evaluates an argument as an integer.</summary>
    /// <param name="index">The zero-based argument position.</param>
    /// <returns>The integer, or zero when the argument does not evaluate to a number.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a passed argument.</exception>
    public int GetInt32(int index) => TryGetInt32(index, out int value) ? value : 0;

    /// <summary>Evaluates an argument as a floating-point number.</summary>
    /// <param name="index">The zero-based argument position.</param>
    /// <returns>The number, or zero when the argument does not evaluate to a number.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a passed argument.</exception>
    public float GetSingle(int index) => TryGetSingle(index, out float value) ? value : 0f;

    /// <summary>Evaluates an argument as a string.</summary>
    /// <param name="index">The zero-based argument position.</param>
    /// <returns>The text, or the empty string when the argument does not evaluate to one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a passed argument.</exception>
    public string GetString(int index) => TryGetString(index, out string value) ? value : string.Empty;

    /// <summary>Evaluates an argument and retains the value's original VM representation.</summary>
    /// <remarks>
    /// This is intended for data-definition natives that need to preserve an indexed tail whose
    /// meaning is owned by a native parser.  Unlike <see cref="GetInt32"/>,
    /// <see cref="GetSingle(int)"/>, and <see cref="GetString(int)"/>, it does not coerce the value.
    /// </remarks>
    public QvmValue GetValue(int index) => TryGetValue(index, out QvmValue value) ? value : QvmValue.Zero;

    /// <summary>Evaluates an argument and reports whether it produced a value.</summary>
    public bool TryGetValue(int index, out QvmValue value)
    {
        return _interpreter.TryEvaluateArgument(Offset(index), QvmValueKind.None, out value);
    }

    /// <summary>Evaluates an argument as an integer, reporting whether it produced one.</summary>
    /// <param name="index">The zero-based argument position.</param>
    /// <param name="value">Receives the integer.</param>
    /// <returns><see langword="false"/> when the argument's expression yields no number.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a passed argument.</exception>
    public bool TryGetInt32(int index, out int value)
    {
        bool evaluated = _interpreter.TryEvaluateArgument(Offset(index), QvmValueKind.Integer, out QvmValue result);
        value = evaluated ? result.Integer : 0;
        return evaluated;
    }

    /// <summary>Evaluates an argument as a floating-point number, reporting whether it produced one.</summary>
    /// <param name="index">The zero-based argument position.</param>
    /// <param name="value">Receives the number.</param>
    /// <returns><see langword="false"/> when the argument's expression yields no number.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a passed argument.</exception>
    public bool TryGetSingle(int index, out float value)
    {
        bool evaluated = _interpreter.TryEvaluateArgument(Offset(index), QvmValueKind.Float, out QvmValue result);
        value = evaluated ? (float)result.Number : 0f;
        return evaluated;
    }

    /// <summary>Evaluates an argument as a string, reporting whether it produced one.</summary>
    /// <param name="index">The zero-based argument position.</param>
    /// <param name="value">Receives the text.</param>
    /// <returns><see langword="false"/> when the argument's expression yields no string.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a passed argument.</exception>
    public bool TryGetString(int index, out string value)
    {
        bool evaluated = _interpreter.TryEvaluateArgument(Offset(index), QvmValueKind.String, out QvmValue result);
        value = evaluated ? result.Text : string.Empty;
        return evaluated;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Symbol.Name}({ArgumentCount} args)";

    private int Offset(int index)
    {
        // The original indexes its argument-offset array unchecked and would read past the call site.
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ArgumentCount);

        // Read straight from the code section, as the original does: a call site can carry thousands
        // of arguments (Task_DeclareParameters reaches 2605), and materialising that array per call
        // would allocate on the hottest path in level loading.
        return _interpreter.ReadArgumentOffset(_argumentTableOffset, index);
    }
}
