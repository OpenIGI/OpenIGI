using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <content>
/// The page's keys, and its place kept. The page is built anew whenever what it shows changes -- a line picked, a filter
/// pressed, the list heard again -- and a build that put the keys back on the first control and the list back at its top would
/// throw the player off the line they were on. So each build takes the focus and the list's scroll from the one before it.
/// The keys: TAB between BROWSE and HOST; browsing, the arrows pick the line they move onto, ENTER joins it, F plays the best
/// game listed and R asks for the list again; ENTER in a code's box joins by the code, and ESC shuts the party's.
/// </content>
public sealed partial class MultiplayerScreen
{
    // The ids of the controls the page names: a line's row, by its code after the prefix, and the two code boxes.
    private const string RowIdPrefix = "row:";
    private const string RoomCodeId = "room-code";
    private const string PartyCodeId = "party-code";

    // The page's content as last built, and the list's viewport in it: what the next build takes its place from.
    private UiElement? _page;
    private ScrollView? _listScroll;

    // The control the next build puts the keys on, whatever held them: a box just opened to type in.
    private string? _focusNext;

    // The control this build starts the keys on, and whether the page chose it itself rather than kept it from the last build:
    // one the page chose is chosen again next time, so the keys land on the list once it has lines.
    private Control? _initialFocus;
    private bool _focusChosen;

    // Seconds since the page was made, for telling a second click on a line from a first.
    private float _clock;

    /// <inheritdoc/>
    protected override Control? InitialFocus => _initialFocus;

    /// <inheritdoc/>
    protected override void OnBuilt()
    {
        Control? kept = _keptFocus is { } key && _page is { } page ? Find(page, key) : null;
        _focusChosen = kept is null;
        _initialFocus = kept ?? ChosenFocus();
    }

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, InputState input)
    {
        if (Coop is not { } coop)
        {
            return base.HandleInput(nav, input);
        }

        if (Focus?.Focused is TextBox box)
        {
            if (Entered(nav, input))
            {
                Submit(coop, box);
                return false;
            }
        }
        else if (!IsPointerCaptured && HandleKeys(coop, nav, input))
        {
            return false;
        }

        // The box to join a party by its code takes back for itself: shutting it is what was meant, not leaving the page.
        if (nav.Back && _joiningParty)
        {
            _joiningParty = false;
            Invalidate();
            return false;
        }

        bool leave = base.HandleInput(nav, input);

        // The arrows pick the line they move onto, so the panel on the right follows the keys as it follows a click.
        if (_tab == 0 && nav.Direction is not null && Focus?.Focused is ServerRow onto && _rows.TryGetValue(onto, out CoopRoomInfo? line))
        {
            Select(line.Code);
        }

        return leave;
    }

    // The page's own keys, while no box is typed in. Whether one was pressed and taken.
    private bool HandleKeys(ICoopRooms coop, UiNavigation nav, InputState input)
    {
        if (input.Keyboard.WasPressed(KeyCode.Tab))
        {
            SwitchTab(1 - _tab);
            return true;
        }

        if (_tab != 0)
        {
            return false;
        }

        if (input.Keyboard.WasPressed(KeyCode.F))
        {
            coop.QuickPlay();
            return true;
        }

        if (input.Keyboard.WasPressed(KeyCode.R))
        {
            Refresh(coop);
            return true;
        }

        if (nav.Accept && Focus?.Focused is ServerRow row && _rows.TryGetValue(row, out CoopRoomInfo? room))
        {
            // A line that can't be joined is picked instead, and the panel on the right says why.
            if (Joinable(room))
            {
                coop.Join(room.Code);
            }
            else
            {
                Select(room.Code);
            }

            return true;
        }

        return false;
    }

    // ENTER in a code's box: join by the code.
    private void Submit(ICoopRooms coop, TextBox box)
    {
        if (box.Id == PartyCodeId)
        {
            if (Parties is { } parties)
            {
                JoinParty(parties);
            }
        }
        else if (box.Id == RoomCodeId)
        {
            JoinByCode(coop);
        }
    }

    // Whether a box was entered: accept, but not the space bar, which in a box is a character.
    private static bool Entered(UiNavigation nav, InputState input) =>
        nav.Accept && !input.Keyboard.IsDown(KeyCode.Space) && !input.Keyboard.WasPressed(KeyCode.Space);

    // What the page being replaced leaves the next: which control held the keys, and where the list was scrolled to.
    private (string? Focus, float? Scroll) LeavePlace()
    {
        string? focus = _focusNext ?? (_focusChosen && ReferenceEquals(Focus?.Focused, _initialFocus) ? null : KeyOf(Focus?.Focused as Control));
        _focusNext = null;
        return (focus, _listScroll?.ScrollOffset);
    }

    // The control the page starts the keys on when it keeps none: the line picked, or BROWSE and HOST while there is none.
    private Control? ChosenFocus()
    {
        if (_tab == 0 && _selectedCode is { } code)
        {
            foreach ((ServerRow row, CoopRoomInfo room) in _rows)
            {
                if (room.Code == code)
                {
                    return row;
                }
            }
        }

        return _tabs;
    }

    // A control's name from one build to the next: its id when it has one, else its type and its place among the page's
    // unnamed controls -- which holds while the page keeps its shape, since the lines, which come and go, are named.
    private string? KeyOf(Control? control)
    {
        if (control is null || _page is not { } page)
        {
            return null;
        }

        if (control.Id is { Length: > 0 } id)
        {
            return id;
        }

        int place = 0;
        foreach (Control other in Unnamed(page))
        {
            if (ReferenceEquals(other, control))
            {
                return PlaceKey(other, place);
            }

            place++;
        }

        return null;
    }

    // The control of a page that a name from KeyOf names, or null when the page has none such.
    private static Control? Find(UiElement page, string key)
    {
        if (page.FindById(key) is { } named)
        {
            return named as Control;
        }

        int place = 0;
        foreach (Control control in Unnamed(page))
        {
            if (PlaceKey(control, place) == key)
            {
                return control;
            }

            place++;
        }

        return null;
    }

    private static IEnumerable<Control> Unnamed(UiElement page) => page.Descendants().OfType<Control>().Where(static c => c.Id is null);

    private static string PlaceKey(Control control, int place) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{control.GetType().Name}#{place}");
}
