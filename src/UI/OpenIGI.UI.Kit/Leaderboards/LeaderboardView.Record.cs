using System.Globalization;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <content>
/// A player's record, opened from their row: the way back to the board and the game switch over it; who they are, their place
/// this season and the week's movement, their score; this season's totals against all time's and their rates; their latest
/// matches, won and lost; their standing on the mission boards; and their medals.
/// </content>
public sealed partial class LeaderboardView
{
    private static readonly string[] DifficultyNames = ["Easy", "Normal", "Hard"];

    private Grid? _recordPage;
    private BoardButton? _back;
    private TabStrip? _recordGames;
    private Panel? _recordBody;
    private ScrollView? _recordScroll;
    private string _builtRecord = string.Empty;

    private Grid BuildRecord()
    {
        _back = new BoardButton(_theme, Compact ? "Back" : "Leaderboard", BoardButtonLook.Outline)
        {
            Icon = KitIcon.ArrowLeft,
            Key = Compact ? string.Empty : "Esc",
            ButtonHeight = Compact ? 44f : 40f,
            TextSize = 14f,
            Tooltip = "Back to the board.",
            VerticalAlignment = VerticalAlignment.Center,
        };
        _back.Activated += _ => Feed.CloseRecord();
        _recordGames = Compact ? null : GameSwitch();

        var bar = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 10f };
        if (Compact)
        {
            bar.Add(_back);
        }

        if (ModeSwitch(missions: false) is { } mode)
        {
            bar.Add(mode);
        }

        if (!Compact)
        {
            bar.Add(_back);
        }

        bar.Add(new Panel(), grow: 1f);
        if (_recordGames is not null)
        {
            bar.Add(_recordGames);
        }

        if (_source.CanOpenLinks)
        {
            bar.Add(SiteButton());
        }

        _recordBody = new Panel();
        _recordScroll = new ScrollView(_theme);
        _recordScroll.SetContent(_recordBody);

        var page = new Grid { RowSpacing = Compact ? 8f : 18f, Visibility = Visibility.Collapsed };
        page.ColumnDefinitions.Add(GridLength.Star);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Star);
        page.Add(bar, 0, 0);
        page.Add(_recordScroll, 1, 0);
        _recordPage = page;
        return page;
    }

    private void SyncRecord(bool entrance)
    {
        RecordView? view = Feed.Record;
        string makeup = view is null
            ? $"{Feed.Title}|{Feed.RecordId}|{Feed.RecordState}"
            : $"{Feed.Title}|{Feed.RecordId}|{view.Name}|{view.Avatar}|{view.Placed?.Rank}|{view.Placed?.Was}|{view.Record.SeasonTotals}|{view.Record.AllTime}|{view.Record.Medals?.Count}|{view.Standing?.Missions.Count}|{view.Standing?.Overall?.Place}|"
                + string.Join(';', (view.Record.Latest ?? []).Select(static match => match.Id));
        if (makeup == _builtRecord)
        {
            return;
        }

        bool another = !_builtRecord.StartsWith($"{Feed.Title}|{Feed.RecordId}|", StringComparison.Ordinal);
        _builtRecord = makeup;
        Fill(_recordBody!, [RecordPart(view)], entrance && another);
        if (another)
        {
            _recordScroll!.ScrollOffset = 0f;
        }
    }

    private UiElement RecordPart(RecordView? view)
    {
        if (view is null)
        {
            if (Feed.RecordState == FeedState.Failed)
            {
                return new BoardMessage(_theme, KitIcon.Warning, "No record to show", "This player has no matches on the official servers yet, or the record can't be reached right now.", warn: true)
                {
                    Margin = new Thickness(0f, 60f, 0f, 0f),
                };
            }

            var loading = new StackPanel { Orientation = Orientation.Vertical, Spacing = 18f };
            loading.AddChild(new SkeletonBlock(Compact ? 82f : 176f));
            loading.AddChild(Columns([.. Enumerable.Range(0, Compact ? 4 : 6).Select(_ => (UiElement)new SkeletonBlock(96f))], Compact ? 4 : 3, 10f));
            return loading;
        }

        PlayerRecord record = view.Record;
        StatTotals season = record.SeasonTotals;
        StatTotals all = record.AllTime;
        BoardRow? placed = view.Placed;
        string avatar = view.Avatar ?? string.Empty;
        var hero = new RecordHero(_theme, _size)
        {
            Eyebrow = $"{GameName} · season {LeaderboardSeasons.Name(record.Season)}",
            Name = view.Name,
            Avatar = () => _source.Avatar(avatar),
            Linked = view.Account is not null,
            Tags = [$"{Played(all.Seconds)} played", string.Create(CultureInfo.InvariantCulture, $"{season.Matches:N0} matches")],
            Placed = placed,
            Players = Feed.Board is { Period: "season" } board && Feed.Period == LeaderboardSeasons.Current ? board.Players : 0,
            Score = season.Score,
            ScoreNote = ScoreNote(placed, view.Ahead, season.Score),
            IsNew = placed is not null && placed.Was is null && Feed.Board?.Week is not null,
        };
        if (hero.Players == 0 && placed is not null)
        {
            hero.Players = Math.Max(placed.Rank, Feed.Board?.Players ?? 0);
        }

        double rate = season.Matches > 0 ? (double)season.Wins / season.Matches : 0;
        string AllTime(int value) => LeaderboardMetric.Count(value) + " all time";
        StatTile Tile(string label, string value, string sub, FigureLight lit = FigureLight.None) => new(_theme, label, value, sub)
        {
            Ink = lit == FigureLight.Medal ? (placed is { Rank: <= 3 } top ? BoardLook.Medal(_theme, top.Rank) : _theme.Accent) : null,
            Panelled = true,
            FigureSize = Compact ? 24f : 30f,
        };
        StatTile score = Tile("Score", LeaderboardMetric.Count(season.Score), AllTime(all.Score), FigureLight.Medal);
        StatTile kills = Tile("Kills", LeaderboardMetric.Count(season.Kills), AllTime(all.Kills));
        StatTile deaths = Tile("Deaths", LeaderboardMetric.Count(season.Deaths), AllTime(all.Deaths));
        StatTile assists = Tile("Assists", LeaderboardMetric.Count(season.Assists), AllTime(all.Assists));
        StatTile matches = Tile("Matches", LeaderboardMetric.Count(season.Matches), AllTime(all.Matches));
        StatTile wins = Tile("Wins", LeaderboardMetric.Count(season.Wins), string.Create(CultureInfo.InvariantCulture, $"{season.Wins:N0} of {season.Matches:N0} this season"));
        StatTile kd = Tile("K/D", Two(season.Kd), Two(all.Kd) + " all time");
        kd.Change = all.Matches > season.Matches ? season.Kd - all.Kd : null;
        StatTile kda = Tile("KDA", Two(Kda(season)), Two(Kda(all)) + " all time");
        kda.Change = all.Matches > season.Matches ? Kda(season) - Kda(all) : null;
        StatTile winRate = Tile("Win rate", BoardColumns.Percent(rate), string.Create(CultureInfo.InvariantCulture, $"{season.Wins:N0} of {season.Matches:N0} won"));
        winRate.Share = rate;

        // The latest matches: won and lost in a strip, then each.
        IReadOnlyList<RecordMatch> latest = record.Latest ?? [];
        List<bool> results = [.. latest.Select(static match => match.Line?.Won ?? false)];
        var recent = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12f };
        recent.AddChild(Section("Recent results", latest.Count == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"last {results.Count}: {results.Count(static w => w)} won")));
        if (latest.Count == 0)
        {
            recent.AddChild(new BoardNote(_theme, $"No matches in {GameName} yet."));
        }
        else
        {
            recent.AddChild(new WinLossStrip(_theme, results) { TileHeight = 28f });
            var lines = new StackPanel { Orientation = Orientation.Vertical };
            foreach (RecordMatch match in latest.Take(_size == BoardSize.Wide ? 10 : 4))
            {
                lines.AddChild(new MatchLine(_theme, match, _source.MapName(Feed.Title, match.Map)) { Compact = Compact });
            }

            recent.AddChild(lines);
        }

        // The mission boards and the medals.
        var standing = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12f };
        MissionStanding? runs = view.Standing;
        standing.AddChild(Section("Per-mission standing", runs is null ? string.Empty : $"Fastest · {DifficultyNames[Math.Clamp(runs.Difficulty, 0, 2)]} · {Capital(runs.Ruleset)}"));
        standing.AddChild(runs is null
            ? new BoardNote(_theme, "No verified runs yet: complete a mission signed in to be on its board.")
            : new StandingTable(_theme, runs, _source.MissionsOf(Feed.Title)) { MaxRows = _size == BoardSize.Wide ? 9 : 8 });
        IReadOnlyList<Medal> medals = record.Medals ?? [];
        standing.AddChild(Section(string.Create(CultureInfo.InvariantCulture, $"Medals  ·  {medals.Count}"), string.Empty));
        standing.AddChild(medals.Count > 0
            ? new MedalStrip(_theme, medals) { DiamondsOnly = _size == BoardSize.Medium }
            : new BoardNote(_theme, "None yet: a kill on an official server is the first."));

        var parts = new StackPanel { Orientation = Orientation.Vertical, Spacing = Compact ? 12f : 18f };
        parts.AddChild(hero);
        if (Compact)
        {
            parts.AddChild(Columns([score, kd, winRate, matches], 4, 8f));
            parts.AddChild(recent);
            parts.AddChild(standing);
            return parts;
        }

        var body = new Grid { ColumnSpacing = 24f };
        body.RowDefinitions.Add(GridLength.Auto);
        if (Wide)
        {
            var figures = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12f };
            figures.AddChild(Section("Season totals", "vs all time"));
            figures.AddChild(Columns([score, kills, deaths, assists, matches, wins], 3, 10f));
            figures.AddChild(Section("Rates", kd.Change > 0 ? "▲ better than all time" : string.Empty));
            figures.AddChild(Columns([kd, kda, winRate], 3, 10f));
            body.ColumnDefinitions.Add(GridLength.Stars(1.05f));
            body.ColumnDefinitions.Add(GridLength.Star);
            body.ColumnDefinitions.Add(GridLength.Star);
            body.Add(figures, 0, 0);
            body.Add(recent, 0, 1);
            body.Add(standing, 0, 2);
        }
        else
        {
            var left = new StackPanel { Orientation = Orientation.Vertical, Spacing = 16f };
            left.AddChild(Columns([score, kd, winRate, matches, kills, deaths, assists, kda], 4, 10f));
            left.AddChild(recent);
            body.ColumnDefinitions.Add(GridLength.Stars(1.4f));
            body.ColumnDefinitions.Add(GridLength.Star);
            body.Add(left, 0, 0);
            body.Add(standing, 0, 1);
        }

        parts.AddChild(body);
        return parts;
    }

    // How the score stands against the next place: behind the one above, or ahead of the one below for the leader.
    private string ScoreNote(BoardRow? placed, BoardRow? ahead, int score)
    {
        if (placed is null)
        {
            return "Not ranked this season";
        }

        if (ahead is not null)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, ahead.Score - score):N0} behind #{ahead.Rank:N0}");
        }

        BoardRow? second = Feed.Board is { Period: "season", Metric: "score" } board && Feed.Period == LeaderboardSeasons.Current
            ? board.Rows.FirstOrDefault(static row => row.Rank == 2)
            : null;
        return second is not null
            ? string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, score - second.Score):N0} ahead of #2")
            : "Leads the season";
    }

    private CaptionRow Section(string caption, string note) => new(_theme, caption, note) { Ink = _theme.Accent, CaptionSize = 11f };

    // Parts in rows of so many columns, each column as wide as the next.
    private static Grid Columns(IReadOnlyList<UiElement> parts, int columns, float gap)
    {
        var grid = new Grid { ColumnSpacing = gap, RowSpacing = gap };
        for (int i = 0; i < columns; i++)
        {
            grid.ColumnDefinitions.Add(GridLength.Star);
        }

        for (int i = 0; i < (parts.Count + columns - 1) / columns; i++)
        {
            grid.RowDefinitions.Add(GridLength.Auto);
        }

        for (int i = 0; i < parts.Count; i++)
        {
            grid.Add(parts[i], i / columns, i % columns);
        }

        return grid;
    }

    private static string Capital(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

    private static double Kda(StatTotals totals) => (totals.Kills + totals.Assists) / (double)Math.Max(1, totals.Deaths);

    private static string Two(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Played(int seconds) => seconds >= 3600
        ? (seconds / 3600d).ToString("0.0", CultureInfo.InvariantCulture) + " h"
        : string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Round(seconds / 60d)} min");

    // Which figure of a record's is lit in its place's colour.
    private enum FigureLight
    {
        None,
        Medal,
    }
}
