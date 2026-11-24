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
/// the board before it leaves the section.
/// </summary>
public sealed class LeaderboardScreen : ShellScreen
{
    private LeaderboardView? _view;
    private bool _showingRecord;

    /// <summary>Initializes a new instance of the <see cref="LeaderboardScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    public LeaderboardScreen(ShellContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Leaderboard;

    /// <inheritdoc/>
    protected override IReadOnlyList<KeyHint> Hints => _view?.ShowingRecord == true
        ? [new("↑ ↓", "Scroll"), new("Esc", "Leaderboard")]
        : [new("Tab", "Metric"), new("↑ ↓", "Select"), new("Enter", "Record"), new("Esc", "Back")];

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
        page.Add(_view.Root, 0, 0);
        return page;
    }

    /// <inheritdoc/>
    /// <remarks>The board opens on the game being played, whichever the page last showed.</remarks>
    protected override void OnEnter() => Source?.Feed.ShowTitle(Context.ActiveTitleId);

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
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
        if (_view is { } view)
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
