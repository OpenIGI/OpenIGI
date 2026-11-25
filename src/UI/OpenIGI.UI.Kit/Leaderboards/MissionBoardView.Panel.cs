using System.Globalization;
using OpenIGI.Core.Titles;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <content>
/// The mission's head over the board, and the selected run's panel beside it on a monitor: who ran it and their place, its
/// time and the rank it earned, the server's check, its splits against the player's own best, and the ways to race, watch and
/// report it. With no run on the board, the head offers the mission to play and the panel says the player's best will stand there.
/// </content>
public sealed partial class MissionBoardView
{
    // The mission's head: its picture, the record, the player's best, the gap.
    private MissionHero Hero(RunBoard? board)
    {
        IReadOnlyList<(int Id, string Name)> missions = _source.MissionsOf(_feed.Title);
        string difficulty = DifficultyNames[Math.Clamp(_feed.Difficulty, 0, 2)];
        string rules = RulesetNames[Math.Max(0, IndexOf(MissionBoardFeed.Rulesets, _feed.Ruleset))];
        int number = _feed.Mission is { } id ? missions.ToList().FindIndex(m => m.Id == id) + 1 : 0;
        int covered = board?.Rows.Count > 0 ? board.Rows.Max(static row => row.Missions) : 0;
        string eyebrow = Overall
            ? $"{(covered > 0 ? MissionRow.MissionsLine(covered, missions.Count) : "Every mission")} · {difficulty} · {rules} rules"
            : string.Create(CultureInfo.InvariantCulture, $"Mission {number} / {missions.Count} · {difficulty} · {rules} rules");
        string title = _feed.Title;
        int? mission = _feed.Mission;
        RunBoardRow? record = board is { Rows.Count: > 0 } ? board.Rows[0] : null;
        RunBoardRow? yours = board?.You ?? board?.Rows.FirstOrDefault(row => row.Account == _feed.You);
        var hero = new MissionHero(_theme, _size)
        {
            Eyebrow = eyebrow,
            Name = MissionName(),
            Picture = () => mission is { } shown ? _source.MissionPicture(title, shown) : null,
            Record = record,
            Yours = yours,
            Players = board?.Players ?? 0,
            Rows = board?.Rows ?? [],
            Read = board is not null,
        };

        if (board is { Rows.Count: 0 } && !Overall && _source.CanPlay)
        {
            hero.ChildRoom = 332f;
            var play = new BoardButton(_theme, "Play mission", BoardButtonLook.Solid)
            {
                Icon = KitIcon.Play,
                Detail = $"{MissionName()} · {difficulty} · {rules}",
                Arrow = true,
                ButtonHeight = 58f,
                ButtonWidth = 300f,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0f, 0f, 32f, 0f),
            };
            play.Activated += _ => PlayShown();
            hero.AddChild(play);
        }

        return hero;
    }

    private void SyncSide()
    {
        if (_size != BoardSize.Wide)
        {
            return;
        }

        RunBoard? board = _feed.Board;
        RunBoardRow? row = board is null ? null
            : (_focusedRun is { } id ? board.Rows.FirstOrDefault(r => r.Account == id) ?? (board.You?.Account == id ? board.You : null) : null)
                ?? (board.Rows.Count > 0 ? board.Rows[0] : null);
        string makeup = board is null
            ? $"none|{_feed.BoardState}"
            : row is null
                ? $"empty|{board.You?.Ticks}"
                : $"{_feed.Title}|{_feed.Mission}|{_feed.Difficulty}|{_feed.Ruleset}|{_feed.Metric}|{row.Account}|{row.Place}|{row.Ticks}|{row.Run}|{board.You?.Ticks}|{row.Run is { } run && _reported.Contains(run)}";
        if (makeup == _builtSide)
        {
            return;
        }

        _builtSide = makeup;
        UiElement part = board is null
            ? Skeleton()
            : row is null
                ? NoRunPanel()
                : RunPanel(board, row);
        Fill(_side, [part], entrance: false);
    }

    // The selected run: who, its time and rank, the check, the splits, and the ways on.
    private BoardPanel RunPanel(RunBoard board, RunBoardRow row)
    {
        var panel = new BoardPanel { Padding = new Thickness(24f, 22f, 24f, 22f), RowSpacing = 18f };
        panel.ColumnDefinitions.Add(GridLength.Star);
        bool first = row.Place == 1;
        var head = new SelectedHead(_theme)
        {
            Caption = "Selected run",
            Badge = first && !Overall ? "World record" : string.Empty,
            Note = first && !Overall ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"#{row.Place:N0} of {board.Players:N0}"),
            Name = row.Name,
            Edge = row.Place <= 3 ? BoardLook.Medal(_theme, row.Place) : _theme.Accent,
            Linked = true,
            Tags = [string.Create(CultureInfo.InvariantCulture, $"#{row.Place:N0} of {board.Players:N0}")],
            MarkSize = 64f,
            NameSize = 34f,
        };
        int lit = BoardLook.StripesLit(row.Rank, GameTitle.RanksPerDifficulty);
        string rank = Overall
            ? (_feed.Metric == "rank" ? $"{row.Points:N0} pts · " : string.Empty) + MissionRow.MissionsLine(row.Missions, _source.MissionsOf(_feed.Title).Count)
            : GameTitle.RankName(row.Rank, _feed.Difficulty) ?? "No rank";
        var time = new RunTimeBlock(_theme, MissionRow.Time(row.Ticks), first ? BoardLook.Gold : _theme.TextBright, Overall ? -1 : lit, rank)
        {
            Caption = Overall ? "Total time" : "Mission time",
            SideCaption = Overall ? "Of" : "Rank earned",
        };
        var check = new VerifiedStrip(_theme, "Verified by the server", "Re-played from inputs before it counted.");

        // The splits: the run's, and the player's best's against them.
        RunBoardRow? yours = board.You;
        List<int> theirs = WithEnd(row.Splits, row.Ticks);
        List<int>? own = yours is not null && yours.Account != row.Account ? WithEnd(yours.Splits, yours.Ticks) : null;
        var splits = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f };
        string note = own is not null && yours is not null ? "you: " + SplitsBar.Delta(yours.Ticks - row.Ticks) : string.Empty;
        splits.AddChild(new CaptionRow(_theme, own is null ? "Splits" : "Splits vs your best", note));
        splits.AddChild(theirs.Count > 1
            ? new SplitsBar(_theme, theirs, own is { Count: > 1 } && own.Count == theirs.Count ? own : null)
            : new BoardNote(_theme, Overall ? "Splits are kept for each mission's runs" : "No splits kept for this run"));

        List<UiElement> parts = [head, time, check, splits, new Panel()];
        if (row.Replay && row.Run is not null && !Overall)
        {
            string run = row.Run;
            var race = new BoardButton(_theme, "Race this ghost", BoardButtonLook.Solid)
            {
                Icon = KitIcon.Play,
                Detail = $"Play {MissionName()} against {row.Name}'s run",
                Key = "R",
                Arrow = true,
                TextSize = 19f,
                ButtonHeight = 62f,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            race.Activated += _ => _source.Race(run);
            parts.Add(race);
            var more = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 10f };
            var watch = new BoardButton(_theme, "Watch replay", BoardButtonLook.Outline) { Icon = KitIcon.Eye, Key = "Enter", TextSize = 16f, ButtonHeight = 52f };
            watch.Activated += _ => _source.Watch(run);
            more.Add(watch, grow: 1f);
            bool mine = row.Account == (board.You?.Account ?? _feed.You);
            if (_source.CanReport && !mine)
            {
                bool reported = _reported.Contains(run);
                var report = new BoardButton(_theme, reported ? "Reported" : "Report run", BoardButtonLook.Quiet) { Key = reported ? string.Empty : "X", TextSize = 14f, ButtonHeight = 52f, IsEnabled = !reported };
                report.Activated += _ => Report(row, null);
                more.Add(report);
            }

            parts.Add(more);
        }
        else if (!Overall)
        {
            parts.Add(new BoardNote(_theme, "Only the top 20 keep their replays to race and watch"));
        }

        for (int i = 0; i < parts.Count; i++)
        {
            panel.RowDefinitions.Add(i == 4 ? GridLength.Star : GridLength.Auto);
            panel.Add(parts[i], i, 0);
        }

        return panel;
    }

    // A board with no runs: where the player's best will stand, and the check every run gets.
    private BoardPanel NoRunPanel()
    {
        var panel = new BoardPanel { Padding = new Thickness(24f, 22f, 24f, 22f), RowSpacing = 14f };
        panel.ColumnDefinitions.Add(GridLength.Star);
        UiElement[] parts =
        [
            new CaptionRow(_theme, "Your best here") { Ink = _theme.Accent, CaptionSize = 11f },
            new SectionHeadline(_theme, "No run yet"),
            new Label(_theme.Body, "Your personal best shows here as soon as the server has verified one of your runs.", 14f, _theme.TextSecondary) { Wrap = true },
            new VerifiedStrip(_theme, "Every run is checked", "The server re-plays each run before it counts."),
        ];
        for (int i = 0; i < parts.Length; i++)
        {
            panel.RowDefinitions.Add(GridLength.Auto);
            panel.Add(parts[i], i, 0);
        }

        return panel;
    }

    private static BoardPanel Skeleton()
    {
        var panel = new BoardPanel { Padding = new Thickness(24f, 22f, 24f, 22f), RowSpacing = 18f };
        panel.ColumnDefinitions.Add(GridLength.Star);
        float[] heights = [100f, 82f, 58f, 52f];
        for (int i = 0; i < heights.Length; i++)
        {
            panel.RowDefinitions.Add(GridLength.Auto);
            panel.Add(new SkeletonBlock(heights[i]), i, 0);
        }

        return panel;
    }

    // A run's splits with its end after them, where it is not the last split already.
    private static List<int> WithEnd(IReadOnlyList<int>? splits, int end)
    {
        List<int> all = [.. splits ?? []];
        if (all.Count == 0 || all[^1] < end)
        {
            all.Add(end);
        }

        return all;
    }
}

/// <summary>A panel's headline in the display face, in capitals.</summary>
/// <param name="theme">The theme.</param>
/// <param name="text">What it says.</param>
public sealed class SectionHeadline(UiTheme theme, string text) : UiElement
{
    /// <summary>Gets or sets its size, in design units.</summary>
    public float TextSize { get; set; } = 30f;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(TextSize * 1.1f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter) =>
        painter.DrawLabel(theme.Display, text.ToUpperInvariant(), new UiVec2(Bounds.X, Bounds.Y), painter.Context.Px(TextSize), theme.TextBright, 0.03f);
}
