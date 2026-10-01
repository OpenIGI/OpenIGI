using System.Diagnostics.CodeAnalysis;

namespace OpenIGI.Core.Configuration;

/// <summary>
/// The set of console variables the running game knows about.
/// </summary>
/// <remarks>
/// <para>
/// This is the single source of truth the plan calls for: the developer console, the settings file
/// and the options menu are three views onto one registry, so a slider, a typed <c>r_fog_quality 4</c>
/// and a line in <c>settings.cfg</c> can never disagree about what the game is doing.
/// </para>
/// <para>
/// Names are matched case-insensitively. Registering the same name twice is a programming error and
/// throws — the source generator reports it at build time instead, so it should never reach here.
/// </para>
/// </remarks>
public sealed class CVarRegistry
{
    private readonly Dictionary<string, ICVar> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ICVar> _ordered = [];

    /// <summary>Occurs after any registered variable has changed.</summary>
    /// <remarks>Saves every subscriber from having to attach to each variable individually.</remarks>
    public event EventHandler<CVarChangedEventArgs>? AnyChanged;

    /// <summary>Gets the registry the static declarations across the game register into.</summary>
    /// <remarks>
    /// Tests construct their own <see cref="CVarRegistry"/> rather than mutating this one, so a test
    /// that changes a value cannot leak into another.
    /// </remarks>
    public static CVarRegistry Default { get; } = new();

    /// <summary>Gets the registered variables, in registration order.</summary>
    public IReadOnlyList<ICVar> All => _ordered;

    /// <summary>Gets the number of registered variables.</summary>
    public int Count => _ordered.Count;

    /// <summary>Adds a variable to the registry.</summary>
    /// <typeparam name="TVar">The concrete variable type, returned so declarations can register inline.</typeparam>
    /// <param name="variable">The variable to add.</param>
    /// <returns><paramref name="variable"/>, so a field initialiser can register and assign in one expression.</returns>
    /// <exception cref="ArgumentException">A variable with the same name is already registered.</exception>
    public TVar Add<TVar>(TVar variable)
        where TVar : ICVar
    {
        ArgumentNullException.ThrowIfNull(variable);

        if (!_byName.TryAdd(variable.Name, variable))
        {
            throw new ArgumentException($"Console variable '{variable.Name}' is already registered.", nameof(variable));
        }

        _ordered.Add(variable);
        variable.Changed += OnVariableChanged;
        return variable;
    }

    /// <summary>Looks a variable up by name.</summary>
    /// <param name="name">The name to look up. Case-insensitive.</param>
    /// <param name="variable">Receives the variable.</param>
    /// <returns><see langword="true"/> when a variable with that name is registered.</returns>
    public bool TryGet(string name, [NotNullWhen(true)] out ICVar? variable) =>
        _byName.TryGetValue(name ?? string.Empty, out variable);

    /// <summary>Determines whether a variable with the given name is registered.</summary>
    /// <param name="name">The name to look up. Case-insensitive.</param>
    /// <returns><see langword="true"/> when the name is known.</returns>
    public bool Contains(string name) => _byName.ContainsKey(name ?? string.Empty);

    /// <summary>Enumerates variables whose name contains <paramref name="fragment"/>, ordered by name.</summary>
    /// <param name="fragment">The substring to match, or an empty string to match everything.</param>
    /// <param name="includeHidden">Whether variables flagged <see cref="CVarOptions.Hidden"/> are included.</param>
    /// <returns>The matching variables.</returns>
    /// <remarks>Backs the console's <c>find</c> command and the options menu's search box.</remarks>
    public IEnumerable<ICVar> Search(string fragment, bool includeHidden = false)
    {
        string needle = fragment ?? string.Empty;

        return _ordered
            .Where(variable => includeHidden || (variable.Flags & CVarOptions.Hidden) == 0)
            .Where(variable => needle.Length == 0 || variable.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .OrderBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Enumerates the variables whose value differs from their default.</summary>
    /// <param name="flags">Only variables carrying every one of these flags are returned.</param>
    /// <returns>The matching variables, ordered by name.</returns>
    /// <remarks>
    /// The settings writer passes <see cref="CVarOptions.Archive"/>: the file records only what the
    /// player actually changed, so a default that is later re-tuned takes effect for everyone who
    /// never touched it.
    /// </remarks>
    public IEnumerable<ICVar> Modified(CVarOptions flags = CVarOptions.None) =>
        _ordered
            .Where(variable => (variable.Flags & flags) == flags && !variable.IsDefault)
            .OrderBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Restores every registered variable to its default.</summary>
    public void ResetAll()
    {
        foreach (ICVar variable in _ordered)
        {
            variable.Reset();
        }
    }

    private void OnVariableChanged(object? sender, CVarChangedEventArgs e) => AnyChanged?.Invoke(this, e);
}
