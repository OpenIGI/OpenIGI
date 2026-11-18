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

    // The kill feed as this end's player sees it -- who it is, its side, the title's weapon pictures -- or null with nothing listed.
    private Presentation.Hud.Remastered.KillFeedView? KillFeedShown()
    {
        if (TickObserver is not ISessionScoreboard { KillFeed: { Count: > 0 } kills } session)
        {
            return null;
        }

        int own = session.LocalEntity ?? -1;
        int team = -1;
        foreach (Game.Multiplayer.ScoreboardLine line in session.CurrentScoreboard?.Lines ?? [])
        {
            if (own >= 0 && line.Entity == own)
            {
                team = line.Team;
                break;
            }
        }

        string mode = session.Mode;
        bool coop = mode is "" or Core.Titles.MatchModes.Coop;
        return new Presentation.Hud.Remastered.KillFeedView(
            kills,
            own,
            team,
            Teams: !coop && Core.Titles.MatchModes.HasTeams(mode),
            Coop: coop,
            Art: _hudOverlay as Presentation.Hud.Remastered.IKillFeedArt ?? _titleHud as Presentation.Hud.Remastered.IKillFeedArt);
    }
}
