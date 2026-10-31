using OpenIGI.Game.Config;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Host.Multiplayer;
using OpenIGI.Input;
using OpenIGI.Presentation;

namespace OpenIGI.Host;

/// <summary>The multiplayer scoreboard: its key, and the host's hand on it.</summary>
public sealed partial class GameHost
{
    // Whether the board is up this frame, settled before anything reads the input; whether the host's pointer was let go on
    // it; and the player whose removal waits for a second click.
    private bool _scoreboardUp;
    private bool _scoreboardPointer;
    private int? _confirmingRemoval;

    // Once a frame, before the game reads its input: the board up while its key is held, and -- for the room's host -- the
    // pointer free over it, the view and the gun let alone, and a click on a player's REMOVE asking again before it acts.
    private void UpdateScoreboard()
    {
        _scoreboardUp = TickObserver is ISessionScoreboard && !_menuFlow.MenuMode && ScoreboardHeld();
        bool hosting = _scoreboardUp && TickObserver is HostedSession;
        if (hosting != _scoreboardPointer)
        {
            _scoreboardPointer = hosting;
            _window.CursorVisible = hosting;
            _window.CursorLocked = !hosting;
            _confirmingRemoval = null;
        }

        if (!hosting || TickObserver is not HostedSession hosted)
        {
            return;
        }

        if (_window.Input.Mouse.WasPressed(MouseButton.Left) && _remasteredHud is { } hud)
        {
            float x = _window.Input.Mouse.X;
            float y = _window.Input.Mouse.Y;
            (int Entity, float X, float Y, float Width, float Height)? hit = hud.ScoreboardRemoveButtons
                .Cast<(int Entity, float X, float Y, float Width, float Height)?>()
                .FirstOrDefault(b => x >= b!.Value.X && x <= b.Value.X + b.Value.Width && y >= b.Value.Y && y <= b.Value.Y + b.Value.Height);
            if (hit is not { } button)
            {
                _confirmingRemoval = null;
            }
            else if (_confirmingRemoval != button.Entity)
            {
                _confirmingRemoval = button.Entity;
            }
            else if (hosted.CurrentScoreboard?.Lines.FirstOrDefault(l => l.Entity == button.Entity) is { Name.Length: > 0 } line)
            {
                hosted.Kick(line.Name);
                _confirmingRemoval = null;
            }
        }

        _window.Input.Captured = true;
    }

    // What the host's hand on the board draws: a remove button on each player but its own.
    private ScoreboardControls? ScoreboardHand(Scoreboard board) =>
        _scoreboardPointer
            ? new ScoreboardControls(
                board.Lines.Where(static l => l.Entity > ServerSession.HostEntity).Select(static l => l.Entity).ToHashSet(),
                _window.Input.Mouse.X,
                _window.Input.Mouse.Y,
                _confirmingRemoval)
            : null;

    // Whether the scoreboard's key is held: Tab unless rebound. A profile saved before the board existed has no line for it
    // and is read against the shipped defaults for this one action, so the key works without a reset.
    private bool ScoreboardHeld()
    {
        PlayerProfile? profile = _configuration.Active;
        if (PlayerProfileInputBindings.BoundKeyName(profile, PlayerProfile.ScoreboardAction) is null)
        {
            profile = null;
        }

        return PlayerProfileInputBindings.IsDown(profile, _window.Input, PlayerProfile.ScoreboardAction);
    }
}
