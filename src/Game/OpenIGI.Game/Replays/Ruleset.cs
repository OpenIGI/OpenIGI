using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Game.Replays;

/// <summary>
/// The rules a run was played under, which the boards rank by separately: <see cref="Classic"/>, the original game's;
/// <see cref="Modern"/>, the remaster's additions on; or <see cref="Unranked"/>, anything else.
/// </summary>
/// <remarks>
/// A ruleset is the value every setting that changes how the level plays (<see cref="CVarOptions.Simulation"/>) must hold: the
/// experience preset's value where the preset sets one (<see cref="ExperiencePresetRegistry"/>), and the setting's own default
/// everywhere else -- so a cheat, a developer's knob or a setting changed on its own leaves the rules, whichever they were.
/// </remarks>
/// <param name="Id">The id the boards and a replay's header name it by.</param>
/// <param name="Name">Its name as a page says it.</param>
public sealed record Ruleset(string Id, string Name)
{
    /// <summary>The original game's rules: every setting as the Vanilla experience has it.</summary>
    public static readonly Ruleset Classic = new("classic", "Classic");

    /// <summary>The remaster's rules: every setting as the Enhanced experience has it.</summary>
    public static readonly Ruleset Modern = new("modern", "Modern");

    /// <summary>Any other settings: kept and played back, never ranked.</summary>
    public static readonly Ruleset Unranked = new("unranked", "Unranked");

    /// <summary>Gets the rulesets the boards rank, in the order a page offers them.</summary>
    public static IReadOnlyList<Ruleset> Ranked { get; } = [Classic, Modern];

    /// <summary>Gets whether runs under these rules are ranked.</summary>
    public bool IsRanked => this != Unranked;

    /// <summary>A ruleset by its id.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The ruleset, or <see cref="Unranked"/> for an id that is none of the ranked ones.</returns>
    public static Ruleset FromId(string? id) => Ranked.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal)) ?? Unranked;

    /// <summary>The settings that change how the level plays, as they stand: by name in ordinal order, each as the console writes it.</summary>
    /// <param name="cvars">The registry.</param>
    /// <returns>The settings, as a replay's header records them.</returns>
    public static IReadOnlyList<(string Name, string Value)> Snapshot(CVarRegistry cvars)
    {
        ArgumentNullException.ThrowIfNull(cvars);
        return
        [
            .. cvars.All
                .Where(static v => (v.Flags & CVarOptions.Simulation) != 0)
                .OrderBy(static v => v.Name, StringComparer.Ordinal)
                .Select(static v => (v.Name, v.ValueText)),
        ];
    }

    /// <summary>Works out which rules a run's settings make.</summary>
    /// <param name="settings">The settings, as <see cref="Snapshot"/> records them.</param>
    /// <param name="cvars">The registry that knows each setting, its default and how it reads a value.</param>
    /// <param name="presets">The experience presets the ranked rules are made from.</param>
    /// <returns>The rules, and for <see cref="Unranked"/> the first setting that kept the run from the nearer of the ranked ones.</returns>
    public static (Ruleset Rules, string Why) Of(
        IReadOnlyList<(string Name, string Value)> settings,
        CVarRegistry cvars,
        ExperiencePresetRegistry presets)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(cvars);
        ArgumentNullException.ThrowIfNull(presets);
        List<string> classic = Mismatches(settings, cvars, presets, ExperiencePreset.Vanilla);
        if (classic.Count == 0)
        {
            return (Classic, string.Empty);
        }

        List<string> modern = Mismatches(settings, cvars, presets, ExperiencePreset.Enhanced);
        if (modern.Count == 0)
        {
            return (Modern, string.Empty);
        }

        // Told by the rules the run came nearer to: a player who changed one setting of the original's hears of that one.
        return (Unranked, classic.Count <= modern.Count ? classic[0] : modern[0]);
    }

    // Every setting that holds other than an experience's rules ask, said as a reason, in the registry's order; a setting the
    // registry knows and the record leaves out counts as held at its default, one the registry does not know stops the run.
    private static List<string> Mismatches(
        IReadOnlyList<(string Name, string Value)> settings,
        CVarRegistry cvars,
        ExperiencePresetRegistry presets,
        ExperiencePreset preset)
    {
        Dictionary<ICVar, string> asked = presets.ValuesOf(preset).ToDictionary(static p => p.Variable, static p => p.Value);
        Dictionary<string, string> held = new(StringComparer.Ordinal);
        List<string> reasons = [];
        foreach ((string name, string value) in settings)
        {
            held[name] = value;
        }

        foreach (ICVar variable in cvars.All.Where(static v => (v.Flags & CVarOptions.Simulation) != 0))
        {
            string expected = asked.TryGetValue(variable, out string? value) ? value : variable.DefaultText;
            string actual = held.Remove(variable.Name, out string? recorded) ? recorded : variable.DefaultText;
            if (!SameValue(variable, expected, actual))
            {
                reasons.Add($"{variable.Name} is {actual}, where these rules have {expected}");
            }
        }

        reasons.AddRange(held.Keys.Order(StringComparer.Ordinal).Select(static unknown => $"{unknown} is a setting this game does not know"));
        return reasons;
    }

    private static bool SameValue(ICVar variable, string expected, string actual) =>
        variable.TryNormalizeText(expected, out string a) && variable.TryNormalizeText(actual, out string b)
            ? string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
            : string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
}
