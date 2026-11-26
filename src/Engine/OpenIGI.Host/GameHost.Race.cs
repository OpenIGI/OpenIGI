using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
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

    // The ghost's cloak, and the moment it was first shown, by the host's clock (null before).
    private Presentation.Rendering.GhostCloak _ghostCloak = new();
    private long? _ghostShownAt;

    // The ghost's runner as last posed, to tell what happened since; which of the two was ahead (-1 the player, 1 the ghost, 0
    // not yet either); whether its run is over; where it stands, in metres; and how much of it was there when last drawn.
    private Game.Multiplayer.ReplicatedHuman? _ghostLast;
    private int _ghostLead;
    private bool _ghostFinished;
    private Vector3d? _ghostFeetMetres;
    private float _ghostPresence;

    // How far over the ghost's feet its mark stands, in metres: just over its head.
    private const double GhostMarkLift = 2.1;

    // How far ahead, in seconds, one of the two must get before the lead changes hands.
    private const double GhostLeadSeconds = 0.5;

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
        ResetGhost();
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
        ResetGhost();
        ReturnSettings();
    }

    // A fresh ghost: its cloak from the start, so it materialises again, and nothing yet seen of its runner.
    private void ResetGhost()
    {
        _ghostCloak = new Presentation.Rendering.GhostCloak();
        _ghostShownAt = null;
        _ghostLast = null;
        _ghostLead = 0;
        _ghostFinished = false;
        _ghostFeetMetres = null;
        _ghostPresence = 0f;
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

            // Every go at the mission, the ghost comes in again from its start.
            ResetGhost();
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

    // The ghost handed to the picture, as it looks this frame: how long it has been shown, how much of it is there and whether
    // its cloak is holding.
    private void DrawRaceGhost(in Core.Graphics.Camera3D eye)
    {
        if (!Racing)
        {
            return;
        }

        long now = _loop.TimeSource.Milliseconds;
        _ghostShownAt ??= now;
        Core.Graphics.GhostLook look = _ghostCloak.Look((now - _ghostShownAt.Value) / 1000.0);
        _ghostPresence = look.Presence;
        _soldiers?.DrawGhost(eye, look);
        if (_titleFeatures?.WorldModels is { } owned && owned.GhostModels is { Count: > 0 } ghost && TitleWorldModelPresenter is { } models)
        {
            models.DrawWorldGhosts(ghost, eye, _titlePresentationBlend, look);
        }
    }

    /// <summary>The race's HUD: the mark over the ghost far off, how far ahead of it or behind it the player is, and the splits so far.</summary>
    /// <param name="state">The frame loop's state.</param>
    /// <param name="eye">The eye the world was drawn from, which the ghost's mark is projected through.</param>
    /// <param name="horizontalTangent">The tangent of half the view's horizontal field.</param>
    /// <param name="aspectRatio">The view's width over its height.</param>
    private void DrawRaceHud(FrameLoopState state, in Core.Graphics.Camera3D eye, float horizontalTangent, float aspectRatio)
    {
        if (!Racing || _race!.Ghost is not { } ghost || _remasteredHud is not { IsReady: true } hud || _menuFlow.MenuMode)
        {
            return;
        }

        if (_ghostFeetMetres is { } feet && _ghostPresence > 0f)
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            hud.DrawRaceGhostMarker(
                state.Batch,
                new Presentation.Hud.RaceGhostMarker(new Vector3d(feet.X, feet.Y, feet.Z + GhostMarkLift), _ghostPresence),
                eye,
                horizontalTangent,
                aspectRatio,
                SafeWindowWidth,
                SafeWindowHeight);
            state.Batch.End();
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
        if (!CutsceneHeld)
        {
            _racePlayed++;
        }

        int ghostTick = ghost.TickAtPlay(_racePlayed);
        Game.Multiplayer.ReplicatedHuman? frame = ghost.At(Math.Min(ghostTick, ghost.Length - 1));
        _soldiers?.FollowGhost(frame);
        _titleFeatures?.Ghost?.FollowGhost(frame);

        // Where the player stands against the ghost's route, and the run whose objectives are the player's splits.
        if (GhostSource()?.GhostFrame() is { } player)
        {
            _raceClock?.Ran(_racePlayed, player);
        }

        _raceRun ??= _run is not null && _runLoad == _levelLoads ? _runGhost : null;
        NoticeGhost(frame, ghostTick >= ghost.Length - 1);
    }

    // What happened to the ghost's runner this tick, for its cloak to answer, and where it now stands for its mark.
    private void NoticeGhost(Game.Multiplayer.ReplicatedHuman? frame, bool over)
    {
        if (frame is not { } now)
        {
            return;
        }

        _ghostFeetMetres = now.Position.ToVector() / WorldUnits.PerMetre;
        Game.Multiplayer.ReplicatedHuman? last = _ghostLast;
        _ghostLast = now;
        Presentation.Rendering.GhostEvents events = GhostEventsBetween(last, now);

        // The two changing places, either way, once one is clearly ahead: within half a second of each other the lead stands as it
        // was, so a neck-and-neck stretch, the gap wavering about nought, does not fail the cloak over and over.
        if (_raceClock?.DeltaSeconds is { } delta && Math.Abs(delta) >= GhostLeadSeconds)
        {
            int lead = delta > 0.0 ? 1 : -1;
            if (_ghostLead != 0 && lead != _ghostLead)
            {
                events |= Presentation.Rendering.GhostEvents.Overtaken;
            }

            _ghostLead = lead;
        }

        if (over && !_ghostFinished)
        {
            _ghostFinished = true;
            events |= Presentation.Rendering.GhostEvents.Finished;
        }

        _ghostCloak.Notice(events);
    }

    /// <summary>What happened to a ghost's runner between two of its frames: a shot, a hurt, a fall.</summary>
    /// <param name="before">The frame before, or null for the first.</param>
    /// <param name="now">The frame now.</param>
    /// <returns>What happened.</returns>
    internal static Presentation.Rendering.GhostEvents GhostEventsBetween(Game.Multiplayer.ReplicatedHuman? before, Game.Multiplayer.ReplicatedHuman now)
    {
        Presentation.Rendering.GhostEvents events = Presentation.Rendering.GhostEvents.None;
        bool alive = (now.Flags & Game.Multiplayer.ReplicatedHumanCondition.Alive) != 0;
        if (before is not { } last)
        {
            return alive ? events : Presentation.Rendering.GhostEvents.Died;
        }

        bool wasAlive = (last.Flags & Game.Multiplayer.ReplicatedHumanCondition.Alive) != 0;
        if (now.Shots != last.Shots)
        {
            events |= Presentation.Rendering.GhostEvents.Fired;
        }

        if (now.Health < last.Health)
        {
            events |= Presentation.Rendering.GhostEvents.Hurt;
        }

        if (wasAlive && !alive)
        {
            events |= Presentation.Rendering.GhostEvents.Died;
        }

        return events;
    }
}
