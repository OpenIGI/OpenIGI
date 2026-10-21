using OpenIGI.Core.Configuration;

namespace OpenIGI.Frontend.Settings;

/// <summary>
/// Changes made on a settings page that have not been applied: what the page's rows show and write, held
/// back from the variables themselves until the player confirms them.
/// </summary>
/// <remarks>
/// <para>
/// Every graphics and display row used to write its variable the moment it moved, which is a preview for a
/// slider but no way to choose a graphics API -- picking Direct3D 12 rebuilt the game there and then, on the
/// way to picking anything else -- or a resolution the display cannot show. Here a row is bound to a
/// <see cref="Stage">staged</see> stand-in for its variable instead: the stand-in reads what was chosen and
/// writes nothing, until <see cref="Commit"/> writes the lot at once, or <see cref="Discard"/> forgets it.
/// </para>
/// <para>
/// The stand-in is an <see cref="ICVar"/> itself, so the row bindings (<see cref="CVarBinding"/>,
/// <see cref="QualityBinding"/>) work over it unchanged, and a change made to the variable from elsewhere --
/// the console, the host -- still reaches the row where nothing is staged over it.
/// </para>
/// </remarks>
public sealed class SettingsDraft : IDisposable
{
    private readonly CVarRegistry _cvars;
    private readonly Dictionary<ICVar, StagedCVar> _staged = [];

    // Pending values in the order they were first chosen, which is the order they are written in.
    private readonly List<StagedCVar> _order = [];

    /// <summary>Initializes a new instance of the <see cref="SettingsDraft"/> class.</summary>
    /// <param name="cvars">The registry the staged variables live in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="cvars"/> is <see langword="null"/>.</exception>
    public SettingsDraft(CVarRegistry cvars)
    {
        ArgumentNullException.ThrowIfNull(cvars);
        _cvars = cvars;
    }

    /// <summary>Occurs whenever what the draft shows changes: a value staged, applied, discarded, or changed underneath.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets a value indicating whether any variable has a change waiting to be applied.</summary>
    public bool HasChanges => _order.Count > 0;

    /// <summary>Gets each variable with a change waiting, and the value it will be given, in the order they were chosen.</summary>
    public IReadOnlyList<(ICVar Variable, string Value)> Pending =>
        [.. _order.Select(static staged => (staged.Target, staged.PendingText!))];

    /// <summary>Gets the staged stand-in for a variable, made on first use.</summary>
    /// <param name="variable">The variable.</param>
    /// <returns>An <see cref="ICVar"/> that reads the draft's value and writes only to the draft.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="variable"/> is <see langword="null"/>.</exception>
    public ICVar Stage(ICVar variable)
    {
        ArgumentNullException.ThrowIfNull(variable);

        if (variable is StagedCVar staged && ReferenceEquals(staged.Draft, this))
        {
            return staged;
        }

        if (!_staged.TryGetValue(variable, out StagedCVar? existing))
        {
            existing = new StagedCVar(this, variable);
            _staged[variable] = existing;
        }

        return existing;
    }

    /// <summary>Finds a variable by name and returns its staged stand-in.</summary>
    /// <param name="name">The variable's name.</param>
    /// <returns>The stand-in, or <see langword="null"/> when no such variable is registered.</returns>
    public ICVar? Find(string name) =>
        _cvars.TryGet(name, out ICVar? variable) && variable is not null ? Stage(variable) : null;

    /// <summary>Whether a variable, with any staged change over it, holds a value.</summary>
    /// <param name="variable">The real variable.</param>
    /// <param name="text">The value in question.</param>
    /// <returns><see langword="true"/> when the value the draft shows for it is <paramref name="text"/>.</returns>
    /// <remarks>Answers without staging the variable, for reading a whole preset's worth of them.</remarks>
    public bool Holds(ICVar variable, string text)
    {
        ArgumentNullException.ThrowIfNull(variable);
        return _staged.TryGetValue(variable, out StagedCVar? staged)
            ? staged.MatchesText(text)
            : variable.MatchesText(text);
    }

    /// <summary>Writes every staged change to its variable, and empties the draft.</summary>
    /// <param name="last">
    /// Which variables to write after all the others, or <see langword="null"/>: for one whose change does
    /// more than set a value -- the graphics API rebuilds the game -- and should see the rest in force.
    /// </param>
    /// <returns>Each variable written, with the value it held before, for putting it back.</returns>
    public IReadOnlyList<(ICVar Variable, string Previous)> Commit(Func<ICVar, bool>? last = null)
    {
        List<StagedCVar> order = [.. _order.Where(staged => last?.Invoke(staged.Target) != true)];
        order.AddRange(_order.Where(staged => last?.Invoke(staged.Target) == true));

        // Emptied first, so each write lands on a variable with nothing staged over it, and reaches its row
        // through the stand-in's echo of the variable's own change.
        var written = new List<(ICVar, string)>(order.Count);
        var values = order.Select(static staged => (staged, Text: staged.PendingText!)).ToList();
        foreach (StagedCVar staged in order)
        {
            staged.ClearPending();
        }

        _order.Clear();

        foreach ((StagedCVar staged, string text) in values)
        {
            string previous = staged.Target.ValueText;
            if (staged.Target.TrySetText(text, out _))
            {
                written.Add((staged.Target, previous));
            }

            // Refused, or taken as something else: the row still shows what was staged, so tell it the truth.
            if (!string.Equals(staged.Target.ValueText, text, StringComparison.Ordinal))
            {
                staged.Echo(text);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return written;
    }

    /// <summary>Forgets every staged change, so each row shows its variable again.</summary>
    public void Discard()
    {
        if (_order.Count == 0)
        {
            return;
        }

        List<StagedCVar> discarded = [.. _order];
        _order.Clear();
        foreach (StagedCVar staged in discarded)
        {
            staged.Revert();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Unhooks every stand-in from its variable. The draft's changes are dropped, not applied.</summary>
    public void Dispose()
    {
        foreach (StagedCVar staged in _staged.Values)
        {
            staged.Detach();
        }

        _staged.Clear();
        _order.Clear();
    }

    private void OnStaged(StagedCVar staged, bool pending)
    {
        _order.Remove(staged);
        if (pending)
        {
            _order.Add(staged);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnUnderlyingChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>A variable as the draft shows it: its staged value if it has one, its own otherwise.</summary>
    private sealed class StagedCVar : ICVar
    {
        public StagedCVar(SettingsDraft draft, ICVar target)
        {
            Draft = draft;
            Target = target;
            Target.Changed += OnTargetChanged;
        }

        public event EventHandler<CVarChangedEventArgs>? Changed;

        public SettingsDraft Draft { get; }

        public ICVar Target { get; }

        public string? PendingText { get; private set; }

        public string Name => Target.Name;

        public string Help => Target.Help;

        public CVarOptions Flags => Target.Flags;

        public Type ValueType => Target.ValueType;

        public string ValueText => PendingText ?? Target.ValueText;

        public string DefaultText => Target.DefaultText;

        public string RangeText => Target.RangeText;

        public IReadOnlyList<string> ValueChoices => Target.ValueChoices;

        public bool IsDefault => string.Equals(ValueText, DefaultText, StringComparison.Ordinal);

        public bool TrySetText(string text, out string? reason)
        {
            if (!Target.TryNormalizeText(text, out string normalized))
            {
                reason = Target.RangeText.Length == 0
                    ? $"'{text}' is not a valid value"
                    : $"'{text}' is not a valid value; expected {Target.RangeText}";
                return false;
            }

            reason = null;
            string previous = ValueText;

            // Choosing what the variable already holds is no change at all, so it unstages it.
            string? pending = string.Equals(normalized, Target.ValueText, StringComparison.Ordinal) ? null : normalized;
            if (string.Equals(pending, PendingText, StringComparison.Ordinal))
            {
                return true;
            }

            PendingText = pending;
            Draft.OnStaged(this, pending is not null);
            if (!string.Equals(previous, ValueText, StringComparison.Ordinal))
            {
                Changed?.Invoke(this, new CVarChangedEventArgs(this, previous, ValueText));
            }

            return true;
        }

        public bool MatchesText(string text) =>
            PendingText is null
                ? Target.MatchesText(text)
                : Target.TryNormalizeText(text, out string normalized)
                    && string.Equals(normalized, PendingText, StringComparison.OrdinalIgnoreCase);

        public bool TryNormalizeText(string text, out string normalized) => Target.TryNormalizeText(text, out normalized);

        public void Reset() => TrySetText(DefaultText, out _);

        public void ClearPending() => PendingText = null;

        public void Echo(string shown) => Changed?.Invoke(this, new CVarChangedEventArgs(this, shown, ValueText));

        public void Revert()
        {
            if (PendingText is not { } previous)
            {
                return;
            }

            PendingText = null;
            Changed?.Invoke(this, new CVarChangedEventArgs(this, previous, ValueText));
        }

        public void Detach()
        {
            Target.Changed -= OnTargetChanged;
            Changed = null;
        }

        private void OnTargetChanged(object? sender, CVarChangedEventArgs e)
        {
            // Changed from elsewhere to what was staged: nothing is waiting any more.
            if (PendingText is not null && string.Equals(PendingText, Target.ValueText, StringComparison.Ordinal))
            {
                PendingText = null;
                Draft.OnStaged(this, pending: false);
            }

            if (PendingText is null)
            {
                Changed?.Invoke(this, new CVarChangedEventArgs(this, e.PreviousText, e.CurrentText));
            }

            Draft.OnUnderlyingChanged();
        }
    }
}
