namespace OpenIGI.Core.Configuration.Presets;

/// <summary>Which experience the game's presentation and remaster additions add up to.</summary>
/// <remarks>Stored in lower case, so <c>g_preset vanilla</c> -- what <c>-vanilla</c> sends -- names <see cref="Vanilla"/>.</remarks>
public enum ExperiencePreset
{
    /// <summary>The settings match neither: some were changed one at a time. A reading, not a choice.</summary>
    Custom,

    /// <summary>The game as it shipped in 2000: its HUD, its crosshair, its deaths, and none of the remaster's additions.</summary>
    Vanilla,

    /// <summary>The remaster's additions on: its HUD, markers, optics, saves, and bodies and weapons that stay where they fall.</summary>
    Enhanced,
}

/// <summary>
/// The other half of the presets: what the game shows and adds, rather than how well it draws. One switch between the
/// original game and the remaster's, over the HUD, the markers and optics, and the gameplay additions.
/// </summary>
/// <remarks>
/// <para>
/// These used to ride on the graphics presets, so choosing Ultra quality also chose the remastered HUD, and the Vanilla
/// picture the original's. They are a separate question -- a player can want the original HUD over the best lighting -- and
/// asked of the same variables by two presets, each made the other read Custom. So the graphics presets are quality only,
/// and these are declared here instead.
/// </para>
/// <para>
/// A <see langword="null"/> value leaves the variable alone under that preset: modern aiming is off in the original, and
/// Enhanced does not force it on.
/// </para>
/// </remarks>
public sealed class ExperiencePresetRegistry
{
    private readonly Dictionary<ICVar, string?[]> _managed = [];
    private readonly List<ICVar> _order = [];

    /// <summary>Gets the registry the game's variables declare themselves in.</summary>
    public static ExperiencePresetRegistry Default { get; } = new();

    /// <summary>Gets the variables this registry manages, in the order they were declared.</summary>
    public IReadOnlyList<ICVar> Managed => _order;

    /// <summary>Declares what the two experiences set a variable to.</summary>
    /// <typeparam name="TVar">The variable's type.</typeparam>
    /// <param name="variable">The variable to manage.</param>
    /// <param name="vanilla">Its value in the original game, or <see langword="null"/> to leave it alone.</param>
    /// <param name="enhanced">Its value in the remaster, or <see langword="null"/> to leave it alone.</param>
    /// <returns><paramref name="variable"/>, so a declaration can register and describe in one expression.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="variable"/> is <see langword="null"/>.</exception>
    public TVar Define<TVar>(TVar variable, string? vanilla, string? enhanced)
        where TVar : ICVar
    {
        ArgumentNullException.ThrowIfNull(variable);
        if (_managed.TryAdd(variable, [vanilla, enhanced]))
        {
            _order.Add(variable);
        }

        return variable;
    }

    /// <summary>Declares a variable by name, for one declared where this registry cannot be reached.</summary>
    /// <param name="cvars">The registry the variable is in.</param>
    /// <param name="name">The variable's name.</param>
    /// <param name="vanilla">Its value in the original game, or <see langword="null"/> to leave it alone.</param>
    /// <param name="enhanced">Its value in the remaster, or <see langword="null"/> to leave it alone.</param>
    /// <returns><see langword="true"/> when the variable is registered and now managed.</returns>
    public bool Define(CVarRegistry cvars, string name, string? vanilla, string? enhanced)
    {
        ArgumentNullException.ThrowIfNull(cvars);
        if (!cvars.TryGet(name, out ICVar? variable) || variable is null)
        {
            return false;
        }

        Define(variable, vanilla, enhanced);
        return true;
    }

    /// <summary>Gets each variable a preset sets, with the value it sets it to, in declaration order.</summary>
    /// <param name="preset">The preset. <see cref="ExperiencePreset.Custom"/> sets nothing.</param>
    /// <returns>The variables and values.</returns>
    public IReadOnlyList<(ICVar Variable, string Value)> ValuesOf(ExperiencePreset preset)
    {
        if (preset == ExperiencePreset.Custom)
        {
            return [];
        }

        int column = Column(preset);
        var values = new List<(ICVar, string)>(_order.Count);
        foreach (ICVar variable in _order)
        {
            if (_managed[variable][column] is { } value)
            {
                values.Add((variable, value));
            }
        }

        return values;
    }

    /// <summary>Applies a preset to every variable it names.</summary>
    /// <param name="preset">The preset to apply. <see cref="ExperiencePreset.Custom"/> does nothing.</param>
    /// <returns>How many variables were assigned.</returns>
    public int Apply(ExperiencePreset preset)
    {
        int applied = 0;
        foreach ((ICVar variable, string value) in ValuesOf(preset))
        {
            if (variable.TrySetText(value, out _))
            {
                applied++;
            }
        }

        return applied;
    }

    /// <summary>Works out which preset some view of the variables adds up to.</summary>
    /// <param name="holds">Whether a variable, as that view has it, holds a value.</param>
    /// <returns>The first preset whose every value is held, or <see cref="ExperiencePreset.Custom"/>.</returns>
    public ExperiencePreset Recognise(Func<ICVar, string, bool> holds)
    {
        ArgumentNullException.ThrowIfNull(holds);
        foreach (ExperiencePreset preset in (ReadOnlySpan<ExperiencePreset>)[ExperiencePreset.Vanilla, ExperiencePreset.Enhanced])
        {
            if (ValuesOf(preset).All(pair => holds(pair.Variable, pair.Value)))
            {
                return preset;
            }
        }

        return ExperiencePreset.Custom;
    }

    /// <summary>Works out which preset the variables themselves add up to.</summary>
    /// <returns>The preset, or <see cref="ExperiencePreset.Custom"/>.</returns>
    public ExperiencePreset Detect() => Recognise(static (variable, value) => variable.MatchesText(value));

    private static int Column(ExperiencePreset preset) => preset == ExperiencePreset.Vanilla ? 0 : 1;
}
