using OpenIGI.Core.Configuration;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Host.DevTools;
using OpenIGI.Host.Multiplayer;
using OpenIGI.Input;

namespace OpenIGI.Host;

/// <content>
/// A team match's team menu: up by itself the first time this player is in a team match of a session's mode, and on its key
/// (M) after, to switch; a click on a side, or its number, asks the server for it (<c>/team</c>), and the menu shuts on the
/// choice, on its key again, or on Escape. The server has put a player who never chooses on the fairer side already.
/// </content>
public sealed partial class GameHost
{
    private bool _teamMenuUp;
    private bool _teamKeyWasDown;

    // The session and mode the menu last came up by itself for: once for each, so a mode switched to teams asks again.
    private (object? Session, string Mode) _teamMenuShownFor;

    // The number keys, in the order the menu numbers its choices.
    private static readonly KeyCode[] TeamNumberKeys = [KeyCode.D1, KeyCode.D2, KeyCode.D3];

    // Once a frame, before the game reads its input: the menu opened or shut, and a side asked for.
    private void UpdateTeamMenu()
    {
        MatchHud? hud = TickObserver is ISessionScoreboard && !_menuFlow.MenuMode ? CurrentMatchHud() : null;
        bool keyDown = hud is not null && BindKeyNames.TryParse(TeamMenuCVars.Key.Value, out KeyCode key) && _window.Input.Keyboard.IsDown(key);
        bool pressed = keyDown && !_teamKeyWasDown;
        _teamKeyWasDown = keyDown;

        // Only a team match has sides to choose, and not while its result is up.
        if (hud is not { Teams: not null } || hud.Result is not null || hud.Phase == MatchPhase.Intermission)
        {
            CloseTeamMenu();
            return;
        }

        (object? Session, string Mode) here = (TickObserver, hud.Mode);
        if (!_teamMenuUp)
        {
            bool first = !Equals(_teamMenuShownFor, here);
            if (pressed || first)
            {
                _teamMenuShownFor = here;
                CloseBuyMenu();
                _teamMenuUp = true;
                _window.CursorLocked = false;
                _window.Input.Captured = true;
            }

            return;
        }

        if (pressed || _window.Input.Keyboard.WasPressed(KeyCode.Escape))
        {
            CloseTeamMenu();
            return;
        }

        int? asked = null;
        for (int i = 0; i < TeamNumberKeys.Length; i++)
        {
            if (_window.Input.Keyboard.WasPressed(TeamNumberKeys[i]))
            {
                asked = i;
            }
        }

        if (asked is null && _window.Input.Mouse.WasPressed(MouseButton.Left) && _remasteredHud is { } drawn)
        {
            float x = _window.Input.Mouse.X;
            float y = _window.Input.Mouse.Y;
            foreach ((int index, float cx, float cy, float width, float height) in drawn.TeamMenuCards)
            {
                if (x >= cx && x <= cx + width && y >= cy && y <= cy + height)
                {
                    asked = index;

                    // A click on a card is the menu's, not a shot or a stab, however long the button stays down after it shuts.
                    _window.Input.Mouse.TakePress(MouseButton.Left);
                }
            }
        }

        if (asked is { } choice && TickObserver is ISessionTalk talk)
        {
            // A side it can't join stays shut to a click; the server would refuse it anyway, and says why to a typed one.
            int team = choice == 0 ? Game.Multiplayer.TeamRoster.Good : Game.Multiplayer.TeamRoster.Bad;
            if (choice == 2 || hud.Teams.Open(team))
            {
                talk.Say($"/team {TeamPick.Choices[choice]}");
                CloseTeamMenu();

                // The click chose a side (its press taken above); nor does the rest of this frame reach the game.
                _window.Input.Captured = true;
                return;
            }
        }

        _window.CursorLocked = false;
        _window.Input.Captured = true;
    }

    private void CloseTeamMenu()
    {
        if (_teamMenuUp)
        {
            _teamMenuUp = false;
            _window.CursorLocked = true;
        }
    }

    // The team menu's key's name, as the menu shows it.
    private static string TeamKeyName() => TeamMenuCVars.Key.Value is { Length: > 0 } name ? name : "M";
}

/// <summary>The team menu's settings.</summary>
public static class TeamMenuCVars
{
    /// <summary>The key that opens the team menu, by its bind name.</summary>
    public static readonly StringCVar Key = CVar.Text(
        "mp_team_key", "m", CVarOptions.Archive, "The key that opens a team match's team menu, to switch sides, by its bind name: M.");
}
