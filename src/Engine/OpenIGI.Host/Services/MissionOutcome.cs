using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Events;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Config;
using OpenIGI.Game.Events;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Platform;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Services;

/// <summary>What deciding a mission reads of the host, and what it asks the host to do.</summary>
internal interface IMissionOutcomeHost
{
    /// <summary>Gets the mission's objectives and messages, or null before they are built.</summary>
    MissionObjectiveRuntime? Objectives { get; }

    /// <summary>Gets the mission's cut-scenes, or null before they are built.</summary>
    CutsceneRuntime? Cutscenes { get; }

    /// <summary>Gets the title's own player, which can decide a mission itself; null for a title without one.</summary>
    ITitlePlayer? TitlePlayer { get; }

    /// <summary>Gets where the menu stands, which the failed page and the campaign's end put up.</summary>
    MenuFlow Menu { get; }

    /// <summary>Gets the window, whose pointer the failed page frees.</summary>
    IGameWindow Window { get; }

    /// <summary>Gets the player profile in use, whose campaign a completion unlocks; null for none.</summary>
    PlayerProfile? ActiveProfile { get; }

    /// <summary>Gets the title's missions, which say what follows a completed one.</summary>
    IMissionCatalogue<MissionDescriptor> Missions { get; }

    /// <summary>Gets the session's events, which are told the mission is decided; null before a session is up.</summary>
    GameEvents? Events { get; }

    /// <summary>Closes the map computer, if it is open, and tells the player it has gone.</summary>
    void CloseComputer();

    /// <summary>Tells the player's look to let go of the pointer and hold still.</summary>
    void HoldPlayerStill();

    /// <summary>Writes the player's settings and profiles.</summary>
    void SaveConfiguration();

    /// <summary>Starts a mission, releasing the one being played.</summary>
    /// <param name="id">The mission to start.</param>
    void StartMission(int id);

    // A co-op client's: the mission goes on, or again, where its server takes it.
    bool FollowsSession { get; }

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
                && host.Cutscenes?.IsPlaying != true
                    ? RemasteredMessageHoldSeconds
                    : 0f;
        }

        host.Objectives?.TickAfterLevelState();
        if (host.Objectives is { IsFailed: true } || host.TitlePlayer is { Outcome: TitleMissionOutcome.Failed })
        {
            // The retail grace period first; see MissionSession.CountFailure.
            if (session.CountFailure() && state.Shell is not null)
            {
                session.FailedMenu = true;
                if (session.Mission is { } failedMission)
                {
                    host.Events?.Raise(new MissionEnded(failedMission.Id, Completed: false));
                }

                // Said out loud because the level decided this, not the player: a LevelFlow
                // 'Failed' expression is authored out of message and area state, and when it
                // turns for a reason nobody in the room understands this line is where the
                // reading starts.
                logger.Information(
                    "Mission",
                    $"The level's LevelFlow has failed the mission; opening the failed page.");
                host.CloseComputer();

                // The result is a shell page, not a drawn card, so its two choices can be clicked
                // and navigated like every other page and the shell's own cursor is the one on
                // screen. Menu mode hands it the frame's input and draw; the pause flag holds the
                // showcase off, because the level this is drawn over is still loaded -- the same
                // pair the pause menu sets.
                host.Menu.MenuMode = true;
                host.Menu.PauseMenu = true;
                state.Shell.OpenMissionFailed();

                // The platform cursor stays hidden: the shell draws its own, so revealing this one
                // would put a second, unrelated pointer beside it.
                host.Window.CursorVisible = false;
                host.HoldPlayerStill();
                host.Window.CursorLocked = false;
            }
        }
        else
        {
            session.ClearFailure();
        }
        if (!session.ResultHandled
            && (host.Objectives is { IsComplete: true } || host.TitlePlayer is { Outcome: TitleMissionOutcome.Completed })
            && session.Mission is { } completedMission)
        {
            session.ResultHandled = true;
            host.Events?.Raise(new MissionEnded(completedMission.Id, Completed: true));
            // A successor the catalogue does not offer ends the campaign the way the last mission
            // does -- in a build cut to the opening missions, the last one offered is its finale.
            int nextMissionId = completedMission.NextMissionId;
            if (nextMissionId != MissionDescriptor.NextMissionUndefined && host.Missions.TryGet(nextMissionId, out _))
            {
                if (host.ActiveProfile is { } activeProfile)
                {
                    MissionProgression.UnlockNext(activeProfile, host.Missions, nextMissionId);
                    // Retail writes config.qsc to the LOCAL device after every non-final completion, even when
                    // replaying an earlier mission leaves the frontier unchanged.
                    host.SaveConfiguration();
                }

                host.TitlePlayer?.MissionCompleted(nextMissionId);
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
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}
