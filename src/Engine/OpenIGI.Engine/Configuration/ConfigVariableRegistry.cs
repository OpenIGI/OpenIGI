using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Engine.Configuration;

/// <summary>
/// The string-keyed variable registry: the original's console-variable table.
/// </summary>
/// <remarks>
/// <para>
/// The original registers variables into a single hash table (<c>0xA43E88</c>) through
/// <c>Var_Register</c>, and <c>Hash_Insert</c> refuses a duplicate with
/// <c>"Symbol \"%s\" already registered in hash table"</c>. Duplicate detection is not defensive
/// programming here: names are composed at runtime from task names, so a collision means two tasks
/// resolved to the same name and one of them would silently drive the other's state.
/// </para>
/// <para>
/// Lookups are case-insensitive, matching the tolerance the original's scripts rely on.
/// </para>
/// </remarks>
public sealed class ConfigVariableRegistry
{
    private const string LogCategory = "Config";

    private readonly Dictionary<string, ConfigVariable> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly IOpenIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="ConfigVariableRegistry"/> class.</summary>
    /// <param name="logger">The logger for registration diagnostics; defaults to a sink that discards them.</param>
    public ConfigVariableRegistry(IOpenIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the number of registered variables.</summary>
    public int Count => _variables.Count;

    /// <summary>Gets every registered variable.</summary>
    public IReadOnlyCollection<ConfigVariable> Variables => _variables.Values;

    /// <summary>Registers a variable.</summary>
    /// <param name="variable">The variable to register.</param>
    /// <returns>The registered variable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="variable"/> is <see langword="null"/>.</exception>
    /// <exception cref="EngineFatalException">A variable of the same name is already registered.</exception>
    public ConfigVariable Register(ConfigVariable variable)
    {
        ArgumentNullException.ThrowIfNull(variable);

        if (!_variables.TryAdd(variable.Name, variable))
        {
            throw new EngineFatalException($"Symbol \"{variable.Name}\" already registered in hash table");
        }

        _logger.Trace(LogCategory, $"Registered variable '{variable.Name}'.");
        return variable;
    }

    /// <summary>Registers a boolean variable.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="get">Reads the current value.</param>
    /// <param name="set">Writes a new value, or <see langword="null"/> for a read-only variable.</param>
    /// <returns>The registered variable.</returns>
    /// <exception cref="EngineFatalException">A variable of the same name is already registered.</exception>
    public ConfigVariable RegisterBoolean(string name, Func<bool> get, Action<bool>? set = null) =>
        Register(ConfigVariable.Boolean(name, get, set));

    /// <summary>Registers an integer variable.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="get">Reads the current value.</param>
    /// <param name="set">Writes a new value, or <see langword="null"/> for a read-only variable.</param>
    /// <returns>The registered variable.</returns>
    /// <exception cref="EngineFatalException">A variable of the same name is already registered.</exception>
    public ConfigVariable RegisterInteger(string name, Func<int> get, Action<int>? set = null) =>
        Register(ConfigVariable.Integer(name, get, set));

    /// <summary>Registers a real-valued variable.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="get">Reads the current value.</param>
    /// <param name="set">Writes a new value, or <see langword="null"/> for a read-only variable.</param>
    /// <returns>The registered variable.</returns>
    /// <exception cref="EngineFatalException">A variable of the same name is already registered.</exception>
    public ConfigVariable RegisterReal(string name, Func<double> get, Action<double>? set = null) =>
        Register(ConfigVariable.Real(name, get, set));

    /// <summary>Registers a string variable.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="get">Reads the current value.</param>
    /// <param name="set">Writes a new value, or <see langword="null"/> for a read-only variable.</param>
    /// <returns>The registered variable.</returns>
    /// <exception cref="EngineFatalException">A variable of the same name is already registered.</exception>
    public ConfigVariable RegisterText(string name, Func<string> get, Action<string>? set = null) =>
        Register(ConfigVariable.Text(name, get, set));

    /// <summary>Looks up a variable.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="variable">Receives the variable when one is registered.</param>
    /// <returns><see langword="true"/> when the name is registered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool TryGet(string name, [NotNullWhen(true)] out ConfigVariable? variable)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _variables.TryGetValue(name, out variable);
    }

    /// <summary>Determines whether a name is registered.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns><see langword="true"/> when the name is registered.</returns>
    public bool Contains(string name) => TryGet(name, out _);

    /// <summary>Removes a variable.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns><see langword="true"/> when a variable was removed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <remarks>Tasks unregister their exported variables when they are destroyed.</remarks>
    public bool Unregister(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _variables.Remove(name);
    }

    /// <summary>Reads a variable's value as text.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The value.</returns>
    /// <exception cref="KeyNotFoundException">The name is not registered.</exception>
    public string GetString(string name) =>
        TryGet(name, out ConfigVariable? variable)
            ? variable.GetString()
            : throw new KeyNotFoundException($"The variable '{name}' is not registered.");

    /// <summary>Writes a variable's value from text.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value to parse and store.</param>
    /// <exception cref="KeyNotFoundException">The name is not registered.</exception>
    /// <exception cref="InvalidOperationException">The variable is read-only.</exception>
    /// <exception cref="FormatException"><paramref name="value"/> does not parse as the variable's type.</exception>
    public void SetString(string name, string value)
    {
        if (!TryGet(name, out ConfigVariable? variable))
        {
            throw new KeyNotFoundException($"The variable '{name}' is not registered.");
        }

        variable.SetString(value);
    }

    /// <summary>Removes every registered variable.</summary>
    public void Clear() => _variables.Clear();
}
