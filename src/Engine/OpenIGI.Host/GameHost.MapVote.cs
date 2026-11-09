using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Host.Multiplayer;
using OpenIGI.Input;

namespace OpenIGI.Host;

/// <content>
/// The vote for the next map, as a player casts it: while a decided match's card shows the maps offered, the pointer is free over
/// them, and a click on one or its number key picks it (<c>/1</c> to the server); a pick can be changed until the vote closes.
/// </content>
public sealed partial class GameHost
{
    // Whether the vote's cards hold the pointer.
    private bool _mapVoteUp;

    // The number keys, in the order the vote numbers its maps.
    private static readonly KeyCode[] VoteNumberKeys = [KeyCode.D1, KeyCode.D2, KeyCode.D3, KeyCode.D4];

    // Once a frame, before the game reads its input.
    // Whether the vote for the next map is open with a scoreboard to show beside it: then everyone sees the board, no key held.
    private bool VoteBesideBoard() =>
        !_menuFlow.MenuMode && CurrentMatchHud()?.MapVote is not null && TickObserver is ISessionScoreboard { CurrentScoreboard: not null };

    private void UpdateMapVote()
    {
        NextMapVote? vote = _menuFlow.MenuMode ? null : CurrentMatchHud()?.MapVote;
        if (vote is null || TickObserver is not ISessionTalk talk)
        {
            if (_mapVoteUp)
            {
                _mapVoteUp = false;
                _window.CursorLocked = true;
            }

            return;
        }

        // A line being typed has the keys.
        if (_chatEntry is not null)
        {
            return;
        }

        _mapVoteUp = true;

        int? picked = null;
        for (int i = 0; i < VoteNumberKeys.Length && i < vote.Choices.Count; i++)
        {
            if (_window.Input.Keyboard.WasPressed(VoteNumberKeys[i]))
            {
                picked = i;
            }
        }

        if (picked is null && _window.Input.Mouse.WasPressed(MouseButton.Left) && _remasteredHud is { } hud)
        {
            float x = _window.Input.Mouse.X;
            float y = _window.Input.Mouse.Y;
            foreach ((int index, float cx, float cy, float width, float height) in hud.MapVoteCards)
            {
                if (x >= cx && x <= cx + width && y >= cy && y <= cy + height)
                {
                    picked = index;

                    // A click on a card is the vote's: never a shot when the next map comes up with the button still down.
                    _window.Input.Mouse.TakePress(MouseButton.Left);
                }
            }
        }

        if (picked is { } index2 && index2 != vote.OwnPick)
        {
            talk.Say(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"/{index2 + 1}"));
        }

        // Read first, then taken: the game beneath sees an idle keyboard and mouse while the cards are up.
        _window.CursorLocked = false;
        _window.Input.Captured = true;
    }
}
