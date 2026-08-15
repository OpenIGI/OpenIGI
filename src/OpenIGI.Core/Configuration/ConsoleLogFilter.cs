namespace OpenIGI.Core.Configuration;

/// <summary>
/// Decides which scrollback lines are shown: by severity, by the subsystem that wrote them, and by a
/// search string.
/// </summary>
/// <remarks>
/// <para>
/// A console that mirrors the engine log fills with hundreds of lines a second during a load, and the
/// one line worth reading is somewhere in the middle of them. Filtering is what turns that from a
/// wall into a tool.
/// </para>
/// <para>
/// The three criteria combine with <b>and</b>, which is the reading every log viewer has trained
/// people to expect: narrowing by severity and then typing a search should search within the
/// narrowing, not widen it again.
/// </para>
/// <para>
/// Deliberately free of any view state — it answers only "does this line pass?". Which lines are on
/// screen, and where the scroll sits, belong to the model that owns the scrollback.
/// </para>
/// </remarks>
public sealed class ConsoleLogFilter
{
    private readonly HashSet<ConsoleSeverity> _severities =
        [.. Enum.GetValues<ConsoleSeverity>()];

    private readonly HashSet<string> _categories = new(StringComparer.OrdinalIgnoreCase);

    private string _search = string.Empty;

    /// <summary>Occurs when any criterion changes, so a view can rebuild what it shows.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets or sets the text a line must contain, or an empty string to match every line.</summary>
    /// <remarks>Matched against the message only, never the category — see <see cref="ConsoleLine"/>.</remarks>
    public string Search
    {
        get => _search;
        set
        {
            string wanted = value ?? string.Empty;
            if (!string.Equals(_search, wanted, StringComparison.Ordinal))
            {
                _search = wanted;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Gets the categories shown, or an empty set to show every category.</summary>
    /// <remarks>Empty means "no category filter", not "hide everything" — a filter nobody has set must not hide the log.</remarks>
    public IReadOnlyCollection<string> Categories => _categories;

    /// <summary>Gets a value indicating whether any criterion is narrowing the scrollback.</summary>
    /// <remarks>What a view asks before drawing a "filtered" badge, and what lets it skip the filter pass entirely.</remarks>
    public bool IsActive =>
        _search.Length > 0 ||
        _categories.Count > 0 ||
        _severities.Count != Enum.GetValues<ConsoleSeverity>().Length;

    /// <summary>Determines whether a severity is shown.</summary>
    /// <param name="severity">The severity to test.</param>
    /// <returns><see langword="true"/> when lines of that severity pass.</returns>
    public bool IsSeverityShown(ConsoleSeverity severity) => _severities.Contains(severity);

    /// <summary>Shows or hides a severity.</summary>
    /// <param name="severity">The severity to change.</param>
    /// <param name="shown">Whether lines of that severity pass.</param>
    public void SetSeverity(ConsoleSeverity severity, bool shown)
    {
        bool changed = shown ? _severities.Add(severity) : _severities.Remove(severity);
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Shows or hides a category.</summary>
    /// <param name="category">The category to change.</param>
    /// <param name="shown">Whether lines from that category pass.</param>
    /// <remarks>
    /// Adding the first category switches the filter from "everything" to "only these", which is why
    /// showing a category is an <em>add</em> and hiding it a <em>remove</em>: the set names what is
    /// wanted, and an empty set wants everything.
    /// </remarks>
    public void SetCategory(string category, bool shown)
    {
        if (string.IsNullOrEmpty(category))
        {
            return;
        }

        bool changed = shown ? _categories.Add(category) : _categories.Remove(category);
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Restores the filter to showing everything.</summary>
    public void Reset()
    {
        _severities.Clear();
        foreach (ConsoleSeverity severity in Enum.GetValues<ConsoleSeverity>())
        {
            _severities.Add(severity);
        }

        _categories.Clear();
        _search = string.Empty;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Determines whether a line passes every criterion.</summary>
    /// <param name="line">The line to test.</param>
    /// <returns><see langword="true"/> when the line is shown.</returns>
    public bool Matches(in ConsoleLine line)
    {
        if (!_severities.Contains(line.Severity))
        {
            return false;
        }

        if (_categories.Count > 0 && !_categories.Contains(line.Category))
        {
            return false;
        }

        return _search.Length == 0 ||
            line.Text.Contains(_search, StringComparison.OrdinalIgnoreCase);
    }
}
