using System.Globalization;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Screens;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>
/// The single-player boards as a page, the launcher's and the game menu's alike: which mission (or all of them), which difficulty,
/// which rules, ranked by the fastest or the best rank; the rows, each run watched or raced from its row where its replay is kept;
/// and the player's own row pinned under the list wherever they stand. Read through an <see cref="ILeaderboardSource"/>'s
/// <see cref="ILeaderboardSource.Missions"/>; the page that holds it calls <see cref="Update"/> each frame.
/// </summary>
public sealed class MissionBoardView
{
    private static readonly string[] DifficultyNames = ["Easy", "Normal", "Hard"];
    private static readonly string[] RulesetNames = ["Classic", "Modern"];
    private static readonly string[] MetricNames = ["Fastest", "Best rank"];

    private readonly UiTheme _theme;
    private readonly ILeaderboardSource _source;
    private readonly MissionBoardFeed _feed;
    private readonly bool _compact;
    private readonly Func<FocusManager?> _focus;
    private readonly Grid _root;
    private readonly StackPanel _list = new() { Orientation = Orientation.Vertical, Spacing = 0f, Margin = new Thickness(0f, 0f, 12f, 0f) };
    private readonly StackPanel _pinned = new() { Orientation = Orientation.Vertical, Spacing = 4f };
    private readonly StackPanel _missionHost = new() { Orientation = Orientation.Horizontal };
    private readonly Label _note;
    private string _builtTitle = string.Empty;
    private int _seen = -1;

    /// <summary>Initializes a new instance of the <see cref="MissionBoardView"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="source">What it is shown from, which must read the mission boards.</param>
    /// <param name="compact">Whether it is laid out for a narrow page.</param>
    /// <param name="focus">The focus of the screen it is on.</param>
    public MissionBoardView(UiTheme theme, ILeaderboardSource source, bool compact, Func<FocusManager?> focus)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _feed = source.Missions ?? throw new ArgumentException("The source reads no mission boards.", nameof(source));
        _compact = compact;
        _focus = focus ?? throw new ArgumentNullException(nameof(focus));

        var title = new SectionTitle(theme, "Single player", "Mission boards") { TitleSize = compact ? 30f : 44f, HorizontalAlignment = HorizontalAlignment.Left };
        _note = new Label(theme.Body, string.Empty, compact ? 13f : 15f, theme.TextSecondary) { Wrap = true };

        OptionCycler Cycler(string label, IEnumerable<string> names, int at, Action<int> changed)
        {
            var cycler = new OptionCycler(theme, label, names, at) { Inline = true, ContentWidth = compact ? 150f : 140f, Width = compact ? 420f : 300f };
            cycler.SelectionChanged += changed;
            return cycler;
        }

        // Side by side; one under the other on a narrow page, where three side by side run off its edge.
        var switches = new StackPanel { Orientation = compact ? Orientation.Vertical : Orientation.Horizontal, Spacing = compact ? 6f : 16f };
        switches.AddChild(_missionHost);
        switches.AddChild(Cycler("Difficulty", DifficultyNames, _feed.Difficulty, _feed.ShowDifficulty));
        switches.AddChild(Cycler("Rules", RulesetNames, Math.Max(0, IndexOf(MissionBoardFeed.Rulesets, _feed.Ruleset)), i => _feed.ShowRuleset(MissionBoardFeed.Rulesets[i])));

        var metrics = new TabStrip(theme, MetricNames)
        {
            SegmentHeight = compact ? 34f : 40f,
            TabPadding = compact ? 10f : 20f,
            TextSize = compact ? 14f : 16f,
            VerticalAlignment = VerticalAlignment.Center,
        };
        metrics.SelectionChanged += i => _feed.ShowMetric(MissionBoardFeed.Metrics[i]);

        var scroll = new ScrollView(theme);
        var banner = new StackPanel { Orientation = Orientation.Vertical, Spacing = compact ? 6f : 10f };
        banner.AddChild(title);
        banner.AddChild(_note);
        _root = new Grid { RowSpacing = compact ? 8f : 16f };
        _root.ColumnDefinitions.Add(GridLength.Star);
        if (compact)
        {
            var content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f };
            content.AddChild(banner);
            content.AddChild(switches);
            content.AddChild(metrics);
            content.AddChild(_list);
            scroll.SetContent(content);
            _root.RowDefinitions.Add(GridLength.Star);
            _root.RowDefinitions.Add(GridLength.Auto);
            _root.Add(scroll, 0, 0);
            _root.Add(_pinned, 1, 0);
        }
        else
        {
            scroll.SetContent(_list);
            foreach (GridLength height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Star, GridLength.Auto })
            {
                _root.RowDefinitions.Add(height);
            }

            _root.Add(banner, 0, 0);
            _root.Add(switches, 1, 0);
            _root.Add(metrics, 2, 0);
            _root.Add(scroll, 3, 0);
            _root.Add(_pinned, 4, 0);
        }

        Sync();
    }

    /// <summary>Gets the page.</summary>
    public UiElement Root => _root;

    /// <summary>Takes in what the master has sent, and asks for what is shown when it is missing or old.</summary>
    /// <param name="shown">Whether the page is the one up.</param>
    /// <returns><see langword="true"/> when the page changed and is to be laid out again.</returns>
    public bool Update(bool shown)
    {
        _feed.Update(shown);
        if (_feed.Revision == _seen)
        {
            return false;
        }

        Sync();
        return true;
    }

    /// <summary>Races the run of the row with the focus, where it can be raced.</summary>
    /// <returns>Whether one was.</returns>
    public bool RaceFocused()
    {
        if (_focus()?.Focused is MissionRow { HasReplay: true } row)
        {
            row.Race();
            return true;
        }

        return false;
    }

    private void Sync()
    {
        SyncList();
        Screen.PlayEntrance(_list);
        Screen.PlayEntrance(_pinned);
    }

    // What the list holds: its state while it is read or why it is empty, else the rows; and the player's own row under it.
    private void SyncList()
    {
        _seen = _feed.Revision;
        if (_builtTitle != _feed.Title)
        {
            _builtTitle = _feed.Title;
            BuildMissionPicker();
        }

        _note.Text = (_compact ? string.Empty : "Every run is played again by the server before it counts. ") + (_source.SignInHint ?? string.Empty);
        _list.ClearChildren();
        _pinned.ClearChildren();
        RunBoard? board = _feed.Board;
        if (board is null)
        {
            _list.AddChild(Message(
                _feed.BoardState == FeedState.Failed ? "The boards can't be reached right now." : "Reading the board…",
                _feed.BoardState == FeedState.Failed ? "They are asked for again shortly." : string.Empty));
            return;
        }

        if (board.Rows.Count == 0)
        {
            _list.AddChild(Message(
                _feed.Mission is null ? "Nobody has run every mission yet." : "Nobody on this board yet.",
                "Complete the mission signed in with Discord to be the first."));
            return;
        }

        bool overall = _feed.Mission is null;
        string? you = board.You?.Account ?? _feed.You;
        foreach (RunBoardRow row in board.Rows)
        {
            _list.AddChild(Row(row, overall, row.Account == you));
        }

        if (board.Players > board.Rows.Count)
        {
            _list.AddChild(new Label(_theme.Mono, string.Create(CultureInfo.InvariantCulture, $"THE TOP {board.Rows.Count} OF {board.Players:N0} RANKED"), 12f, _theme.TextMuted)
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0f, 14f, 0f, 4f),
            });
        }

        if (board.You is { } own && !board.Rows.Any(row => row.Account == own.Account))
        {
            _pinned.AddChild(new Eyebrow(_theme, "Your place") { TextSize = 12f });
            _pinned.AddChild(Row(own, overall, isYou: true));
        }
    }

    // The mission picker for the game shown: every mission together, then each in campaign order.
    private void BuildMissionPicker()
    {
        IReadOnlyList<(int Id, string Name)> missions = _source.MissionsOf(_feed.Title);
        int at = _feed.Mission is { } shown ? Math.Max(0, missions.ToList().FindIndex(m => m.Id == shown) + 1) : 0;
        var picker = new OptionCycler(_theme, "Mission", ["All missions", .. missions.Select(static m => m.Name)], at)
        {
            Inline = true,
            ContentWidth = _compact ? 150f : 200f,
            Width = _compact ? 420f : 380f,
        };
        picker.SelectionChanged += i => _feed.ShowMission(i == 0 ? null : missions[i - 1].Id);
        _missionHost.ClearChildren();
        _missionHost.AddChild(picker);
        picker.Visual.AppearInstantly();
    }

    private MissionRow Row(RunBoardRow row, bool overall, bool isYou)
    {
        var line = new MissionRow(_theme, row, _feed.Metric, overall) { IsYou = isYou, Compact = _compact };
        if (line.HasReplay)
        {
            string run = row.Run!;
            line.Activated += _ => _source.Watch(run);
            line.Raced += _ => _source.Race(run);
        }

        return line;
    }

    private StackPanel Message(string headline, string detail)
    {
        var message = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f, Margin = new Thickness(0f, _compact ? 24f : 56f), HorizontalAlignment = HorizontalAlignment.Center };
        message.AddChild(new Label(_theme.Heading, headline.ToUpperInvariant(), _compact ? 18f : 24f, _theme.Text) { HorizontalAlignment = HorizontalAlignment.Center });
        if (detail.Length > 0)
        {
            message.AddChild(new Label(_theme.Body, detail, _compact ? 14f : 16f, _theme.TextSecondary) { HorizontalAlignment = HorizontalAlignment.Center });
        }

        return message;
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
            {
                return i;
            }
        }

        return -1;
    }
}
