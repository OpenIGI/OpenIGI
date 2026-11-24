using System.Globalization;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <content>
/// A player's record, opened from their row: the way back to the board and the game switch over it; who they are, their place
/// this season and how long they have played; eight figures, this season's with all time's under each; their medals; and their
/// latest matches, won and lost.
/// </content>
public sealed partial class LeaderboardView
{
    private Grid? _recordPage;
    private Button? _back;
    private TabStrip? _recordGames;
    private StackPanel? _recordBody;
    private ScrollView? _recordScroll;
    private string _builtRecord = string.Empty;

    private Grid BuildRecord()
    {
        _back = Ghost("Leaderboard", "Back to the board.", () => Feed.CloseRecord(), KitIcon.ArrowLeft);
        _recordGames = GameSwitch();

        var bar = new FlexPanel { Orientation = Orientation.Horizontal, Gap = _compact ? 10f : 16f };
        bar.Add(_back);
        bar.Add(new Panel(), grow: 1f);
        if (_recordGames is not null)
        {
            bar.Add(_recordGames);
        }

        if (_source.CanOpenLinks)
        {
            bar.Add(Ghost(_compact ? "Site" : "Open on openigi.com", "This record on the site, to share it.", () =>
            {
                if (Feed.RecordId is { } player)
                {
                    _source.OpenLink(LeaderboardLinks.Record(player, Feed.Title));
                }
            }, KitIcon.Globe));
        }

        _recordBody = new StackPanel { Orientation = Orientation.Vertical, Spacing = _compact ? 10f : 16f };
        _recordScroll = new ScrollView(_theme);
        _recordScroll.SetContent(_recordBody);

        var page = new Grid { RowSpacing = _compact ? 10f : 18f, Visibility = Visibility.Collapsed };
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
            : $"{Feed.Title}|{Feed.RecordId}|{view.Name}|{view.Avatar}|{view.Placed?.Rank}|{view.Record.SeasonTotals}|{view.Record.AllTime}|{view.Record.Medals?.Count}|"
                + string.Join(';', (view.Record.Latest ?? []).Select(static match => match.Id));
        if (makeup == _builtRecord)
        {
            return;
        }

        bool another = !_builtRecord.StartsWith($"{Feed.Title}|{Feed.RecordId}|", StringComparison.Ordinal);
        _builtRecord = makeup;
        Fill(_recordBody!, RecordParts(view), entrance && another);
        if (another)
        {
            _recordScroll!.ScrollOffset = 0f;
        }
    }

    private List<UiElement> RecordParts(RecordView? view)
    {
        if (view is null)
        {
            if (Feed.RecordState == FeedState.Failed)
            {
                return [Message("No record to show.", "This player has no matches on the official servers yet, or the record can't be reached right now.")];
            }

            return [new SkeletonBlock(_compact ? 100f : 168f), TileGrid([.. Enumerable.Range(0, 8).Select(_ => (UiElement)new SkeletonBlock(_compact ? 92f : 124f))])];
        }

        PlayerRecord record = view.Record;
        StatTotals season = record.SeasonTotals;
        StatTotals all = record.AllTime;
        List<string> tags = [];
        if (view.Placed is { } placed)
        {
            tags.Add(string.Create(CultureInfo.InvariantCulture, $"#{placed.Rank:N0} this season"));
        }

        if (view.Account is not null)
        {
            tags.Add("Discord linked");
        }

        tags.Add($"{Played(all.Seconds)} played");
        string avatar = view.Avatar ?? string.Empty;
        var hero = new RecordHero(_theme, _compact)
        {
            Eyebrow = $"{GameName} · season {LeaderboardSeasons.Name(record.Season)}",
            Name = view.Name,
            Avatar = () => _source.Avatar(avatar),
            Linked = view.Account is not null,
            Tags = tags,
            FirstLit = view.Placed is not null,
        };

        double rate = season.Matches > 0 ? (double)season.Wins / season.Matches : 0;
        string AllTime(int value) => LeaderboardMetric.Count(value) + " all time";
        FigureTile Tile(string label, string value, string sub, bool lit = false, double? share = null) =>
            new(_theme, label, value, sub) { Lit = lit, Share = share, Compact = _compact };
        UiElement tiles = TileGrid(
        [
            Tile("Score", LeaderboardMetric.Count(season.Score), AllTime(all.Score), lit: true),
            Tile("Kills", LeaderboardMetric.Count(season.Kills), AllTime(all.Kills)),
            Tile("Deaths", LeaderboardMetric.Count(season.Deaths), AllTime(all.Deaths)),
            Tile("Assists", LeaderboardMetric.Count(season.Assists), AllTime(all.Assists)),
            Tile("K/D", Two(season.Kd), Two(all.Kd) + " all time"),
            Tile("KDA", Two(Kda(season)), Two(Kda(all)) + " all time"),
            Tile("Win rate", BoardColumns.Percent(rate), string.Create(CultureInfo.InvariantCulture, $"{season.Wins:N0} of {season.Matches:N0} won"), share: rate),
            Tile("Matches", LeaderboardMetric.Count(season.Matches), AllTime(all.Matches)),
        ]);

        List<UiElement> parts = [hero, tiles];
        IReadOnlyList<Medal> medals = record.Medals ?? [];
        parts.Add(Part(medals.Count > 0 ? string.Create(CultureInfo.InvariantCulture, $"Medals · {medals.Count}") : "Medals"));
        parts.Add(medals.Count > 0
            ? Columns([.. medals.Select(medal => (UiElement)new MedalChip(_theme, medal))], _compact ? 1 : 3)
            : Note("None yet: a kill on an official server is the first."));

        IReadOnlyList<RecordMatch> latest = record.Latest ?? [];
        parts.Add(Part("Latest matches"));
        if (latest.Count == 0)
        {
            parts.Add(Note($"No matches in {GameName} yet."));
        }
        else
        {
            var matches = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f };
            foreach (RecordMatch match in latest)
            {
                matches.AddChild(new MatchLine(_theme, match, _source.MapName(Feed.Title, match.Map)) { Compact = _compact });
            }

            parts.Add(matches);
        }

        return parts;
    }

    private Eyebrow Part(string text) => new(_theme, text) { TextSize = _compact ? 12f : 13f, Margin = new Thickness(0f, _compact ? 8f : 16f, 0f, 0f) };

    private Grid TileGrid(IReadOnlyList<UiElement> tiles) => Columns(tiles, _compact ? 2 : 4);

    // Parts in rows of so many columns, each column as wide as the next.
    private Grid Columns(IReadOnlyList<UiElement> parts, int columns)
    {
        var grid = new Grid { ColumnSpacing = _compact ? 8f : 12f, RowSpacing = _compact ? 8f : 12f };
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

    private static double Kda(StatTotals totals) => (totals.Kills + totals.Assists) / (double)Math.Max(1, totals.Deaths);

    private static string Two(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Played(int seconds) => seconds >= 3600
        ? (seconds / 3600d).ToString("0.0", CultureInfo.InvariantCulture) + " h"
        : string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Round(seconds / 60d)} min");
}
