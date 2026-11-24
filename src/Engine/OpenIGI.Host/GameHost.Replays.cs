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

    /// <summary>
    /// Gets or sets what hears each run's replay as it is finished, written to the library or not (a run that saves no user
    /// files, as a test's): the replay itself.
    /// </summary>
    public Action<ReplayFile>? ReplayMade { get; set; }

    /// <summary>Gets the player's replay library.</summary>
    internal ReplayLibrary Replays => _replayLibrary ??= new ReplayLibrary(_logger);

    /// <summary>Gets the build's version, as the replays and the rooms are told it.</summary>
    internal static string BuildVersion { get; } = typeof(GameHost).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "dev";

    /// <summary>Arms the recorder for the mission just brought up: its run is recorded from the level's first tick.</summary>
    /// <param name="missionId">The mission.</param>
    private void ArmMissionReplay(int missionId)
    {
        FinishRun(ReplayOutcome.Abandoned, "the level was loaded again");
        _runArmed = null;
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

        _logger.Debug("Replays", $"Recording mission {missionId} from its start.");
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

        // A run left has no score of its own: by now the level under it may be the next one.
        MissionScoreSheet? sheet = outcome == ReplayOutcome.Abandoned ? null : TitlePlayer?.ScoreSheet(_runMission);
        MissionScore? score = sheet?.Score;
        (Ruleset rules, string ruleReason) = Ruleset.Of(_runSettings, CVarRegistry.Default, ExperiencePresetRegistry.Default);
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
        ReplayFile replay = new(header, new ReplayResult(outcome, Math.Max(0, run.Length - 1), score, unranked), run);
        ReplayMade?.Invoke(replay);
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
    private string RunContentFingerprint(int missionId)
    {
        string map = missionId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Game.Multiplayer.SessionFileList files = GameplayRules.Build(map, new Multiplayer.HostSessionFiles(_fileSystem, () => GameplayRules, withoutPins: true));
        return ContentFingerprint.Of(files);
    }

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
