using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Replays;

namespace OpenIGI.Host;

/// <content>
/// A race against a run: its mission played from the start under the settings and difficulty the run was played by, the
/// player's own run recorded as any is, and the run's runner beside it as a ghost (<see cref="GhostTrack"/>), posed each tick
/// from its track and drawn only -- nothing of the level sees, meets or is changed by it. The race goes on over its own mission
/// started again, and ends with any other.
/// </content>
public sealed partial class GameHost
{
    // The run raced, and the level load it is raced on (-1 until its mission is up).
    private ReplayFile? _race;
    private int _raceLoad = -1;

    // The mission a start is bringing up, while the one before it is let go of; null at any other time.
    private int? _missionStarting;

    // When the ghost glitches, and the moment it was first shown, by the host's clock (null before).
    private Presentation.Rendering.GhostGlitch _ghostGlitch = new();
    private long? _ghostShownAt;

    // How the race on this level stands, the player's own run's track it reads the splits from, how many splits were last shown
    // and when the newest was reached, by the host's clock.
    private RaceClock? _raceClock;

    // The ticks of the race's level the player has played, those a cutscene held left out.
    private int _racePlayed;
    private GhostTrack? _raceRun;
    private int _raceSplitsSeen;
    private long _raceSplitAt;

    // How long a split reached is held up in the middle of the screen, in milliseconds.
    private const int RaceSplitHoldMilliseconds = 3000;

    /// <summary>Gets the run being raced, or <see langword="null"/> while none is.</summary>
    public ReplayFile? RacingAgainst => _race;

    /// <summary>Gets how much later than the ghost the player is where they stand, in seconds, or null off its route or out of a race.</summary>
    internal double? RaceDeltaSecondsForTesting => _raceClock?.DeltaSeconds;

    /// <summary>Gets a value indicating whether a race is under way on the level now up.</summary>
    private bool Racing => _race is not null && _raceLoad == _levelLoads;

    /// <summary>Races a run's ghost: its mission brought up afresh under its settings and difficulty, the ghost beside the player.</summary>
    /// <param name="run">The run, which must carry its ghost.</param>
    /// <returns>Why it cannot be raced, or <see langword="null"/> once the race is under way.</returns>
    public string? Race(ReplayFile run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (ReplayProblem(run) is { } problem)
        {
            return problem;
        }

        if (run.Ghost is not { Length: > 0 })
        {
            return "the replay carries no ghost to race";
        }

        StopPlayback();
        StopRace();
        BorrowSettings(run.Header.Settings);
        _race = run;
        _raceLoad = -1;
        _ghostGlitch = new Presentation.Rendering.GhostGlitch();
        _ghostShownAt = null;
        _logger.Information("Replays", $"Racing the ghost of a run of mission {run.Header.Mission}, {run.Tape.Length / 30.0:0.#} s.");
        StartMissionWhenReadable(run.Header.Mission);
        return null;
    }

    /// <summary>Ends the race, if one is on: the ghost taken away and the player's own settings put back.</summary>
    public void StopRace()
    {
        if (_race is null)
        {
            return;
        }

        // The run raced in is over before the settings it was played under are let go of: changed under a run, they would be
        // recorded into it as changed during play.
        FinishRun(ReplayOutcome.Abandoned, "the race was left");
        _race = null;
        _raceLoad = -1;
        _soldiers?.FollowGhost(null);
        _titleFeatures?.Ghost?.FollowGhost(null);
        _ghostShownAt = null;
        ReturnSettings();
    }

    // The difficulty a race's mission is brought up at: the run's own.
    private Game.Ai.AiDifficulty? RaceDifficulty =>
        _race is { } run ? (Game.Ai.AiDifficulty)Math.Clamp(run.Header.Difficulty, 0, 2) : null;

    // A mission has come up: the race is bound to it, its own mission's every start a new go at it.
    private void BindRace()
    {
        if (_race is { } run && _session.Mission?.Id == run.Header.Mission)
        {
            _raceLoad = _levelLoads;
            _raceClock = run.Ghost is { } ghost ? new RaceClock(ghost) : null;
            _racePlayed = 0;
            _raceRun = null;
            _raceSplitsSeen = 0;
        }
    }

    // The level the race was on has gone: the race with it, unless its own mission is being started again.
    private void RaceLevelReleased()
    {
        if (_race is { } run && _raceLoad >= 0 && _missionStarting != run.Header.Mission)
        {
            StopRace();
        }
    }

    // The ghost handed to the picture, as it looks this frame: how long it has been shown and how hard it glitches.
    private void DrawRaceGhost(in Core.Graphics.Camera3D eye)
    {
        if (!Racing)
        {
            return;
        }

        long now = _loop.TimeSource.Milliseconds;
        _ghostShownAt ??= now;
        Core.Graphics.GhostLook look = _ghostGlitch.Look((now - _ghostShownAt.Value) / 1000.0);
        _soldiers?.DrawGhost(eye, look);
        if (_titleFeatures?.WorldModels is { } owned && owned.GhostModels is { Count: > 0 } ghost && TitleWorldModelPresenter is { } models)
        {
            models.DrawWorldGhosts(ghost, eye, _titlePresentationBlend, look);
        }
    }

    /// <summary>The race's HUD: how far ahead of the ghost or behind it the player is, and the splits so far.</summary>
    /// <param name="state">The frame loop's state.</param>
    private void DrawRaceHud(FrameLoopState state)
    {
        if (!Racing || _race!.Ghost is not { } ghost || _remasteredHud is not { IsReady: true } hud || _menuFlow.MenuMode)
        {
            return;
        }

        List<Presentation.Hud.RaceSplit> splits = [];
        if (_raceRun is { } mine)
        {
            foreach ((int slot, int tick, int ghostTick) in RaceClock.Splits(mine, ghost))
            {
                string label = slot < 0 ? "Finish" : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Objective {slot + 1}");
                splits.Add(new Presentation.Hud.RaceSplit(label, tick / 30.0, (tick - ghostTick) / 30.0));
            }
        }

        long now = _loop.TimeSource.Milliseconds;
        if (splits.Count > _raceSplitsSeen)
        {
            _raceSplitsSeen = splits.Count;
            _raceSplitAt = now;
        }

        float shown = splits.Count == 0 ? 0f : 1f - Math.Clamp((now - _raceSplitAt) / (float)RaceSplitHoldMilliseconds, 0f, 1f);
        _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
        state.Batch.Begin();
        hud.DrawRace(state.Batch, new Presentation.Hud.RaceHud(_raceClock?.DeltaSeconds, splits, shown), SafeWindowWidth, SafeWindowHeight);
        state.Batch.End();
    }

    // After a tick of the race's level: the ghost where its runner was after the same tick, or where it ended.
    private void RaceTicked(int tick)
    {
        if (!Racing || _race!.Ghost is not { Length: > 0 } ghost)
        {
            return;
        }

        // Both runs timed in the ticks they played: the ghost waits out the player's cutscenes where its runner's own began.
        if (!CutscenePlaying)
        {
            _racePlayed++;
        }

        Game.Multiplayer.ReplicatedHuman? frame = ghost.At(Math.Min(ghost.TickAtPlay(_racePlayed), ghost.Length - 1));
        _soldiers?.FollowGhost(frame);
        _titleFeatures?.Ghost?.FollowGhost(frame);

        // Where the player stands against the ghost's route, and the run whose objectives are the player's splits.
        if (GhostSource()?.GhostFrame() is { } player)
        {
            _raceClock?.Ran(_racePlayed, player);
        }

        _raceRun ??= _run is not null && _runLoad == _levelLoads ? _runGhost : null;
    }
}
