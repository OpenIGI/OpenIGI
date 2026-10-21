using OpenIGI.Core.Configuration;
using OpenIGI.Frontend.Settings;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The categories made straight from variables — Audio, and the variables of Gameplay — grouped by the word
/// after their prefix, so <c>g_ragdoll_force</c> and <c>g_ragdoll_gravity</c> share a Ragdoll panel.
/// </summary>
internal static class CvarGroupBuilder
{
    /// <summary>The variables a list of names binds to, grouped by the word after their prefix.</summary>
    /// <param name="rowBuilder">What the rows are built with.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="names">The variable names, in the order the rows are wanted.</param>
    /// <param name="fallback">The group a variable goes in when no other shares its word.</param>
    public static List<SettingsGroup> CvarGroups(SettingsRowBuilder rowBuilder, UiTheme theme, IReadOnlyList<string> names, string fallback)
    {
        var byWord = new Dictionary<string, List<Control>>(StringComparer.Ordinal);
        var order = new List<string>();
        var words = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            string word = GroupWord(name);
            words[word] = words.GetValueOrDefault(word) + 1;
        }

        foreach ((ICVar cvar, Control control) in CvarRows(rowBuilder, theme, names))
        {
            string word = GroupWord(cvar.Name);
            string title = words.GetValueOrDefault(word) >= 2 ? Humanize(word) : fallback;
            if (!byWord.TryGetValue(title, out List<Control>? rows))
            {
                rows = [];
                byWord[title] = rows;
                order.Add(title);
            }

            rows.Add(control);
        }

        // The catch-all leads, as General does in the design; the named groups follow in the order they met.
        order.Sort((a, b) => (a == fallback ? 0 : 1).CompareTo(b == fallback ? 0 : 1));
        var groups = new List<SettingsGroup>(order.Count);
        foreach (string title in order)
        {
            groups.Add(SettingsGroup.Of(title, byWord[title]));
        }

        return groups;
    }

    /// <summary>A row per variable that can be bound, captioned from its name and explained by its own help.</summary>
    public static List<(ICVar Cvar, Control Control)> CvarRows(SettingsRowBuilder rowBuilder, UiTheme theme, IReadOnlyList<string> names)
    {
        var rows = new List<(ICVar, Control)>();
        foreach (string name in names)
        {
            if (rowBuilder.Context.Cvars.TryGet(name, out ICVar? cvar) && cvar is not null && CVarBinding.CanBind(cvar)
                && CVarBinding.CreateControl(theme, cvar, lifetime: rowBuilder.Bindings) is { } control)
            {
                // The variable already carries the sentence that explains it — the same one the console
                // prints — so the tooltip is the help text the CVar was declared with.
                control.Tooltip = cvar.Flags.HasFlag(CVarOptions.RequiresRestart)
                    ? $"{cvar.Help}\n\nTakes effect after a restart.\n\n{name}"
                    : $"{cvar.Help}\n\n{name}";

                rowBuilder.BoundCvars[control] = cvar;
                rowBuilder.TabCvars.Add(cvar);
                rows.Add((cvar, control));
            }
        }

        return rows;
    }

    /// <summary>The names of every visible variable that starts with a prefix, in registration order.</summary>
    public static List<string> NamesWithPrefix(CVarRegistry cvars, string prefix)
    {
        var names = new List<string>();
        foreach (ICVar cvar in cvars.All)
        {
            if (cvar.Name.StartsWith(prefix, StringComparison.Ordinal)
                && !cvar.Flags.HasFlag(CVarOptions.Hidden))
            {
                names.Add(cvar.Name);
            }
        }

        return names;
    }

    /// <summary>The names of every visible gameplay (<c>g_</c>) and interface (<c>ui_</c>) variable.</summary>
    public static List<string> GameplayNames(CVarRegistry cvars)
    {
        var names = new List<string>();
        foreach (ICVar cvar in cvars.All)
        {
            if ((cvar.Name.StartsWith("g_", StringComparison.Ordinal) || cvar.Name.StartsWith("ui_", StringComparison.Ordinal))
                && !cvar.Flags.HasFlag(CVarOptions.Hidden))
            {
                names.Add(cvar.Name);
            }
        }

        return names;
    }

    /// <summary>The word that groups a variable: <c>g_ragdoll_force</c> groups under <c>ragdoll</c>.</summary>
    private static string GroupWord(string name)
    {
        string[] parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? parts[1] : string.Empty;
    }

    private static string Humanize(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];
}
