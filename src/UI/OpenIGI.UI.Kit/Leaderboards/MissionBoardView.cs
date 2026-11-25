using System.Globalization;
using OpenIGI.Core.Titles;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Screens;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>
/// The single-player boards as a page, the launcher's and the game menu's alike: the mission's head (its picture, the world
/// record, the player's best and the gap); which mission (or all of them), which difficulty and which rules, ranked by the
/// fastest or the best rank; the rows, each run raced and watched from its row where its replay is kept; the player's own best
/// pinned under them wherever it stands; and, on a wide page, the selected run beside them -- its time and rank, the server's
/// check, its splits against the player's best, and the ways to race, watch and report it. Read through an
/// <see cref="ILeaderboardSource"/>'s <see cref="ILeaderboardSource.Missions"/>; the page that holds it calls
/// <see cref="Update"/> each frame.
/// </summary>
public sealed partial class MissionBoardView
{
    private static readonly string[] DifficultyNames = ["Easy", "Normal", "Hard"];
    private static readonly string[] RulesetNames = ["Classic", "Modern"];
    private static readonly string[] MetricNames = ["Fastest", "Best rank"];

    private readonly UiTheme _theme;
    private readonly ILeaderboardSource _source;
    private readonly MissionBoardFeed _feed;
    private readonly BoardSize _size;
    private readonly Func<FocusManager?> _focus;
    private readonly Action<bool>? _showMissions;
    private readonly Grid _root;
    private readonly Panel _hero = new();
    private readonly Panel _columns = new();
    private readonly StackPanel _list = new() { Orientation = Orientation.Vertical, Spacing = 0f, Margin = new Thickness(0f, 0f, 10f, 0f) };
    private readonly ScrollView _scroll;
    private readonly StackPanel _pinned = new() { Orientation = Orientation.Vertical };
    private readonly Panel _side = new();
    private readonly StackPanel _missionHost = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TabStrip _difficulty;
    private readonly TabStrip _rules;

    // A phone's pickers for the difficulty and the rules, in the segmented switches' place.
    private readonly BoardCycler _difficultyCycler;
    private readonly BoardCycler _rulesCycler;
    private readonly TabStrip _metrics;
    private readonly Label _rulesNote;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private TabStrip? _games;
    private string _builtTitle = string.Empty;
    private string _builtList = string.Empty;
    private string _builtSide = string.Empty;
    private string? _focusedRun;
    private int _seen = -1;

    /// <summary>Initializes a new instance of the <see cref="MissionBoardView"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="source">What it is shown from, which must read the mission boards.</param>
    /// <param name="size">How much room the page has.</param>
    /// <param name="focus">The focus of the screen it is on.</param>
    /// <param name="showMissions">Switches the page to the mission boards (true) or back; null where the page has none.</param>
    public MissionBoardView(UiTheme theme, ILeaderboardSource source, BoardSize size, Func<FocusManager?> focus, Action<bool>? showMissions = null)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _feed = source.Missions ?? throw new ArgumentException("The source reads no mission boards.", nameof(source));
        _size = size;
        _focus = focus ?? throw new ArgumentNullException(nameof(focus));
        _showMissions = showMissions;
        bool compact = size == BoardSize.Compact;

        _difficulty = LeaderboardModes.Segmented(theme, compact ? DifficultyNames.Select(static d => d.ToUpperInvariant()) : DifficultyNames, compact);
        _difficulty.SelectionChanged += _feed.ShowDifficulty;
        _rules = LeaderboardModes.Segmented(theme, RulesetNames, compact);
        _rules.SelectionChanged += i => _feed.ShowRuleset(MissionBoardFeed.Rulesets[i]);
        _difficultyCycler = new BoardCycler(theme, string.Empty, DifficultyNames, _feed.Difficulty) { ValueWidth = 70f };
        _difficultyCycler.SelectionChanged += _feed.ShowDifficulty;
        _rulesCycler = new BoardCycler(theme, string.Empty, RulesetNames, Math.Max(0, IndexOf(MissionBoardFeed.Rulesets, _feed.Ruleset))) { ValueWidth = 84f };
        _rulesCycler.SelectionChanged += i => _feed.ShowRuleset(MissionBoardFeed.Rulesets[i]);
        _rulesNote = new Label(theme.Mono, string.Empty, 11f, theme.TextMuted) { VerticalAlignment = VerticalAlignment.Center };
        _metrics = LeaderboardModes.Segmented(theme, compact ? ["Fastest", "Best"] : MetricNames, compact);
        _metrics.SelectionChanged += i => _feed.ShowMetric(MissionBoardFeed.Metrics[i]);
        _scroll = new ScrollView(theme);
        _scroll.SetContent(_list);

        // Along the top: the boards' switch; the game, refresh and the site.
        var top = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 10f };
        if (LeaderboardModes.Switch(theme, size, missions: true, showMissions) is { } mode)
        {
            top.Add(mode);
        }

        top.Add(new Panel(), grow: 1f);
        if (GameTabs() is { } games)
        {
            top.Add(games);
        }

        top.Add(LeaderboardModes.Square(theme, KitIcon.Refresh, "Read the board again now.", Refresh, compact));
        if (source.CanOpenLinks && !compact)
        {
            top.Add(LeaderboardModes.SiteButton(theme, compact, () => source.OpenLink(LeaderboardLinks.Site + "?board=missions")));
        }

        // Which board: the mission, the difficulty, the rules; the metric at the right.
        var filters = new FlexPanel { Orientation = Orientation.Horizontal, Gap = compact ? 10f : 26f };
        filters.Add(_missionHost);
        filters.Add(compact ? _difficultyCycler : Labelled("Difficulty", _difficulty, compact));
        var rules = Labelled("Rules", compact ? _rulesCycler : _rules, compact);
        if (!compact)
        {
            rules.Add(_rulesNote);
        }

        filters.Add(rules);
        filters.Add(new Panel(), grow: 1f);
        var metric = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 10f };
        if (!compact)
        {
            metric.Add(LeaderboardModes.Cap(theme, "Tab"));
        }

        metric.Add(_metrics);
        filters.Add(metric);

        // The rows under their headings, the player's best at the foot; the selected run beside them on a monitor.
        var board = new Grid { RowSpacing = 10f };
        board.ColumnDefinitions.Add(GridLength.Star);
        board.RowDefinitions.Add(GridLength.Auto);
        board.RowDefinitions.Add(GridLength.Star);
        board.RowDefinitions.Add(GridLength.Auto);
        var rows = new Grid();
        rows.ColumnDefinitions.Add(GridLength.Star);
        rows.RowDefinitions.Add(GridLength.Auto);
        rows.RowDefinitions.Add(GridLength.Star);
        rows.Add(_columns, 0, 0);
        rows.Add(_scroll, 1, 0);
        board.Add(rows, 1, 0);
        board.Add(_pinned, 2, 0);
        var body = new Grid { ColumnSpacing = 28f };
        body.ColumnDefinitions.Add(GridLength.Star);
        body.RowDefinitions.Add(GridLength.Star);
        body.Add(board, 0, 0);
        if (size == BoardSize.Wide)
        {
            body.ColumnDefinitions.Add(GridLength.Absolute(520f));
            body.Add(_side, 0, 1);
        }

        _root = new Grid { RowSpacing = compact ? 8f : 18f };
        _root.ColumnDefinitions.Add(GridLength.Star);
        UiElement[] parts = compact ? [top, filters, body] : [top, _hero, filters, body];
        for (int i = 0; i < parts.Length; i++)
        {
            _root.RowDefinitions.Add(i == parts.Length - 1 ? GridLength.Star : GridLength.Auto);
            _root.Add(parts[i], i, 0);
        }

        Sync(entrance: false);
    }

    /// <summary>Gets the page.</summary>
    public UiElement Root => _root;

    private bool Compact => _size == BoardSize.Compact;

    private bool Overall => _feed.Mission is null;

    /// <summary>Takes in what the master has sent, and asks for what is shown when it is missing or old.</summary>
    /// <param name="shown">Whether the page is the one up.</param>
    /// <returns><see langword="true"/> when the page changed and is to be laid out again.</returns>
    public bool Update(bool shown)
    {
        // A game whose runs are held for now (GameTitle.RunsHeld) has no boards to show: the first game offered stands in,
        // whichever game the page that holds this was asked to show -- the one being played, the launcher's.
        if (GameTitle.RunsOfferedFor(_feed.Title) is var offered && offered != _feed.Title)
        {
            _feed.ShowTitle(offered);
        }

        string? focused = (_focus()?.Focused as MissionRow)?.Row.Account;
        if (focused is not null && focused != _focusedRun)
        {
            _focusedRun = focused;
            SyncSide();
        }

        _feed.Update(shown);
        if (_feed.Revision == _seen)
        {
            return false;
        }

        Sync(entrance: true);
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

    /// <summary>Reports the run of the row with the focus, where the player may: another's run on a mission's board, once.</summary>
    /// <returns>Whether one was.</returns>
    public bool ReportFocused()
    {
        if (_focus()?.Focused is not MissionRow row)
        {
            return false;
        }

        return Report(row.Row, row);
    }

    /// <summary>Shows the next mission's board, or the one before; every mission together comes before the first.</summary>
    /// <param name="direction">+1 for the next, -1 for the one before.</param>
    public void StepMission(int direction)
    {
        IReadOnlyList<(int Id, string Name)> missions = _source.MissionsOf(_feed.Title);
        int at = _feed.Mission is { } shown ? missions.ToList().FindIndex(m => m.Id == shown) + 1 : 0;
        int next = Wrap(at + Math.Sign(direction), missions.Count + 1);
        _feed.ShowMission(next == 0 ? null : missions[next - 1].Id);
    }

    /// <summary>Shows the next difficulty's board.</summary>
    public void StepDifficulty() => _feed.ShowDifficulty((_feed.Difficulty + 1) % DifficultyNames.Length);

    /// <summary>Shows the other rules' board.</summary>
    public void StepRules() => _feed.ShowRuleset(MissionBoardFeed.Rulesets[Wrap(IndexOf(MissionBoardFeed.Rulesets, _feed.Ruleset) + 1, MissionBoardFeed.Rulesets.Count)]);

    /// <summary>Ranks by the other metric.</summary>
    public void StepMetric() => _feed.ShowMetric(MissionBoardFeed.Metrics[Wrap(IndexOf(MissionBoardFeed.Metrics, _feed.Metric) + 1, MissionBoardFeed.Metrics.Count)]);

    /// <summary>Shows the next game's boards, past any whose runs are held.</summary>
    public void StepGame()
    {
        IReadOnlyList<LeaderboardGame> games = _source.Games;
        int at = Math.Max(0, IndexOfGame(_feed.Title));
        for (int step = 1; step < games.Count; step++)
        {
            string next = games[Wrap(at + step, games.Count)].Id;
            if (GameTitle.All.FirstOrDefault(t => t.Id == next) is not { RunsHeld: true })
            {
                _feed.ShowTitle(next);
                return;
            }
        }
    }

    /// <summary>Reads the board again now.</summary>
    public void Refresh() => _feed.Refresh();

    // Everything shown made to match the feed.
    private void Sync(bool entrance)
    {
        _seen = _feed.Revision;
        if (_builtTitle != _feed.Title)
        {
            _builtTitle = _feed.Title;
            BuildMissionPicker();
            if (_games is { } games)
            {
                games.SelectedIndex = Math.Max(0, IndexOfGame(_feed.Title));
            }
        }

        _difficulty.SelectedIndex = Math.Clamp(_feed.Difficulty, 0, 2);
        _rules.SelectedIndex = Math.Max(0, IndexOf(MissionBoardFeed.Rulesets, _feed.Ruleset));
        _difficultyCycler.SelectedIndex = _difficulty.SelectedIndex;
        _rulesCycler.SelectedIndex = _rules.SelectedIndex;
        _metrics.SelectedIndex = Math.Max(0, IndexOf(MissionBoardFeed.Metrics, _feed.Metric));
        _rulesNote.Text = _feed.Ruleset == "classic" ? "original values" : "remastered values";
        if (_missionHost.Children.Count > 0 && _missionHost.Children[0] is BoardCycler picker)
        {
            int at = _feed.Mission is { } shown ? Math.Max(0, _source.MissionsOf(_feed.Title).ToList().FindIndex(m => m.Id == shown) + 1) : 0;
            if (picker.SelectedIndex != at)
            {
                picker.SelectedIndex = at;
            }
        }

        RunBoard? board = _feed.Board;
        string makeup = $"{_feed.Title}|{_feed.Mission}|{_feed.Difficulty}|{_feed.Ruleset}|{_feed.Metric}|{_feed.BoardState}|{board?.You?.Account}|{board?.You?.Ticks}|{board?.Players}|"
            + string.Join(';', (board?.Rows ?? []).Select(static row => $"{row.Place}:{row.Account}:{row.Ticks}:{row.Rank}:{row.Points}:{row.Replay}"));
        if (makeup != _builtList)
        {
            _builtList = makeup;
            string? kept = (_focus()?.Focused as UiElement)?.Id;
            Fill(_hero, Compact ? [] : [Hero(board)], entrance);
            Fill(_columns, board is { Rows.Count: > 0 } || board is null ? [MissionColumns.Header(_theme, _size, Overall)] : [], entrance);
            Fill(_list, ListParts(board), entrance);
            Fill(_pinned, PinnedParts(board), entrance);
            if (kept is not null && _root.FindById(kept) is Control again && _focus() is { } focus)
            {
                focus.Focus(again);
            }
        }

        SyncSide();
    }

    // What the list holds: the rows' shapes while it is read, why it is empty or can't be read, or the rows.
    private List<UiElement> ListParts(RunBoard? board)
    {
        if (board is null)
        {
            if (_feed.BoardState == FeedState.Failed)
            {
                return [new BoardMessage(_theme, KitIcon.Warning, "The boards can't be reached", "Check your connection; they are asked for again shortly.", warn: true) { Margin = new Thickness(0f, 40f, 0f, 0f) }];
            }

            return [.. Enumerable.Range(0, 8).Select(i => (UiElement)new RankRowSkeleton(i, _size))];
        }

        if (board.Rows.Count == 0)
        {
            return EmptyParts();
        }

        string? you = board.You?.Account ?? _feed.You;
        int leader = board.Rows[0].Ticks;
        List<UiElement> parts = [];
        int previous = 0;
        foreach (RunBoardRow row in board.Rows)
        {
            if (row.Place > previous + 1 && previous > 0)
            {
                parts.Add(new BoardNote(_theme, string.Create(CultureInfo.InvariantCulture, $"· · ·  places {previous + 1}–{row.Place - 1}  · · ·")));
            }

            parts.Add(Row(row, row.Account == you, leader));
            previous = row.Place;
        }

        if (board.Players > board.Rows.Count)
        {
            parts.Add(new BoardNote(_theme, string.Create(CultureInfo.InvariantCulture, $"The top {board.Rows.Count} of {board.Players:N0} ranked")));
        }

        return parts;
    }

    // Nobody on the board: play it, or try the other rules.
    private List<UiElement> EmptyParts()
    {
        string mission = MissionName();
        string detail = Overall
            ? "Nobody has a verified time on every mission at this difficulty and these rules yet."
            : $"Nobody has a verified time on {mission} · {DifficultyNames[_feed.Difficulty]} · {RulesetNames[Math.Max(0, IndexOf(MissionBoardFeed.Rulesets, _feed.Ruleset))]}. Finish it and you set the world record.";
        var parts = new StackPanel { Orientation = Orientation.Vertical, Spacing = 20f, Margin = new Thickness(0f, Compact ? 10f : 40f, 0f, 0f) };
        parts.AddChild(new BoardMessage(_theme, KitIcon.History, Overall ? "No overall times yet" : "No runs on this mission yet", detail));
        var buttons = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 10f, HorizontalAlignment = HorizontalAlignment.Center };
        if (!Overall && _source.CanPlay)
        {
            var play = new BoardButton(_theme, "Play mission", BoardButtonLook.Solid) { Icon = KitIcon.Play, Key = "Enter", ButtonHeight = 40f };
            play.Activated += _ => PlayShown();
            buttons.Add(play);
        }

        string other = _feed.Ruleset == "classic" ? "modern" : "classic";
        var rules = new BoardButton(_theme, $"Try {other} rules", BoardButtonLook.Outline) { Key = "C", ButtonHeight = 40f };
        rules.Activated += _ => _feed.ShowRuleset(other);
        buttons.Add(rules);
        parts.AddChild(buttons);
        return [parts];
    }

    // The player's own best under the list, where it is not in it.
    private List<UiElement> PinnedParts(RunBoard? board)
    {
        if (board?.You is not { } own || board.Rows.Any(row => row.Account == own.Account))
        {
            return [];
        }

        MissionRow line = Row(own, isYou: true, board.Rows.Count > 0 ? board.Rows[0].Ticks : null);
        line.Pinned = true;
        return [line];
    }

    private MissionRow Row(RunBoardRow row, bool isYou, int? leader)
    {
        var line = new MissionRow(_theme, row, _feed.Metric, Overall, _feed.Difficulty)
        {
            IsYou = isYou,
            Size = _size,
            Leader = leader,
            Reported = row.Run is { } id && _reported.Contains(id),
        };
        if (line.HasReplay)
        {
            string run = row.Run!;
            line.Activated += _ => _source.Watch(run);
            line.Raced += _ => _source.Race(run);
        }

        return line;
    }

    // Reports a run, where the player may: another's run on a mission's board, once.
    private bool Report(RunBoardRow row, MissionRow? line)
    {
        if (!_source.CanReport || Overall || row.Run is not { } run || row.Account == (_feed.Board?.You?.Account ?? _feed.You) || !_reported.Add(run))
        {
            return false;
        }

        _source.Report(run);
        if (line is not null)
        {
            line.Reported = true;
        }

        _builtSide = string.Empty;
        SyncSide();
        return true;
    }

    private void PlayShown()
    {
        if (_feed.Mission is { } mission)
        {
            _source.PlayMission(_feed.Title, mission, _feed.Difficulty, _feed.Ruleset);
        }
    }

    private string MissionName() =>
        _feed.Mission is { } id ? _source.MissionsOf(_feed.Title).FirstOrDefault(m => m.Id == id).Name ?? $"Mission {id}" : "All missions";

    // The games whose boards there are, one tab each: a game whose runs are held for now (GameTitle.RunsHeld) shown, dimmed and
    // marked as coming. None where there is one game.
    private TabStrip? GameTabs()
    {
        IReadOnlyList<LeaderboardGame> games = _source.Games;
        if (games.Count < 2)
        {
            return null;
        }

        bool Held(int index) => GameTitle.All.FirstOrDefault(t => t.Id == games[index].Id) is { RunsHeld: true };
        _games = LeaderboardModes.Segmented(_theme, games.Select(game => Compact ? game.Name.Replace("PROJECT ", string.Empty, StringComparison.OrdinalIgnoreCase) : game.Name), Compact);
        _games.TabEnabled = index => !Held(index);
        _games.Notes = [.. games.Select((_, index) => Held(index) ? "SOON" : string.Empty)];
        _games.SelectedIndex = Math.Max(0, IndexOfGame(_feed.Title));
        _games.SelectionChanged += index => _feed.ShowTitle(games[index].Id);
        return _games;
    }

    private int IndexOfGame(string title) => _source.Games.ToList().FindIndex(game => game.Id == title);

    // The mission picker for the game shown: every mission together, then each in campaign order.
    private void BuildMissionPicker()
    {
        IReadOnlyList<(int Id, string Name)> missions = _source.MissionsOf(_feed.Title);
        int at = _feed.Mission is { } shown ? Math.Max(0, missions.ToList().FindIndex(m => m.Id == shown) + 1) : 0;
        var picker = new BoardCycler(_theme, Compact ? string.Empty : "Mission", ["All missions", .. missions.Select(static m => m.Name)], at)
        {
            ValueWidth = Compact ? 110f : 170f,
        };
        picker.SelectionChanged += i => _feed.ShowMission(i == 0 ? null : missions[i - 1].Id);
        _missionHost.ClearChildren();
        _missionHost.AddChild(picker);
        picker.Visual.AppearInstantly();
    }

    // A switch with its name before it.
    private FlexPanel Labelled(string name, UiElement control, bool compact)
    {
        var row = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 10f, VerticalAlignment = VerticalAlignment.Center };
        if (!compact)
        {
            row.Add(new Label(_theme.Body, name, 14f, _theme.TextSecondary) { VerticalAlignment = VerticalAlignment.Center });
        }

        row.Add(control);
        return row;
    }

    private static void Fill(UiElement host, IEnumerable<UiElement> parts, bool entrance)
    {
        host.ClearChildren();
        foreach (UiElement part in parts)
        {
            host.AddChild(part);
        }

        if (entrance)
        {
            Screen.PlayEntrance(host);
            return;
        }

        foreach (Control control in host.Descendants().OfType<Control>())
        {
            control.Visual.AppearInstantly();
        }
    }

    private static int Wrap(int index, int count) => ((index % count) + count) % count;

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
