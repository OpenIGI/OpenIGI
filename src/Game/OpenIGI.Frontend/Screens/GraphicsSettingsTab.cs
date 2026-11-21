using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Frontend.Settings;
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
        groups.AddRange(SettingsPanelBuilder.Build(rowBuilder, theme, "Graphics", GraphicsMenu.Sections));
        return groups;
    }

    /// <summary>
    /// The preset, across the top of the page: stages a whole preset in the draft, and reads which one the draft amounts to.
    /// </summary>
    /// <remarks>The one control that answers the whole question at once, so it leads, as segments to press rather than a row to step.</remarks>
    private static SettingsGroup BuildPresetBand(SettingsRowBuilder rowBuilder, UiTheme theme, SettingsDraft draft)
    {
        GraphicsPresetRegistry registry = rowBuilder.Context.Presets;
        GraphicsPreset[] presets = Enum.GetValues<GraphicsPreset>();
        return new PresetBand
        {
            Title = "Quality preset",
            Line = "Start here. Changing any setting below switches the preset to Custom.",
            Help = "Sets every graphics quality setting at once, when you apply. Custom is what the settings become as soon as "
                + "you change any of them yourself — it is a reading, not a choice.",
            Captions = [.. presets.Select(static preset => preset.ToString())],
            Choosable = index => presets[index] != GraphicsPreset.Custom,
            Reading = () => Math.Max(0, Array.IndexOf(presets, registry.Recognise(draft.Holds))),
            Choose = index =>
            {
                foreach ((ICVar variable, string value) in registry.ValuesOf(presets[index]))
                {
                    draft.Stage(variable).TrySetText(value, out _);
                }
            },
            Watch = changed =>
            {
                EventHandler handler = (_, _) => changed();
                draft.Changed += handler;
                return new Unsubscribe(() => draft.Changed -= handler);
            },
        }.Build(theme, rowBuilder.Bindings);
    }
}
