using System.Globalization;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <content>
/// The panel beside a wide page's board, on the row with the keys (the first, before the keys reach the board): who they are,
/// their place and the week's movement; six of their figures; their last ten matches won and lost and their medals, once their
/// record has been read; and the way into it. Over an empty board, what a season's score counts.
/// </content>
public sealed partial class LeaderboardView
{
    private void SyncSelected()
    {
        if (!Wide || _side is null)
        {
            return;
        }

        Leaderboard? board = Feed.Board;
        BoardRow? row = board is null ? null : Selected(board);
        RecordView? peek = row is not null && Feed.PeekId == row.RecordId ? Feed.Peeked : null;
        string makeup = board is null
            ? $"none|{Feed.BoardState}"
            : row is null
                ? $"empty|{Feed.Period}"
                : $"{Feed.Title}|{Feed.Period}|{Feed.Metric.Id}|{row.RecordId}|{row.Rank}|{row.Score}|{row.Was}|{board.Players}|{peek?.Record.Latest?.Count}|{peek?.Record.Medals?.Count}";
        if (makeup == _builtSide)
        {
            return;
        }

        _builtSide = makeup;
        UiElement part = board is null
            ? SkeletonPanel()
            : row is null
                ? new PointsPanel(_theme) { Foot = Feed.Period == LeaderboardSeasons.Current ? SeasonEnds() : string.Empty }
                : SelectedPanel(board, row, peek);
        Fill(_side, [part], entrance: false);
    }

    // The row the panel is on: the one with the keys, or the first.
    private BoardRow? Selected(Leaderboard board)
    {
        if (board.Rows.Count == 0)
        {
            return null;
        }

        return (_focusedRow is { } id ? board.Rows.FirstOrDefault(r => r.RecordId == id) ?? (board.You?.RecordId == id ? board.You : null) : null)
            ?? board.Rows[0];
    }

    private static string SeasonEnds()
    {
        int days = LeaderboardSeasons.DaysLeft(DateTimeOffset.UtcNow);
        return string.Create(CultureInfo.InvariantCulture, $"Season ends in {days} {(days == 1 ? "day" : "days")}");
    }

    private BoardPanel SelectedPanel(Leaderboard board, BoardRow row, RecordView? peek)
    {
        var panel = new BoardPanel { Padding = new Thickness(24f, 22f, 24f, 22f), RowSpacing = 18f };
        panel.ColumnDefinitions.Add(GridLength.Star);
        List<string> tags = [];
        if (row.Rank <= CutOff)
        {
            tags.Add(row.Rank == 1 ? "#1" : string.Create(CultureInfo.InvariantCulture, $"Top {CutOff}"));
        }

        string avatar = row.Account?.Avatar ?? string.Empty;
        var head = new SelectedHead(_theme)
        {
            Caption = "Selected player",
            Note = board.Players > 0 ? string.Create(CultureInfo.InvariantCulture, $"#{row.Rank:N0} of {board.Players:N0}") : string.Empty,
            Name = row.Name,
            Picture = () => _source.Avatar(avatar),
            Edge = BoardLook.Medal(_theme, row.Rank),
            Linked = row.Account is not null,
            Tags = tags,
            Climbed = row.Climbed,
            IsNew = board.IsNewThisWeek(row),
        };

        string periodWord = Feed.Period == LeaderboardSeasons.AllTime ? "all time" : "season";
        StatTile Tile(string label, string value, string sub = "") => new(_theme, label, value, sub);
        var tiles = new Grid { ColumnSpacing = 8f, RowSpacing = 8f };
        for (int i = 0; i < 3; i++)
        {
            tiles.ColumnDefinitions.Add(GridLength.Star);
        }

        tiles.RowDefinitions.Add(GridLength.Auto);
        tiles.RowDefinitions.Add(GridLength.Auto);
        tiles.Add(Tile("Score", LeaderboardMetric.Count(row.Score), periodWord), 0, 0);
        tiles.Add(Tile("K/D", row.Kd.ToString("0.00", CultureInfo.InvariantCulture), string.Create(CultureInfo.InvariantCulture, $"{row.Kills:N0} / {row.Deaths:N0}")), 0, 1);
        tiles.Add(Tile("Win rate", BoardColumns.Percent(row.WinRate), string.Create(CultureInfo.InvariantCulture, $"{row.Wins:N0} of {row.Matches:N0}")), 0, 2);
        tiles.Add(Tile("Kills", LeaderboardMetric.Count(row.Kills)), 1, 0);
        tiles.Add(Tile("Assists", LeaderboardMetric.Count(row.Assists)), 1, 1);
        tiles.Add(Tile("Matches", LeaderboardMetric.Count(row.Matches)), 1, 2);

        // The last ten matches and the medals, once the record is read.
        List<bool>? results = peek?.Record.Latest?.Select(static match => match.Line?.Won ?? false).Take(10).ToList();
        int won = results?.Count(static w => w) ?? 0;
        var latest = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f };
        latest.AddChild(new CaptionRow(_theme, "Last 10 matches", results is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{won} won  ·  {results.Count - won} lost")));
        latest.AddChild(new WinLossStrip(_theme, results));

        IReadOnlyList<Medal> medals = peek?.Record.Medals ?? [];
        var medalPart = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f };
        medalPart.AddChild(new CaptionRow(_theme, peek is null ? "Medals" : string.Create(CultureInfo.InvariantCulture, $"Medals  ·  {medals.Count}")));
        medalPart.AddChild(peek is null ? new SkeletonBlock(34f) : medals.Count > 0 ? new MedalStrip(_theme, medals) : new BoardNote(_theme, "None yet"));

        var open = new BoardButton(_theme, "View full record", BoardButtonLook.Solid)
        {
            Key = "Enter",
            Arrow = true,
            TextSize = 18f,
            ButtonHeight = 60f,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        open.Activated += _ => Feed.OpenRecord(row.RecordId);

        UiElement[] parts = [head, tiles, latest, medalPart, new Panel(), open];
        for (int i = 0; i < parts.Length; i++)
        {
            panel.RowDefinitions.Add(i == 4 ? GridLength.Star : GridLength.Auto);
            panel.Add(parts[i], i, 0);
        }

        return panel;
    }

    // The panel's shape while the board is read.
    private static BoardPanel SkeletonPanel()
    {
        var panel = new BoardPanel { Padding = new Thickness(24f, 22f, 24f, 22f), RowSpacing = 18f };
        panel.ColumnDefinitions.Add(GridLength.Star);
        float[] heights = [110f, 180f, 40f, 34f];
        for (int i = 0; i < heights.Length; i++)
        {
            panel.RowDefinitions.Add(GridLength.Auto);
            panel.Add(new SkeletonBlock(heights[i]), i, 0);
        }

        return panel;
    }
}
