namespace OpenIGI.Core.Configuration.Presets;

/// <summary>
/// Knows what each graphics preset means, and notices when the settings stop matching one.
/// </summary>
/// <remarks>
/// <para>
/// Each remaster stage declares its own variables and, in the same place, what each preset sets them
/// to. Nothing here has to be updated when a stage lands, and no central table can fall out of step
/// with the variables it names.
/// </para>
/// <para>
/// The registry watches the variables it manages. A change made while a preset is being applied is
/// expected; any other change means the player has taken over, so <see cref="Current"/> becomes
/// <see cref="GraphicsPreset.Custom"/>. That is the behaviour every options menu has, and getting it
/// from the registry rather than from the menu means the console gets it too.
/// </para>
/// </remarks>
public sealed class GraphicsPresetRegistry
{
    private readonly Dictionary<ICVar, string?[]> _managed = [];
    private readonly HashSet<ICVar> _interface = [];
    private GraphicsPreset _look = GraphicsPreset.Custom;
    private bool _applying;
    private Dictionary<ICVar, string>? _heldOver;

    /// <summary>Occurs after <see cref="Current"/> changes.</summary>
    public event EventHandler<GraphicsPreset>? PresetChanged;

    /// <summary>Gets the registry the game's graphics settings register with.</summary>
    public static GraphicsPresetRegistry Default { get; } = new();

    /// <summary>Gets the preset the settings currently correspond to.</summary>
    public GraphicsPreset Current { get; private set; } = GraphicsPreset.Custom;

    /// <summary>Gets the preset the picture is: what a look that is the original's or the remaster's asks.</summary>
    /// <remarks>
    /// <para>
    /// Not <see cref="Current"/>, which reads Custom the moment any managed variable leaves the preset -- the interface's
    /// included. A player on Vanilla who turned the remastered heads-up display on lost every look that asked for
    /// Vanilla with it: the forest's blended leaves and fog, the sky turned to its sun, the lens flares, the original's
    /// texture storage. The interface (<see cref="DefineInterface"/>) draws over the picture and is left out here.
    /// </para>
    /// <para>
    /// A build that ships a single picture (<see cref="LockedPreset"/>, the browser's Vanilla) is that picture whatever
    /// the console has done since.
    /// </para>
    /// </remarks>
    public GraphicsPreset Look => LockedPreset ?? HeldPreset ?? _look;

    /// <summary>Gets whether the picture is the original's (<see cref="Look"/> is <see cref="GraphicsPreset.Vanilla"/>).</summary>
    public bool IsVanillaLook => Look == GraphicsPreset.Vanilla;

    /// <summary>Gets the one preset that may be applied, or <see langword="null"/> when any may be.</summary>
    /// <remarks>
    /// Set by <see cref="Lock"/>, for a build that ships a single picture -- the browser build is held on
    /// <see cref="GraphicsPreset.Vanilla"/>. The lock is on choosing a preset, not on the variables: the
    /// menu still frames its backdrop through the depth-of-field settings, and the console still reaches
    /// any variable by name, so <see cref="Current"/> can read <see cref="GraphicsPreset.Custom"/> while
    /// locked exactly as it can otherwise.
    /// </remarks>
    public GraphicsPreset? LockedPreset { get; private set; }

    /// <summary>Gets the one preset the game being played offers for now, or <see langword="null"/> when it offers them all.</summary>
    /// <remarks>
    /// <para>
    /// Set by <see cref="Hold"/>, for a game whose look under the remastered presets is not ready yet -- I.G.I. 2's lighting
    /// is still being tuned. Unlike <see cref="LockedPreset"/> it is the game's, not the build's: switching to a game that
    /// offers every preset puts back the player's own picture.
    /// </para>
    /// <para>
    /// It holds the picture only. The interface's variables (<see cref="DefineInterface"/>) stay as the player has them, and
    /// what the player had chosen for the picture is kept aside: put back by <see cref="Hold"/> with nothing, and what the
    /// settings file writes in its place (<see cref="PlayerValue"/>), so a game quit while held starts again as the player
    /// left it.
    /// </para>
    /// </remarks>
    public GraphicsPreset? HeldPreset { get; private set; }

    /// <summary>Gets the variables this registry manages.</summary>
    public IReadOnlyCollection<ICVar> Managed => _managed.Keys;

    /// <summary>Declares what the four presets set a variable to.</summary>
    /// <param name="variable">The variable to manage.</param>
    /// <param name="vanilla">The value under <see cref="GraphicsPreset.Vanilla"/>.</param>
    /// <param name="enhanced">The value under <see cref="GraphicsPreset.Enhanced"/>.</param>
    /// <param name="high">The value under <see cref="GraphicsPreset.High"/>.</param>
    /// <param name="ultra">The value under <see cref="GraphicsPreset.Ultra"/>.</param>
    /// <returns><paramref name="variable"/>, so a declaration can register and describe in one expression.</returns>
    /// <remarks>A <see langword="null"/> value means that preset leaves the variable alone.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="variable"/> is <see langword="null"/>.</exception>
    public TVar Define<TVar>(TVar variable, string? vanilla, string? enhanced, string? high, string? ultra)
        where TVar : ICVar
    {
        ArgumentNullException.ThrowIfNull(variable);

        if (_managed.TryAdd(variable, [vanilla, enhanced, high, ultra]))
        {
            variable.Changed += OnManagedVariableChanged;
        }

        return variable;
    }

    /// <summary>Declares what the four presets set an interface variable to: one drawn over the picture, not part of it.</summary>
    /// <param name="variable">The variable to manage.</param>
    /// <param name="vanilla">The value under <see cref="GraphicsPreset.Vanilla"/>.</param>
    /// <param name="enhanced">The value under <see cref="GraphicsPreset.Enhanced"/>.</param>
    /// <param name="high">The value under <see cref="GraphicsPreset.High"/>.</param>
    /// <param name="ultra">The value under <see cref="GraphicsPreset.Ultra"/>.</param>
    /// <returns><paramref name="variable"/>.</returns>
    /// <remarks>Counted for <see cref="Current"/> like any other, and left out of <see cref="Look"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="variable"/> is <see langword="null"/>.</exception>
    public TVar DefineInterface<TVar>(TVar variable, string? vanilla, string? enhanced, string? high, string? ultra)
        where TVar : ICVar
    {
        Define(variable, vanilla, enhanced, high, ultra);
        _interface.Add(variable);
        return variable;
    }

    /// <summary>Applies a preset to every variable it names.</summary>
    /// <param name="preset">The preset to apply. <see cref="GraphicsPreset.Custom"/> does nothing.</param>
    /// <returns>How many variables were assigned.</returns>
    /// <exception cref="InvalidOperationException">
    /// The registry is locked to another preset; see <see cref="LockedPreset"/>.
    /// </exception>
    public int Apply(GraphicsPreset preset)
    {
        if (preset == GraphicsPreset.Custom)
        {
            return 0;
        }

        if (!IsAllowed(preset))
        {
            throw new InvalidOperationException(
                $"The graphics preset is locked to {LockedPreset}; {preset} cannot be applied.");
        }

        int index = (int)preset;
        int applied = 0;

        _applying = true;
        try
        {
            foreach ((ICVar variable, string?[] values) in _managed)
            {
                if (values[index] is not { } value)
                {
                    continue;
                }

                if (variable.TrySetText(value, out _))
                {
                    applied++;
                }
            }
        }
        finally
        {
            _applying = false;
        }

        SetCurrent(preset);
        return applied;
    }

    /// <summary>Applies a preset and makes it the only one that may be applied from then on.</summary>
    /// <param name="preset">The preset to hold the settings on.</param>
    /// <returns>How many variables were assigned.</returns>
    /// <remarks>
    /// Called once the settings file has been read, so whatever a saved file restored is replaced by the
    /// locked preset rather than surviving underneath it. Locking again to the same preset re-applies it.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="preset"/> is <see cref="GraphicsPreset.Custom"/>.</exception>
    /// <exception cref="InvalidOperationException">The registry is already locked to another preset.</exception>
    public int Lock(GraphicsPreset preset)
    {
        if (preset == GraphicsPreset.Custom)
        {
            throw new ArgumentOutOfRangeException(nameof(preset), preset, "Custom is a reading, not a preset to lock to.");
        }

        if (!IsAllowed(preset))
        {
            throw new InvalidOperationException(
                $"The graphics preset is already locked to {LockedPreset}; it cannot be locked to {preset}.");
        }

        LockedPreset = preset;
        return Apply(preset);
    }

    /// <summary>The value the lock holds a variable at.</summary>
    /// <param name="variable">The variable.</param>
    /// <returns>
    /// The locked preset's value for it, or <see langword="null"/> where nothing is locked, the variable is not managed, or
    /// the locked preset leaves it alone.
    /// </returns>
    /// <remarks>
    /// What a locked build's settings file measures a managed variable against, in place of its default: the lock applies
    /// the preset at every start, so the player's own change is whatever differs from it. Measured against the default, a
    /// change back to the default was not written at all, and the next start's lock undid it.
    /// </remarks>
    public string? LockedValue(ICVar variable)
    {
        ArgumentNullException.ThrowIfNull(variable);

        return LockedPreset is { } locked && _managed.TryGetValue(variable, out string?[]? values)
            ? values[(int)locked]
            : null;
    }

    /// <summary>Records every managed variable's value and the preset they read as, to put back with <see cref="Restore"/>.</summary>
    /// <returns>The record.</returns>
    public GraphicsPresetSnapshot Capture() => new([.. _managed.Keys.Select(static v => (v, v.ValueText))], Current);

    /// <summary>Puts back what <see cref="Capture"/> recorded, whatever has been applied since.</summary>
    /// <param name="snapshot">The record.</param>
    public void Restore(GraphicsPresetSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        SetQuietly(snapshot.Values.Select(static entry => KeyValuePair.Create(entry.Variable, entry.Value)));
        SetCurrent(snapshot.Preset);
    }

    /// <summary>Whether a preset may be applied under the current lock.</summary>
    /// <param name="preset">The preset in question.</param>
    /// <returns><see langword="true"/> when nothing is locked or <paramref name="preset"/> is the locked one.</returns>
    public bool IsAllowed(GraphicsPreset preset) =>
        (LockedPreset is not { } locked || locked == preset) && (HeldPreset is not { } held || held == preset);

    /// <summary>Holds the picture on a preset for the game being played, or lets it go back to the player's.</summary>
    /// <param name="preset">The one preset the game offers, or <see langword="null"/> for every one.</param>
    /// <remarks>
    /// <para>
    /// Holding records the player's picture -- every managed variable but the interface's -- and applies the preset's
    /// picture over it. Letting go puts the record back, and the preset is read again. Holding the preset already held does
    /// nothing, and holding another lets the first go before taking the second, so the record is always the player's own.
    /// </para>
    /// <para>
    /// A build locked to one preset (<see cref="LockedPreset"/>) is already held: nothing is done. The player's changes to
    /// the picture's variables while held last until it is let go; they are not the player's own picture.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="preset"/> is <see cref="GraphicsPreset.Custom"/>.</exception>
    public void Hold(GraphicsPreset? preset)
    {
        if (preset == GraphicsPreset.Custom)
        {
            throw new ArgumentOutOfRangeException(nameof(preset), preset, "Custom is a reading, not a preset to hold.");
        }

        if (preset == HeldPreset || LockedPreset is not null)
        {
            return;
        }

        if (_heldOver is { } player)
        {
            _heldOver = null;
            HeldPreset = null;
            SetQuietly(player);
            Detect();
        }

        if (preset is not { } held)
        {
            return;
        }

        _heldOver = _managed.Keys.Where(variable => !_interface.Contains(variable)).ToDictionary(static v => v, static v => v.ValueText);
        HeldPreset = held;
        SetQuietly(_managed
            .Where(entry => !_interface.Contains(entry.Key) && entry.Value[(int)held] is not null)
            .Select(entry => KeyValuePair.Create(entry.Key, entry.Value[(int)held]!)));
        Detect();
    }

    /// <summary>What the player had a variable at under the held preset, for the settings file to write in place of the held value.</summary>
    /// <param name="variable">The variable.</param>
    /// <returns>The player's value, or <see langword="null"/> where nothing is held or the hold does not cover the variable.</returns>
    public string? PlayerValue(ICVar variable)
    {
        ArgumentNullException.ThrowIfNull(variable);
        return _heldOver is { } player && player.TryGetValue(variable, out string? value) ? value : null;
    }

    // Sets variables as a preset does: the registry does not read each change as the player taking over.
    private void SetQuietly(IEnumerable<KeyValuePair<ICVar, string>> values)
    {
        _applying = true;
        try
        {
            foreach ((ICVar variable, string value) in values)
            {
                variable.TrySetText(value, out _);
            }
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>Works out which preset, if any, the current settings correspond to.</summary>
    /// <returns>The matching preset, or <see cref="GraphicsPreset.Custom"/>.</returns>
    /// <remarks>
    /// Called after the settings file is loaded, so that a player who saved on High and never touched
    /// anything since is shown High rather than Custom.
    /// </remarks>
    public GraphicsPreset Detect()
    {
        SetCurrent(Recognise(static (variable, expected) => variable.MatchesText(expected)));
        return Current;
    }

    /// <summary>Works out which preset some view of the settings corresponds to, without changing <see cref="Current"/>.</summary>
    /// <param name="holds">
    /// Whether a variable, in that view, holds a value: the variable itself for <see cref="Detect"/>, or the
    /// settings page's unapplied changes over it.
    /// </param>
    /// <returns>The matching preset, or <see cref="GraphicsPreset.Custom"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="holds"/> is <see langword="null"/>.</exception>
    public GraphicsPreset Recognise(Func<ICVar, string, bool> holds)
    {
        ArgumentNullException.ThrowIfNull(holds);
        return Recognise(holds, pictureOnly: false);
    }

    private GraphicsPreset Recognise(Func<ICVar, string, bool> holds, bool pictureOnly)
    {
        for (int index = 0; index < 4; index++)
        {
            bool matches = true;

            foreach ((ICVar variable, string?[] values) in _managed)
            {
                // MatchesText rather than a comparison against ValueText: a float renders as its own
                // shortest round trip, so a preset declaring 0.70 would never be recognised again once
                // it had been applied and read back as 0.7.
                if (pictureOnly && _interface.Contains(variable))
                {
                    continue;
                }

                if (values[index] is { } expected && !holds(variable, expected))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return (GraphicsPreset)index;
            }
        }

        return GraphicsPreset.Custom;
    }

    /// <summary>What a preset sets each variable it names to.</summary>
    /// <param name="preset">The preset. <see cref="GraphicsPreset.Custom"/> names nothing.</param>
    /// <returns>Each variable the preset assigns, with its value, in the order they were declared.</returns>
    /// <remarks>For a caller that stages the preset rather than applying it: the settings page's draft.</remarks>
    public IReadOnlyList<(ICVar Variable, string Value)> ValuesOf(GraphicsPreset preset)
    {
        var values = new List<(ICVar, string)>();
        if (preset == GraphicsPreset.Custom)
        {
            return values;
        }

        foreach ((ICVar variable, string?[] presets) in _managed)
        {
            if (presets[(int)preset] is { } value)
            {
                values.Add((variable, value));
            }
        }

        return values;
    }

    private void OnManagedVariableChanged(object? sender, CVarChangedEventArgs e)
    {
        // Recognised again rather than set to Custom outright: a setting turned and then turned back is
        // the preset again. Setting Custom left it Custom for good, and every look that asks for Vanilla
        // -- the original's rain, forest, fogless sky -- stayed off after the player put the value back,
        // so toggling Storm Effects on and off on Vanilla swapped the original's rain for the band.
        if (!_applying)
        {
            SetCurrent(Recognise(static (variable, expected) => variable.MatchesText(expected)));
        }
    }

    private void SetCurrent(GraphicsPreset preset)
    {
        // Every path that settles Current comes through here, so the picture's preset is read again with it.
        _look = Recognise(static (variable, expected) => variable.MatchesText(expected), pictureOnly: true);
        if (Current == preset)
        {
            return;
        }

        Current = preset;
        PresetChanged?.Invoke(this, preset);
    }
}

/// <summary>The managed variables' values at one moment, and the preset they read as (<see cref="GraphicsPresetRegistry.Capture"/>).</summary>
/// <param name="Values">Each variable with its value as text.</param>
/// <param name="Preset">The preset the values read as.</param>
public sealed record GraphicsPresetSnapshot(IReadOnlyList<(ICVar Variable, string Value)> Values, GraphicsPreset Preset);
