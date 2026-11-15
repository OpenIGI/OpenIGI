using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Ai;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// Community maps: the ones installed here and those published for this game, in a list down the left; the chosen one's name,
/// author and standing on the right, with Download (or Update) and Play, a rating, and Remove and Report.
/// </summary>
/// <remarks>
/// Laid out as the campaign is. What is published is fetched as the page opens; everything that goes to the server goes off
/// the frame, and the page says what came of it.
/// </remarks>
public sealed class MapsScreen : ShellScreen
{
    private static readonly string[] Difficulties = AiDifficultyNames.All;
    private static readonly string[] Ratings = ["Not rated", "1 star", "2 stars", "3 stars", "4 stars", "5 stars"];

    private readonly List<ListCard> _cards = [];
    private IReadOnlyList<ShellMap> _maps = [];
    private string? _selectedId;
    private Task<string?>? _busy;
    private string _busyWith = string.Empty;
    private string _status = string.Empty;
    private bool _refreshed;

    private Label? _title;
    private Label? _byline;
    private Label? _summary;
    private Label? _said;
    private PrimaryAction? _primary;
    private OptionCycler? _difficulty;
    private OptionCycler? _rating;
    private ActionRow? _remove;
    private ActionRow? _report;

    /// <summary>Initializes a new instance of the <see cref="MapsScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    public MapsScreen(ShellContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Maps;

    /// <inheritdoc/>
    protected override IReadOnlyList<KeyHint> Hints => [new("↑ ↓", "Map"), new("← →", "Option"), new("Esc", "Back")];

    /// <inheritdoc/>
    protected override Control? InitialFocus => _cards.Find(static c => c.Selected) ?? (_cards.Count > 0 ? _cards[0] : null);

    private IShellMaps? Maps => Context.Host.Maps;

    private ShellMap? Selected => _maps.FirstOrDefault(m => m.Id == _selectedId);

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        UiTheme theme = Context.Theme;
        _cards.Clear();
        if (Maps is not { } maps)
        {
            return new Label(theme.Body, "Community maps are not offered in this build.", 22f, theme.TextMuted) { Margin = new Thickness(SideMargin, 56f), VerticalAlignment = VerticalAlignment.Top };
        }

        _maps = maps.Maps();
        _selectedId ??= _maps.Count > 0 ? _maps[0].Id : null;
        bool compact = Size == ShellSize.Compact;
        float listWidth = Size switch { ShellSize.Wide => 600f, ShellSize.Medium => 520f, _ => 268f };
        float gap = Size switch { ShellSize.Wide => 48f, ShellSize.Medium => 38f, _ => 16f };
        float top = Size switch { ShellSize.Wide => 40f, ShellSize.Medium => 32f, _ => 10f };
        var page = new Canvas();
        page.Add(BuildList(theme, compact), new Anchor(UiVec2.Zero, new UiVec2(0f, 1f), new Thickness(SideMargin, top, -(SideMargin + listWidth), top)));
        // A wrapped line is measured at the width it is given: the detail column's.
        float canvas = ShellSizes.Canvas(context).X;
        float detailWidth = MathF.Max(160f, canvas - (SideMargin * 2f) - listWidth - gap - (compact ? RailWidth + 10f : 0f));
        UiElement detail = BuildDetail(theme, compact, detailWidth);
        if (compact)
        {
            var scroll = new ScrollView(theme);
            scroll.SetContent(detail);
            detail = scroll;
        }

        page.Add(detail, new Anchor(UiVec2.Zero, UiVec2.One, new Thickness(SideMargin + listWidth + gap, top, SideMargin, top)));
        Refresh();
        return page;
    }

    /// <inheritdoc/>
    protected override void OnEnter()
    {
        // What is published, fetched once as the page first opens.
        if (!_refreshed && Maps is { } maps)
        {
            _refreshed = true;
            Start("Fetching the maps", maps.RefreshAsync());
        }
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        if (Focus?.Focused is ListCard focused && _cards.IndexOf(focused) is var index and >= 0 && index < _maps.Count && _maps[index].Id != _selectedId)
        {
            Choose(_maps[index].Id);
        }

        if (_busy is { IsCompleted: true } done)
        {
            _busy = null;
            string? problem = done.IsCompletedSuccessfully ? done.Result : done.Exception?.GetBaseException().Message;
            _status = problem ?? string.Empty;
            Invalidate();
        }
    }

    private GlassPanel BuildList(UiTheme theme, bool compact)
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Star);
        layout.Add(new GroupHeader(theme, "Community maps") { Margin = compact ? new Thickness(16f, 12f, 16f, 4f) : new Thickness(20f, 28f, 20f, 14f) }, 0, 0);

        var rows = new StackPanel { Orientation = Orientation.Vertical };
        if (_maps.Count == 0)
        {
            rows.AddChild(new Label(theme.Body, _busy is not null ? "Fetching the maps…" : "None yet. Make one in the editor and publish it from its File menu.", compact ? 15f : 17f, theme.TextMuted) { Wrap = true, Margin = new Thickness(20f, 8f) });
        }

        foreach (ShellMap map in _maps)
        {
            string badge = map.Rating > 0 ? map.Rating.ToString("0.0", CultureInfo.InvariantCulture) : "—";
            var card = new ListCard(theme, badge, map.Title)
            {
                Selected = map.Id == _selectedId,
                RowHeight = compact ? 52f : 72f,
                TitleSize = compact ? 17f : 24f,
                Tag = map.HasUpdate ? "UPDATE" : map.Installed ? "INSTALLED" : string.Empty,
                TagColor = map.HasUpdate ? theme.Accent : theme.TextSecondary,
            };
            string id = map.Id;
            card.Activated += _ => Choose(id);
            _cards.Add(card);
            rows.AddChild(card);
        }

        var scroll = new ScrollView(theme);
        scroll.SetContent(rows);
        layout.Add(scroll, 1, 0);
        var panel = new GlassPanel(theme) { Tint = Bgra32.FromRgba(12, 14, 15, compact ? (byte)235 : (byte)199), Border = UiPalette.White(0.12f) };
        panel.AddChild(layout);
        return panel;
    }

    private StackPanel BuildDetail(UiTheme theme, bool compact, float width)
    {
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = compact ? 10f : 24f, VerticalAlignment = compact ? VerticalAlignment.Top : VerticalAlignment.Bottom };
        _title = new Label(theme.Heading, string.Empty, compact ? 30f : 64f, theme.Text) { Wrap = true, Width = width };
        _byline = new Label(theme.Mono, string.Empty, compact ? 13f : 15f, theme.TextSecondary) { Wrap = true, Width = width };
        _summary = new Label(theme.Body, string.Empty, compact ? 15f : 19f, theme.TextSecondary) { Wrap = true, Width = width };
        _said = new Label(theme.Body, string.Empty, compact ? 14f : 16f, theme.Accent) { Wrap = true, Width = width };
        column.AddChild(_title);
        column.AddChild(_byline);
        column.AddChild(_summary);

        // Rated, removed and reported, in a panel as settings rows are.
        _rating = new OptionCycler(theme, "Your rating", Ratings, 0) { Inline = true, Height = compact ? 48f : 56f };
        _rating.SelectionChanged += stars =>
        {
            if (stars > 0 && Selected is { } map && Maps is { } maps)
            {
                Start($"Rating {map.Title}", maps.RateAsync(map.Id, stars));
            }
        };
        _remove = new ActionRow(theme, "Remove it from this machine", "Remove");
        _remove.Activated += _ =>
        {
            if (Selected is { Installed: true } map && Maps is { } maps)
            {
                maps.Remove(map.Id);
                _status = $"Removed {map.Title}.";
                Invalidate();
            }
        };
        _report = new ActionRow(theme, "Report it as broken or not right to have", "Report");
        _report.Activated += _ =>
        {
            if (Selected is { } map && Maps is { } maps)
            {
                Start($"Reporting {map.Title}", maps.ReportAsync(map.Id, "Reported from the game's Maps page."));
            }
        };
        var rows = new StackPanel { Orientation = Orientation.Vertical };
        rows.AddChild(_rating);
        rows.AddChild(_remove);
        rows.AddChild(_report);
        var panel = new GlassPanel(theme) { Tint = Bgra32.FromRgba(12, 14, 15, compact ? (byte)235 : (byte)199), Border = UiPalette.White(0.12f), Padding = new Thickness(8f, 6f) };
        panel.AddChild(rows);
        column.AddChild(panel);
        column.AddChild(_said);

        // Download or update, then Play, at a difficulty.
        _difficulty = new OptionCycler(theme, "Difficulty", Difficulties, Math.Clamp(Context.Config.Active?.Difficulty ?? 1, 0, Difficulties.Length - 1));
        _primary = new PrimaryAction(theme, "Play") { Key = Context.Host.TouchScreen || compact ? string.Empty : "Enter" };
        _primary.Activated += _ => Act();
        var actions = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 12f, Height = compact ? 52f : 88f };
        _difficulty.Width = compact ? 200f : 232f;
        actions.Add(_difficulty);
        actions.Add(new Panel(), grow: 1f);
        _primary.Width = compact ? 220f : 380f;
        actions.Add(_primary);
        column.AddChild(actions);
        return column;
    }

    // Play an installed map that is up to date; otherwise fetch it first.
    private void Act()
    {
        if (Selected is not { } map || Maps is not { } maps || _busy is not null)
        {
            return;
        }

        if (map.Installed && !map.HasUpdate)
        {
            maps.Play(map.Id, _difficulty?.SelectedIndex ?? 1);
            return;
        }

        Start($"Downloading {map.Title}", maps.InstallAsync(map.Id));
    }

    private void Start(string what, Task<string?> task)
    {
        _busy = task;
        _busyWith = what;
        _status = string.Empty;
        Refresh();
    }

    private void Choose(string id)
    {
        _selectedId = id;
        foreach ((ListCard card, ShellMap map) in _cards.Zip(_maps))
        {
            card.Selected = map.Id == id;
        }

        _status = string.Empty;
        Refresh();
    }

    // The chosen map's words and what can be done with it, as things stand.
    private void Refresh()
    {
        ShellMap? map = Selected;
        if (_title is null || _byline is null || _summary is null || _said is null || _primary is null || _remove is null || _report is null || _rating is null)
        {
            return;
        }

        _title.Text = map?.Title.ToUpperInvariant() ?? (_maps.Count == 0 ? "NO MAPS YET" : string.Empty);
        _byline.Text = map is null ? string.Empty : string.Create(
            CultureInfo.InvariantCulture,
            $"BY {map.Author.ToUpperInvariant()} · V{map.Version} · {(map.Ratings > 0 ? $"{map.Rating:0.0} STARS ({map.Ratings})" : "NOT RATED YET")} · {map.Downloads} DOWNLOADS · {map.Size / (1024.0 * 1024.0):0.0} MB");
        _summary.Text = map?.Summary ?? string.Empty;
        _said.Text = _busy is not null ? _busyWith + "…" : _status;
        _primary.IsEnabled = map is not null && _busy is null && (map.Installed || map.Online);
        _primary.Title = map is { Installed: true, HasUpdate: false } ? "Play" : map?.HasUpdate == true ? "Update" : "Download";
        _primary.Subtitle = map is null ? string.Empty
            : map.Installed && !map.HasUpdate ? $"COMMUNITY MAP · {Difficulties[_difficulty?.SelectedIndex ?? 1].ToUpperInvariant()}"
            : string.Create(CultureInfo.InvariantCulture, $"{map.Size / (1024.0 * 1024.0):0.0} MB");
        _remove.IsEnabled = map?.Installed == true && _busy is null;
        _report.IsEnabled = map?.Online == true && _busy is null;
        _rating.IsEnabled = map?.Online == true && _busy is null;
    }
}
