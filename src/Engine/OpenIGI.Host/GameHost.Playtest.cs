using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Host.Editor;
using OpenIGI.Input;

namespace OpenIGI.Host;

/// <content>
/// The editor's play-test controls, while a mission is played from the editor: the level held still and stepped a tick at a
/// time, run slower or faster, the player kept alive or unseen, and stood where the editor's camera was. Keys, taken before
/// the game's own (F6 pause, F7 step, F8 speed, F2 god, F3 unseen, F4 to the camera); the editor's banner shows them.
/// </content>
/// <remarks>
/// The cheats go through the game's own (<see cref="PlayerCheatCVars"/>): the gate is opened for the play-test and everything
/// put back as it was when it ends, so a play-test leaves no cheat behind it.
/// </remarks>
public sealed partial class GameHost
{
    private static readonly float[] PlaytestSpeeds = [0.25f, 0.5f, 1f, 2f, 4f, 8f];

    // The keys a play-test takes: their binds (the developer's renderer toggles, co-op's calls) do not run while it does.
    private static readonly KeyCode[] PlaytestKeys = [KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F6, KeyCode.F7, KeyCode.F8, KeyCode.F9, KeyCode.F10];

    private bool _playtestOverlay;

    // A play-test's skip of the cutscene playing, held as the skip key is until the scene ends.
    private bool _playtestSkipping;
    private const int PlaytestNormalSpeed = 2;

    private bool _playtestPaused;
    private int _playtestSteps;
    private int _playtestSpeed = PlaytestNormalSpeed;
    private bool _playtestWasPlaying;
    private Vector3d? _playtestCamera;

    // Which way the editor's camera looked across the ground, as the player's heading: forward is (sin, cos).
    private float _playtestCameraHeading;

    // The cheats as they were before the play-test opened the gate; null while it has not.
    private (bool Enabled, bool Health, bool Unseen)? _playtestCheatsBefore;

    /// <summary>Gets the play-test's controls as they stand, for the editor's banner.</summary>
    internal EditorPlaytest Playtest => new(
        _playtestPaused,
        PlaytestSpeeds[_playtestSpeed],
        PlayerCheatOptions.Current.InfiniteHealth,
        PlayerCheatOptions.Current.Unseen,
        _playtestCamera is not null)
    {
        Overlay = _playtestOverlay,
        Tape = TapeState(),
    };

    /// <summary>Gets whether a key is the play-test's while one runs, so its bind is passed over.</summary>
    /// <param name="key">The key pressed.</param>
    /// <returns>Whether the play-test takes it.</returns>
    private bool PlaytestOwns(KeyCode key) => _editor is { IsPlaying: true } && Array.IndexOf(PlaytestKeys, key) >= 0;

    /// <summary>Gets whether the play-test holds the level still: paused with no step asked for, or a replay at its end.</summary>
    private bool PlaytestHoldsLevel =>
        (TransportActive && ((_playtestPaused && _playtestSteps == 0) || ReplayAtEnd)) || PlaybackHoldsLevel;

    /// <summary>Gets how fast the play-test runs the level's clock: 1 outside one, or under lockstep (whose clock is the frames').</summary>
    /// <remarks>A replay's seek runs as fast as the frames let ticks through.</remarks>
    private float PlaytestTimeScale => TransportActive && !_options.Lockstep
        ? _replayTarget is not null && Replaying ? ReplaySeekSpeed : PlaytestSpeeds[_playtestSpeed]
        : 1f;

    /// <summary>Notes the start and end of a play-test: where the editor's camera was, and, at its end, everything put back.</summary>
    private void TrackPlaytest()
    {
        bool playing = _editor is { IsPlaying: true };
        if (playing == _playtestWasPlaying)
        {
            return;
        }

        _playtestWasPlaying = playing;
        _playtestPaused = false;
        _playtestSteps = 0;
        _playtestSpeed = PlaytestNormalSpeed;
        if (playing)
        {
            // The editor's last view is its camera's, until the game draws its own: where it stood, and which way it looked
            // across the ground (the line through the middle of the view runs along both clip planes of x = 0 and y = 0).
            _playtestCamera = _editorView.Width > 0f ? _editorView.Eye : null;
            Matrix4f m = _editorView.ViewProjection;
            Vector3d along = Vector3d.Cross(new Vector3d(m.M11, m.M12, m.M13), new Vector3d(m.M21, m.M22, m.M23));
            along = (m.M41 * along.X) + (m.M42 * along.Y) + (m.M43 * along.Z) < 0 ? -along : along;
            _playtestCameraHeading = (float)Math.Atan2(along.X, along.Y);
            return;
        }

        _playtestCamera = null;
        _recording = false;
        _replay = null;
        _replayTarget = null;
        _tapeCheckpoints.Clear();
        if (_playtestCheatsBefore is { } before)
        {
            PlayerCheatCVars.InfiniteHealth.Value = before.Health;
            PlayerCheatCVars.Unseen.Value = before.Unseen;
            PlayerCheatCVars.CheatsEnabled.Value = before.Enabled;
            _playtestCheatsBefore = null;
        }
    }

    /// <summary>Takes the play-test's keys, before the game reads its own.</summary>
    private void HandlePlaytestKeys()
    {
        KeyboardState keys = _window.Input.Keyboard;
        bool shift = keys.IsDown(KeyCode.LeftShift) || keys.IsDown(KeyCode.RightShift);
        string? command = true switch
        {
            _ when keys.WasPressed(KeyCode.F6) => _playtestPaused ? "run" : "pause",

            // A step from running pauses first: the level stops where it is and moves on a tick.
            _ when keys.WasPressed(KeyCode.F7) => "step",
            _ when keys.WasPressed(KeyCode.F8) => string.Create(CultureInfo.InvariantCulture, $"speed {PlaytestSpeeds[Math.Clamp(_playtestSpeed + (shift ? -1 : 1), 0, PlaytestSpeeds.Length - 1)]}"),
            _ when keys.WasPressed(KeyCode.F2) => "god",
            _ when keys.WasPressed(KeyCode.F3) => "unseen",
            _ when keys.WasPressed(KeyCode.F4) => "camera",
            _ when keys.WasPressed(KeyCode.F10) => "overlay",
            _ when keys.WasPressed(KeyCode.F9) => Replaying ? "record" : "replay",

            // Played back, the arrows go back and on through it and Home to its start: the player's own keys do nothing then.
            _ when Replaying && keys.WasPressed(KeyCode.Left) => "back",
            _ when Replaying && keys.WasPressed(KeyCode.Right) => "on",
            _ when Replaying && keys.WasPressed(KeyCode.Home) => "seek 0",
            _ => null,
        };
        if (command is not null)
        {
            Said(PlaytestCommand(command));
        }
    }

    /// <summary>Stands the player where the editor's camera was when the play-test began.</summary>
    /// <returns>What happened, to say.</returns>
    internal string PlaytestToCamera()
    {
        if (_playtestCamera is not { } eye || TitlePlayer is null || _titleFeatures?.Placement is not { } placement)
        {
            return "No editor camera to go to.";
        }

        if (!placement.TryPlace(eye * Game.World.WorldScene.UnitsPerMetre, _playtestCameraHeading))
        {
            return "Nothing to stand on under the editor's camera.";
        }

        RecordCommand(string.Create(CultureInfo.InvariantCulture, $"place {eye.X:R} {eye.Y:R} {eye.Z:R} {_playtestCameraHeading:R}"), "to the camera");
        return string.Create(CultureInfo.InvariantCulture, $"Stood where the editor's camera was ({eye.X:0.#}, {eye.Y:0.#}).");
    }

    /// <summary>Applies the play-test's commands from the editor: pause, step, speed and the cheats.</summary>
    /// <param name="command">The command: pause, run, step, speed &lt;x&gt;, god, unseen, camera.</param>
    /// <returns>What happened, to say.</returns>
    internal string PlaytestCommand(string command)
    {
        string[] words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (ReplayCommand(words) is { } replayed)
        {
            return replayed;
        }

        // What a recording plays back is the game as it was played: nothing changes it but going back to playing.
        if (Replaying && words is ["god"] or ["unseen"] or ["camera"] or ["skip"])
        {
            return "Playing back a recording: record again (or Esc) to play.";
        }

        switch (words)
        {
            case ["pause"]:
                _playtestPaused = true;
                _playtestSteps = 0;
                return "Paused.";
            // A replay at its end has nothing further to run: run plays it again from the start, and a step goes nowhere.
            case ["run"] when ReplayAtEnd:
                return StartReplay();
            case ["run"]:
                _playtestPaused = false;
                return "Running.";
            case ["step", ..] when ReplayAtEnd:
                _playtestPaused = true;
                return "The end of the recording: F6 plays it from the start.";
            case ["step", ..]:
                _playtestPaused = true;
                _playtestSteps += words.Length > 1 && int.TryParse(words[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? Math.Max(1, n) : 1;
                return "Stepping.";
            case ["speed", string x] when float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out float speed):
                _playtestSpeed = Array.IndexOf(PlaytestSpeeds, PlaytestSpeeds.MinBy(s => MathF.Abs(s - speed)));
                return string.Create(CultureInfo.InvariantCulture, $"Speed {PlaytestSpeeds[_playtestSpeed]:0.##}x.");
            case ["god"]:
                OpenPlaytestCheats();
                PlayerCheatCVars.InfiniteHealth.Value = !PlayerCheatCVars.InfiniteHealth.Value;
                RecordCommand($"god {(PlayerCheatCVars.InfiniteHealth.Value ? 1 : 0)}", PlayerCheatCVars.InfiniteHealth.Value ? "god on" : "god off");
                return PlayerCheatCVars.InfiniteHealth.Value ? "God mode on." : "God mode off.";
            case ["unseen"]:
                OpenPlaytestCheats();
                PlayerCheatCVars.Unseen.Value = !PlayerCheatCVars.Unseen.Value;
                RecordCommand($"unseen {(PlayerCheatCVars.Unseen.Value ? 1 : 0)}", PlayerCheatCVars.Unseen.Value ? "unseen on" : "unseen off");
                return PlayerCheatCVars.Unseen.Value ? "Unseen on." : "Unseen off.";
            case ["camera"]:
                return PlaytestToCamera();
            case ["skip"]:
                _playtestSkipping = true;
                return "Skipping the cutscene, if one is playing that the level lets be skipped.";
            case ["overlay"]:
                _playtestOverlay = !_playtestOverlay;
                return _playtestOverlay ? "Guards' minds shown: sight, what each does, where each goes." : "Guards' minds hidden.";
            default:
                return "Play-test commands: pause, run, step [n], speed <x>, god, unseen, camera, overlay, skip, replay, seek <s>, back, on, record.";
        }
    }

    // Opens the cheats' gate for the play-test, keeping what it was to put back; not in a multiplayer session, which holds it shut.
    private void OpenPlaytestCheats()
    {
        if (PlayerCheatLock.IsHeld)
        {
            return;
        }

        _playtestCheatsBefore ??= (PlayerCheatCVars.CheatsEnabled.Value, PlayerCheatCVars.InfiniteHealth.Value, PlayerCheatCVars.Unseen.Value);
        PlayerCheatCVars.CheatsEnabled.Value = true;
    }

    private void Said(string text) => _logger.Information("Editor", text);
}
