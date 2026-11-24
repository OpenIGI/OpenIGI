using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Replays;

namespace OpenIGI.Host;

/// <content>
/// A replay played back: its mission brought up afresh under the settings it was recorded with, and given its inputs tick by
/// tick through the play-test's replay (<see cref="InputTape"/>), its fingerprint compared every second it kept one. The first
/// second they differ is where the level went another way -- a replay of another build, or a level that read what its inputs
/// did not carry -- and is told rather than played on as if nothing were wrong.
/// </content>
public sealed partial class GameHost
{
    // The replay asked to be played, and the level load it plays on once its mission is up (-1 until then).
    private ReplayFile? _playback;
    private int _playbackLoad = -1;

    // The player's own values of the settings a replay or a race set, put back when it is done.
    private readonly List<(ICVar Variable, string Value)> _borrowedSettings = [];

    // The first tick whose fingerprint did not match the recording's, how many matched, and the last tick compared: a seek back
    // runs ticks again that were compared already, and they are not counted twice.
    private int? _playbackDivergedAt;
    private int _playbackMatched;
    private int _playbackComparedThrough = -1;

    /// <summary>Gets or sets what hears a replay part from its recording: the tick, the fingerprint recorded and the one played.</summary>
    public Action<int, ulong, ulong>? ReplayDiverged { get; set; }

    /// <summary>Gets the replay being played back, or <see langword="null"/> while none is.</summary>
    public ReplayFile? PlayingReplay => _playback;

    /// <summary>Gets the first tick a replay being played parted from its recording on, or <see langword="null"/> while it agrees.</summary>
    public int? ReplayDivergedAt => _playbackDivergedAt;

    /// <summary>Gets how many of the recording's fingerprints the replay being played has matched so far.</summary>
    public int ReplayFingerprintsMatched => _playbackMatched;

    /// <summary>Gets a value indicating whether the replay being played has given its mission every tick it recorded.</summary>
    public bool ReplayFinished => _playback is not null && ReplayAtEnd;

    /// <summary>Plays a replay from its start: its mission brought up afresh under its settings, then given its inputs.</summary>
    /// <param name="replay">The replay.</param>
    /// <returns>Why it cannot be played, or <see langword="null"/> once it is under way.</returns>
    public string? PlayReplay(ReplayFile replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        if (ReplayProblem(replay) is { } problem)
        {
            return problem;
        }

        StopPlayback();
        StopRace();
        BorrowSettings(replay.Header.Settings);
        _playback = replay;
        _playbackLoad = -1;
        _playbackDivergedAt = null;
        _playbackMatched = 0;
        _playbackComparedThrough = -1;
        if (!string.Equals(replay.Header.GameVersion, BuildVersion, StringComparison.Ordinal))
        {
            _logger.Warning(
                "Replays",
                $"The replay was recorded by {replay.Header.GameVersion} and this is {BuildVersion}: another build may play it differently.");
        }

        _logger.Information("Replays", $"Playing back mission {replay.Header.Mission}, {replay.Tape.Length / 30.0:0.#} s.");
        StartMissionWhenReadable(replay.Header.Mission);
        return null;
    }

    /// <summary>Why a replay cannot be played in the game running, or <see langword="null"/> where it can.</summary>
    /// <param name="replay">The replay.</param>
    /// <returns>Why not, or <see langword="null"/>.</returns>
    private string? ReplayProblem(ReplayFile replay)
    {
        if (!string.Equals(replay.Header.Title, _titleRuntime.Title.Id, StringComparison.Ordinal))
        {
            return $"it is a replay of {replay.Header.Title}, and {_titleRuntime.Title.Id} is the game running";
        }

        return _missions.TryGet(replay.Header.Mission, out _) ? null : $"this game has no mission {replay.Header.Mission}";
    }

    /// <summary>Plays back the replay a file holds (<c>-playreplay</c>), or says why it cannot.</summary>
    /// <param name="path">The file.</param>
    private void StartReplayFromFile(string path)
    {
        string? problem;
        try
        {
            problem = PlayReplay(ReplayFile.Read(File.ReadAllBytes(path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            problem = ex.Message;
        }

        if (problem is not null)
        {
            _logger.Warning("Replays", $"The replay {path} was not played: {problem}.");
        }
    }

    /// <summary>
    /// Gets or sets what gives a recorded command no game of this build knows, as a replay comes to it: a test's own moves of
    /// its player, which it records beside the inputs.
    /// </summary>
    internal Action<string[]>? ReplayCommandHandler { get; set; }

    /// <summary>Records a command into the run being recorded, before the tick about to run: a test's own move of its player.</summary>
    /// <param name="command">The command, its words space-separated.</param>
    internal void RecordRunCommandForTesting(string command)
    {
        if (_run is { } run && _runLoad == _levelLoads)
        {
            run.Command(TickOfLevel, command);
        }
    }

    /// <summary>Stops the replay being played, if one is, and puts the player's own settings back.</summary>
    public void StopPlayback()
    {
        if (_playback is null)
        {
            return;
        }

        _playback = null;
        _playbackLoad = -1;
        _playbackReloading = false;
        ResetReplayViewer();
        if (ReferenceEquals(_replay, _replayTapeOfPlayback))
        {
            _replay = null;
        }

        _replayTapeOfPlayback = null;
        ReturnSettings();
    }

    // The player's own settings back, where a replay or a race set them.
    private void ReturnSettings()
    {
        foreach ((ICVar variable, string value) in _borrowedSettings)
        {
            variable.TrySetText(value, out _);
        }

        _borrowedSettings.Clear();
    }

    /// <summary>
    /// Gets a value indicating whether the level's cutscenes play: as a replay being played, or the run a race is against, recorded
    /// it; else as the game was started.
    /// </summary>
    private bool PlaysCutscenes => (_playback ?? _race)?.Header.PlaysCutscenes ?? _options.PlayCutscenes;

    // The tape the play-test's replay was handed by a playback, to tell it from an editor's own.
    private InputTape? _replayTapeOfPlayback;

    // The difficulty a replay's mission is brought up at: its own.
    private Game.Ai.AiDifficulty? PlaybackDifficulty =>
        _playback is { } replay && (_playbackLoad < 0 || _playbackReloading) ? (Game.Ai.AiDifficulty)Math.Clamp(replay.Header.Difficulty, 0, 2) : null;

    /// <summary>
    /// A mission has come up: the replay asked for is bound to it and given to the play-test's replay from its first tick; or,
    /// once a replay's level has gone, the replay is over.
    /// </summary>
    /// <returns>Whether a replay plays on this level.</returns>
    private bool BindPlayback()
    {
        if (_playback is null)
        {
            return false;
        }

        if (_playbackReloading)
        {
            // The replay's own level loaded again for a seek back: still the replay's, which the seek runs on from its start.
            _playbackReloading = false;
            _playbackLoad = _levelLoads;
            return true;
        }

        if (_playbackLoad >= 0)
        {
            // Another mission came up after the replay's: it is over.
            StopPlayback();
            return false;
        }

        ResetReplayViewer();
        _playbackLoad = _levelLoads;
        _replay = _playback.Tape;
        _replayTapeOfPlayback = _playback.Tape;
        _replayLoad = _levelLoads;
        _replayTarget = null;
        _replayCommandsGiven = (-1, -1);
        _tapeCheckpoints.Clear();
        return true;
    }

    /// <summary>Gets a value indicating whether a replay's level is held where its recording ends: past it is no input to give.</summary>
    private bool PlaybackHoldsLevel => _playback is not null && _playbackLoad == _levelLoads && ReplayAtEnd;

    /// <summary>A command a replay recorded, given again on its tick: a setting changed during the run, changed there too.</summary>
    /// <param name="words">The command's words.</param>
    /// <returns>Whether it was a replay's command.</returns>
    private bool ApplyPlaybackCommand(string[] words)
    {
        if (_playback is null)
        {
            return false;
        }

        if (words is ["set", string name, string value])
        {
            BorrowSetting(name, value);
            return true;
        }

        if (words is not ["god", _] and not ["unseen", _] and not ["place", _, _, _, _] && ReplayCommandHandler is { } handler)
        {
            handler(words);
            return true;
        }

        return false;
    }

    /// <summary>After a tick of a replay's level: its fingerprint compared with the recording's, where one was kept.</summary>
    /// <param name="tick">The tick that ran.</param>
    private void PlaybackTicked(int tick)
    {
        if (_playback is not { } replay || _playbackLoad != _levelLoads || tick <= _playbackComparedThrough
            || replay.Tape.DigestAt(tick) is not { } recorded)
        {
            return;
        }

        _playbackComparedThrough = tick;
        ulong played = SimulationDigest();
        if (played == recorded)
        {
            _playbackMatched++;
            return;
        }

        if (_playbackDivergedAt is null)
        {
            _playbackDivergedAt = tick;
            _logger.Warning(
                "Replays",
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"The replay parted from its recording at {tick / 30.0:0.0} s (tick {tick}): this level is not the one it recorded."));
            ReplayDiverged?.Invoke(tick, recorded, played);
        }
    }

    /// <summary>
    /// The replay's settings as they stood at a tick it is put back to: as its header set them, then each it changed before the tick.
    /// </summary>
    /// <param name="tick">The tick.</param>
    private void PlaybackSettingsAt(int tick)
    {
        if (_playback is not { } replay)
        {
            return;
        }

        BorrowSettings(replay.Header.Settings);
        foreach (string command in replay.Tape.CommandsBefore(tick))
        {
            if (command.Split(' ') is ["set", string name, string value])
            {
                BorrowSetting(name, value);
            }
        }
    }

    /// <summary>The level of a replay being watched has gone -- the player left it, or another came up -- and the replay with it.</summary>
    private void PlaybackLevelReleased()
    {
        if (_playback is not null && _playbackLoad >= 0 && !_playbackReloading)
        {
            StopPlayback();
        }
    }

    // A run's recorded settings set for a replay or a race of it -- only those that change how the level plays, which is all a
    // header may name -- the player's own kept to put back.
    private void BorrowSettings(IReadOnlyList<(string Name, string Value)> settings)
    {
        foreach ((string name, string value) in settings)
        {
            BorrowSetting(name, value);
        }
    }

    // One setting set for a replay or a race, the player's own value kept the first time; a name that is no simulation setting is
    // not set.
    private void BorrowSetting(string name, string value)
    {
        if (!CVarRegistry.Default.TryGet(name, out ICVar? variable) || (variable.Flags & CVarOptions.Simulation) == 0)
        {
            _logger.Warning("Replays", $"The replay names a setting {name} that does not change how the level plays here; left alone.");
            return;
        }

        if (!_borrowedSettings.Exists(kept => ReferenceEquals(kept.Variable, variable)))
        {
            _borrowedSettings.Add((variable, variable.ValueText));
        }

        if (!variable.TrySetText(value, out string? reason))
        {
            _logger.Warning("Replays", $"The replay's {name} {value} was not taken: {reason}.");
        }
    }
}
