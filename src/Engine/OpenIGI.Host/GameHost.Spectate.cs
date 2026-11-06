namespace OpenIGI.Host;

/// <content>The dead player's view in a match: a living player of its side watched, the next each time it fires.</content>
public sealed partial class GameHost
{
    // How many times the dead player asked for the next player to watch.
    private int _spectatePick;

    // Who the dead player watches now, or null for its own body (no match, or nobody standing).
    private Game.Multiplayer.Modes.SpectateTarget? Spectated()
    {
        // A demo's viewer watching one of its players.
        if (_demoWatched is { } watched)
        {
            return watched;
        }

        // Alive, the next death starts at its kill cam again.
        if (Viewpoint?.IsDeathViewActive != true)
        {
            _spectatePick = 0;
            return null;
        }

        return TickObserver is ISessionScoreboard session ? session.Spectate(_spectatePick) : null;
    }
}
