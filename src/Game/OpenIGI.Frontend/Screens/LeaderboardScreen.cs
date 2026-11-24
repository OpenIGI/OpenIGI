using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Kit.Leaderboards;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The Leaderboard section: the official servers' boards and players' records, drawn by the same view the launcher draws them
/// with (<see cref="LeaderboardView"/>). It opens on the game being played; its own switch shows the other game's board
/// without changing the game. Tab steps the metric, Enter on a row opens that player's record, and Back on a record returns to
/// the board before it leaves the section. Where the build reads them, M switches to the single-player mission boards
/// (<see cref="MissionBoardView"/>), whose rows Enter watches and R races.
/// </summary>
public sealed class LeaderboardScreen : ShellScreen
{
    private LeaderboardView? _view;
    private MissionBoardView? _missions;
    private bool _showingRecord;

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
    protected override IReadOnlyList<KeyHint> Hints => ShowingMissions
        ? [new("M", "Matches"), new("↑ ↓", "Select"), new("Enter", "Watch"), new("R", "Race"), new("Esc", "Back")]
        : _view?.ShowingRecord == true
            ? [new("↑ ↓", "Scroll"), new("Esc", "Leaderboard")]
            : [.. Source?.Missions is null ? [] : new KeyHint[] { new("M", "Missions") }, new("Tab", "Metric"), new("↑ ↓", "Select"), new("Enter", "Record"), new("Esc", "Back")];

    private bool ShowingMissions => _showMissions && _missions is not null;

    private ILeaderboardSource? Source => Context.Host.Leaderboard;

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

        _view = new LeaderboardView(theme, source, compact, () => Focus);
        _missions = source.Missions is null ? null : new MissionBoardView(theme, source, compact, () => Focus);
        _showingRecord = _view.ShowingRecord;
        bool roomy = Size == ShellSize.Wide;
        var page = new Grid
        {
            Margin = compact
                ? new Thickness(SideMargin, 10f, SideMargin, 10f)
                : new Thickness(SideMargin, roomy ? 32f : 24f, SideMargin, roomy ? 24f : 16f),
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

        if (_view?.Update(shown: true) != true)
        {
            return;
        }

        RequestLayout();
        if (_view.ShowingRecord != _showingRecord)
        {
            _showingRecord = _view.ShowingRecord;
            RefreshHints();
        }
    }

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, InputState input)
    {
        ArgumentNullException.ThrowIfNull(nav);
        ArgumentNullException.ThrowIfNull(input);
        // M switches between the matches' boards and the missions'.
        if (_missions is not null && input.Keyboard.WasPressed(KeyCode.M) && _view?.ShowingRecord != true)
        {
            _showMissions = !_showMissions;
            Invalidate();
            RefreshHints();
            return false;
        }

        if (ShowingMissions && input.Keyboard.WasPressed(KeyCode.R) && _missions!.RaceFocused())
        {
            return false;
        }

        if (!ShowingMissions && _view is { } view)
        {
            if (nav.Back && view.Back())
            {
                return false;
            }

            if (!view.ShowingRecord && input.Keyboard.WasPressed(KeyCode.Tab))
            {
                bool shift = input.Keyboard.IsDown(KeyCode.LeftShift) || input.Keyboard.IsDown(KeyCode.RightShift);
                view.StepMetric(shift ? -1 : 1);
                return false;
            }
        }

        return base.HandleInput(nav, input);
    }
}
