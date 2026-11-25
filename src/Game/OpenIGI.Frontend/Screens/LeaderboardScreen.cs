using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Kit.Leaderboards;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The Leaderboard section: the official servers' boards and players' records (<see cref="LeaderboardView"/>) and, where the build
/// reads them, the single-player mission boards (<see cref="MissionBoardView"/>), drawn by the same views the launcher draws them
/// with, switched at the page's top or by M. It opens on the game being played; its own switch (or G) shows the other game's
/// board without changing the game. On the matches' boards Tab steps the metric, [ and ] the period, R reads the board again,
/// Enter on a row opens that player's record, and Back on a record returns to the board before it leaves the section. On the
/// missions' boards the arrows step the mission, D the difficulty, C the rules and Tab the metric; Enter watches a row's run, R
/// races it and X reports it.
/// </summary>
public sealed class LeaderboardScreen : ShellScreen
{
    private LeaderboardView? _view;
    private MissionBoardView? _missions;
    private ILeaderboardSource? _source;
    private bool _showingRecord;
    private bool _failed;

    // Whether the mission boards are shown rather than the matches' (kept as the page is built again).
    private bool _showMissions;

    /// <summary>Initializes a new instance of the <see cref="LeaderboardScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    public LeaderboardScreen(ShellContext context)
        : base(context)
    {
    }

    /// <summary>Gets a value indicating whether the page opens on the mission boards rather than the matches'.</summary>
    public bool StartOnMissions
    {
        get => _showMissions;
        init => _showMissions = value;
    }

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Leaderboard;

    /// <inheritdoc/>
    protected override IReadOnlyList<KeyHint> Hints
    {
        get
        {
            if (ShowingMissions)
            {
                List<KeyHint> missions = [new("M", "Multiplayer"), new("← →", "Mission"), new("D", "Difficulty"), new("C", "Rules"), new("Tab", "Board"), new("↑ ↓", "Select"), new("R", "Race"), new("Enter", "Watch")];
                if (Context.Host.Leaderboard?.CanReport == true)
                {
                    missions.Add(new("X", "Report"));
                }

                missions.Add(new("Esc", "Back"));
                return missions;
            }

            if (_view?.ShowingRecord == true)
            {
                return [new("↑ ↓", "Scroll"), new("O", "openigi.com"), new("Esc", "Leaderboard")];
            }

            if (_view?.Failed == true)
            {
                return [new("R", "Retry"), new("O", "openigi.com"), new("Esc", "Back")];
            }

            List<KeyHint> hints = [];
            if (Source?.Missions is not null)
            {
                hints.Add(new("M", "Single player"));
            }

            hints.AddRange([new("G", "Game"), new("Tab", "Metric"), new("[ ]", "Period"), new("↑ ↓", "Select"), new("Enter", "Record"), new("R", "Refresh"), new("Esc", "Back")]);
            return hints;
        }
    }

    private bool ShowingMissions => _showMissions && _missions is not null;

    private ILeaderboardSource? Source => _source ??= Context.Host.Leaderboard is { } boards
        ? new MenuLeaderboardSource(boards, Context.Host, Context.ActiveTitleId, () => PlayerName, GoTo)
        : null;

    private BoardSize BoardSize => Size switch
    {
        ShellSize.Wide => BoardSize.Wide,
        ShellSize.Medium => BoardSize.Medium,
        _ => BoardSize.Compact,
    };

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        UiTheme theme = Context.Theme;
        bool compact = Size == ShellSize.Compact;
        if (Source is not { } source)
        {
            _view = null;
            var notHere = new StackPanel { Orientation = Orientation.Vertical, Spacing = 16f, Margin = new Thickness(SideMargin, 56f) };
            notHere.AddChild(new SectionTitle(theme, "Official servers", "Leaderboard") { HorizontalAlignment = HorizontalAlignment.Left });
            notHere.AddChild(new Label(theme.Body, "This build plays offline: the leaderboards are read from the OpenIGI servers.", 18f, theme.TextMuted) { Wrap = true });
            return notHere;
        }

        Action<bool>? switchBoards = source.Missions is null ? null : ShowMissions;
        _view = new LeaderboardView(theme, source, BoardSize, () => Focus, switchBoards);
        _missions = source.Missions is null ? null : new MissionBoardView(theme, source, BoardSize, () => Focus, switchBoards);
        _showingRecord = _view.ShowingRecord;
        _failed = _view.Failed;
        bool roomy = Size == ShellSize.Wide;
        var page = new Grid
        {
            Margin = compact
                ? new Thickness(SideMargin, 10f, SideMargin, 10f)
                : new Thickness(SideMargin, roomy ? 24f : 20f, SideMargin, roomy ? 20f : 14f),
        };
        page.ColumnDefinitions.Add(GridLength.Star);
        page.RowDefinitions.Add(GridLength.Star);
        page.Add(ShowingMissions ? _missions!.Root : _view.Root, 0, 0);
        return page;
    }

    /// <inheritdoc/>
    /// <remarks>The board opens on the game being played, whichever the page last showed.</remarks>
    protected override void OnEnter()
    {
        Source?.Feed.ShowTitle(Context.ActiveTitleId);
        Source?.Missions?.ShowTitle(Context.ActiveTitleId);
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        if (ShowingMissions)
        {
            if (_missions!.Update(shown: true))
            {
                RequestLayout();
            }

            return;
        }

        if (_view?.Update(shown: true, elapsedSeconds) != true)
        {
            return;
        }

        RequestLayout();
        if (_view.ShowingRecord != _showingRecord || _view.Failed != _failed)
        {
            _showingRecord = _view.ShowingRecord;
            _failed = _view.Failed;
            RefreshHints();
        }
    }

    // The page rebuilt on the matches' boards or the missions'.
    private void ShowMissions(bool missions)
    {
        if (_showMissions == missions)
        {
            return;
        }

        _showMissions = missions;
        Invalidate();
        RefreshHints();
    }

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, InputState input)
    {
        ArgumentNullException.ThrowIfNull(nav);
        ArgumentNullException.ThrowIfNull(input);
        KeyboardState keys = input.Keyboard;

        // M switches between the matches' boards and the missions'.
        if (_missions is not null && keys.WasPressed(KeyCode.M) && _view?.ShowingRecord != true)
        {
            ShowMissions(!_showMissions);
            return false;
        }

        if (ShowingMissions)
        {
            // A key the boards take is used up; anything else goes on to the page (true from it leaves the section).
            return !MissionKeys(keys) && base.HandleInput(nav, input);
        }

        if (_view is not { } view)
        {
            return base.HandleInput(nav, input);
        }

        if (nav.Back && view.Back())
        {
            return false;
        }

        if (keys.WasPressed(KeyCode.O))
        {
            view.OpenOnSite();
            return false;
        }

        if (view.ShowingRecord)
        {
            return base.HandleInput(nav, input);
        }

        bool shift = keys.IsDown(KeyCode.LeftShift) || keys.IsDown(KeyCode.RightShift);
        Action? act = keys switch
        {
            _ when keys.WasPressed(KeyCode.Tab) => () => view.StepMetric(shift ? -1 : 1),
            _ when keys.WasPressed(KeyCode.LeftBracket) => () => view.StepPeriod(-1),
            _ when keys.WasPressed(KeyCode.RightBracket) => () => view.StepPeriod(1),
            _ when keys.WasPressed(KeyCode.G) => view.StepGame,
            _ when keys.WasPressed(KeyCode.R) => view.Refresh,
            _ when keys.WasPressed(KeyCode.F) && Source is { SignedIn: true } => () => Source.FindMatch(),
            _ when keys.WasPressed(KeyCode.P) && Source is { SignedIn: false } => () => Source.OpenProfile(),
            _ => null,
        };
        if (act is null)
        {
            return base.HandleInput(nav, input);
        }

        act();
        return false;
    }

    // The mission boards' own keys: the board picked, and a row's run raced or reported.
    private bool MissionKeys(KeyboardState keys)
    {
        MissionBoardView missions = _missions!;
        bool onRow = Focus?.Focused is MissionRow;
        Action? act = keys switch
        {
            _ when keys.WasPressed(KeyCode.Tab) => missions.StepMetric,
            _ when keys.WasPressed(KeyCode.D) => missions.StepDifficulty,
            _ when keys.WasPressed(KeyCode.C) => missions.StepRules,
            _ when keys.WasPressed(KeyCode.G) => missions.StepGame,
            _ when onRow && keys.WasPressed(KeyCode.Left) => () => missions.StepMission(-1),
            _ when onRow && keys.WasPressed(KeyCode.Right) => () => missions.StepMission(1),
            _ when keys.WasPressed(KeyCode.X) => () => missions.ReportFocused(),
            _ when keys.WasPressed(KeyCode.R) => () => missions.RaceFocused(),
            _ => null,
        };
        act?.Invoke();
        return act is not null;
    }
}
