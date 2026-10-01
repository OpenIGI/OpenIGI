using OpenIGI.UI;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher.Pages;

/// <summary>A section that exists in the design but not yet in the launcher: its title over an empty panel.</summary>
internal static class PlaceholderPage
{
    public static UiElement Build(UiTheme theme, string eyebrow, string title, string note)
    {
        var panel = new GlassPanel(theme) { Tint = UiPalette.Ink.WithAlpha(168) };
        panel.AddChild(new Text(theme.Mono, note, 15f, theme.TextDim, 0.2f, uppercase: true)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var page = new Grid { RowSpacing = 24f, Padding = new Thickness(48f, 36f, 48f, 28f) };
        page.ColumnDefinitions.Add(GridLength.Star);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Star);
        page.Add(new SectionTitle(theme, eyebrow, title) { HorizontalAlignment = HorizontalAlignment.Left }, 0, 0);
        page.Add(panel, 1, 0);
        return page;
    }
}
