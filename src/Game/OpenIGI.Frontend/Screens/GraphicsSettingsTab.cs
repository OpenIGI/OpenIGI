using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Frontend.Settings;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>The settings page's Graphics category: the preset first, then a group per subject.</summary>
/// <remarks>
/// A row's tooltip still names the variables it drives, because that is the handle somebody needs to go
/// further with it from the console, and the console is the only advanced interface there is.
/// </remarks>
internal static class GraphicsSettingsTab
{
    /// <summary>Builds the category's groups.</summary>
    /// <param name="rowBuilder">What the rows are built with; its lookup goes through the draft.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="draft">The draft the preset row stages whole presets in.</param>
    public static List<SettingsGroup> Build(SettingsRowBuilder rowBuilder, UiTheme theme, SettingsDraft draft)
    {
        List<SettingsGroup> groups = [BuildPresetBand(rowBuilder, theme, draft)];

        foreach (GraphicsSection section in GraphicsMenu.Sections)
        {
            var rows = new List<Control>();
            foreach (QualityGroup group in section.Groups)
            {
                if (QualityBinding.CreateControl(theme, rowBuilder.Find, group, rowBuilder.Bindings) is not { } control)
                {
                    continue;
                }

                // A group can write a variable that only takes effect on the next level load. The player has
                // to be told which, and the help line shows only the tooltip's first line, so the note goes
                // there rather than at the end.
                string restart = rowBuilder.NamesRequiringRestart(group.Variables) ? "   [takes effect on the next level]" : string.Empty;
                string unavailable = rowBuilder.Unavailable(group.Variables);
                control.IsEnabled = unavailable.Length == 0;
                control.Tooltip =
                    $"{unavailable}{group.Help}{restart}\n\nSets: {string.Join(", ", group.Variables)}\n\nReachable from the console by name.";
                rows.Add(control);
                rowBuilder.TrackForReset(group.Variables);
            }

            rows.AddRange(rowBuilder.SettingRows(theme, section.Settings));
            groups.Add(SettingsGroup.Of(section.Title, rows));
        }

        return groups;
    }

    /// <summary>
    /// The preset, across the top of the page: stages a whole preset in the draft, and reads which one the draft amounts to.
    /// </summary>
    /// <remarks>The one control that answers the whole question at once, so it leads, as segments to press rather than a row to step.</remarks>
    private static SettingsGroup BuildPresetBand(SettingsRowBuilder rowBuilder, UiTheme theme, SettingsDraft draft)
    {
        GraphicsPresetRegistry registry = rowBuilder.Context.Presets;
        string[] presetNames = Enum.GetNames<GraphicsPreset>();
        int Shown() => Math.Max(0, Array.IndexOf(presetNames, registry.Recognise(draft.Holds).ToString()));

        bool compact = ShellSizes.IsCompact(theme);
        const string Help = "Sets every graphics variable at once, when you apply. Custom is what the settings become "
            + "as soon as you change any of them yourself — it is a reading, not a choice.";

        // Segments to press where there is room for five across; a row that steps where there is not.
        Control control;
        Action<int> show;
        Action<Action<int>> onPicked;
        if (compact)
        {
            var row = new Dropdown(theme, "Preset", presetNames, Shown()) { Tooltip = Help };
            control = row;
            show = index => row.SelectedIndex = index;
            onPicked = handler => row.SelectionChanged += handler;
        }
        else
        {
            var strip = new TabStrip(theme, presetNames)
            {
                SegmentHeight = 36f,
                TabPadding = 18f,
                TextSize = 17f,
                VerticalAlignment = VerticalAlignment.Center,
                Tooltip = Help,
            };
            strip.SelectedIndex = Shown();
            control = strip;
            show = index => strip.SelectedIndex = index;
            onPicked = handler => strip.SelectionChanged += handler;
        }

        // Choosing a preset stages its values, each of which the draft reports; the band reads the preset back
        // once, at the end, rather than at every one of them.
        bool updating = false;
        void ShowPreset()
        {
            updating = true;
            try
            {
                show(Shown());
            }
            finally
            {
                updating = false;
            }
        }

        onPicked(i =>
        {
            if (updating || !Enum.TryParse(presetNames[i], out GraphicsPreset preset))
            {
                return;
            }

            updating = true;
            try
            {
                foreach ((ICVar variable, string value) in registry.ValuesOf(preset))
                {
                    draft.Stage(variable).TrySetText(value, out _);
                }
            }
            finally
            {
                updating = false;
            }

            ShowPreset();
        });

        EventHandler changed = (_, _) =>
        {
            if (!updating)
            {
                ShowPreset();
            }
        };

        draft.Changed += changed;
        rowBuilder.Bindings.Add(new Unsubscribe(() => draft.Changed -= changed));

        if (compact)
        {
            return SettingsGroup.Of("General", [control]);
        }

        var words = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4f, VerticalAlignment = VerticalAlignment.Center };
        words.AddChild(new Label(theme.BodyStrong, "Quality preset", theme.BodyTextSize, theme.Text));
        words.AddChild(new Label(theme.Body, "Start here. Changing any setting below switches the preset to Custom.", 15f, theme.TextSecondary) { Wrap = true });

        var inner = new Grid { ColumnSpacing = 24f };
        inner.RowDefinitions.Add(GridLength.Auto);
        inner.ColumnDefinitions.Add(GridLength.Star);
        inner.ColumnDefinitions.Add(GridLength.Auto);
        inner.Add(words, 0, 0);
        inner.Add(control, 0, 1);

        var band = new OpenIGI.UI.Kit.Widgets.GlassPanel(theme)
        {
            Tint = OpenIGI.Core.Graphics.Bgra32.FromRgba(12, 14, 15, 199),
            Border = UiPalette.White(0.12f),
            Padding = new Thickness(20f, 14f),
        };
        band.AddChild(inner);
        return new SettingsGroup("Quality preset", [control]) { Banner = band };
    }
}
