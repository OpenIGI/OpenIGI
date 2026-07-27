namespace OpenIGI.Scripting.Runtime;

/// <summary>
/// A fixed-capacity string variable, bound to a getter and a setter.
/// </summary>
/// <remarks>
/// The capacity is the size of the original's character buffer including its terminator, so an
/// assignment keeps at most <c>capacity - 1</c> characters. Longer text is truncated, never rejected.
/// </remarks>
public sealed class QvmStringVariable : IQvmVariable
{
    private readonly Func<string> _get;
    private readonly Action<string> _set;

    /// <summary>Initializes a new instance of the <see cref="QvmStringVariable"/> class.</summary>
    /// <param name="get">Reads the current value.</param>
    /// <param name="set">Stores a new value, already truncated to fit.</param>
    /// <param name="capacity">The buffer size in bytes, terminator included.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is less than one.</exception>
    public QvmStringVariable(Func<string> get, Action<string> set, int capacity)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _get = get;
        _set = set;
        Capacity = capacity;
    }

    /// <inheritdoc/>
    public QvmValueKind ValueType => QvmValueKind.String;

    /// <summary>Gets the buffer size in bytes, terminator included.</summary>
    public int Capacity { get; }

    /// <inheritdoc/>
    public QvmValue Read() => QvmValue.FromString(_get());

    /// <inheritdoc/>
    public bool TryWrite(QvmValue value, out QvmValue stored)
    {
        if (value.Kind is not (QvmValueKind.String or QvmValueKind.Buffer))
        {
            stored = default;
            return false;
        }

        string text = value.Text;
        if (text.Length > Capacity - 1)
        {
            text = text[..(Capacity - 1)];
        }

        _set(text);
        stored = QvmValue.FromString(text);
        return true;
    }
}
