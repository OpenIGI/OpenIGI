using OpenIGI.Core.Numerics;

namespace OpenIGI.Scripting.Runtime;

/// <summary>
/// An integer variable of a given storage width, bound to a getter and a setter.
/// </summary>
/// <remarks>
/// The width reproduces the original's three cases — one byte, two bytes, four bytes — including the
/// quirk that only an <em>integer</em> assignment narrows to it. Assigning a float writes a full
/// 32-bit word whatever the declared width says, because that path in the original skips the width
/// switch entirely.
/// </remarks>
public sealed class QvmIntegerVariable : IQvmVariable
{
    private readonly Func<int> _get;
    private readonly Action<int> _set;
    private readonly int _size;

    /// <summary>Initializes a new instance of the <see cref="QvmIntegerVariable"/> class.</summary>
    /// <param name="get">Reads the current value.</param>
    /// <param name="set">Stores a new value.</param>
    /// <param name="size">The storage width in bytes: 1, 2 or 4.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not 1, 2 or 4.</exception>
    public QvmIntegerVariable(Func<int> get, Action<int> set, int size = sizeof(int))
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        if (size is not (1 or 2 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "An integer variable is 1, 2 or 4 bytes wide.");
        }

        _get = get;
        _set = set;
        _size = size;
    }

    /// <inheritdoc/>
    public QvmValueKind ValueType => QvmValueKind.Integer;

    /// <summary>Gets the storage width in bytes.</summary>
    public int Size => _size;

    /// <inheritdoc/>
    public QvmValue Read() => QvmValue.FromInt32(_get());

    /// <inheritdoc/>
    public bool TryWrite(QvmValue value, out QvmValue stored)
    {
        int narrowed;
        switch (value.Kind)
        {
            case QvmValueKind.Integer:
                narrowed = _size switch
                {
                    1 => (sbyte)value.Integer,
                    2 => (short)value.Integer,
                    _ => value.Integer,
                };
                break;

            case QvmValueKind.Float:
                // The original's float-to-integer store writes a whole word and keeps the low half of
                // the x87 truncation, regardless of the declared width.
                narrowed = (int)IgiMath.Trunc(value.Number);
                break;

            default:
                stored = default;
                return false;
        }

        _set(narrowed);
        stored = QvmValue.FromInt32(narrowed);
        return true;
    }
}
