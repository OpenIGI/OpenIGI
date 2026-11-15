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
    private static readonly float[] PlaytestSpeeds = [0.25f, 0.5f, 1f, 2f, 4f];

    // The keys a play-test takes: their binds (the developer's renderer toggles, co-op's calls) do not run while it does.
    private static readonly KeyCode[] PlaytestKeys = [KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F6, KeyCode.F7, KeyCode.F8, KeyCode.F10];

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
    };

    /// <summary>Gets whether a key is the play-test's while one runs, so its bind is passed over.</summary>
    /// <param name="key">The key pressed.</param>
    /// <returns>Whether the play-test takes it.</returns>
    private bool PlaytestOwns(KeyCode key) => _editor is { IsPlaying: true } && Array.IndexOf(PlaytestKeys, key) >= 0;

    /// <summary>Gets whether the play-test holds the level still: paused, and no step asked for.</summary>
    private bool PlaytestHoldsLevel => _editor is { IsPlaying: true } && _playtestPaused && _playtestSteps == 0;

    /// <summary>Gets how fast the play-test runs the level's clock: 1 outside one, or under lockstep (whose clock is the frames').</summary>
    private float PlaytestTimeScale => _editor is { IsPlaying: true } && !_options.Lockstep ? PlaytestSpeeds[_playtestSpeed] : 1f;

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

        return placement.TryPlace(eye * Game.World.WorldScene.UnitsPerMetre, _playtestCameraHeading)
            ? string.Create(CultureInfo.InvariantCulture, $"Stood where the editor's camera was ({eye.X:0.#}, {eye.Y:0.#}).")
            : "Nothing to stand on under the editor's camera.";
    }

    /// <summary>Applies the play-test's commands from the editor: pause, step, speed and the cheats.</summary>
    /// <param name="command">The command: pause, run, step, speed &lt;x&gt;, god, unseen, camera.</param>
    /// <returns>What happened, to say.</returns>
    internal string PlaytestCommand(string command)
    {
        string[] words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (words)
        {
            case ["pause"]:
                _playtestPaused = true;
                _playtestSteps = 0;
                return "Paused.";
            case ["run"]:
                _playtestPaused = false;
                return "Running.";
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
                return PlayerCheatCVars.InfiniteHealth.Value ? "God mode on." : "God mode off.";
            case ["unseen"]:
                OpenPlaytestCheats();
                PlayerCheatCVars.Unseen.Value = !PlayerCheatCVars.Unseen.Value;
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
                return "Play-test commands: pause, run, step [n], speed <x>, god, unseen, camera, overlay, skip.";
        }
    }

    // A step asked for has run: one tick fewer to let through.
    private void PlaytestTicked()
    {
        if (_playtestSteps > 0)
        {
            _playtestSteps--;
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
