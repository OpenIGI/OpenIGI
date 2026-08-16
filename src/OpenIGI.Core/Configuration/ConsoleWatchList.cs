namespace OpenIGI.Core.Configuration;

/// <summary>
/// One line of a watch panel: a name and the value it resolved to this frame.
/// </summary>
/// <param name="Name">What is being watched — a variable name or a registered provider's key.</param>
/// <param name="Value">The value read this frame, or a short message when it could not be read.</param>
/// <param name="IsResolved">Whether the name resolved to anything at all.</param>
public readonly record struct ConsoleWatchEntry(string Name, string Value, bool IsResolved);

/// <summary>
/// The set of values pinned to stay on screen, resolved fresh every frame.
/// </summary>
/// <remarks>
/// <para>
/// Watching is what a console is for during a hunt: naming a variable once and then playing while it
/// updates beats typing its name repeatedly and reading a scrollback that has already moved on.
/// </para>
/// <para>
/// Entries are resolved <b>by name, every read</b>, rather than being bound to an <see cref="ICVar"/>
/// once. That is deliberate: a name can be pinned before the subsystem that declares it has
/// registered, and an unresolved entry should start working the moment the variable appears rather
/// than staying permanently dead.
/// </para>
/// <para>
/// Beyond variables, a host can register a <see cref="AddProvider">provider</see> — a named function
/// returning text — so things that are not variables at all, such as a frame rate or a player
/// position, can be pinned in the same list without this type knowing what any of them are.
/// </para>
/// </remarks>
public sealed class ConsoleWatchList
{
    private readonly List<string> _names = [];
    private readonly Dictionary<string, Func<string>> _providers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Occurs when something is pinned or unpinned.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the pinned names, in the order they were pinned.</summary>
    public IReadOnlyList<string> Names => _names;

    /// <summary>Gets the names a host has made watchable that are not console variables.</summary>
    public IReadOnlyCollection<string> ProviderNames => _providers.Keys;

    /// <summary>Makes a named value watchable without it being a console variable.</summary>
    /// <param name="name">The key the reader pins.</param>
    /// <param name="read">Returns the value's current text.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="read"/> is <see langword="null"/>.</exception>
    /// <remarks>Registering the same name again replaces the reader, so a host can re-attach after a world reload.</remarks>
    public void AddProvider(string name, Func<string> read)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(read);
        _providers[name] = read;
    }

    /// <summary>Pins a name.</summary>
    /// <param name="name">The variable or provider name to watch.</param>
    /// <returns><see langword="true"/> when it was added, <see langword="false"/> when already pinned.</returns>
    public bool Add(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string trimmed = name.Trim();
        if (_names.Exists(existing => existing.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        _names.Add(trimmed);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Unpins a name.</summary>
    /// <param name="name">The name to stop watching.</param>
    /// <returns><see langword="true"/> when it was pinned.</returns>
    public bool Remove(string name)
    {
        int index = _names.FindIndex(existing => existing.Equals(name ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }

        _names.RemoveAt(index);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Unpins everything.</summary>
    public void Clear()
    {
        if (_names.Count == 0)
        {
            return;
        }

        _names.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reads every pinned value.</summary>
    /// <param name="variables">The registry names are resolved against.</param>
    /// <param name="into">The list to fill, cleared first.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// Fills a caller-owned list rather than allocating, because this runs every frame while the
    /// overlay is up — a watch panel that costs a garbage collection is a watch panel that changes
    /// what it is measuring.
    /// </remarks>
    public void Read(CVarRegistry variables, List<ConsoleWatchEntry> into)
    {
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();

        foreach (string name in _names)
        {
            if (_providers.TryGetValue(name, out Func<string>? read))
            {
                into.Add(new ConsoleWatchEntry(name, read(), true));
                continue;
            }

            into.Add(variables.TryGet(name, out ICVar? variable)
                ? new ConsoleWatchEntry(name, variable.ValueText, true)
                : new ConsoleWatchEntry(name, "?", false));
        }
    }

    /// <summary>Renders the pinned names as one line, for the settings file.</summary>
    /// <returns>The names separated by spaces.</returns>
    public string Render() => string.Join(' ', _names);

    /// <summary>Replaces the pinned names from a rendered line.</summary>
    /// <param name="text">A space-separated list of names, as <see cref="Render"/> produces.</param>
    /// <remarks>Unknown names are kept rather than dropped: they resolve later, and silently discarding a reader's pin is worse than showing it unresolved.</remarks>
    public void Parse(string? text)
    {
        _names.Clear();

        foreach (string name in (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!_names.Exists(existing => existing.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                _names.Add(name);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
