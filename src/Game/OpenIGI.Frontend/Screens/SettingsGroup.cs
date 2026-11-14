using OpenIGI.UI;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>A titled run of rows: one glass panel's worth, or the start of one that continues.</summary>
/// <param name="Title">The heading the panel shows.</param>
/// <param name="Rows">The rows under it, in reading order.</param>
internal sealed record SettingsGroup(string Title, List<Control> Rows)
{
    /// <summary>Gets what the group is shown as when it is not a panel of rows: a band across the top of the page, over the columns.</summary>
    /// <remarks>Its <see cref="Rows"/> are the controls in it, for the keys to reach.</remarks>
    public UiElement? Banner { get; init; }

    /// <summary>A group of these rows under this title.</summary>
    public static SettingsGroup Of(string title, IEnumerable<Control> rows) => new(title, [.. rows]);
}
