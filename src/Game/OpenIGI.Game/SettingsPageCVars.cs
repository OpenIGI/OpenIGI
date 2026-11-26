using OpenIGI.Core.Configuration;

namespace OpenIGI.Game;

/// <summary>What the settings page remembers about how the player left it.</summary>
/// <remarks>
/// Declared with the game's other variables, not in the frontend, so the value is registered before the settings file
/// is read and survives a restart like any other setting.
/// </remarks>
[CVarContainer]
public static class SettingsPageCVars
{
    /// <summary>The panels whose Advanced rows the player has opened, as their keys joined by commas.</summary>
    public static readonly StringCVar ExpandedPanels = CVar.Text(
        "ui_settings_expanded",
        string.Empty,
        CVarOptions.Archive | CVarOptions.Hidden,
        "The settings page's panels whose Advanced rows are open, as tab/panel keys joined by commas.");
}
