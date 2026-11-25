using System.Globalization;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <content>
/// The board: the switches along the top (the boards, the game, refresh, the site); the title and the scoring beside the
/// season's card; the metric and the period; the podium over the ranked rows, the player's own row pinned under them; and, on a
/// wide page, the panel on the selected row beside them. While it is read, its shape; empty, the podium's places open and what
/// counts; unreachable, the way to try again.
/// </content>
public sealed partial class LeaderboardView
{
    private const int SkeletonRows = 8;
    private const int CutOff = 10;

    private Grid? _boardPage;
    private TabStrip? _boardGames;
    private SectionTitle? _title;
    private Label? _scoring;
    private SeasonCard? _season;
    private BoardChip? _daysChip;
    private StackPanel? _periodHost;
    private BoardCycler? _period;
    private TabStrip? _metrics;
    private Label? _standing;
    private Panel? _body;
    private Panel? _podium;
    private Panel? _columns;
    private StackPanel? _list;
    private ScrollView? _listScroll;
    private StackPanel? _pinned;
    private Panel? _side;
    private Grid? _boardColumn;
    private Panel? _failed;

    // What the period cycler offers, by the period each asks for; and what the parts were made from.
    private List<string> _periods = [];
    private string _builtList = string.Empty;
    private string _builtPick = string.Empty;
    private string _builtPinned = string.Empty;
    private string _builtSide = string.Empty;

    private Grid BuildBoard()
    {
        _boardGames = GameSwitch();
        _title = new SectionTitle(_theme, string.Empty, "Leaderboard") { TitleSize = 64f, HorizontalAlignment = HorizontalAlignment.Left };
        _scoring = new Label(_theme.Body, string.Empty, 15f, _theme.TextSecondary) { Wrap = true };
        _season = new SeasonCard(_theme) { Small = _size == BoardSize.Medium, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Right };
        _daysChip = new BoardChip(_theme) { Icon = KitIcon.History, VerticalAlignment = VerticalAlignment.Center };
        _periodHost = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _metrics = Segmented(LeaderboardMetric.All.Select(static metric => metric.Label));
        _metrics.SelectionChanged += index => Feed.ShowMetric(LeaderboardMetric.All[index]);
        _standing = new Label(_theme.Mono, string.Empty, 12f, _theme.TextSecondary) { VerticalAlignment = VerticalAlignment.Center };

        // Along the top: the boards' switch at the left; the game, refresh and the site at the right.
        var top = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 10f };
        if (ModeSwitch(missions: false) is { } mode)
        {
            top.Add(mode);
        }

        top.Add(new Panel(), grow: 1f);
        if (_boardGames is not null)
        {
            top.Add(_boardGames);
        }

        if (Compact)
        {
            top.Add(_daysChip);
        }

        top.Add(LeaderboardModes.Square(_theme, KitIcon.Refresh, "Read the board again now.", Refresh, Compact));
        if (_source.CanOpenLinks)
        {
            top.Add(SiteButton());
        }

        // The metric and the period.
        var tools = new FlexPanel { Orientation = Orientation.Horizontal, Gap = Compact ? 10f : 28f };
        var metric = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 10f };
        if (!Compact)
        {
            metric.Add(LeaderboardModes.Cap(_theme, "Tab"));
        }

        metric.Add(_metrics);
        tools.Add(metric);
        if (!Compact)
        {
            tools.Add(_periodHost);
        }

        tools.Add(new Panel(), grow: 1f);
        if (Compact)
        {
            tools.Add(_periodHost);
        }
        else
        {
            tools.Add(_standing);
        }

        _podium = new Panel();
        _columns = new Panel();
        _list = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0f, Margin = new Thickness(0f, 0f, 10f, 0f) };
        _listScroll = new ScrollView(_theme);
        _listScroll.SetContent(_list);
        _pinned = new StackPanel { Orientation = Orientation.Vertical };
        _side = new Panel();
        _failed = new Panel { Visibility = Visibility.Collapsed };

        var page = new Grid { RowSpacing = Compact ? 8f : 18f };
        page.ColumnDefinitions.Add(GridLength.Star);
        _body = new Panel();
        if (Compact)
        {
            // A phone on its side: the top three in a column beside the list, the player's own row under both.
            var body = new Grid { ColumnSpacing = 10f };
            body.ColumnDefinitions.Add(GridLength.Absolute(220f));
            body.ColumnDefinitions.Add(GridLength.Star);
            body.RowDefinitions.Add(GridLength.Star);
            body.Add(_podium, 0, 0);
            body.Add(_listScroll, 0, 1);
            _body.AddChild(body);
            _body.AddChild(_failed);
            foreach (GridLength height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Star, GridLength.Auto })
            {
                page.RowDefinitions.Add(height);
            }

            page.Add(top, 0, 0);
            page.Add(tools, 1, 0);
            page.Add(_body, 2, 0);
            page.Add(_pinned, 3, 0);
        }
        else
        {
            var heading = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f, VerticalAlignment = VerticalAlignment.Bottom };
            heading.AddChild(_title);
            heading.AddChild(_scoring);
            var titleRow = new Grid { ColumnSpacing = 28f };
            titleRow.ColumnDefinitions.Add(GridLength.Star);
            titleRow.ColumnDefinitions.Add(GridLength.Auto);
            titleRow.RowDefinitions.Add(GridLength.Auto);
            titleRow.Add(heading, 0, 0);
            titleRow.Add(_season, 0, 1);

            // The podium over the headings, the rows scrolling under them, the player's own row at the foot.
            _boardColumn = new Grid { RowSpacing = 12f };
            _boardColumn.ColumnDefinitions.Add(GridLength.Star);
            foreach (GridLength height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Star, GridLength.Auto })
            {
                _boardColumn.RowDefinitions.Add(height);
            }

            var rows = new Grid();
            rows.ColumnDefinitions.Add(GridLength.Star);
            rows.RowDefinitions.Add(GridLength.Auto);
            rows.RowDefinitions.Add(GridLength.Star);
            rows.Add(_columns, 0, 0);
            rows.Add(_listScroll, 1, 0);
            _boardColumn.Add(_podium, 0, 0);
            _boardColumn.Add(rows, 2, 0);
            _boardColumn.Add(_pinned, 3, 0);

            var body = new Grid { ColumnSpacing = 28f };
            body.ColumnDefinitions.Add(GridLength.Star);
            if (Wide)
            {
                body.ColumnDefinitions.Add(GridLength.Absolute(520f));
            }

            body.RowDefinitions.Add(GridLength.Star);
            body.Add(_boardColumn, 0, 0);
            if (Wide)
            {
                body.Add(_side, 0, 1);
            }

            _body.AddChild(body);
            _body.AddChild(_failed);
            foreach (GridLength height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Star })
            {
                page.RowDefinitions.Add(height);
            }

            page.Add(top, 0, 0);
            page.Add(titleRow, 1, 0);
            page.Add(tools, 2, 0);
            page.Add(_body, 3, 0);
        }

        _boardPage = page;
        return page;
    }

    private void SyncBoard(bool entrance)
    {
        Leaderboard? board = Feed.Board;
        string game = GameName;
        string period = Feed.Period;
        bool current = period == LeaderboardSeasons.Current;
        bool failed = board is null && Feed.BoardState == FeedState.Failed;
        string season = board?.Season is { Length: > 0 } read ? read : LeaderboardSeasons.IsMonth(period) ? period : SeasonNow();
        _title!.Eyebrow = $"{game} · Multiplayer";
        _scoring!.Text = "Every match on an official server counts: a kill 10, an assist 5, an objective 25, a win 50.";
        _season!.State = failed ? SeasonCardState.Unavailable
            : board is null ? SeasonCardState.Loading
            : period == LeaderboardSeasons.AllTime ? SeasonCardState.AllTime
            : current ? SeasonCardState.Current
            : SeasonCardState.Ended;
        _season.Season = season;
        _season.Now = DateTimeOffset.UtcNow;
        int days = LeaderboardSeasons.DaysLeft(DateTimeOffset.UtcNow);
        _daysChip!.Text = string.Create(CultureInfo.InvariantCulture, $"{days} {(days == 1 ? "DAY" : "DAYS")} LEFT");
        _daysChip.Visibility = current && !failed ? Visibility.Visible : Visibility.Collapsed;

        SyncPeriods(board?.Seasons);
        int metric = Math.Max(0, IndexOf(LeaderboardMetric.All, Feed.Metric));
        if (_metrics!.SelectedIndex != metric)
        {
            _metrics.SelectedIndex = metric;
        }

        _metrics.IsEnabled = !failed;
        if (_period is not null)
        {
            _period.IsEnabled = !failed;
        }

        _standing!.Text = board switch
        {
            null when failed => "Not connected",
            null => "Loading the season board…",
            { Players: > 0 } => string.Create(CultureInfo.InvariantCulture, $"{board.Players:N0} ranked players  ·  sorted by {Feed.Metric.Label.ToLowerInvariant()}"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{board.Players:N0} ranked players"),
        };
        _standing.Color = board is null && !failed ? _theme.Accent : _theme.TextSecondary;

        // Unreachable: one panel in the body's place, the way to try again.
        _failed!.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        foreach (UiElement child in _body!.Children)
        {
            if (!ReferenceEquals(child, _failed))
            {
                child.Visibility = failed ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        if (failed && _failed.Children.Count == 0)
        {
            Fill(_failed, [FailedPanel()], entrance);
        }
        else if (!failed)
        {
            _failed.ClearChildren();
        }

        // The podium and the list made again only when what they show changed; their parts fade in only when what is picked did.
        string pick = $"{Feed.Title}|{period}|{Feed.Metric.Id}";
        string makeup = $"{pick}|{Feed.BoardState}|{board?.You?.RecordId}|{board?.Week}|"
            + string.Join(';', (board?.Rows ?? []).Select(row => $"{row.Rank}:{row.RecordId}:{row.Name}:{Feed.Metric.Figure(row)}:{row.Kills}:{row.Deaths}:{row.Assists}:{row.Wins}:{row.Matches}:{row.Was}:{row.Account?.Avatar}"));
        bool picked = pick != _builtPick;
        if (makeup != _builtList)
        {
            _builtList = makeup;
            _builtPick = pick;
            Fill(_podium!, [PodiumPart(board)], entrance && picked);
            Fill(_columns!, ColumnParts(board), entrance && picked);
            Fill(_list!, ListParts(board), entrance && picked);
            if (picked)
            {
                _listScroll!.ScrollOffset = 0f;
            }
        }

        string pinned = $"{pick}|{Feed.BoardState}|{_source.SignedIn}|{board?.You?.Rank}|{board?.You?.Name}|{board?.Ahead?.Rank}|{board?.Players}|{board?.You is { } own && board.Rows.Any(row => row.RecordId == own.RecordId)}";
        if (pinned != _builtPinned)
        {
            _builtPinned = pinned;
            Fill(_pinned!, PinnedParts(board), entrance);
        }

        SyncSelected();
    }

    private static string SeasonNow() => DateTimeOffset.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    // This season, all time and the seasons gone by the board names: the cycler made again only when they change.
    private void SyncPeriods(IReadOnlyList<string>? seasons)
    {
        List<string> periods = [LeaderboardSeasons.Current, LeaderboardSeasons.AllTime, .. (seasons ?? []).Skip(1)];
        if (LeaderboardSeasons.IsMonth(Feed.Period) && !periods.Contains(Feed.Period))
        {
            periods.Add(Feed.Period);
        }

        int at = Math.Max(0, periods.IndexOf(Feed.Period));
        if (_period is not null && periods.SequenceEqual(_periods))
        {
            if (_period.SelectedIndex != at)
            {
                _period.SelectedIndex = at;
            }

            return;
        }

        bool hadFocus = _period is not null && ReferenceEquals(_focus()?.Focused, _period);
        _periods = periods;
        _period = new BoardCycler(_theme, Compact ? string.Empty : "Period", [.. periods.Select(PeriodName)], at)
        {
            ValueWidth = Compact ? 140f : 170f,
            Tooltip = "This season, all time, or a season gone by.",
        };
        _period.SelectionChanged += index => Feed.ShowPeriod(_periods[index]);
        _periodHost!.ClearChildren();
        _periodHost.AddChild(_period);
        Appear(_period, entrance: false);
        if (hadFocus)
        {
            Refocus(_period);
        }
    }

    private static string PeriodName(string period) => period switch
    {
        LeaderboardSeasons.Current => "This season",
        LeaderboardSeasons.AllTime => "All time",
        _ => LeaderboardSeasons.Name(period),
    };

    // The top three: their cards, their shapes while the board is read, or their places open where nobody has them yet.
    private UiElement PodiumPart(Leaderboard? board)
    {
        if (Compact)
        {
            var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6f };
            for (int place = 1; place <= 3; place++)
            {
                BoardRow? row = board?.Rows.FirstOrDefault(r => r.Rank == place);
                column.AddChild(row is not null ? Card(board!, row)
                    : board is null ? new SkeletonBlock(PodiumCard.HeightOf(place, _size))
                    : new OpenPodiumSlot(_theme, place, _size));
            }

            return column;
        }

        // The first in the middle and taller, the second on its left, the third on its right; bottom-aligned.
        var podium = new Grid { ColumnSpacing = 14f, Height = PodiumCard.HeightOf(1, _size) };
        podium.ColumnDefinitions.Add(GridLength.Star);
        podium.ColumnDefinitions.Add(GridLength.Stars(1.22f));
        podium.ColumnDefinitions.Add(GridLength.Star);
        podium.RowDefinitions.Add(GridLength.Star);
        int[] order = [2, 1, 3];
        for (int i = 0; i < order.Length; i++)
        {
            int place = order[i];
            BoardRow? row = board?.Rows.FirstOrDefault(r => r.Rank == place);
            UiElement part = row is not null ? Card(board!, row)
                : board is null ? new SkeletonBlock(PodiumCard.HeightOf(place, _size))
                : new OpenPodiumSlot(_theme, place, _size);
            part.VerticalAlignment = VerticalAlignment.Bottom;
            podium.Add(part, 0, i);
        }

        return podium;
    }

    private PodiumCard Card(Leaderboard board, BoardRow row)
    {
        string? you = board.You?.RecordId ?? Feed.You;
        var card = new PodiumCard(_theme, row, Feed.Metric, _source.Avatar)
        {
            IsYou = row.RecordId == you || row.Player == you,
            IsNew = board.IsNewThisWeek(row),
            Size = _size,
        };
        card.Activated += _ => Feed.OpenRecord(row.RecordId);
        return card;
    }

    // The headings over the rows, where there are rows (or their shapes).
    private List<UiElement> ColumnParts(Leaderboard? board) =>
        Compact || (board is { Rows.Count: <= 3 }) ? [] : [BoardColumns.Header(_theme, Feed.Metric.Label, _size, board?.Week is not null)];

    // What the list holds: the rows' shapes while it is read, why it is empty, or the rows under the podium.
    private List<UiElement> ListParts(Leaderboard? board)
    {
        if (board is null)
        {
            return [.. Enumerable.Range(0, SkeletonRows).Select(i => (UiElement)new RankRowSkeleton(i, _size))];
        }

        if (board.Rows.Count == 0)
        {
            return EmptyParts();
        }

        string? you = board.You?.RecordId ?? Feed.You;
        BoardRow leader = board.Rows[0];
        List<UiElement> parts = [];
        foreach (BoardRow row in board.Rows.Skip(Compact ? 3 : 3))
        {
            if (row.Rank == CutOff + 1 && board.Rows.Count > CutOff)
            {
                parts.Add(new BoardCutOff(_theme, string.Create(CultureInfo.InvariantCulture, $"Top {CutOff} cut-off")));
            }

            var line = new RankRow(_theme, row, Feed.Metric, _source.Avatar)
            {
                IsYou = row.RecordId == you || row.Player == you,
                IsNew = board.IsNewThisWeek(row),
                Leader = leader,
                Size = _size,
            };
            line.Activated += _ => Feed.OpenRecord(row.RecordId);
            parts.Add(line);
        }

        if (board.Players > board.Rows.Count)
        {
            parts.Add(new BoardNote(_theme, string.Create(CultureInfo.InvariantCulture, $"The top {board.Rows.Count} of {board.Players:N0} ranked")));
        }

        return parts;
    }

    // Nobody on the board: what to do about it, and the season gone by.
    private List<UiElement> EmptyParts()
    {
        string when = Feed.Period == LeaderboardSeasons.Current ? "this season" : LeaderboardSeasons.IsMonth(Feed.Period) ? "in " + LeaderboardSeasons.Name(Feed.Period) : "yet";
        string detail = Feed.Period == LeaderboardSeasons.Current
            ? $"Season {LeaderboardSeasons.Name(SeasonNow())} is open. Win one match on an official server and the top spot is yours."
            : $"No matches in {GameName} were counted {when}.";
        var parts = new StackPanel { Orientation = Orientation.Vertical, Spacing = 20f, Margin = new Thickness(0f, Compact ? 10f : 40f, 0f, 0f) };
        parts.AddChild(new BoardMessage(_theme, KitIcon.Leaderboard, $"Nobody yet {when}", detail));
        var buttons = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 10f, HorizontalAlignment = HorizontalAlignment.Center };
        if (_source.CanFindMatch)
        {
            var find = new BoardButton(_theme, "Find a match", BoardButtonLook.Solid) { Key = "Enter", Arrow = true, ButtonHeight = 40f, ButtonWidth = 200f };
            find.Activated += _ => _source.FindMatch();
            buttons.Add(find);
        }

        if (_periods.Count > 2)
        {
            var last = new BoardButton(_theme, "Last season", BoardButtonLook.Outline) { Key = "]", ButtonHeight = 40f };
            last.Activated += _ => Feed.ShowPeriod(_periods[2]);
            buttons.Add(last);
        }

        parts.AddChild(buttons);
        return [parts];
    }

    // The board can't be reached: why, and the ways on.
    private BoardPanel FailedPanel()
    {
        var panel = new BoardPanel();
        panel.ColumnDefinitions.Add(GridLength.Star);
        panel.RowDefinitions.Add(GridLength.Star);
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 22f, VerticalAlignment = VerticalAlignment.Center };
        stack.AddChild(new BoardMessage(_theme, KitIcon.Warning, "The leaderboard server can't be reached", "Check your connection, then try again. The same board is also on openigi.com.", warn: true));
        var buttons = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 10f, HorizontalAlignment = HorizontalAlignment.Center };
        var retry = new BoardButton(_theme, "Retry", BoardButtonLook.Solid) { Key = "R", Icon = KitIcon.Refresh, ButtonHeight = 40f, ButtonWidth = 160f };
        retry.Activated += _ => Refresh();
        buttons.Add(retry);
        if (_source.CanOpenLinks)
        {
            buttons.Add(SiteButton());
        }

        stack.AddChild(buttons);
        stack.AddChild(new BoardNote(_theme, "Last attempt: just now  ·  it is asked for again shortly"));
        panel.Add(stack, 0, 0);
        return panel;
    }

    // The player's own row under the list: their place wherever they stand, or why they have none and what to do about it.
    private List<UiElement> PinnedParts(Leaderboard? board)
    {
        if (board is null)
        {
            return Feed.BoardState == FeedState.Failed ? [] : [new SkeletonBlock(Compact ? 48f : 64f)];
        }

        BoardRow? own = board.You;
        if (own is not null && board.Rows.Any(row => row.RecordId == own.RecordId))
        {
            return [];
        }

        var row = new YourPlaceRow(_theme, own, Feed.Metric, _source.Avatar)
        {
            Name = _source.YourName,
            Linked = _source.SignedIn,
            Players = board.Players,
            Ahead = board.Ahead,
            Leader = board.Rows.Count > 0 ? board.Rows[0] : null,
            IsNew = own is not null && board.IsNewThisWeek(own),
            Size = _size,
            Margin = new Thickness(0f, Compact ? 6f : 0f, 0f, 0f),
        };
        if (own is not null)
        {
            // Placed, signed in or not: a key on its own is ranked under the name it plays by.
            row.Activated += _ => Feed.OpenRecord(own.RecordId);
        }
        else if (!_source.SignedIn)
        {
            row.State = YourPlaceState.SignedOut;
            row.Advice = "Sign in with Discord on the Profile page to be on the board under your name.";
            if (_source.CanOpenProfile)
            {
                row.Action = "Go to profile";
                row.ActionKey = "P";
                row.Activated += _ => _source.OpenProfile();
            }
        }
        else
        {
            row.State = YourPlaceState.NotRanked;
            row.Advice = Feed.Period == LeaderboardSeasons.Current
                ? "No matches on an official server this season. One match gets you a place."
                : "No matches on an official server in this period.";
            if (_source.CanFindMatch)
            {
                row.Action = "Find a match";
                row.ActionKey = "F";
                row.Activated += _ => _source.FindMatch();
            }
        }

        return [row];
    }
}
