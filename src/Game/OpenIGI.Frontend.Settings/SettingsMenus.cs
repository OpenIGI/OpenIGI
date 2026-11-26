namespace OpenIGI.Frontend.Settings;

/// <summary>Every page of the settings screen that is written out as panels, together.</summary>
public static class SettingsMenus
{
    /// <summary>Gets each written-out page by its name, with its panels, in the order the screen shows them.</summary>
    public static IReadOnlyList<(string Page, IReadOnlyList<SettingsPanel> Panels)> Pages { get; } =
    [
        ("Graphics", GraphicsMenu.Sections),
        ("Display", DisplayMenu.Panels),
        ("Audio", AudioMenu.Panels),
        ("Gameplay", GameplayMenu.Panels),
        ("Interface", InterfaceMenu.Panels),
    ];

    /// <summary>Gets every single-variable row on those pages, in the order they are shown.</summary>
    public static IReadOnlyList<SettingRow> Rows { get; } =
        [.. Pages.SelectMany(page => page.Panels).SelectMany(panel => panel.Rows)];

    /// <summary>Gets every variable the pages deliberately do not show, from each page's own list.</summary>
    public static IReadOnlyList<string> ConsoleOnly { get; } =
        [.. GraphicsMenu.ConsoleOnly, .. AudioMenu.ConsoleOnly, .. GameplayMenu.ConsoleOnly];
}
