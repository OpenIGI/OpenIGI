using OpenIGI.Frontend.Settings;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>The settings page's Gameplay category, with the interface and effect options that are not quality levels.</summary>
internal static class GameplaySettingsTab
{
    /// <summary>Builds the category's groups.</summary>
    /// <param name="rowBuilder">What the rows are built with.</param>
    /// <param name="theme">The theme.</param>
    public static List<SettingsGroup> Build(SettingsRowBuilder rowBuilder, UiTheme theme)
    {
        List<SettingsGroup> groups = CvarGroupBuilder.CvarGroups(rowBuilder, theme, CvarGroupBuilder.GameplayNames(rowBuilder.Context.Cvars), "General");
        groups.Add(SettingsGroup.Of("Interface and effects", rowBuilder.SettingRows(theme, GraphicsMenu.InterfaceSettings)));
        return groups;
    }
}
