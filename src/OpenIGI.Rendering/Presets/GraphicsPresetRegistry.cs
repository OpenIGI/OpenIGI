using OpenIGI.Core.Configuration;

namespace OpenIGI.Rendering.Presets;

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
    public int Apply(GraphicsPreset preset)
    {
        if (preset == GraphicsPreset.Custom)
        {
            return 0;
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

    /// <summary>Works out which preset, if any, the current settings correspond to.</summary>
    /// <returns>The matching preset, or <see cref="GraphicsPreset.Custom"/>.</returns>
    /// <remarks>
    /// Called after the settings file is loaded, so that a player who saved on High and never touched
    /// anything since is shown High rather than Custom.
    /// </remarks>
    public GraphicsPreset Detect()
    {
        for (int index = 0; index < 4; index++)
        {
            bool matches = true;

            foreach ((ICVar variable, string?[] values) in _managed)
            {
                // MatchesText rather than a comparison against ValueText: a float renders as its own
                // shortest round trip, so a preset declaring 0.70 would never be recognised again once
                // it had been applied and read back as 0.7.
                if (values[index] is { } expected && !variable.MatchesText(expected))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                SetCurrent((GraphicsPreset)index);
                return Current;
            }
        }

        SetCurrent(GraphicsPreset.Custom);
        return Current;
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
