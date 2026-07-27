using System.Globalization;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Scripting.Runtime;

/// <summary>
/// One value on the VM stack, or one value handed to and from a native function.
/// </summary>
/// <remarks>
/// <para>
/// The original stores a value's bytes on a word stack and its tag on a parallel type stack. This
/// struct carries both together. Only the member matching <see cref="Kind"/> is meaningful.
/// </para>
/// <para>
/// Floating-point values keep a <see cref="double"/> because a script variable is stored as one,
/// while everything the VM pushes has already been narrowed to 32 bits — the same asymmetry the
/// original has between its variable storage and its stack.
/// </para>
/// </remarks>
public readonly struct QvmValue : IEquatable<QvmValue>
{
    private readonly int _integer;
    private readonly double _number;
    private readonly string? _text;
    private readonly QvmSymbol? _symbol;

    private QvmValue(QvmValueKind kind, int integer, double number, string? text, QvmSymbol? symbol)
    {
        Kind = kind;
        _integer = integer;
        _number = number;
        _text = text;
        _symbol = symbol;
    }

    /// <summary>Gets the integer zero: what a native returns when it produces no value.</summary>
    public static QvmValue Zero => FromInt32(0);

    /// <summary>Gets the type tag.</summary>
    public QvmValueKind Kind { get; }

    /// <summary>Gets the integer, for <see cref="QvmValueKind.Integer"/> and <see cref="QvmValueKind.Address"/>.</summary>
    public int Integer => _integer;

    /// <summary>Gets the number, for <see cref="QvmValueKind.Float"/>.</summary>
    public double Number => _number;

    /// <summary>Gets the text, for <see cref="QvmValueKind.String"/>, <see cref="QvmValueKind.Identifier"/> and <see cref="QvmValueKind.Buffer"/>.</summary>
    public string Text => _text ?? string.Empty;

    /// <summary>Gets the symbol, for <see cref="QvmValueKind.Symbol"/>.</summary>
    public QvmSymbol? Symbol => _symbol;

    /// <summary>Gets a value indicating whether this is a number the arithmetic instructions accept.</summary>
    public bool IsNumber => Kind is QvmValueKind.Integer or QvmValueKind.Float;

    /// <summary>Creates an integer value.</summary>
    /// <param name="value">The integer.</param>
    /// <returns>The value.</returns>
    public static QvmValue FromInt32(int value) => new(QvmValueKind.Integer, value, value, null, null);

    /// <summary>Creates a floating-point value as the VM pushes one, narrowed to 32 bits.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The value.</returns>
    public static QvmValue FromSingle(float value) => new(QvmValueKind.Float, 0, value, null, null);

    /// <summary>Creates a floating-point value at full width, as a variable read produces one.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The value.</returns>
    public static QvmValue FromDouble(double value) => new(QvmValueKind.Float, 0, value, null, null);

    /// <summary>Creates a string value.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The value.</returns>
    public static QvmValue FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new QvmValue(QvmValueKind.String, 0, 0, value, null);
    }

    /// <summary>Creates an unresolved identifier value.</summary>
    /// <param name="name">The identifier.</param>
    /// <returns>The value.</returns>
    public static QvmValue FromIdentifier(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new QvmValue(QvmValueKind.Identifier, 0, 0, name, null);
    }

    /// <summary>Creates an address value.</summary>
    /// <param name="value">The address immediate.</param>
    /// <returns>The value.</returns>
    public static QvmValue FromAddress(int value) => new(QvmValueKind.Address, value, 0, null, null);

    /// <summary>Creates a symbol value.</summary>
    /// <param name="symbol">The resolved symbol.</param>
    /// <returns>The value.</returns>
    public static QvmValue FromSymbol(QvmSymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        return new QvmValue(QvmValueKind.Symbol, 0, 0, null, symbol);
    }

    /// <summary>Compares two values for equality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when both have the same tag and payload.</returns>
    public static bool operator ==(QvmValue left, QvmValue right) => left.Equals(right);

    /// <summary>Compares two values for inequality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values differ.</returns>
    public static bool operator !=(QvmValue left, QvmValue right) => !left.Equals(right);

    /// <summary>Converts the value to the integer slot of the original's return union.</summary>
    /// <returns>The integer form: floats truncate toward zero, anything else yields zero.</returns>
    public int ToInt32() => Kind switch
    {
        QvmValueKind.Integer or QvmValueKind.Address => _integer,
        QvmValueKind.Float => IgiMath.TruncToInt32(_number),
        _ => 0,
    };

    /// <summary>Converts the value to the floating-point slot of the original's return union.</summary>
    /// <returns>The 32-bit float form, or zero for values that are not numbers.</returns>
    public float ToSingle() => Kind switch
    {
        QvmValueKind.Integer or QvmValueKind.Address => _integer,
        QvmValueKind.Float => (float)_number,
        _ => 0f,
    };

    /// <summary>Converts the value to the string slot of the original's return union.</summary>
    /// <returns>The text, or the empty string for values that are not text.</returns>
    /// <remarks>
    /// The original fills that slot with a pointer to its shared empty string whenever a native has
    /// no string to return, so the empty string is the faithful answer rather than a placeholder.
    /// </remarks>
    public string ToText() => Kind switch
    {
        QvmValueKind.String or QvmValueKind.Identifier or QvmValueKind.Buffer => Text,
        _ => string.Empty,
    };

    /// <inheritdoc/>
    public bool Equals(QvmValue other) =>
        Kind == other.Kind
        && _integer == other._integer
        && _number.Equals(other._number)
        && string.Equals(_text, other._text, StringComparison.Ordinal)
        && ReferenceEquals(_symbol, other._symbol);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is QvmValue other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, _integer, _number, _text, _symbol);

    /// <inheritdoc/>
    public override string ToString() => Kind switch
    {
        QvmValueKind.Integer => _integer.ToString(CultureInfo.InvariantCulture),
        QvmValueKind.Address => string.Create(CultureInfo.InvariantCulture, $"&{_integer}"),
        QvmValueKind.Float => _number.ToString("R", CultureInfo.InvariantCulture),
        QvmValueKind.String or QvmValueKind.Buffer => $"\"{Text}\"",
        QvmValueKind.Identifier => Text,
        QvmValueKind.Symbol => _symbol?.Name ?? "<symbol>",
        _ => "<none>",
    };
}
