using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Net.Browsing;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher.Pages;

/// <summary>
/// The launcher's Servers section, laid out as the game's own Multiplayer page and drawn with the same parts
/// (<c>OpenIGI.UI.Kit.Servers</c>): BROWSE, the chosen game's servers and rooms in one panel -- the kinds with their counts,
/// a search, the mode and HIDE FULL; the columns, each ordering the list; a line a server -- with the server picked on the
/// right, its map's picture over its name, players, ping and version, and JOIN, which starts the game at it. HOST is a
/// dedicated server of this build, started from a form.
/// </summary>
/// <remarks>The list is the same one the game's menu shows: the same browser, the same saved and recent servers.</remarks>
internal sealed partial class ServersPage
{
    private const float PanelGap = 24f;
    private const float ButtonText = 17f;
    private const float ButtonTracking = 0.12f;

    private readonly UiTheme _theme;
    private readonly UiTheme _rowTheme;
    private readonly LauncherModel _model;
    private readonly Func<string, UiPicture?> _pictures;
    private readonly TabStrip _tabs;
    private readonly UiElement _browse;
    private readonly Panel _host = new();
    private int _tab;
    private int _shownRevision = -1;
    private string? _shownHosted;
    private bool _shownCanHost;
    private bool _entered;

    /// <summary>Initializes a new instance of the <see cref="ServersPage"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="rowTheme">The theme of the host form's rows.</param>
    /// <param name="model">The launcher's state.</param>
    /// <param name="pictures">Reads a picture file, for the maps' pictures.</param>
    public ServersPage(UiTheme theme, UiTheme rowTheme, LauncherModel model, Func<string, UiPicture?> pictures)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _rowTheme = rowTheme ?? throw new ArgumentNullException(nameof(rowTheme));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _pictures = pictures ?? throw new ArgumentNullException(nameof(pictures));

        // BROWSE and HOST beside the title, as on the game's page.
        _tabs = new TabStrip(theme, ["Browse", "Host"]) { SegmentHeight = 40f, TabPadding = 20f, TextSize = ButtonText, VerticalAlignment = VerticalAlignment.Bottom };
        _tabs.SelectionChanged += index =>
        {
            _tab = index;
            ShowTab();
        };
        Button quick = new(theme, "Quick play")
        {
            Accent = ButtonAccent.Ghost,
            LeadingGlyph = KitIcons.Glyph(KitIcon.Play),
            GlyphSize = 16f,
            TextSize = ButtonText,
            Tracking = ButtonTracking,
            Padding = new Thickness(22f, 0f),
            Height = 48f,
            VerticalAlignment = VerticalAlignment.Bottom,
            Tooltip = "Join the best open game listed: near, with people in it, and room for you.",
        };
        quick.Activated += _ => QuickPlay();

        var head = new Grid { ColumnSpacing = 28f };
        head.RowDefinitions.Add(GridLength.Auto);
        head.ColumnDefinitions.Add(GridLength.Auto);
        head.ColumnDefinitions.Add(GridLength.Auto);
        head.ColumnDefinitions.Add(GridLength.Star);
        head.ColumnDefinitions.Add(GridLength.Auto);
        head.Add(new SectionTitle(theme, "Together", "Multiplayer") { HorizontalAlignment = HorizontalAlignment.Left }, 0, 0);
        head.Add(_tabs, 0, 1);
        head.Add(quick, 0, 3);

        _browse = BuildBrowse();
        var content = new Panel();
        content.AddChild(_browse);
        content.AddChild(_host);

        var page = new Grid { RowSpacing = 24f, Padding = new Thickness(48f, 36f, 48f, 28f) };
        page.ColumnDefinitions.Add(GridLength.Star);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Star);
        page.Add(head, 0, 0);
        page.Add(content, 1, 0);
        Root = page;
        ShowTab();
    }

    /// <summary>Gets the page.</summary>
    public UiElement Root { get; }

    /// <summary>Takes in the browser's news and the hosted server's; asks after the servers the first time the page is shown.</summary>
    /// <param name="shown">Whether the page is the one up.</param>
    public void Update(bool shown)
    {
        if (shown && !_entered)
        {
            _entered = true;
            _model.RefreshServers();
        }

        if (_model.ServersRevision != _shownRevision)
        {
            _shownRevision = _model.ServersRevision;
            RefreshBrowse();
        }

        if (_model.HostedServerStatus != _shownHosted || _model.CanHostServer != _shownCanHost || _host.Children.Count == 0)
        {
            _shownHosted = _model.HostedServerStatus;
            _shownCanHost = _model.CanHostServer;
            BuildHost();
        }
    }

    /// <summary>Shows BROWSE (0) or HOST (1).</summary>
    /// <param name="tab">The tab.</param>
    public void SelectTab(int tab)
    {
        _tab = Math.Clamp(tab, 0, 1);
        ShowTab();
    }

    // Shows the tab picked and hides the other.
    private void ShowTab()
    {
        _tabs.SelectedIndex = _tab;
        _browse.Visibility = _tab == 0 ? Visibility.Visible : Visibility.Collapsed;
        _host.Visibility = _tab == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void QuickPlay()
    {
        if (BrowserFilter.QuickPlay(_model.Servers, _filter.Mode) is { } best)
        {
            _model.JoinServer(best);
        }
        else
        {
            _note = "No open game to join right now. Refresh, or host one.";
            RefreshBrowse();
        }
    }

    // A label for a body of text on the page.
    private Label Body(string text, Bgra32 colour, float size = 16f) =>
        new(_theme.Body, text, size, colour) { Wrap = true, Margin = new Thickness(0f, 2f, 0f, 2f) };

    // A server's map, as the list names it: its mission's number.
    private static string MapName(BrowserServer server) =>
        MapNumber(server) is > 0 and int number ? string.Create(CultureInfo.InvariantCulture, $"Mission {number}") : server.Info.Map;

    private static int MapNumber(BrowserServer server) =>
        int.TryParse(server.Info.Map, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number : 0;
}
