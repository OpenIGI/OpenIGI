using OpenIGI.Core.Configuration;

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
    private bool _applying;

    /// <summary>Occurs after <see cref="Current"/> changes.</summary>
    public event EventHandler<GraphicsPreset>? PresetChanged;

    /// <summary>Gets the registry the game's graphics settings register with.</summary>
    public static GraphicsPresetRegistry Default { get; } = new();

    /// <summary>Gets the preset the settings currently correspond to.</summary>
    public GraphicsPreset Current { get; private set; } = GraphicsPreset.Custom;

    /// <summary>Gets the one preset that may be applied, or <see langword="null"/> when any may be.</summary>
    /// <remarks>
    /// Set by <see cref="Lock"/>, for a build that ships a single picture -- the browser build is held on
    /// <see cref="GraphicsPreset.Vanilla"/>. The lock is on choosing a preset, not on the variables: the
    /// menu still frames its backdrop through the depth-of-field settings, and the console still reaches
    /// any variable by name, so <see cref="Current"/> can read <see cref="GraphicsPreset.Custom"/> while
    /// locked exactly as it can otherwise.
    /// </remarks>
    public GraphicsPreset? LockedPreset { get; private set; }

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

    /// <summary>Whether a preset may be applied under the current lock.</summary>
    /// <param name="preset">The preset in question.</param>
    /// <returns><see langword="true"/> when nothing is locked or <paramref name="preset"/> is the locked one.</returns>
    public bool IsAllowed(GraphicsPreset preset) => LockedPreset is not { } locked || locked == preset;

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

        for (int index = 0; index < 4; index++)
        {
            bool matches = true;

            foreach ((ICVar variable, string?[] values) in _managed)
            {
                // MatchesText rather than a comparison against ValueText: a float renders as its own
                // shortest round trip, so a preset declaring 0.70 would never be recognised again once
                // it had been applied and read back as 0.7.
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
        if (!_applying)
        {
            SetCurrent(GraphicsPreset.Custom);
        }
    }

    private void SetCurrent(GraphicsPreset preset)
    {
        if (Current == preset)
        {
            return;
        }

        Current = preset;
        PresetChanged?.Invoke(this, preset);
    }
}
