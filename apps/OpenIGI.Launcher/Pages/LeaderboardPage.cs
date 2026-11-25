using OpenIGI.Releases.Accounts;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Leaderboards;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher.Pages;

/// <summary>
/// The launcher's Leaderboard section: the official servers' boards and players' records, drawn by the same view the game's
/// menu draws them with (<see cref="LeaderboardView"/>). It opens on the game picked in the header, and follows the header when
/// another is picked; a player's record opens from their row, or from MY RECORD in the account panel. Its switch shows the
/// single-player mission boards instead (<see cref="MissionBoardView"/>), whose runs are watched and raced in the game.
/// </summary>
internal sealed class LeaderboardPage
{
    private readonly LauncherModel _model;
    private readonly LeaderboardView _view;
    private readonly MissionBoardView _missions;
    private readonly Panel _shown = new();
    private string _followed;
    private bool _showMissions;

    // Whether the page was switched since it was last laid out.
    private bool _switched;

    /// <summary>Initializes a new instance of the <see cref="LeaderboardPage"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="model">The launcher's state.</param>
    /// <param name="pictures">Reads a picture file, for the players' pictures.</param>
    /// <param name="focus">The screen's focus.</param>
    public LeaderboardPage(UiTheme theme, LauncherModel model, Func<string, UiPicture?> pictures, Func<FocusManager?> focus)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _followed = model.PickedTitle;
        model.Leaderboard.ShowTitle(_followed);
        model.Missions.ShowTitle(_followed);
        Source source = new(model, pictures);

        // The launcher's page has a laptop's room: the boards without the panel beside them. Matches or missions are switched
        // at the top of each.
        _view = new LeaderboardView(theme, source, BoardSize.Medium, focus, Switch);
        _missions = new MissionBoardView(theme, source, BoardSize.Medium, focus, Switch);
        _shown.AddChild(_view.Root);
        var page = new Grid { Padding = new Thickness(48f, 20f, 48f, 20f) };
        page.ColumnDefinitions.Add(GridLength.Star);
        page.RowDefinitions.Add(GridLength.Star);
        page.Add(_shown, 0, 0);
        Root = page;
    }

    /// <summary>Gets the page.</summary>
    public UiElement Root { get; }

    /// <summary>Gets whether a record is open, which Back closes before it leaves the section.</summary>
    public bool ShowingRecord => _view.ShowingRecord;

    /// <summary>Follows the header's game, takes in what the master sent, and asks for what is shown while the page is up.</summary>
    /// <param name="shown">Whether the page is the one up.</param>
    /// <returns><see langword="true"/> when the page changed and is to be laid out again.</returns>
    public bool Update(bool shown)
    {
        if (_model.PickedTitle != _followed)
        {
            _followed = _model.PickedTitle;
            _model.Leaderboard.ShowTitle(_followed);
            _model.Missions.ShowTitle(_followed);
        }

        bool changed = _showMissions ? _missions.Update(shown) : _view.Update(shown);
        changed |= _switched;
        _switched = false;
        return changed;
    }

    // The matches' boards or the missions', in the page's place.
    private void Switch(bool missions)
    {
        if (_showMissions == missions)
        {
            return;
        }

        _showMissions = missions;
        _shown.ClearChildren();
        _shown.AddChild(missions ? _missions.Root : _view.Root);
        OpenIGI.UI.Screens.Screen.PlayEntrance(_shown);
        _switched = true;
    }

    /// <summary>Closes the open record.</summary>
    /// <returns><see langword="true"/> when one was open.</returns>
    public bool Back() => _view.Back();

    /// <summary>Shows the board, or the signed-in player's own record.</summary>
    /// <param name="own">Whether to open the player's own record.</param>
    public void Show(bool own)
    {
        if (own && _model.Leaderboard.You is { } you)
        {
            _view.OpenRecord(you);
        }
        else
        {
            _view.Back();
        }
    }

    // The launcher's account, avatars and browser, as the view asks for them.
    private sealed class Source(LauncherModel model, Func<string, UiPicture?> pictures) : ILeaderboardSource
    {
        public LeaderboardFeed Feed => model.Leaderboard;

        public IReadOnlyList<LeaderboardGame> Games => LeaderboardGame.Official;

        public string? SignInHint => model.SignedInForBoard ? null : "Sign in with Discord from the account panel, top right, to be on the board under your name.";

        public bool CanOpenLinks => true;

        public UiPicture? Avatar(string? url)
        {
            if (AvatarCache.PngUrl(url) is not { } png || model.BoardAvatars.PathOf(png) is not { } file)
            {
                return null;
            }

            if (pictures(file) is { } picture)
            {
                return picture;
            }

            model.BoardAvatars.Unusable(png);
            return null;
        }

        public string MapName(string title, string map) => LeaderboardGame.MapName(title, map);

        public void OpenLink(string url) => model.OpenLink(url);

        public MissionBoardFeed Missions => model.Missions;

        public void Watch(string run) => model.PlayBoardRun(run, race: false);

        public void Race(string run) => model.PlayBoardRun(run, race: true);
    }
}
