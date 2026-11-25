using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>
/// What both of the Leaderboard section's pages -- the matches' boards and the missions' -- show alike at their top: the switch
/// between them, the boards' segmented switches, and the way out to the site.
/// </summary>
public static class LeaderboardModes
{
    /// <summary>The switch between the matches' boards and the missions', where the page has both.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="size">How much room the page has.</param>
    /// <param name="missions">Whether the missions' page is the one it is on.</param>
    /// <param name="show">Switches the page; null where it has one page.</param>
    /// <returns>The switch, or null.</returns>
    public static TabStrip? Switch(UiTheme theme, BoardSize size, bool missions, Action<bool>? show)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (show is null)
        {
            return null;
        }

        TabStrip strip = Segmented(theme, ["Multiplayer", "Single player"], size == BoardSize.Compact);
        strip.SelectedIndex = missions ? 1 : 0;
        strip.Accented = true;
        strip.SelectionChanged += index => show(index == 1);
        return strip;
    }

    /// <summary>A segmented switch in the boards' look: a dark tray, the picked segment filled.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="tabs">The segments.</param>
    /// <param name="compact">Whether it is a phone's.</param>
    /// <returns>The switch.</returns>
    public static TabStrip Segmented(UiTheme theme, IEnumerable<string> tabs, bool compact) => new(theme, tabs)
    {
        SegmentHeight = compact ? 38f : 32f,
        TabPadding = compact ? 12f : 14f,
        TextSize = compact ? 15f : 15f,
        Spacing = 2f,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    /// <summary>The way out to the site: OPEN ON OPENIGI.COM, or a globe alone on a phone.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="compact">Whether it is a phone's.</param>
    /// <param name="open">Opens the site.</param>
    /// <returns>The button.</returns>
    public static BoardButton SiteButton(UiTheme theme, bool compact, Action open)
    {
        ArgumentNullException.ThrowIfNull(open);
        var button = new BoardButton(theme, compact ? string.Empty : "Open on openigi.com", BoardButtonLook.Outline)
        {
            Icon = KitIcon.Globe,
            ButtonHeight = compact ? 44f : 40f,
            ButtonWidth = compact ? 44f : 0f,
            TextSize = 14f,
            Tooltip = "The board on the site, to share it.",
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Activated += _ => open();
        return button;
    }

    /// <summary>A square icon button in the boards' look: refresh, the site.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="icon">The icon.</param>
    /// <param name="tooltip">What it does.</param>
    /// <param name="act">Does it.</param>
    /// <param name="compact">Whether it is a phone's.</param>
    /// <returns>The button.</returns>
    public static BoardButton Square(UiTheme theme, KitIcon icon, string tooltip, Action act, bool compact)
    {
        ArgumentNullException.ThrowIfNull(act);
        float side = compact ? 44f : 40f;
        var button = new BoardButton(theme, string.Empty, BoardButtonLook.Quiet)
        {
            Icon = icon,
            ButtonHeight = side,
            ButtonWidth = side,
            TextSize = 16f,
            Tooltip = tooltip,
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Activated += _ => act();
        return button;
    }

    /// <summary>A key's cap on its own, before the switch it steps.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="key">The key.</param>
    /// <returns>The cap.</returns>
    public static KeyCapLabel Cap(UiTheme theme, string key) => new(theme, key, KeyCapStyle.Footer) { TextSize = 10f, VerticalAlignment = VerticalAlignment.Center };
}
