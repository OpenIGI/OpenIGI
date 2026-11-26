using System.Diagnostics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Game.Player;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>
/// The kill feed in single player, when <see cref="HudCVars.KillFeedSinglePlayer"/> asks for it: the player's kills of the
/// level's soldiers, read off its tally as a co-op session's world reads them (<see cref="ShotTallyWatch"/>), and listed and
/// drawn as a session's are.
/// </summary>
public sealed partial class GameHost
{
    // The player's entity in its own feed: there is no session to number it.
    private const int SoloEntity = 0;

    private readonly KillFeed _soloFeed = new();
    private readonly ShotTallyWatch _soloTally = new();
    private readonly Stopwatch _soloClock = Stopwatch.StartNew();

    // A kill of the player's lists a guard under its name, with the weapon in hand. The tally is watched with the setting off as
    // well, so turning it on does not announce what was killed before.
    private void NoteSoloKills(IShotTallySource? source, ShotTally? tally)
    {
        if (tally is null || TickObserver is ISessionScoreboard || _soloTally.Take(tally) is not { Killed: true } news
            || !HudCVars.KillFeedSinglePlayer.Value)
        {
            return;
        }

        string name = _configuration.Active?.Name is { Length: > 0 } profile ? profile : "You";
        MatchPlayer player = new(SoloEntity, name, -1, 0);
        for (int i = 0; i < news.Kills; i++)
        {
            // Only the last of several kills at once is known to have been to the head.
            bool head = news.Head && i == news.Kills - 1;
            _soloFeed.Add(KillNote.OfGuard(player, SoloEntity, source?.PlayerWeapon ?? string.Empty, head), _soloClock.Elapsed.TotalSeconds);
        }
    }

    // The single player's feed, or null when it is off or lists nothing.
    private KillFeedView? SoloKillFeedShown()
    {
        if (!HudCVars.KillFeedSinglePlayer.Value
            || _soloFeed.Entries(_soloClock.Elapsed.TotalSeconds, SoloEntity) is not { Count: > 0 } kills)
        {
            return null;
        }

        return KillFeedView(kills, SoloEntity, -1, teams: false, coop: true);
    }

    // A feed's view with the title's weapon pictures: a session's, or the single player's own.
    private KillFeedView KillFeedView(IReadOnlyList<KillFeedEntry> kills, int own, int team, bool teams, bool coop) =>
        new(kills, own, team, teams, coop, _hudOverlay as IKillFeedArt ?? _titleHud as IKillFeedArt);
}
