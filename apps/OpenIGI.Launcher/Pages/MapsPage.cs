using System.Globalization;
using OpenIGI.Releases.Maps;
using OpenIGI.UI;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher.Pages;

/// <summary>
/// The launcher's Maps section: the selected game's community maps, a line each -- its name and author, its rating, downloads
/// and size -- downloaded into the game's own maps folder (or updated, or removed) to be played from the game's Maps page.
/// </summary>
/// <remarks>The same maps, the same folder and the same server the game's page uses; publishing is the editor's.</remarks>
internal sealed class MapsPage
{
    private readonly UiTheme _theme;
    private readonly LauncherModel _model;
    private readonly StackPanel _rows = new() { Orientation = Orientation.Vertical, Spacing = 8f };
    private readonly Label _summary;
    private int _shownRevision = -1;
    private bool _entered;

    public MapsPage(UiTheme theme, LauncherModel model)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _summary = new Label(theme.Body, string.Empty, 15f, theme.TextMuted) { Wrap = true };

        Button refresh = new(theme, "Refresh") { Accent = ButtonAccent.Ghost };
        refresh.Activated += _ => model.RefreshMaps(again: true);
        var tools = new FlexPanel { Gap = 18f };
        tools.Add(_summary, grow: 1f);
        tools.Add(refresh);

        var scroll = new ScrollView(theme);
        scroll.SetContent(_rows);
        var page = new Grid { RowSpacing = 24f, Padding = new Thickness(48f, 36f, 48f, 28f) };
        page.ColumnDefinitions.Add(GridLength.Star);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Star);
        page.Add(new SectionTitle(theme, "Community", "Maps") { HorizontalAlignment = HorizontalAlignment.Left }, 0, 0);
        page.Add(tools, 1, 0);
        page.Add(scroll, 2, 0);
        Root = page;
    }

    /// <summary>Gets the page.</summary>
    public UiElement Root { get; }

    /// <summary>Asks after the maps the first time the page is shown, and rebuilds the rows when the model changed.</summary>
    /// <param name="shown">Whether the page is the one up.</param>
    public void Update(bool shown)
    {
        if (shown && !_entered)
        {
            _entered = true;
            _model.RefreshMaps();
        }

        if (_model.Revision != _shownRevision)
        {
            _shownRevision = _model.Revision;
            BuildRows();
        }
    }

    private void BuildRows()
    {
        _rows.ClearChildren();
        IReadOnlyList<(MapListing Map, int? Installed)> maps = _model.Maps;
        int installed = maps.Count(static m => m.Installed is not null);
        _summary.Text = _model.MapsStatus.Length > 0 ? _model.MapsStatus
            : maps.Count == 0 ? "No maps are published for this game yet. Make one in the editor and publish it from its File menu."
            : string.Create(CultureInfo.InvariantCulture, $"{maps.Count} {(maps.Count == 1 ? "map" : "maps")}, {installed} installed. Play them from the game's Maps page.");
        // The screen plays its controls' entrance once, as it opens; rows that come later play their own, or stay unseen.
        int index = 0;
        foreach ((MapListing map, int? mine) in maps)
        {
            GlassPanel row = Row(map, mine);
            _rows.AddChild(row);
            foreach (Control control in row.Descendants().OfType<Control>())
            {
                control.PlayEnter(MathF.Min(index++ * 0.03f, 0.3f));
            }
        }
    }

    private GlassPanel Row(MapListing map, int? installed)
    {
        bool update = installed is { } version && map.Version > version;
        string rating = map.Ratings > 0 ? string.Create(CultureInfo.InvariantCulture, $"{map.Rating:0.0} STARS ({map.Ratings})") : "NOT RATED YET";
        var text = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2f, VerticalAlignment = VerticalAlignment.Center };
        text.AddChild(new Label(_theme.Heading, map.Title, 18f, _theme.TextBright));
        text.AddChild(new Label(_theme.Mono, string.Create(CultureInfo.InvariantCulture, $"BY {map.Author.ToUpperInvariant()}  ·  V{map.Version}  ·  {rating}  ·  {map.Downloads} DOWNLOADS"), 12f, _theme.TextMuted));
        if (map.Summary.Length > 0)
        {
            text.AddChild(new Label(_theme.Body, map.Summary, 14f, _theme.TextMuted) { Wrap = true });
        }

        bool busy = _model.MapDownloading is not null;
        string action = _model.MapDownloading == map.Id ? "Downloading…" : update ? "Update" : installed is null ? "Download" : "Installed";
        Button get = new(_theme, action) { Accent = ButtonAccent.Solid, IsEnabled = !busy && (installed is null || update), VerticalAlignment = VerticalAlignment.Center };
        get.Activated += _ => _model.InstallMap(map.Id);
        Button remove = new(_theme, "Remove") { Accent = ButtonAccent.Ghost, IsEnabled = installed is not null && !busy, VerticalAlignment = VerticalAlignment.Center };
        remove.Activated += _ => _model.RemoveMap(map.Id);

        var line = new Grid { ColumnSpacing = 16f };
        line.RowDefinitions.Add(GridLength.Auto);
        line.ColumnDefinitions.Add(GridLength.Star);
        line.ColumnDefinitions.Add(GridLength.Absolute(90f));
        line.ColumnDefinitions.Add(GridLength.Auto);
        line.ColumnDefinitions.Add(GridLength.Auto);
        line.Add(text, 0, 0);
        line.Add(new Label(_theme.Mono, string.Create(CultureInfo.InvariantCulture, $"{map.Size / (1024.0 * 1024.0):0.0} MB"), 15f, _theme.TextBright) { VerticalAlignment = VerticalAlignment.Center }, 0, 1);
        line.Add(remove, 0, 2);
        line.Add(get, 0, 3);

        var panel = new GlassPanel(_theme) { Tint = UiPalette.Ink.WithAlpha(170), Padding = new Thickness(20f, 12f) };
        panel.AddChild(line);
        return panel;
    }
}
