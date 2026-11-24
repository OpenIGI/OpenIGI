using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Events;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Config;
using OpenIGI.Game.Events;
using OpenIGI.Game.Player;
using OpenIGI.Game.Scoring;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.World;
using OpenIGI.Platform;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Services;

/// <summary>What deciding a mission reads of the host, and what it asks the host to do.</summary>
internal interface IMissionOutcomeHost
{
    /// <summary>Gets how the mission stands by its level, or null before its objectives are built.</summary>
    IMissionProgress? Objectives { get; }

    /// <summary>Gets a value indicating whether one of the mission's cut-scenes is playing.</summary>
    bool CutscenePlaying { get; }

    /// <summary>Tells the host a single player's mission is decided, so its run is finished with how it ended.</summary>
    /// <param name="completed">Whether it was completed.</param>
    void MissionDecided(bool completed);

    /// <summary>Gets the title's own player, which can decide a mission itself; null for a title without one.</summary>
    ITitlePlayer? TitlePlayer { get; }

    /// <summary>Gets where the menu stands, which the failed page and the campaign's end put up.</summary>
    MenuFlow Menu { get; }

    /// <summary>Gets the window, whose pointer the failed page frees.</summary>
    IGameWindow Window { get; }

    /// <summary>Gets the player profile in use, whose campaign a completion unlocks; null for none.</summary>
    PlayerProfile? ActiveProfile { get; }

    /// <summary>Gets the game being played, by title id: whose progress in the profile a completion moves.</summary>
    string TitleId { get; }

    /// <summary>Gets the title's missions, which say what follows a completed one.</summary>
    IMissionCatalogue<MissionDescriptor> Missions { get; }

    /// <summary>Gets the session's events, which are told the mission is decided; null before a session is up.</summary>
    GameEvents? Events { get; }

    /// <summary>Opens a shell page over the mission, its level still loaded under it: the failed page.</summary>
    void OpenOverMission(Frontend.Shell shell, Action<Frontend.Shell> page);

    /// <summary>Writes the player's settings and profiles.</summary>
    void SaveConfiguration();

    /// <summary>Starts a mission, releasing the one being played, once the load gate says its files can be read.</summary>
    /// <param name="id">The mission to start.</param>
    void StartMission(int id);

    // A co-op client's: the mission goes on, or again, where its server takes it.
    bool FollowsSession { get; }

    // A versus session's: the level is an arena, so its own objectives and failures end nothing; the mode does.
    bool PlaysArena { get; }

    // A dedicated server's: nobody sits at it to choose what a failed mission's page offers.
    bool RunsUnattended { get; }

    // No session with other players: a single player's mission, the only kind retail ranks.
    bool PlaysSolo { get; }

    /// <summary>Gets the co-op session's part in a mission's score, or <see langword="null"/> for a mission played alone.</summary>
    Multiplayer.ISessionMissionScore? ScoreSession { get; }

    /// <summary>Gets the host clock's time now, in milliseconds, which a co-op score's countdown runs on.</summary>
    long Milliseconds { get; }

    // Whether the rank screen is put up at a mission's end: not where nothing is drawn to read it.
    bool ShowsScoreScreen { get; }

    /// <summary>Gets the words a mission's score is told in, in the language being played.</summary>
    MissionScoreWords ScoreWords { get; }

    /// <summary>Lets go of the mission being played.</summary>
    void ReleasePlayedMission();
}

/// <summary>
/// The end of a mission's tick: the objectives' messages held, and the failed page or the next mission once the level has
/// decided it.
/// </summary>
/// <param name="session">The mission being played, which counts the failure's grace and holds the result.</param>
/// <param name="logger">The log the decision is reported to.</param>
/// <param name="host">What deciding a mission reads of the host.</param>
internal sealed class MissionOutcome(MissionSession session, IOpenIgiLogger logger, IMissionOutcomeHost host) : IHostService
{
    // Whether a co-op client has said its mission failed and it waits on its server, so it is said once.
    private bool _followerToldOfFailure;

    /// <summary>How much longer than the original the remaster holds a status message, in seconds.</summary>
    private const float RemasteredMessageHoldSeconds = 3f;

    /// <inheritdoc/>
    public string Name => "Mission outcome";

    /// <summary>Holds the objectives' messages, and opens the failure menu or moves on once the mission is decided.</summary>
    /// <param name="state">The frame loop's state, whose shell the failed page and the campaign's end open on.</param>
    public void Settle(FrameLoopState state)
    {
        if (host.Objectives is { } objectiveTick)
        {
            // The remaster holds a message three seconds longer than the original does, because
            // the authored durations are short and the hold only starts once the last character
            // has been typed. The retail HUD always retains the original timing and message block,
            // irrespective of the independently archived tactical-message preference.
            objectiveTick.ExtraMessageHoldSeconds =
                RemasteredHudPresentation.UsesTacticalMessages(
                    HudCVars.RemasteredHud.Value,
                    HudCVars.TacticalMessages.Value)
                && !host.CutscenePlaying
                    ? RemasteredMessageHoldSeconds
                    : 0f;
        }

        host.Objectives?.TickAfterLevelState();
        if (host.PlaysArena)
        {
            host.Objectives?.Silenced = true;
            session.ClearFailure();
            return;
        }

        if (host.Objectives is { IsFailed: true } || host.TitlePlayer is { Outcome: TitleMissionOutcome.Failed })
        {
            // The retail grace period first; see MissionSession.CountFailure.
            bool failureDue = session.CountFailure();
            if (failureDue && host.RunsUnattended && session.Mission is { } retried)
            {
                // A dedicated server begins the mission again, and its players follow it there. On the failed page, which
                // nobody at the server can click, it stopped answering: no one could join it again until it was restarted.
                host.Events?.Raise(new MissionEnded(retried.Id, Completed: false));
                logger.Information("Mission", $"The level's LevelFlow has failed the mission; a dedicated server begins mission {retried.Id} again.");
                session.ClearFailure();
                host.StartMission(retried.Id);
                return;
            }

            // A co-op client's failure is its server's to settle: the server begins the mission again, or its host chooses on
            // its own failed page, and this client follows it there. On a failed page of its own it stopped ticking its session,
            // went quiet, and was let go by the server two seconds later -- never to follow it anywhere.
            if (failureDue && host.FollowsSession)
            {
                if (!_followerToldOfFailure)
                {
                    _followerToldOfFailure = true;
                    logger.Information("Mission", "The level's LevelFlow has failed the mission; waiting for the server to go on.");
                }

                return;
            }

            if (failureDue && state.Shell is not null)
            {
                session.FailedMenu = true;
                if (session.Mission is { } failedMission)
                {
                    host.Events?.Raise(new MissionEnded(failedMission.Id, Completed: false));
                    host.MissionDecided(completed: false);
                }

                // Said out loud because the level decided this, not the player: a LevelFlow
                // 'Failed' expression is authored out of message and area state, and when it
                // turns for a reason nobody in the room understands this line is where the
                // reading starts.
                logger.Information(
                    "Mission",
                    $"The level's LevelFlow has failed the mission; opening the failed page.");
                host.OpenOverMission(state.Shell, static shell => shell.OpenMissionFailed());
            }
        }
        else
        {
            session.ClearFailure();
            _followerToldOfFailure = false;
        }
        if (!session.ResultHandled
            && (host.Objectives is { IsComplete: true } || host.TitlePlayer is { Outcome: TitleMissionOutcome.Completed })
            && session.Mission is { } completedMission)
        {
            session.ResultHandled = true;
            host.Events?.Raise(new MissionEnded(completedMission.Id, Completed: true));
            host.MissionDecided(completed: true);
            bool recorded = KeepScore(completedMission);

            // A successor the catalogue does not offer ends the campaign the way the last mission
            // does -- in a build cut to the opening missions, the last one offered is its finale.
            int nextMissionId = completedMission.NextMissionId;
            if (HasNext(completedMission))
            {
                if (host.ActiveProfile is { } activeProfile)
                {
                    MissionProgression.UnlockNext(activeProfile, host.TitleId, host.Missions, nextMissionId);
                    // Retail writes config.qsc to the LOCAL device after every non-final completion, even when
                    // replaying an earlier mission leaves the frontier unchanged.
                    host.SaveConfiguration();
                }

                host.TitlePlayer?.MissionCompleted(nextMissionId);
            }
            else if (recorded)
            {
                host.SaveConfiguration();
            }

            // The rank screen holds the level until the player goes on (MissionScoreScreen_RunHandler: Space), or a co-op squad's
            // countdown runs out.
            _completed = completedMission;
            if (session.Score is not null)
            {
                logger.Information("Mission", $"Completed mission {completedMission.Id}; its rank screen is up until the squad goes on.");
                return;
            }

            GoOn(state);
        }
    }

    /// <summary>Takes the rank screen down and goes on to what follows the mission it scored.</summary>
    /// <param name="state">The frame loop's state, whose shell the campaign's end opens on.</param>
    public void ContinueFromScore(FrameLoopState state)
    {
        if (session.Score is null)
        {
            return;
        }

        session.Score = null;
        GoOn(state);
    }

    /// <summary>
    /// Puts up the score a co-op client's server told at the mission's end: this player's own card with the squad's table, held
    /// until the server goes on -- which the client then follows -- and kept as this player's best where it improves on it.
    /// </summary>
    /// <param name="squad">The squad's score.</param>
    public void HearSquadScore(SquadScore squad)
    {
        ArgumentNullException.ThrowIfNull(squad);
        if (session.Mission is not { } mission || host.ScoreSession?.LocalEntity is not { } own)
        {
            return;
        }

        PlayerProfile? profile = host.ActiveProfile;
        if (MissionScoring.ForSquadMember(squad, own, profile?.MissionRecordOf(host.TitleId, mission.Id), host.ScoreWords) is not { } score)
        {
            return;
        }

        Keep(mission, score, new ScoreHold(score.Card, host.Milliseconds + (squad.SecondsLeft * 1000L), CanContinue: false, squad.HostCanContinue));
        if (score.Record is not null)
        {
            host.SaveConfiguration();
        }
    }

    // The mission completed last, which the rank screen holds the way on from.
    private MissionDescriptor? _completed;

    private bool HasNext(MissionDescriptor mission) =>
        mission.NextMissionId != MissionDescriptor.NextMissionUndefined && host.Missions.TryGet(mission.NextMissionId, out _);

    /// <summary>
    /// Scores the mission just completed: a single player's alone, as retail does (<c>Game_CreateMissionScoreScreen</c>); a co-op
    /// squad's, which a server scores player by player and tells them all, each held for <see cref="SquadScore.AutoAdvanceSeconds"/>
    /// unless the host goes on sooner. The record this end's player keeps, and its rank screen where somebody is there to read it.
    /// A co-op client's score comes from its server (<see cref="HearSquadScore"/>).
    /// </summary>
    /// <param name="mission">The mission.</param>
    /// <returns>Whether the profile's record changed.</returns>
    private bool KeepScore(MissionDescriptor mission)
    {
        // A client's score is its server's to tell, and may already be up: its own level deciding late must not take it down.
        if (host.FollowsSession)
        {
            return false;
        }

        session.Score = null;
        if (host.PlaysArena || host.TitlePlayer?.ScoreSheet(mission.Id) is not { } sheet)
        {
            return false;
        }

        TitleMissionRecord? best = host.ActiveProfile?.MissionRecordOf(host.TitleId, mission.Id);
        if (host.PlaysSolo)
        {
            MissionScoreResult score = MissionScoring.Score(sheet, best, host.ScoreWords);
            return Keep(mission, score, new ScoreHold(score.Card, DeadlineMilliseconds: null, CanContinue: true));
        }

        if (host.ScoreSession?.AnnounceMissionScore(sheet) is not { } squad)
        {
            return false;
        }

        // The server's own player, where it has one; a dedicated server shows nothing and only waits out the countdown.
        long deadline = host.Milliseconds + (squad.SecondsLeft * 1000L);
        MissionScoreResult? own = MissionScoring.ForSquadMember(squad, host.ScoreSession.LocalEntity, best, host.ScoreWords);
        logger.Information("Mission", $"Mission {mission.Id} scored for {squad.Members.Count} player(s); the squad goes on in {squad.SecondsLeft} s{(squad.HostCanContinue ? " unless the host goes on sooner" : string.Empty)}.");
        if (own is null)
        {
            session.Score = new ScoreHold(null, deadline, CanContinue: false);
            return false;
        }

        return Keep(mission, own, new ScoreHold(own.Card, deadline, CanContinue: !host.RunsUnattended));
    }

    // Puts a score's hold on the level -- its card only where somebody is there to read it -- and keeps its record as the profile's
    // best where it is better.
    private bool Keep(MissionDescriptor mission, MissionScoreResult score, ScoreHold hold)
    {
        logger.Information("Mission", $"Mission {mission.Id} earned the rank '{score.Card.RankName}'{(score.Record is null ? string.Empty : "; it is the profile's best")}.");
        // Where nothing is drawn, a score nobody can see holds nothing -- unless a co-op squad's countdown has others to wait for.
        MissionScoreCard? card = host.ShowsScoreScreen ? hold.Card : null;
        session.Score = card is null && hold.DeadlineMilliseconds is null ? null : hold with { Card = card };
        if (score.Record is { } record && host.ActiveProfile is { } profile)
        {
            profile.SetMissionRecord(host.TitleId, mission.Id, record);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Keeps a record of a mission -- a single player's or a co-op player's -- from the moment it is first played, as retail's level load does
    /// (<c>Game_LoadHandler</c> calls <c>MissionScoreSettings_UpdateMissionStats</c>): nothing played yet, beside the mission's
    /// requirements, so the menu can show what its ranks ask before it is ever completed.
    /// </summary>
    /// <remarks>
    /// Only where the profile keeps none. Retail also weighs a loaded save's score here, which would keep a mission half played
    /// -- taken in a fraction of the time it allows -- as earning its top rank; the remaster ranks completed missions only.
    /// </remarks>
    public void MissionBegun()
    {
        if (host.PlaysArena
            || host.RunsUnattended
            || session.Mission is not { } mission
            || host.ActiveProfile is not { } profile
            || profile.MissionRecordOf(host.TitleId, mission.Id) is not null
            || host.TitlePlayer?.ScoreSheet(mission.Id) is not { } sheet)
        {
            return;
        }

        profile.SetMissionRecord(host.TitleId, mission.Id, MissionScoring.FreshRecord(sheet));
        host.SaveConfiguration();
    }

    // On from a completed mission: the next one, or the campaign's end.
    private void GoOn(FrameLoopState state)
    {
        if (_completed is not { } completedMission)
        {
            return;
        }

        _completed = null;
        int nextMissionId = completedMission.NextMissionId;
        if (HasNext(completedMission))
        {
            if (host.FollowsSession)
            {
                // The server loads the next one, and this client follows it there.
                logger.Information("Mission", $"Completed mission {completedMission.Id}; following the room's host to {nextMissionId}.");
            }
            else
            {
                logger.Information("Mission", $"Completed mission {completedMission.Id}; loading {nextMissionId}.");
                host.StartMission(nextMissionId);
            }
        }
        else
        {
            logger.Information("Mission", $"Completed final mission {completedMission.Id}; campaign complete.");
            host.ReleasePlayedMission();
            if (state.Shell is not null)
            {
                // Event 7 is the retail outro-screen path. The authored final cutscene has
                // completed before LevelFlow reaches here, so the shell now continues to its
                // campaign-complete destination.
                state.Shell.OpenOn("extras");
                host.Menu.MenuMode = true;
                host.Menu.ShowcasePending = true;
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}
