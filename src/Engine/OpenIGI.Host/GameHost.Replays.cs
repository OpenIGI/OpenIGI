using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Replays;
using OpenIGI.Game.Scoring;
using OpenIGI.Host.Replays;

namespace OpenIGI.Host;

/// <content>
/// The recorder: every single-player mission played from its start, kept as a replay (<see cref="ReplayFile"/>). From the
/// level's first tick it keeps the input each tick was given, the cutscene's break, the level's fingerprint every second, and
/// any setting that changes how the level plays changed on the way; when the mission is decided -- or left, or a save is loaded
/// over it -- it is written to the player's library, with how it ended and the rules it was played under.
/// </content>
public sealed partial class GameHost
{
    // How often the level's fingerprint is kept, in ticks: a second.
    private const int RunDigestEvery = 30;

    // The shortest run worth keeping when it was left, not decided, in ticks: five seconds.
    private const int ShortestAbandonedRun = 5 * 30;

    // The run being recorded, the level load it records, and the mission.
    private InputTape? _run;
    private int _runLoad = -1;
    private int _runMission;
    private int _runDifficulty;
    private long _runStartedAt;
    private IReadOnlyList<(string Name, string Value)> _runSettings = [];

    // Why it can no longer be ranked, from what the recording saw; empty while nothing stops it.
    private string _runUnranked = string.Empty;

    // The level load a run is armed for -- the mission brought up from its start -- and that mission; null while none is.
    private (int Load, int Mission)? _runArmed;

    private ReplayLibrary? _replayLibrary;
    private bool _runSettingsWatched;

    // The run's runner as a race against it shows it, and the session's events it was wired to hear.
    private GhostTrack? _runGhost;

    // Whether a cutscene held the level's last tick, and which level that was; and the clock that says.
    private (int Load, bool Playing) _sceneHeld = (-1, false);
    private readonly Game.Scoring.CutsceneClock _sceneClock = new();
    private Engine.Events.GameEvents? _runEventsHeard;

    /// <summary>
    /// Gets or sets what hears each run's replay as it is finished, written to the library or not (a run that saves no user
    /// files, as a test's): the replay itself.
    /// </summary>
    public Action<ReplayFile>? ReplayMade { get; set; }

    /// <summary>Gets the player's replay library.</summary>
    internal ReplayLibrary Replays => _replayLibrary ??= new ReplayLibrary(_logger);

    /// <summary>Gets the build's version, as the replays and the rooms are told it.</summary>
    public static string BuildVersion { get; } = typeof(GameHost).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "dev";

    /// <summary>Arms the recorder for the mission just brought up: its run is recorded from the level's first tick.</summary>
    /// <param name="missionId">The mission.</param>
    private void ArmMissionReplay(int missionId)
    {
        FinishRun(ReplayOutcome.Abandoned, "the level was loaded again");
        _runArmed = null;
        BindRace();
        if (BindPlayback())
        {
            // A replay plays on this level: it is not a run of the player's to record.
            return;
        }

        _runArmed = (_levelLoads, missionId);
        if (!_runSettingsWatched)
        {
            _runSettingsWatched = true;
            CVarRegistry.Default.AnyChanged += (_, changed) => RunSettingChanged(changed.Variable);
        }
    }

    /// <summary>
    /// Before the level's next tick: the run begins on the level's first, where a single player plays it from its start; and
    /// the cutscene's break is noted for the tick about to run.
    /// </summary>
    private void RunTickStarting()
    {
        if (_run is not null && (_runLoad != _levelLoads || _session.LevelRoot is null))
        {
            FinishRun(ReplayOutcome.Abandoned, "the level went");
        }

        if (_run is null && _runArmed is { } armed && armed.Load == _levelLoads && TickOfLevel == 0)
        {
            _runArmed = null;
            BeginRun(armed.Mission);
        }

        if (_run is { } run && _runLoad == _levelLoads)
        {
            run.SetBreak(TickOfLevel, _cutsceneSkip.Breaking);
        }
    }

    /// <summary>Records the input the player's tick is given, as given: the window's, or a play-test's replay's.</summary>
    /// <param name="input">The input.</param>
    private void RecordRunInput(in TitlePlayerInput input)
    {
        if (_run is { } run && _runLoad == _levelLoads)
        {
            run.SetInput(TickOfLevel, input);
        }
    }

    /// <summary>After one of the level's ticks: counted, and its fingerprint kept every second.</summary>
    /// <param name="tick">The tick that ran.</param>
    private void RunTicked(int tick)
    {
        if (_run is not { } run || _runLoad != _levelLoads)
        {
            return;
        }

        run.Ran(tick);
        if (_runGhost is { IsFull: false } ghost && GhostSource()?.GhostFrame() is { } frame)
        {
            ghost.Set(tick, frame);
        }

        if ((tick + 1) % RunDigestEvery == 0)
        {
            run.SetDigest(tick, SimulationDigest());
        }

        if (run.IsFull)
        {
            FinishRun(ReplayOutcome.Abandoned, "the run grew past what a replay holds");
        }
    }

    /// <summary>The mission is decided: its run is finished with how it ended and its score.</summary>
    /// <param name="completed">Whether it was completed.</param>
    internal void MissionDecided(bool completed) =>
        FinishRun(completed ? ReplayOutcome.Completed : ReplayOutcome.Failed, string.Empty);

    /// <summary>A save was read onto the level: the run from its start is over where the save took over.</summary>
    private void RunSaveLoaded() => FinishRun(ReplayOutcome.Abandoned, "a save was loaded");

    // The recorder's own reasons a single-player run is not one: a session, the editor's play, a replay played back.
    private bool RunRecordable =>
        ReplayCVars.Record.Value
        && TickObserver is null
        && _editor is not { IsPlaying: true }
        && !Replaying
        && TitlePlayer is not null;

    private void BeginRun(int missionId)
    {
        if (!RunRecordable)
        {
            return;
        }

        _run = new InputTape();
        _runGhost = new GhostTrack();
        HearRunEvents();
        _runLoad = _levelLoads;
        _runMission = missionId;
        _runDifficulty = _mission.PlayerDifficulty;
        _runStartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _runSettings = Ruleset.Snapshot(CVarRegistry.Default);
        _runUnranked = string.Empty;
        if (TitlePlayer?.ScoreSheet(missionId)?.Score is { GameLoads: > 0 })
        {
            // Begun from a save, not from the start: a replay from the start would not be this run.
            _run = null;
            return;
        }

        if (_titleRuntime.Mods.Any())
        {
            _runUnranked = "mods are on";
        }
        else if (!Core.Numerics.SimMathFingerprint.MatchesHere)
        {
            // This machine's arithmetic is not the checker's: the run would part from its playing there, so it stays here.
            _runUnranked = "this platform's arithmetic differs from the boards' checker";
        }

        _logger.Debug("Replays", $"Recording mission {missionId} from its start.");
    }

    // The running mission as its player's ghost is taken from it: the hosted mission's, or the title player's.
    private IGhostSource? GhostSource() => _hosted as IGhostSource ?? TitlePlayer as IGhostSource;

    // The session's events heard for the run's ghost -- an objective done, a kill, the alarm -- once for each session.
    private void HearRunEvents()
    {
        if (_title is not { } title || !title.Services.TryGet(out Engine.Events.GameEvents? events) || ReferenceEquals(events, _runEventsHeard))
        {
            return;
        }

        _runEventsHeard = events;
        events.Subscribe((in Game.Events.ObjectiveChanged changed) =>
        {
            if (changed.State == Game.World.MissionObjectiveState.Complete)
            {
                RunEvent(GhostEventKind.Objective, changed.Slot);
            }
        });
        events.Subscribe((in Game.Events.HumanKilled killed) => RunEvent(GhostEventKind.Kill, killed.Team));
        events.Subscribe((in Game.Events.AlarmChanged alarm) => RunEvent(GhostEventKind.Alarm, alarm.On ? 1 : 0));
    }

    // Something the run's ghost marks, on the tick it happened: the run being recorded's, or the replay being played's, the first
    // time its level runs the tick.
    private void RunEvent(GhostEventKind kind, int detail) => RunEventAt(TickOfLevel, kind, detail);

    private void RunEventAt(int tick, GhostEventKind kind, int detail)
    {
        if (_run is not null && _runLoad == _levelLoads)
        {
            _runGhost?.Event(tick, kind, detail);
        }

        if (_playback is not null && _playbackLoad == _levelLoads && _playbackGhost is { } ghost && tick >= ghost.Length - 1)
        {
            ghost.Event(tick, kind, detail);
        }
    }

    /// <summary>Gets whether a cutscene plays on the level: the host's own, or the title's.</summary>
    private bool CutscenePlaying => _mission.Scenes?.IsPlaying == true || TitlePlayer?.IsCutscenePlaying == true;

    /// <summary>Gets whether the level's last tick was one its cutscenes held (<see cref="Game.Scoring.CutsceneClock"/>).</summary>
    private bool CutsceneHeld => _sceneHeld.Load == _levelLoads && _sceneHeld.Playing;

    // After one of the level's ticks: whether a cutscene held it, as the mission's time reckons it, and a change in that marked on
    // the run's ghost, so a race times both runs as the boards do. A title's own scenes say nothing of being skippable: held whole.
    private void CutsceneTicked(int tick)
    {
        if (_sceneHeld.Load != _levelLoads)
        {
            _sceneClock.Reset();
        }

        bool held = _sceneClock.Tick(CutscenePlaying, _mission.Scenes?.CanBreak == true);
        bool was = CutsceneHeld;
        _sceneHeld = (_levelLoads, held);
        if (held != was)
        {
            RunEventAt(tick, GhostEventKind.Cutscene, held ? 1 : 0);
        }
    }

    // A setting that changes how the level plays, changed while a run is recorded: kept as a command on the tick it came before,
    // so a replay changes it there too; and the run, played under two rules, is ranked under neither.
    private void RunSettingChanged(ICVar variable)
    {
        if (_run is not { } run || _runLoad != _levelLoads || (variable.Flags & CVarOptions.Simulation) == 0)
        {
            return;
        }

        run.Command(TickOfLevel, $"set {variable.Name} {variable.ValueText}");
        if (_runUnranked.Length == 0)
        {
            _runUnranked = $"{variable.Name} was changed during the run";
        }
    }

    // The run, finished: made a replay, told, and kept in the library where this game keeps the player's files.
    private void FinishRun(ReplayOutcome outcome, string why)
    {
        if (_run is not { } run)
        {
            return;
        }

        _run = null;
        if (outcome == ReplayOutcome.Abandoned && run.Length < ShortestAbandonedRun)
        {
            return;
        }

        // A mission is decided as the tick that decides it closes, before the tick is counted: the run holds that tick too, its
        // input already given, or a replay would stop one short of it and never see the mission decided.
        if (outcome != ReplayOutcome.Abandoned && _runLoad == _levelLoads)
        {
            run.Ran(TickOfLevel);
            if (_runGhost is { } deciding && GhostSource()?.GhostFrame() is { } frame)
            {
                deciding.Set(TickOfLevel, frame);
            }
        }

        // A run left has no score of its own: by now the level under it may be the next one.
        MissionScoreSheet? sheet = outcome == ReplayOutcome.Abandoned ? null : TitlePlayer?.ScoreSheet(_runMission);
        MissionScore? score = sheet?.Score;
        (Ruleset rules, string ruleReason) = Ruleset.Of(_runSettings, CVarRegistry.Default, ExperiencePresetRegistry.Default, _titleRuntime.Title.Id);
        string unranked = FirstReason(
            _runUnranked,
            PlaysCutscenes ? string.Empty : "the game was started without its cutscenes",
            ruleReason,
            outcome == ReplayOutcome.Abandoned ? why : string.Empty,
            score is { GameLoads: > 0 } ? "a save was loaded" : string.Empty);

        ReplayHeader header = new(
            BuildVersion,
            _titleRuntime.Title.Id,
            _runMission,
            _runDifficulty,
            rules.Id,
            _runSettings,
            RunContentFingerprint(_runMission),
            PlayerIdentityId(),
            PlayerName(),
            _runStartedAt,
            PlaysCutscenes);
        GhostTrack? ghost = _runGhost;
        _runGhost = null;
        if (outcome != ReplayOutcome.Abandoned)
        {
            ghost?.Event(Math.Max(0, run.Length - 1), GhostEventKind.End, outcome == ReplayOutcome.Completed ? 1 : 0);
        }

        ReplayFile replay = new(header, new ReplayResult(outcome, Math.Max(0, run.Length - 1), score, unranked), run) { Ghost = ghost };
        ReplayMade?.Invoke(replay);
        OfferRunToBoards(replay, sheet);
        if (!_options.SavesUserFiles)
        {
            return;
        }

        if (Replays.Save(replay, ReplayCVars.Keep.Value, sheet is null ? null : s => sheet.Requirements.GetRank(s)) is { } path)
        {
            _logger.Information(
                "Replays",
                $"Kept the run of mission {_runMission} ({outcome.ToString().ToLowerInvariant()}, {run.Length / 30.0:0.#} s, "
                + $"{(unranked.Length == 0 ? rules.Name : "unranked: " + unranked)}) at {path}.");
        }
    }

    // The files the mission played from, as one value: the same files a session's server holds its players to.
    private string RunContentFingerprint(int missionId) => ContentFingerprint.Of(ContentFilesOf(missionId));

    /// <summary>The files a mission's fingerprint is made of: each by path, length and hash, as this installation holds them.</summary>
    /// <param name="missionId">The mission.</param>
    /// <returns>The files.</returns>
    public Game.Multiplayer.SessionFileList ContentFilesOf(int missionId) =>
        GameplayRules.Build(
            missionId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new Multiplayer.HostSessionFiles(_fileSystem, () => GameplayRules, withoutPins: true));

    // The install key's id the player plays under, or nothing while the key is not ready.
    private static string PlayerIdentityId() =>
        Net.Identity.PlayerIdentities.Default is { IsReady: true } identity
            ? Net.Identity.PlayerIdentities.IdOf(identity.PublicKey)
            : string.Empty;

    private static string FirstReason(params ReadOnlySpan<string> reasons)
    {
        foreach (string reason in reasons)
        {
            if (reason.Length > 0)
            {
                return reason;
            }
        }

        return string.Empty;
    }
}
