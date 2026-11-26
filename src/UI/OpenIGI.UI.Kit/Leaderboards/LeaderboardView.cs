using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Screens;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>
/// The official servers' leaderboard as a page, the launcher's and the game menu's alike: which game, which season (this one,
/// all time or one gone by) and which metric, beside the season's card; the top three on a podium and the rest ranked under
/// them, the player's own row pinned under the list wherever they stand; on a wide page, a panel beside the board on the row
/// with the keys; and a player's record, opened from their row -- their figures this season against all time, their latest
/// matches, their standing on the mission boards and their medals. Everything is read through an
/// <see cref="ILeaderboardSource"/>; the page that holds it calls <see cref="Update"/> each frame and lays out again when it
/// says so.
/// </summary>
public sealed partial class LeaderboardView
{
    // How long the keys rest on a row before its record is read for the panel beside the board.
    private const float PeekDelaySeconds = 0.25f;

    private readonly UiTheme _theme;
    private readonly ILeaderboardSource _source;
    private readonly BoardSize _size;
    private readonly Func<FocusManager?> _focus;
    private readonly Action<bool>? _showMissions;
    private readonly Panel _root = new();

    private int _seen = -1;
    private bool _wasRecord;
    private string? _focusedRow;
    private float _focusedFor;

    /// <summary>Initializes a new instance of the <see cref="LeaderboardView"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="source">What it is shown from.</param>
    /// <param name="size">How much room the page has.</param>
    /// <param name="focus">The focus of the screen it is on, for it to keep the keys on the row they were on as the board is drawn anew.</param>
    /// <param name="showMissions">Switches the page to the mission boards (true) or back; null where the page has none.</param>
    public LeaderboardView(UiTheme theme, ILeaderboardSource source, BoardSize size, Func<FocusManager?> focus, Action<bool>? showMissions = null)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _size = size;
        _focus = focus ?? throw new ArgumentNullException(nameof(focus));
        _showMissions = showMissions;
        _root.AddChild(BuildBoard());
        _root.AddChild(BuildRecord());
        Sync(entrance: false);
    }

    /// <summary>Gets the page.</summary>
    public UiElement Root => _root;

    /// <summary>Gets whether a record is open (over the board), which Back closes.</summary>
    public bool ShowingRecord => Feed.RecordId is not null;

    /// <summary>Gets whether the board can't be read right now: the page offers to read it again.</summary>
    public bool Failed => !ShowingRecord && Feed.Board is null && Feed.BoardState == FeedState.Failed;

    private LeaderboardFeed Feed => _source.Feed;

    private string GameName => _source.Games.FirstOrDefault(game => game.Id == Feed.Title)?.Name ?? Feed.Title;

    private bool Compact => _size == BoardSize.Compact;

    private bool Wide => _size == BoardSize.Wide;

    /// <summary>Takes in what the master has sent, and asks for what is shown when it is missing or old.</summary>
    /// <param name="shown">Whether the page is the one up: nothing is asked for while it isn't.</param>
    /// <param name="elapsedSeconds">The time since the last update.</param>
    /// <returns><see langword="true"/> when the page changed and is to be laid out again before it is drawn.</returns>
    public bool Update(bool shown, float elapsedSeconds = 0f)
    {
        FollowFocus(elapsedSeconds);
        Feed.Update(shown);
        if (Feed.Revision == _seen)
        {
            return false;
        }

        Sync(entrance: true);
        return true;
    }

    /// <summary>Ranks by the next metric, or the one before.</summary>
    /// <param name="direction">+1 for the next, -1 for the one before.</param>
    public void StepMetric(int direction)
    {
        IReadOnlyList<LeaderboardMetric> all = LeaderboardMetric.All;
        int at = Math.Max(0, IndexOf(all, Feed.Metric));
        Feed.ShowMetric(all[Wrap(at + Math.Sign(direction), all.Count)]);
    }

    /// <summary>Shows the next period, or the one before: this season, all time, the seasons gone by.</summary>
    /// <param name="direction">+1 for the next, -1 for the one before.</param>
    public void StepPeriod(int direction)
    {
        if (_periods.Count == 0)
        {
            return;
        }

        int at = Math.Max(0, _periods.IndexOf(Feed.Period));
        Feed.ShowPeriod(_periods[Wrap(at + Math.Sign(direction), _periods.Count)]);
    }

    /// <summary>Shows the next game's board.</summary>
    public void StepGame()
    {
        IReadOnlyList<LeaderboardGame> games = _source.Games;
        if (games.Count < 2)
        {
            return;
        }

        int at = Math.Max(0, games.ToList().FindIndex(game => game.Id == Feed.Title));
        Feed.ShowTitle(games[Wrap(at + 1, games.Count)].Id);
    }

    /// <summary>Reads the board (and the record open) again now.</summary>
    public void Refresh() => Feed.Refresh();

    /// <summary>Opens the board, or the record open, on the site.</summary>
    public void OpenOnSite()
    {
        if (!_source.CanOpenLinks)
        {
            return;
        }

        _source.OpenLink(Feed.RecordId is { } player ? LeaderboardLinks.Record(player, Feed.Title) : LeaderboardLinks.Board(Feed.Title));
    }

    /// <summary>Closes the open record, back to the board.</summary>
    /// <returns><see langword="true"/> when a record was open and is closed; <see langword="false"/> when the board was shown.</returns>
    public bool Back()
    {
        if (!ShowingRecord)
        {
            return false;
        }

        Feed.CloseRecord();
        return true;
    }

    /// <summary>Opens a player's record.</summary>
    /// <param name="player">Their account's id or key's.</param>
    public void OpenRecord(string player) => Feed.OpenRecord(player);

    // The row the keys rest on, read for the panel beside the board once they have rested there a moment.
    private void FollowFocus(float elapsedSeconds)
    {
        if (!Wide || ShowingRecord)
        {
            return;
        }

        string? row = (_focus()?.Focused as RankRow)?.Row.RecordId ?? (_focus()?.Focused as PodiumCard)?.Row.RecordId;
        if (row is null)
        {
            return;
        }

        if (row != _focusedRow)
        {
            _focusedRow = row;
            _focusedFor = 0f;
            SyncSelected();
            return;
        }

        _focusedFor += elapsedSeconds;
        if (_focusedFor >= PeekDelaySeconds && Feed.PeekId != row)
        {
            Feed.Peek(row);
        }
    }

    // Everything shown made to match the feed: the controls set, the board's or the record's body made again when what it
    // shows has changed, and the keys kept where they were.
    private void Sync(bool entrance)
    {
        _seen = Feed.Revision;
        bool record = ShowingRecord;
        _boardPage!.Visibility = record ? Visibility.Collapsed : Visibility.Visible;
        _recordPage!.Visibility = record ? Visibility.Visible : Visibility.Collapsed;
        string? kept = (_focus()?.Focused as UiElement)?.Id;

        SyncGames();
        if (record)
        {
            SyncRecord(entrance);
        }
        else
        {
            SyncBoard(entrance);
        }

        // Into a record, the keys go to its way back; out of one, to the row it was opened from.
        if (record != _wasRecord)
        {
            _wasRecord = record;
            if (record)
            {
                Refocus(_back);
            }
            else
            {
                Refocus(kept is not null ? _root.FindById(kept) as Control : null);
            }

            return;
        }

        if (kept is not null && _root.FindById(kept) is Control again && !ReferenceEquals(again, _focus()?.Focused))
        {
            Refocus(again);
        }
    }

    private void Refocus(Control? control)
    {
        if (control is not null && _focus() is { } focus)
        {
            focus.Focus(control);
        }
    }

    // The games, on both pages' switches.
    private void SyncGames()
    {
        int at = Math.Max(0, IndexOf(_source.Games.Select(static game => game.Id).ToList(), Feed.Title));
        foreach (TabStrip? games in new[] { _boardGames, _recordGames })
        {
            if (games is not null && games.SelectedIndex != at)
            {
                games.SelectedIndex = at;
            }
        }
    }

    // A switch between the games, where there is more than one: picks the board's game, the record's with it.
    private TabStrip? GameSwitch()
    {
        if (_source.Games.Count < 2)
        {
            return null;
        }

        TabStrip strip = Segmented(_source.Games.Select(static game => game.Name));
        strip.SelectionChanged += index => Feed.ShowTitle(_source.Games[index].Id);
        return strip;
    }

    // The switch between the matches' boards and the missions', where the page has both.
    private TabStrip? ModeSwitch(bool missions) => LeaderboardModes.Switch(_theme, _size, missions, _showMissions);

    // A segmented switch in the boards' look.
    private TabStrip Segmented(IEnumerable<string> tabs) => LeaderboardModes.Segmented(_theme, tabs, Compact);

    private IconButton Icon(KitIcon icon, string tooltip, Action act)
    {
        var button = new IconButton(_theme, icon) { Tooltip = tooltip, VerticalAlignment = VerticalAlignment.Center };
        button.Activated += _ => act();
        return button;
    }

    private BoardButton SiteButton() => LeaderboardModes.SiteButton(_theme, Compact, OpenOnSite);

    // A part of the page made again: its controls fade in as a page's do, or are simply there when it is the same part read
    // again (a control made after the page came in is otherwise never shown).
    private static void Fill(UiElement host, IEnumerable<UiElement> parts, bool entrance)
    {
        host.ClearChildren();
        foreach (UiElement part in parts)
        {
            host.AddChild(part);
        }

        Appear(host, entrance);
    }

    private static void Appear(UiElement part, bool entrance)
    {
        if (entrance)
        {
            Screen.PlayEntrance(part);
            return;
        }

        foreach (Control control in part.Descendants().OfType<Control>().Prepend(part as Control).OfType<Control>())
        {
            control.Visual.AppearInstantly();
        }
    }

    private static int Wrap(int index, int count) => ((index % count) + count) % count;

    private static int IndexOf<T>(IReadOnlyList<T> list, T item)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(list[i], item))
            {
                return i;
            }
        }

        return -1;
    }
}
