using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Screens;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>
/// The official servers' leaderboard as a page, the launcher's and the game menu's alike: which game, which season (this one,
/// all time or one gone by) and which metric; the top three on a podium and the rest ranked under them, the player's own row
/// pinned under the list wherever they stand; and a player's record, opened from their row -- their figures this season and
/// all time, their medals and their latest matches. Everything is read through an <see cref="ILeaderboardSource"/>; the page
/// that holds it calls <see cref="Update"/> each frame and lays out again when it says so.
/// </summary>
public sealed partial class LeaderboardView
{
    private readonly UiTheme _theme;
    private readonly ILeaderboardSource _source;
    private readonly bool _compact;
    private readonly Func<FocusManager?> _focus;
    private readonly Panel _root = new();

    private int _seen = -1;
    private bool _wasRecord;

    /// <summary>Initializes a new instance of the <see cref="LeaderboardView"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="source">What it is shown from.</param>
    /// <param name="compact">Whether it is laid out for a narrow page: a phone on its side, a small window.</param>
    /// <param name="focus">The focus of the screen it is on, for it to keep the keys on the row they were on as the board is drawn anew.</param>
    public LeaderboardView(UiTheme theme, ILeaderboardSource source, bool compact, Func<FocusManager?> focus)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _compact = compact;
        _focus = focus ?? throw new ArgumentNullException(nameof(focus));
        _root.AddChild(BuildBoard());
        _root.AddChild(BuildRecord());
        Sync(entrance: false);
    }

    /// <summary>Gets the page.</summary>
    public UiElement Root => _root;

    /// <summary>Gets whether a record is open (over the board), which Back closes.</summary>
    public bool ShowingRecord => Feed.RecordId is not null;

    private LeaderboardFeed Feed => _source.Feed;

    private string GameName => _source.Games.FirstOrDefault(game => game.Id == Feed.Title)?.Name ?? Feed.Title;

    /// <summary>Takes in what the master has sent, and asks for what is shown when it is missing or old.</summary>
    /// <param name="shown">Whether the page is the one up: nothing is asked for while it isn't.</param>
    /// <returns><see langword="true"/> when the page changed and is to be laid out again before it is drawn.</returns>
    public bool Update(bool shown)
    {
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
        Feed.ShowMetric(all[((at + Math.Sign(direction)) % all.Count + all.Count) % all.Count]);
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

        var strip = new TabStrip(_theme, _source.Games.Select(static game => game.Name))
        {
            SegmentHeight = 36f,
            TabPadding = _compact ? 12f : 18f,
            TextSize = _compact ? 14f : 16f,
            VerticalAlignment = VerticalAlignment.Center,
        };
        strip.SelectionChanged += index => Feed.ShowTitle(_source.Games[index].Id);
        return strip;
    }

    // A ghost button of the page's.
    private Button Ghost(string text, string tooltip, Action act, KitIcon icon = KitIcon.None)
    {
        var button = new Button(_theme, text)
        {
            Accent = ButtonAccent.Ghost,
            TextSize = _compact ? 14f : 15f,
            Tracking = 0.12f,
            Padding = new Thickness(_compact ? 12f : 16f, 0f),
            Height = _compact ? 36f : 40f,
            Tooltip = tooltip,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (icon != KitIcon.None)
        {
            button.LeadingGlyph = KitIcons.Glyph(icon);
            button.GlyphSize = 16f;
        }

        button.Activated += _ => act();
        return button;
    }

    private Label Note(string text, float size = 15f) => new(_theme.Body, text, _compact ? size - 1f : size, _theme.TextSecondary) { Wrap = true };

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
