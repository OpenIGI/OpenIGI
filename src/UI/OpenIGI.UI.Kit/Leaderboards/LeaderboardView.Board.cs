using System.Globalization;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <content>
/// The board: the banner (the game and season, the scoring, how long the season has left), the switches of game, period and
/// metric, the podium and the ranked rows in a list that scrolls, and the player's own row pinned under it.
/// </content>
public sealed partial class LeaderboardView
{
    private const int SkeletonRows = 7;

    private Grid? _boardPage;
    private SectionTitle? _title;
    private Label? _scoring;
    private Chip? _clock;
    private TabStrip? _boardGames;
    private StackPanel? _periodHost;
    private OptionCycler? _period;
    private TabStrip? _metrics;
    private Label? _standing;
    private StackPanel? _list;
    private ScrollView? _listScroll;
    private StackPanel? _pinned;

    // What the period cycler offers, by the period each asks for; and what the list and the pinned row were made from.
    private List<string> _periods = [];
    private string _builtList = string.Empty;
    private string _builtPick = string.Empty;
    private string _builtPinned = string.Empty;

    private Grid BuildBoard()
    {
        _title = new SectionTitle(_theme, "Official servers", "Leaderboard") { TitleSize = _compact ? 30f : 44f, HorizontalAlignment = HorizontalAlignment.Left };
        _scoring = Note(string.Empty);
        _clock = new Chip(_theme, "Season ends in", string.Empty) { KeySize = 12f, ValueSize = 16f, HorizontalAlignment = HorizontalAlignment.Left };
        _boardGames = GameSwitch();
        _periodHost = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _metrics = new TabStrip(_theme, LeaderboardMetric.All.Select(static metric => metric.Label))
        {
            SegmentHeight = _compact ? 34f : 40f,
            TabPadding = _compact ? 10f : 20f,
            TextSize = _compact ? 14f : 16f,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _metrics.SelectionChanged += index => Feed.ShowMetric(LeaderboardMetric.All[index]);
        _standing = new Label(_theme.Mono, string.Empty, _compact ? 13f : 14f, _theme.TextSecondary) { VerticalAlignment = VerticalAlignment.Center };

        var refresh = new IconButton(_theme, KitIcon.Refresh) { Tooltip = "Read the board again now.", VerticalAlignment = VerticalAlignment.Center };
        refresh.Activated += _ => Feed.Refresh();

        var banner = new StackPanel { Orientation = Orientation.Vertical, Spacing = _compact ? 6f : 10f };
        banner.AddChild(_title);
        banner.AddChild(_scoring);
        if (!_compact)
        {
            banner.AddChild(_clock);
        }

        var switches = new FlexPanel { Orientation = Orientation.Horizontal, Gap = _compact ? 10f : 16f, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Right };
        if (_boardGames is not null)
        {
            switches.Add(_boardGames);
        }

        switches.Add(_periodHost);

        var tools = new FlexPanel { Orientation = Orientation.Horizontal, Gap = _compact ? 10f : 16f };
        tools.Add(_metrics);
        tools.Add(new Panel(), grow: 1f);
        tools.Add(_standing);
        tools.Add(refresh);
        if (_source.CanOpenLinks && !_compact)
        {
            tools.Add(Ghost("Open on openigi.com", "The board on the site, to share it.", () => _source.OpenLink(LeaderboardLinks.Board(Feed.Title)), KitIcon.Globe));
        }

        _list = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0f, Margin = new Thickness(0f, 0f, 12f, 0f) };
        _listScroll = new ScrollView(_theme);
        _pinned = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4f };

        var page = new Grid { RowSpacing = _compact ? 8f : 18f };
        page.ColumnDefinitions.Add(GridLength.Star);
        if (_compact)
        {
            // Height is what a phone on its side has least of: the banner and the switches scroll away with the list, and only
            // the player's own row stays put under it.
            var content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f };
            switches.HorizontalAlignment = HorizontalAlignment.Left;
            content.AddChild(banner);
            content.AddChild(switches);
            content.AddChild(tools);
            content.AddChild(_list);
            _listScroll.SetContent(content);
            page.RowDefinitions.Add(GridLength.Star);
            page.RowDefinitions.Add(GridLength.Auto);
            page.Add(_listScroll, 0, 0);
            page.Add(_pinned, 1, 0);
        }
        else
        {
            _listScroll.SetContent(_list);
            page.ColumnDefinitions.Add(GridLength.Auto);
            page.RowDefinitions.Add(GridLength.Auto);
            page.RowDefinitions.Add(GridLength.Auto);
            page.RowDefinitions.Add(GridLength.Star);
            page.RowDefinitions.Add(GridLength.Auto);
            page.Add(banner, 0, 0);
            page.Add(switches, 0, 1);
            page.Add(tools, 1, 0, columnSpan: 2);
            page.Add(_listScroll, 2, 0, columnSpan: 2);
            page.Add(_pinned, 3, 0, columnSpan: 2);
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
        string season = board?.Season ?? string.Empty;
        _title!.Eyebrow = period == LeaderboardSeasons.AllTime
            ? $"{game} · all time"
            : current
                ? (season.Length > 0 ? $"{game} · season {LeaderboardSeasons.Name(season)}" : $"{game} · this season")
                : $"{game} · season {LeaderboardSeasons.Name(period)} · ended";
        string scoring = _compact ? string.Empty : "Every match on an official server counts: a kill 10, an assist 5, an objective 25, a win 50.";
        _scoring!.Text = _source.SignInHint is { } hint ? (scoring + " " + hint).Trim() : scoring;
        _scoring.Visibility = _scoring.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        int days = LeaderboardSeasons.DaysLeft(DateTimeOffset.UtcNow);
        _clock!.Value = days == 1 ? "1 DAY" : string.Create(CultureInfo.InvariantCulture, $"{days} DAYS");
        _clock.Visibility = current && !_compact ? Visibility.Visible : Visibility.Collapsed;

        SyncPeriods(board?.Seasons);
        int metric = Math.Max(0, IndexOf(LeaderboardMetric.All, Feed.Metric));
        if (_metrics!.SelectedIndex != metric)
        {
            _metrics.SelectedIndex = metric;
        }

        _standing!.Text = _compact ? string.Empty : board switch
        {
            { You: { } you, Players: > 0 } => string.Create(CultureInfo.InvariantCulture, $"YOU  #{you.Rank:N0} OF {board.Players:N0}"),
            { Players: > 0 } => string.Create(CultureInfo.InvariantCulture, $"{board.Players:N0} RANKED"),
            _ => string.Empty,
        };

        // The list made again only when what it shows changed; its parts fade in only when what is picked did.
        string pick = $"{Feed.Title}|{period}|{Feed.Metric.Id}";
        string makeup = $"{pick}|{Feed.BoardState}|{board?.You?.RecordId}|"
            + string.Join(';', (board?.Rows ?? []).Select(row => $"{row.Rank}:{row.RecordId}:{row.Name}:{Feed.Metric.Figure(row)}:{row.Kills}:{row.Deaths}:{row.Assists}:{row.Wins}:{row.Matches}:{row.Account?.Avatar}"));
        bool picked = pick != _builtPick;
        if (makeup != _builtList)
        {
            _builtList = makeup;
            _builtPick = pick;
            Fill(_list!, ListParts(board), entrance && picked);
            if (picked)
            {
                _listScroll!.ScrollOffset = 0f;
            }
        }

        string pinned = $"{pick}|{board?.You?.Rank}|{board?.You?.Name}|{board?.You is { } own && (board.Rows.Any(row => row.RecordId == own.RecordId))}";
        if (pinned != _builtPinned)
        {
            _builtPinned = pinned;
            Fill(_pinned!, PinnedParts(board), entrance);
        }
    }

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
        _period = new OptionCycler(_theme, "Period", periods.Select(PeriodName), at)
        {
            Inline = true,
            ContentWidth = _compact ? 150f : 190f,

            // A form's row takes all the room it is given: here it stands beside the game switch, as wide as its words.
            Width = _compact ? 300f : 360f,
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

    // What the list holds: its shape while it is read, why it is empty or can't be read, or the podium and the rows.
    private List<UiElement> ListParts(Leaderboard? board)
    {
        if (board is null)
        {
            if (Feed.BoardState == FeedState.Failed)
            {
                return [Message("The leaderboard can't be reached right now.", "It is asked for again shortly, or now with the refresh button.")];
            }

            var podium = Podium([new SkeletonBlock(_compact ? 144f : 204f), new SkeletonBlock(_compact ? 164f : 236f), new SkeletonBlock(_compact ? 144f : 204f)]);
            return [podium, .. Enumerable.Range(0, SkeletonRows).Select(i => (UiElement)new RankRowSkeleton(i, _compact))];
        }

        if (board.Rows.Count == 0)
        {
            string when = Feed.Period == LeaderboardSeasons.Current ? " this season" : LeaderboardSeasons.IsMonth(Feed.Period) ? $" in {LeaderboardSeasons.Name(Feed.Period)}" : string.Empty;
            return [Message($"Nobody yet in {GameName}{when}.", "Play a match on an official server to be the first.")];
        }

        string? you = board.You?.RecordId ?? Feed.You;
        Func<string?, UiPicture?> avatar = _source.Avatar;

        // The first in the middle, the second on its left, the third on its right.
        PodiumCard Card(BoardRow row)
        {
            var card = new PodiumCard(_theme, row, Feed.Metric, avatar) { IsYou = row.RecordId == you || row.Player == you, Compact = _compact };
            card.Activated += _ => Feed.OpenRecord(row.RecordId);
            return card;
        }

        List<BoardRow> top = [.. board.Rows.Take(3)];
        UiElement?[] places = [top.Count > 1 ? Card(top[1]) : null, Card(top[0]), top.Count > 2 ? Card(top[2]) : null];
        List<UiElement> parts = [Podium(places)];
        if (board.Rows.Count > 3)
        {
            parts.Add(new Panel { Height = _compact ? 10f : 18f });
            parts.Add(BoardColumns.Header(_theme, Feed.Metric.Label, _compact));
            foreach (BoardRow row in board.Rows.Skip(3))
            {
                parts.Add(Row(row, row.RecordId == you || row.Player == you));
            }
        }

        if (board.Players > board.Rows.Count)
        {
            parts.Add(new Label(_theme.Mono, string.Create(CultureInfo.InvariantCulture, $"THE TOP {board.Rows.Count} OF {board.Players:N0} RANKED"), 12f, _theme.TextMuted)
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0f, 14f, 0f, 4f),
            });
        }

        return parts;
    }

    // The player's own row under the list, where it is not in the list: their place wherever they stand.
    private List<UiElement> PinnedParts(Leaderboard? board)
    {
        if (board?.You is not { } own || board.Rows.Any(row => row.RecordId == own.RecordId))
        {
            return [];
        }

        return [new Eyebrow(_theme, "Your place") { TextSize = 12f }, Row(own, isYou: true)];
    }

    private RankRow Row(BoardRow row, bool isYou)
    {
        var line = new RankRow(_theme, row, Feed.Metric, _source.Avatar) { IsYou = isYou, Compact = _compact };
        line.Activated += _ => Feed.OpenRecord(row.RecordId);
        return line;
    }

    // Three places across, bottom-aligned; an empty place left empty.
    private Grid Podium(UiElement?[] places)
    {
        var podium = new Grid { ColumnSpacing = _compact ? 8f : 16f, Margin = new Thickness(0f, 4f, 0f, 0f) };
        podium.RowDefinitions.Add(GridLength.Auto);
        for (int i = 0; i < places.Length; i++)
        {
            podium.ColumnDefinitions.Add(GridLength.Star);
            if (places[i] is { } place)
            {
                place.VerticalAlignment = VerticalAlignment.Bottom;
                podium.Add(place, 0, i);
            }
        }

        return podium;
    }

    private StackPanel Message(string headline, string detail)
    {
        var message = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f, Margin = new Thickness(0f, _compact ? 24f : 56f), HorizontalAlignment = HorizontalAlignment.Center };
        message.AddChild(new Label(_theme.Heading, headline.ToUpperInvariant(), _compact ? 18f : 24f, _theme.Text) { HorizontalAlignment = HorizontalAlignment.Center });
        message.AddChild(new Label(_theme.Body, detail, _compact ? 14f : 16f, _theme.TextSecondary) { HorizontalAlignment = HorizontalAlignment.Center });
        return message;
    }
}
