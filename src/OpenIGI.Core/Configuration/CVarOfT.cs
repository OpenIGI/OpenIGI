namespace OpenIGI.Core.Configuration;

/// <summary>
/// A strongly-typed console variable: one setting, owned by the code that reads it.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
/// <remarks>
/// <para>
/// A variable is declared once, as a <see langword="static" /> <see langword="readonly" /> field next
/// to the subsystem that consumes it, and read through <see cref="Value"/> with no lookup and no
/// conversion. The console, the settings file and the options menu reach the same instance through
/// <see cref="ICVar"/>, which is what keeps the three of them from ever disagreeing.
/// </para>
/// <para>
/// Derived types supply only the text conversion and the range rule; assignment, change notification
/// and default tracking live here so every variable behaves identically.
/// </para>
/// </remarks>
public abstract class CVar<T> : ICVar
{
    private T _value;

    /// <summary>Initialises a new instance of the <see cref="CVar{T}"/> class.</summary>
    /// <param name="name">The registry key.</param>
    /// <param name="defaultValue">The value the variable holds until something changes it.</param>
    /// <param name="flags">The variable's behavioural attributes.</param>
    /// <param name="help">The one-line description shown by the console and the options menu.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or white space.</exception>
    protected CVar(string name, T defaultValue, CVarOptions flags, string help)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Help = help ?? string.Empty;
        Flags = flags;
        DefaultValue = defaultValue;
        _value = defaultValue;
    }

    /// <inheritdoc/>
    public event EventHandler<CVarChangedEventArgs>? Changed;

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string Help { get; }

    /// <inheritdoc/>
    public CVarOptions Flags { get; }

    /// <summary>Gets the value the variable holds until something changes it.</summary>
    public T DefaultValue { get; }

    /// <summary>Gets or sets the current value.</summary>
    /// <remarks>
    /// The setter applies the derived type's range rule, so a value assigned in code is subject to the
    /// same clamping as one typed into the console. Assigning an equal value raises no event.
    /// </remarks>
    public T Value
    {
        get => _value;
        set => Assign(value);
    }

    /// <inheritdoc/>
    public Type ValueType => typeof(T);

    /// <inheritdoc/>
    public string ValueText => Format(_value);

    /// <inheritdoc/>
    public string DefaultText => Format(DefaultValue);

    /// <inheritdoc/>
    public virtual string RangeText => string.Empty;

    /// <inheritdoc/>
    /// <remarks>
    /// Empty by default: a variable can only enumerate its values when they form a finite set, which is
    /// true of a switch and an enumeration and false of a number or free text.
    /// </remarks>
    public virtual IReadOnlyList<string> ValueChoices => [];

    /// <inheritdoc/>
    public bool IsDefault => EqualityComparer<T>.Default.Equals(_value, DefaultValue);

    /// <inheritdoc/>
    public bool TrySetText(string text, out string? reason)
    {
        if (!TryParse(text ?? string.Empty, out T parsed))
        {
            reason = RangeText.Length == 0
                ? $"'{text}' is not a valid {FriendlyTypeName} value"
                : $"'{text}' is not a valid {FriendlyTypeName} value; expected {RangeText}";
            return false;
        }

        reason = null;
        Assign(parsed);
        return true;
    }

    /// <inheritdoc/>
    public void Reset() => Assign(DefaultValue);

    /// <summary>Returns the variable's name and current value, as the console prints it.</summary>
    /// <returns>A string of the form <c>name = value</c>.</returns>
    public override string ToString() => $"{Name} = {ValueText}";

    /// <summary>Gets the type name used in parse-failure messages, for example <c>"integer"</c>.</summary>
    protected virtual string FriendlyTypeName => typeof(T).Name;

    /// <summary>Converts <paramref name="text"/> into a value.</summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="value">Receives the parsed value.</param>
    /// <returns><see langword="true"/> when <paramref name="text"/> was understood.</returns>
    protected abstract bool TryParse(string text, out T value);

    /// <summary>Renders <paramref name="value"/> as the text the settings file stores.</summary>
    /// <param name="value">The value to render.</param>
    /// <returns>The rendered text.</returns>
    /// <remarks>Implementations must be culture-invariant so a settings file is portable between machines.</remarks>
    protected abstract string Format(T value);

    /// <summary>Brings <paramref name="value"/> into the variable's accepted range.</summary>
    /// <param name="value">The value to constrain.</param>
    /// <returns>The constrained value. The default implementation returns it unchanged.</returns>
    protected virtual T Coerce(T value) => value;

    private void Assign(T value)
    {
        T coerced = Coerce(value);
        if (EqualityComparer<T>.Default.Equals(coerced, _value))
        {
            return;
        }

        string previous = Format(_value);
        _value = coerced;
        Changed?.Invoke(this, new CVarChangedEventArgs(this, previous, Format(coerced)));
    }
}
