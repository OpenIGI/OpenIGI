using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Replays;

namespace OpenIGI.Host;

/// <content>
/// A play-test recorded and played back. The level runs the same from the same start given the same input a tick, so a
/// recording is only that: the input the player was given each tick of the level, the ticks the cutscene was being broken,
/// and the play-test's own commands that change the game (god, unseen, standing the player somewhere) with the tick each came
/// before. Played back, the level is put back as the recording began it and given it all again; a moment is reached by running the
/// level to it -- fast forward, or, to go back, the level put back from memory where it stood a little before (a checkpoint every
/// two seconds of the recording) and run up to it. Marks note where a guard first saw the player and where a command was given,
/// for the scrubber.
/// </content>
/// <remarks>
/// The same transport drives a replay watched in the game (<see cref="Watching"/>): there the recording is a run's from its file.
/// </remarks>
public sealed partial class GameHost
{
    // How fast a seek runs the level: as many ticks as a frame lets through.
    private const float ReplaySeekSpeed = 32f;

    // How often a recording looks for a guard newly seeing the player, in ticks.
    private const int SightCheckTicks = 6;

    // Seconds a seek with the arrow keys goes.
    private const double ReplayStepSeconds = 5;

    // How often the level is held along a recording, in ticks: going back plays on at most this far.
    private const int TapeCheckpointEvery = 60;

    // The level as the recording began it, and held along it since.
    private readonly CheckpointRing _tapeCheckpoints = new(512L * 1024 * 1024);

    private readonly List<Game.Ai.SoldierSnapshot> _tapeSoldiers = [];
    private readonly HashSet<string> _tapeSeeing = new(StringComparer.Ordinal);

    // The ticks the level now loaded has run, and which load they count for.
    private int _ticksOfLoad;
    private int _ticksLoad = -1;

    // The recording being made, and the level load it records (null until one comes up after it began).
    private InputTape? _tape;
    private int? _tapeLoad;
    private int _tapeAfterLoad;
    private bool _recording;

    // The recording being played, the load it plays on, and the tick a seek runs to.
    private InputTape? _replay;
    private int _replayLoad = -1;
    private int? _replayTarget;

    // The load and tick whose recorded commands were last given, so a frame that runs no tick gives none again; and the tick
    // last looked at for sightings.
    private (int Load, int Tick) _replayCommandsGiven = (-1, -1);
    private int _sightCheckedTick = -1;

    /// <summary>Gets the ticks the level now loaded has run.</summary>
    internal int TickOfLevel
    {
        get
        {
            if (_ticksLoad != _levelLoads)
            {
                (_ticksLoad, _ticksOfLoad) = (_levelLoads, 0);
            }

            return _ticksOfLoad;
        }
    }

    /// <summary>Gets whether a recording is being played back on the level now loaded.</summary>
    private bool Replaying => _replay is not null && _replayLoad == _levelLoads;

    /// <summary>Gets whether the replay has run all the recording: the level is held there, past it is no input to give.</summary>
    private bool ReplayAtEnd => Replaying && _replayTarget is null && TickOfLevel >= _replay!.Length;

    // Whether the recording takes this level's ticks: begun, and this the first level to come up since.
    private bool TapeTakes
    {
        get
        {
            if (!_recording || _tape is null)
            {
                return false;
            }

            if (_tapeLoad is null && _levelLoads > _tapeAfterLoad)
            {
                _tapeLoad = _levelLoads;
            }

            if (_tapeLoad is { } load && load != _levelLoads)
            {
                // The level loaded again under it (the mission restarted): what was recorded stands, and recording ends.
                _recording = false;
                Said("The level loaded again; the recording ends there.");
                return false;
            }

            return _tapeLoad is not null;
        }
    }

    /// <summary>Begins a recording of the next level to come up, or of the one just loaded.</summary>
    /// <param name="ofLevelJustLoaded">Whether the level just loaded is the one recorded (the editor's play loads it first).</param>
    private void BeginRecording(bool ofLevelJustLoaded = false)
    {
        _tape = new InputTape();
        _tapeLoad = null;
        _tapeAfterLoad = ofLevelJustLoaded ? _levelLoads - 1 : _levelLoads;
        _recording = true;
        _tapeSeeing.Clear();
    }

    /// <summary>The input a title's player is given this tick: recorded, or replaced by the recording being played.</summary>
    /// <param name="input">The input gathered from the window.</param>
    /// <returns>The input to give.</returns>
    private TitlePlayerInput PlaytestInput(TitlePlayerInput input)
    {
        if (Replaying)
        {
            return _replay!.InputAt(TickOfLevel) ?? default;
        }

        if (TapeTakes)
        {
            _tape!.SetInput(TickOfLevel, input);
        }

        return input;
    }

    /// <summary>
    /// Before each frame's run of the level, for the tick it may run: a recording notes the cutscene's break as it stands and new
    /// sightings; a replay gives the tick's commands (once) and its break.
    /// </summary>
    private void PlaytestTickStarting()
    {
        int tick = TickOfLevel;
        HoldTape(tick);
        if (Replaying)
        {
            InputTape tape = _replay!;
            if (_replayCommandsGiven != (_levelLoads, tick))
            {
                _replayCommandsGiven = (_levelLoads, tick);
                foreach (string command in tape.CommandsAt(tick))
                {
                    ApplyRecordedCommand(command);
                }
            }

            if (tape.BreaksAt(tick))
            {
                if (!tape.BreaksAt(tick - 1))
                {
                    _cutsceneSounds.StopAll();
                    _mission.Progress?.ClearCutsceneMessages();
                }

                _mission.Scenes?.RequestBreak();
            }

            return;
        }

        if (!TapeTakes)
        {
            return;
        }

        InputTape recording = _tape!;
        recording.SetBreak(tick, _cutsceneSkip.Breaking);
        if (tick % SightCheckTicks == 0 && _sightCheckedTick != tick && _soldiers is { } soldiers)
        {
            _sightCheckedTick = tick;
            soldiers.Snapshot(_tapeSoldiers);
            foreach (Game.Ai.SoldierSnapshot soldier in _tapeSoldiers)
            {
                bool sees = soldier is { HasMind: true, IsDead: false, SeesTarget: true };
                if (sees && _tapeSeeing.Add(soldier.Name))
                {
                    recording.Mark(tick, $"{(soldier.Name.Length > 0 ? soldier.Name : "a guard")} sees you");
                }
                else if (!sees)
                {
                    _tapeSeeing.Remove(soldier.Name);
                }
            }
        }
    }

    /// <summary>After a tick of the level: counted, a step taken, and a seek or the recording's end met.</summary>
    private void PlaytestTicked()
    {
        int tick = TickOfLevel;
        if (TapeTakes)
        {
            _tape!.Ran(tick);
        }

        _ticksOfLoad++;
        if (_playtestSteps > 0)
        {
            _playtestSteps--;
        }

        if (!Replaying)
        {
            return;
        }

        if (_replayTarget is { } target && _ticksOfLoad >= target)
        {
            _replayTarget = null;
            _playtestPaused = true;
        }

        if (_ticksOfLoad == _replay!.Length)
        {
            _playtestPaused = true;
            _playtestSteps = 0;
            if (_editor is { IsPlaying: true })
            {
                Said("The end of the recording: F6 plays it again.");
            }
        }
    }

    /// <summary>Records a command that changes the game, before the tick it comes before.</summary>
    /// <param name="command">The command, as <see cref="ApplyRecordedCommand"/> takes it.</param>
    /// <param name="mark">What the scrubber says of it.</param>
    private void RecordCommand(string command, string mark)
    {
        if (TapeTakes)
        {
            _tape!.Command(TickOfLevel, command);
            _tape.Mark(TickOfLevel, mark);
        }
    }

    /// <summary>Marks the recording being made where it has got to.</summary>
    /// <param name="text">What the mark says.</param>
    private void MarkTape(string text)
    {
        if (TapeTakes)
        {
            _tape!.Mark(TickOfLevel, text);
        }
    }

    // A recorded command, given again: the cheats as they were set, the player stood where he was stood.
    private void ApplyRecordedCommand(string command)
    {
        string[] words = command.Split(' ');
        if (ApplyPlaybackCommand(words))
        {
            return;
        }

        switch (words)
        {
            case ["god", string on]:
                OpenPlaytestCheats();
                PlayerCheatCVars.InfiniteHealth.Value = on == "1";
                break;
            case ["unseen", string on]:
                OpenPlaytestCheats();
                PlayerCheatCVars.Unseen.Value = on == "1";
                break;
            case ["place", string x, string y, string z, string heading]
                when _titleFeatures?.Placement is { } placement:
                placement.TryPlace(
                    new Vector3d(Number(x), Number(y), Number(z)) * Game.World.WorldScene.UnitsPerMetre,
                    (float)Number(heading));
                break;
            default:
                break;
        }

        static double Number(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>Plays the recording from its start: the level loaded afresh and given it again.</summary>
    /// <returns>What happened, to say.</returns>
    private string StartReplay()
    {
        InputTape? tape = Replaying ? _replay : _tape;
        if (tape is null || tape.Length == 0)
        {
            return "Nothing recorded yet: play a little first.";
        }

        _recording = false;
        _replay = tape;
        ReplayFromStart(target: null);
        _playtestPaused = false;
        return string.Create(CultureInfo.InvariantCulture, $"Playing back {tape.Length / 30.0:0.#} s.");
    }

    // The level put back where the recording last held it at or before a tick (its start, with none given), and run on to the tick;
    // loaded afresh where the recording holds nothing of it.
    private void ReplayFromStart(int? target)
    {
        int to = target is > 0 ? target.Value : 0;
        if (_tapeCheckpoints.AtOrBefore(to) is { } held && RestoreLevel(held))
        {
            (_ticksLoad, _ticksOfLoad) = (_levelLoads, held.Tick);
            ComputerAsRestored();
            _replayCommandsGiven = (-1, -1);
            PlaytestCheatsAt(held.Tick);
            PlaybackSettingsAt(held.Tick);
            _replayLoad = _levelLoads;
            _replayTarget = to > held.Tick ? to : null;
            return;
        }

        PlaytestCheatsAsAtStart();
        PlaybackSettingsAt(0);

        // A replay being watched keeps its level through the load: it is the same run, from its start.
        _playbackReloading = _playback is not null && _playbackLoad >= 0;
        ReloadLevelInPlace();
        _playbackReloading = false;
        _tapeCheckpoints.Clear();
        _replayLoad = _levelLoads;
        _replayTarget = target is > 0 ? target : null;
    }

    // A checkpoint of the level at a tick of the recording being made or played, every two seconds, before the tick runs.
    private void HoldTape(int tick)
    {
        if (tick % TapeCheckpointEvery != 0 || _tapeCheckpoints.Has(tick) || !(Replaying || TapeTakes) || !CanCheckpointLevel)
        {
            return;
        }

        if (CaptureLevel(tick, _tapeCheckpoints.Last) is { } held)
        {
            _tapeCheckpoints.Add(held);
        }
    }

    // The cheats as the recording had them at a tick: as it began, then each god or unseen it was given before the tick.
    private void PlaytestCheatsAt(int tick)
    {
        PlaytestCheatsAsAtStart();
        if (_replay is not { } tape)
        {
            return;
        }

        foreach (string command in tape.CommandsBefore(tick))
        {
            if (command.StartsWith("god ", StringComparison.Ordinal) || command.StartsWith("unseen ", StringComparison.Ordinal))
            {
                ApplyRecordedCommand(command);
            }
        }
    }
    // The cheats as the recording began with them: off, as every play-test begins.
    private void PlaytestCheatsAsAtStart()
    {
        if (_playtestCheatsBefore is { } before)
        {
            PlayerCheatCVars.InfiniteHealth.Value = before.Health;
            PlayerCheatCVars.Unseen.Value = before.Unseen;
        }
    }

    /// <summary>Moves the replay to a tick: run on to it, or, before where it is, the level loaded afresh and run up to it.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>What happened, to say.</returns>
    private string SeekReplay(int tick)
    {
        if (!Replaying)
        {
            return "Not playing a recording back.";
        }

        tick = Math.Clamp(tick, 0, _replay!.Length);
        if (tick < TickOfLevel)
        {
            ReplayFromStart(tick);
        }
        else if (tick > TickOfLevel)
        {
            _replayTarget = tick;
        }

        _playtestPaused = tick == TickOfLevel;
        _playtestSteps = 0;
        return string.Create(CultureInfo.InvariantCulture, $"To {tick / 30.0:0.#} s.");
    }

    /// <summary>The recording's commands: replay, seek &lt;seconds&gt;, back, on, record.</summary>
    /// <param name="words">The command's words.</param>
    /// <returns>What happened, or null when the words are not the recording's.</returns>
    private string? ReplayCommand(string[] words) => words switch
    {
        ["replay"] => StartReplay(),
        ["seek", string at] when double.TryParse(at, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) => SeekReplay((int)Math.Round(seconds * 30)),
        ["back"] => SeekReplay(TickOfLevel - (int)(ReplayStepSeconds * 30)),
        ["on"] => SeekReplay(TickOfLevel + (int)(ReplayStepSeconds * 30)),
        ["record"] => Record(),
        _ => null,
    };

    // A recording begun again from the level's start: put back as the last recording began it (what it held after that belonged
    // to the old recording), or loaded afresh.
    private string Record()
    {
        _replay = null;
        PlaytestCheatsAsAtStart();
        if (_tapeCheckpoints.AtOrBefore(0) is { Tick: 0 } start && RestoreLevel(start))
        {
            _tapeCheckpoints.Clear();
            _tapeCheckpoints.Add(start);
            BeginRecording(ofLevelJustLoaded: true);
            _tapeLoad = _levelLoads;
            (_ticksLoad, _ticksOfLoad) = (_levelLoads, 0);
            return "Recording from the start of the level.";
        }

        _tapeCheckpoints.Clear();
        BeginRecording();
        ReloadLevelInPlace();
        return "Recording from the start of the level.";
    }
    /// <summary>The recording's state, for the editor's scrubber.</summary>
    private Editor.EditorPlaytestTape TapeState()
    {
        InputTape? tape = Replaying ? _replay : _tape;
        double length = (tape?.Length ?? 0) / 30.0;
        double seconds = Replaying ? TickOfLevel / 30.0 : length;
        IReadOnlyList<(double, string)> marks = tape is null ? [] : [.. tape.Marks.Select(static m => (m.Tick / 30.0, m.Text))];
        return new Editor.EditorPlaytestTape(_recording && TapeTakes, Replaying, seconds, length, marks);
    }
}
