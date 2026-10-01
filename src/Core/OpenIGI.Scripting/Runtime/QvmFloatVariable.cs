namespace OpenIGI.Scripting.Runtime;

/// <summary>
/// A floating-point variable, bound to a getter and a setter.
/// </summary>
/// <remarks>
/// Storage is 64-bit because the original's assignment handler writes a <c>double</c> through the
/// variable pointer, while everything it pushes back onto the stack is narrowed to 32 bits first.
/// </remarks>
public sealed class QvmFloatVariable : IQvmVariable
{
    private readonly Func<double> _get;
    private readonly Action<double> _set;

    /// <summary>Initializes a new instance of the <see cref="QvmFloatVariable"/> class.</summary>
    /// <param name="get">Reads the current value.</param>
    /// <param name="set">Stores a new value.</param>
    public QvmFloatVariable(Func<double> get, Action<double> set)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        _get = get;
        _set = set;
    }

    /// <inheritdoc/>
    public QvmValueKind ValueType => QvmValueKind.Float;

    /// <inheritdoc/>
    public QvmValue Read() => QvmValue.FromDouble(_get());

    /// <inheritdoc/>
    public bool TryWrite(QvmValue value, out QvmValue stored)
    {
        switch (value.Kind)
        {
            case QvmValueKind.Integer:
                _set(value.Integer);
                stored = QvmValue.FromSingle(value.Integer);
                return true;

            case QvmValueKind.Float:
                _set(value.Number);
                stored = QvmValue.FromSingle((float)value.Number);
                return true;

            default:
                stored = default;
                return false;
        }
    }
}
