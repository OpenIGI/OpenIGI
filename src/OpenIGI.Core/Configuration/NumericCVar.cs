using System.Globalization;
using System.Numerics;

namespace OpenIGI.Core.Configuration;

/// <summary>
/// A console variable holding a number, optionally bounded.
/// </summary>
/// <typeparam name="T">The numeric type, for example <see cref="int"/>, <see cref="float"/> or <see cref="double"/>.</typeparam>
/// <remarks>
/// One implementation covers every numeric type through generic math, which keeps the parse, format
/// and clamp rules identical for an integer quality level and a floating-point intensity. Text is
/// always read and written with <see cref="CultureInfo.InvariantCulture"/> so a settings file written
/// on a machine whose decimal separator is a comma still loads elsewhere.
/// </remarks>
public sealed class NumericCVar<T> : CVar<T>
    where T : struct, INumber<T>
{
    /// <summary>Initialises a new instance of the <see cref="NumericCVar{T}"/> class.</summary>
    /// <param name="name">The registry key.</param>
    /// <param name="defaultValue">The value the variable holds until something changes it.</param>
    /// <param name="minimum">The lowest accepted value, or <see langword="null"/> when unbounded below.</param>
    /// <param name="maximum">The highest accepted value, or <see langword="null"/> when unbounded above.</param>
    /// <param name="flags">The variable's behavioural attributes.</param>
    /// <param name="help">The one-line description shown by the console and the options menu.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty, or <paramref name="minimum"/> exceeds <paramref name="maximum"/>.</exception>
    public NumericCVar(
        string name,
        T defaultValue,
        T? minimum = null,
        T? maximum = null,
        CVarOptions flags = CVarOptions.None,
        string help = "")
        : base(name, Constrain(defaultValue, minimum, maximum), flags, help)
    {
        if (minimum is { } low && maximum is { } high && low > high)
        {
            throw new ArgumentException($"Console variable '{name}' declares a minimum above its maximum.", nameof(minimum));
        }

        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>Gets the lowest accepted value, or <see langword="null"/> when unbounded below.</summary>
    public T? Minimum { get; }

    /// <summary>Gets the highest accepted value, or <see langword="null"/> when unbounded above.</summary>
    public T? Maximum { get; }

    /// <inheritdoc/>
    public override string RangeText => (Minimum, Maximum) switch
    {
        (null, null) => string.Empty,
        ({ } low, null) => $"{Render(low)} or more",
        (null, { } high) => $"{Render(high)} or less",
        ({ } low, { } high) => $"{Render(low)} .. {Render(high)}",
    };

    /// <inheritdoc/>
    protected override string FriendlyTypeName => T.IsInteger(T.Zero) ? "integer" : "number";

    /// <inheritdoc/>
    protected override bool TryParse(string text, out T value) =>
        T.TryParse(text.Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

    /// <inheritdoc/>
    protected override string Format(T value) => Render(value);

    /// <inheritdoc/>
    protected override T Coerce(T value) => Constrain(value, Minimum, Maximum);

    private static T Constrain(T value, T? minimum, T? maximum)
    {
        // A NaN slips through both comparisons, so it is mapped onto the lower bound explicitly
        // rather than being allowed to poison a render setting.
        if (T.IsNaN(value))
        {
            return minimum ?? T.Zero;
        }

        if (minimum is { } low && value < low)
        {
            return low;
        }

        return maximum is { } high && value > high ? high : value;
    }

    private static string Render(T value) => value.ToString(null, CultureInfo.InvariantCulture);
}
