using System.Globalization;
using OpenIGI.Core.Configuration;
using OpenIGI.Frontend.Settings;
using OpenIGI.UI;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// Turns a page's <see cref="SettingsPanel"/>s into the groups the settings page deals into columns: each row bound to
/// its variable, the Advanced rows behind an expander at the panel's foot, and the rows that follow another setting
/// shown only while it is on.
/// </summary>
internal static class SettingsPanelBuilder
{
    /// <summary>The variable that remembers which panels' Advanced rows are open.</summary>
    internal const string ExpandedVariable = "ui_settings_expanded";

    /// <summary>Builds a page's panels.</summary>
    /// <param name="rowBuilder">What the rows are built with; its lookup goes through the draft on a staged page.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="page">The page's name, which keys its panels' remembered state.</param>
    /// <param name="panels">The panels, in the order shown.</param>
    /// <param name="build">
    /// Builds a row the page makes itself rather than as <see cref="SettingsRowBuilder.SettingRows"/> would, or returns
    /// <see langword="null"/> to have it built that way; <see langword="null"/> for none.
    /// </param>
    public static List<SettingsGroup> Build(
        SettingsRowBuilder rowBuilder,
        UiTheme theme,
        string page,
        IReadOnlyList<SettingsPanel> panels,
        Func<SettingRow, Control?>? build = null)
    {
        var groups = new List<SettingsGroup>(panels.Count);
        foreach (SettingsPanel panel in panels)
        {
            if (Panel(rowBuilder, theme, $"{page}/{panel.Title}", panel, build) is { } group)
            {
                groups.Add(group);
            }
        }

        return groups;
    }

    /// <summary>The panel's rows, or <see langword="null"/> when none of its variables is registered here.</summary>
    private static SettingsGroup? Panel(
        SettingsRowBuilder rowBuilder,
        UiTheme theme,
        string key,
        SettingsPanel panel,
        Func<SettingRow, Control?>? build)
    {
        var rows = new List<PanelRow>();
        foreach (QualityGroup group in panel.Groups)
        {
            if (QualityRow(rowBuilder, theme, group) is { } control)
            {
                rows.Add(new PanelRow(control, group.ShownWhen, Advanced: false));
            }
        }

        foreach (SettingRow setting in panel.Rows)
        {
            Control? control = build?.Invoke(setting);
            if (control is null && rowBuilder.SettingRows(theme, [setting]) is [{ } built])
            {
                control = built;
            }

            if (control is not null)
            {
                rows.Add(new PanelRow(control, setting.ShownWhen, setting.Advanced));
            }
        }

        if (rows.Count == 0)
        {
            return null;
        }

        List<PanelRow> advanced = [.. rows.Where(static row => row.Advanced)];
        ExpanderRow? expander = advanced.Count == 0
            ? null
            : new ExpanderRow(theme, string.Empty, IsExpanded(rowBuilder.Context, key))
            {
                Tooltip = "Shows the panel's finer settings, which most players never need to change.",
            };

        // The followed variables as this page reads them -- through the draft on a staged page -- so a row appears the
        // moment the setting it follows is chosen, before it is applied.
        var followed = new Dictionary<string, ICVar?>(StringComparer.Ordinal);
        foreach (PanelRow row in rows)
        {
            if (row.ShownWhen is { } condition && !followed.ContainsKey(condition.Variable))
            {
                followed[condition.Variable] = rowBuilder.Find(condition.Variable);
            }
        }

        bool Shows(PanelRow row) =>
            row.ShownWhen is not { } condition
            || followed[condition.Variable] is not { } cvar
            || condition.Shows(cvar.ValueText);

        void Refresh()
        {
            int offered = 0;
            foreach (PanelRow row in rows)
            {
                bool shows = Shows(row);
                if (row.Advanced && shows)
                {
                    offered++;
                }

                bool shown = shows && (!row.Advanced || expander!.IsExpanded);
                row.Control.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            }

            if (expander is not null)
            {
                expander.Caption = string.Create(CultureInfo.InvariantCulture, $"Advanced ({offered})");
                expander.Visibility = offered > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        foreach (ICVar? cvar in followed.Values)
        {
            if (cvar is null)
            {
                continue;
            }

            EventHandler<CVarChangedEventArgs> changed = (_, _) => Refresh();
            cvar.Changed += changed;
            rowBuilder.Bindings.Add(new Unsubscribe(() => cvar.Changed -= changed));
        }

        if (expander is not null)
        {
            expander.ExpandedChanged += expanded =>
            {
                Remember(rowBuilder.Context, key, expanded);
                Refresh();
            };
        }

        Refresh();

        // The common rows, then the expander, then what it opens: the order the keys walk them in.
        List<Control> shownFirst = [.. rows.Where(static row => !row.Advanced).Select(static row => row.Control)];
        List<Control> after = [.. advanced.Select(static row => row.Control)];
        if (expander is not null)
        {
            shownFirst.Add(expander);
        }

        return new SettingsGroup(panel.Title, [.. shownFirst, .. after]);
    }

    /// <summary>A quality group's row, explained and greyed out where it cannot take effect; null where its variables are not all registered.</summary>
    private static Dropdown? QualityRow(SettingsRowBuilder rowBuilder, UiTheme theme, QualityGroup group)
    {
        if (QualityBinding.CreateControl(theme, rowBuilder.Find, group, rowBuilder.Bindings) is not { } control)
        {
            return null;
        }

        // A group can write a variable that only takes effect on the next level load. The player has to be told which,
        // and the help line shows only the tooltip's first line, so the note goes there rather than at the end.
        string restart = rowBuilder.NamesRequiringRestart(group.Variables) ? "   [takes effect on the next level]" : string.Empty;
        string unavailable = rowBuilder.Unavailable(group.Variables);
        control.IsEnabled = unavailable.Length == 0;
        control.Tooltip =
            $"{unavailable}{group.Help}{restart}\n\nSets: {string.Join(", ", group.Variables)}\n\nReachable from the console by name.";
        rowBuilder.TrackForReset(group.Variables);
        return control;
    }

    /// <summary>Whether the player left a panel's Advanced rows open.</summary>
    private static bool IsExpanded(ShellContext context, string key) =>
        context.Cvars.TryGet(ExpandedVariable, out ICVar? cvar) && cvar is not null
            && cvar.ValueText.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(key, StringComparer.Ordinal);

    /// <summary>Remembers that a panel's Advanced rows were opened or closed.</summary>
    private static void Remember(ShellContext context, string key, bool expanded)
    {
        if (!context.Cvars.TryGet(ExpandedVariable, out ICVar? cvar) || cvar is null)
        {
            return;
        }

        List<string> keys = [.. cvar.ValueText.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(k => k != key)];
        if (expanded)
        {
            keys.Add(key);
        }

        cvar.TrySetText(string.Join(',', keys), out _);
    }

    /// <summary>A row of a panel, and when it is shown.</summary>
    private sealed record PanelRow(Control Control, RowCondition? ShownWhen, bool Advanced);
}
