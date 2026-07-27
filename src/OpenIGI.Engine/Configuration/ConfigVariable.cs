using System.Globalization;

namespace OpenIGI.Engine.Configuration;

/// <summary>
/// A named variable bound to live storage through a getter and an optional setter.
/// </summary>
/// <remarks>
/// <para>
/// This models <c>Var_Register</c> (<c>0x4BAAC0</c>) and its typed wrappers, which register a name
/// against a pointer plus an accessor so that reads and writes go straight to the owning subsystem's
/// field. Delegates give the port the same liveness without exposing raw memory.
/// </para>
/// <para>
/// The same registry later carries the per-task variables the mission scripts read and write —
/// <c>&lt;task&gt;.isOn</c>, <c>&lt;task&gt;.nTick</c> and friends, whose names the original composes at
/// runtime from the task's name.
/// </para>
/// </remarks>
public sealed class ConfigVariable
{
    private readonly Func<string> _get;
    private readonly Action<string>? _set;

    private ConfigVariable(string name, ConfigVariableKind kind, Func<string> get, Action<string>? set)
    {
        Name = name;
        Kind = kind;
        _get = get;
        _set = set;
    }

    /// <summary>Gets the variable name.</summary>
    public string Name { get; }

    /// <summary>Gets the value type this variable exposes.</summary>
    public ConfigVariableKind Kind { get; }

    /// <summary>Gets a value indicating whether the variable can be written.</summary>
    public bool IsReadOnly => _set is null;

    /// <summary>Creates a boolean variable.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="get">Reads the current value.</param>
    /// <param name="set">Writes a new value, or <see langword="null"/> for a read-only variable.</param>
    /// <returns>The variable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="get"/> is <see langword="null"/>.</exception>
    public static ConfigVariable Boolean(string name, Func<bool> get, Action<bool>? set = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        return Create(
            name,
            ConfigVariableKind.Boolean,
            () => get() ? "1" : "0",
            set is null ? null : (Action<string>)(text => set(ParseBoolean(name, text))));
    }

    /// <summary>Creates an integer variable.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="get">Reads the current value.</param>
    /// <param name="set">Writes a new value, or <see langword="null"/> for a read-only variable.</param>
    /// <returns>The variable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="get"/> is <see langword="null"/>.</exception>
    public static ConfigVariable Integer(string name, Func<int> get, Action<int>? set = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        return Create(
            name,
            ConfigVariableKind.Integer,
            () => get().ToString(CultureInfo.InvariantCulture),
            set is null ? null : (Action<string>)(text => set(ParseInteger(name, text))));
    }

    /// <summary>Creates a real-valued variable.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="get">Reads the current value.</param>
    /// <param name="set">Writes a new value, or <see langword="null"/> for a read-only variable.</param>
    /// <returns>The variable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="get"/> is <see langword="null"/>.</exception>
    public static ConfigVariable Real(string name, Func<double> get, Action<double>? set = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        return Create(
            name,
            ConfigVariableKind.Real,
            () => get().ToString("R", CultureInfo.InvariantCulture),
            set is null ? null : (Action<string>)(text => set(ParseReal(name, text))));
    }

    /// <summary>Creates a string variable.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="get">Reads the current value.</param>
    /// <param name="set">Writes a new value, or <see langword="null"/> for a read-only variable.</param>
    /// <returns>The variable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="get"/> is <see langword="null"/>.</exception>
    public static ConfigVariable Text(string name, Func<string> get, Action<string>? set = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        return Create(name, ConfigVariableKind.String, get, set);
    }

    /// <summary>Reads the current value as text.</summary>
    /// <returns>The value.</returns>
    public string GetString() => _get();

    /// <summary>Writes a new value from text.</summary>
    /// <param name="value">The value to parse and store.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The variable is read-only.</exception>
    /// <exception cref="FormatException"><paramref name="value"/> does not parse as this variable's type.</exception>
    public void SetString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (_set is null)
        {
            throw new InvalidOperationException($"The variable '{Name}' is read-only.");
        }

        _set(value);
    }

    /// <summary>Reads the current value as a boolean.</summary>
    /// <returns>The value.</returns>
    /// <exception cref="FormatException">The stored text does not parse as a boolean.</exception>
    public bool GetBoolean() => ParseBoolean(Name, _get());

    /// <summary>Reads the current value as an integer.</summary>
    /// <returns>The value.</returns>
    /// <exception cref="FormatException">The stored text does not parse as an integer.</exception>
    public int GetInteger() => ParseInteger(Name, _get());

    /// <summary>Reads the current value as a real.</summary>
    /// <returns>The value.</returns>
    /// <exception cref="FormatException">The stored text does not parse as a real.</exception>
    public double GetReal() => ParseReal(Name, _get());

    /// <inheritdoc/>
    public override string ToString() => $"{Name} [{Kind}] = {_get()}";

    private static ConfigVariable Create(string name, ConfigVariableKind kind, Func<string> get, Action<string>? set)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new ConfigVariable(name, kind, get, set);
    }

    private static bool ParseBoolean(string name, string text)
    {
        // The original stores booleans as integers, and its scripts also use the TRUE/FALSE constants.
        if (bool.TryParse(text, out bool parsed))
        {
            return parsed;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
        {
            return number != 0;
        }

        throw new FormatException($"The value '{text}' is not valid for the boolean variable '{name}'.");
    }

    private static int ParseInteger(string name, string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new FormatException($"The value '{text}' is not valid for the integer variable '{name}'.");

    private static double ParseReal(string name, string text) =>
        double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double value)
            ? value
            : throw new FormatException($"The value '{text}' is not valid for the real variable '{name}'.");
}
