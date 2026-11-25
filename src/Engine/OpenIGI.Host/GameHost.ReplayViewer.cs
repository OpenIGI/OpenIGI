using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game;
using OpenIGI.Game.Replays;
using OpenIGI.Input;
using OpenIGI.Presentation.Hud;

namespace OpenIGI.Host;

/// <content>
/// A replay watched in the game: the play-test's transport -- held, stepped, run slower or faster, moved back and on through the
/// run -- driven from the transport bar at the foot of the screen, and seen through the player's eyes, from behind them, or from
/// the free camera. Nothing the viewer does reaches the level: the inputs are the recording's, the game's own keys and binds stand
/// down, the camera only chooses where the picture is taken from, and the run's end writes nothing to the viewer's profile.
/// </content>
public sealed partial class GameHost
{
    // Whose view a watched replay is drawn from.
    private enum ReplayCamera
    {
        // The player's own eyes, as the run was played.
        Eyes,

        // Over the player's shoulder.
        Behind,

        // The free camera, flown about the level as the player plays on in it.
        Free,
    }

    private ReplayCamera _replayCamera;
    private bool _replayBarHidden;

    // The viewer's own over-the-shoulder setting while a camera other than the eyes holds it; null while the eyes do.
    private bool? _replayThirdPersonBefore;

    // How the watched level decided its mission, once it has: completed or failed.
    private bool? _replayDecided;

    // Whether the level is being loaded again under a replay being watched, for a seek back: it stays the replay's.
    private bool _playbackReloading;

    /// <summary>Gets a value indicating whether a replay is being watched on the level now up, outside the editor's play-test.</summary>
    private bool Watching => _playback is not null && _playbackLoad == _levelLoads && _editor is not { IsPlaying: true };

    /// <summary>Gets a value indicating whether the play-test's transport drives the level: from the editor, or watching a replay.</summary>
    private bool TransportActive => _editor is { IsPlaying: true } || Watching;

    /// <summary>Gets a value indicating whether a watched replay is drawn from the free camera.</summary>
    private bool ReplayFlies => Watching && _replayCamera == ReplayCamera.Free;

    /// <summary>Gets a value indicating whether a watched replay is held where it stands, paused or at a moment sought.</summary>
    internal bool ReplayPausedForTesting => _playtestPaused;

    /// <summary>Drives a watched replay's transport as its keys do: run, pause, step, seek &lt;seconds&gt;, back, on, speed &lt;x&gt;.</summary>
    /// <param name="command">The command.</param>
    /// <returns>What happened.</returns>
    internal string ReplayTransportForTesting(string command) => PlaytestCommand(command);

    /// <summary>Lets go of every moment the replay's level was held at, so the next seek back loads the level afresh.</summary>
    internal void ForgetReplayCheckpointsForTesting() => _tapeCheckpoints.Clear();

    /// <summary>The viewer's keys and pointer, before the game reads its own: none of the game's own run while a replay is watched.</summary>
    /// <param name="state">The frame loop's state.</param>
    private void HandleReplayViewer(FrameLoopState state)
    {
        if (!Watching || state.Console.IsOpen || _menuFlow.MenuMode || _session.FailedMenu)
        {
            return;
        }

        KeyboardState keys = _window.Input.Keyboard;

        // The arrows go through the run too, but not while they fly the free camera.
        bool arrows = !ReplayFlies;
        string? command = true switch
        {
            _ when keys.WasPressed(KeyCode.Space) || keys.WasPressed(KeyCode.K) => _playtestPaused || ReplayAtEnd ? "run" : "pause",
            _ when keys.WasPressed(KeyCode.Period) => "step",
            _ when keys.WasPressed(KeyCode.J) || (arrows && keys.WasPressed(KeyCode.Left)) => "back",
            _ when keys.WasPressed(KeyCode.L) || (arrows && keys.WasPressed(KeyCode.Right)) => "on",
            _ when keys.WasPressed(KeyCode.Home) => "seek 0",
            _ when keys.WasPressed(KeyCode.Equals) || (arrows && keys.WasPressed(KeyCode.Up)) => SpeedCommand(+1),
            _ when keys.WasPressed(KeyCode.Minus) || (arrows && keys.WasPressed(KeyCode.Down)) => SpeedCommand(-1),
            _ => null,
        };
        if (command is not null)
        {
            Said(PlaytestCommand(command));
        }

        if (keys.WasPressed(KeyCode.C))
        {
            CycleReplayCamera();
        }

        if (keys.WasPressed(KeyCode.H))
        {
            _replayBarHidden = !_replayBarHidden;
        }

        // A click on the track goes to that moment of the run.
        if (!_replayBarHidden && _window.Input.Mouse.WasPressed(MouseButton.Left) && _remasteredHud is { } hud)
        {
            (float x, float y, float width, float height) = hud.ReplayTrack;
            float px = _window.Input.Mouse.X;
            float py = _window.Input.Mouse.Y;
            if (width > 0f && px >= x && px <= x + width && py >= y && py <= y + height && _replay is { } tape)
            {
                _window.Input.Mouse.TakePress(MouseButton.Left);
                Said(SeekReplay((int)Math.Round(tape.Length * Math.Clamp((px - x) / width, 0f, 1f))));
            }
        }

        string SpeedCommand(int step) => string.Create(
            CultureInfo.InvariantCulture,
            $"speed {PlaytestSpeeds[Math.Clamp(_playtestSpeed + step, 0, PlaytestSpeeds.Length - 1)]}");
    }

    // The eyes, then behind them, then the free camera, set off from where the picture was.
    private void CycleReplayCamera()
    {
        _replayCamera = _replayCamera switch
        {
            ReplayCamera.Eyes => ReplayCamera.Behind,
            ReplayCamera.Behind => ReplayCamera.Free,
            _ => ReplayCamera.Eyes,
        };

        // Behind the player, and in the free camera, the body is drawn as the over-the-shoulder view draws it; the viewer's own
        // setting is put back on the eyes.
        if (_replayCamera == ReplayCamera.Eyes)
        {
            RestoreReplayThirdPerson();
        }
        else if (_replayThirdPersonBefore is null)
        {
            _replayThirdPersonBefore = GameplayCVars.ThirdPerson.Value;
            GameplayCVars.ThirdPerson.Value = true;
        }

        if (_replayCamera == ReplayCamera.Free && _camera is not null && TitlePlayer is { } player)
        {
            Camera3D eye = TitleCamera(player);
            Vector3f forward = eye.Orientation.Row3;
            _camera.MoveTo(eye.Position, MathF.Atan2(forward.X, forward.Y), MathF.Asin(Math.Clamp(forward.Z, -1f, 1f)));
        }
    }

    // The viewer's own over-the-shoulder setting back as it was before a camera took it.
    private void RestoreReplayThirdPerson()
    {
        if (_replayThirdPersonBefore is { } before)
        {
            GameplayCVars.ThirdPerson.Value = before;
            _replayThirdPersonBefore = null;
        }
    }

    // The viewer as a replay's level first comes up: playing, at its own speed, through the player's eyes.
    private void ResetReplayViewer()
    {
        _playtestPaused = false;
        _playtestSteps = 0;
        _playtestSpeed = PlaytestNormalSpeed;
        _replayCamera = ReplayCamera.Eyes;
        _replayBarHidden = false;
        _replayDecided = null;
        _playbackDecidedAt = null;
        _playbackScore = null;
        _playbackRank = -1;
        RestoreReplayThirdPerson();
    }

    /// <summary>Steers the free camera of a watched replay, and frees the pointer for the bar: the player's look is the recording's.</summary>
    /// <param name="state">The frame loop's state.</param>
    /// <param name="elapsed">Seconds since the last frame.</param>
    private void SteerReplayView(FrameLoopState state, float elapsed)
    {
        ForgetTitleInput();
        if (ReplayFlies && _camera is { } camera)
        {
            camera.Update(ViewInput, elapsed);
            _window.CursorLocked = !OverlayHasPointer && !state.CaptureMode && camera.IsCapturingMouse;
        }
        else
        {
            _window.CursorLocked = false;
        }

        _window.CursorVisible = !_window.CursorLocked;
    }

    /// <summary>The transport bar, over everything the run's own HUD draws.</summary>
    /// <param name="state">The frame loop's state.</param>
    private void DrawReplayBar(FrameLoopState state)
    {
        if (!Watching || _replayBarHidden || _playback is not { } replay || _replay is not { } tape
            || _remasteredHud is not { IsReady: true } hud || _menuFlow.MenuMode)
        {
            return;
        }

        _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
        state.Batch.Begin();
        hud.DrawReplayBar(state.Batch, ReplayBarOf(replay, tape), SafeWindowWidth, SafeWindowHeight);
        state.Batch.End();
    }

    // What the bar shows of the replay as it stands.
    private ReplayBar ReplayBarOf(ReplayFile replay, InputTape tape)
    {
        ReplayHeader header = replay.Header;
        string mission = _missions.TryGet(header.Mission, out Game.World.MissionDescriptor? descriptor) && descriptor.Name.Length > 0
            ? Shown(descriptor.Name)
            : string.Create(CultureInfo.InvariantCulture, $"Mission {header.Mission}");
        string difficulty = ((Game.Ai.AiDifficulty)Math.Clamp(header.Difficulty, 0, 2)).ToString();
        string who = header.PlayerName.Length > 0 ? $"{header.PlayerName} · " : string.Empty;
        string heading = $"{who}{mission} · {difficulty} · {Ruleset.FromId(header.Ruleset).Name}";
        string? ending = !ReplayAtEnd ? null : (_replayDecided ?? (replay.Result.Outcome == ReplayOutcome.Completed ? true : replay.Result.Outcome == ReplayOutcome.Failed ? false : null)) switch
        {
            true => "Mission complete · Space plays it again",
            false => "Mission failed · Space plays it again",
            null => "The run ends here · Space plays it again",
        };
        return new ReplayBar(
            heading,
            TickOfLevel / 30.0,
            tape.Length / 30.0,
            _playtestPaused || ReplayAtEnd,
            PlaytestSpeeds[_playtestSpeed],
            _replayCamera switch { ReplayCamera.Behind => "Behind", ReplayCamera.Free => "Free camera", _ => "Eyes" },
            [.. tape.Marks.Select(static m => (m.Tick / 30.0, m.Text))],
            Seeking: _replayTarget is not null,
            PartedAt: _playbackDivergedAt / 30.0,
            Ending: ending);
    }
}
