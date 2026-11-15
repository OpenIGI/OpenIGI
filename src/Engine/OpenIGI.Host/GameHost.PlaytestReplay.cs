using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;

namespace OpenIGI.Host;

/// <content>
/// A play-test recorded and played back. The level runs the same from the same start given the same input a tick, so a
/// recording is only that: the input the player was given each tick of the level, the ticks the cutscene was being broken,
/// and the play-test's own commands that change the game (god, unseen, standing the player somewhere) with the tick each came
/// before. Played back, the level is loaded afresh and given it all again; a moment is reached by running the level to it --
/// fast forward, or, to go back, the level loaded afresh and run up to it. Marks note where a guard first saw the player and
/// where a command was given, for the scrubber.
/// </content>
/// <remarks>
/// Not recorded, so not played back: the remaster's weapon wheel and the computer's screens, which take the mouse outside a tick.
/// </remarks>
public sealed partial class GameHost
{
    // How fast a seek runs the level: as many ticks as a frame lets through.
    private const float ReplaySeekSpeed = 32f;

    // How often a recording looks for a guard newly seeing the player, in ticks.
    private const int SightCheckTicks = 6;

    // Seconds a seek with the arrow keys goes.
    private const double ReplayStepSeconds = 5;

    private readonly List<Game.Ai.SoldierSnapshot> _tapeSoldiers = [];
    private readonly HashSet<string> _tapeSeeing = new(StringComparer.Ordinal);

    // The ticks the level now loaded has run, and which load they count for.
    private int _ticksOfLoad;
    private int _ticksLoad = -1;

    // The recording being made, and the level load it records (null until one comes up after it began).
    private PlaytestTape? _tape;
    private int? _tapeLoad;
    private int _tapeAfterLoad;
    private bool _recording;

    // The recording being played, the load it plays on, and the tick a seek runs to.
    private PlaytestTape? _replay;
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
        _tape = new PlaytestTape();
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
        if (Replaying)
        {
            PlaytestTape tape = _replay!;
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

        PlaytestTape recording = _tape!;
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
        _ = TickOfLevel;
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

        if (_ticksOfLoad >= _replay!.Length && !_playtestPaused)
        {
            _playtestPaused = true;
            Said("The end of the recording.");
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

    // A recorded command, given again: the cheats as they were set, the player stood where he was stood.
    private void ApplyRecordedCommand(string command)
    {
        string[] words = command.Split(' ');
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
        PlaytestTape? tape = Replaying ? _replay : _tape;
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

    // The level loaded afresh for the recording, and run on to a tick when one is given.
    private void ReplayFromStart(int? target)
    {
        PlaytestCheatsAsAtStart();
        ReloadLevelInPlace();
        _replayLoad = _levelLoads;
        _replayTarget = target is > 0 ? target : null;
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

    // A recording begun again, on the level loaded afresh.
    private string Record()
    {
        _replay = null;
        BeginRecording();
        PlaytestCheatsAsAtStart();
        ReloadLevelInPlace();
        return "Recording from the start of the level.";
    }

    /// <summary>The recording's state, for the editor's scrubber.</summary>
    private Editor.EditorPlaytestTape TapeState()
    {
        PlaytestTape? tape = Replaying ? _replay : _tape;
        double length = (tape?.Length ?? 0) / 30.0;
        double seconds = Replaying ? TickOfLevel / 30.0 : length;
        IReadOnlyList<(double, string)> marks = tape is null ? [] : [.. tape.Marks.Select(static m => (m.Tick / 30.0, m.Text))];
        return new Editor.EditorPlaytestTape(_recording && TapeTakes, Replaying, seconds, length, marks);
    }

    /// <summary>A play-test as it was played: the input of every tick, the cutscene's breaks, the commands and the marks.</summary>
    private sealed class PlaytestTape
    {
        private readonly List<TitlePlayerInput?> _inputs = [];
        private readonly List<bool> _breaks = [];
        private readonly List<(int Tick, string Command)> _commands = [];

        public List<(int Tick, string Text)> Marks { get; } = [];

        /// <summary>Gets how many ticks it covers.</summary>
        public int Length { get; private set; }

        public void SetInput(int tick, TitlePlayerInput input)
        {
            Grow(tick);
            _inputs[tick] = input;
        }

        public void SetBreak(int tick, bool breaking)
        {
            Grow(tick);
            _breaks[tick] = breaking;
        }

        public void Command(int tick, string command) => _commands.Add((tick, command));

        public void Mark(int tick, string text) => Marks.Add((tick, text));

        public TitlePlayerInput? InputAt(int tick) => tick >= 0 && tick < _inputs.Count ? _inputs[tick] : null;

        public bool BreaksAt(int tick) => tick >= 0 && tick < _breaks.Count && _breaks[tick];

        public IEnumerable<string> CommandsAt(int tick) => _commands.Where(c => c.Tick == tick).Select(static c => c.Command);

        private void Grow(int tick)
        {
            while (_inputs.Count <= tick)
            {
                _inputs.Add(null);
                _breaks.Add(false);
            }

            Length = Math.Max(Length, tick + 1);
        }
    }
}
